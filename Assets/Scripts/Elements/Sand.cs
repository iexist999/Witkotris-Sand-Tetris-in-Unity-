using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sand : PowderElement
{
    public override bool useCustomColorData => true;
    public override Color[] colorData => new Color[]
    {
            new Color(0.94f, 0.85f, 0.53f), // Light Beige
            new Color(0.87f, 0.76f, 0.46f), // Soft Yellow Sand
            new Color(0.91f, 0.77f, 0.47f), // Light Brown Sand
            new Color(0.81f, 0.67f, 0.35f), // Desert Sand
            new Color(0.74f, 0.62f, 0.37f), // Medium Sand
            new Color(0.85f, 0.72f, 0.48f), // Warm Sand
            new Color(0.78f, 0.61f, 0.39f), // Brownish Sand
            new Color(0.92f, 0.80f, 0.47f)  // Pale Sandy Yellow
    };

    public Sand(Vector2Int localPos, Vector2Int chunkPos) : base(localPos, chunkPos)
    {
        if (useCustomColorData && colorData != null && colorData.Length > 0)
        {
            this.localColor = colorData[Random.Range(0, colorData.Length - 1)];
        }
    }

    // properties
    public override float density => 0.8f;
}