using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimulationSettings
{
    public static Vector2 SimulationGravity = new Vector2(0, -9.81f);
    public static float SimulationGravityFactor = 0.05f; // -9.81f * 0.1f = -0.981f

    public static float SimulationMaxVelocity = 200f;
}
