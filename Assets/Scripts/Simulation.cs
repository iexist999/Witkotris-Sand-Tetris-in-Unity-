using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Pure grid simulation. Knows nothing about textures, sprites, cameras or input.
    /// Other scripts read <see cref="Cells"/> / <see cref="Version"/> and call <see cref="SetCell"/>.
    ///
    /// Every movable cell is stepped in two phases:
    ///   1. FORCES   - ApplyGravity (and later buoyancy, wind, pressure...) only ever modify cell.velocity.
    ///   2. MOVEMENT - the velocity is turned into whole cells, one axis at a time (Y, then X).
    ///                 Hitting something triggers an impact response (slide down a slope, convert fall
    ///                 speed into sideways speed, bounce...), which again only edits velocity / position.
    /// </summary>
    public class Simulation : MonoBehaviour
    {
        public static Simulation Instance { get; private set; }

        [Header("Grid")]
        public int width = 30;
        public int height = 100;

        [Header("Simulation")]
        public float stepsPerSecond = 60f;
        public bool paused;

        [Header("Medium")]
        [Tooltip("Element that fills empty space: any element of type Atmosphere (e.g. Air, Vacuum). Never stepped.")]
        public string defaultMedium = "air";

        /// <summary>Raw grid, row-major, y = 0 is the bottom row. Treat as read-only outside this class.</summary>
        public Cell[] Cells => cells;

        /// <summary>Increments whenever the grid changes. Views compare this to know when to redraw.</summary>
        public int Version { get; private set; }

        /// <summary>Element id that fills empty space. Resolved from <see cref="defaultMedium"/> in Awake.</summary>
        public ushort Medium { get; private set; }

        Cell[] cells;
        ElementBehavior[] behaviors; // behavior per element id, built once (a flat lookup is cheaper than touching the fat Element struct per cell)
        byte parity;
        bool leftToRight;
        float accumulator;
        uint rng = 2463534242;

        // ---------------------------------------------------------------
        // SETUP
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

            BuildBehaviorTable();
            ResolveMedium();

            cells = new Cell[width * height];
            for (int i = 0; i < cells.Length; i++) cells[i] = MakeCell(Medium);
        }

        void BuildBehaviorTable()
        {
            int count = ElementDatabase.Count;
            behaviors = new ElementBehavior[count];

            for (ushort id = 0; id < count; id++)
            {
                ref readonly Element def = ref ElementDatabase.Get(id);
                // anything of type Atmosphere is a medium: never simulated
                behaviors[id] = def.type == ElementType.Atmosphere ? ElementBehavior.Atmosphere : def.behavior;
            }
        }

        void ResolveMedium()
        {
            if (ElementDatabase.TryFind(defaultMedium, out ushort id) && behaviors[id] == ElementBehavior.Atmosphere)
            {
                Medium = id;
                return;
            }

            // not found / not an Atmosphere type: fall back to the first Atmosphere element
            for (ushort i = 0; i < behaviors.Length; i++)
            {
                if (behaviors[i] != ElementBehavior.Atmosphere) continue;

                Debug.LogWarning($"Simulation: '{defaultMedium}' is not an Atmosphere-type element, using '{ElementDatabase.Get(i).name}'.", this);
                Medium = i;
                return;
            }

            Debug.LogError("Simulation: no Atmosphere-type element is registered, so there is no medium.", this);
            Medium = 0;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------
        // PUBLIC API
        // ---------------------------------------------------------------
        public bool InBounds(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

        public Cell GetCell(int x, int y) => cells[y * width + x];

        public void SetCell(int x, int y, ushort element)
        {
            if (!InBounds(x, y)) return;

            cells[y * width + x] = MakeCell(element); // velocity starts at zero
            Version++;
        }

        /// <summary>True if the cell holds an Atmosphere-type element (Air, Vacuum, ...), i.e. it counts as empty space.</summary>
        public bool IsAtmosphere(int x, int y) => behaviors[cells[y * width + x].element] == ElementBehavior.Atmosphere;

        Cell MakeCell(ushort element) =>
            new Cell { element = element, shade = (byte)(Rand() % (uint)ElementDatabase.Get(element).shadeCount) };

        // ---------------------------------------------------------------
        // MAIN LOOP
        // ---------------------------------------------------------------
        void Update()
        {
            if (paused) return;

            float dt = 1f / stepsPerSecond;
            accumulator += Time.deltaTime;

            int steps = 0;
            while (accumulator >= dt && steps < 4) // cap to avoid spiral of death
            {
                Step();
                accumulator -= dt;
                steps++;
            }
            if (steps == 4) accumulator = 0f;
        }

        // ---------------------------------------------------------------
        // SIMULATION
        // ---------------------------------------------------------------
        void Step()
        {
            parity ^= 1;
            leftToRight = !leftToRight; // alternate scan direction to avoid drift bias

            // bottom row first (y = 0), so falling cells never get processed twice
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int i = 0; i < width; i++)
                {
                    int x = leftToRight ? i : width - 1 - i;
                    int idx = row + x;

                    ElementBehavior behavior = behaviors[cells[idx].element];
                    if (behavior == ElementBehavior.Atmosphere) continue; // medium: never simulated
                    if (cells[idx].stamp == parity) continue;
                    cells[idx].stamp = parity;

                    switch (behavior)
                    {
                        case ElementBehavior.Powder: StepMovable(x, y, idx, false); break;
                        case ElementBehavior.Liquid: StepMovable(x, y, idx, true); break;
                            // Gas: StepMovable(x, y, idx, true) works too once a gas has a negative gravityInfluence
                    }
                }
            }
            Version++;
        }

        // ---------------------------------------------------------------
        // PHASE 1: FORCES (only change velocity, never position)
        // ---------------------------------------------------------------
        void ApplyGravity(int idx)
        {
            ref Cell c = ref cells[idx];
            float influence = ElementDatabase.Get(c.element).gravityInfluence;

            // e.g. (0, -9.81) * 0.1 * 1.0 = (0, -0.981) added to the velocity every step
            c.velocity += SimulationSettings.SimulationGravity * SimulationSettings.SimulationGravityFactor * influence;

            c.velocity.x = Mathf.Clamp(c.velocity.x, -SimulationSettings.SimulationMaxVelocity, SimulationSettings.SimulationMaxVelocity);
            c.velocity.y = Mathf.Clamp(c.velocity.y, -SimulationSettings.SimulationMaxVelocity, SimulationSettings.SimulationMaxVelocity);
        }

        // ---------------------------------------------------------------
        // PHASE 2: VELOCITY -> MOVEMENT
        // ---------------------------------------------------------------
        void StepMovable(int x, int y, int idx, bool liquid)
        {
            // ---- 1. forces ----
            ApplyGravity(idx);
            // future forces slot in here: ApplyBuoyancy(idx); ApplyWind(idx); ApplyPressure(idx); ...

            // ---- 2. movement ----
            float friction = ElementDatabase.Get(cells[idx].element).friction;
            int cur = idx, cx = x, cy = y; // where the cell currently is (it changes as it swaps)
            int dir = RandBool() ? -1 : 1;
            bool landed = false;

            // --- vertical axis ---
            int dy = Mathf.RoundToInt(cells[cur].velocity.y);
            if (dy != 0 && !MoveAxis(ref cur, ref cx, ref cy, 0, dy))
            {
                // blocked: impact
                landed = true;
                float impact = Mathf.Abs(cells[cur].velocity.y);
                cells[cur].velocity.y = 0f;
                int sy = dy > 0 ? 1 : -1;

                if (liquid)
                {
                    // liquids always slide down a slope and keep a little momentum
                    int before = cx;
                    if (TrySlope(ref cur, ref cx, ref cy, dir, sy) || TrySlope(ref cur, ref cx, ref cy, -dir, sy))
                    {
                        if (cells[cur].velocity.x == 0f) cells[cur].velocity.x = cx - before;
                        return;
                    }

                    // flat ground: convert fall speed into sideways speed
                    float vx = cells[cur].velocity.x;
                    float side = vx != 0f ? Mathf.Sign(vx) : dir;
                    cells[cur].velocity.x += side * impact;
                }
                else
                {
                    // powders: lose all sideways speed, then only slide if friction lets them
                    cells[cur].velocity.x = 0f;
                    if (FrictionRoll(friction)) return;
                    if (!TrySlope(ref cur, ref cx, ref cy, dir, sy)) TrySlope(ref cur, ref cx, ref cy, -dir, sy);
                    return;
                }
            }

            // --- horizontal axis ---
            // at rest on the ground: thin liquids keep creeping sideways, thick ones only on some steps
            if (liquid && landed && Mathf.RoundToInt(cells[cur].velocity.x) == 0 && !FrictionRoll(friction))
                cells[cur].velocity.x = dir;

            int dx = Mathf.RoundToInt(cells[cur].velocity.x);
            if (dx != 0 && !MoveAxis(ref cur, ref cx, ref cy, dx, 0))
            {
                // hit a wall or a denser neighbour
                if (liquid) cells[cur].velocity.x = -cells[cur].velocity.x; // bounce (takes effect next step)
                else cells[cur].velocity.x = 0f;
            }

            // friction only acts while touching the ground
            if (landed) cells[cur].velocity.x *= 1f - friction;
        }

        /// <summary>
        /// Moves the cell at `cur` along ONE axis (exactly one of dx / dy is non-zero) as far as the path
        /// stays clear. Updates cur / cx / cy to the cell's new location.
        /// Returns true if it travelled the whole distance, false if something stopped it early.
        /// </summary>
        bool MoveAxis(ref int cur, ref int cx, ref int cy, int dx, int dy)
        {
            int steps = Mathf.Abs(dx) + Mathf.Abs(dy);
            int sx = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int sy = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

            int free = 0;
            while (free < steps && CanEnter(cur, cx + sx * (free + 1), cy + sy * (free + 1)))
                free++;

            if (free > 0)
            {
                cx += sx * free;
                cy += sy * free;
                cur = MoveTo(cur, cx, cy);
            }
            return free == steps;
        }

        /// <summary>One diagonal step (dirX, dirY). Used for sliding down slopes.</summary>
        bool TrySlope(ref int cur, ref int cx, ref int cy, int dirX, int dirY)
        {
            int nx = cx + dirX, ny = cy + dirY;
            if (!CanEnter(cur, nx, ny)) return false;

            cur = MoveTo(cur, nx, ny);
            cx = nx;
            cy = ny;
            return true;
        }

        /// <summary>Swaps the cell into (x, y) and returns its new index. The displaced cell ends up at the old spot.</summary>
        int MoveTo(int from, int x, int y)
        {
            int to = y * width + x;
            Swap(from, to);
            cells[from].velocity = Vector2.zero; // the displaced cell doesn't inherit our momentum
            return to;
        }

        bool FrictionRoll(float friction) => (Rand() & 0xFFFF) < (uint)(friction * 65536f);

        bool CanEnter(int from, int tx, int ty)
        {
            if (!InBounds(tx, ty)) return false;
            return ElementDatabase.Get(cells[ty * width + tx].element).density
                 < ElementDatabase.Get(cells[from].element).density;
        }

        void Swap(int a, int b)
        {
            Cell t = cells[a];
            cells[a] = cells[b];
            cells[b] = t;
            cells[a].stamp = cells[b].stamp = parity;
        }

        // xorshift32 - cheaper than UnityEngine.Random in a hot loop
        uint Rand()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return rng;
        }

        bool RandBool() => (Rand() & 0x10000) != 0;
    }
}