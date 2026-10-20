using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Pure grid simulation. Knows nothing about textures, sprites, cameras or input.
    /// Other scripts read <see cref="Cells"/> / <see cref="Version"/> and call <see cref="SetCell"/>.
    ///
    /// This class only does three things: builds the parts below, runs them at a fixed rate (Update), and exposes
    /// the public API. The actual work lives in one file per concern:
    ///
    ///   World.cs            the data: grid, zones / areas / chunks, element lookup, random numbers. No rules.
    ///   WakeSystem.cs       who is awake / asleep (waking cells, chunks, areas, zones)
    ///   Stepper.cs          one step: walks zone -> area -> chunk -> cell, skips sleepers, hands cells to their behavior
    ///   CellBehavior.cs     base class for per-element rules
    ///     PowderBehavior.cs   sand, dirt...
    ///     LiquidBehavior.cs   water...
    ///   Forces.cs           everything that only edits velocity: gravity, friction, impact transfer, speed cap
    ///   CellMover.cs        the only place where cells change position (and wake their neighbours when they do)
    ///
    /// WHAT HAPPENS IN ONE STEP
    ///   Update -> Stepper.Step -> (zone -> area -> chunk -> cell, sleepers skipped)
    ///     -> behavior.IsSettled  ? put the cell to sleep
    ///     -> behavior.Step       : 1. Forces (gravity...)  2. CellMover (velocity -> movement)  3. Forces (friction)
    ///                              CellMover wakes the neighbourhood of every move via WakeSystem
    ///
    /// WORLD LAYOUT
    ///   zone -> area -> chunk -> cell, sizes from WorldSettings, conversions in <see cref="WorldLayout"/>.
    ///   Cells live in ONE flat array (the renderer and input rely on that).
    /// </summary>
    public class Simulation : MonoBehaviour
    {
        public static Simulation Instance { get; private set; }

        [Header("Simulation")]
        public WorldSettings worldSettings;
        public bool paused;

        // ---- public API: read ----

        /// raw grid
        public Cell[] Cells => world?.Cells;

        /// increments whenever the grid changes (a fully sleeping world no longer bumps it)
        public int Version => world != null ? world.Version : 0;

        /// simulation world medium
        public ushort Medium => world != null ? world.Medium : (ushort)0;

        /// sizes of every level + coordinate conversions
        public WorldLayout Layout => world?.Layout;

        // ---- the parts (created in Awake) ----
        World world;
        WakeSystem waker;
        Stepper stepper;
        SupportSystem support;

        float accumulator;

        // ---------------------------------------------------------------
        // LIFECYCLE
        // ---------------------------------------------------------------
        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;

            // NOTE: worldSettings.ApplyPhysics() is not called anywhere, so gravity / factor / max velocity on the
            // asset are unused and SimulationSettings keeps its own defaults. Call it here if the asset should win.

            world = new World(worldSettings, this);
            waker = new WakeSystem(world);
            support = new SupportSystem(world, waker);

            var forces = new Forces(world);
            var mover = new CellMover(world, waker);
            stepper = new Stepper(world, waker, forces, mover, support);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (paused) return;

            float dt = 1f / worldSettings.stepsPerSecond;
            accumulator += Time.deltaTime;

            int steps = 0;
            while (accumulator >= dt && steps < 4) // cap to avoid spiral of death
            {
                stepper.Step();
                accumulator -= dt;
                steps++;
            }
            if (steps == 4) accumulator = 0f;
        }

        // ---------------------------------------------------------------
        // PUBLIC API: read / write cells
        // ---------------------------------------------------------------
        public bool InBounds(int x, int y) => world.InBounds(x, y);

        public Cell GetCell(int x, int y) => world.Cells[world.Index(x, y)];

        public void SetCell(int x, int y, ushort element)
        {
            if (!world.InBounds(x, y)) return;

            int idx = world.Index(x, y);
            bool wasAnchored = world.Cells[idx].anchored;

            world.Cells[idx] = world.MakeCell(element); // velocity starts at zero, asleep
            waker.WakeAround(x, y);                     // the new cell may fall; an erased cell lets its neighbours move
            support.OnCellChanged(x, y, wasAnchored);   // anchored or free? did a neighbour just lose its support?
            world.Version++;
        }

        /// <summary>True if the cell holds an Atmosphere-type element (Air, Vacuum, ...), i.e. it counts as empty space.</summary>
        public bool IsAtmosphere(int x, int y) =>
            world.Behaviors[world.Cells[world.Index(x, y)].element] == ElementBehavior.Atmosphere;

        // ---------------------------------------------------------------
        // PUBLIC API: tooling / debug
        // ---------------------------------------------------------------
        /// <summary>Is anything awake in this zone / area / chunk? Coordinates are at that level (see <see cref="Layout"/>).</summary>
        public bool IsZoneAwake(int zx, int zy) => world.Zones[world.Layout.ZoneIndex(zx, zy)].awake != 0;
        public bool IsAreaAwake(int ax, int ay) => world.Areas[world.Layout.AreaIndex(ax, ay)].awake != 0;
        public bool IsChunkAwake(int cx, int cy) => world.Chunks[world.Layout.ChunkIndex(cx, cy)].awake != 0;
    }
}