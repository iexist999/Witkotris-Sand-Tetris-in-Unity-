using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Draws a <see cref="Simulation"/> onto a sprite. Lives on its own GameObject,
    /// so you can move / scale / parent it independently of the simulation.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SimulationRenderer : MonoBehaviour
    {
        [SerializeField] Simulation simulation;
        [Tooltip("Cells per Unity world unit")]
        public float cellsPerUnit = 16f;

        Texture2D tex;
        Color32[] palette;
        int shadeStride;
        int renderedVersion = -1;

        // ---------------------------------------------------------------
        // SETUP
        // ---------------------------------------------------------------
        void Start()
        {
            if (simulation == null) simulation = Simulation.Instance;
            if (simulation == null)
            {
                Debug.LogError("SimulationRenderer: no Simulation assigned or found.", this);
                enabled = false;
                return;
            }

            BuildPalette();
            BuildTexture();
        }

        void BuildPalette()
        {
            int count = ElementDatabase.Count;

            shadeStride = 1;
            for (int id = 0; id < count; id++)
                shadeStride = Mathf.Max(shadeStride, ElementDatabase.Get((ushort)id).shadeCount);

            // palette is indexed by [elementId * shadeStride + shade]
            palette = new Color32[count * shadeStride];
            for (int id = 0; id < count; id++)
            {
                ref readonly Element def = ref ElementDatabase.Get((ushort)id);

                for (int s = 0; s < shadeStride; s++)
                {
                    if (def.customColor && def.customColors != null && def.customColors.Length > 0)
                    {
                        palette[id * shadeStride + s] = def.customColors[Mathf.Min(s, def.customColors.Length - 1)];
                        continue;
                    }

                    float f = 1f - s * 0.04f;
                    Color32 c = def.baseColor;
                    palette[id * shadeStride + s] = new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), c.a);
                }
            }
        }

        void BuildTexture()
        {
            int w = simulation.worldSettings.GridWidth, h = simulation.worldSettings.GridHeight;

            tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            GetComponent<SpriteRenderer>().sprite =
                Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.zero, cellsPerUnit);
        }

        // ---------------------------------------------------------------
        // DRAWING
        // ---------------------------------------------------------------
        // LateUpdate so we always draw after the simulation / input have updated this frame.
        void LateUpdate()
        {
            if (simulation.Version != renderedVersion) Render();
        }

        void Render()
        {
            var raw = tex.GetRawTextureData<Color32>();
            var cells = simulation.Cells;

            for (int i = 0; i < cells.Length; i++)
                raw[i] = palette[cells[i].element * shadeStride + cells[i].shade];

            tex.Apply(false);
            renderedVersion = simulation.Version;
        }

        void OnDestroy()
        {
            if (tex != null) Destroy(tex);
        }

        // ---------------------------------------------------------------
        // WORLD <-> GRID CONVERSION (respects this object's position/rotation/scale)
        // ---------------------------------------------------------------
        public Vector2Int WorldToCell(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return new Vector2Int(Mathf.FloorToInt(local.x * cellsPerUnit),
                                  Mathf.FloorToInt(local.y * cellsPerUnit));
        }

        public Vector3 CellToWorld(int x, int y)
        {
            return transform.TransformPoint(new Vector3((x + 0.5f) / cellsPerUnit,
                                                        (y + 0.5f) / cellsPerUnit, 0f));
        }
    }
}