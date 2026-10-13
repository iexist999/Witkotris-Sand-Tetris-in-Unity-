using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SandBXDSIM
{
    public static class GridHelper
    {
        // 2D VECTOR CELL TRAVERSAL
        // traversal using DDA algorithm (Digital Differential Analyzer)
        public static Point[] GetLineTraversalPositionsDDA(int startX, int startY, int endX, int endY)
        {
            float xLength, yLength;
            xLength = Math.Abs(startX - endX);
            yLength = Math.Abs(startY - endY);

            float currentX = startX;
            float currentY = startY;

            int difference = (int)Math.Abs(xLength - yLength);
            int looplength = (int)Math.Max(xLength, yLength);

            if (looplength == 0) { return new Point[] { new Point(startX, startY) }; }

            Point[] points = new Point[looplength];

            for (int i = 0; i < looplength; i++)
            {
                points[i] = new Point((int)Math.Round(currentX), (int)Math.Round(currentY));

                if (xLength != 0) currentX = currentX + xLength / looplength * Math.Sign(endX - startX);
                if (yLength != 0) currentY = currentY + yLength / looplength * Math.Sign(endY - startY);
            }

            return points;
        }

        //traversal using Bresenham's algorithm
        public static Point[] GetLineTraversalPositionsBR(int startX, int startY, int endX, int endY)
        {
            int dx = Math.Abs(endX - startX);
            int dy = Math.Abs(endY - startY);
            int sx = startX < endX ? 1 : -1;
            int sy = startY < endY ? 1 : -1;
            int err = dx - dy;

            var points = new List<Point>();
            int x = startX, y = startY;

            while (true)
            {
                points.Add(new Point(x, y));
                if (x == endX && y == endY) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }

            return points.ToArray();
        }

        // traversal using supercover
        public static Point[] GetLineTraversalPositionsSC(int startX, int startY, int endX, int endY)
        {
            int x1 = startX, y1 = startY;
            int dx = endX - startX;
            int dy = endY - startY;

            int xstep = dx < 0 ? -1 : 1;
            int ystep = dy < 0 ? -1 : 1;
            dx = Math.Abs(dx);
            dy = Math.Abs(dy);

            int ddx = 2 * dx;
            int ddy = 2 * dy;

            var points = new List<Point> { new Point(x1, y1) };

            if (ddx >= ddy)
            {
                int errorPrev = dx;
                int error = dx;

                for (int i = 0; i < dx; i++)
                {
                    x1 += xstep;
                    error += ddy;

                    if (error > ddx)
                    {
                        y1 += ystep;
                        error -= ddx;

                        if (error + errorPrev < ddx) points.Add(new Point(x1, y1 - ystep));
                        else if (error + errorPrev > ddx) points.Add(new Point(x1 - xstep, y1));
                        else
                        {
                            points.Add(new Point(x1, y1 - ystep));
                            points.Add(new Point(x1 - xstep, y1));
                        }
                    }

                    points.Add(new Point(x1, y1));
                    errorPrev = error;
                }
            }
            else
            {
                int errorPrev = dy;
                int error = dy;

                for (int i = 0; i < dy; i++)
                {
                    y1 += ystep;
                    error += ddx;

                    if (error > ddy)
                    {
                        x1 += xstep;
                        error -= ddy;

                        if (error + errorPrev < ddy) points.Add(new Point(x1 - xstep, y1));
                        else if (error + errorPrev > ddy) points.Add(new Point(x1, y1 - ystep));
                        else
                        {
                            points.Add(new Point(x1 - xstep, y1));
                            points.Add(new Point(x1, y1 - ystep));
                        }
                    }

                    points.Add(new Point(x1, y1));
                    errorPrev = error;
                }
            }

            return points.ToArray();
        }

        // CELL TRAVERSAL INCLUDING NEIGHBORS

        private static void AddCellAndNeighbors(HashSet<Point> seen, List<Point> result, int x, int y, bool includeDiagonals)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!includeDiagonals && dx != 0 && dy != 0) continue;

                    var p = new Point(x + dx, y + dy);
                    if (seen.Add(p)) result.Add(p); // add returns false if already present
                }
            }
        }

        // traversal using DDA algorithm (Digital Differential Analyzer)
        public static Point[] GetLineTraversalPositionsWithNeighborsDDA(int startX, int startY, int endX, int endY, bool includeDiagonals = true)
        {
            float xLength = Math.Abs(startX - endX);
            float yLength = Math.Abs(startY - endY);

            float currentX = startX;
            float currentY = startY;

            int looplength = (int)Math.Max(xLength, yLength);

            var seen = new HashSet<Point>();
            var result = new List<Point>();

            if (looplength == 0)
            {
                AddCellAndNeighbors(seen, result, startX, startY, includeDiagonals);
                return result.ToArray();
            }

            for (int i = 0; i < looplength; i++)
            {
                AddCellAndNeighbors(seen, result, (int)Math.Round(currentX), (int)Math.Round(currentY), includeDiagonals);

                if (xLength != 0) currentX = currentX + xLength / looplength * Math.Sign(endX - startX);
                if (yLength != 0) currentY = currentY + yLength / looplength * Math.Sign(endY - startY);
            }

            return result.ToArray();
        }

        // traversal with neighbors using Bresenham's algorithm
        public static Point[] GetLineTraversalPositionsWithNeighborsBR(int startX, int startY, int endX, int endY, bool includeDiagonals = true)
        {
            int dx = Math.Abs(endX - startX);
            int dy = Math.Abs(endY - startY);
            int sx = startX < endX ? 1 : -1;
            int sy = startY < endY ? 1 : -1;
            int err = dx - dy;

            var seen = new HashSet<Point>();
            var result = new List<Point>();
            int x = startX, y = startY;

            while (true)
            {
                AddCellAndNeighbors(seen, result, x, y, includeDiagonals);
                if (x == endX && y == endY) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }

            return result.ToArray();
        }

        public static Point[] GetLineTraversalPositionsWithNeighborsSC(int startX, int startY, int endX, int endY, bool includeDiagonals = true)
        {
            int x1 = startX, y1 = startY;
            int dx = endX - startX;
            int dy = endY - startY;

            int xstep = dx < 0 ? -1 : 1;
            int ystep = dy < 0 ? -1 : 1;
            dx = Math.Abs(dx);
            dy = Math.Abs(dy);

            int ddx = 2 * dx;
            int ddy = 2 * dy;

            var seen = new HashSet<Point>();
            var result = new List<Point>();

            void Add(int px, int py) => AddCellAndNeighbors(seen, result, px, py, includeDiagonals);

            Add(x1, y1);

            if (ddx >= ddy)
            {
                int errorPrev = dx;
                int error = dx;

                for (int i = 0; i < dx; i++)
                {
                    x1 += xstep;
                    error += ddy;

                    if (error > ddx)
                    {
                        y1 += ystep;
                        error -= ddx;

                        if (error + errorPrev < ddx) Add(x1, y1 - ystep);
                        else if (error + errorPrev > ddx) Add(x1 - xstep, y1);
                        else
                        {
                            Add(x1, y1 - ystep);
                            Add(x1 - xstep, y1);
                        }
                    }

                    Add(x1, y1);
                    errorPrev = error;
                }
            }
            else
            {
                int errorPrev = dy;
                int error = dy;

                for (int i = 0; i < dy; i++)
                {
                    y1 += ystep;
                    error += ddx;

                    if (error > ddy)
                    {
                        x1 += xstep;
                        error -= ddy;

                        if (error + errorPrev < ddy) Add(x1 - xstep, y1);
                        else if (error + errorPrev > ddy) Add(x1, y1 - ystep);
                        else
                        {
                            Add(x1 - xstep, y1);
                            Add(x1, y1 - ystep);
                        }
                    }

                    Add(x1, y1);
                    errorPrev = error;
                }
            }

            return result.ToArray();
        }

        // GETTING NEIGHBORS

        private static readonly Point[] NeighborOffsets =
        {
            new Point( 0, -1), // up
            new Point( 1,  0), // right
            new Point( 0,  1), // down
            new Point(-1,  0), // left
            new Point( 1, -1), // up-right
            new Point( 1,  1), // down-right
            new Point(-1,  1), // down-left
            new Point(-1, -1), // up-left
        };

        /// <summary>
        /// Returns the positions of the cells surrounding (x, y).
        /// Does NOT bounds-check — pass width/height to the overload below if you need that.
        /// </summary>
        public static Point[] GetNeighbors(int x, int y, bool includeDiagonals = true)
        {
            int count = includeDiagonals ? 8 : 4;
            var result = new Point[count];

            for (int i = 0; i < count; i++)
                result[i] = new Point(x + NeighborOffsets[i].X, y + NeighborOffsets[i].Y);

            return result;
        }
    }
}
