using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Water : LiquidElement
{
    public override bool useCustomColorData => true;
    public override Color[] colorData => new Color[]
    {
                    new Color(0.1f, 0.4f, 0.505f),
                    new Color(0.1f, 0.4f, 0.51f),
                    new Color(0.1f, 0.4f, 0.515f),
                    new Color(0.1f, 0.4f, 0.52f),
                    new Color(0.1f, 0.4f, 0.525f),
    };

    public Water(Vector2Int localPos, Vector2Int chunkPos) : base(localPos, chunkPos)
    {
        if (useCustomColorData && colorData != null && colorData.Length > 0)
        {
            this.localColor = colorData[Random.Range(0, colorData.Length - 1)];
        }
    }

    // properties
    public override float density => 0.5f;
}
