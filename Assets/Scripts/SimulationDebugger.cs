using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Debug overlay for the simulation hierarchy. Currently draws outlines of zones, areas and chunks,
    /// each level with its own on/off switch and its own awake / sleeping colour.
    ///
    /// Drawn with Gizmos, so it works in every render pipeline: always visible in the Scene view, and in the
    /// Game view when the "Gizmos" toggle in that view's toolbar is on. Play mode only (the hierarchy is
    /// built when the Simulation wakes up). Add it to any GameObject.
    ///
    /// Draw order: zones, then areas, then chunks (smaller levels on top). Inside each level the sleeping
    /// outlines are drawn first and the awake ones on top, so an awake edge is never hidden by a sleeping
    /// neighbour that shares the same border line.
    /// </summary>
    public class SimulationDebugger : MonoBehaviour
    {
        [System.Serializable]
        public struct LevelStyle
        {
            [Tooltip("Draw outlines for this level")]
            public bool render;
            [Tooltip("Outline colour while anything inside is awake")]
            public Color awakeColor;
            [Tooltip("Outline colour while everything inside is asleep (alpha 0 hides sleeping ones)")]
            public Color sleepingColor;

            public LevelStyle(bool render, Color awakeColor, Color sleepingColor)
            {
                this.render = render;
                this.awakeColor = awakeColor;
                this.sleepingColor = sleepingColor;
            }
        }

        [Header("References (found automatically if left empty)")]
        [SerializeField] Simulation simulation;
        [SerializeField] SimulationRenderer view; // defines where the grid sits in the world

        [Header("Outlines")]
        public LevelStyle zones = new LevelStyle(true, new Color(1.00f, 0.60f, 0.00f, 1.0f), new Color(0.45f, 0.25f, 0.00f, 0.7f));
        public LevelStyle areas = new LevelStyle(true, new Color(0.10f, 0.85f, 1.00f, 1.0f), new Color(0.10f, 0.30f, 0.45f, 0.6f));
        public LevelStyle chunks = new LevelStyle(true, new Color(0.20f, 1.00f, 0.20f, 0.9f), new Color(0.35f, 0.35f, 0.35f, 0.35f));

        // per-draw state
        Transform gridTransform;
        float cellSize; // world units per cell (before the transform's own scale)

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || !Resolve()) return;

            WorldLayout layout = simulation.Layout;
            gridTransform = view.transform;
            cellSize = 1f / view.cellsPerUnit;

            Color previous = Gizmos.color;

            if (zones.render)
            {
                DrawZones(layout, false);
                DrawZones(layout, true);
            }
            if (areas.render)
            {
                DrawAreas(layout, false);
                DrawAreas(layout, true);
            }
            if (chunks.render)
            {
                DrawChunks(layout, false);
                DrawChunks(layout, true);
            }

            Gizmos.color = previous;
        }

        bool Resolve()
        {
            if (simulation == null) simulation = Simulation.Instance;
            if (view == null) view = FindObjectOfType<SimulationRenderer>();
            return simulation != null && view != null && simulation.Layout != null;
        }

        // ---------------------------------------------------------------
        // ONE LOOP PER LEVEL. awakePass picks which half gets drawn this time.
        // ---------------------------------------------------------------
        void DrawZones(WorldLayout layout, bool awakePass)
        {
            for (int zy = 0; zy < layout.ZonesY; zy++)
            {
                for (int zx = 0; zx < layout.ZonesX; zx++)
                {
                    bool awake = simulation.IsZoneAwake(zx, zy);
                    if (awake != awakePass) continue;

                    Gizmos.color = awake ? zones.awakeColor : zones.sleepingColor;
                    DrawCellRect(layout.CellsOfZone(zx, zy));
                }
            }
        }

        void DrawAreas(WorldLayout layout, bool awakePass)
        {
            for (int ay = 0; ay < layout.AreasY; ay++)
            {
                for (int ax = 0; ax < layout.AreasX; ax++)
                {
                    bool awake = simulation.IsAreaAwake(ax, ay);
                    if (awake != awakePass) continue;

                    Gizmos.color = awake ? areas.awakeColor : areas.sleepingColor;
                    DrawCellRect(layout.CellsOfArea(ax, ay));
                }
            }
        }

        void DrawChunks(WorldLayout layout, bool awakePass)
        {
            for (int cy = 0; cy < layout.ChunksY; cy++)
            {
                for (int cx = 0; cx < layout.ChunksX; cx++)
                {
                    bool awake = simulation.IsChunkAwake(cx, cy);
                    if (awake != awakePass) continue;

                    Gizmos.color = awake ? chunks.awakeColor : chunks.sleepingColor;
                    DrawCellRect(layout.CellsOfChunk(cx, cy));
                }
            }
        }

        // ---------------------------------------------------------------
        // DRAWING
        // ---------------------------------------------------------------
        /// <summary>Outlines a rectangle given in cells (xMin / yMin inclusive, xMax / yMax exclusive).</summary>
        void DrawCellRect(RectInt r)
        {
            Vector3 bl = CellCorner(r.xMin, r.yMin);
            Vector3 br = CellCorner(r.xMax, r.yMin);
            Vector3 tr = CellCorner(r.xMax, r.yMax);
            Vector3 tl = CellCorner(r.xMin, r.yMax);

            Gizmos.DrawLine(bl, br);
            Gizmos.DrawLine(br, tr);
            Gizmos.DrawLine(tr, tl);
            Gizmos.DrawLine(tl, bl);
        }

        /// <summary>World position of a grid corner (the grid's origin is the sprite's bottom-left, like in SimulationRenderer).</summary>
        Vector3 CellCorner(int x, int y) =>
            gridTransform.TransformPoint(new Vector3(x * cellSize, y * cellSize, 0f));
    }
}