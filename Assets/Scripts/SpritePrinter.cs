using SandBXDSIM;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpritePrinter : MonoBehaviour
{


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void PrintSpriteWorld(SpritePrinter.PrintSettings printSettings)
    {
        if (printSettings != null) {

            if(printSettings.elementArray == null) { Debug.LogWarning("WARNING: elementArray is empty. cancelling operation."); return; }
            
            if(printSettings.colorPrintMode == PrintSettings.ColorPrintMode.CustomDye && printSettings.printDyeColor == null) {
                Debug.LogWarning("WARNING: the dye color is not set, despite using ColorPrintMode.CustomDye. cancelling operation."); return;
            }

            if(Simulation.Instance.InBounds((int)printSettings.printPosition.x, (int)printSettings.printPosition.y) == false) {
                Debug.LogWarning("WARNING: the printPosition points out of world bounds. cancelling operation."); return;
            }
        }
        else {
            Debug.LogWarning("WARNING: printSettings is null. cancelling operation.");
        }
    }

    // PRINT SETTINGS

    public class PrintSettings
    {
        // PLACEMENT SETTINGS
        public Vector2 printPosition; // target position of the print
        public float printRotation; // target rotation of the print (0-360). supports arbitrary angles.

        /// <summary>
        /// offset for the printPosition. allows to print the sprite in positions otherwise outside of bounds for printPosition.
        /// sprite will be cut off if non-alpha pixels extend out of world bounds
        /// </summary>
        public Vector2 printOffset = Vector2.zero;

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

        // COLOR SETTINGS
        /// <summary>
        /// ElementColor - sprite colors will be ignored, and elements will not have their default color changed
        /// SpriteColor - sprite colors will be used to replace the default colors of the elements
        /// CustomDye - grayscale sprites will be dyed based either or brightness
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


