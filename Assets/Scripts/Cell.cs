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
        /// color shade index.
        /// </summary>
        public byte shade;
        /// <summary>
        /// last step that mofified this cell. prevents double moves in one step.
        /// </summary>
        public byte stamp;
        /// <summary>
        /// cell velocity.
        /// </summary>
        public Vector2 velocity;

        /// <summary>
        /// does this cell act as an world anchor for other connected cells
        /// </summary>
        public bool anchored;
        public bool awake;
    }
}