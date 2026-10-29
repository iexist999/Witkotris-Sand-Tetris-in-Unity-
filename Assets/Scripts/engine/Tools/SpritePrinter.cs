
using System.Collections.Generic;
using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Prints a sprite / texture into the simulation as real cells (sand, water, ...), optionally painted to match it.
    ///
    ///   1 sprite pixel = 1 cell. Pixels below PrintSettings.alphaThreshold are skipped (they leave the world untouched).
    ///   The pixel that contains the sprite's pivot lands on the cell at printPosition + printOffset.
    ///   Rotation is counter-clockwise in degrees, around that pixel, sampled nearest-neighbour (no holes, no blur).
    ///   Anything that ends up outside the world is cut off.
    ///
    /// Talks to the simulation only through its public API (SetCell / PaintCell / GetCell / IsAtmosphere / InBounds).
    /// The sprite's texture must have Read/Write enabled in its import settings.
    /// </summary>
    public class SpritePrinter : MonoBehaviour
    {
        // ---------------------------------------------------------------
        // OPTIONAL TEST HOOK (safe to delete)
        // Play mode: right-click this component > "Print Test Sprite".
        // ---------------------------------------------------------------
        [Header("Test hook")]
        [SerializeField] Sprite testSprite;
        [SerializeField] string testElement = "sand";
        [SerializeField] Vector2 testPosition = new Vector2(100f, 100f);
        [SerializeField, Range(0f, 360f)] float testRotation;

        [ContextMenu("Print Test Sprite")]
        void PrintTestSprite() {
            if (testSprite == null) { Debug.LogWarning("WARNING: no test sprite assigned."); return; }

            var settings = new PrintSettings {
                printPosition = testPosition,
                printRotation = testRotation,
                elementPrintMode = PrintSettings.ElementPrintMode.SingleElement,
                elementArray = new (Color32?, string)[] { (null, testElement) },
                colorPrintMode = PrintSettings.ColorPrintMode.SpriteColor,
            };
            PrintSpriteWorld(testSprite, settings);
        }

        // ---------------------------------------------------------------
        // PUBLIC API
        // ---------------------------------------------------------------

        /// <summary>Prints a sprite. Its own pivot decides which pixel sits on the print position. Returns how many cells were placed.</summary>
        public int PrintSpriteWorld(Sprite sprite, PrintSettings printSettings, List<Vector2Int> placedCells = null) {
            if (sprite == null) { Debug.LogWarning("WARNING: sprite is null. cancelling operation."); return 0; }
            if (!TryReadSprite(sprite, out Color32[] pixels, out int w, out int h, out Vector2 pivot)) return 0;

            return Print(pixels, w, h, pivot, printSettings, placedCells);
        }

        /// <summary>Prints a whole texture. The pivot is its centre. Returns how many cells were placed.</summary>
        public int PrintSpriteWorld(Texture2D texture, PrintSettings printSettings, List<Vector2Int> placedCells = null) {
            if (texture == null) { Debug.LogWarning("WARNING: texture is null. cancelling operation."); return 0; }

            Color32[] pixels;
            try { pixels = texture.GetPixels32(); }
            catch (System.Exception e) {
                Debug.LogWarning($"WARNING: can't read '{texture.name}' (enable Read/Write in its import settings, and use an uncompressed format). cancelling operation.\n{e.Message}");
                return 0;
            }

            return Print(pixels, texture.width, texture.height, new Vector2(texture.width * 0.5f, texture.height * 0.5f), printSettings, placedCells);
        }

        public SandBlock PrintSpriteBlock(Sprite sprite, PrintSettings printSettings, string[] allowedMediums = null) {
            var placed = new List<Vector2Int>();
            if (PrintSpriteWorld(sprite, printSettings, placed) == 0) return null;
            return new SandBlock(placed, allowedMediums);
        }

        // ---------------------------------------------------------------
        // READING THE SPRITE
        // ---------------------------------------------------------------
        static bool TryReadSprite(Sprite sprite, out Color32[] pixels, out int w, out int h, out Vector2 pivot) {
            pixels = null; w = h = 0; pivot = Vector2.zero;

            try {
                Rect r = sprite.packed ? sprite.textureRect : sprite.rect;
                int rx = Mathf.RoundToInt(r.x), ry = Mathf.RoundToInt(r.y);
                w = Mathf.RoundToInt(r.width);
                h = Mathf.RoundToInt(r.height);

                Color[] raw = sprite.texture.GetPixels(rx, ry, w, h); // bottom-left origin, row-major (same as the grid)
                pixels = new Color32[raw.Length];
                for (int i = 0; i < raw.Length; i++) pixels[i] = raw[i];

                pivot = sprite.pivot; // pixels, relative to the sprite's bottom-left
                return true;
            }
            catch (System.Exception e) {
                Debug.LogWarning($"WARNING: can't read sprite '{sprite.name}' (enable Read/Write in its texture's import settings, and use an uncompressed format). cancelling operation.\n{e.Message}");
                return false;
            }
        }

        // ---------------------------------------------------------------
        // PRINTING
        // ---------------------------------------------------------------
        int Print(Color32[] pixels, int w, int h, Vector2 pivot, PrintSettings s, List<Vector2Int> placedCells) {
            // ---- validation ----
            if (s == null) { Debug.LogWarning("WARNING: printSettings is null. cancelling operation."); return 0; }

            Simulation sim = Simulation.Instance;
            if (sim == null || sim.Layout == null) { Debug.LogWarning("WARNING: no running Simulation found. cancelling operation."); return 0; }

            if (s.elementArray == null || s.elementArray.Length == 0) { Debug.LogWarning("WARNING: elementArray is empty. cancelling operation."); return 0; }

            if (s.colorPrintMode == PrintSettings.ColorPrintMode.CustomDye && s.printDyeColor == null) {
                Debug.LogWarning("WARNING: the dye color is not set, despite using ColorPrintMode.CustomDye. cancelling operation."); return 0;
            }

            if (!sim.InBounds(Mathf.FloorToInt(s.printPosition.x), Mathf.FloorToInt(s.printPosition.y))) {
                Debug.LogWarning("WARNING: the printPosition points out of world bounds. cancelling operation."); return 0;
            }

            ElementPicker picker = BuildPicker(s, w, h);
            if (picker == null) return 0;

            // ---- placement ----
            int worldW = sim.Layout.CellsX, worldH = sim.Layout.CellsY;
            int tx = Mathf.FloorToInt(s.printPosition.x + s.printOffset.x); // the cell the pivot pixel lands on
            int ty = Mathf.FloorToInt(s.printPosition.y + s.printOffset.y);

            // pivot snapped to the centre of its pixel, so pixel centres and cell centres line up (no half-cell jitter)
            int pivotPxX = Mathf.Clamp(Mathf.FloorToInt(pivot.x), 0, w - 1);
            int pivotPxY = Mathf.Clamp(Mathf.FloorToInt(pivot.y), 0, h - 1);
            float pivotX = pivotPxX + 0.5f, pivotY = pivotPxY + 0.5f;

            float rad = s.printRotation * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

            // bounding box of the rotated sprite, relative to the target cell
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++) {
                float ax = ((i == 1 || i == 2) ? w : 0) - pivotX;
                float ay = (i >= 2 ? h : 0) - pivotY;
                float rx = cos * ax - sin * ay;
                float ry = sin * ax + cos * ay;
                if (rx < minX) minX = rx; if (rx > maxX) maxX = rx;
                if (ry < minY) minY = ry; if (ry > maxY) maxY = ry;
            }

            int x0 = Mathf.Max(0, tx + Mathf.FloorToInt(minX)), x1 = Mathf.Min(worldW - 1, tx + Mathf.CeilToInt(maxX));
            int y0 = Mathf.Max(0, ty + Mathf.FloorToInt(minY)), y1 = Mathf.Min(worldH - 1, ty + Mathf.CeilToInt(maxY));

            // ---- print: for every world cell in the box, look up which sprite pixel lands there (inverse rotation) ----
            int placed = 0;
            for (int cy = y0; cy <= y1; cy++) {
                for (int cx = x0; cx <= x1; cx++) {
                    float rx = cx - tx, ry = cy - ty;
                    int sx = Mathf.FloorToInt(pivotX + cos * rx + sin * ry);
                    int sy = Mathf.FloorToInt(pivotY - sin * rx + cos * ry);
                    if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;

                    Color32 pixel = pixels[sy * w + sx];
                    if (pixel.a < s.alphaThreshold) continue;

                    if (!s.overwriteExisting && !sim.IsAtmosphere(cx, cy)) continue;

                    ushort id = picker.Pick(sx, sy, pixel);
                    sim.SetCell(cx, cy, id);
                    ApplyColor(sim, cx, cy, id, pixel, s);
                    placed++;
                    placedCells?.Add(new Vector2Int(cx, cy));
                }
            }
            return placed;
        }

        // ---------------------------------------------------------------
        // COLORS
        // ---------------------------------------------------------------
        void ApplyColor(Simulation sim, int x, int y, ushort id, Color32 pixel, PrintSettings s) {
            switch (s.colorPrintMode) {
                case PrintSettings.ColorPrintMode.SpriteColor: {
                        Color32 c = pixel;
                        if (s.retainOriginalBrightnessDifferences)
                            c = WithGrain(c, sim.GetCell(x, y).color, id, s.brightnessDifferenceMultiplier);
                        sim.PaintCell(x, y, c);
                        break;
                    }
                case PrintSettings.ColorPrintMode.CustomDye: {
                        // white = the exact dye color, darker pixels = darker dye
                        sim.PaintCell(x, y, Scale(s.printDyeColor.Value, Luma(pixel)));
                        break;
                    }
                    // ElementColor: the cell keeps the shade SetCell rolled for it
            }
        }

        /// <summary>
        /// Keeps the element's own grain. The cell got a random shade of its element when it was placed; how far that shade
        /// is from the element's average brightness is applied to the sprite color, scaled by `multiplier`
        /// (0 = flat sprite color, 1 = the element's full grain).
        /// </summary>
        Color32 WithGrain(Color32 spriteColor, Color32 cellColor, ushort id, float multiplier) {
            float reference = GrainReference(id);
            if (reference <= 0f) return spriteColor;

            float cellV = Mathf.Max(cellColor.r, Mathf.Max(cellColor.g, cellColor.b)) / 255f;
            float factor = Mathf.Max(0f, 1f + (cellV / reference - 1f) * multiplier);
            return Scale(spriteColor, factor);
        }

        readonly Dictionary<ushort, float> grainReference = new Dictionary<ushort, float>();

        /// <summary>
        /// Average brightness (HSV value, 0..1) over an element's shades. Mirrors ElementDatabase.BuildShadeColors,
        /// so update this if the way shades are generated changes.
        /// </summary>
        float GrainReference(ushort id) {
            if (grainReference.TryGetValue(id, out float cached)) return cached;

            ref readonly Element e = ref ElementDatabase.Get(id);
            int n = Mathf.Max(1, (int)e.shadeCount);
            bool custom = e.customColor && e.customColors != null && e.customColors.Length > 0;
            float baseV = Mathf.Max(e.baseColor.r, Mathf.Max(e.baseColor.g, e.baseColor.b)) / 255f;

            float sum = 0f;
            for (int s = 0; s < n; s++) {
                if (custom) {
                    Color32 c = e.customColors[Mathf.Min(s, e.customColors.Length - 1)];
                    sum += Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
                }
                else sum += baseV * (1f - s * 0.04f);
            }

            float mean = sum / n;
            grainReference[id] = mean;
            return mean;
        }

        static float Luma(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        static Color32 Scale(Color32 c, float f) => new Color32(ToByte(c.r * f), ToByte(c.g * f), ToByte(c.b * f), c.a);

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

        // ---------------------------------------------------------------
        // ELEMENT PICKING
        // ---------------------------------------------------------------
        ElementPicker BuildPicker(PrintSettings s, int w, int h) {
            var ids = new List<ushort>();
            var keys = new List<Color32?>();

            foreach (var entry in s.elementArray) {
                ushort id = 0;
                bool found = !string.IsNullOrEmpty(entry.Item2) && ElementDatabase.TryFind(entry.Item2, out id);
                if (!found) {
                    Debug.LogWarning($"WARNING: unknown element '{entry.Item2}' in elementArray. skipping it.");
                    continue;
                }
                ids.Add(id);
                keys.Add(entry.Item1);
            }

            if (ids.Count == 0) { Debug.LogWarning("WARNING: none of the elements in elementArray exist. cancelling operation."); return null; }
            return new ElementPicker(s, ids.ToArray(), keys.ToArray(), w, h);
        }

        /// <summary>Decides which element a given sprite pixel becomes, according to the print settings.</summary>
        sealed class ElementPicker
        {
            readonly ushort[] ids;                 // the valid elementArray entries, in order
            readonly bool single;                  // SingleElement: always ids[0]
            readonly PrintSettings.ElementDistributionMode mode;
            readonly System.Random rng = new System.Random();

            // Blocks: one random element per blockSize x blockSize group of sprite pixels
            readonly int blockSize, blocksX;
            readonly int[] blockPick;

            // Color: sprite color -> element
            readonly int tolerance;
            readonly Color32[] keyColors;
            readonly ushort[] keyElements;
            readonly Dictionary<int, ushort> colorCache = new Dictionary<int, ushort>();

            public ElementPicker(PrintSettings s, ushort[] ids, Color32?[] keys, int w, int h) {
                this.ids = ids;
                single = s.elementPrintMode == PrintSettings.ElementPrintMode.SingleElement || ids.Length == 1;
                mode = s.elementDistributionMode;
                if (single) return;

                if (mode == PrintSettings.ElementDistributionMode.Blocks) {
                    blockSize = Mathf.Max(1, s.blockSize);
                    blocksX = (w + blockSize - 1) / blockSize;
                    int blocksY = (h + blockSize - 1) / blockSize;

                    blockPick = new int[blocksX * blocksY];
                    for (int i = 0; i < blockPick.Length; i++) blockPick[i] = rng.Next(ids.Length);
                }
                else if (mode == PrintSettings.ElementDistributionMode.Color) {
                    tolerance = Mathf.Max(0, s.colorMatchTolerance);

                    var kc = new List<Color32>();
                    var ke = new List<ushort>();
                    for (int i = 0; i < keys.Length; i++) {
                        if (!keys[i].HasValue) continue;
                        kc.Add(keys[i].Value);
                        ke.Add(ids[i]);
                    }
                    keyColors = kc.ToArray();
                    keyElements = ke.ToArray();

                    if (keyColors.Length == 0)
                        Debug.LogWarning("WARNING: ElementDistributionMode.Color is used but no elementArray entry has a color. every pixel will use the first element.");
                }
            }

            /// <param name="sx">sprite pixel x (in the un-rotated sprite)</param>
            /// <param name="sy">sprite pixel y</param>
            public ushort Pick(int sx, int sy, Color32 pixel) {
                if (single) return ids[0];

                switch (mode) {
                    case PrintSettings.ElementDistributionMode.Random:
                        return ids[rng.Next(ids.Length)];

                    case PrintSettings.ElementDistributionMode.Blocks:
                        return ids[blockPick[(sy / blockSize) * blocksX + sx / blockSize]];

                    default:
                        return PickByColor(pixel);
                }
            }

            /// <summary>Closest keyed color within tolerance wins (ties: the earlier entry). No match = the first element.</summary>
            ushort PickByColor(Color32 c) {
                int key = c.r | (c.g << 8) | (c.b << 16);
                if (colorCache.TryGetValue(key, out ushort cached)) return cached;

                ushort result = ids[0];
                int best = tolerance + 1; // a color only matches if its distance is <= tolerance
                for (int i = 0; i < keyColors.Length; i++) {
                    int d = Mathf.Max(Mathf.Abs(c.r - keyColors[i].r),
                            Mathf.Max(Mathf.Abs(c.g - keyColors[i].g), Mathf.Abs(c.b - keyColors[i].b)));
                    if (d < best) { best = d; result = keyElements[i]; }
                }

                colorCache[key] = result;
                return result;
            }
        }

        // ---------------------------------------------------------------
        // PRINT SETTINGS
        // ---------------------------------------------------------------
        public class PrintSettings
        {
            // PLACEMENT SETTINGS
            public Vector2 printPosition; // target position of the print (a cell; must be inside the world)
            public float printRotation;   // target rotation of the print (0-360, counter-clockwise). supports arbitrary angles.

            /// <summary>
            /// offset for the printPosition. allows to print the sprite in positions otherwise outside of bounds for printPosition.
            /// sprite will be cut off if non-alpha pixels extend out of world bounds
            /// </summary>
            public Vector2 printOffset = Vector2.zero;

            /// <summary>sprite pixels with an alpha below this (0-255) are treated as empty and are not printed.</summary>
            public byte alphaThreshold = 128;

            /// <summary>true = the print replaces whatever is already in the world. false = only fills empty space (air).</summary>
            public bool overwriteExisting = true;

            // ELEMENT SETTINGS
            /// <summary>
            /// SingleElement - uses one element for the whole sprite
            /// MultiElement - uses multiple elements
            /// </summary>
            public enum ElementPrintMode { SingleElement, MultiElement }
            public ElementPrintMode elementPrintMode;
            /// <summary>
            /// Random - elements are distributed randomly in a noise pattern
            /// Blocks - elements are distributed in square groups
            /// Color - element distribution is dictated by color. each color gets assigned an element
            /// colors without an assigned element will use the first one from the elementArray
            /// </summary>
            public enum ElementDistributionMode { Random, Blocks, Color } // EDM
            public ElementDistributionMode elementDistributionMode;
            /// <summary>
            /// element array to be used during printing.
            /// Color32 is nullable and may be left empty if EDM is not set to Color,
            /// otherwise, the referenced colors will use the element assigned to them here.
            /// </summary>
            public (Color32?, string)[] elementArray;

            /// <summary>side length (in sprite pixels) of one group when using ElementDistributionMode.Blocks.</summary>
            public int blockSize = 4;

            /// <summary>
            /// ElementDistributionMode.Color: how far (per channel, 0-255) a sprite color may be from an assigned color and
            /// still use its element. 0 = exact match only. raise it for sprites with anti-aliasing / compression noise.
            /// </summary>
            public int colorMatchTolerance = 0;

            // COLOR SETTINGS
            /// <summary>
            /// ElementColor - sprite colors will be ignored, and elements will not have their default color changed
            /// SpriteColor - sprite colors will be used to replace the default colors of the elements
            /// CustomDye - grayscale sprites will be dyed based on their brightness
            /// (customdye example): in case of yellow
            /// - white pixels will be dyed the exact color (yellow)
            /// - darker pixels will be dyed accordingly (darker yellow)
            /// </summary>
            public enum ColorPrintMode { ElementColor, SpriteColor, CustomDye }
            public ColorPrintMode colorPrintMode;
            /// <summary>
            /// the color used when dyeing the grayscale sprite.
            /// may leave empty if ColorPrintMode.CustomDye is not used
            /// </summary>
            public Color32? printDyeColor;
            /// <summary>
            /// whether to retain the uniform brightness/shade differences when using ColorPrintMode.SpriteColor
            /// </summary>
            public bool retainOriginalBrightnessDifferences = false;
            /// <summary>
            /// multiplier of the retained brightness/shade differences. 
            /// </summary>
            public float brightnessDifferenceMultiplier = 0.5f;
        }
    }
}