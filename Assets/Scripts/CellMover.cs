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
        readonly FlowSystem flowSystem;
        readonly Cell[] cells;

        public CellMover(World world, WakeSystem waker, FlowSystem flowSystem)
        {
            this.world = world;
            this.waker = waker;
            this.flowSystem = flowSystem;
            cells = world.Cells;
        }

        /// <summary>
        /// Moves along one axis as far as the path is clear AND the mover's speed can pay for it
        /// (see Element.densityInfluence). Updates pos and returns true if the full distance was travelled.
        /// </summary>
        public bool MoveAxis(ref CellPos pos, int dx, int dy)
        {
            int steps = Mathf.Abs(dx) + Mathf.Abs(dy);
            int sx = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int sy = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

            // power along this move; resistant cells charge part of it per cell entered
            float speed = dx != 0 ? Mathf.Abs(cells[pos.idx].velocity.x) : Mathf.Abs(cells[pos.idx].velocity.y);
            float start = ElementDatabase.ImpactPower(speed);
            float budget = start;

            int free = 0;
            while (free < steps && world.TryEnter(pos.idx, pos.x + sx * (free + 1), pos.y + sy * (free + 1), ref budget))
                free++;

            if (free > 0)
            {
                int fromX = pos.x, fromY = pos.y;
                pos.x += sx * free;
                pos.y += sy * free;
                pos.idx = MoveTo(pos.idx, fromX, fromY, pos.x, pos.y);

                // what was spent on resistant cells is gone from the mover's speed
                if (budget < start)
                {
                    float left = ElementDatabase.SpeedFromPower(budget);
                    ref Cell c = ref cells[pos.idx];
                    if (dx != 0) c.velocity.x = Mathf.Sign(c.velocity.x) * left;
                    else c.velocity.y = Mathf.Sign(c.velocity.y) * left;
                }
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
            cells[from].freeFalling = true; // the displaced cell changed cells too

            // a moving cell drags the air with it; airInfluence scales how hard
            Vector2 push = cells[to].velocity * ElementDatabase.Get(cells[to].element).airInfluence;
            //if (push.sqrMagnitude > 1f) flowSystem.Push(tx, ty, push);
            flowSystem.Push(tx, ty, push);

            world.Changed = true;
            waker.MarkAwake(tx, ty);  // the mover is awake wherever it lands, maybe in a sleeping chunk
            waker.WakeAround(fx, fy); // the vacated spot, whatever got displaced into it, and everything touching it may now move
            waker.WakeSlideReach(fx, fy, 1 + SimulationSettings.PowderMaxExtraStack);
            return to;
        }

        /// <summary>
        /// A powder that just moved shakes its 8 neighbours: every resting powder rolls against its own
        /// inertialResistance and, if it loses, becomes free falling and is woken so it can act on it.
        /// </summary>
        public void Disturb(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = x + dx, ny = y + dy;
                    if (!world.InBounds(nx, ny)) continue;

                    ref Cell n = ref cells[world.Index(nx, ny)];
                    if (n.freeFalling || n.anchored) continue;

                    ElementBehavior b = world.Behaviors[n.element];
                    if (b != ElementBehavior.Powder && b != ElementBehavior.Ash) continue;

                    float r = ElementDatabase.Get(n.element).inertialResistance;
                    if (r <= 0f) continue;                                       // no inertia: always free anyway
                    if ((world.Rand() & 0xFFFF) < (uint)(r * 65536f)) continue;  // it resisted

                    n.freeFalling = true;
                    waker.Wake(nx, ny); // a sleeping cell would never notice otherwise
                }
            }
        }
    }
}