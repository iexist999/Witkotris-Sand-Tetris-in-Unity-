using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SandSimulationHelper : MonoBehaviour
{
    // Convert a tilemap position to chunk position
    public static Vector2Int TilemapToChunkPosition(Vector3Int tilemapPosition)
    {
        return new Vector2Int(tilemapPosition.x, tilemapPosition.y);
    }

    // Convert a world position to chunk position
    public static Vector2Int WorldToChunkPosition(Vector2 worldPosition)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x),
            Mathf.FloorToInt(worldPosition.y)
        );
    }

    // Convert a world position to local position within a chunk
    public static Vector2Int WorldToLocalPosition(Vector2 worldPosition, int chunkSize)
    {
        Vector2Int chunkPos = WorldToChunkPosition(worldPosition);

        // Calculate local position within the chunk
        Vector2Int localPos = new Vector2Int(
            Mathf.RoundToInt((worldPosition.x - chunkPos.x) * chunkSize),
            Mathf.RoundToInt((worldPosition.y - chunkPos.y) * chunkSize)
        );

        // Clamp to chunk bounds
        localPos.x = Mathf.Clamp(localPos.x, 0, chunkSize - 1);
        localPos.y = Mathf.Clamp(localPos.y, 0, chunkSize - 1);

        return localPos;
    }

    // Convert chunk and local position to world position
    public static Vector2 ChunkToWorldPosition(Vector2Int chunkPos, Vector2Int localPos, int chunkSize)
    {
        return new Vector2(
            chunkPos.x + (float)localPos.x / chunkSize,
            chunkPos.y + (float)localPos.y / chunkSize
        );
    }

    // Convert a local position within a chunk to a world position
    public static Vector2 LocalToWorldPosition(Vector2Int localPos, Vector2Int chunkPos, int chunkSize)
    {
        // Calculate the world position
        return new Vector2(
            chunkPos.x + (float)localPos.x / chunkSize,
            chunkPos.y + (float)localPos.y / chunkSize
        );
    }
}
