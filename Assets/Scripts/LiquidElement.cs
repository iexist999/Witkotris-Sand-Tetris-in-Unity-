using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static SandSimulation;

public abstract class LiquidElement : Element
{
    public virtual float density => 1.0f;  // default density for liquid particles
    public virtual float dispersionRate => 5f;  // how many cells to check horizontally
    public virtual float flowSpeed => 0.8f;  // how quickly the liquid flows horizontally

    //COLOR DATA
    public override bool useCustomColorData => false;
    public override Color[] colorData => new Color[]
    {
                    new Color(0.1f, 0.4f, 0.505f),
                    new Color(0.1f, 0.4f, 0.51f),
                    new Color(0.1f, 0.4f, 0.515f),
                    new Color(0.1f, 0.4f, 0.52f),
                    new Color(0.1f, 0.4f, 0.525f),
    };

    public LiquidElement(Vector2Int localPos, Vector2Int chunkPos) : base(localPos, chunkPos)
    {

    }

    public override float maxVelocity => 4f;
    public override float velocityAbsorption => 0.3f;
    public override float maxHorizontalVelocity => 4f;
    public override float horizontalDrag => 0.1f;
    public override float friction => 0.2f;


    private Vector2Int? lastPosition = null;

    public override void Simulate(Chunk currentChunk)
    {
        currentChunk.CheckElementContacts(this);
        ApplyFriction();

        lastPosition = localPosition;

        Chunk nextMoveChunk = null;

        if (isResting)
        {
            Vector2Int belowCheck = new Vector2Int(localPosition.x, localPosition.y - 1);
            Vector2Int targetChunkCheck = chunkPosition;
            belowCheck = AdjustPositionForChunk(belowCheck, ref targetChunkCheck, currentChunk.chunkSize);

            if (belowCheck.y < 0)
            {
                belowCheck.y = currentChunk.chunkSize - 1;
                targetChunkCheck.y -= 1;
            }

            if (SandSimulation.Instance.chunks.TryGetValue(targetChunkCheck, out Chunk targetChunkObjCheck))
            {

                bool isEmpty = !SandSimulation.Instance.collisionTilemap.HasTile(new Vector3Int(
                    belowCheck.x / currentChunk.chunkSize + targetChunkCheck.x,
                    belowCheck.y / currentChunk.chunkSize + targetChunkCheck.y,
                    0)) &&
                    targetChunkObjCheck.elements[belowCheck.x, belowCheck.y] == null;

                if (isEmpty)
                {
                    isResting = false;
                    verticalVelocity = 0f;
                }
                else
                {
                    return;
                }
            }
        }

        currentChunk.WakeUpElementsNeighbors(localPosition, chunkPosition);
        //chunks[chunkPos].WakeUpChunkNeighbors(localPos, true);

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
            List<Vector2Int> potentialMoves = new List<Vector2Int>();

            // always try moving down first
            potentialMoves.Add(new Vector2Int(current.x, current.y - 1));

            // calculate horizontal spread based on dispersionRate
            int maxSpread = Mathf.CeilToInt(dispersionRate);

            // add diagonal moves with increasing horizontal distance
            for (int spread = 1; spread <= maxSpread; spread++)
            {
                // calculate probability based on distance
                float spreadProbability = flowSpeed * (1f - (spread - 1) / (float)maxSpread);

                int rnd0 = Random.Range(0, 2);

                if (rnd0 == 1)
                {
                    // try left side
                    if (Random.value < spreadProbability)
                    {
                        int rnd = Random.Range(0, 2);

                        if (rnd == 0)
                        {
                            potentialMoves.Add(new Vector2Int(current.x - spread, current.y - 1));  // Diagonal down-left
                            potentialMoves.Add(new Vector2Int(current.x - spread, current.y));      // Straight left
                        }
                        else
                        {
                            potentialMoves.Add(new Vector2Int(current.x - spread, current.y));      // Straight left
                            potentialMoves.Add(new Vector2Int(current.x - spread, current.y - 1));  // Diagonal down-left
                        }
                    }

                    // try right side
                    if (Random.value < spreadProbability)
                    {
                        int rnd = Random.Range(0, 2);

                        if (rnd == 0)
                        {
                            potentialMoves.Add(new Vector2Int(current.x + spread, current.y - 1));  // Diagonal down-right
                            potentialMoves.Add(new Vector2Int(current.x + spread, current.y));      // Straight right
                        }
                        else
                        {
                            potentialMoves.Add(new Vector2Int(current.x + spread, current.y));      // Straight right
                            potentialMoves.Add(new Vector2Int(current.x + spread, current.y - 1));  // Diagonal down-right
                        }
                    }
                }
                else
                {
                    // try right side
                    if (Random.value < spreadProbability)
                    {
                        int rnd = Random.Range(0, 2);

                        if (rnd == 0)
                        {
                            potentialMoves.Add(new Vector2Int(current.x + spread, current.y - 1));  // Diagonal down-right
                            potentialMoves.Add(new Vector2Int(current.x + spread, current.y));      // Straight right
                        }
                        else
                        {
                            potentialMoves.Add(new Vector2Int(current.x + spread, current.y));      // Straight right
                            potentialMoves.Add(new Vector2Int(current.x + spread, current.y - 1));  // Diagonal down-right
                        }
                    }

                    // try left side
                    if (Random.value < spreadProbability)
                    {
                        int rnd = Random.Range(0, 2);

                        if (rnd == 0)
                        {
                            potentialMoves.Add(new Vector2Int(current.x - spread, current.y - 1));  // Diagonal down-left
                            potentialMoves.Add(new Vector2Int(current.x - spread, current.y));      // Straight left
                        }
                        else
                        {
                            potentialMoves.Add(new Vector2Int(current.x - spread, current.y));      // Straight left
                            potentialMoves.Add(new Vector2Int(current.x - spread, current.y - 1));  // Diagonal down-left
                        }
                    }
                }


            }

            bool moved = false;
            bool hitObstacle = false;

            foreach (Vector2Int targetPos in potentialMoves)
            {
                Vector2Int targetChunkPos = currentChunkPos;
                Vector2Int adjustedPos = targetPos;
                adjustedPos = AdjustPositionForChunk(targetPos, ref targetChunkPos, currentChunk.chunkSize);

                /*
                // Adjust position and chunk if moving across chunk boundaries
                if (adjustedPos.y < 0)
                {
                    adjustedPos.y = currentChunk.chunkSize - 1;
                    targetChunkPos.y -= 1;
                }
                if (adjustedPos.x < 0)
                {
                    adjustedPos.x = currentChunk.chunkSize - 1;
                    targetChunkPos.x -= 1;
                }
                else if (adjustedPos.x >= currentChunk.chunkSize)
                {
                    adjustedPos.x = 0;
                    targetChunkPos.x += 1;
                }*/

                if (SandSimulation.Instance.chunks.TryGetValue(targetChunkPos, out Chunk targetChunkObject))
                {
                    Vector3Int tilePosition = new Vector3Int(
                        adjustedPos.x / currentChunk.chunkSize + targetChunkPos.x,
                        adjustedPos.y / currentChunk.chunkSize + targetChunkPos.y,
                        0);

                    nextMoveChunk = targetChunkObject;

                    bool isEmpty = !SandSimulation.Instance.collisionTilemap.HasTile(tilePosition) &&
                                 targetChunkObject.elements[adjustedPos.x, adjustedPos.y] == null;

                    if (isEmpty)
                    {
                        // move the particle
                        if (targetChunkPos == currentChunkPos)
                        {
                            currentChunk.elements[localPosition.x, localPosition.y] = null;
                            localPosition = adjustedPos;
                            currentChunk.elements[adjustedPos.x, adjustedPos.y] = this;
                            current = adjustedPos;
                        }
                        else
                        {
                            currentChunk.elements[localPosition.x, localPosition.y] = null;
                            localPosition = adjustedPos;
                            chunkPosition = targetChunkPos;
                            targetChunkObject.elements[adjustedPos.x, adjustedPos.y] = this;
                            targetChunkObject.isActive = true;
                            return;
                        }
                        moved = true;
                        break;
                    }
                    else if (targetPos.y < current.y)
                    {
                        hitObstacle = true;
                    }
                }
            }

            if (!moved)
            {
                if (hitObstacle && verticalVelocity > 0.5f)
                {
                    float impactForce = verticalVelocity * velocityAbsorption;
                    horizontalVelocity = Random.Range(-1f, 1f) * impactForce;
                    horizontalVelocity = Mathf.Clamp(horizontalVelocity, -maxHorizontalVelocity, maxHorizontalVelocity);
                }

                verticalVelocity = 0f;
                isResting = (Mathf.Abs(horizontalVelocity) < 0.1f);
                return;
            }
        }

        if (lastPosition != null)
        {
            if (localPosition == lastPosition)
            {
                this.isResting = true;
            }
            else
            {
                //wake up its chunk
                if (nextMoveChunk != null)
                {
                    nextMoveChunk.isActiveNextFrame = true;
                    //currentChunk.ActivateAllNeighbors(true);
                    //currentChunk.WakeUpAllChunkNeighbors(true);
                    currentChunk.WakeUpChunkNeighbors(localPosition, true);
                }
            }
        }
        else
        {
            lastPosition = localPosition;

            //wake up its chunk
            if (nextMoveChunk != null)
            {
                nextMoveChunk.isActiveNextFrame = true;
                //currentChunk.ActivateAllNeighbors(true);
                //currentChunk.WakeUpAllChunkNeighbors(true);
                currentChunk.WakeUpChunkNeighbors(localPosition, true);
            }
        }
    }

    private Vector2Int AdjustPositionForChunk(Vector2Int position, ref Vector2Int chunkPos, int chunkSize)
    {
        Vector2Int adjustedPos = position;

        // handle vertical boundaries
        if (adjustedPos.y < 0) { adjustedPos.y += chunkSize; chunkPos.y -= 1; }
        else if (adjustedPos.y >= chunkSize) { adjustedPos.y -= chunkSize; chunkPos.y += 1; }

        // handle horizontal boundaries
        if (adjustedPos.x < 0) { adjustedPos.x += chunkSize; chunkPos.x -= 1; }
        else if (adjustedPos.x >= chunkSize) { adjustedPos.x -= chunkSize; chunkPos.x += 1; }
        
        return adjustedPos;
    }
}