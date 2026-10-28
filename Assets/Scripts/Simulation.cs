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
        public int ColorVersion => world != null ? world.ColorVersion : 0;

        /// simulation world medium
        public ushort Medium => world != null ? world.Medium : (ushort)0;

        /// sizes of every level + coordinate conversions
        public WorldLayout Layout => world?.Layout;
        internal World World => world;
        internal WakeSystem Waker => waker;

        // ---- the parts (created in Awake) ----
        World world;
        WakeSystem waker;
        Stepper stepper;
        SupportSystem support;
        FlowSystem flowSystem;

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
            flowSystem = new FlowSystem(world);

            var forces = new Forces(world);
            var mover = new CellMover(world, waker, flowSystem);
            stepper = new Stepper(world, waker, forces, mover, support, flowSystem);
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
            waker.WakeSlideReach(x, y, 1 + SimulationSettings.PowderMaxExtraStack);
            support.OnCellChanged(x, y, wasAnchored);   // anchored or free? did a neighbour just lose its support?
            world.Version++;
        }

        // ---------------------------------------------------------------
        // PUBLIC API: cell colors
        // Visual only: none of these wake cells or touch physics. Atmosphere cells (air, vacuum) are skipped,
        // and a cell's alpha belongs to its element, so only r / g / b are ever changed.
        // ---------------------------------------------------------------

        /// <summary>Sets the color exactly. Brightness is NOT kept: a pile painted #FF0000 is flat #FF0000.</summary>
        public void PaintCell(int x, int y, Color32 color)
        {
            if (!TryGetColorable(x, y, out int idx)) return;
            SetColor(idx, color.r, color.g, color.b);
        }

        /// <summary>Takes hue and saturation from `dye`, keeps the cell's own brightness (HSV value), so grain survives.</summary>
        public void DyeCell(int x, int y, Color32 dye)
        {
            if (!TryGetColorable(x, y, out int idx)) return;

            Color.RGBToHSV(dye, out float h, out float s, out _);
            Color.RGBToHSV(world.Cells[idx].color, out _, out _, out float v);

            Color32 c = Color.HSVToRGB(h, s, v);
            SetColor(idx, c.r, c.g, c.b);
        }

        /// <summary>Subtracts `amount` from r, g and b, clamped at 0. 255 = black. Negative values lighten.</summary>
        public void DarkenCell(int x, int y, int amount)
        {
            if (!TryGetColorable(x, y, out int idx)) return;

            Color32 c = world.Cells[idx].color;
            SetColor(idx, Clamp255(c.r - amount), Clamp255(c.g - amount), Clamp255(c.b - amount));
        }

        /// <summary>
        /// Back to a normal color for the element (a fresh random shade, the original is gone). A cell that already shows
        /// one of its element's shades is left alone, otherwise holding the clean button would re-roll it every frame.
        /// </summary>
        public void CleanCell(int x, int y)
        {
            if (!TryGetColorable(x, y, out int idx)) return;

            ref Cell c = ref world.Cells[idx];
            if (ElementDatabase.IsNaturalColor(c.element, c.color)) return;

            Color32 fresh = ElementDatabase.RollColor(c.element, world.Rand());
            SetColor(idx, fresh.r, fresh.g, fresh.b);
        }

        bool TryGetColorable(int x, int y, out int idx)
        {
            idx = 0;
            if (!world.InBounds(x, y)) return false;

            idx = world.Index(x, y);
            return world.Behaviors[world.Cells[idx].element] != ElementBehavior.Atmosphere;
        }

        void SetColor(int idx, byte r, byte g, byte b)
        {
            ref Color32 col = ref world.Cells[idx].color;
            if (col.r == r && col.g == g && col.b == b) return; // nothing to redraw

            col.r = r; col.g = g; col.b = b;
            world.ColorVersion++;
        }

        static byte Clamp255(int v) => (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));

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

        // Airfield manipulation

        public Vector2 WindAt(int x, int y) => flowSystem.VelocityAt(x, y);

        public void AddWind(int x, int y, Vector2 v)
        {
            if (!world.InBounds(x, y)) return;
            flowSystem.Impulse(x, y, v);
            waker.Wake(x, y); // resting dust here has to notice the wind
        }
    }
}