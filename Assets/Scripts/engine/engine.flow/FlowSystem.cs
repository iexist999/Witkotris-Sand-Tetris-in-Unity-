using UnityEngine;
using static SimulationSettings;

namespace SandBXDSIM
{
    /// <summary>
    /// Replaces AirField as the one object the rest of the simulation talks to. Owns the <see cref="MediumMap"/> and
    /// one <see cref="FlowField"/> per medium (air, water), and routes every call to the field that owns the cell.
    /// The two fields never exchange flow: the boundary between the mediums is a wall for both.
    ///
    /// API is the old AirField's (VelocityAt / Impulse / Step) plus <see cref="Push"/> for cells that drag the flow.
    /// </summary>
    internal sealed class FlowSystem
    {
        readonly MediumMap map;
        readonly FlowField air, water;
        readonly SimulationSettings.FlowSettings airSettings, waterSettings;
        readonly int width;

        public FlowSystem(World world)
        {
            width = world.Width;
            map = new MediumMap(world);

            airSettings = SimulationSettings.AirFlow;
            waterSettings = SimulationSettings.WaterFlow;

            air = new FlowField(world, map, MediumMap.Air, ResolveElement(world, airSettings), airSettings);
            water = new FlowField(world, map, MediumMap.Water, ResolveElement(world, waterSettings), waterSettings);
        }

        /// <summary>The element whose properties (vorticity) describe a field's medium. Null / unknown name = the world's medium.</summary>
        static ushort ResolveElement(World world, SimulationSettings.FlowSettings settings)
        {
            if (string.IsNullOrEmpty(settings.element)) return world.Medium;
            if (ElementDatabase.TryFind(settings.element, out ushort id)) return id;

            Debug.LogWarning($"FlowSystem: unknown flow element '{settings.element}', using the world medium instead.");
            return world.Medium;
        }

        // ---------------------------------------------------------------
        // READ
        // ---------------------------------------------------------------

        /// <summary>Flow at a cell, from whichever medium it sits in (sand in a pool reads the water current, sand on a beach reads wind).</summary>
        public Vector2 VelocityAt(int x, int y)
        {
            switch (map.kind[y * width + x])
            {
                case MediumMap.Air: return air.VelocityAt(x, y);
                case MediumMap.Water: return water.VelocityAt(x, y);
                default: return Vector2.zero;
            }
        }

        public float PressureAt(int x, int y)
        {
            switch (map.kind[y * width + x])
            {
                case MediumMap.Air: return air.PressureAt(x, y);
                case MediumMap.Water: return water.PressureAt(x, y);
                default: return 0f;
            }
        }

        // ---------------------------------------------------------------
        // WRITE
        // ---------------------------------------------------------------

        /// <summary>Raw impulse (mouse, tools): goes to the medium of the cell, unscaled.</summary>
        public void Impulse(int x, int y, Vector2 vel)
        {
            map.EnsureFresh(); // after painting while both fields slept, the map may be out of date
            switch (map.kind[y * width + x])
            {
                case MediumMap.Air: air.Impulse(x, y, vel); break;
                case MediumMap.Water: water.Impulse(x, y, vel); break;
            }
        }

        /// <summary>A moving cell dragging the flow around it (CellMover): like Impulse, scaled by the medium's own coupling.</summary>
        public void Push(int x, int y, Vector2 vel)
        {
            map.EnsureFresh();
            switch (map.kind[y * width + x])
            {
                case MediumMap.Air: Stir(air, airSettings, x, y, vel); break;
                case MediumMap.Water: Stir(water, waterSettings, x, y, vel); break;
            }
        }

        static void Stir(FlowField field, FlowSettings s, int x, int y, Vector2 vel)
        {
            if (vel.sqrMagnitude < s.pushThreshold * s.pushThreshold) return;
            field.Impulse(x, y, vel * s.coupling);
        }

        // ---------------------------------------------------------------
        // STEP
        // ---------------------------------------------------------------
        public void Step()
        {
            if (!air.IsActive && !water.IsActive) return; // both asleep: no map rebuild, no solve

            map.EnsureFresh();
            air.Step();
            water.Step();
        }
    }
}