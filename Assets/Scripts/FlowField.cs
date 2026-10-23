using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// One incompressible flow solver for ONE medium (see <see cref="AirField"/>'s old doc for the algorithm:
    /// staggered MAC grid, semi-Lagrangian advection, vorticity confinement, red/black Gauss-Seidel pressure projection).
    ///
    /// What makes it medium-agnostic: it owns only the cells whose <see cref="MediumMap.kind"/> equals its own kind.
    /// Every other cell (other medium, walls) is a wall to this field, and the solver already zeroes every face that
    /// touches a wall, so nothing flows across the boundary in either direction and nothing needs special handling
    /// when the water level moves. A cell that changes medium simply has its leftover velocity zeroed by its old field.
    ///
    /// Per-field tuning lives in <see cref="FlowSettings"/> (decay, speed cap, iterations, edges...).
    /// Vorticity confinement strength = Element.vorticity of the medium element * FlowSettings.vorticityScale.
    ///
    /// Grid layout (u on vertical faces, v on horizontal faces):
    ///
    ///                 v[x, y+1]
    ///      u[x, y]    cell (x, y)    u[x+1, y]
    ///                 v[x, y]
    /// </summary>
    internal sealed class FlowField
    {
        readonly MediumMap map;
        readonly byte kind;
        readonly ushort mediumElement; // element whose properties (vorticity) describe this medium
        readonly SimulationSettings.FlowSettings s;

        readonly int width, height;
        readonly int uStride; // width + 1: u has one more column than there are cells
        readonly int padW;    // width + 2: the fluid mask has a one-cell border, so neighbour lookups need no bounds checks

        float[] u, u2; // x-velocity on vertical faces,   (width + 1) * height, index = y * uStride + x
        float[] v, v2; // y-velocity on horizontal faces, width * (height + 1), index = y * width + x

        readonly byte[] fs;        // padded mask: 1 = this field may flow here, 0 = wall (or the other medium)
        readonly float[] curl;     // scratch for vorticity confinement
        readonly float[] pressure; // only filled while FlowSettings.trackPressure is on

        bool active;               // false = the whole field is exactly zero, Step() costs nothing
        int maskStamp = -1;        // MediumMap.Stamp the mask was built from
        bool maskOpen;             // edge setting the mask was built with

        public bool IsActive => active;

        public FlowField(World world, MediumMap map, byte kind, ushort mediumElement, SimulationSettings.FlowSettings settings)
        {
            this.map = map;
            this.kind = kind;
            this.mediumElement = mediumElement;
            s = settings;

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

        /// <summary>Flow at the centre of a cell: the average of its two u faces and its two v faces. Cells outside this field read zero.</summary>
        public Vector2 VelocityAt(int x, int y)
        {
            int iu = y * uStride + x;
            int iv = y * width + x;
            return new Vector2(0.5f * (u[iu] + u[iu + 1]),
                               0.5f * (v[iv] + v[iv + width]));
        }

        public float PressureAt(int x, int y) => pressure[y * width + x];

        public float DivergenceAt(int x, int y)
        {
            int iu = y * uStride + x;
            int iv = y * width + x;
            return u[iu + 1] - u[iu] + v[iv + width] - v[iv];
        }

        /// <summary>
        /// Adds vel to the cell at (x, y) if that cell belongs to this field's medium (otherwise ignored).
        /// x / y must be in bounds, and the caller should have called MediumMap.EnsureFresh().
        /// </summary>
        public void Impulse(int x, int y, Vector2 vel)
        {
            if (map.kind[y * width + x] != kind) return;

            float max = s.maxSpeed;
            int iu = y * uStride + x;
            int iv = y * width + x;

            u[iu] = Mathf.Clamp(u[iu] + vel.x, -max, max);
            u[iu + 1] = Mathf.Clamp(u[iu + 1] + vel.x, -max, max);
            v[iv] = Mathf.Clamp(v[iv] + vel.y, -max, max);
            v[iv + width] = Mathf.Clamp(v[iv + width] + vel.y, -max, max);

            active = true;
        }

        /// <summary>Call after the map is fresh.</summary>
        public void Step()
        {
            if (!active) return;

            RebuildMask();
            Advect();

            float vorticity = ElementDatabase.Get(mediumElement).vorticity * s.vorticityScale;
            if (vorticity > 0f) ConfineVorticity(vorticity);

            ZeroBlockedFaces();
            Project();
            Finish();
        }

        // ---------------------------------------------------------------
        // MASK
        // ---------------------------------------------------------------

        /// <summary>
        /// Padded mask of the cells this field owns. The border ring is the world edge:
        /// 0 = sealed (flow bounces back), 1 = open (acts like a pressure-0 reservoir, flow can leave and enter).
        /// Only rebuilt when the medium map changed.
        /// </summary>
        void RebuildMask()
        {
            bool open = s.openBoundaries;
            if (map.Stamp == maskStamp && open == maskOpen) return;
            maskStamp = map.Stamp;
            maskOpen = open;

            byte edge = open ? (byte)1 : (byte)0;
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

            byte[] k = map.kind;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int pad = (y + 1) * padW + 1;
                for (int x = 0; x < width; x++)
                    fs[pad + x] = k[row + x] == kind ? (byte)1 : (byte)0;
            }
        }

        // A face is blocked if either cell next to it is not ours (wall, other medium, or the sealed world edge).
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
        // ADVECTION (semi-Lagrangian, midpoint backtrace)
        // ---------------------------------------------------------------

        /// <summary>
        /// For every face: walk BACKWARDS along the flow to where the fluid now sitting here came from, and take the
        /// (bilinearly interpolated) velocity found there. Positions are in cell units: cell (x, y) covers
        /// [x, x+1] x [y, y+1], u[x, y] sits at (x, y + 0.5), v[x, y] sits at (x + 0.5, y).
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

                    PointVelocity(px - 0.5f * vx, py - 0.5f * vy, out float mx, out float my);
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

        void PointVelocity(float px, float py, out float vx, out float vy)
        {
            vx = Bilerp(u, uStride, height, px, py - 0.5f);
            vy = Bilerp(v, width, height + 1, px - 0.5f, py);
        }

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
        // VORTICITY CONFINEMENT
        // ---------------------------------------------------------------

        /// <summary>
        /// Advection smooths small swirls away; this adds a force that pushes fluid around each vortex core in the
        /// direction it already spins. Only cells of THIS field get a force (wall cells are skipped, and faces shared
        /// with another medium are zeroed afterwards), so confinement can't leak across the air / water boundary and
        /// each field uses its own strength.
        ///
        ///   w = dv/dx - du/dy,   N = grad|w| / |grad|w||,   f = strength * (N.y * w, -N.x * w)
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
                    float fx = gy * k;
                    float fy = -gx * k;

                    // every face is shared by two cells, so each cell contributes half
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
        // PRESSURE PROJECTION (red/black Gauss-Seidel + over-relaxation)
        // ---------------------------------------------------------------

        /// <summary>
        /// Makes the flow divergence-free: for every cell, what flows in must equal what flows out. Compressed cells get
        /// positive pressure and push fluid out through their faces. Walls (and the other medium) never move, so they
        /// reflect the push back, which is also what keeps the two mediums from exchanging flow.
        /// </summary>
        void Project()
        {
            int iterations = s.iterations;
            float omega = s.overRelaxation;
            bool track = s.trackPressure;

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
        // FINISH
        // ---------------------------------------------------------------

        /// <summary>Decay (friction), speed cap, snap tiny values to exact zero, and sleep when everything is zero.</summary>
        void Finish()
        {
            float keep = s.decay;
            float cap = s.maxSpeed;
            float floor = s.sleepSpeed;
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
                active = false;
                System.Array.Clear(pressure, 0, pressure.Length);
            }
        }

        static float Settle(float a, float cap, float floor)
        {
            if (a > cap) a = cap;
            else if (a < -cap) a = -cap;
            return (a < floor && a > -floor) ? 0f : a;
        }
    }
}