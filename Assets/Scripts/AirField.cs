using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Wind as a real (incompressible) fluid, after Jos Stam's "Stable Fluids". Units are cells and steps, so dt = 1.
    ///
    /// The ONLY state is the velocity field. Pressure is not stored state: it is solved fresh every step, as
    /// whatever it takes to stop air from piling up or thinning out anywhere.
    ///
    /// Staggered (MAC) grid: u lives on the vertical faces between cells, v on the horizontal ones. Every cell then
    /// has exactly four face values around it, which makes "flow in minus flow out" (divergence) exact and avoids the
    /// checkerboard artefacts you get from storing one Vector2 per cell.
    ///
    ///                 v[x, y+1]
    ///      u[x, y]    cell (x, y)    u[x+1, y]
    ///                 v[x, y]
    ///
    /// One step:
    ///   1. mask      which cells are walls (Solid / SuperSolid) and which are air
    ///   2. advect    the velocity field carries itself along (semi-Lagrangian: "where did the air that arrives here come from?")
    ///   3. vorticity put back the small swirls that advection smears out (optional)
    ///   4. project   find the pressure that makes the flow divergence-free, and push the air down its gradient
    ///   5. finish    decay, speed cap, and go to sleep once everything is still
    ///
    /// Outside forces (mouse, moving cells) arrive through <see cref="Impulse"/> between steps.
    /// Public API is the same as the old AirField: VelocityAt / Impulse / Step (+ PressureAt / DivergenceAt for debugging).
    ///
    /// Needs these in SimulationSettings (WindDiffusion is no longer used):
    ///   WindDecay, WindCoupling, WindMaxSpeed, WindWakeSpeed, SimulateDrag      (already there)
    ///   WindPressureIterations, WindOverRelaxation, WindVorticity, WindOpenBoundaries, WindSleepSpeed, WindTrackPressure
    /// </summary>
    internal sealed class AirField
    {
        readonly Cell[] cells;
        readonly ElementBehavior[] behaviors;
        readonly int width, height;
        readonly int uStride; // width + 1: u has one more column than there are cells
        readonly int padW;    // width + 2: the fluid mask has a one-cell border, so neighbour lookups need no bounds checks

        // velocity, double-buffered for advection (reads the old field, writes the new one)
        float[] u, u2; // x-velocity on vertical faces,   (width + 1) * height, index = y * uStride + x
        float[] v, v2; // y-velocity on horizontal faces, width * (height + 1), index = y * width + x

        readonly byte[] fs;        // padded mask: 1 = air can flow through, 0 = wall. Border = world edge.
        readonly float[] curl;     // scratch for vorticity confinement
        readonly float[] pressure; // only filled while SimulationSettings.WindTrackPressure is on (debug view)

        bool active; // false = the whole field is exactly zero, Step() costs nothing

        public AirField(World world)
        {
            cells = world.Cells;
            behaviors = world.Behaviors;
            width = world.Width;
            height = world.Height;
            uStride = width + 1;
            padW = width + 2;

            u = new float[uStride * height];
            u2 = new float[u.Length];
            v = new float[width * (height + 1)];
            v2 = new float[v.Length];

            fs = new byte[padW * (height + 2)];
            curl = new float[width * height];
            pressure = new float[width * height];
        }

        // ---------------------------------------------------------------
        // PUBLIC API
        // ---------------------------------------------------------------

        /// <summary>Wind at the centre of a cell: the average of its two u faces and its two v faces. Walls read zero.</summary>
        public Vector2 VelocityAt(int x, int y)
        {
            int iu = y * uStride + x;
            int iv = y * width + x;
            return new Vector2(0.5f * (u[iu] + u[iu + 1]),
                               0.5f * (v[iv] + v[iv + width]));
        }

        /// <summary>Debug: pressure from the last step (only filled when SimulationSettings.WindTrackPressure is on).</summary>
        public float PressureAt(int x, int y) => pressure[y * width + x];

        /// <summary>Debug: net flow out of the cell. After a step this should be close to 0 (more iterations = closer).</summary>
        public float DivergenceAt(int x, int y)
        {
            int iu = y * uStride + x;
            int iv = y * width + x;
            return u[iu + 1] - u[iu] + v[iv + width] - v[iv];
        }

        /// <summary>
        /// Adds v to the velocity of the cell at (x, y). x / y must be in bounds.
        /// The cell's velocity is the average of its faces, so adding v to each face adds exactly v to the cell.
        /// Note: the next step's projection removes the part of an impulse that would compress the air, so a small
        /// blob loses a lot of its push (it turns into a swirl plus flow around it). That is the physics, not a bug.
        /// </summary>
        public void Impulse(int x, int y, Vector2 vel)
        {
            if (IsWall(y * width + x)) return;

            float max = SimulationSettings.WindMaxSpeed;
            int iu = y * uStride + x;
            int iv = y * width + x;

            u[iu] = Mathf.Clamp(u[iu] + vel.x, -max, max);
            u[iu + 1] = Mathf.Clamp(u[iu + 1] + vel.x, -max, max);
            v[iv] = Mathf.Clamp(v[iv] + vel.y, -max, max);
            v[iv + width] = Mathf.Clamp(v[iv + width] + vel.y, -max, max);

            active = true;
        }

        public void Step()
        {
            if (!active) return;

            RebuildMask();
            Advect();
            if (SimulationSettings.WindVorticity > 0f) ConfineVorticity(SimulationSettings.WindVorticity);
            ZeroBlockedFaces();
            Project();
            Finish();
        }

        // ---------------------------------------------------------------
        // 1. MASK
        // ---------------------------------------------------------------

        // v1: only Solid / SuperSolid block air. Sand, water and ash do NOT, so a dust cell can
        // still read the wind at its own position (if they were walls, that read would be zero).
        bool IsWall(int i)
        {
            ElementBehavior b = behaviors[cells[i].element];
            return b == ElementBehavior.Solid || b == ElementBehavior.SuperSolid;
        }

        /// <summary>
        /// Rebuilds the padded fluid mask. The border ring is the world edge:
        /// 0 = sealed box (air bounces back), 1 = open (acts like a pressure-0 reservoir, air can leave and enter).
        /// </summary>
        void RebuildMask()
        {
            byte edge = SimulationSettings.WindOpenBoundaries ? (byte)1 : (byte)0;
            int rows = height + 2;

            for (int x = 0; x < padW; x++)
            {
                fs[x] = edge;
                fs[(rows - 1) * padW + x] = edge;
            }
            for (int y = 0; y < rows; y++)
            {
                fs[y * padW] = edge;
                fs[y * padW + padW - 1] = edge;
            }

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int pad = (y + 1) * padW + 1;
                for (int x = 0; x < width; x++)
                    fs[pad + x] = IsWall(row + x) ? (byte)0 : (byte)1;
            }
        }

        // A face is blocked if either cell next to it is a wall (or the sealed world edge).
        bool BlockedU(int x, int y)
        {
            int c = (y + 1) * padW + x; // padded index of the cell LEFT of the face
            return fs[c] == 0 || fs[c + 1] == 0;
        }

        bool BlockedV(int x, int y)
        {
            int c = y * padW + x + 1;   // padded index of the cell BELOW the face
            return fs[c] == 0 || fs[c + padW] == 0;
        }

        void ZeroBlockedFaces()
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x <= width; x++)
                    if (BlockedU(x, y)) u[y * uStride + x] = 0f;

            for (int y = 0; y <= height; y++)
                for (int x = 0; x < width; x++)
                    if (BlockedV(x, y)) v[y * width + x] = 0f;
        }

        // ---------------------------------------------------------------
        // 2. ADVECTION (semi-Lagrangian, midpoint backtrace)
        // ---------------------------------------------------------------

        /// <summary>
        /// For every face: walk BACKWARDS along the wind to where the air now sitting here came from, and take the
        /// (bilinearly interpolated) velocity found there. Nothing can blow up however fast the wind is, because
        /// we only ever copy values that already exist. The price is some smoothing, see <see cref="ConfineVorticity"/>.
        /// Positions below are in cell units: cell (x, y) covers [x, x+1] x [y, y+1], u[x, y] sits at (x, y + 0.5),
        /// v[x, y] sits at (x + 0.5, y).
        /// </summary>
        void Advect()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x <= width; x++)
                {
                    int i = y * uStride + x;
                    if (BlockedU(x, y)) { u2[i] = 0f; continue; }

                    float px = x, py = y + 0.5f;
                    float vx = u[i];
                    float vy = Bilerp(v, width, height + 1, px - 0.5f, py);

                    PointVelocity(px - 0.5f * vx, py - 0.5f * vy, out float mx, out float my); // wind at the half-way point
                    u2[i] = Bilerp(u, uStride, height, px - mx, py - my - 0.5f);
                }
            }

            for (int y = 0; y <= height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (BlockedV(x, y)) { v2[i] = 0f; continue; }

                    float px = x + 0.5f, py = y;
                    float vx = Bilerp(u, uStride, height, px, py - 0.5f);
                    float vy = v[i];

                    PointVelocity(px - 0.5f * vx, py - 0.5f * vy, out float mx, out float my);
                    v2[i] = Bilerp(v, width, height + 1, px - mx - 0.5f, py - my);
                }
            }

            (u, u2) = (u2, u);
            (v, v2) = (v2, v);
        }

        /// <summary>Velocity at any point (cell units). Walls hold zero faces, so air dragged from a wall slows down.</summary>
        void PointVelocity(float px, float py, out float vx, out float vy)
        {
            vx = Bilerp(u, uStride, height, px, py - 0.5f);
            vy = Bilerp(v, width, height + 1, px - 0.5f, py);
        }

        /// <summary>Bilinear sample. gx / gy are in the field's own index space; out-of-range positions clamp to the edge.</summary>
        static float Bilerp(float[] f, int fw, int fh, float gx, float gy)
        {
            gx = gx < 0f ? 0f : (gx > fw - 1 ? fw - 1 : gx);
            gy = gy < 0f ? 0f : (gy > fh - 1 ? fh - 1 : gy);

            int x0 = (int)gx; if (x0 > fw - 2) x0 = fw - 2;
            int y0 = (int)gy; if (y0 > fh - 2) y0 = fh - 2;
            float tx = gx - x0, ty = gy - y0;

            int i = y0 * fw + x0;
            float a = f[i] + (f[i + 1] - f[i]) * tx;
            float b = f[i + fw] + (f[i + fw + 1] - f[i + fw]) * tx;
            return a + (b - a) * ty;
        }

        // ---------------------------------------------------------------
        // 3. VORTICITY CONFINEMENT (Fedkiw et al.)
        // ---------------------------------------------------------------

        /// <summary>
        /// Swirls already EXIST in this solver (advection + pressure create them, e.g. behind a stone). What
        /// semi-Lagrangian advection does is smooth small ones away. This adds a small force that pushes air around
        /// the centre of each vortex in the direction it already spins, which keeps them alive longer.
        ///
        ///   w   = dv/dx - du/dy                     (curl, positive = counter-clockwise)
        ///   N   = grad|w| / |grad|w||               (points toward the vortex core)
        ///   f   = strength * (N.y * w, -N.x * w)    (N x w, in 2D)
        /// </summary>
        void ConfineVorticity(float strength)
        {
            for (int y = 0; y < height; y++)
            {
                int yd = y > 0 ? y - 1 : y, yu = y < height - 1 ? y + 1 : y;
                for (int x = 0; x < width; x++)
                {
                    int c = y * width + x;
                    if (fs[(y + 1) * padW + x + 1] == 0) { curl[c] = 0f; continue; }

                    int xl = x > 0 ? x - 1 : x, xr = x < width - 1 ? x + 1 : x;
                    float dvdx = (CenterV(xr, y) - CenterV(xl, y)) / (xr - xl);
                    float dudy = (CenterU(x, yu) - CenterU(x, yd)) / (yu - yd);
                    curl[c] = dvdx - dudy;
                }
            }

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int c = y * width + x;
                    if (fs[(y + 1) * padW + x + 1] == 0) continue;

                    float gx = (Mathf.Abs(curl[c + 1]) - Mathf.Abs(curl[c - 1])) * 0.5f;
                    float gy = (Mathf.Abs(curl[c + width]) - Mathf.Abs(curl[c - width])) * 0.5f;
                    float len = Mathf.Sqrt(gx * gx + gy * gy);
                    if (len < 1e-5f) continue;

                    float k = strength * curl[c] / len;
                    float fx = gy * k;   // N.y * w * strength
                    float fy = -gx * k;  // -N.x * w * strength

                    // spread to the four faces; every face is shared by two cells, so each contributes half
                    int iu = y * uStride + x;
                    int iv = y * width + x;
                    u[iu] += 0.5f * fx;
                    u[iu + 1] += 0.5f * fx;
                    v[iv] += 0.5f * fy;
                    v[iv + width] += 0.5f * fy;
                }
            }
        }

        float CenterU(int x, int y) => 0.5f * (u[y * uStride + x] + u[y * uStride + x + 1]);
        float CenterV(int x, int y) => 0.5f * (v[y * width + x] + v[(y + 1) * width + x]);

        // ---------------------------------------------------------------
        // 4. PRESSURE PROJECTION
        // ---------------------------------------------------------------

        /// <summary>
        /// Makes the flow divergence-free: for every air cell, what flows in must equal what flows out.
        /// Cells that are compressed (net inflow) get positive pressure and push air out through their faces,
        /// thin cells (net outflow) pull air in. Walls never move, so they just reflect the push back.
        /// This is how a gust gets forced AROUND an obstacle and why pressure "levels out".
        ///
        /// Solved with Gauss-Seidel + successive over-relaxation, in red/black order:
        ///   - a cell only shares faces with cells of the OTHER colour, so each colour can be done in any order
        ///     (no left-to-right bias, and each half-sweep is trivially parallel: Burst job / compute shader later)
        ///   - for each cell:  p = -divergence / (number of open neighbours) * overRelaxation
        ///     then push p onto its four faces. A wall neighbour counts 0, so its face is left alone.
        ///
        /// Few iterations = squishy air (leftover divergence), many = stiff but slow. Long-range pressure converges
        /// slowly, which is why big worlds eventually want a coarser air grid or a multigrid solver.
        /// </summary>
        void Project()
        {
            int iterations = SimulationSettings.WindPressureIterations;
            float omega = SimulationSettings.WindOverRelaxation;
            bool track = SimulationSettings.WindTrackPressure;

            if (track) System.Array.Clear(pressure, 0, pressure.Length);

            for (int it = 0; it < iterations; it++)
            {
                for (int color = 0; color < 2; color++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = (y + color) & 1; x < width; x += 2)
                        {
                            int c = (y + 1) * padW + x + 1;
                            if (fs[c] == 0) continue;

                            int sl = fs[c - 1], sr = fs[c + 1], sd = fs[c - padW], su = fs[c + padW];
                            int open = sl + sr + sd + su;
                            if (open == 0) continue; // sealed in on all sides

                            int iu = y * uStride + x;  // left face, right face = iu + 1
                            int iv = y * width + x;    // bottom face, top face = iv + width

                            float div = u[iu + 1] - u[iu] + v[iv + width] - v[iv];
                            float p = -div / open * omega;

                            u[iu] -= sl * p;
                            u[iu + 1] += sr * p;
                            v[iv] -= sd * p;
                            v[iv + width] += su * p;

                            if (track) pressure[y * width + x] += p;
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // 5. FINISH
        // ---------------------------------------------------------------

        /// <summary>Decay (air friction), speed cap, snap tiny values to exact zero, and sleep when everything is zero.</summary>
        void Finish()
        {
            float keep = SimulationSettings.WindDecay;
            float cap = SimulationSettings.WindMaxSpeed;
            float floor = SimulationSettings.WindSleepSpeed;
            float fastest = 0f;

            for (int i = 0; i < u.Length; i++)
            {
                float a = Settle(u[i] * keep, cap, floor);
                u[i] = a;
                float m = a < 0f ? -a : a;
                if (m > fastest) fastest = m;
            }
            for (int i = 0; i < v.Length; i++)
            {
                float a = Settle(v[i] * keep, cap, floor);
                v[i] = a;
                float m = a < 0f ? -a : a;
                if (m > fastest) fastest = m;
            }

            if (fastest == 0f)
            {
                active = false; // dead calm: skip every step until the next Impulse
                System.Array.Clear(pressure, 0, pressure.Length);
            }
        }

        // exact zero (not just small) avoids denormal slowdowns and is what lets the whole field sleep
        static float Settle(float a, float cap, float floor)
        {
            if (a > cap) a = cap;
            else if (a < -cap) a = -cap;
            return (a < floor && a > -floor) ? 0f : a;
        }
    }
}