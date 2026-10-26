using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Runs one simulation step: walks zones -> areas -> chunks -> cells, skips everything that is asleep and hands
    /// every awake cell to the <see cref="CellBehavior"/> for its element. It contains no element rules itself.
    ///
    /// Scan order: zone rows, then area rows, then chunk rows, then cell rows, all from y = 0 upwards.
    /// Any single column of cells is therefore still visited bottom to top, so falling cells are never
    /// processed twice. X direction alternates every step at every level to avoid drift bias.
    /// </summary>
    internal sealed class Stepper
    {
        readonly World world;
        readonly WakeSystem waker;
        readonly WorldLayout layout;
        readonly FlowSystem flowSystem;
        readonly Cell[] cells;
        readonly ElementBehavior[] behaviors;
        readonly int width;

        // one handler per dynamic ElementBehavior
        readonly PowderBehavior powder;
        readonly LiquidBehavior liquid;
        readonly SolidBehavior solid;
        readonly AshBehavior ash;

        bool leftToRight;

        public Stepper(World world, WakeSystem waker, Forces forces, CellMover mover, SupportSystem support, FlowSystem flowSystem)
        {
            this.world = world;
            this.waker = waker;
            this.flowSystem = flowSystem;
            layout = world.Layout;
            cells = world.Cells;
            behaviors = world.Behaviors;
            width = world.Width;

            powder = new PowderBehavior(world, forces, mover, flowSystem);
            liquid = new LiquidBehavior(world, forces, mover);
            solid = new SolidBehavior(world, forces, mover, support, waker);
            ash = new AshBehavior(world, forces, mover, flowSystem);
        }

        /// <summary>Maps an ElementBehavior to the class that implements it. Keep in sync with CellBehavior.IsDynamic.</summary>
        CellBehavior HandlerFor(ElementBehavior behavior)
        {
            switch (behavior)
            {
                case ElementBehavior.Powder: return powder;
                case ElementBehavior.Ash: return ash;
                case ElementBehavior.Liquid: return liquid;
                case ElementBehavior.Solid: return solid;
                default:
                    throw new System.InvalidOperationException(
                        $"No CellBehavior registered for '{behavior}'. Add one in Stepper.HandlerFor (and CellBehavior.IsDynamic).");
            }
        }

        // ---------------------------------------------------------------
        // ONE STEP
        // ---------------------------------------------------------------
        public void Step()
        {
            flowSystem.Step();

            if (++world.StepId == 0) world.StepId = 1; // 0 is what fresh cells carry, so it can't be a valid "already handled" mark
            leftToRight = !leftToRight;

            for (int zy = 0; zy < layout.ZonesY; zy++)
            {
                for (int i = 0; i < layout.ZonesX; i++)
                {
                    int zx = leftToRight ? i : layout.ZonesX - 1 - i;
                    ref Zone zone = ref world.Zones[layout.ZoneIndex(zx, zy)];
                    if (zone.awake == 0) continue;

                    StepZone(ref zone);
                }
            }

            if (world.Changed)
            {
                world.Changed = false;
                world.Version++;
            }
        }

        void StepZone(ref Zone zone)
        {
            int span = zone.x1 - zone.x0;
            for (int ay = zone.y0; ay < zone.y1; ay++)
            {
                for (int i = 0; i < span; i++)
                {
                    int ax = leftToRight ? zone.x0 + i : zone.x1 - 1 - i;
                    ref Area area = ref world.Areas[layout.AreaIndex(ax, ay)];
                    if (area.awake == 0) continue;

                    StepArea(ref area);
                }
            }

            waker.RefreshZone(ref zone); // sleeps again unless one of its areas is still awake (or got woken meanwhile)
        }

        void StepArea(ref Area area)
        {
            int span = area.x1 - area.x0;
            for (int cy = area.y0; cy < area.y1; cy++)
            {
                for (int i = 0; i < span; i++)
                {
                    int cx = leftToRight ? area.x0 + i : area.x1 - 1 - i;
                    ref Chunk chunk = ref world.Chunks[layout.ChunkIndex(cx, cy)];
                    if (chunk.awake == 0) continue;

                    StepChunk(ref chunk);
                }
            }

            waker.RefreshArea(ref area);
        }

        void StepChunk(ref Chunk chunk)
        {
            // Assume it falls asleep. It is set back to 1 by: a cell that is still awake after its step,
            // a cell that was swapped here this step, or any wake (own cells moving, or a neighbouring chunk's cells).
            chunk.awake = 0;

            int span = chunk.x1 - chunk.x0;
            for (int y = chunk.y0; y < chunk.y1; y++)
            {
                int row = y * width;
                for (int i = 0; i < span; i++)
                {
                    int x = leftToRight ? chunk.x0 + i : chunk.x1 - 1 - i;
                    int idx = row + x;

                    if (!cells[idx].awake) continue; // sleeping cell

                    ElementBehavior behavior = behaviors[cells[idx].element];
                    if (!CellBehavior.IsDynamic(behavior))
                    {
                        cells[idx].awake = false; // medium / solid ended up awake somehow: nothing to do for it
                        continue;
                    }

                    // swapped into this spot earlier this step: it was already moved, handle it next step
                    if (cells[idx].stamp == world.StepId)
                    {
                        waker.KeepChunkAwake(ref chunk, x, y);
                        continue;
                    }

                    CellBehavior handler = HandlerFor(behavior);

                    // nothing it could try would succeed: sleep until a neighbour changes
                    if (handler.IsSettled(x, y, idx))
                    {
                        cells[idx].awake = false;
                        cells[idx].velocity = Vector2.zero;
                        cells[idx].freeFalling = false;
                        continue;
                    }

                    cells[idx].stamp = world.StepId;
                    handler.Step(x, y, idx);

                    // It had a possible move, so it stays awake whether or not this step's attempt worked
                    // (powders roll their friction, liquids pick a random side). If it did move, CellMover
                    // already marked the chunk it landed in.
                    waker.KeepChunkAwake(ref chunk, x, y);
                }
            }
        }
    }
}