using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Debug overlay for the simulation. Draws outlines of zones, areas and chunks (each level with its own
    /// on/off switch and its own awake / sleeping colour) and, optionally, the wind field as arrows and
    /// mouse info (a + at the pointer, the hovered cell outlined, and its cell / chunk coordinates under the pointer).
    ///
    /// Drawn with Gizmos, so it works in every render pipeline: always visible in the Scene view, and in the
    /// Game view when the "Gizmos" toggle in that view's toolbar is on. Play mode only (the hierarchy is
    /// built when the Simulation wakes up). Add it to any GameObject.
    /// The one exception is the mouse info text: it is IMGUI (OnGUI), so it is Game view only and does not need the Gizmos toggle.
    ///
    /// Draw order: zones, then areas, then chunks (smaller levels on top), then the wind arrows, then the mouse info on top of everything.
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

            public LevelStyle(bool render, Color awakeColor, Color sleepingColor) {
                this.render = render;
                this.awakeColor = awakeColor;
                this.sleepingColor = sleepingColor;
            }
        }

        [Header("References (found automatically if left empty)")]
        [SerializeField] Simulation simulation;
        [SerializeField] SimulationRenderer view; // defines where the grid sits in the world
        [SerializeField] Camera cam;              // only used by Mouse Info, defaults to Camera.main

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

        [Header("Mouse Info")]
        [Tooltip("Draws a + at the mouse, outlines the hovered cell and shows its X / Y (and its chunk's X / Y in brackets) under the pointer. " +
                 "The + and the outline are gizmos (Game view needs the Gizmos toggle on), the text is always shown in the Game view.")]
        public bool drawMouseInfo = false;
        [Tooltip("Length of each arm of the + in cells")]
        [Min(0.1f)] public float crosshairSize = 2f;
        public Color crosshairColor = new Color(1.00f, 1.00f, 1.00f, 1.00f);
        public Color hoverCellColor = new Color(1.00f, 0.90f, 0.20f, 1.00f);
        public Color labelColor = Color.white;
        public Color labelBackground = new Color(0f, 0f, 0f, 0.65f);
        [Range(8, 32)] public int labelFontSize = 12;

        const float LabelOffset = 20f; // pixels between the pointer and the label

        // per-draw state
        Transform gridTransform;
        float cellSize; // world units per cell (before the transform's own scale)
        GUIStyle labelStyle;

        void OnDrawGizmos() {
            if (!Application.isPlaying || !Resolve()) return;

            WorldLayout layout = simulation.Layout;
            gridTransform = view.transform;
            cellSize = 1f / view.cellsPerUnit;

            Color previous = Gizmos.color;

            if (zones.render) {
                DrawZones(layout, false);
                DrawZones(layout, true);
            }
            if (areas.render) {
                DrawAreas(layout, false);
                DrawAreas(layout, true);
            }
            if (chunks.render) {
                DrawChunks(layout, false);
                DrawChunks(layout, true);
            }
            if (drawAirflow) DrawAirflow(layout);
            if (drawMouseInfo) DrawMouseGizmos();

            Gizmos.color = previous;
        }

        bool Resolve() {
            if (simulation == null) simulation = Simulation.Instance;
            if (view == null) view = FindObjectOfType<SimulationRenderer>();
            return simulation != null && view != null && simulation.Layout != null;
        }

        // ---------------------------------------------------------------
        // ONE LOOP PER LEVEL. awakePass picks which half gets drawn this time.
        // ---------------------------------------------------------------
        void DrawZones(WorldLayout layout, bool awakePass) {
            for (int zy = 0; zy < layout.ZonesY; zy++) {
                for (int zx = 0; zx < layout.ZonesX; zx++) {
                    bool awake = simulation.IsZoneAwake(zx, zy);
                    if (awake != awakePass) continue;

                    Gizmos.color = awake ? zones.awakeColor : zones.sleepingColor;
                    DrawCellRect(layout.CellsOfZone(zx, zy));
                }
            }
        }

        void DrawAreas(WorldLayout layout, bool awakePass) {
            for (int ay = 0; ay < layout.AreasY; ay++) {
                for (int ax = 0; ax < layout.AreasX; ax++) {
                    bool awake = simulation.IsAreaAwake(ax, ay);
                    if (awake != awakePass) continue;

                    Gizmos.color = awake ? areas.awakeColor : areas.sleepingColor;
                    DrawCellRect(layout.CellsOfArea(ax, ay));
                }
            }
        }

        void DrawChunks(WorldLayout layout, bool awakePass) {
            for (int cy = 0; cy < layout.ChunksY; cy++) {
                for (int cx = 0; cx < layout.ChunksX; cx++) {
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
        void DrawAirflow(WorldLayout layout) {
            int step = arrowSpacing;
            int half = step / 2;
            float maxLength = step * 1.5f;
            float minSqr = minSpeed * minSpeed;

            for (int y = half; y < layout.CellsY; y += step) {
                for (int x = half; x < layout.CellsX; x += step) {
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
        void DrawArrow(Vector2 from, Vector2 vec) {
            Vector2 tip = from + vec;
            Gizmos.DrawLine(CellPoint(from.x, from.y), CellPoint(tip.x, tip.y));

            float length = vec.magnitude;
            Vector2 back = -vec / length * Mathf.Min(length * 0.35f, 1.5f); // head size in cells, never huge

            Vector2 h1 = tip + Rotate(back, 28f);
            Vector2 h2 = tip + Rotate(back, -28f);
            Gizmos.DrawLine(CellPoint(tip.x, tip.y), CellPoint(h1.x, h1.y));
            Gizmos.DrawLine(CellPoint(tip.x, tip.y), CellPoint(h2.x, h2.y));
        }

        static Vector2 Rotate(Vector2 v, float degrees) {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ---------------------------------------------------------------
        // MOUSE INFO
        // ---------------------------------------------------------------
        /// <summary>
        /// The mouse as the simulation sees it: `cellPos` is the exact pointer position in (fractional) cell coordinates
        /// (where the + goes), `cell` is the cell the pointer is in. The cell comes from the same conversion SimulationInput
        /// uses, so the outlined cell is exactly the one a brush would paint.
        /// Returns false when there is no camera or the pointer is outside the screen.
        /// </summary>
        bool TryGetMouse(out Vector2 cellPos, out Vector2Int cell) {
            cellPos = default;
            cell = default;

            if (cam == null) cam = Camera.main;
            if (cam == null) return false;

            Vector3 screen = Input.mousePosition;
            if (screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height) return false;

            Vector3 world = cam.ScreenToWorldPoint(screen);
            Vector3 local = view.transform.InverseTransformPoint(world);

            cellPos = new Vector2(local.x * view.cellsPerUnit, local.y * view.cellsPerUnit);
            cell = view.WorldToCell(world);
            return true;
        }

        /// <summary>The + at the pointer and the outline of the hovered cell (the outline only while the pointer is inside the world).</summary>
        void DrawMouseGizmos() {
            if (!TryGetMouse(out Vector2 pos, out Vector2Int cell)) return;

            Gizmos.color = crosshairColor;
            Gizmos.DrawLine(CellPoint(pos.x - crosshairSize, pos.y), CellPoint(pos.x + crosshairSize, pos.y));
            Gizmos.DrawLine(CellPoint(pos.x, pos.y - crosshairSize), CellPoint(pos.x, pos.y + crosshairSize));

            if (!simulation.InBounds(cell.x, cell.y)) return;

            Gizmos.color = hoverCellColor;
            DrawCellRect(new RectInt(cell.x, cell.y, 1, 1));
        }

        /// <summary>
        /// "X: 120  Y: 45  (X: 7  Y: 2)" under the pointer: the hovered cell, then its chunk in brackets.
        /// IMGUI, because gizmos can't draw text at runtime. Nothing is shown while the pointer is outside the world.
        /// </summary>
        void OnGUI() {
            if (!drawMouseInfo || Event.current.type != EventType.Repaint) return;
            if (!Application.isPlaying || !Resolve()) return;
            if (!TryGetMouse(out _, out Vector2Int cell) || !simulation.InBounds(cell.x, cell.y)) return;

            Vector2Int chunk = simulation.Layout.ChunkOfCell(cell.x, cell.y);
            string text = $"X: {cell.x}  Y: {cell.y}  (X: {chunk.x}  Y: {chunk.y})";

            if (labelStyle == null) {
                labelStyle = new GUIStyle(GUI.skin.label) {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                    padding = new RectOffset(6, 6, 2, 2)
                };
            }
            labelStyle.fontSize = labelFontSize;
            labelStyle.normal.textColor = labelColor;

            Vector2 size = labelStyle.CalcSize(new GUIContent(text));

            // IMGUI's y axis points down, Input.mousePosition's points up
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

            float x = Mathf.Clamp(mouse.x - size.x * 0.5f, 0f, Mathf.Max(0f, Screen.width - size.x));
            float y = mouse.y + LabelOffset;
            if (y + size.y > Screen.height) y = mouse.y - LabelOffset - size.y; // no room below: put it above the pointer

            var rect = new Rect(x, y, size.x, size.y);

            Color previous = GUI.color;
            GUI.color = labelBackground;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(rect, text, labelStyle);
        }

        // ---------------------------------------------------------------
        // DRAWING
        // ---------------------------------------------------------------
        /// <summary>Outlines a rectangle given in cells (xMin / yMin inclusive, xMax / yMax exclusive).</summary>
        void DrawCellRect(RectInt r) {
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