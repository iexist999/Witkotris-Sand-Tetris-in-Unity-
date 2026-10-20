using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Stone, metal, glass... Fixed in place while connected to a SuperSolid (see <see cref="SupportSystem"/>).
    ///
    ///   - Anchored: never stepped, never woken, nothing can displace it (World.CanEnter refuses anchored cells).
    ///   - Free (lost or never had support): falls straight down with the normal gravity / speed cap.
    ///     The moment it touches an anchored cell it freezes again, together with every free solid touching it.
    ///
    /// A floating chunk is released all at once with the same velocity, and the scan visits every column bottom to
    /// top, so it falls in lockstep and keeps its shape in flight. On landing it freezes on first contact; an uneven
    /// underside can end up slightly squashed, because cells fall one by one rather than as a rigid body.
    /// </summary>
    internal sealed class SolidBehavior : CellBehavior
    {
        readonly SupportSystem support;

        public SolidBehavior(World world, Forces forces, CellMover mover, SupportSystem support)
            : base(world, forces, mover)
        {
            this.support = support;
        }

        public override bool IsSettled(int x, int y, int idx)
        {
            if (cells[idx].anchored) return true;          // fixed in place: nothing to do
            if (support.TouchesAnchor(x, y)) return false; // not free after all: Step will anchor it

            int down = DownOf(idx);
            if (down == 0) return false; // no vertical pull: movement is velocity-only, don't try to predict it

            return !world.CanEnter(idx, x, y + down);
        }

        public override void Step(int x, int y, int idx)
        {
            // touching support already (e.g. support was painted next to it): freeze instead of moving
            if (support.TouchesAnchor(x, y))
            {
                support.Anchor(x, y);
                return;
            }

            // ---- 1. forces ----
            forces.ApplyGravity(idx);
            forces.ClampVelocity(idx);

            // ---- 2. movement: straight down only, no sliding ----
            var pos = new CellPos(x, y, idx);

            int dy = Mathf.RoundToInt(cells[pos.idx].velocity.y);
            if (dy != 0 && !mover.MoveAxis(ref pos, 0, dy))
                cells[pos.idx].velocity.y = 0f; // blocked: landed

            // ---- 3. did it land on / next to support? ----
            if (support.TouchesAnchor(pos.x, pos.y))
                support.Anchor(pos.x, pos.y);
        }
    }
}