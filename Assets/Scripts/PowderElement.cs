using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static SandSimulation;

public abstract class PowderElement : Element
{
    // specific for this element/particle type
    public virtual float density => 2.0f; // Default density for powder particles
    public virtual float sinkSpeed => 0.3f; // How fast the particle sinks in liquid
    public virtual float diagonalSinkSpeed => 0.15f; // How likely to sink diagonally

    //COLOR DATA
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

    public PowderElement(Vector2Int localPos, Vector2Int chunkPos) : base(localPos, chunkPos)
    {

    }

    // overriden from the base Element class
    public override float maxVelocity => 5f;  // Maximum fall speed
    public override float velocityAbsorption => 0.5f; // How much velocity is retained on impact (0-1)
    public override float maxHorizontalVelocity => 3f; // Maximum horizontal speed
    public override float horizontalDrag => 0.2f; // Base drag in air
    public override float friction => 0.4f; // Additional slowdown when touching surfaces


    private Vector2Int? lastPosition = null;
    private bool waitingToSink;
    public override bool NeedsUpdate => !isResting || waitingToSink;

    public override void Simulate(Chunk currentChunk)
    {
        //epicness

        lastPosition = localPosition;

        currentChunk.CheckElementContacts(this);
        ApplyFriction();

        //print(localPosition);

        Chunk nextMoveChunk = null;

        // Handle liquid interactions...
        Vector2Int below = new Vector2Int(localPosition.x, localPosition.y - 1);
        Vector2Int targetChunk = chunkPosition;
        below = AdjustPositionForChunk(below, ref targetChunk, currentChunk.chunkSize);

        // Try to sink straight down first
        if (TrySwapWithLiquid(below, targetChunk, currentChunk, sinkSpeed))
        {
            return;
        }

        bool shouldSink = false;
        LiquidElement liquidBelow = null;

        if (SandSimulation.Instance.chunks.TryGetValue(targetChunk, out Chunk targetChunkObj))
        {
            var particleBelow = targetChunkObj.elements[below.x, below.y];
            if (particleBelow is LiquidElement liquid)
            {
                liquidBelow = liquid;
                float densityDiff = density - liquid.density;
                // Add randomness to make sinking look more natural
                shouldSink = densityDiff > 0 && Random.value < (sinkSpeed * Mathf.Min(densityDiff, 1f));
            }
        }

        if (shouldSink && liquidBelow != null)
        {
            // Swap positions with liquid below
            if (targetChunk == chunkPosition)
            {
                // Same chunk swap
                currentChunk.elements[localPosition.x, localPosition.y] = liquidBelow;
                currentChunk.elements[below.x, below.y] = this;
                liquidBelow.localPosition = localPosition;
                localPosition = below;
            }
            else
            {
                // Cross-chunk swap
                currentChunk.elements[localPosition.x, localPosition.y] = liquidBelow;
                targetChunkObj.elements[below.x, below.y] = this;
                liquidBelow.localPosition = localPosition;
                liquidBelow.chunkPosition = chunkPosition;
                localPosition = below;
                chunkPosition = targetChunk;
                targetChunkObj.isActive = true;
            }

            // Reduce horizontal velocity when sinking
            horizontalVelocity *= 0.8f;
            return;
        }


        if (isResting && !isSleeping)
        {
            Vector2Int belowCheck = new Vector2Int(localPosition.x, localPosition.y - 1);
            Vector2Int targetChunkCheck = chunkPosition;
            belowCheck = AdjustPositionForChunk(belowCheck, ref targetChunkCheck, currentChunk.chunkSize);

            if (SandSimulation.Instance.chunks.TryGetValue(targetChunkCheck, out Chunk targetChunkObjCheck))
            {
                Vector3Int tilePosition = GetTilemapPosition(belowCheck, targetChunkCheck);

                bool isEmpty = !SandSimulation.Instance.collisionTilemap.HasTile(tilePosition) &&
                             targetChunkObjCheck.elements[belowCheck.x, belowCheck.y] == null;

                bool shouldSleep = CanSinkInLiquid(targetChunkObjCheck.elements[belowCheck.x, belowCheck.y]);

                if (isEmpty)
                {
                    isResting = false;
                    verticalVelocity = 0f;
                }
                else
                {
                    if (shouldSleep)
                    {
                        isResting = false;
                        isSleeping = true;
                    }
                    else
                    {
                        return;
                    }
                }
            }
        }

        currentChunk.WakeUpElementsNeighbors(localPosition, chunkPosition);
        verticalVelocity = Mathf.Min(verticalVelocity + SandSimulation.Instance.gravity, maxVelocity);

        if (Mathf.Abs(horizontalVelocity) > 0)
        {
            horizontalVelocity *= (1f - horizontalDrag);
            if (Mathf.Abs(horizontalVelocity) < 0.1f)
            {
                horizontalVelocity = 0f;
            }
        }

        float totalVelocity = Mathf.Sqrt(verticalVelocity * verticalVelocity + horizontalVelocity * horizontalVelocity);
        int steps = Mathf.CeilToInt(totalVelocity);

        Vector2Int current = localPosition;
        Vector2Int currentChunkPos = chunkPosition;

        for (int step = 0; step < steps; step++)
        {
            Vector2Int[] potentialMoves = GetPotentialMoves(current, isResting, horizontalVelocity);
            bool moved = false;
            bool hitObstacle = false;

            foreach (Vector2Int targetPos in potentialMoves)
            {
                Vector2Int targetChunkPos = currentChunkPos;
                Vector2Int adjustedPos = AdjustPositionForChunk(targetPos, ref targetChunkPos, currentChunk.chunkSize);

                if (SandSimulation.Instance.chunks.TryGetValue(targetChunkPos, out Chunk targetChunkObject))
                {
                    Vector3Int tilePosition = GetTilemapPosition(adjustedPos, targetChunkPos);
                    nextMoveChunk = targetChunkObject;

                    bool isEmpty = !SandSimulation.Instance.collisionTilemap.HasTile(tilePosition) &&
                                 targetChunkObject.elements[adjustedPos.x, adjustedPos.y] == null;

                    if (isEmpty)
                    {
                        isSleeping = false;
                        moved = MoveToNewPosition(adjustedPos, targetChunkPos, currentChunk, targetChunkObject);
                        if (targetChunkPos != currentChunkPos)
                        {
                            UpdateLastPosition(nextMoveChunk, currentChunk);
                            return;
                        }
                        current = adjustedPos;
                        break;
                    }
                    else if (targetPos.y < current.y && targetPos.x != current.x)
                    {
                        isSleeping = true;
                        if (TrySwapWithLiquid(adjustedPos, targetChunkPos, currentChunk, diagonalSinkSpeed))
                        {
                            UpdateLastPosition(nextMoveChunk, currentChunk);
                            return;
                        }
                        hitObstacle = true;
                    }
                    else if (targetPos.y < current.y)
                    {
                        hitObstacle = true;
                    }
                }
            }

            if (!moved)
            {
                HandleCollision(hitObstacle);
                return;
            }
        }

        UpdateLastPosition(nextMoveChunk, currentChunk);
    }

    private Vector2Int[] GetPotentialMoves(Vector2Int current, bool isResting, float horizontalVelocity)
    {
        if (isResting)
        {
            return new Vector2Int[] { new Vector2Int(current.x, current.y - 1) };
        }

        int horizontalDir = horizontalVelocity > 0 ? 1 : -1;
        if (Mathf.Abs(horizontalVelocity) < 0.1f) horizontalDir = 0;

        if (horizontalDir == 0)
        {
            return Random.Range(0, 2) == 0 ?
                new Vector2Int[]
                {
                    new Vector2Int(current.x, current.y - 1),
                    new Vector2Int(current.x - 1, current.y - 1),
                    new Vector2Int(current.x + 1, current.y - 1)
                } :
                new Vector2Int[]
                {
                    new Vector2Int(current.x, current.y - 1),
                    new Vector2Int(current.x + 1, current.y - 1),
                    new Vector2Int(current.x - 1, current.y - 1)
                };
        }

        return Random.Range(0, 2) == 0 ?
            new Vector2Int[]
            {
                new Vector2Int(current.x, current.y - 1),
                new Vector2Int(current.x + horizontalDir, current.y - 1),
                new Vector2Int(current.x + horizontalDir, current.y),
                new Vector2Int(current.x - horizontalDir, current.y - 1)
            } :
            new Vector2Int[]
            {
                new Vector2Int(current.x + horizontalDir, current.y - 1),
                new Vector2Int(current.x, current.y - 1),
                new Vector2Int(current.x + horizontalDir, current.y),
                new Vector2Int(current.x - horizontalDir, current.y - 1)
            };
    }

    private void HandleCollision(bool hitObstacle)
    {
        if (hitObstacle && verticalVelocity > 0.5f)
        {
            float impactForce = verticalVelocity * velocityAbsorption;
            horizontalVelocity = Random.Range(-1f, 1f) * impactForce;
            horizontalVelocity = Mathf.Clamp(horizontalVelocity, -maxHorizontalVelocity, maxHorizontalVelocity);
        }

        verticalVelocity = 0f;
        isResting = (Mathf.Abs(horizontalVelocity) < 0.1f);
    }

    private void UpdateLastPosition(Chunk nextMoveChunk, Chunk currentChunk)
    {
        if (isSleeping)
        {
            nextMoveChunk.isActiveNextFrame = true;
            //currentChunk.WakeUpAllChunkNeighbors(true);
            currentChunk.WakeUpChunkNeighbors(localPosition, true);
            if (lastPosition != null) currentChunk.WakeUpChunkNeighbors(lastPosition.Value, true);
        }

        if (lastPosition != null)
        {
            if (localPosition == lastPosition)
            {
                isResting = true;
            }
            else if (nextMoveChunk != null)
            {
                nextMoveChunk.isActiveNextFrame = true;
                //currentChunk.WakeUpAllChunkNeighbors(true);
                currentChunk.WakeUpChunkNeighbors(localPosition, true);
                if (lastPosition != null) currentChunk.WakeUpChunkNeighbors(lastPosition.Value, true);
            }
        }
        else
        {
            lastPosition = localPosition;
            if (nextMoveChunk != null)
            {
                nextMoveChunk.isActiveNextFrame = true;
                //currentChunk.WakeUpAllChunkNeighbors(true);
                currentChunk.WakeUpChunkNeighbors(localPosition, true);
            }
        }
    }

    private bool CanSinkInLiquid(Element particle)
        => particle is LiquidElement liquid && density > liquid.density;

    private bool TrySwapWithLiquid(Vector2Int targetPos, Vector2Int targetChunkPos, Chunk currentChunk, float swapChance)
    {
        if (!SandSimulation.Instance.chunks.TryGetValue(targetChunkPos, out Chunk targetChunkObj)) return false;
        if (targetChunkObj.elements[targetPos.x, targetPos.y] is not LiquidElement liquid) return false;

        float densityDiff = density - liquid.density;
        if (densityDiff <= 0) return false;

        if (Random.value >= swapChance * Mathf.Min(densityDiff, 1f))
        {
            waitingToSink = true;
            return false;
        }

        // liquid takes our old cell
        currentChunk.elements[localPosition.x, localPosition.y] = liquid;
        liquid.localPosition = localPosition;
        liquid.chunkPosition = chunkPosition;
        liquid.verticalVelocity = verticalVelocity * liquid.velocityAbsorption * -1f;
        liquid.isResting = false;
        currentChunk.isActiveNextFrame = true;

        // we take the liquid's cell
        targetChunkObj.elements[targetPos.x, targetPos.y] = this;
        localPosition = targetPos;
        chunkPosition = targetChunkPos;
        targetChunkObj.isActive = true;
        targetChunkObj.isActiveNextFrame = true;

        horizontalVelocity *= 0.8f;
        return true;
    }

    private Vector2Int AdjustPositionForChunk(Vector2Int position, ref Vector2Int chunkPos, int chunkSize)
    {
        Vector2Int adjustedPos = position;

        // Handle vertical boundaries
        if (adjustedPos.y < 0)
        {
            adjustedPos.y = chunkSize - 1;
            chunkPos.y -= 1;
        }
        else if (adjustedPos.y >= chunkSize)
        {
            adjustedPos.y = 0;
            chunkPos.y += 1;
        }

        // Handle horizontal boundaries
        if (adjustedPos.x < 0)
        {
            adjustedPos.x = chunkSize - 1;
            chunkPos.x -= 1;
        }
        else if (adjustedPos.x >= chunkSize)
        {
            adjustedPos.x = 0;
            chunkPos.x += 1;
        }

        return adjustedPos;
    }

    private Vector3Int GetTilemapPosition(Vector2Int localPos, Vector2Int chunkPos)
    {
        // Simply use the chunk position as the tilemap position since 1 tile = 1 chunk
        return new Vector3Int(chunkPos.x, chunkPos.y, 0);
    }

    private bool MoveToNewPosition(Vector2Int newPos, Vector2Int newChunkPos, Chunk currentChunk, Chunk targetChunk)
    {
        if (currentChunk == targetChunk)
        {
            // Same chunk movement
            currentChunk.elements[localPosition.x, localPosition.y] = null;
            currentChunk.elements[newPos.x, newPos.y] = this;
            localPosition = newPos;
            return true;
        }
        else
        {
            // Cross-chunk movement
            currentChunk.elements[localPosition.x, localPosition.y] = null;
            targetChunk.elements[newPos.x, newPos.y] = this;
            localPosition = newPos;
            chunkPosition = newChunkPos;
            targetChunk.isActive = true;
            return true;
        }
    }
}