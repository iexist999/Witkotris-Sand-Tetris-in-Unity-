using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// A world template. Create assets via Assets > Create > SandBXDSIM > World Settings.
    /// Every hierarchy level is counted in the level below it (cells -> chunk -> area -> zone),
    /// so the levels always divide evenly. Treat assets as read-only at runtime: edits made to a
    /// ScriptableObject during Play Mode persist in the editor. Instantiate a copy if you need runtime tweaks.
    /// </summary>
    [CreateAssetMenu(fileName = "NewWorldSettings", menuName = "SandBXDSIM/World Settings")]
    public class WorldSettings : ScriptableObject
    {
        [Header("Hierarchy (each level counted in the level below)")]
        [Tooltip("Cells per chunk side. The only value in raw cells.")]
        [Min(1)] public int chunkSize = 16;
        [Tooltip("Chunks per area")]
        [Min(1)] public int areaChunksX = 8;
        [Min(1)] public int areaChunksY = 6;
        [Tooltip("Areas per zone")]
        [Min(1)] public int zoneAreasX = 3;
        [Min(1)] public int zoneAreasY = 3;

        [Header("World Size")]
        [Tooltip("World size in zones. With the flat single-grid simulation this IS the grid size.")]
        [Min(1)] public int worldZonesX = 1;
        [Min(1)] public int worldZonesY = 1;

        [Header("Simulation")]
        [Min(1f)] public float stepsPerSecond = 60f;
        [Tooltip("Name of an Atmosphere-type element that fills empty space (air, vacuum, ...)")]
        public string defaultMedium = "air";

        [Header("Physics")]
        public Vector2 gravity = new Vector2(0f, -9.81f);
        public float gravityFactor = 0.05f;
        [Min(0f)] public float maxVelocity = 200f;

        // DERIVED 
        public int AreaCellsX => areaChunksX * chunkSize;
        public int AreaCellsY => areaChunksY * chunkSize;

        public int ZoneChunksX => zoneAreasX * areaChunksX;
        public int ZoneChunksY => zoneAreasY * areaChunksY;
        public int ZoneCellsX => ZoneChunksX * chunkSize;
        public int ZoneCellsY => ZoneChunksY * chunkSize;

        public int GridWidth => worldZonesX * ZoneCellsX;
        public int GridHeight => worldZonesY * ZoneCellsY;

        /// <summary>Pushes this world's physics values into the global SimulationSettings.</summary>
        public void ApplyPhysics()
        {
            SimulationSettings.SimulationGravity = gravity;
            SimulationSettings.SimulationGravityFactor = gravityFactor;
            SimulationSettings.SimulationMaxVelocity = maxVelocity;
        }

        // ---------------------------------------------------------------
        // CONVERSIONS (floor division, so negative coordinates are safe)
        // ---------------------------------------------------------------
        static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

        public Vector2Int CellToChunk(int x, int y) => new Vector2Int(FloorDiv(x, chunkSize), FloorDiv(y, chunkSize));
        public Vector2Int ChunkToArea(int cx, int cy) => new Vector2Int(FloorDiv(cx, areaChunksX), FloorDiv(cy, areaChunksY));
        public Vector2Int AreaToZone(int ax, int ay) => new Vector2Int(FloorDiv(ax, zoneAreasX), FloorDiv(ay, zoneAreasY));

#if UNITY_EDITOR
        void OnValidate()
        {
            long cells = (long)GridWidth * GridHeight;
            if (cells > 4_000_000)
                Debug.LogWarning($"{name}: {GridWidth}x{GridHeight} = {cells:N0} cells in one flat grid. " +
                                 "Fine for testing, but this is where zone streaming becomes necessary.", this);
        }
#endif
    }
}