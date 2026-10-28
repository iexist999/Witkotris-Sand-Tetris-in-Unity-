using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// Decides which Solid cells are fixed in place ("anchored", <see cref="Cell.anchored"/>) and which are free.
    ///
    ///   - SuperSolid cells are always anchored (World.MakeCell sets it).
    ///   - A Solid cell is anchored while it is connected to a SuperSolid, directly or through other anchored Solids.
    ///   - Any other Solid is free: <see cref="SolidBehavior"/> lets it fall until it touches an anchored cell, then it freezes.
    ///
    /// Connectivity is NOT recomputed every step, only when something changes:
    ///   - SetCell paints / erases a cell     -> <see cref="OnCellChanged"/>
    ///   - a free solid moves                 -> SolidBehavior calls <see cref="TouchesAnchor"/> / <see cref="Anchor"/>
    /// Only REMOVING an anchored cell can take support away from others, so that is the one expensive case
    /// (<see cref="Revalidate"/>). Everything else is a cheap local check, or a flood that anchors more cells.
    ///
    /// Invariant: every anchored Solid is connected to a SuperSolid through anchored cells.
    /// Anything that deletes / replaces cells (SetCell today, explosions / acid later) must go through
    /// <see cref="OnCellChanged"/> or the invariant breaks.
    /// </summary>
    internal sealed class SupportSystem
    {
        readonly World world;
        readonly WakeSystem waker;
        readonly Cell[] cells;
        readonly ElementBehavior[] behaviors;
        readonly int width;

        // scratch buffers shared by every search, grown on demand
        int[] stack = new int[256];
        int[] visited = new int[256];
        int visitedCount;

        // one mark per cell, compared against a rising token so it never has to be cleared (see Revalidate)
        readonly int[] mark;
        int token;

        public SupportSystem(World world, WakeSystem waker)
        {
            this.world = world;
            this.waker = waker;
            cells = world.Cells;
            behaviors = world.Behaviors;
            width = world.Width;
            mark = new int[cells.Length];
        }

        // ---------------------------------------------------------------
        // PUBLIC API
        // ---------------------------------------------------------------

        /// <summary>
        /// Call right after the cell at (x, y) was replaced (painted / erased). wasAnchored = the OLD cell's flag.
        /// Handles both directions: neighbours that may have lost their support, and the new cell's own state.
        /// </summary>
        public void OnCellChanged(int x, int y, bool wasAnchored)
        {
            if (wasAnchored) Revalidate(x, y);

            int idx = world.Index(x, y);
            ElementBehavior now = behaviors[cells[idx].element];

            if (now == ElementBehavior.SuperSolid) Spread(idx);                         // new anchor: grab loose solids around it
            else if (now == ElementBehavior.Solid && TouchesAnchor(x, y)) Anchor(x, y); // new solid next to support: fixed
            // else: a free solid (woken by SetCell's WakeAround, it will fall) or not a solid at all
        }

        /// <summary>True if any neighbour of (x, y) is anchored (8 or 4 neighbours, see SimulationSettings.SolidSupportDiagonals).</summary>
        public bool TouchesAnchor(int x, int y)
        {
            bool diag = SimulationSettings.SolidSupportDiagonals;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || (!diag && dx != 0 && dy != 0)) continue;

                    int nx = x + dx, ny = y + dy;
                    if (world.InBounds(nx, ny) && cells[ny * width + nx].anchored) return true;
                }
            }
            return false;
        }

        /// <summary>Freezes the free solid at (x, y) and every free solid touching it, directly or through each other.</summary>
        public void Anchor(int x, int y)
        {
            int idx = world.Index(x, y);
            ref Cell c = ref cells[idx];
            c.anchored = true;
            c.awake = false;
            c.velocity = Vector2.zero;

            Spread(idx);
        }

        // ---------------------------------------------------------------
        // SPREADING SUPPORT (cheap: only ever anchors more cells)
        // ---------------------------------------------------------------

        /// <summary>Anchors every free Solid reachable from the (already anchored) cell at startIdx through other free Solids.</summary>
        void Spread(int startIdx)
        {
            bool diag = SimulationSettings.SolidSupportDiagonals;
            int sp = 0;
            Push(ref stack, ref sp, startIdx);

            while (sp > 0)
            {
                int i = stack[--sp];
                int x = i % width, y = i / width;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dy == 0) || (!diag && dx != 0 && dy != 0)) continue;

                        int nx = x + dx, ny = y + dy;
                        if (!world.InBounds(nx, ny)) continue;

                        int ni = ny * width + nx;
                        ref Cell n = ref cells[ni];
                        if (n.anchored || n.blockId != 0 || behaviors[n.element] != ElementBehavior.Solid) continue;

                        n.anchored = true; // doubles as the "visited" mark
                        n.awake = false;
                        n.velocity = Vector2.zero;
                        Push(ref stack, ref sp, ni);
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // LOSING SUPPORT (the expensive case)
        // ---------------------------------------------------------------

        /// <summary>
        /// The anchored cell at (x, y) is gone. Every anchored Solid next to it must prove it still reaches a SuperSolid;
        /// a group that can't is released: un-anchored and woken, so it starts to fall.
        ///
        /// Marks: each search gets a fresh token, and cells it visits are stamped with it. If a search succeeds, its cells
        /// are proven supported, so later searches of the SAME call treat them as "found" (any mark >= firstToken).
        /// A failed search's cells are un-anchored, so they are skipped by the anchored check before marks matter.
        /// </summary>
        void Revalidate(int x, int y)
        {
            if (token > int.MaxValue - 1024) // practically never, but keeps the marks valid forever
            {
                System.Array.Clear(mark, 0, mark.Length);
                token = 0;
            }

            bool diag = SimulationSettings.SolidSupportDiagonals;
            int firstToken = token + 1;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || (!diag && dx != 0 && dy != 0)) continue;

                    int nx = x + dx, ny = y + dy;
                    if (!world.InBounds(nx, ny)) continue;

                    int ni = ny * width + nx;
                    ref Cell n = ref cells[ni];

                    // free cells never relied on the removed cell, and SuperSolids don't need support
                    if (!n.anchored || behaviors[n.element] != ElementBehavior.Solid) continue;
                    if (mark[ni] >= firstToken) continue; // an earlier search of this call already proved it

                    if (!ReachesSuperSolid(ni, firstToken, ++token)) ReleaseVisited();
                }
            }
        }

        /// <summary>
        /// Depth-first search over anchored cells from start, looking for a SuperSolid (or a cell already proven supported).
        /// Neighbours in the falling direction are pushed last, so they are popped first: the search dives toward the
        /// ground, where anchors usually are, instead of flooding the whole structure. The common "still connected"
        /// answer therefore costs about the height of the structure, not its area.
        /// On failure, <see cref="visited"/> holds the entire disconnected group.
        /// </summary>
        bool ReachesSuperSolid(int start, int firstToken, int search)
        {
            bool diag = SimulationSettings.SolidSupportDiagonals;
            int fall = SimulationSettings.SimulationGravity.y > 0f ? 1 : -1;

            visitedCount = 0;
            int sp = 0;

            mark[start] = search;
            Push(ref visited, ref visitedCount, start);
            Push(ref stack, ref sp, start);

            while (sp > 0)
            {
                int i = stack[--sp];
                int x = i % width, y = i / width;

                for (int row = 0; row < 3; row++)
                {
                    int dy = (row - 1) * fall; // against the fall first, along the fall last
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dy == 0) || (!diag && dx != 0 && dy != 0)) continue;

                        int nx = x + dx, ny = y + dy;
                        if (!world.InBounds(nx, ny)) continue;

                        int ni = ny * width + nx;
                        ref Cell n = ref cells[ni];
                        if (!n.anchored) continue;
                        if (behaviors[n.element] == ElementBehavior.SuperSolid) return true;

                        int m = mark[ni];
                        if (m == search) continue;        // already in this search
                        if (m >= firstToken) return true; // proven supported by an earlier search of this call

                        mark[ni] = search;
                        Push(ref visited, ref visitedCount, ni);
                        Push(ref stack, ref sp, ni);
                    }
                }
            }
            return false;
        }

        /// <summary>Un-anchors and wakes every cell of the group the last failed search collected.</summary>
        void ReleaseVisited()
        {
            for (int k = 0; k < visitedCount; k++)
            {
                int i = visited[k];

                ref Cell c = ref cells[i];
                c.anchored = false;
                c.velocity = Vector2.zero;

                waker.Wake(i % width, i / width); // free now: awake, and its chunk / area / zone with it
            }
        }

        // ---------------------------------------------------------------
        static void Push(ref int[] buffer, ref int count, int value)
        {
            if (count == buffer.Length) System.Array.Resize(ref buffer, count * 2);
            buffer[count++] = value;
        }
    }
}