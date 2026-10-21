using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Ash, soot, flour, pollen... A powder that the air pushes around. Landing, slopes and friction are
    /// inherited from <see cref="PowderBehavior"/>; this class only changes three things:
    ///
    ///   1. Forces: gravity, then drag toward the wind at the cell's position, then a little flutter.
    ///   2. Rounding: velocities below 0.5 cells/step still move (stochastic rounding), so slow-falling
    ///      dust drifts down instead of hanging in mid-air.
    ///   3. Sleeping: dust that is sitting in moving air never counts as settled.
    ///
    /// Needs: Element.drag / Element.flutter, Forces.ApplyDrag / ApplyFlutter, World.RoundStochastic and an AirField.
    /// </summary>
    internal sealed class AshBehavior : PowderBehavior
    {
        readonly AirField airField;

        public AshBehavior(World world, Forces forces, CellMover mover, AirField airField)
            : base(world, forces, mover)
        {
            this.airField = airField;
        }

        public override bool IsSettled(int x, int y, int idx)
        {
            // Powder's check is geometry only ("can I fall or slide?"). Dust lying on the ground has nowhere to
            // fall, so it would go to sleep right away even while wind is blowing over it.
            float wake = SimulationSettings.WindWakeSpeed;
            if (airField.VelocityAt(x, y).sqrMagnitude > wake * wake) return false;

            return base.IsSettled(x, y, idx);
        }

        // Order matters: gravity first, then drag pulls the result toward the air's velocity.
        // x / y are the cell's position BEFORE it moves this step, which is where the wind is read.
        protected override void ApplyForces(int x, int y, int idx)
        {
            forces.ApplyGravity(idx);
            forces.ApplyDrag(idx, airField.VelocityAt(x, y));
            forces.ApplyFlutter(idx);
            forces.ClampVelocity(idx);
        }

        protected override int ToCells(float v) => world.RoundStochastic(v);
    }
}