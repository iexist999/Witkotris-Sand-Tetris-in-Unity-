using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static SandSimulation;

public abstract class Element
{
    public Vector2Int localPosition;
    public Vector2Int chunkPosition;
    public bool isResting;
    public bool isSleeping;
    public float verticalVelocity;
    public float horizontalVelocity;
    public bool hasContactBelow;
    public bool hasContactAbove;
    // virtual properties
    public virtual float maxVelocity => 5f;  // Maximum fall speed
    public virtual float velocityAbsorption => 0.5f; // How much velocity is retained on impact (0-1)
    public virtual float maxHorizontalVelocity => 3f; // Maximum horizontal speed
    public virtual float horizontalDrag => 0.2f; // Base drag in air
    public virtual float friction => 0.4f; // Additional slowdown when touching surfaces

    //color data
    public virtual bool useCustomColorData => false;
    public virtual Color[] colorData => new Color[1] { new Color(1, 1, 1) };
    protected Color localColor = Color.white;
    public Color GetLocalColor() => localColor;

    //other
    public int lastUpdatedFrame = -1;
    public virtual bool NeedsUpdate => !isResting;


    public Element(Vector2Int localPos, Vector2Int chunkPos)
    {
        localPosition = localPos;
        chunkPosition = chunkPos;
        isResting = false;
        verticalVelocity = 0f;
        horizontalVelocity = 0f;
        hasContactBelow = false;
        hasContactAbove = false;
    }

    public Vector2Int GetGlobalPosition()
    {
        return chunkPosition * SandSimulation.Instance.chunkSize + localPosition;
    }

    public void ApplyFriction()
    {
        if (hasContactBelow || hasContactAbove)
        {
            // Apply additional slowdown when touching surfaces
            horizontalVelocity *= (1f - friction);
        }
    }

    public bool HasSupport(Chunk currentChunk)
    {
        Vector2Int below = new Vector2Int(localPosition.x, localPosition.y - 1);
        Vector2Int targetChunk = chunkPosition;

        if (below.y < 0)
        {
            below.y = currentChunk.chunkSize - 1;
            targetChunk.y -= 1;
        }

        if (SandSimulation.Instance.chunks.TryGetValue(targetChunk, out Chunk targetChunkObj))
        {
            Vector3Int tilePosition = new Vector3Int(
                below.x / currentChunk.chunkSize + targetChunk.x,
                below.y / currentChunk.chunkSize + targetChunk.y,
                0);

            return SandSimulation.Instance.collisionTilemap.HasTile(tilePosition) ||
                   targetChunkObj.elements[below.x, below.y] != null;
        }

        return false;
    }

    // Abstract method that must be implemented by derived classes
    public abstract void Simulate(Chunk currentChunk);
}