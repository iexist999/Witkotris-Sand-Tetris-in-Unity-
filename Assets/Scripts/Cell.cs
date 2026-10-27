using UnityEngine;

namespace SandBXDSIM
{
    public struct Cell
    {
        /// <summary>
        /// element index lookup into elementdatabase.
        /// </summary>
        public ushort element;
        /// <summary>color shade index</summary>
        public byte shade;
        /// <summary>
        /// last step that mofified this cell. prevents double moves in one step.
        /// </summary>
        public byte stamp;
        /// <summary>velocity of this cell</summary>
        public Vector2 velocity;

        /// <summary>whether this cell is actively moving/falling.
        /// if false, certain calculations per step are limited to save resources.
        /// (varies by element type)</summary>
        public bool freeFalling;

        /// <summary>
        /// per-cell paint color. alpha dictates whether it gets used by the renderer. a=0=(not used)
        /// </summary>
        public Color32 paint;

        /// <summary>
        /// does this cell act as an world anchor for other connected cells
        /// </summary>
        public bool anchored;
        public bool awake;
    }
}