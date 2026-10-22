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
        public PowderBehavior(World world, Forces forces, CellMover mover) : base(world, forces, mover) { }

        public override bool IsSettled(int x, int y, int idx)
        {
            int down = DownOf(idx);
            if (down == 0) return false; // no vertical pull: movement is velocity-only, don't try to predict it

            return !CanFallOrSlide(idx, x, y, down);
        }

        /// <summary>Forces applied before movement. x / y is the cell's position before it moves this step.</summary>
        protected virtual void ApplyForces(int x, int y, int idx)
        {
            forces.ApplyGravity(idx);
            if (InLiquid(x, y, DownOf(idx), out ushort liquid)) forces.ApplyLiquidDrag(idx, liquid);
            // future forces slot in here: forces.ApplyWind(idx); ...
            forces.ClampVelocity(idx);
        }

        protected virtual int ToCells(float v) => world.RoundStochastic(v);
        //protected virtual int ToCells(float v) => Mathf.RoundToInt(v);

        public override void Step(int x, int y, int idx)
        {
            // ---- 1. forces ----
            ApplyForces(x, y, idx);

            // ---- 2. movement ----
            var pos = new CellPos(x, y, idx); // where the cell currently is (it changes as it swaps)
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

                // slope: slides down it unless friction holds the powder in place
                if (!forces.FrictionRoll(pos.idx) && (mover.TrySlope(ref pos, dir, sy) || mover.TrySlope(ref pos, -dir, sy)))
                {
                    cells[pos.idx].velocity.x = 0f; // the slide used up its sideways speed
                    return;
                }

                Element el = ElementDatabase.Get(cells[pos.idx].element);

                float transfer = (el.velocityTransfer + Random.Range(-el.velocityTransferDeviation, el.velocityTransferDeviation)) *
                    SimulationSettings.PowderImpactTransferMultiplier *
                    SimulationSettings.ImpactTransferMultiplier;

                // held by friction, or flat ground: part of the fall speed becomes sideways speed (a small splash)
                forces.TransferImpact(pos.idx, impact, dir, transfer);
            }

            // --- horizontal axis ---
            // non-zero after an impact transfer, or when something else pushed it (wind for dust, explosions later)
            int dx = ToCells(cells[pos.idx].velocity.x);
            if (dx != 0 && !mover.MoveAxis(ref pos, dx, 0))
                cells[pos.idx].velocity.x = 0f; // hit a wall or a denser neighbour

            // ---- 3. friction (only acts while touching the ground) ----
            if (landed) forces.ApplyFriction(pos.idx);
        }
    }
}