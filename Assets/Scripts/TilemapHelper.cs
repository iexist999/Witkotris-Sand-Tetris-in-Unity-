using UnityEngine;
using UnityEngine.Tilemaps;

namespace SandBXDSIM
{
    /// <summary>Optional: converts tiles in a Tilemap into cells of a chosen element when the sim starts.</summary>
    public class TilemapHelper : MonoBehaviour
    {
        [SerializeField] Simulation simulation;
        [SerializeField] SimulationRenderer view; // defines where the grid sits in the world
        [SerializeField] Tilemap solidTilemap;
        [SerializeField] string elementName = "stone";
        [SerializeField] bool bakeOnStart = true;

        void Start()
        {
            if (simulation == null) simulation = Simulation.Instance;
            if (view == null) view = FindObjectOfType<SimulationRenderer>();
            if (bakeOnStart) Bake();
        }

        [ContextMenu("Bake")]
        public void Bake()
        {
            if (solidTilemap == null || simulation == null || view == null) return;

            if (!ElementDatabase.TryFind(elementName, out ushort id))
            {
                Debug.LogWarning($"TilemapBaker: unknown element '{elementName}'.", this);
                return;
            }

            for (int y = 0; y < simulation.worldSettings.GridHeight; y++)
            {
                for (int x = 0; x < simulation.worldSettings.GridWidth; x++)
                {
                    Vector3 world = view.CellToWorld(x, y);
                    if (solidTilemap.HasTile(solidTilemap.WorldToCell(world)))
                        simulation.SetCell(x, y, id);
                }
            }
        }
    }
}