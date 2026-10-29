namespace SandBXDSIM
{
    /// <summary>
    /// Everything about who is awake and who is asleep. Sleeping cells / chunks / areas / zones are skipped by the
    /// <see cref="Stepper"/>, which is what keeps a mostly-settled world cheap.
    ///
    ///   - A cell is woken when it moves, when a neighbour moves away, and when SetCell paints / erases next to it.
    ///   - Waking a cell also wakes its chunk, area and zone ("marking" them awake).
    ///   - Chunks fall asleep in <see cref="Stepper"/> when none of their cells stayed awake; areas and zones fall
    ///     asleep in <see cref="RefreshArea"/> / <see cref="RefreshZone"/> once none of their children are awake.
    ///   - Optional (SimulationSettings.WakeAdjacentChunks): an awake cell on a chunk edge / corner also wakes the
    ///     chunk(s) it touches.
    ///
    /// Deciding that a cell has nothing left to try is NOT done here, each behavior owns that (CellBehavior.IsSettled).
    /// </summary>
    internal sealed class WakeSystem
    {
        readonly World world;
        readonly WorldLayout layout;
        readonly Cell[] cells;
        readonly Chunk[] chunks;
        readonly Area[] areas;
        readonly Zone[] zones;
        readonly ElementBehavior[] behaviors;

        public WakeSystem(World world)
        {
            this.world = world;
            layout = world.Layout;
            cells = world.Cells;
            chunks = world.Chunks;
            areas = world.Areas;
            zones = world.Zones;
            behaviors = world.Behaviors;
        }

        // ---------------------------------------------------------------
        // WAKING
        // ---------------------------------------------------------------

        /// <summary>Wakes the cell at (x, y) (if it is the kind of cell that moves) and its chunk, area and zone.</summary>
        public void Wake(int x, int y)
        {
            if (!world.InBounds(x, y)) return;

            ref Cell c = ref cells[world.Index(x, y)];
            if (!c.awake)
            {
                if (c.anchored || c.blockId != 0 || !CellBehavior.IsDynamic(behaviors[c.element])) return; // air / solids never need stepping
                c.awake = true;
            }
            MarkAwake(x, y);
        }

        /// <summary>Wakes the cells beside and above (x, y), up to maxDepth high: their slide check looks down into (x, y).</summary>
        public void WakeSlideReach(int x, int y, int maxDepth)
        {
            int up = SimulationSettings.SimulationGravity.y <= 0f ? 1 : -1;
            for (int k = 2; k <= maxDepth; k++) // k = 1 is already covered by WakeAround
            {
                Wake(x - 1, y + up * k);
                Wake(x + 1, y + up * k);
            }
        }

        /// <summary>Wakes the cell at (x, y) and its 8 neighbours. Neighbours in other chunks / areas / zones wake those too.</summary>
        public void WakeAround(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    Wake(x + dx, y + dy);
        }

        /// <summary>Marks the chunk holding cell (x, y) awake, plus the chunks it touches if SimulationSettings.WakeAdjacentChunks is on.</summary>
        public void MarkAwake(int x, int y)
        {
            MarkChunkAwake(layout.ChunkIndexOfCell(x, y));
            if (SimulationSettings.WakeAdjacentChunks) MarkTouchingChunks(x, y);
        }

        /// <summary>Keeps a chunk awake for the next step from inside its own step loop (plus its touching chunks if WakeAdjacentChunks is on).</summary>
        public void KeepChunkAwake(ref Chunk chunk, int x, int y)
        {
            chunk.awake = 1;
            if (SimulationSettings.WakeAdjacentChunks) MarkTouchingChunks(x, y);
        }

        // ---------------------------------------------------------------
        // SLEEPING (parents fall asleep on their own once their children are)
        // ---------------------------------------------------------------

        /// <summary>The area stays awake only if one of its chunks is (or got woken meanwhile).</summary>
        public void RefreshArea(ref Area area)
        {
            for (int cy = area.y0; cy < area.y1; cy++)
            {
                for (int cx = area.x0; cx < area.x1; cx++)
                {
                    if (chunks[layout.ChunkIndex(cx, cy)].awake != 0)
                    {
                        area.awake = 1;
                        return;
                    }
                }
            }
            area.awake = 0;
        }

        /// <summary>The zone stays awake only if one of its areas is (or got woken meanwhile).</summary>
        public void RefreshZone(ref Zone zone)
        {
            for (int ay = zone.y0; ay < zone.y1; ay++)
            {
                for (int ax = zone.x0; ax < zone.x1; ax++)
                {
                    if (areas[layout.AreaIndex(ax, ay)].awake != 0)
                    {
                        zone.awake = 1;
                        return;
                    }
                }
            }
            zone.awake = 0;
        }

        // ---------------------------------------------------------------
        // INTERNALS
        // ---------------------------------------------------------------
        void MarkChunkAwake(int chunkIndex)
        {
            ref Chunk chunk = ref chunks[chunkIndex];
            chunk.awake = 1;

            ref Area area = ref areas[chunk.area];
            area.awake = 1;

            zones[area.zone].awake = 1;
        }

        /// <summary>
        /// Cell on a chunk edge -> also wake the chunk across that edge. Cell in a chunk corner -> the 3 chunks around
        /// that corner (two sides + the diagonal). A touching chunk in another area / zone wakes that area / zone too,
        /// because MarkChunkAwake marks the parents. Neighbours outside the world are ignored.
        /// </summary>
        void MarkTouchingChunks(int x, int y)
        {
            ref Chunk c = ref chunks[layout.ChunkIndexOfCell(x, y)];
            bool left = x == c.x0, right = x == c.x1 - 1, bottom = y == c.y0, top = y == c.y1 - 1;
            if (!(left || right || bottom || top)) return; // interior cell: the common case

            if (left) MarkChunkAt(x - 1, y);
            if (right) MarkChunkAt(x + 1, y);
            if (bottom) MarkChunkAt(x, y - 1);
            if (top) MarkChunkAt(x, y + 1);

            if (left && bottom) MarkChunkAt(x - 1, y - 1);
            if (left && top) MarkChunkAt(x - 1, y + 1);
            if (right && bottom) MarkChunkAt(x + 1, y - 1);
            if (right && top) MarkChunkAt(x + 1, y + 1);
        }

        void MarkChunkAt(int x, int y)
        {
            if (world.InBounds(x, y)) MarkChunkAwake(layout.ChunkIndexOfCell(x, y));
        }
    }
}