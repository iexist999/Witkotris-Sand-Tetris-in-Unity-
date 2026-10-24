using UnityEngine;

public class SimulationSettings
{

    /// =================================
    /// GENERAL SETTINGS
    /// =================================

    /// <summary>
    /// should cells located in chunk's sides or corner wake up adjacent neighbors on simulation step?
    /// </summary>
    public static bool WakeAdjacentChunks = false;

    /// =================================
    /// SIMULATION PHYSICS SETTINGS
    /// =================================
    
    public static Vector2 SimulationGravity = new Vector2(0, -9.81f);
    public static float SimulationGravityFactor = 0.05f; // -9.81f * 0.05f = -0.49f added to velocity.y per step

    /// <summary>
    /// velocity cap for the simulation
    /// </summary>
    public static float SimulationMaxVelocity = 200f;

    public static bool SimulateGravity = true;
    public static bool SimulateFriction = true;

    public static float MassGravityInfluence = 0.2f;

    public static bool SimulateSurfaceTension = true;
    public static int SurfaceTensionReach = 8;
    public static int SurfaceTensionPuddleSize = 24;
    public static float SurfaceTensionSharpness = 2f;

    /// <summary>
    /// general velocity transfer multiplier for all elementtypes
    /// </summary>
    public static float ImpactTransferMultiplier = 1f;

    /// <summary>
    /// share of a landing liquid's fall speed that becomes sideways speed (0 = none, 1 = all of it).
    /// With 0.5, water hitting flat ground at speed 4 gains 2 cells/step sideways.
    /// </summary>
    public static float LiquidImpactTransferMultiplier = 1f;

    /// <summary>
    /// same for powders (sand, dirt...). Keep it well below the liquid value: grains should scatter a little
    /// when they hit the ground, not splash like water.
    /// </summary>
    public static float PowderImpactTransferMultiplier = 1f;

    /// <summary>How many extra cells a powder with powderFlowability 0 can hold on a ledge.</summary>
    public static int PowderMaxExtraStack = 6;


    /// <summary>
    /// sideways speed at or below this counts as "not moving sideways" when a cell lands, so its
    /// direction is picked at random instead of following its (tiny, friction-decayed) leftover velocity.
    /// </summary>
    public static float ImpactRestSpeed = 0.001f;

    /// <summary>
    /// should solid materials get held up by diagonal connection with anchored neighboring cells
    /// </summary>
    public static bool SolidSupportDiagonals = false;

    // air simulation settings
    //public static float WindDiffusion = 0.25f; // 0..1, keep it at or below ~0.5
    //public static float WindDecay = 0.995f;     // per step
    //public static float WindCoupling = 0.1f;   // how much a moving cell pushes the air
    //public static float WindMaxSpeed = 8f;
    public static float WindWakeSpeed = 0.2f; // wind stronger than this keeps dust awake
    public static bool SimulateDrag = true;

    public static readonly FlowSettings AirFlow = new FlowSettings
    {
        element = null,          // default medium
        decay = 0.995f,
        maxSpeed = 8f,
        coupling = 0.1f,
        iterations = 20,
        openBoundaries = true,
    };

    public static readonly FlowSettings WaterFlow = new FlowSettings
    {
        element = "water",       // its vorticity property drives this field
        decay = 0.98f,
        maxSpeed = 3f,
        coupling = 0.3f,
        iterations = 30,
        openBoundaries = false,   // sealed: water can't leave the world
    };

    //public static int WindPressureIterations = 20;  // more = stiffer air, slower
    //public static float WindOverRelaxation = 1.7f;  // 1..2, above ~1.9 gets unstable
    //public static float WindVorticity = 0.1f;       // 0 = off
    //public static bool WindOpenBoundaries = true;   // false = sealed box, air bounces off the edges
    //public static float WindSleepSpeed = 0.01f;
    //public static bool WindTrackPressure = false;   // turn on to read PressureAt()

    public sealed class FlowSettings
    {
        public string element;
        public float vorticityScale = 1f;
        public float decay = 0.99f;
        public float maxSpeed = 5f;
        public float coupling = 0.01f;
        public int iterations = 20;
        public float overRelaxation = 1.7f;
        public float sleepSpeed = 0.01f;
        public bool openBoundaries = true;
        public bool trackPressure;
        public float pushThreshold = 4.65f;

        public float vorticityMinCurl = 0.08f;     // swirls weaker than this get no confinement at all
        public float vorticityFullCurl = 0.6f;    // swirls stronger than this get the full strength; in between it ramps up
    }

    // default values for worldsettings
    public static int DefaultWorldChunkWidth = 16;
    public static int DefaultWorldChunkHeight = 16;

    public static int DefaultWorldAreaWidth = 64; //16 * 4 = 64
    public static int DefaultWorldAreaHeight = 64; //16 * 4 = 64

    public static int DefaultWorldZoneWidth = 6; // areas in a zone horizontally
    public static int DefaultWorldZoneHeight = 4; // areas in a zone vertically
    // this means a zone is 6 * 64 = 384 wide; 4 * 64 = 256 tall = 384x256 cells
    // render distance in game should refer to the amount of zones to load around camera center
}