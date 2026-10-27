using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>Mouse / keyboard painting. Talks to the Simulation only through its public API.</summary>
    public class SimulationInput : MonoBehaviour
    {
        [SerializeField] Simulation simulation;
        [SerializeField] SimulationRenderer view; // used for world -> cell conversion

        [Header("Drawing (LMB-draw) (RMB-erase)")]
        [Tooltip("Name of the element to paint (e.g. Sand, Water). Number keys 1-9 pick the first nine non-Atmosphere elements.")]
        public string brushElement = "sand";
        [Range(0, 10)] public int brushRadius = 0;

        [Header("Painting (M5-add paint) (M4-clear paint)")]
        public Color paintColor = Color.red;
        [Tooltip("if true, only applies hue and saturation.")]
        public bool keepBrightness = false;

        const int AddPaintButton = 4;  // Mouse5
        const int CleanPaintButton = 3;  // Mouse4

        [Header("Wind (hold middle mouse and drag)")]
        [Tooltip("Wind added per cell of mouse travel in one frame. Raise for a stronger blow.")]
        public float windStrength = 0.05f;
        [Tooltip("Mouse travel per frame (in cells) beyond which a faster swipe adds no extra power.")]
        public float windMaxSwipe = 12f;
        [Range(0, 6)] public int windRadius = 2;

        ushort brushId;
        bool brushValid;
        string resolvedName;
        Camera cam;

        // wind drag state
        bool windActive;
        Vector2Int lastWindCell;

        void Start()
        {
            cam = Camera.main;
            if (simulation == null) simulation = Simulation.Instance;
            if (view == null) view = FindObjectOfType<SimulationRenderer>();

            if (simulation == null || view == null)
            {
                Debug.LogError("SimulationInput: missing Simulation or SimulationRenderer reference.", this);
                enabled = false;
                return;
            }

            ResolveBrush();
        }

        void Update()
        {
            if (brushElement != resolvedName) ResolveBrush(); // inspector edit at runtime
            HandleHotkeys();
            HandleDrawing();
            HandlePaint();
            HandleWind();
        }

        void ResolveBrush()
        {
            resolvedName = brushElement;
            brushValid = ElementDatabase.TryFind(brushElement, out brushId);
            if (!brushValid) Debug.LogWarning($"SimulationInput: unknown element '{brushElement}'.", this);
        }

        void HandleHotkeys()
        {
            // 1..9 -> the first nine elements that aren't mediums, in registration order
            int slot = 0;
            for (ushort id = 0; id < ElementDatabase.Count; id++)
            {
                ref readonly Element e = ref ElementDatabase.Get(id);
                if (e.type == ElementType.Atmosphere) continue;
                if (++slot > 9) break;

                if (Input.GetKeyDown(KeyCode.Alpha1 + (slot - 1)))
                {
                    brushElement = e.name;
                    ResolveBrush();
                }
            }

            if (Input.GetKeyDown(KeyCode.Space)) simulation.paused = !simulation.paused;
        }

        void HandleDrawing()
        {
            bool draw = Input.GetMouseButton(0) && brushValid;
            bool erase = Input.GetMouseButton(1);
            if (!draw && !erase) return;

            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2Int center = view.WorldToCell(world);

            for (int dy = -brushRadius; dy <= brushRadius; dy++)
            {
                for (int dx = -brushRadius; dx <= brushRadius; dx++)
                {
                    if (dx * dx + dy * dy > brushRadius * brushRadius) continue;

                    int x = center.x + dx, y = center.y + dy;
                    if (!simulation.InBounds(x, y)) continue;

                    ushort current = simulation.GetCell(x, y).element;

                    if (erase)
                    {
                        if (current != simulation.Medium) simulation.SetCell(x, y, simulation.Medium);
                    }
                    else if (current != brushId && simulation.IsAtmosphere(x, y)) // only paint into empty space
                    {
                        simulation.SetCell(x, y, brushId);
                    }
                }
            }
        }

        void HandlePaint()
        {
            bool paint = Input.GetMouseButton(AddPaintButton);
            bool clean = Input.GetMouseButton(CleanPaintButton);
            if (!paint && !clean) return;

            Color32 color = paintColor; // alpha is ignored, the cell's own alpha is kept

            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2Int center = view.WorldToCell(world);

            for (int dy = -brushRadius; dy <= brushRadius; dy++)
            {
                for (int dx = -brushRadius; dx <= brushRadius; dx++)
                {
                    if (dx * dx + dy * dy > brushRadius * brushRadius) continue;

                    int x = center.x + dx, y = center.y + dy;

                    if (clean) simulation.CleanCell(x, y);
                    else if (keepBrightness) simulation.DyeCell(x, y, color);
                    else simulation.PaintCell(x, y, color);
                }
            }
        }


        // ---------------------------------------------------------------
        // WIND: middle mouse. Direction = how the mouse moved since the last frame, power = how far it moved.
        // ---------------------------------------------------------------
        void HandleWind()
        {
            if (!Input.GetMouseButton(2))
            {
                windActive = false;
                return;
            }

            Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2Int cell = view.WorldToCell(mouseWorld);

            // first frame of the press: there is no previous position yet, so no direction
            if (!windActive)
            {
                windActive = true;
                lastWindCell = cell;
                return;
            }

            Vector2Int delta = cell - lastWindCell;
            if (delta == Vector2Int.zero) return; // lastWindCell is kept, so a slow drag still adds up to a full cell

            Vector2 dir = ((Vector2)delta).normalized;
            float power = Mathf.Min(delta.magnitude, windMaxSwipe) * windStrength;
            Vector2 impulse = dir * power;

            // blow along the whole path the mouse covered, not just where it ended up, so fast swipes leave no gaps
            var path = GridHelper.GetLineTraversalPositionsBR(lastWindCell.x, lastWindCell.y, cell.x, cell.y);
            foreach (var p in path)
                BlowAirFlowAt(p.X, p.Y, impulse);

            lastWindCell = cell;
        }

        void BlowAirFlowAt(int cx, int cy, Vector2 impulse)
        {
            int r2 = windRadius * windRadius;

            for (int dy = -windRadius; dy <= windRadius; dy++)
            {
                for (int dx = -windRadius; dx <= windRadius; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;

                    simulation.AddWind(cx + dx, cy + dy, impulse); // bounds-checked inside
                }
            }
        }
    }
}