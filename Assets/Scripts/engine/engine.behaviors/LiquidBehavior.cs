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
    ///
    /// SURFACE TENSION (Element.surfaceTension, 0..1) is only about PUDDLES: small single-layer clumps of liquid
    /// lying on a flat liquid surface (see <see cref="IsPuddle"/> for the exact definition). Anything that is not a
    /// puddle, so slopes, steps, the top step of a hill, runs with more liquid stacked on them, the body itself,
    /// behaves exactly as if surfaceTension was 0. That is what keeps the surface levelling out.
    ///
    ///   1. Seek:  a loose cell (no neighbours) that starts creeping heads for the nearest puddle on its row within
    ///             reach, so scattered cells find each other and gather.
    ///   2. Stick: a cell stopped by a neighbour of its own liquid, while it is part of a puddle, stays there
    ///             instead of bouncing off.
    ///   3. Hold:  a creep-speed sideways step that would tear a cell off a puddle is cancelled.
    ///
    /// Seek / stick / hold are rolled against the element's grip (<see cref="Grip"/>), and the biggest puddle is
    /// surfaceTension * SurfaceTensionPuddleSize cells long, so higher tension = more and bigger puddles.
    ///
    /// Only surfaceTension = 1 is a hard lock; those puddle cells can fall asleep (see <see cref="IsSettled"/>).
    ///
    /// DISPERSION vs STREAMS. Sideways motion comes in two kinds, handled separately so one does not fake the other:
    ///
    ///   - Levelling (nothing liquid above the cell): the cell remembers a direction (velocity.x = +-1) but while it
    ///     travels along a surface or the ground it covers Element.spreadSpeed cells per step as DISPLACEMENT
    ///     (<see cref="SpreadsAlong"/>). Its velocity stays 1, so if it leaves the surface (a hole, an edge) it does not
    ///     carry spreadSpeed into the air.
    ///   - Pressure (liquid above the cell): a submerged cell that gets room to move leaves at
    ///     PressureFlowScale * sqrt(2 * gravity * head), where head = liquid cells stacked above it, i.e. the speed of
    ///     a cell that fell through that height. A hole just under the surface gives a trickle, a deep one a jet
    ///     (<see cref="StartCreep"/>).
    ///
    /// A cell that is merely resting re-lands every other step (gravity rounds to 0 cells on alternate steps) with the
    /// impact of two gravity steps. That is not a real impact, so it transfers no sideways speed (<see cref="RestImpact"/>);
    /// before this gate every landing pumped speed into cells sliding along a surface and they accelerated without limit.
    /// </summary>
    internal sealed class LiquidBehavior : CellBehavior
    {
        public LiquidBehavior(World world, Forces forces, CellMover mover) : base(world, forces, mover) { }

        public override bool IsSettled(int x, int y, int idx)
        {
            int down = DownOf(idx);
            if (down == 0) return false; // no vertical pull: movement is velocity-only, don't try to predict it

            if (CanFallOrSlide(idx, x, y, down)) return false;

            // liquids can also spread sideways (unless surface tension has locked the cell to its puddle for good)
            return !(CanSpread(idx, x, y, -1, down) || CanSpread(idx, x, y, 1, down));
        }

        bool CanSpread(int idx, int x, int y, int side, int down) =>
            world.CanEnter(idx, x + side, y) && !TensionLocks(idx, x, y, side, down);

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
                    mover.Disturb(pos.x, pos.y);
                    return;
                }

                Element el = ElementDatabase.Get(cells[pos.idx].element);

                float transfer = (el.velocityTransfer + Random.Range(-el.velocityTransferDeviation, el.velocityTransferDeviation)) *
                    SimulationSettings.LiquidImpactTransferMultiplier *
                    SimulationSettings.ImpactTransferMultiplier;

                // flat ground: part of the fall speed becomes sideways speed. a cell that is only resting does not
                // count (see RestImpact): without this gate cells sliding along a surface speed up every other step
                if (impact > RestImpact(cells[pos.idx].element))
                    forces.TransferImpact(pos.idx, impact, dir, transfer);
            }

            // --- horizontal axis ---
            int down = DownOf(pos.idx);
            bool surface = IsSurface(pos.x, pos.y, down); // surface tension only looks at the top layer of a liquid body

            // at rest on the ground: thin liquids keep creeping sideways, thick ones only on some steps.
            // StartCreep decides direction and speed: heading +-1 for a levelling cell, pressure speed for a submerged one
            if (landed && Mathf.RoundToInt(cells[pos.idx].velocity.x) == 0 && !forces.FrictionRoll(pos.idx))
                StartCreep(pos, dir, surface, down);

            int dx = Mathf.RoundToInt(cells[pos.idx].velocity.x);

            // surface tension, hold: a creeping step that would tear the cell off its puddle is cancelled.
            // anything faster than creep speed has enough momentum to break free (splashes still splash).
            if (surface && dx != 0 && Mathf.Abs(cells[pos.idx].velocity.x) < HoldSpeed
                && TensionHolds(pos, dx > 0 ? 1 : -1, down))
            {
                cells[pos.idx].velocity.x = 0f;
                dx = 0;
            }

            // spreadSpeed: along a surface or the ground the cell covers several cells per step, but its velocity stays dx
            int move = dx;
            if (dx != 0 && SpreadsAlong(pos, down)) move = dx * SpreadCells(cells[pos.idx].element);

            if (dx != 0 && !mover.MoveAxis(ref pos, move, 0))
            {
                // hit a wall or a denser neighbour: bounce. but if it was its own liquid, in a puddle, and surface
                // tension wins the roll, the two stick together instead
                if (surface && TensionSticks(pos, dx, down)) cells[pos.idx].velocity.x = 0f;
                else cells[pos.idx].velocity.x = -cells[pos.idx].velocity.x;
            }

            // ---- 3. friction (only acts while touching the ground) ----
            if (landed) forces.ApplyFriction(pos.idx);

            // disturb uneven powder piles
            if (pos.idx != idx) mover.Disturb(pos.x, pos.y);
        }

        // ---------------------------------------------------------------
        // SURFACE TENSION
        // ---------------------------------------------------------------

        /// <summary>
        /// Chance (0..1) that a puddle cell keeps hold each time something tries to pull it away.
        ///   grip = 1 - (1 - surfaceTension) ^ SurfaceTensionSharpness
        /// With the default sharpness of 2: tension 0.2 -> 0.36, 0.5 -> 0.75, 0.8 -> 0.96, 1 -> 1 (never lets go).
        /// </summary>
        float Grip(ushort el)
        {
            float t = Mathf.Clamp01(ElementDatabase.Get(el).surfaceTension);
            return 1f - Mathf.Pow(1f - t, SimulationSettings.SurfaceTensionSharpness);
        }

        // ---------------------------------------------------------------
        // DISPERSION AND PRESSURE
        // ---------------------------------------------------------------

        /// <summary>Sideways speed below which surface tension can still hold a cell back. Creep heading is 1, so this is 1 plus slack.</summary>
        const float HoldSpeed = 1.5f;

        /// <summary>Cells per step a levelling cell covers along a surface or the ground. Element.spreadSpeed rounded, never below 1.</summary>
        int SpreadCells(ushort el) => Mathf.Max(1, Mathf.RoundToInt(ElementDatabase.Get(el).spreadSpeedMultiplier));

        /// <summary>
        /// Impact speed of a cell that is only resting: it re-lands every other step after two steps of gravity.
        /// Landings at or below this (with a little slack) are not impacts and transfer no sideways speed.
        /// </summary>
        float RestImpact(ushort el) =>
            2f * Mathf.Abs(SimulationSettings.SimulationGravity.y) * SimulationSettings.SimulationGravityFactor *
            ElementDatabase.GravityScale(el) * 1.05f;

        /// <summary>
        /// True while the cell travels along something: nothing liquid directly above it (so it is not under pressure)
        /// and something it cannot fall into directly below it (liquid, ground, a wall). In free fall it is false.
        /// </summary>
        bool SpreadsAlong(CellPos pos, int down)
        {
            if (down == 0) return false;

            int above = pos.y - down;
            if (world.InBounds(pos.x, above) && world.Behaviors[cells[world.Index(pos.x, above)].element] == ElementBehavior.Liquid)
                return false;

            return !world.CanEnter(pos.idx, pos.x, pos.y + down);
        }

        /// <summary>Liquid cells stacked directly above (x, y), up to PressureMaxHead. This is the pressure head on the cell.</summary>
        int HeadAbove(int x, int y, int down)
        {
            int up = -down, head = 0, max = SimulationSettings.PressureMaxHead;
            for (int yy = y + up; head < max && world.InBounds(x, yy); yy += up)
            {
                if (world.Behaviors[cells[world.Index(x, yy)].element] != ElementBehavior.Liquid) break;
                head++;
            }
            return head;
        }

        /// <summary>
        /// A resting cell starts creeping sideways.
        ///   - liquid above it (submerged): pressure outflow. speed = PressureFlowScale * sqrt(2 * g * head), rounded
        ///     stochastically so low heads give a slow trickle instead of nothing. Direction is random; a cell facing
        ///     the wrong way bounces off and leaves next step.
        ///   - nothing liquid above (levelling): heading +-1, and a loose puddle cell picks the direction of the nearest
        ///     puddle (SurfaceCreepDir). SpreadsAlong turns the heading into spreadSpeed cells per step.
        /// </summary>
        void StartCreep(CellPos pos, int randomDir, bool surface, int down)
        {
            ushort el = cells[pos.idx].element;

            int head = down == 0 ? 0 : HeadAbove(pos.x, pos.y, down);
            if (head > 0 && SimulationSettings.PressureFlowScale > 0f)
            {
                float g = Mathf.Abs(SimulationSettings.SimulationGravity.y) * SimulationSettings.SimulationGravityFactor *
                          ElementDatabase.GravityScale(el);
                float speed = Mathf.Min(SimulationSettings.PressureMaxSpeed,
                                        SimulationSettings.PressureFlowScale * Mathf.Sqrt(2f * g * head));
                cells[pos.idx].velocity.x = randomDir * world.RoundStochastic(speed);
                return;
            }

            cells[pos.idx].velocity.x = surface ? SurfaceCreepDir(pos, randomDir, down) : randomDir;
        }

        /// <summary>Longest run (in cells) that still counts as a puddle for this element.</summary>
        int PuddleLimit(ushort el) =>
            Mathf.RoundToInt(Mathf.Clamp01(ElementDatabase.Get(el).surfaceTension) * SimulationSettings.SurfaceTensionPuddleSize);

        bool Roll(float chance) => (world.Rand() & 0xFFFF) < (uint)(chance * 65536f);

        bool IsSame(ushort el, int x, int y) =>
            world.InBounds(x, y) && cells[world.Index(x, y)].element == el;

        bool IsFreeSpace(int x, int y)
        {
            ElementBehavior b = world.Behaviors[cells[world.Index(x, y)].element];
            return b == ElementBehavior.Atmosphere || b == ElementBehavior.Gas;
        }

        /// <summary>Top layer of a liquid body: liquid directly below, free space (air / gas) directly above.</summary>
        bool IsTopLayer(int x, int y, int down)
        {
            int below = y + down, above = y - down;
            if (!world.InBounds(x, below) || !world.InBounds(x, above)) return false;

            if (world.Behaviors[cells[world.Index(x, below)].element] != ElementBehavior.Liquid) return false;
            return IsFreeSpace(x, above);
        }

        /// <summary>Master switch + gravity check + top layer. Everything surface tension does starts here.</summary>
        bool IsSurface(int x, int y, int down) =>
            SimulationSettings.SimulateSurfaceTension && down != 0 && IsTopLayer(x, y, down);

        /// <summary>A free (air / gas) cell with liquid directly below it: the surface continues one layer lower there.</summary>
        bool IsLowerFlank(int x, int y, int down)
        {
            if (!world.InBounds(x, y) || !world.InBounds(x, y + down)) return false;
            if (!IsFreeSpace(x, y)) return false;
            return world.Behaviors[cells[world.Index(x, y + down)].element] == ElementBehavior.Liquid;
        }

        /// <summary>
        /// "Puddle": the run of the same liquid on one row that contains (x, y), where
        ///   - the run is short: at most surfaceTension * SurfaceTensionPuddleSize cells,
        ///   - every cell of it is top layer (liquid below, nothing stacked on top),
        ///   - both ends are bounded by a free cell one layer lower (air with liquid below it),
        ///   - and the surface stays level for SurfaceTensionFlatMargin cells beyond both ends (<see cref="IsLevelBeyond"/>).
        /// So: a small clump lying on a flat liquid surface. A slope or staircase never qualifies (one end is higher, or it
        /// ends at a wall), neither does the top step of a hill against a wall, or a run with liquid stacked on
        /// part of it, and neither does the top of a mound (the surface drops away right next to it). Those keep
        /// levelling exactly like they do without surface tension.
        /// </summary>
        bool IsPuddle(ushort el, int x, int y, int down)
        {
            int limit = PuddleLimit(el);
            if (limit < 1) return false;

            int left = x, right = x;
            while (IsSame(el, left - 1, y)) { left--; if (right - left + 1 > limit) return false; }
            while (IsSame(el, right + 1, y)) { right++; if (right - left + 1 > limit) return false; }

            for (int i = left; i <= right; i++)
                if (!IsTopLayer(i, y, down)) return false;

            return IsLowerFlank(left - 1, y, down) && IsLowerFlank(right + 1, y, down)
                && IsLevelBeyond(left - 1, y, -1, down) && IsLevelBeyond(right + 1, y, 1, down);
        }

        /// <summary>
        /// Walks SurfaceTensionFlatMargin cells away from a puddle end (starting past its flank cell) and checks that the
        /// surface there is level: liquid at the puddle's base layer, nothing stacked above the puddle layer. A solid
        /// or the world edge ends the walk and counts as level. Without this, the top plateau of a mound (and every layer
        /// of it, as the mound wears down) looked like a puddle and was held in place, which slowed levelling a lot.
        /// </summary>
        bool IsLevelBeyond(int flankX, int y, int side, int down)
        {
            int baseRow = y + down, upRow = y - down;
            int margin = SimulationSettings.SurfaceTensionFlatMargin;

            for (int k = 1; k <= margin; k++)
            {
                int x = flankX + side * k;
                if (!world.InBounds(x, y)) return true; // world edge

                ElementBehavior here = world.Behaviors[cells[world.Index(x, y)].element];
                if (here == ElementBehavior.Solid || here == ElementBehavior.SuperSolid) return true; // wall

                if (world.Behaviors[cells[world.Index(x, baseRow)].element] != ElementBehavior.Liquid) return false; // surface drops here
                if (!world.InBounds(x, upRow) || !IsFreeSpace(x, upRow)) return false;                                // stacked higher here
            }
            return true;
        }

        /// <summary>
        /// True if a one-cell step toward `side` would leave the cell with fewer same-liquid neighbours (left / right)
        /// than it has now. A loose cell has nothing to lose, so it never detaches. The old spot is empty after the
        /// step, so the only possible new neighbour is the cell two away (that is how a cell can hop a 1-cell gap
        /// between two puddle runs without counting as a detach).
        /// </summary>
        bool Detaches(ushort el, int x, int y, int side)
        {
            int here = (IsSame(el, x - 1, y) ? 1 : 0) + (IsSame(el, x + 1, y) ? 1 : 0);
            if (here == 0) return false;

            int there = IsSame(el, x + 2 * side, y) ? 1 : 0;
            return there < here;
        }

        /// <summary>Hold: the step would tear the cell off a puddle and the grip roll succeeds.</summary>
        bool TensionHolds(CellPos pos, int side, int down)
        {
            ushort el = cells[pos.idx].element;
            return Detaches(el, pos.x, pos.y, side)
                && IsPuddle(el, pos.x, pos.y, down)
                && Roll(Grip(el));
        }

        /// <summary>Stick: stopped by a neighbour of its own liquid, the cell is part of a puddle, and the grip roll succeeds.</summary>
        bool TensionSticks(CellPos pos, int dx, int down)
        {
            ushort el = cells[pos.idx].element;
            return IsSame(el, pos.x + (dx > 0 ? 1 : -1), pos.y)
                && IsPuddle(el, pos.x, pos.y, down)
                && Roll(Grip(el));
        }

        /// <summary>
        /// True if surface tension makes a step toward `side` impossible, not just unlikely. Needs grip 1
        /// (surfaceTension 1), because anything less leaves a chance that the roll fails. Geometry only, no rolls,
        /// so <see cref="IsSettled"/> can use it. Mirrors the conditions Step uses for Hold.
        /// </summary>
        bool TensionLocks(int idx, int x, int y, int side, int down)
        {
            ushort el = cells[idx].element;
            if (Mathf.Abs(cells[idx].velocity.x) >= HoldSpeed) return false; // fast enough to tear free, Step only holds creep-speed steps

            return Grip(el) >= 1f
                && IsSurface(x, y, down)
                && Detaches(el, x, y, side)
                && IsPuddle(el, x, y, down);
        }

        /// <summary>
        /// Direction a resting surface cell starts creeping in. A loose cell (no neighbours) that lies on flat liquid
        /// heads for the nearest puddle on its row within reach (reach = surfaceTension * SurfaceTensionReach cells).
        /// Touching liquid already, not on flat liquid (it may be levelling out), nothing in reach, or a tie:
        /// the usual random direction.
        /// </summary>
        int SurfaceCreepDir(CellPos pos, int randomDir, int down)
        {
            ushort el = cells[pos.idx].element;

            // already touching its own liquid: nothing to look for, the grip rolls decide whether it stays
            if (IsSame(el, pos.x - 1, pos.y) || IsSame(el, pos.x + 1, pos.y)) return randomDir;

            float t = Mathf.Clamp01(ElementDatabase.Get(el).surfaceTension);
            int reach = Mathf.RoundToInt(t * SimulationSettings.SurfaceTensionReach);
            if (reach < 2) return randomDir; // reach 1 would only see a neighbour, and that case is handled above

            // only a cell lying on flat liquid takes part. on a slope or a step it behaves normally and keeps levelling
            if (!IsPuddle(el, pos.x, pos.y, down)) return randomDir;

            int left = DistanceToPuddle(pos.idx, el, pos.x, pos.y, -1, reach, down);
            int right = DistanceToPuddle(pos.idx, el, pos.x, pos.y, 1, reach, down);
            if (left == right) return randomDir; // nothing in reach on either side, or an exact tie

            return left < right ? -1 : 1;
        }

        /// <summary>
        /// Distance along the row toward `side` to the first cell of the same liquid, if that cell belongs to a puddle.
        /// int.MaxValue if it is not a puddle, if something the cell could not move through is in the way first
        /// (wall, denser stuff), or if nothing is within reach.
        /// </summary>
        int DistanceToPuddle(int idx, ushort el, int x, int y, int side, int reach, int down)
        {
            for (int k = 1; k <= reach; k++)
            {
                int nx = x + side * k;
                if (IsSame(el, nx, y)) return IsPuddle(el, nx, y, down) ? k : int.MaxValue;
                if (!world.CanEnter(idx, nx, y)) return int.MaxValue;
            }
            return int.MaxValue;
        }
    }
}