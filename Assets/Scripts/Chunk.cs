using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static SandSimulation;

public class Chunk
{
    public Vector2Int chunkPosition;
    public Element[,] elements;
    public int chunkSize;
    public bool isActive;
    public bool isActiveNextFrame;

    public Chunk(Vector2Int position, int size)
    {
        chunkPosition = position;
        chunkSize = size;
        elements = new Element[chunkSize, chunkSize];
        isActive = false;
        isActiveNextFrame = false;
    }

    public void FillWithElement(string particleType)
    {
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                if (elements[x, y] == null)
                {
                    AddElement(new Vector2Int(x, y), particleType);
                }
            }
        }
        isActive = true;
    }


    public float GetOccupancy()
    {
        int occupiedCells = 0;
        int totalCells = chunkSize * chunkSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                if (elements[x, y] != null)
                {
                    occupiedCells++;
                }
            }
        }

        return (float)occupiedCells / totalCells;
    }

    public void AddElement(Vector2Int localPos, string type = "sand")
    {
        if (localPos.x >= 0 && localPos.x < chunkSize && localPos.y >= 0 && localPos.y < chunkSize)
        {
            Element newParticle;
            switch (type.ToLower())
            {
                case "water":
                    newParticle = new Water(localPos, chunkPosition);
                    break;
                case "sand":
                default:
                    newParticle = new Sand(localPos, chunkPosition);
                    break;
            }
            elements[localPos.x, localPos.y] = newParticle;
            this.isActive = true;
            this.isActiveNextFrame = true;
        }
    }

    // Update particle behavior in this chunk
    public void UpdateElements()
    {
        if (!isActive) return;

        var sim = SandSimulation.Instance;
        int frame = sim.frameCount;
        bool leftToRight = (frame & 1) == 0;
        bool keepAlive = false;

        for (int i = 0; i < chunkSize; i++)
        {
            int x = leftToRight ? i : chunkSize - 1 - i;
            for (int y = 0; y < chunkSize; y++)
            {
                Element p = elements[x, y];
                if (p == null || p.lastUpdatedFrame == frame) continue;

                p.lastUpdatedFrame = frame;
                p.Simulate(this);

                if (p.chunkPosition != chunkPosition)
                {
                    // the destination stays awake
                    if (sim.chunks.TryGetValue(p.chunkPosition, out Chunk dest))
                    {
                        dest.isActive = true;
                        dest.isActiveNextFrame = true;
                    }
                }
                else if (p.NeedsUpdate)
                {
                    keepAlive = true;
                }
            }
        }

        isActive = isActiveNextFrame || keepAlive;
        isActiveNextFrame = false;
    }

    public void CheckElementContacts(Element particle)
    {
        Vector2Int[] contactPoints = new Vector2Int[]
        {
                new Vector2Int(particle.localPosition.x, particle.localPosition.y - 1), // below
                new Vector2Int(particle.localPosition.x, particle.localPosition.y + 1)  // above
        };

        particle.hasContactBelow = false;
        particle.hasContactAbove = false;

        foreach (Vector2Int contactPoint in contactPoints)
        {
            Vector2Int targetChunk = particle.chunkPosition;
            Vector2Int adjustedPos = contactPoint;

            if (adjustedPos.y < 0)
            {
                adjustedPos.y = chunkSize - 1;
                targetChunk.y -= 1;
            }
            else if (adjustedPos.y >= chunkSize)
            {
                adjustedPos.y = 0;
                targetChunk.y += 1;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(targetChunk, out Chunk targetChunkObj))
            {
                Vector3Int tilePosition = new Vector3Int(
                    adjustedPos.x / chunkSize + targetChunk.x,
                    adjustedPos.y / chunkSize + targetChunk.y,
                    0);

                bool hasContact = SandSimulation.Instance.collisionTilemap.HasTile(tilePosition) ||
                                targetChunkObj.elements[adjustedPos.x, adjustedPos.y] != null;

                if (contactPoint.y < particle.localPosition.y)
                {
                    particle.hasContactBelow = hasContact;
                }
                else
                {
                    particle.hasContactAbove = hasContact;
                }
            }
        }
    }


    public bool HasParticleType<T>() where T : Element
    {
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                if (elements[x, y] is T) return true;
            }
        }
        return false;
    }

    public float GetElementTypePercentage<T>() where T : Element
    {
        int count = 0;
        int totalCells = chunkSize * chunkSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                if (elements[x, y] is T) count++;
            }
        }

        return (float)count / totalCells;
    }

    public void WakeUpAllChunkNeighbors(bool diagonal = false)
    {
        if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y), out Chunk rightChunk))
        {
            rightChunk.isActive = true;
            rightChunk.isActiveNextFrame = true;
        }

        if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y), out Chunk leftChunk))
        {
            leftChunk.isActive = true;
            leftChunk.isActiveNextFrame = true;
        }

        if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x, this.chunkPosition.y + 1), out Chunk aboveChunk))
        {
            aboveChunk.isActive = true;
            aboveChunk.isActiveNextFrame = true;
        }

        if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x, this.chunkPosition.y - 1), out Chunk belowChunk))
        {
            belowChunk.isActive = true;
            belowChunk.isActiveNextFrame = true;
        }

        if (diagonal)
        {
            if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y + 1), out Chunk aboveRightChunk))
            {
                aboveRightChunk.isActive = true;
                aboveRightChunk.isActiveNextFrame = true;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y - 1), out Chunk belowRightChunk))
            {
                belowRightChunk.isActive = true;
                belowRightChunk.isActiveNextFrame = true;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y + 1), out Chunk aboveLeftChunk))
            {
                aboveLeftChunk.isActive = true;
                aboveLeftChunk.isActiveNextFrame = true;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y - 1), out Chunk belowLeftChunk))
            {
                belowLeftChunk.isActive = true;
                belowLeftChunk.isActiveNextFrame = true;
            }
        }
    }

    public void WakeUpChunkNeighbors(Vector2Int chunkPosition, bool diagonal = false)
    {
        Vector2Int chunkPos = chunkPosition;

        bool activateRight = chunkPos.x == chunkSize - 1;
        bool activateLeft = chunkPos.x == 0;
        bool activateUp = chunkPos.y == chunkSize - 1;
        bool activateDown = chunkPos.y == 0;
        //diagonal
        bool activateRightUp = chunkPos.x == chunkSize - 1 && chunkPos.y == chunkSize - 1;
        bool activateRightDown = chunkPos.x == chunkSize - 1 && chunkPos.y == 0;
        bool activateLeftUp = chunkPos.x == 0 && chunkPos.y == chunkSize - 1;
        bool activateLeftDown = chunkPos.x == 0 && chunkPos.y == 0;

        if (activateRight && SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y), out Chunk rightChunk))
        {
            rightChunk.isActive = true;
            rightChunk.isActiveNextFrame = true;
        }

        if (activateLeft && SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y), out Chunk leftChunk))
        {
            leftChunk.isActive = true;
            leftChunk.isActiveNextFrame = true;
        }

        if (activateUp && SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x, this.chunkPosition.y + 1), out Chunk aboveChunk))
        {
            aboveChunk.isActive = true;
            aboveChunk.isActiveNextFrame = true;
        }

        if (activateDown && SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x, this.chunkPosition.y - 1), out Chunk belowChunk))
        {
            belowChunk.isActive = true;
            belowChunk.isActiveNextFrame = true;
        }

        if (diagonal)
        {
            if (activateRightUp && SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y + 1), out Chunk aboveRightChunk))
            {
                aboveRightChunk.isActive = true;
                aboveRightChunk.isActiveNextFrame = true;
            }

            if (activateRightDown && SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y - 1), out Chunk belowRightChunk))
            {
                belowRightChunk.isActive = true;
                belowRightChunk.isActiveNextFrame = true;
            }

            if (activateLeftUp && SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y + 1), out Chunk aboveLeftChunk))
            {
                aboveLeftChunk.isActive = true;
                aboveLeftChunk.isActiveNextFrame = true;
            }

            if (activateLeftDown && SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y - 1), out Chunk belowLeftChunk))
            {
                belowLeftChunk.isActive = true;
                belowLeftChunk.isActiveNextFrame = true;
            }
        }
    }

    public void WakeUpElementsNeighbors(Vector2Int position, Vector2Int currentChunkPos)
    {
        Vector2Int[] neighbors = new Vector2Int[]
        {
                new Vector2Int(position.x - 1, position.y),     // left
                new Vector2Int(position.x + 1, position.y),     // right
                new Vector2Int(position.x, position.y + 1),     // above
                new Vector2Int(position.x - 1, position.y + 1), // above-left
                new Vector2Int(position.x + 1, position.y + 1), // above-right
        };

        foreach (Vector2Int neighborPos in neighbors)
        {
            Vector2Int targetChunk = currentChunkPos;
            Vector2Int adjustedPos = neighborPos;

            if (adjustedPos.x < 0)
            {
                adjustedPos.x = chunkSize - 1;
                targetChunk.x -= 1;
            }
            else if (adjustedPos.x >= chunkSize)
            {
                adjustedPos.x = 0;
                targetChunk.x += 1;
            }
            if (adjustedPos.y >= chunkSize)
            {
                adjustedPos.y = 0;
                targetChunk.y += 1;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(targetChunk, out Chunk targetChunkObj))
            {
                Element neighbor = targetChunkObj.elements[adjustedPos.x, adjustedPos.y];
                if (neighbor != null && neighbor.isResting)
                {
                    neighbor.isResting = false;
                    neighbor.verticalVelocity = 0f;
                    targetChunkObj.isActive = true;
                    targetChunkObj.isActiveNextFrame = true;
                }
            }
        }
    }

    public void ActivateAllNeighbors(bool diagonal = false)
    {
        if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y), out Chunk rightChunk))
        {
            rightChunk.isActive = true;
            rightChunk.isActiveNextFrame = true;
        }

        if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y), out Chunk leftChunk))
        {
            leftChunk.isActive = true;
            leftChunk.isActiveNextFrame = true;
        }

        if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x, this.chunkPosition.y + 1), out Chunk aboveChunk))
        {
            aboveChunk.isActive = true;
            aboveChunk.isActiveNextFrame = true;
        }

        if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x, this.chunkPosition.y - 1), out Chunk belowChunk))
        {
            belowChunk.isActive = true;
            belowChunk.isActiveNextFrame = true;
        }

        if (diagonal)
        {
            if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y + 1), out Chunk aboveRightChunk))
            {
                aboveRightChunk.isActive = true;
                aboveRightChunk.isActiveNextFrame = true;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x + 1, this.chunkPosition.y - 1), out Chunk belowRightChunk))
            {
                belowRightChunk.isActive = true;
                belowRightChunk.isActiveNextFrame = true;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y + 1), out Chunk aboveLeftChunk))
            {
                aboveLeftChunk.isActive = true;
                aboveLeftChunk.isActiveNextFrame = true;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(new Vector2Int(this.chunkPosition.x - 1, this.chunkPosition.y - 1), out Chunk belowLeftChunk))
            {
                belowLeftChunk.isActive = true;
                belowLeftChunk.isActiveNextFrame = true;
            }
        }
    }

    public void ActivateNeighbors(Vector2Int localPos)
    {
        Vector2Int currentChunkPos = this.chunkPosition;

        bool isOnLeftBorder = localPos.x == 0;
        bool isOnRightBorder = localPos.x == chunkSize;
        bool isOnTopBorder = localPos.y == chunkSize;
        bool isOnBottomBorder = localPos.y == 0;

        List<Vector2Int> chunksToWake = new List<Vector2Int>();

        if (isOnLeftBorder)
        {
            chunksToWake.Add(new Vector2Int(currentChunkPos.x - 1, currentChunkPos.y));
            if (isOnTopBorder)
                chunksToWake.Add(new Vector2Int(currentChunkPos.x - 1, currentChunkPos.y + 1));
            if (isOnBottomBorder)
                chunksToWake.Add(new Vector2Int(currentChunkPos.x - 1, currentChunkPos.y - 1));
        }
        if (isOnRightBorder)
        {
            chunksToWake.Add(new Vector2Int(currentChunkPos.x + 1, currentChunkPos.y));
            if (isOnTopBorder)
                chunksToWake.Add(new Vector2Int(currentChunkPos.x + 1, currentChunkPos.y + 1));
            if (isOnBottomBorder)
                chunksToWake.Add(new Vector2Int(currentChunkPos.x + 1, currentChunkPos.y - 1));
        }

        if (isOnTopBorder)
        {
            chunksToWake.Add(new Vector2Int(currentChunkPos.x, currentChunkPos.y + 1));
        }
        if (isOnBottomBorder)
        {
            chunksToWake.Add(new Vector2Int(currentChunkPos.x, currentChunkPos.y - 1));
        }

        foreach (Vector2Int neighborChunk in chunksToWake)
        {
            if (SandSimulation.Instance.chunks.TryGetValue(neighborChunk, out Chunk neighborChunkObj))
            {
                neighborChunkObj.isActive = true;
                neighborChunkObj.isActiveNextFrame = true;
            }
        }
    }
}