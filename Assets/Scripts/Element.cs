using UnityEngine;

namespace SandBXDSIM
{
    struct Element
    {
        // === Identity
        public string name;
        public ElementType type;

        // === Physical Properties
        public float density;
        public float friction;
        public float velocityTransfer;
        public float velocityTransferDeviation;

        // === Chemical Properties
        public float temperature;
        public float thermalConductivity;

        // === Appearance Settings
        public bool customColor;
        public byte shadeCount;
        public Color32 baseColor;
        public Color32[] customColors;

        // === Liquid Specific Properties
        public float viscosity;
        public float vorticity;
        public float cohesion;

        // === Other Properties
        public float gravityInfluence;

        // === Behavior
        public ElementBehavior behavior;

        public Element(/*Identity*/string name, ElementType type,
            /*Physical Properties*/byte density, float friction, float velocityTransfer, float velocityTransferDeviation,
            /*Chemical Properties*/float temperature, float thermalConductivity,
            /*Appearance*/bool customColor, byte shadeCount, Color32 baseColor, Color32[] customColors,
            /*Liquid Specific Properties*/ float viscosity, float vorticity, float cohesion,
            /*Other*/float gravityInfluence)
        {
            // Identity
            this.name = name;
            this.type = type;

            // Physical Properties
            this.density = density;
            this.friction = friction;
            this.velocityTransfer = velocityTransfer;
            this.velocityTransferDeviation = velocityTransferDeviation;

            // Chemical Properties
            this.temperature = temperature;
            this.thermalConductivity = thermalConductivity;

            // Appearance
            this.customColor = customColor;
            this.shadeCount = (byte)Mathf.Max(1, shadeCount);
            this.baseColor = baseColor;
            this.customColors = customColors;

            // Liquid Specific Properties
            this.viscosity = viscosity;
            this.vorticity = vorticity;
            this.cohesion = cohesion;

            // Other Properties
            this.gravityInfluence = gravityInfluence;

            // Behavior
            this.behavior = BehaviorFor(type);
        }

        static ElementBehavior BehaviorFor(ElementType type)
        {
            switch (type)
            {
                case ElementType.Atmosphere: return ElementBehavior.Atmosphere;
                case ElementType.SuperSolid: return ElementBehavior.SuperSolid;
                case ElementType.Solid: return ElementBehavior.Solid;
                case ElementType.Powder: return ElementBehavior.Powder;
                case ElementType.Liquid: return ElementBehavior.Liquid;
                case ElementType.Gas: return ElementBehavior.Gas;
                default: return ElementBehavior.Custom; // Plasma etc.
            }
        }
    }
}