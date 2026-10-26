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
    /// FREE STACKS. Stones that are not held by an anchor pile up as blocks, not as infinite pixel towers.
    /// Anchored stones are not affected by any of this.
    ///
    ///   Width rule     A stack can only be so tall for how wide it is. Walking down the column from its top stone,
    ///                  the k+1 stones above a row may not exceed SolidStackRatio * (width of that row), where the
    ///                  width of a row is the run of solid cells on it. Ratio 3: a 1 wide tower holds 3, 2 wide 6,
    ///                  3 wide 9... and a pyramid, whose rows widen as they go down, is never a problem.
    ///                  Whatever is above the limit tips over: the top stones get a sideways kick (the top one the
    ///                  most), slide off the side and fall beside the stack. See <see cref="Overload"/>.
    ///
    ///   Hard landing   A stone that lands on a stack faster than SolidSafeImpact may shake it: odds go from 0 at
    ///                  SolidSafeImpact to 1 at SolidFullImpact, and at that speed the stack only keeps
    ///                  SolidShockStability of its normal limit. How much of the stack goes is capped at
    ///                  SolidCollapseFraction of it, so you get "the top quarter / half fell off". A wide mass
    ///                  barely notices, and a stack boxed in on both sides has nowhere to tip, so it holds.
    ///
    /// SLIDING. A stone with sideways speed (tipping) loses 1 cell/step of it every step and stops dead on a wall.
    /// Sideways speed is always a whole number of cells. A sliding stone is not part of a stack until it stops.
    /// </summary>
    internal sealed class SolidBehavior : CellBehavior
    {
        readonly SupportSystem support;
        readonly WakeSystem waker;

        public SolidBehavior(World world, Forces forces, CellMover mover, SupportSystem support, WakeSystem waker)
            : base(world, forces, mover)
        {
            this.support = support;
            this.waker = waker;
        }

        public override bool IsSettled(int x, int y, int idx)
        {
            if (cells[idx].anchored) return true;          // fixed in place: nothing to do
            if (support.TouchesAnchor(x, y)) return false; // not free after all: Step will anchor it
            if (cells[idx].velocity.x != 0f) return false; // sliding off a stack: Step has to run it

            int down = DownOf(idx);
            if (down == 0) return false; // no vertical pull: movement is velocity-only, don't try to predict it

            if (world.CanEnter(idx, x, y + down)) return false; // can still fall

            // resting: only a stack that is too tall for its width, and has room to tip over, keeps it awake
            return !MustTip(x, y, idx, down);
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

            // ---- 2. movement ----
            var pos = new CellPos(x, y, idx);
            float impact = 0f;

            // vertical: straight down
            int dy = Mathf.RoundToInt(cells[pos.idx].velocity.y);
            if (dy != 0 && !mover.MoveAxis(ref pos, 0, dy))
            {
                impact = Mathf.Abs(cells[pos.idx].velocity.y); // blocked: landed
                cells[pos.idx].velocity.y = 0f;
            }

            // sideways: only a stone that was tipped off a stack has any. It slows by 1 cell/step, stops dead on a wall.
            int dx = Mathf.RoundToInt(cells[pos.idx].velocity.x);
            if (dx != 0)
            {
                int sign = dx > 0 ? 1 : -1;
                bool clear = mover.MoveAxis(ref pos, dx, 0);
                cells[pos.idx].velocity.x = clear ? dx - sign : 0f;
            }

            // ---- 3. did it land on / next to support? ----
            if (support.TouchesAnchor(pos.x, pos.y))
            {
                support.Anchor(pos.x, pos.y);
                return;
            }

            // ---- 4. resting on other free stones: is the stack still standing? ----
            SettleStack(pos, impact);
        }

        // ---------------------------------------------------------------
        // STACKS
        // ---------------------------------------------------------------

        void SettleStack(CellPos pos, float impact)
        {
            if (cells[pos.idx].velocity.x != 0f) return; // sliding: not part of a stack until it stops

            int down = DownOf(pos.idx);
            if (down == 0) return;
            if (world.CanEnter(pos.idx, pos.x, pos.y + down)) return; // still in the air

            // A stone can reach the stack on exactly the step it arrives, without being "blocked" yet,
            // so the speed it still carries counts as impact too.
            impact = Mathf.Max(impact, Mathf.Abs(cells[pos.idx].velocity.y));

            // hard landing on a stack: may shake its top loose, however well it would stand otherwise
            if (impact > SimulationSettings.SolidSafeImpact && StandsOnSolid(pos, down))
            {
                float range = Mathf.Max(0.01f, SimulationSettings.SolidFullImpact - SimulationSettings.SolidSafeImpact);
                float severity = Mathf.Clamp01((impact - SimulationSettings.SolidSafeImpact) / range);

                // harder hit = better odds, and the stack keeps less of its stability
                float stability = Mathf.Lerp(1f, SimulationSettings.SolidShockStability, severity);
                if (Roll(severity) && TipOverload(pos, down, stability, severity, true)) return;
            }

            // otherwise it only tips if it is simply too tall for its width
            TipOverload(pos, down, 1f, 0f, false);
        }

        /// <summary>True if the cell directly under the stone is a Solid (so it landed on a stack, not on bare ground).</summary>
        bool StandsOnSolid(CellPos pos, int down)
        {
            int by = pos.y + down;
            return world.InBounds(pos.x, by) && world.Behaviors[cells[world.Index(pos.x, by)].element] == ElementBehavior.Solid;
        }

        /// <summary>
        /// Tips whatever is too much for the stack this stone stands in.
        /// stability scales SolidStackRatio (1 = normal, less = the stack was just hit).
        /// capToFraction limits how much goes to SolidCollapseFraction of the stack (hard landings only).
        /// </summary>
        bool TipOverload(CellPos pos, int down, float stability, float severity, bool capToFraction)
        {
            int count = Overload(pos.x, pos.y, down, stability, out int topY, out int height);
            if (count <= 0) return false;

            if (capToFraction)
            {
                float share = SimulationSettings.SolidCollapseFraction * (0.5f + 0.5f * Rand01());
                count = Mathf.Min(count, Mathf.Max(1, Mathf.RoundToInt(height * share)));
            }

            int dir = TipDir(pos.idx, pos.x, topY, down, count);
            if (dir == 0) return false; // boxed in on both sides: it stays up

            // sideways speed: the top stone gets the most, each stone further down a little less (never below 1)
            int top = SimulationSettings.SolidTipSpeed +
                      Mathf.RoundToInt(severity * (SimulationSettings.SolidCollapseSpeed - SimulationSettings.SolidTipSpeed));
            top = Mathf.Max(1, top);

            for (int i = 0; i < count; i++)
            {
                int cy = topY + down * i;
                int speed = Mathf.Max(1, Mathf.RoundToInt(top * (count - i) / (float)count));

                cells[world.Index(pos.x, cy)].velocity = new Vector2(dir * speed, 0f);
                waker.Wake(pos.x, cy); // asleep in a sleeping chunk, most likely
            }
            return true;
        }

        /// <summary>A free solid that is not sliding and would not move this step: it counts as part of the stack.</summary>
        bool IsStackStone(int x, int y)
        {
            ref Cell c = ref cells[world.Index(x, y)];
            return !c.anchored && c.velocity.x == 0f && Mathf.RoundToInt(c.velocity.y) == 0 &&
                   world.Behaviors[c.element] == ElementBehavior.Solid;
        }

        bool IsSolid(int x, int y) => world.Behaviors[cells[world.Index(x, y)].element] == ElementBehavior.Solid;

        /// <summary>
        /// Finds the column of resting free stones that (x, y) belongs to and returns how many of its TOP stones are
        /// more than the stack can carry (0 = it stands). topY is the row of the top stone, height the column's size.
        ///
        /// Row k (0 = top stone) carries the k + 1 stones above it, which must be at most
        /// stability * SolidStackRatio * (width of row k), the width being the run of solid cells on that row.
        /// If row k is short by n, n stones have to go, and since they go from the top, one count covers every row:
        ///   count = max over k of (k + 1 - allowed(row k))
        /// A row never needs more than ceil((k + 1) / ratio) cells to pass, so the width scan stops there.
        /// </summary>
        int Overload(int x, int y, int down, float stability, out int topY, out int height)
        {
            int up = -down;
            float ratio = SimulationSettings.SolidStackRatio * stability;

            topY = y;
            while (world.InBounds(x, topY + up) && IsStackStone(x, topY + up)) topY += up;

            int bottomY = y;
            while (world.InBounds(x, bottomY + down) && IsStackStone(x, bottomY + down)) bottomY += down;

            height = Mathf.Abs(topY - bottomY) + 1;
            if (height <= Mathf.Max(1, (int)ratio)) return 0; // even a 1 wide tower carries this much

            int count = 0;
            for (int k = 0; k < height; k++)
            {
                int cy = topY + down * k;
                int need = Mathf.CeilToInt((k + 1) / ratio);
                int allowed = Mathf.Max(1, (int)(ratio * RowWidth(x, cy, need)));
                count = Mathf.Max(count, k + 1 - allowed);
            }
            return count;
        }

        /// <summary>Run of solid cells on row y that contains x, counted up to `cap`.</summary>
        int RowWidth(int x, int y, int cap)
        {
            int w = 1;
            for (int nx = x - 1; w < cap && world.InBounds(nx, y) && IsSolid(nx, y); nx--) w++;
            for (int nx = x + 1; w < cap && world.InBounds(nx, y) && IsSolid(nx, y); nx++) w++;
            return w;
        }

        // ---------------------------------------------------------------
        // WHICH WAY IT TIPS
        // ---------------------------------------------------------------

        /// <summary>Same overload check as Step uses, plus "is there anywhere for it to go". Geometry only, no rolls.</summary>
        bool MustTip(int x, int y, int idx, int down)
        {
            int count = Overload(x, y, down, 1f, out int topY, out _);
            if (count <= 0) return false;

            TipRoom(idx, x, topY, down, count, out int left, out int right);
            return left + right > 0;
        }

        /// <summary>How many of the `count` top rows have a free cell right beside them, on each side.</summary>
        void TipRoom(int idx, int x, int topY, int down, int count, out int left, out int right)
        {
            left = right = 0;
            for (int i = 0; i < count; i++)
            {
                int cy = topY + down * i;
                if (world.CanEnter(idx, x - 1, cy)) left++;
                if (world.CanEnter(idx, x + 1, cy)) right++;
            }
        }

        /// <summary>Side (-1 / +1) with more free room. A tie is random, 0 = no room on either side.</summary>
        int TipDir(int idx, int x, int topY, int down, int count)
        {
            TipRoom(idx, x, topY, down, count, out int left, out int right);

            if (left == 0 && right == 0) return 0;
            if (left == right) return world.RandomDir();
            return left > right ? -1 : 1;
        }

        bool Roll(float chance) => (world.Rand() & 0xFFFF) < (uint)(chance * 65536f);

        float Rand01() => (world.Rand() & 0xFFFF) / 65536f;
    }
}