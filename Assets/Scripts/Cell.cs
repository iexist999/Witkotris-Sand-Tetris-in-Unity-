using UnityEngine;

namespace SandBXDSIM
{
    public struct Cell
    {
        public ushort element;  // id into ElementDatabase. Simulation fills new grids with its default medium
        public byte shade;      // color variation (0..shadeCount-1)
        public byte stamp;      // parity of the last step that touched this cell (prevents double moves)
        public Vector2 velocity; // cells/step. Forces change this, the movement pass consumes it. Travels with the cell on Swap.

        public bool awake;
    }
}