using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;
using static SandSimulation;
using Random = UnityEngine.Random;

public class SandSimulation : MonoBehaviour
{
    public static SandSimulation Instance { get; set; }
    public readonly System.Random globalRandom = new System.Random();

    public enum ChunkGenerationMode
    {
        TilemapOutline,
        TilemapRooms,
        Independent // not implemented yet
    }

    public enum ChunkUpdateMode
    {
        Sequential,
        FourSample, //todo: fix this pos
    }

    [Header("Simulation Settings")]
    public bool initOnStart = false;
    public bool isSimPaused = false;
    public float updateInterval = 0.1f; //how often to update (in seconds)
    public float gravity = 0.2f;
    public int chunkSize = 16;
    public ChunkGenerationMode chunkGenerationMode = ChunkGenerationMode.TilemapOutline;
    public ChunkUpdateMode chunkUpdateMode = ChunkUpdateMode.Sequential;

    [Header("Interaction Settings")]
    public bool enableDrawing = false;
    public string selectedElement = "sand";
    [Range(0, 15)] public float drawingBrushSize = 1;
    public bool interactionUpdatesEnvironment = true;

    [Header("References")]
    public SimulationRenderer simulationRenderer;
    public Tilemap collisionTilemap;
    public Tilemap boundaryTilemap;
    [HideInInspector] public Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();

    [Header("Debug")]
    public bool drawDebug = false;
    public bool renderChunkBorders = true;
    public bool showActiveChunks = true;
    public bool renderElements = false;

    // other
    [HideInInspector] public int frameCount;

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if(initOnStart) InitSimulation();
    }

    private float timeSinceLastUpdate = 0f;
    void Update()
    {
        if (!isSimPaused)
        {
            timeSinceLastUpdate += Time.deltaTime;

            if (timeSinceLastUpdate >= updateInterval)
            {
                UpdateChunks();
                timeSinceLastUpdate = 0f;
            }
        }

        if (enableDrawing && Input.GetMouseButton(0))
        {
            CreateElementsAtMousePos();
        }

        if (enableDrawing && Input.GetMouseButton(1))
        {
            DiscardElementsAtMousePos();
        }
    }

    // INPUT LOGIC

    private void CreateElementsAtMousePos()
    {
        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        int checkRadius = Mathf.CeilToInt(drawingBrushSize);
        for (int xOffset = -checkRadius; xOffset <= checkRadius; xOffset++)
        {
            for (int yOffset = -checkRadius; yOffset <= checkRadius; yOffset++)
            {
                Vector2 checkPos = new Vector2(
                    mouseWorldPos.x + xOffset / (float)chunkSize,
                    mouseWorldPos.y + yOffset / (float)chunkSize
                );

                float distance = Vector2.Distance(mouseWorldPos, checkPos);
                if (distance > drawingBrushSize / (float)chunkSize) continue;

                Vector2Int cellPos = new Vector2Int(
                    Mathf.FloorToInt(checkPos.x),
                    Mathf.FloorToInt(checkPos.y)
                );

                Vector2Int chunkPos = new Vector2Int(
                    Mathf.FloorToInt(cellPos.x),
                    Mathf.FloorToInt(cellPos.y)
                );

                if (!chunks.ContainsKey(chunkPos))
                {
                    continue;
                }

                Vector2Int localPos = new Vector2Int(
                    Mathf.RoundToInt((checkPos.x - chunkPos.x) * chunkSize),
                    Mathf.RoundToInt((checkPos.y - chunkPos.y) * chunkSize)
                );

                localPos.x = Mathf.Clamp(localPos.x, 0, chunkSize - 1);
                localPos.y = Mathf.Clamp(localPos.y, 0, chunkSize - 1);

                Vector3Int tilePosition = new Vector3Int(
                    localPos.x / chunkSize + chunkPos.x,
                    localPos.y / chunkSize + chunkPos.y,
                    0
                );

                if (collisionTilemap.HasTile(tilePosition))
                {
                    continue;
                }

                if (chunks[chunkPos].elements[localPos.x, localPos.y] == null)
                {
                    chunks[chunkPos].AddElement(localPos, selectedElement);
                    if(interactionUpdatesEnvironment) chunks[chunkPos].WakeUpElementsNeighbors(localPos, chunkPos);
                }
            }
        }
    }

    private void DiscardElementsAtMousePos()
    {
        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        int checkRadius = Mathf.CeilToInt(drawingBrushSize);
        for (int xOffset = -checkRadius; xOffset <= checkRadius; xOffset++)
        {
            for (int yOffset = -checkRadius; yOffset <= checkRadius; yOffset++)
            {
                Vector2 checkPos = new Vector2(
                    mouseWorldPos.x + xOffset / (float)chunkSize,
                    mouseWorldPos.y + yOffset / (float)chunkSize
                );

                float distance = Vector2.Distance(mouseWorldPos, checkPos);
                if (distance > drawingBrushSize / (float)chunkSize) continue;

                Vector2Int cellPos = new Vector2Int(
                    Mathf.FloorToInt(checkPos.x),
                    Mathf.FloorToInt(checkPos.y)
                );

                Vector2Int chunkPos = new Vector2Int(
                    Mathf.FloorToInt(cellPos.x),
                    Mathf.FloorToInt(cellPos.y)
                );

                Vector2Int localPos = new Vector2Int(
                    Mathf.RoundToInt((checkPos.x - chunkPos.x) * chunkSize),
                    Mathf.RoundToInt((checkPos.y - chunkPos.y) * chunkSize)
                );

                localPos.x = Mathf.Clamp(localPos.x, 0, chunkSize - 1);
                localPos.y = Mathf.Clamp(localPos.y, 0, chunkSize - 1);

                if (chunks.ContainsKey(chunkPos))
                {
                    Chunk chunk = chunks[chunkPos];
                    if (chunk.elements[localPos.x, localPos.y] != null)
                    {
                        chunk.WakeUpElementsNeighbors(localPos, chunkPos); //reqiured
                        if(interactionUpdatesEnvironment) chunk.WakeUpAllChunkNeighbors(true);
                        chunk.elements[localPos.x, localPos.y] = null;

                        // Check if the chunk is now empty
                        bool hasParticles = false;
                        for (int x = 0; x < chunk.chunkSize; x++)
                        {
                            for (int y = 0; y < chunk.chunkSize; y++)
                            {
                                if (chunk.elements[x, y] != null)
                                {
                                    hasParticles = true;
                                    break;
                                }
                            }
                            if (hasParticles) break;
                        }
                        chunk.isActive = hasParticles;
                    }
                }
            }
        }
    }

    /*private void EvacutableElementsAtMousePos()
    {
        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        Vector2Int chunkPos = new Vector2Int(
            Mathf.FloorToInt(mouseWorldPos.x),
            Mathf.FloorToInt(mouseWorldPos.y)
        );

        Debug.Log($"Attempting to evacuate chunk at {chunkPos}");

        EvacuateChunk(chunkPos);
    }*/

    [ContextMenu("Init Simulation")]
    public void InitSimulation()
    {
        if (chunkGenerationMode == ChunkGenerationMode.TilemapOutline)
        {
            BoundsInt bounds = boundaryTilemap.cellBounds;

            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    Vector2Int chunkPosition = new Vector2Int(x, y);
                    chunks[chunkPosition] = new Chunk(chunkPosition, chunkSize);
                }
            }
        }else if (chunkGenerationMode == ChunkGenerationMode.TilemapRooms)
        {
            BoundsInt bounds = boundaryTilemap.cellBounds;

            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    Vector2Int chunkPosition = new Vector2Int(x, y);
                    if(boundaryTilemap.GetTile(new Vector3Int(chunkPosition.x, chunkPosition.x, 0)) != null)
                    {
                        chunks[chunkPosition] = new Chunk(chunkPosition, chunkSize);
                    }
                }
            }
        }

        simulationRenderer.enabled = true;
    }

    public void UpdateChunks()
    {
        frameCount++;

        if (chunkUpdateMode == ChunkUpdateMode.Sequential)
        {
            foreach (var chunk in chunks.Values)
            {
                if (chunk.isActive == false) continue;
                chunk.UpdateElements();
            }
        }
        else if(chunkUpdateMode == ChunkUpdateMode.FourSample)
        {
            foreach (var chunk in chunks.Values)
            {
                if (chunk.isActive == false) continue;
                if (chunk.chunkPosition.x % 2 != 0 && chunk.chunkPosition.y % 2 != 0) continue;
                chunk.UpdateElements();
            }

            foreach (var chunk in chunks.Values)
            {
                if (chunk.isActive == false) continue;
                if (chunk.chunkPosition.x % 2 == 0 && chunk.chunkPosition.y % 2 == 0) continue;
                chunk.UpdateElements();
            }

            foreach (var chunk in chunks.Values)
            {
                if (chunk.isActive == false) continue;
                if (chunk.chunkPosition.x % 2 != 0 && chunk.chunkPosition.y % 2 == 0) continue;
                chunk.UpdateElements();
            }

            foreach (var chunk in chunks.Values)
            {
                if (chunk.isActive == false) continue;
                if (chunk.chunkPosition.x % 2 == 0 && chunk.chunkPosition.y % 2 != 0) continue;
                chunk.UpdateElements();
            }
        }
    }

    public void EvacuateChunk(Vector2Int chunkPosition)
    {
        if (!chunks.ContainsKey(chunkPosition))
        {
            Debug.LogWarning($"Attempted to evacuate non-existent chunk at {chunkPosition}");
            return;
        }

        Chunk sourceChunk = chunks[chunkPosition];

        Vector2Int[] directions = new Vector2Int[]
        {
            new Vector2Int(1, 0),   // right
            new Vector2Int(1, 1),   // top-right
            new Vector2Int(0, 1),   // top
            new Vector2Int(-1, 1),  // top-left
            new Vector2Int(-1, 0),  // left
            new Vector2Int(-1, -1), // bottom-left
            new Vector2Int(0, -1),  // bottom
            new Vector2Int(1, -1)   // bottom-right
        };

        List<Element> elementsToMove = new List<Element>();
        for (int x = 0; x < sourceChunk.chunkSize; x++)
        {
            for (int y = 0; y < sourceChunk.chunkSize; y++)
            {
                if (sourceChunk.elements[x, y] != null)
                {
                    elementsToMove.Add(sourceChunk.elements[x, y]);
                    sourceChunk.elements[x, y] = null;
                }
            }
        }

        if (elementsToMove.Count == 0) return;

        List<(Vector2Int pos, Chunk chunk)> neighbors = new List<(Vector2Int, Chunk)>();
        foreach (Vector2Int dir in directions)
        {
            Vector2Int neighborPos = chunkPosition + dir;
            if (chunks.TryGetValue(neighborPos, out Chunk neighborChunk))
            {
                neighbors.Add((neighborPos, neighborChunk));
            }
        }

        const int maxAttempts = 3;

        foreach (var element in elementsToMove)
        {
            bool placed = false;
            int attempts = 0;

            while (!placed && attempts < maxAttempts)
            {
                // Randomly select a neighbor chunk
                int neighborIndex = Random.Range(0, neighbors.Count);
                var (neighborPos, neighborChunk) = neighbors[neighborIndex];

                // Try a random position in the chunk
                int x = Random.Range(0, neighborChunk.chunkSize);
                int y = Random.Range(0, neighborChunk.chunkSize);

                // Check if position is valid
                Vector3Int tilePosition = new Vector3Int(
                    x / neighborChunk.chunkSize + neighborPos.x,
                    y / neighborChunk.chunkSize + neighborPos.y,
                    0);

                if (!collisionTilemap.HasTile(tilePosition) && neighborChunk.elements[x, y] == null)
                {
                    element.localPosition = new Vector2Int(x, y);
                    element.chunkPosition = neighborPos;
                    neighborChunk.elements[x, y] = element;
                    neighborChunk.isActive = true;
                    placed = true;
                }

                attempts++;
            }

            if (!placed)
            {
                Debug.LogWarning($"Particle destroyed during evacuation after {maxAttempts} failed placement attempts");
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (chunks == null) return;
        if(!drawDebug) return;


        float scale = 1f / chunkSize;

        foreach (var chunk in chunks.Values)
        {
            if(SandSimulation.Instance.renderChunkBorders && !SandSimulation.Instance.showActiveChunks)
            {
                Gizmos.color = Color.red;
                Vector3 chunkWorldPos = new Vector3(chunk.chunkPosition.x, chunk.chunkPosition.y, 0);
                Gizmos.DrawWireCube(chunkWorldPos + Vector3.one * 0.5f, Vector3.one);
            }
            else if (SandSimulation.Instance.showActiveChunks && !SandSimulation.Instance.renderChunkBorders)
            {
                Chunk ch;
                Gizmos.color = Color.green;
                Vector3 chunkWorldPos = new Vector3(chunk.chunkPosition.x, chunk.chunkPosition.y, 0);
                chunks.TryGetValue(SandSimulationHelper.WorldToChunkPosition(chunkWorldPos), out ch);

                if(ch != null)
                {
                    if (ch.isActive)
                    {
                        Gizmos.DrawWireCube(chunkWorldPos + Vector3.one * 0.5f, Vector3.one);
                    }
                }
            }
            else if (SandSimulation.Instance.showActiveChunks && SandSimulation.Instance.renderChunkBorders)
            {
                Chunk ch;
                Vector3 chunkWorldPos = new Vector3(chunk.chunkPosition.x, chunk.chunkPosition.y, 0);
                chunks.TryGetValue(SandSimulationHelper.WorldToChunkPosition(chunkWorldPos), out ch);

                if (ch != null)
                {
                    if (ch.isActive)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawWireCube(chunkWorldPos + Vector3.one * 0.5f, Vector3.one);
                    }
                    else if (!ch.isActive)
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawWireCube(chunkWorldPos + Vector3.one * 0.5f, Vector3.one);
                    }
                }
            }

            if (SandSimulation.Instance.renderElements)
            {
                // Draw particles
                for (int x = 0; x < chunk.chunkSize; x++)
                {
                    for (int y = 0; y < chunk.chunkSize; y++)
                    {
                        Element particle = chunk.elements[x, y];
                        if (particle != null)
                        {
                            float worldX = chunk.chunkPosition.x + (x * scale);
                            float worldY = chunk.chunkPosition.y + (y * scale);
                            Vector3 worldPos = new Vector3(worldX, worldY, 0);

                            Gizmos.color = Color.yellow;
                            Gizmos.DrawCube(worldPos + (Vector3.one * scale * 0.5f), Vector3.one * scale * 0.8f);
                        }
                    }
                }
            }
        }
    }
#endif    
}

