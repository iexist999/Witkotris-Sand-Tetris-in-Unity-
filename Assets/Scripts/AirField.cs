using UnityEngine;

namespace SandBXDSIM
{
    /// Wind: one Vector2 per grid position, in cells/step (same units as Cell.velocity).
    /// Lives outside Cell on purpose: World.Swap moves cells, but wind belongs to the place.
    internal sealed class AirField
    {
        readonly Cell[] cells;
        readonly ElementBehavior[] behaviors;
        readonly int width, height;

        Vector2[] cur, next;

        public AirField(World world)
        {
            cells = world.Cells;
            behaviors = world.Behaviors;
            width = world.Width;
            height = world.Height;
            cur = new Vector2[width * height];
            next = new Vector2[width * height];
        }

        public Vector2 VelocityAt(int x, int y) => cur[y * width + x];

        public void Impulse(int x, int y, Vector2 v)
        {
            int i = y * width + x;
            if (IsWall(i)) return;
            cur[i] = Vector2.ClampMagnitude(cur[i] + v, SimulationSettings.WindMaxSpeed);
        }

        // v1: only Solid / SuperSolid block air. Sand, water and ash do NOT, so a dust cell can
        // still read the wind at its own position (if they were walls, that read would be zero).
        bool IsWall(int i)
        {
            ElementBehavior b = behaviors[cells[i].element];
            return b == ElementBehavior.Solid || b == ElementBehavior.SuperSolid;
        }

        public void Step()
        {
            float mix = SimulationSettings.WindDiffusion;
            float keep = SimulationSettings.WindDecay;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (IsWall(i)) { next[i] = Vector2.zero; continue; }

                    Vector2 me = cur[i];
                    Vector2 sum = Neighbor(x - 1, y, me) + Neighbor(x + 1, y, me)
                                + Neighbor(x, y - 1, me) + Neighbor(x, y + 1, me);

                    Vector2 v = Vector2.Lerp(me, sum * 0.25f, mix) * keep;
                    next[i] = v.sqrMagnitude < 1e-6f ? Vector2.zero : v; // exact zero: avoids denormal slowdowns, enables sleeping later
                }
            }
            (cur, next) = (next, cur);
        }

        // A wall or the world edge hands back the cell's own value, so nothing flows through it.
        Vector2 Neighbor(int x, int y, Vector2 fallback)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return fallback;
            int j = y * width + x;
            return IsWall(j) ? fallback : cur[j];
        }
    }
}