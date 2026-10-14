using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public class SimulationSettings
{
    public static Vector2 SimulationGravity = new Vector2(0, -9.81f);
    public static float SimulationGravityFactor = 0.05f; // -9.81f * 0.1f = -0.981f

    public static float SimulationMaxVelocity = 200f;
    

    //Defaults for World Settings
    public static int DefaultWorldChunkWidth = 16;
    public static int DefaultWorldChunkHeight = 16;

    public static int DefaultWorldAreaWidth = 64; //16 * 4 = 64
    public static int DefaultWorldAreaHeight = 64; //16 * 4 = 64

    public static int DefaultWorldZoneWidth = 6; // areas in a zone horizontally
    public static int DefaultWorldZoneHeight = 4; // areas in a zone vertically
    // this means a zone is 6 * 64 = 384 wide; 4 * 64 = 256 tall = 384x256 cells
    // render distance in game should refer to the amount of zones to load around camera center
}
