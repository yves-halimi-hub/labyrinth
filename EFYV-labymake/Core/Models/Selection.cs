using System;
using System.Collections.Generic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    // An immutable pixel mask over a frame, produced by the selection tools.
    // Bounds are already intersected with the frame at construction time, so a
    // region always addresses valid canvas pixels. The factories return null
    // when the requested geometry selects nothing.
    public sealed class SelectionRegion
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int SelectedCount { get; }

        private readonly bool[] mask;

        internal bool[] Mask => mask;

        private SelectionRegion(int x, int y, int width, int height, bool[] mask, int selectedCount)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            this.mask = mask;
            SelectedCount = selectedCount;
        }

        public bool Contains(int canvasX, int canvasY)
        {
            int localX = canvasX - X;
            int localY = canvasY - Y;
            if (localX < Config.Canvas.MinCoordinate || localY < Config.Canvas.MinCoordinate ||
                localX >= Width || localY >= Height)
                return false;
            return mask[localY * Width + localX];
        }

        public static SelectionRegion FromRectangle(
            int frameWidth,
            int frameHeight,
            int x0,
            int y0,
            int x1,
            int y1)
        {
            if (frameWidth <= Config.Canvas.MinCoordinate || frameHeight <= Config.Canvas.MinCoordinate)
                return null;

            long rawMinX = System.Math.Min(x0, x1);
            long rawMaxX = System.Math.Max(x0, x1);
            long rawMinY = System.Math.Min(y0, y1);
            long rawMaxY = System.Math.Max(y0, y1);

            int minX = (int)System.Math.Max(rawMinX, Config.Canvas.MinCoordinate);
            int maxX = (int)System.Math.Min(rawMaxX, frameWidth - Config.Common.UnitCount);
            int minY = (int)System.Math.Max(rawMinY, Config.Canvas.MinCoordinate);
            int maxY = (int)System.Math.Min(rawMaxY, frameHeight - Config.Common.UnitCount);
            if (minX > maxX || minY > maxY) return null;

            int width = maxX - minX + Config.Common.UnitCount;
            int height = maxY - minY + Config.Common.UnitCount;
            var mask = new bool[checked(width * height)];
            for (int index = Config.Common.FirstIndex; index < mask.Length; index++) mask[index] = true;
            return new SelectionRegion(minX, minY, width, height, mask, mask.Length);
        }

        // Lasso mask: a pixel is selected when its center (x + 0.5, y + 0.5) is
        // inside the closed polygon by the even-odd rule. A degenerate stroke
        // that encloses no pixel center selects nothing and returns null.
        public static SelectionRegion FromPolygon(
            int frameWidth,
            int frameHeight,
            IReadOnlyList<int> pointsX,
            IReadOnlyList<int> pointsY)
        {
            if (pointsX == null) throw new ArgumentNullException(nameof(pointsX));
            if (pointsY == null) throw new ArgumentNullException(nameof(pointsY));
            if (pointsX.Count != pointsY.Count) throw new ArgumentException(nameof(pointsY));
            if (pointsX.Count > Config.Tool.Selection.MaxLassoPoints)
                throw new ArgumentOutOfRangeException(nameof(pointsX));
            if (frameWidth <= Config.Canvas.MinCoordinate || frameHeight <= Config.Canvas.MinCoordinate)
                return null;
            if (pointsX.Count < Config.Tool.Selection.MinPolygonPoints) return null;

            long rawMinX = long.MaxValue;
            long rawMaxX = long.MinValue;
            long rawMinY = long.MaxValue;
            long rawMaxY = long.MinValue;
            for (int index = Config.Common.FirstIndex; index < pointsX.Count; index++)
            {
                if (pointsX[index] < rawMinX) rawMinX = pointsX[index];
                if (pointsX[index] > rawMaxX) rawMaxX = pointsX[index];
                if (pointsY[index] < rawMinY) rawMinY = pointsY[index];
                if (pointsY[index] > rawMaxY) rawMaxY = pointsY[index];
            }

            int minX = (int)System.Math.Max(rawMinX, Config.Canvas.MinCoordinate);
            int maxX = (int)System.Math.Min(rawMaxX, frameWidth - Config.Common.UnitCount);
            int minY = (int)System.Math.Max(rawMinY, Config.Canvas.MinCoordinate);
            int maxY = (int)System.Math.Min(rawMaxY, frameHeight - Config.Common.UnitCount);
            if (minX > maxX || minY > maxY) return null;

            int width = maxX - minX + Config.Common.UnitCount;
            int height = maxY - minY + Config.Common.UnitCount;
            var mask = new bool[checked(width * height)];
            int selectedCount = Config.Common.EmptyCount;

            for (int y = minY; y <= maxY; y++)
            {
                double centerY = y + 0.5;
                for (int x = minX; x <= maxX; x++)
                {
                    double centerX = x + 0.5;
                    if (!IsInsidePolygon(pointsX, pointsY, centerX, centerY)) continue;
                    mask[(y - minY) * width + (x - minX)] = true;
                    selectedCount++;
                }
            }

            if (selectedCount == Config.Common.EmptyCount) return null;
            return new SelectionRegion(minX, minY, width, height, mask, selectedCount);
        }

        private static bool IsInsidePolygon(
            IReadOnlyList<int> pointsX,
            IReadOnlyList<int> pointsY,
            double probeX,
            double probeY)
        {
            bool inside = false;
            int count = pointsX.Count;
            int previous = count - Config.Common.UnitCount;
            for (int current = Config.Common.FirstIndex; current < count; previous = current, current++)
            {
                double currentY = pointsY[current];
                double previousY = pointsY[previous];
                if ((currentY > probeY) == (previousY > probeY)) continue;
                double crossingX = pointsX[current] +
                    (probeY - currentY) * (pointsX[previous] - pointsX[current]) / (previousY - currentY);
                if (probeX < crossingX) inside = !inside;
            }
            return inside;
        }
    }

    // A lifted (or pasted) buffer of masked pixels hovering over the canvas.
    // It lives only inside a DesignerSession: the session moves it, renders it
    // through ViewportController compositing, and anchors or cancels it. The
    // arrays are owned by the selection; hosts must treat them as read-only.
    public sealed class FloatingSelection
    {
        public int OffsetX { get; internal set; }
        public int OffsetY { get; internal set; }
        public int Width { get; }
        public int Height { get; }
        public uint[] Pixels { get; }
        public bool[] Mask { get; }

        internal FloatingSelection(int offsetX, int offsetY, int width, int height, uint[] pixels, bool[] mask)
        {
            if (width <= Config.Canvas.MinCoordinate) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= Config.Canvas.MinCoordinate) throw new ArgumentOutOfRangeException(nameof(height));
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            int expected = checked(width * height);
            if (pixels.Length != expected) throw new ArgumentException(nameof(pixels));
            if (mask.Length != expected) throw new ArgumentException(nameof(mask));

            OffsetX = offsetX;
            OffsetY = offsetY;
            Width = width;
            Height = height;
            Pixels = pixels;
            Mask = mask;
        }

        public bool HitTest(int canvasX, int canvasY)
        {
            int localX = canvasX - OffsetX;
            int localY = canvasY - OffsetY;
            if (localX < Config.Canvas.MinCoordinate || localY < Config.Canvas.MinCoordinate ||
                localX >= Width || localY >= Height)
                return false;
            return Mask[localY * Width + localX];
        }

        internal FloatingSelection Clone()
        {
            return new FloatingSelection(
                OffsetX,
                OffsetY,
                Width,
                Height,
                (uint[])Pixels.Clone(),
                (bool[])Mask.Clone());
        }
    }
}
