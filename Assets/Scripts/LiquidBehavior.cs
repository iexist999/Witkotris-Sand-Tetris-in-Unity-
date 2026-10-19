using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Water, oil... Falls, slides down slopes, and on flat ground spreads sideways.
    ///
    ///   - Landing on a slope: slides down it and keeps a little momentum.
    ///   - Landing on flat ground: part of the fall speed becomes sideways speed (<see cref="Forces.TransferImpact"/>).
    ///   - Resting on the ground: thin liquids keep creeping sideways, thick ones (high friction) only on some steps.
    ///   - Hitting a wall or a denser neighbour sideways: bounces (velocity.x flips, takes effect next step).
    /// </summary>
    internal sealed class LiquidBehavior : CellBehavior
    {
        public LiquidBehavior(World world, Forces forces, CellMover mover) : base(world, forces, mover) { }

        public override bool IsSettled(int x, int y, int idx)
        {
            int down = DownOf(idx);
            if (down == 0) return false; // no vertical pull: movement is velocity-only, don't try to predict it

            if (CanFallOrSlide(idx, x, y, down)) return false;

            // liquids can also spread sideways
            return !(world.CanEnter(idx, x - 1, y) || world.CanEnter(idx, x + 1, y));
        }

        public override void Step(int x, int y, int idx)
        {
            // ---- 1. forces ----
            forces.ApplyGravity(idx);
            // future forces slot in here: forces.ApplyBuoyancy(idx); forces.ApplyPressure(idx); ...
            forces.ClampVelocity(idx);

            // ---- 2. movement ----
            var pos = new CellPos(x, y, idx); // where the cell currently is (it changes as it swaps)
            int dir = world.RandomDir();
            bool landed = false;

            // --- vertical axis ---
            int dy = Mathf.RoundToInt(cells[pos.idx].velocity.y);
            if (dy != 0 && !mover.MoveAxis(ref pos, 0, dy))
            {
                // blocked: impact
                landed = true;
                float impact = Mathf.Abs(cells[pos.idx].velocity.y);
                cells[pos.idx].velocity.y = 0f;
                int sy = dy > 0 ? 1 : -1;

                // slope: always slide down it and keep a little momentum
                int before = pos.x;
                if (mover.TrySlope(ref pos, dir, sy) || mover.TrySlope(ref pos, -dir, sy))
                {
                    if (cells[pos.idx].velocity.x == 0f) cells[pos.idx].velocity.x = pos.x - before;
                    return;
                }

                Element el = ElementDatabase.Get(cells[pos.idx].element);

                float transfer = (el.velocityTransfer + Random.Range(-el.velocityTransferDeviation, el.velocityTransferDeviation)) *
                    SimulationSettings.LiquidImpactTransferMultiplier *
                    SimulationSettings.ImpactTransferMultiplier;

                // flat ground: part of the fall speed becomes sideways speed
                forces.TransferImpact(pos.idx, impact, dir, transfer);
            }

            // --- horizontal axis ---
            // at rest on the ground: thin liquids keep creeping sideways, thick ones only on some steps
            if (landed && Mathf.RoundToInt(cells[pos.idx].velocity.x) == 0 && !forces.FrictionRoll(pos.idx))
                cells[pos.idx].velocity.x = dir;

            int dx = Mathf.RoundToInt(cells[pos.idx].velocity.x);
            if (dx != 0 && !mover.MoveAxis(ref pos, dx, 0))
                cells[pos.idx].velocity.x = -cells[pos.idx].velocity.x; // hit a wall or a denser neighbour: bounce

            // ---- 3. friction (only acts while touching the ground) ----
            if (landed) forces.ApplyFriction(pos.idx);
        }
    }
}