using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Debug overlay for the simulation. Draws outlines of zones, areas and chunks (each level with its own
    /// on/off switch and its own awake / sleeping colour) and, optionally, the wind field as arrows.
    ///
    /// Drawn with Gizmos, so it works in every render pipeline: always visible in the Scene view, and in the
    /// Game view when the "Gizmos" toggle in that view's toolbar is on. Play mode only (the hierarchy is
    /// built when the Simulation wakes up). Add it to any GameObject.
    ///
    /// Draw order: zones, then areas, then chunks (smaller levels on top), then the wind arrows on top of everything.
    /// Inside each outline level the sleeping outlines are drawn first and the awake ones on top, so an awake
    /// edge is never hidden by a sleeping neighbour that shares the same border line.
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

        [Header("Airflow")]
        [Tooltip("Draw one arrow per block of cells, pointing where the wind blows")]
        public bool drawAirflow = true;
        [Tooltip("One arrow per N x N cells. Lower = denser (and slower to draw).")]
        [Range(1, 16)] public int arrowSpacing = 4;
        [Tooltip("Arrow length in cells per 1 cell/step of wind. Capped at 1.5x the spacing so neighbours don't overlap.")]
        [Min(0f)] public float arrowScale = 3f;
        [Tooltip("Wind slower than this (cells/step) gets no arrow")]
        [Min(0f)] public float minSpeed = 0.02f;
        [Tooltip("Wind speed (cells/step) at which the arrow reaches the fast colour")]
        [Min(0.01f)] public float colorMaxSpeed = 2f;
        public Color slowColor = new Color(0.40f, 0.80f, 1.00f, 0.85f);
        public Color fastColor = new Color(1.00f, 0.30f, 0.20f, 1.00f);

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
            if (drawAirflow) DrawAirflow(layout);

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
        // AIRFLOW
        // ---------------------------------------------------------------
        /// <summary>
        /// One arrow per arrowSpacing x arrowSpacing block, sampled at the block's centre. Direction = wind direction,
        /// length = wind speed (capped), colour = wind speed (slowColor -> fastColor). Still air and walls draw nothing.
        /// </summary>
        void DrawAirflow(WorldLayout layout)
        {
            int step = arrowSpacing;
            int half = step / 2;
            float maxLength = step * 1.5f;
            float minSqr = minSpeed * minSpeed;

            for (int y = half; y < layout.CellsY; y += step)
            {
                for (int x = half; x < layout.CellsX; x += step)
                {
                    Vector2 wind = simulation.WindAt(x, y);
                    float sqr = wind.sqrMagnitude;
                    if (sqr < minSqr || sqr == 0f) continue;

                    float speed = Mathf.Sqrt(sqr);
                    float length = Mathf.Min(speed * arrowScale, maxLength); // in cells
                    Vector2 vec = wind / speed * length;

                    Gizmos.color = Color.Lerp(slowColor, fastColor, Mathf.Clamp01(speed / colorMaxSpeed));
                    DrawArrow(new Vector2(x + 0.5f, y + 0.5f), vec);
                }
            }
        }

        /// <summary>Line from 'from' to 'from + vec' (both in cell coordinates) with a small two-line arrowhead.</summary>
        void DrawArrow(Vector2 from, Vector2 vec)
        {
            Vector2 tip = from + vec;
            Gizmos.DrawLine(CellPoint(from.x, from.y), CellPoint(tip.x, tip.y));

            float length = vec.magnitude;
            Vector2 back = -vec / length * Mathf.Min(length * 0.35f, 1.5f); // head size in cells, never huge

            Vector2 h1 = tip + Rotate(back, 28f);
            Vector2 h2 = tip + Rotate(back, -28f);
            Gizmos.DrawLine(CellPoint(tip.x, tip.y), CellPoint(h1.x, h1.y));
            Gizmos.DrawLine(CellPoint(tip.x, tip.y), CellPoint(h2.x, h2.y));
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
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
        Vector3 CellCorner(int x, int y) => CellPoint(x, y);

        /// <summary>World position of a point given in (fractional) cell coordinates, respecting the grid's position / rotation / scale.</summary>
        Vector3 CellPoint(float x, float y) =>
            gridTransform.TransformPoint(new Vector3(x * cellSize, y * cellSize, 0f));
    }
}