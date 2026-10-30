using System;
using System.Collections.Generic;
using UnityEngine;

namespace SandBXDSIM
{
    /// <summary>
    /// A rigid group of cells (a printed sprite, for example) that is frozen in place and can be moved by hand.
    ///
    ///   - Locked: every cell of the block carries Cell.blockId = this block's id. Locked cells are never stepped,
    ///     never woken, nothing can enter them (<see cref="World.EntryCostAt"/>), they do not act as support for
    ///     free solids, and the flow fields treat them as walls. They keep their element, color and everything else.
    ///   - <see cref="TryMove"/> shifts the whole block by (dx, dy) cells, or does nothing and returns false.
    ///   - <see cref="DestroyBlock"/> unlocks every cell and wakes it, so it falls / flows like any other cell.
    ///
    /// WHAT THE BLOCK CAN MOVE THROUGH. The world's medium (air) is always passable. <see cref="allowedMediums"/> lists
    /// more element names (e.g. "water"). Anything else (stone, sand, another block, anchored cells, the world edge)
    /// blocks the move.
    ///
    /// DISPLACEMENT. Cells of an allowed element that sit where the block lands are not deleted, they are pushed
    /// into the spots the block just left: the highest vacated spots first (against gravity). A T-shaped block moving
    /// down into water: the water under the stem ends up in the cells the top bar used to occupy. Moving a block
    /// never changes how many cells exist: the number of cells it lands on always equals the number it leaves.
    ///
    /// Grid coordinates are y-up: TryMove(0, 3) is three cells up, TryMove(1, 0) is one cell right.
    ///
    /// Cells of the block that get erased or overwritten from outside (SetCell, the erase tool) simply drop out of the
    /// block the next time it is used. When no cell is left the block counts as destroyed.
    /// </summary>
    public sealed class SandBlock
    {
        // ---------------------------------------------------------------
        // PUBLIC STATE
        // ---------------------------------------------------------------

        /// <summary>
        /// Names of the elements the block may move THROUGH, e.g. { "water" }. Case-insensitive. The world's medium
        /// (air) is always allowed and does not have to be listed. May be changed at any time.
        /// </summary>
        public string[] allowedMediums;

        /// <summary>
        /// true  = TryMove also checks every cell the block sweeps over on its way, so it cannot jump over a wall.
        /// false = only the destination is checked (a teleport).
        /// Diagonal moves go horizontal first, then vertical.
        /// </summary>
        public bool checkPath = true;

        public bool IsDestroyed => destroyed;

        /// <summary>How many cells the block still holds.</summary>
        public int Count
        {
            get { Prune(); return count; }
        }

        /// <summary>Smallest rectangle around the block (xMax / yMax exclusive).</summary>
        public RectInt Bounds
        {
            get
            {
                Prune();
                if (count == 0) return new RectInt();

                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                for (int i = 0; i < count; i++) {
                    if (xs[i] < x0) x0 = xs[i];
                    if (xs[i] > x1) x1 = xs[i];
                    if (ys[i] < y0) y0 = ys[i];
                    if (ys[i] > y1) y1 = ys[i];
                }
                return new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            }
        }

        /// <summary>Results of <see cref="TryMove(int, int)"/>.</summary>
        public const int MoveSuccess = 0;
        public const int MoveBlocked = 1;     // a target cell is occupied (anchored, another block, or a non-allowed element)
        public const int MoveOutOfBounds = 2; // a target position is outside the world

        // ---------------------------------------------------------------
        // STATE
        // ---------------------------------------------------------------
        static int nextId = 1; // 0 means "not part of any block"

        readonly World world;
        readonly WakeSystem waker;
        readonly Cell[] cells;
        readonly int id;

        int[] xs, ys; // current position of every block cell (parallel arrays)
        int count;
        bool destroyed;
        bool warnedUnknown;

        // scratch for TryMove, sized once: a move never needs more than one slot per block cell
        Cell[] carried;     // the block's own cells while they are being moved
        Cell[] displaced;   // cells pushed out of the way
        int[] slotKey;      // vacated spots, keyed by height
        int[] slotCell;     // which block cell each vacated spot belonged to
        bool[] passable;    // per element id: may the block enter it?

        // ---------------------------------------------------------------
        // CREATE
        // ---------------------------------------------------------------

        /// <summary>
        /// Locks the cells at the given positions into a block. Positions that cannot be part of one are skipped
        /// (and counted in one warning): out of the world, air, anchored terrain, cells of another block.
        /// Needs a running Simulation.
        /// </summary>
        /// <param name="cellPositions">grid positions of the cells, e.g. what SpritePrinter placed</param>
        /// <param name="allowedMediums">element names the block may move through (null = air only)</param>
        public SandBlock(IReadOnlyList<Vector2Int> cellPositions, string[] allowedMediums = null) {
            Simulation sim = Simulation.Instance;
            if (sim == null || sim.Layout == null) {
                Debug.LogError("SandBlock: no running Simulation found.");
                destroyed = true;
                return;
            }

            world = sim.World;
            waker = sim.Waker;
            cells = world.Cells;
            this.allowedMediums = allowedMediums ?? new string[0];
            id = nextId++;

            int cap = cellPositions != null ? cellPositions.Count : 0;
            xs = new int[cap];
            ys = new int[cap];

            int skipped = 0;
            for (int i = 0; i < cap; i++) {
                Vector2Int p = cellPositions[i];
                if (!world.InBounds(p.x, p.y)) { skipped++; continue; }

                ref Cell c = ref cells[world.Index(p.x, p.y)];
                if (c.blockId == id) continue; // listed twice

                if (c.blockId != 0 || c.anchored || world.Behaviors[c.element] == ElementBehavior.Atmosphere) {
                    skipped++;
                    continue;
                }

                c.blockId = id;
                c.awake = false; // the stepper skips sleeping cells, and WakeSystem refuses to wake locked ones
                c.freeFalling = false;
                c.velocity = Vector2.zero;

                xs[count] = p.x;
                ys[count] = p.y;
                count++;
            }

            carried = new Cell[count];
            displaced = new Cell[count];
            slotKey = new int[count];
            slotCell = new int[count];

            if (skipped > 0)
                Debug.LogWarning($"SandBlock: skipped {skipped} position(s) (outside the world, air, anchored terrain or already in another block).");
            if (count == 0) destroyed = true;

            world.Version++;
        }

        // ---------------------------------------------------------------
        // MOVE
        // ---------------------------------------------------------------

        /// <summary>
        /// Moves the whole block by (dx, dy) cells. Returns:
        ///   0 (<see cref="MoveSuccess"/>)     the block moved (a zero move counts as success)
        ///   1 (<see cref="MoveBlocked"/>)     nothing changed: a cell it would enter is anchored, part of another block,
        ///                                     or an element that is neither the world's medium nor in <see cref="allowedMediums"/>
        ///   2 (<see cref="MoveOutOfBounds"/>) nothing changed: a cell it would enter is outside the world
        /// See <see cref="checkPath"/> for what counts as "enter".
        /// </summary>
        public int TryMove(int dx, int dy) {
            if (destroyed) return MoveBlocked;

            Prune();
            if (count == 0) return MoveBlocked;
            if (dx == 0 && dy == 0) return MoveSuccess;

            BuildPassable();

            int result = CheckPath(dx, dy);
            if (result != MoveSuccess) return result;

            Commit(dx, dy);
            return MoveSuccess;
        }

        public int TryMove(Vector2Int delta) => TryMove(delta.x, delta.y);

        /// <summary>Can the block, as it is now, occupy (tx, ty)? Its own cells count as free: they move along.</summary>
        int CanOccupy(int tx, int ty) {
            if (!world.InBounds(tx, ty)) return MoveOutOfBounds;

            ref readonly Cell c = ref cells[world.Index(tx, ty)];
            if (c.blockId == id) return MoveSuccess;
            if (c.blockId != 0 || c.anchored) return MoveBlocked;
            return passable[c.element] ? MoveSuccess : MoveBlocked;
        }

        int CheckPath(int dx, int dy) {
            int ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
            int sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
            int steps = ax + ay;

            // offset after k unit steps: horizontal first, then vertical
            for (int k = checkPath ? 1 : steps; k <= steps; k++) {
                int ox = sx * Mathf.Min(k, ax);
                int oy = sy * Mathf.Max(0, k - ax);

                bool blocked = false;
                for (int i = 0; i < count; i++) {
                    int r = CanOccupy(xs[i] + ox, ys[i] + oy);
                    if (r == MoveOutOfBounds) return MoveOutOfBounds; // out of bounds wins within a step, so the result doesn't depend on cell order
                    if (r == MoveBlocked) blocked = true;
                }
                if (blocked) return MoveBlocked;
            }
            return MoveSuccess;
        }

        void BuildPassable() {
            int n = ElementDatabase.Count;
            if (passable == null || passable.Length != n) passable = new bool[n];
            else Array.Clear(passable, 0, n);

            passable[world.Medium] = true;
            if (allowedMediums == null) return;

            foreach (string name in allowedMediums) {
                if (ElementDatabase.TryFind(name, out ushort eid)) passable[eid] = true;
                else if (!warnedUnknown) {
                    warnedUnknown = true;
                    Debug.LogWarning($"SandBlock: unknown element '{name}' in allowedMediums.");
                }
            }
        }

        /// <summary>
        /// Performs a move that PathIsClear already approved. Four phases, all reads before any write:
        ///   1. read    snapshot the block, collect the cells it lands on (displaced) and the spots it leaves (vacated)
        ///   2. sort    vacated spots by height, highest last
        ///   3. write   empty the old spots, put displaced cells into the highest vacated ones, write the block at its new place
        ///   4. wake    everything around the vacated spots (stuff on top of the block, powder piles beside it, displaced cells)
        /// </summary>
        void Commit(int dx, int dy) {
            ushort medium = world.Medium;
            int up = SimulationSettings.SimulationGravity.y <= 0f ? 1 : -1;
            int w = world.Width;

            int pushed = 0, vacated = 0;

            // ---- 1. read ----
            for (int i = 0; i < count; i++) {
                carried[i] = cells[world.Index(xs[i], ys[i])];

                // a cell the block lands on that is not ours and not just medium has to be moved out of the way
                ref Cell target = ref cells[world.Index(xs[i] + dx, ys[i] + dy)];
                if (target.blockId != id && target.element != medium) displaced[pushed++] = target;

                // a spot the block leaves is vacated unless another block cell lands on it
                int sx = xs[i] - dx, sy = ys[i] - dy;
                bool refilled = world.InBounds(sx, sy) && cells[world.Index(sx, sy)].blockId == id;
                if (!refilled) {
                    slotCell[vacated] = i;
                    slotKey[vacated] = up * ys[i] * w + xs[i];
                    vacated++;
                }
            }

            // ---- 2. sort (ascending: the highest vacated spot ends up last) ----
            Array.Sort(slotKey, slotCell, 0, vacated);

            // ---- 3. write ----
            for (int i = 0; i < count; i++)
                cells[world.Index(xs[i], ys[i])] = world.MakeCell(medium);

            for (int j = 0; j < pushed; j++) // pushed <= vacated always: the block lands on as many spots as it leaves
            {
                int i = slotCell[vacated - 1 - j];
                Cell c = displaced[j];
                c.velocity = Vector2.zero;
                c.freeFalling = true;
                cells[world.Index(xs[i], ys[i])] = c;
            }

            for (int i = 0; i < count; i++) {
                xs[i] += dx;
                ys[i] += dy;
                cells[world.Index(xs[i], ys[i])] = carried[i];
            }

            // ---- 4. wake ----
            for (int s = 0; s < vacated; s++) {
                int i = slotCell[s];
                int vx = xs[i] - dx, vy = ys[i] - dy; // the spot block cell i left
                waker.WakeAround(vx, vy);
                waker.WakeSlideReach(vx, vy, 1 + SimulationSettings.PowderMaxExtraStack);
            }

            world.Version++;
        }

        // ---------------------------------------------------------------
        // DESTROY
        // ---------------------------------------------------------------

        /// <summary>
        /// Releases every cell: they stop being locked, become free falling and are woken, so they move like any other
        /// cell of their element from the next step on. The block is unusable afterwards.
        /// </summary>
        public void DestroyBlock() {
            if (destroyed) return;
            destroyed = true;

            for (int i = 0; i < count; i++) {
                ref Cell c = ref cells[world.Index(xs[i], ys[i])];
                if (c.blockId != id) continue; // erased or replaced from outside

                c.blockId = 0;
                c.freeFalling = true;
                c.velocity = Vector2.zero;
            }

            // second pass, so every cell is already unlocked when its neighbours get woken
            for (int i = 0; i < count; i++)
                waker.WakeAround(xs[i], ys[i]);

            count = 0;
            world.Version++;
        }

        // ---------------------------------------------------------------
        // INTERNALS
        // ---------------------------------------------------------------

        /// <summary>Drops cells that were erased / overwritten from outside. A block with no cells left is destroyed.</summary>
        void Prune() {
            if (destroyed) return;

            int w = 0;
            for (int r = 0; r < count; r++) {
                if (cells[world.Index(xs[r], ys[r])].blockId != id) continue;
                xs[w] = xs[r];
                ys[w] = ys[r];
                w++;
            }
            count = w;
            if (count == 0) destroyed = true;
        }
    }
} 