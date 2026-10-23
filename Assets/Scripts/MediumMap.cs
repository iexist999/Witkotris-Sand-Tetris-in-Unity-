using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Says which medium every cell belongs to, so each <see cref="FlowField"/> knows where it may flow.
    ///
    ///   Wall   Solid / SuperSolid. Blocks both fields.
    ///   Water  Liquid cells, plus powders / ash that sit in a liquid.
    ///   Air    Atmosphere / Gas cells, plus powders / ash that sit in air.
    ///
    /// Powders are not liquid, so a grain sinking through a pool would otherwise be a hole in the water field.
    /// Rule: a non-liquid, non-wall cell takes the medium of the first liquid / atmosphere cell ABOVE it (against
    /// gravity). Sand at the bottom of a pool is water, sand on a beach is air. Walls reset the scan.
    ///
    /// Every cell gets exactly one kind, so the fields partition the grid: for the water field everything that is
    /// not Water is a wall, and vice versa. That is what keeps flow inside its own medium.
    ///
    /// Rebuilt lazily: only when World.Version changed since the last build (cells moved or were painted).
    /// </summary>
    internal sealed class MediumMap
    {
        public const byte Wall = 0, Air = 1, Water = 2;

        public readonly byte[] kind;

        /// <summary>Increments on every rebuild, so fields can tell whether their own mask is out of date.</summary>
        public int Stamp;

        readonly World world;
        readonly Cell[] cells;
        readonly ElementBehavior[] behaviors;
        readonly int width, height;
        readonly byte[] inLiquid; // per column while scanning: is the last medium seen above this cell a liquid?
        int builtVersion = -1;

        public MediumMap(World world)
        {
            this.world = world;
            cells = world.Cells;
            behaviors = world.Behaviors;
            width = world.Width;
            height = world.Height;
            kind = new byte[width * height];
            inLiquid = new byte[width];
        }

        public void EnsureFresh()
        {
            if (world.Version != builtVersion) Rebuild();
        }

        void Rebuild()
        {
            // the grid is y-up: normal gravity pulls toward y = 0, so "above" means larger y and we scan from the top row down
            bool gravityDown = SimulationSettings.SimulationGravity.y <= 0f;
            int y = gravityDown ? height - 1 : 0;
            int step = gravityDown ? -1 : 1;

            System.Array.Clear(inLiquid, 0, width);

            for (int n = 0; n < height; n++, y += step)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    ElementBehavior b = behaviors[cells[row + x].element];
                    byte k;

                    if (b == ElementBehavior.Solid || b == ElementBehavior.SuperSolid) { k = Wall; inLiquid[x] = 0; }
                    else if (b == ElementBehavior.Liquid) { k = Water; inLiquid[x] = 1; }
                    else if (b == ElementBehavior.Atmosphere || b == ElementBehavior.Gas) { k = Air; inLiquid[x] = 0; }
                    else k = inLiquid[x] != 0 ? Water : Air; // powder / ash / anything else: lives in the medium above it

                    kind[row + x] = k;
                }
            }

            builtVersion = world.Version;
            Stamp++;
        }
    }
}