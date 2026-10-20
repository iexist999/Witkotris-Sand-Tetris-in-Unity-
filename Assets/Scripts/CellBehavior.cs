namespace SandBXDSIM
{
    internal abstract class CellBehavior
    {
        /// <summary>
        /// world reference
        /// </summary>
        protected readonly World world;
        /// <summary>
        /// cells reference
        /// </summary>
        protected readonly Cell[] cells;
        /// <summary>
        /// forces reference
        /// </summary>
        protected readonly Forces forces;
        /// <summary>
        /// cellmover reference
        /// </summary>
        protected readonly CellMover mover;

        protected CellBehavior(World world, Forces forces, CellMover mover)
        {
            this.world = world;
            this.forces = forces;
            this.mover = mover;
            cells = world.Cells;
        }

        /// <summary>ignores elementbehavior that dont need contant updating each step</summary>
        public static bool IsDynamic(ElementBehavior behavior) =>
            behavior == ElementBehavior.Powder || behavior == ElementBehavior.Liquid || behavior == ElementBehavior.Solid;

        /// <summary>
        /// true if no valid moves are possible. checks geometry only, without random rolls.
        /// </summary>
        public abstract bool IsSettled(int x, int y, int idx);

        public abstract void Step(int x, int y, int idx);

        // ---------------------------------------------------------------
        // SHARED HELPERS
        // ---------------------------------------------------------------

        /// <summary>
        /// returns -1 for up, +1 for down, or 0 for no gravity.
        /// </summary>
        protected int DownOf(int idx)
        {
            float g = SimulationSettings.SimulationGravity.y * ElementDatabase.Get(cells[idx].element).gravityInfluence;
            if (g == 0f) return 0;
            return g < 0f ? -1 : 1;
        }

        /// <summary>can the cell fall/slide?</summary>
        protected bool CanFallOrSlide(int idx, int x, int y, int down) =>
            world.CanEnter(idx, x, y + down) ||
            world.CanEnter(idx, x - 1, y + down) ||
            world.CanEnter(idx, x + 1, y + down);
    }
}