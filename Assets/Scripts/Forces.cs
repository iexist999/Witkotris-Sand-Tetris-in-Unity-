using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Everything that only edits Cell.velocity, never a position: gravity, friction, impact response and the speed cap.
    /// Behaviors decide WHEN to call these; <see cref="CellMover"/> turns the resulting velocity into actual movement.
    /// </summary>
    internal sealed class Forces
    {
        readonly World world;
        readonly Cell[] cells;

        public Forces(World world)
        {
            this.world = world;
            cells = world.Cells;
        }

        // ---------------------------------------------------------------
        // GRAVITY
        // ---------------------------------------------------------------
        public void ApplyGravity(int idx)
        {
            if (!SimulationSettings.SimulateGravity) return;

            ref Cell c = ref cells[idx];
            float influence = ElementDatabase.Get(c.element).gravityInfluence;

            // e.g. (0, -9.81) * 0.05 * 1.0 = (0, -0.49) added to the velocity every step
            c.velocity += SimulationSettings.SimulationGravity * SimulationSettings.SimulationGravityFactor * influence;
        }

        /// <summary>Caps both velocity axes at SimulationSettings.SimulationMaxVelocity. Call after the forces, before moving.</summary>
        public void ClampVelocity(int idx)
        {
            ref Cell c = ref cells[idx];
            float max = SimulationSettings.SimulationMaxVelocity;

            c.velocity.x = Mathf.Clamp(c.velocity.x, -max, max);
            c.velocity.y = Mathf.Clamp(c.velocity.y, -max, max);
        }

        // ---------------------------------------------------------------
        // FRICTION (both skipped when SimulationSettings.SimulateFriction is off)
        // ---------------------------------------------------------------

        /// <summary>
        /// Kinetic friction: slows the cell's sideways speed by its element's friction.
        /// Only meaningful while the cell is touching the ground, so the behavior decides when to call it.
        /// </summary>
        public void ApplyFriction(int idx)
        {
            if (!SimulationSettings.SimulateFriction) return;

            ref Cell c = ref cells[idx];
            c.velocity.x *= 1f - ElementDatabase.Get(c.element).friction;
        }

        /// <summary>
        /// Static friction: rolls the element's friction as a probability. True = grip wins this step
        /// (a powder stays on its slope, a thick liquid doesn't creep). Always false when friction is off.
        /// </summary>
        public bool FrictionRoll(int idx)
        {
            if (!SimulationSettings.SimulateFriction) return false;

            float friction = ElementDatabase.Get(cells[idx].element).friction;
            return (world.Rand() & 0xFFFF) < (uint)(friction * 65536f);
        }

        // ---------------------------------------------------------------
        // IMPACT
        // ---------------------------------------------------------------

        /// <summary>
        /// Partial velocity transfer. Called when a cell lands: a share of its fall speed (0..1) is turned into
        /// sideways speed. Each behavior passes its own share (SimulationSettings.LiquidImpactTransfer /
        /// PowderImpactTransfer), so water can splash much more than sand.
        ///
        ///   gain = impact * transfer
        ///   not moving sideways  -> vx = randomDir * gain         (left or right, random)
        ///   already moving       -> vx += sign(vx) * gain         (keeps its direction, gain is added on top)
        ///
        /// "Not moving sideways" means |vx| is at or below SimulationSettings.ImpactRestSpeed. Friction decays vx
        /// toward zero but never exactly to it, so an exact "== 0" check would almost never be true again.
        /// The fall speed itself (velocity.y) is zeroed by the caller.
        /// </summary>
        /// <param name="impact">fall speed at the moment of impact (always positive)</param>
        /// <param name="randomDir">-1 or +1, only used when the cell is not already moving sideways</param>
        /// <param name="transfer">share of the impact that becomes sideways speed, 0..1</param>
        public void TransferImpact(int idx, float impact, int randomDir, float transfer)
        {
            ref Cell c = ref cells[idx];

            float gain = impact * transfer;
            bool movingSideways = Mathf.Abs(c.velocity.x) > SimulationSettings.ImpactRestSpeed;
            float side = movingSideways ? Mathf.Sign(c.velocity.x) : randomDir;

            c.velocity.x += side * gain;
        }
    }
}