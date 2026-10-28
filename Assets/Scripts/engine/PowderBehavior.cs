using Unity.VisualScripting;
using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Sand, dirt, snow... Falls straight down. On landing:
    ///   - on a slope it slides down diagonally, unless its friction roll holds it in place (that grip is what
    ///     lets a powder keep a pile shape)
    ///   - otherwise part of the fall speed becomes sideways speed (<see cref="Forces.TransferImpact"/>), a small
    ///     splash, and friction then slows it down again
    ///
    /// Two hooks let subclasses (see <see cref="AshBehavior"/>) change the feel without copying Step:
    ///   - <see cref="ApplyForces"/>  which forces act on the cell each step
    ///   - <see cref="ToCells"/>      how a velocity becomes a whole number of cells to move
    /// </summary>
    internal class PowderBehavior : CellBehavior
    {
        protected readonly FlowSystem flowSystem;

        public PowderBehavior(World world, Forces forces, CellMover mover, FlowSystem flowSystem) : base(world, forces, mover)
        {
            this.flowSystem = flowSystem;
        }

        public override bool IsSettled(int x, int y, int idx)
        {
            int down = DownOf(idx);
            if (down == 0) return false;

            if (world.CanEnter(idx, x, y + down)) return false; // can still fall straight down
            if (!IsFree(idx)) return true;                      // at rest: straight down is the only move it tries

            int depth = SlideDepth(x, y, idx);
            return !(CanSlide(idx, x, y, down, -1, depth) || CanSlide(idx, x, y, down, 1, depth));
        }

        /// <summary>Forces applied before movement. x / y is the cell's position before it moves this step.</summary>
        protected virtual void ApplyForces(int x, int y, int idx)
        {
            forces.ApplyGravity(idx);
            if (InLiquid(x, y, DownOf(idx), out ushort liquid))
                forces.ApplyLiquidDrag(idx, liquid, flowSystem.VelocityAt(x, y));
            forces.ClampVelocity(idx);
        }

        /// <summary>The grain changed cells this step: it is free falling, and it shakes whatever rests beside it.</summary>
        void OnMoved(CellPos pos)
        {
            cells[pos.idx].freeFalling = true;
            mover.Disturb(pos.x, pos.y);
        }

        bool IsFree(int idx) =>
            cells[idx].freeFalling || ElementDatabase.Get(cells[idx].element).inertialResistance <= 0f;

        protected virtual int ToCells(float v) => world.RoundStochastic(v);
        //protected virtual int ToCells(float v) => Mathf.RoundToInt(v);

        public override void Step(int x, int y, int idx)
        {
            // ---- 1. forces ----
            ApplyForces(x, y, idx);

            // ---- 2. movement ----
            var pos = new CellPos(x, y, idx);
            int dir = world.RandomDir();
            bool landed = false;

            // --- vertical axis ---
            int dy = ToCells(cells[pos.idx].velocity.y);
            if (dy != 0 && !mover.MoveAxis(ref pos, 0, dy))
            {
                // blocked: impact
                landed = true;
                float impact = Mathf.Abs(cells[pos.idx].velocity.y);
                cells[pos.idx].velocity.y = 0f;
                int sy = dy > 0 ? 1 : -1;

                // only a moving grain looks diagonally: it travelled part of the way this step, or it is flagged free falling
                bool looksDiagonally = pos.idx != idx || IsFree(pos.idx);
                int depth = SlideDepth(pos.x, pos.y, pos.idx);

                if (looksDiagonally && !forces.FrictionRoll(pos.idx) &&
                    (TrySlopeDeep(ref pos, dir, sy, depth) || TrySlopeDeep(ref pos, -dir, sy, depth)))
                {
                    cells[pos.idx].velocity.x = 0f;
                    OnMoved(pos);
                    return;
                }

                Element el = ElementDatabase.Get(cells[pos.idx].element);

                float transfer = (el.velocityTransfer + Random.Range(-el.velocityTransferDeviation, el.velocityTransferDeviation)) *
                    SimulationSettings.PowderImpactTransferMultiplier *
                    SimulationSettings.ImpactTransferMultiplier;

                forces.TransferImpact(pos.idx, impact, dir, transfer);
            }

            // --- horizontal axis ---
            int dx = ToCells(cells[pos.idx].velocity.x);
            if (dx != 0 && !mover.MoveAxis(ref pos, dx, 0))
                cells[pos.idx].velocity.x = 0f;

            // ---- 3. friction (only acts while touching the ground) ----
            if (landed) forces.ApplyFriction(pos.idx);

            // ---- 4. inertia ----
            if (pos.idx != idx) OnMoved(pos);                           // it moved: free falling, shake the neighbours
            else if (landed) cells[pos.idx].freeFalling = false;        // hit something, couldn't slide or splash: at rest
        }

        /// <summary>How many free cells deep the neighbouring column must be before this grain slides into it.</summary>
        int SlideDepth(int x, int y, int idx)
        {
            float stick = 1f - ElementDatabase.Get(cells[idx].element).powderStackability;
            if (stick <= 0f) return 1;

            float extra = stick * SimulationSettings.PowderMaxExtraStack; // e.g. 0.4 * 3 = 1.2
            int whole = (int)extra;
            if (Hash01(x, y) < extra - whole) whole++; // fractional part: some spots hold one more
            return 1 + whole;
        }

        /// <summary>The whole column beside us, `depth` cells deep, is free.</summary>
        bool CanSlide(int idx, int x, int y, int down, int side, int depth)
        {
            for (int k = 1; k <= depth; k++)
                if (!world.CanEnter(idx, x + side, y + down * k)) return false;
            return true;
        }

        bool TrySlopeDeep(ref CellPos pos, int side, int sy, int depth)
        {
            for (int k = 2; k <= depth; k++) // k = 1 is checked by TrySlope itself
                if (!world.CanEnter(pos.idx, pos.x + side, pos.y + sy * k)) return false;
            return mover.TrySlope(ref pos, side, sy);
        }

        // stable per-position noise, so IsSettled and Step always agree
        static float Hash01(int x, int y)
        {
            uint h = (uint)x * 73856093u ^ (uint)y * 19349663u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFF) / 65536f;
        }
    }
}