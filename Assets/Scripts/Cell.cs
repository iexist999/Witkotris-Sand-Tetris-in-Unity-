using UnityEngine;

namespace SandBXDSIM
{
    public struct Cell
    {
        /// <summary>
        /// element index lookup into elementdatabase.
        /// </summary>
        public ushort element;
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

        public Color32 color;

        /// <summary>
        /// does this cell act as an world anchor for other connected cells
        /// </summary>
        public bool anchored;
        public int blockId; // 0 = none
        public bool awake;
    }
}