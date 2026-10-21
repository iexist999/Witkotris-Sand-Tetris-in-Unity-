using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Where a cell currently is while it is being moved: grid x / y plus its flat index into World.Cells.
    /// Kept together and passed by ref so the three values can never drift apart as the cell swaps through the grid.
    /// </summary>
    internal struct CellPos
    {
        public int x, y, idx;

        public CellPos(int x, int y, int idx)
        {
            this.x = x;
            this.y = y;
            this.idx = idx;
        }
    }

    /// <summary>
    /// The only place where cells actually change position. Behaviors ask for a move, this class checks the path
    /// (<see cref="World.CanEnter"/>), swaps the cells and wakes whatever the move affected.
    /// </summary>
    internal sealed class CellMover
    {
        readonly World world;
        readonly WakeSystem waker;
        readonly AirField airField;
        readonly Cell[] cells;

        public CellMover(World world, WakeSystem waker, AirField airField)
        {
            this.world = world;
            this.waker = waker;
            this.airField = airField;
            cells = world.Cells;
        }

        /// <summary>
        /// moves along one axis as far as the path is clear.
        /// updates pos and returns true if the full distance was travelled.
        /// </summary>b
        public bool MoveAxis(ref CellPos pos, int dx, int dy)
        {
            int steps = Mathf.Abs(dx) + Mathf.Abs(dy);
            int sx = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int sy = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

            int free = 0;
            while (free < steps && world.CanEnter(pos.idx, pos.x + sx * (free + 1), pos.y + sy * (free + 1)))
                free++;

            if (free > 0)
            {
                int fromX = pos.x, fromY = pos.y;
                pos.x += sx * free;
                pos.y += sy * free;
                pos.idx = MoveTo(pos.idx, fromX, fromY, pos.x, pos.y);
            }
            return free == steps;
        }

        /// <summary>diagonal step for going down slopes.</summary>
        public bool TrySlope(ref CellPos pos, int dirX, int dirY)
        {
            int nx = pos.x + dirX, ny = pos.y + dirY;
            if (!world.CanEnter(pos.idx, nx, ny)) return false;

            pos.idx = MoveTo(pos.idx, pos.x, pos.y, nx, ny);
            pos.x = nx;
            pos.y = ny;
            return true;
        }

        /// <summary>
        /// Swaps the cell from (fx, fy) into (tx, ty) and returns its new index. The displaced cell ends up at the old spot.
        /// This is the single place where cells move, so it is also where waking happens.
        /// </summary>
        int MoveTo(int from, int fx, int fy, int tx, int ty)
        {
            int to = world.Index(tx, ty);
            world.Swap(from, to);
            cells[from].velocity = Vector2.zero; // the displaced cell doesn't inherit our momentum

            Vector2 v = cells[to].velocity;
            if (v.sqrMagnitude > 1f) airField.Impulse(tx, ty, v * SimulationSettings.WindCoupling);

            world.Changed = true;
            waker.MarkAwake(tx, ty);  // the mover is awake wherever it lands, maybe in a sleeping chunk
            waker.WakeAround(fx, fy); // the vacated spot, whatever got displaced into it, and everything touching it may now move
            return to;
        }
    }
}