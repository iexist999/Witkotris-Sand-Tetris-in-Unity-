using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>Mouse / keyboard painting. Talks to the Simulation only through its public API.</summary>
    public class SimulationInput : MonoBehaviour
    {
        [SerializeField] Simulation simulation;
        [SerializeField] SimulationRenderer view; // used for world -> cell conversion

        [Header("Drawing")]
        [Tooltip("Name of the element to paint (e.g. Sand, Water). Number keys 1-9 pick the first nine non-Atmosphere elements.")]
        public string brushElement = "sand";
        [Range(0, 10)] public int brushRadius = 0;

        ushort brushId;
        bool brushValid;
        string resolvedName;
        Camera cam;

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
    }
}