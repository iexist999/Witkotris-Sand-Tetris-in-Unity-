using System;
using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Registry of every concrete element. An element's id is its index here and is what
    /// Cell.element stores. ElementType is NOT a key: many elements can share one type
    /// (Sand, Dirt, Snow are all Powder; Water, Oil are Liquid; ...).
    /// Elements of type Atmosphere (Air, Vacuum, ...) are "mediums": real elements with real
    /// properties, but the simulation never steps them. Which one fills empty space is chosen
    /// by Simulation.defaultMedium.
    /// </summary>
    static class ElementDatabase
    {
        static Element[] elements = new Element[16];
        static int count;

        public static int Count => count;

        public static ref readonly Element Get(ushort id) => ref elements[id];

        public static bool TryFind(string name, out ushort id)
        {
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(elements[i].name, name, StringComparison.OrdinalIgnoreCase))
                {
                    id = (ushort)i;
                    return true;
                }
            }
            id = 0;
            return false;
        }

        static ushort Register(Element e)
        {
            if (count == elements.Length) Array.Resize(ref elements, count * 2);
            elements[count] = e;
            return (ushort)count++;
        }

        // Registration order = element id = number-key order in SimulationInput (Atmosphere types are skipped there).
        // NOTE: values are placeholders - tweak freely. Density decides who sinks below whom, so a medium's
        // density should stay below everything that is meant to fall through it.
        static ElementDatabase()
        {
            // - Air
            Register(/*Identity*/new Element("air", ElementType.Atmosphere,
                /*Physical Properties*/0, 0f,
                /*Chemical Properties*/20f, 0.02f,
                /*Appearance*/false, 1, new Color32(0, 0, 0, 0), null,
                /*Liquid Specific Properties*/0f, 0f, 0f,
                /*Other Properties*/1f));

            // - Sand
            Register(/*Identity*/new Element("sand", ElementType.Powder,
                /*Physical Properties*/150, 0.5f,
                /*Chemical Properties*/20f, 0.10f,
                /*Appearance*/false, 6, new Color32(222, 192, 110, 255), null,
                /*Liquid Specific Properties*/0f, 0f, 0f,
                /*Other Properties*/1f));

            // - Dirt
            Register(/*Identity*/new Element("dirt", ElementType.Powder,
                /*Physical Properties*/150, 0.7f,
                /*Chemical Properties*/20f, 0.10f,
                /*Appearance*/false, 5, new Color32(110, 75, 45, 255), null,
                /*Liquid Specific Properties*/0f, 0f, 0f,
                /*Other Properties*/1f));

            // - Water
            Register(/*Identity*/new Element("water", ElementType.Liquid,
                /*Physical Properties*/100, 0.5f,
                /*Chemical Properties*/20f, 0.50f,
                /*Appearance*/false, 4, new Color32(0, 0, 255, 255), null,
                /*Liquid Specific Properties*/0f, 0f, 0f,
                /*Other Properties*/1f));
        }
    }
}