using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Runtime snapshot of a <see cref="WorldSettings"/> hierarchy: the size of every level plus all the
    /// coordinate / index conversions. Built once by <see cref="Simulation"/>.
    ///
    ///     cell  ->  chunk  ->  area  ->  zone        (each level is counted in the one below)
    ///
    /// Every level is stored as one flat, row-major array: index = y * countX + x.
    /// All conversions expect in-bounds input (no bounds checks, no floor-division for negatives).
    /// WorldSettings has similar Vector2Int helpers for tooling; this class is the hot-loop version
    /// (lookup tables instead of divisions) and keeps runtime data out of the settings asset.
    /// </summary>
    public sealed class WorldLayout
    {
        // ---- counts per level
        public readonly int CellsX, CellsY;
        public readonly int ChunksX, ChunksY;
        public readonly int AreasX, AreasY;
        public readonly int ZonesX, ZonesY;

        // ---- children per parent
        public readonly int ChunkSize;                  // cells per chunk side
        public readonly int AreaChunksX, AreaChunksY;   // chunks per area
        public readonly int ZoneAreasX, ZoneAreasY;     // areas per zone

        // cell -> chunk lookup tables (one entry per cell column / row), so the hot path needs no division
        readonly int[] chunkColOfCell;  // cell x -> chunk x
        readonly int[] chunkRowOfCell;  // cell y -> chunk y * ChunksX (already a row offset)

        public WorldLayout(WorldSettings s)
        {
            ChunkSize = s.chunkSize;
            AreaChunksX = s.areaChunksX;
            AreaChunksY = s.areaChunksY;
            ZoneAreasX = s.zoneAreasX;
            ZoneAreasY = s.zoneAreasY;

            ZonesX = s.worldZonesX;
            ZonesY = s.worldZonesY;
            AreasX = ZonesX * ZoneAreasX;
            AreasY = ZonesY * ZoneAreasY;
            ChunksX = AreasX * AreaChunksX;
            ChunksY = AreasY * AreaChunksY;
            CellsX = ChunksX * ChunkSize;
            CellsY = ChunksY * ChunkSize;

            chunkColOfCell = new int[CellsX];
            for (int x = 0; x < CellsX; x++) chunkColOfCell[x] = x / ChunkSize;

            chunkRowOfCell = new int[CellsY];
            for (int y = 0; y < CellsY; y++) chunkRowOfCell[y] = (y / ChunkSize) * ChunksX;
        }

        // ---------------------------------------------------------------
        // FLAT INDICES (coordinates at a level -> index into that level's array)
        // ---------------------------------------------------------------
        public int CellIndex(int x, int y) => y * CellsX + x;
        public int ChunkIndex(int cx, int cy) => cy * ChunksX + cx;
        public int AreaIndex(int ax, int ay) => ay * AreasX + ax;
        public int ZoneIndex(int zx, int zy) => zy * ZonesX + zx;

        // ---------------------------------------------------------------
        // CELL -> CHUNK, hot path (used on every wake)
        // ---------------------------------------------------------------
        public int ChunkIndexOfCell(int x, int y) => chunkRowOfCell[y] + chunkColOfCell[x];

        // ---------------------------------------------------------------
        // CHILD -> PARENT COORDINATES
        // ---------------------------------------------------------------
        public Vector2Int ChunkOfCell(int x, int y) => new Vector2Int(x / ChunkSize, y / ChunkSize);
        public Vector2Int AreaOfChunk(int cx, int cy) => new Vector2Int(cx / AreaChunksX, cy / AreaChunksY);
        public Vector2Int ZoneOfArea(int ax, int ay) => new Vector2Int(ax / ZoneAreasX, ay / ZoneAreasY);

        public Vector2Int AreaOfCell(int x, int y) => AreaOfChunk(x / ChunkSize, y / ChunkSize);

        public Vector2Int ZoneOfCell(int x, int y)
        {
            Vector2Int a = AreaOfCell(x, y);
            return ZoneOfArea(a.x, a.y);
        }

        // ---------------------------------------------------------------
        // PARENT -> CHILD RANGES (RectInt: xMin / yMin inclusive, xMax / yMax exclusive)
        // ---------------------------------------------------------------
        public RectInt CellsOfChunk(int cx, int cy) => new RectInt(cx * ChunkSize, cy * ChunkSize, ChunkSize, ChunkSize);
        public RectInt ChunksOfArea(int ax, int ay) => new RectInt(ax * AreaChunksX, ay * AreaChunksY, AreaChunksX, AreaChunksY);
        public RectInt AreasOfZone(int zx, int zy) => new RectInt(zx * ZoneAreasX, zy * ZoneAreasY, ZoneAreasX, ZoneAreasY);

        // ---------------------------------------------------------------
        // PARENT -> CELL RANGE (whole area / zone expressed in cells)
        // ---------------------------------------------------------------
        public RectInt CellsOfArea(int ax, int ay) =>
            new RectInt(ax * AreaChunksX * ChunkSize, ay * AreaChunksY * ChunkSize, AreaChunksX * ChunkSize, AreaChunksY * ChunkSize);

        public RectInt CellsOfZone(int zx, int zy)
        {
            int w = ZoneAreasX * AreaChunksX * ChunkSize, h = ZoneAreasY * AreaChunksY * ChunkSize;
            return new RectInt(zx * w, zy * h, w, h);
        }
    }
}