using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// The simulation's DATA, plus the smallest helpers around it. No rules live here: nothing in this
    /// class decides how a cell behaves, it only holds the grid and answers simple questions about it.
    ///
    ///   Layout / Zones / Areas / Chunks   the hierarchy (sizes and conversions are in <see cref="WorldLayout"/>)
    ///   Cells                             ONE flat array, row-major: index = y * Width + x
    ///   Behaviors                         ElementBehavior per element id (a flat lookup is cheaper than touching the fat Element struct)
    ///   Medium                            element id that fills empty space (air, vacuum, ...)
    ///   StepId / Changed / Version        bookkeeping written by <see cref="Stepper"/>, <see cref="CellMover"/> and Simulation
    ///
    /// Everything else (waking, forces, movement, per-element rules) lives in its own class and receives this object.
    /// </summary>
    internal sealed class World
    {
        public readonly WorldLayout Layout;
        public readonly Zone[] Zones;
        public readonly Area[] Areas;
        public readonly Chunk[] Chunks;
        public readonly Cell[] Cells;
        public readonly int Width, Height;

        public readonly ElementBehavior[] Behaviors;
        public readonly ushort Medium;

        /// increments whenever the grid changes; the renderer redraws when it differs from what it last drew
        public int Version;

        /// counts steps (wraps, never 0). Cell.stamp == StepId means "already handled this step"
        public byte StepId;

        /// set when a cell moved during the current step -> Version is bumped at the end of the step
        public bool Changed;

        uint rng = 2463534242;

        // ---------------------------------------------------------------
        // SETUP
        // ---------------------------------------------------------------
        /// <param name="logContext">object that warnings / errors are attached to in the Unity console</param>
        public World(WorldSettings settings, UnityEngine.Object logContext)
        {
            Behaviors = BuildBehaviorTable();
            Medium = ResolveMedium(settings.defaultMedium, logContext);

            Layout = new WorldLayout(settings);
            Width = Layout.CellsX;
            Height = Layout.CellsY;

            Zones = new Zone[Layout.ZonesX * Layout.ZonesY];
            Areas = new Area[Layout.AreasX * Layout.AreasY];
            Chunks = new Chunk[Layout.ChunksX * Layout.ChunksY];
            Cells = new Cell[Width * Height];

            FillHierarchy();
            for (int i = 0; i < Cells.Length; i++) Cells[i] = MakeCell(Medium);
        }

        static ElementBehavior[] BuildBehaviorTable()
        {
            int count = ElementDatabase.Count;
            var table = new ElementBehavior[count];

            for (ushort id = 0; id < count; id++)
            {
                ref readonly Element def = ref ElementDatabase.Get(id);
                // anything of type Atmosphere is a medium: never simulated
                table[id] = def.type == ElementType.Atmosphere ? ElementBehavior.Atmosphere : def.behavior;
            }
            return table;
        }

        ushort ResolveMedium(string wanted, UnityEngine.Object logContext)
        {
            if (ElementDatabase.TryFind(wanted, out ushort id) && Behaviors[id] == ElementBehavior.Atmosphere)
                return id;

            // not found / not an Atmosphere type: fall back to the first Atmosphere element
            for (ushort i = 0; i < Behaviors.Length; i++)
            {
                if (Behaviors[i] != ElementBehavior.Atmosphere) continue;

                Debug.LogWarning($"Simulation: '{wanted}' is not an Atmosphere-type element, using '{ElementDatabase.Get(i).name}'.", logContext);
                return i;
            }

            Debug.LogError("Simulation: no Atmosphere-type element is registered, so there is no medium.", logContext);
            return 0;
        }

        /// <summary>Generates zones -> areas -> chunks top-down. Everything starts asleep.</summary>
        void FillHierarchy()
        {
            for (int zy = 0; zy < Layout.ZonesY; zy++)
            {
                for (int zx = 0; zx < Layout.ZonesX; zx++)
                {
                    RectInt zoneAreas = Layout.AreasOfZone(zx, zy);
                    int zi = Layout.ZoneIndex(zx, zy);
                    Zones[zi] = new Zone
                    {
                        x = zx,
                        y = zy,
                        x0 = zoneAreas.xMin,
                        y0 = zoneAreas.yMin,
                        x1 = zoneAreas.xMax,
                        y1 = zoneAreas.yMax
                    };

                    for (int ay = zoneAreas.yMin; ay < zoneAreas.yMax; ay++)
                    {
                        for (int ax = zoneAreas.xMin; ax < zoneAreas.xMax; ax++)
                        {
                            RectInt areaChunks = Layout.ChunksOfArea(ax, ay);
                            int ai = Layout.AreaIndex(ax, ay);
                            Areas[ai] = new Area
                            {
                                x = ax,
                                y = ay,
                                x0 = areaChunks.xMin,
                                y0 = areaChunks.yMin,
                                x1 = areaChunks.xMax,
                                y1 = areaChunks.yMax,
                                zone = zi
                            };

                            for (int cy = areaChunks.yMin; cy < areaChunks.yMax; cy++)
                            {
                                for (int cx = areaChunks.xMin; cx < areaChunks.xMax; cx++)
                                {
                                    RectInt chunkCells = Layout.CellsOfChunk(cx, cy);
                                    Chunks[Layout.ChunkIndex(cx, cy)] = new Chunk
                                    {
                                        x0 = chunkCells.xMin,
                                        y0 = chunkCells.yMin,
                                        x1 = chunkCells.xMax,
                                        y1 = chunkCells.yMax,
                                        area = ai
                                    };
                                }
                            }
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // GRID QUERIES
        // ---------------------------------------------------------------
        public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        public int Index(int x, int y) => y * Width + x;

        /// <summary>A cell may enter a spot if whatever is there is less dense than the mover. This is what makes heavy things sink.</summary>
        public bool CanEnter(int from, int tx, int ty)
        {
            if (!InBounds(tx, ty)) return false;
            return ElementDatabase.Get(Cells[ty * Width + tx].element).density
                 < ElementDatabase.Get(Cells[from].element).density;
        }

        /// <summary>Swaps two cells and stamps both as handled this step. The velocity travels with the cell.</summary>
        public void Swap(int a, int b)
        {
            Cell t = Cells[a];
            Cells[a] = Cells[b];
            Cells[b] = t;
            Cells[a].stamp = Cells[b].stamp = StepId;
        }

        public Cell MakeCell(ushort element) =>
            new Cell { element = element, shade = (byte)(Rand() % (uint)ElementDatabase.Get(element).shadeCount) };

        // ---------------------------------------------------------------
        // RANDOM
        // ---------------------------------------------------------------
        // xorshift32 - cheaper than UnityEngine.Random in a hot loop
        public uint Rand()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return rng;
        }

        public bool RandBool() => (Rand() & 0x10000) != 0;

        /// <summary>-1 or +1, picked at random.</summary>
        public int RandomDir() => RandBool() ? -1 : 1;
    }
}