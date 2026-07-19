using System.Buffers;
using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Math
{
    public static class Algorithms
    {
        // BRESENHAM'S LINE ALGORITHM
        // Decoupled from the Maker App. Uses raw memory pointers to draw a perfect line instantly.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void DrawLineBresenham(uint* canvas, int width, int height, int x0, int y0, int x1, int y1, uint colorVal)
        {
            if (canvas == null) throw new System.ArgumentNullException(nameof(canvas));
            if (width <= 0) throw new System.ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new System.ArgumentOutOfRangeException(nameof(height));

            int dx = FastMath.Abs(x1 - x0), sx = x0 < x1 ? BackendConfig.Math.StepPositive : BackendConfig.Math.StepNegative;
            int dy = -FastMath.Abs(y1 - y0), sy = y0 < y1 ? BackendConfig.Math.StepPositive : BackendConfig.Math.StepNegative;
            int err = dx + dy, e2;

            while (true)
            {
                // Unsigned cast bounds check
                if ((uint)x0 < (uint)width && (uint)y0 < (uint)height)
                {
                    *(canvas + (y0 * width + x0)) = colorVal;
                }
                if (x0 == x1 && y0 == y1) break;
                e2 = FastMath.FastMulPow2(err, BackendConfig.Math.SingleBitShift);
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public enum BrushShape { Square, Circle }

        // MIGRATION: Thick Brush support for the LabyMake Pencil Tool.
        // Stamps a shape centered around the line trajectory.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void DrawThickLineBresenham(uint* canvas, int width, int height, int x0, int y0, int x1, int y1, uint colorVal, int brushSize, BrushShape shape)
        {
            if (canvas == null) throw new System.ArgumentNullException(nameof(canvas));
            if (width <= 0) throw new System.ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new System.ArgumentOutOfRangeException(nameof(height));
            if (brushSize <= 0) throw new System.ArgumentOutOfRangeException(nameof(brushSize));

            int dx = FastMath.Abs(x1 - x0), sx = x0 < x1 ? BackendConfig.Math.StepPositive : BackendConfig.Math.StepNegative;
            int dy = -FastMath.Abs(y1 - y0), sy = y0 < y1 ? BackendConfig.Math.StepPositive : BackendConfig.Math.StepNegative;
            int err = dx + dy, e2;

            int minOffset = -FastMath.FastDivPow2(brushSize, BackendConfig.Math.SingleBitShift);
            int maxOffset = minOffset + brushSize - BackendConfig.Math.StepPositive;
            bool hasPixelCenteredBrush = (brushSize & BackendConfig.Math.StepPositive) != 0;
            while (true)
            {
                for (int by = minOffset; by <= maxOffset; by++)
                {
                    for (int bx = minOffset; bx <= maxOffset; bx++)
                    {
                        if (shape == BrushShape.Circle)
                        {
                            long radius;
                            long circleX;
                            long circleY;
                            if (hasPixelCenteredBrush)
                            {
                                radius = FastMath.FastDivPow2(brushSize, BackendConfig.Math.SingleBitShift);
                                circleX = bx;
                                circleY = by;
                            }
                            else
                            {
                                radius = brushSize;
                                circleX = FastMath.FastMulPow2(bx, BackendConfig.Math.SingleBitShift) +
                                    BackendConfig.Math.StepPositive;
                                circleY = FastMath.FastMulPow2(by, BackendConfig.Math.SingleBitShift) +
                                    BackendConfig.Math.StepPositive;
                            }

                            if ((circleX * circleX) + (circleY * circleY) > radius * radius)
                                continue;
                        }
                        
                        int px = x0 + bx;
                        int py = y0 + by;
                        
                        if ((uint)px < (uint)width && (uint)py < (uint)height)
                        {
                            *(canvas + (py * width + px)) = colorVal;
                        }
                    }
                }

                if (x0 == x1 && y0 == y1) break;
                e2 = FastMath.FastMulPow2(err, BackendConfig.Math.SingleBitShift);
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // SHAPE TOOLS: axis-aligned rectangle spanned by two anchor corners.
        // `filled` paints the whole box; otherwise a border band `thickness`
        // pixels wide is painted along the inside of the box (a band wider than
        // the box degrades to a solid fill). Off-canvas anchors are legal: only
        // the intersection with the canvas is visited, so far coordinates cost
        // at most one canvas sweep.
        public static unsafe void DrawRectangle(uint* canvas, int width, int height, int x0, int y0, int x1, int y1, uint colorVal, int thickness, bool filled)
        {
            if (canvas == null) throw new System.ArgumentNullException(nameof(canvas));
            if (width <= 0) throw new System.ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new System.ArgumentOutOfRangeException(nameof(height));
            if (thickness <= 0) throw new System.ArgumentOutOfRangeException(nameof(thickness));

            long minX = FastMath.FastMin(x0, x1);
            long maxX = FastMath.FastMax(x0, x1);
            long minY = FastMath.FastMin(y0, y1);
            long maxY = FastMath.FastMax(y0, y1);

            int clipMinX = (int)(minX > 0 ? minX : 0);
            int clipMaxX = (int)(maxX < width - 1 ? maxX : width - 1);
            int clipMinY = (int)(minY > 0 ? minY : 0);
            int clipMaxY = (int)(maxY < height - 1 ? maxY : height - 1);

            long innerMinX = minX + thickness;
            long innerMaxX = maxX - thickness;
            long innerMinY = minY + thickness;
            long innerMaxY = maxY - thickness;

            for (int py = clipMinY; py <= clipMaxY; py++)
            {
                uint* row = canvas + ((long)py * width);
                for (int px = clipMinX; px <= clipMaxX; px++)
                {
                    if (!filled &&
                        px >= innerMinX && px <= innerMaxX &&
                        py >= innerMinY && py <= innerMaxY)
                        continue;
                    *(row + px) = colorVal;
                }
            }
        }

        // SHAPE TOOLS: axis-aligned ellipse inscribed in the anchor bounding box.
        // Pixel (px, py) is inside when ((px-cx)/rx)^2 + ((py-cy)/ry)^2 <= 1 in
        // double math over the box center/half-extents. An outline is the
        // morphological ring: an inside pixel is kept when the square of radius
        // `thickness` around it is NOT fully inside the ellipse. Because the
        // ellipse is convex, that square lies inside exactly when its four
        // corners do, so the ring test is four extra inclusion checks - and any
        // inside pixel with an outside 4-neighbor is always kept (a corner
        // beyond that neighbor would prove the neighbor inside by convexity),
        // so the ring can never develop gaps, even for extremely eccentric
        // ellipses where shrinking both radii by `thickness` would overshoot
        // the true band near the flat sides. A ring thicker than a radius
        // degrades to the solid fill. A zero-width/height box degrades to its
        // (filled) bounding-box line.
        public static unsafe void DrawEllipse(uint* canvas, int width, int height, int x0, int y0, int x1, int y1, uint colorVal, int thickness, bool filled)
        {
            if (canvas == null) throw new System.ArgumentNullException(nameof(canvas));
            if (width <= 0) throw new System.ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new System.ArgumentOutOfRangeException(nameof(height));
            if (thickness <= 0) throw new System.ArgumentOutOfRangeException(nameof(thickness));

            long minX = FastMath.FastMin(x0, x1);
            long maxX = FastMath.FastMax(x0, x1);
            long minY = FastMath.FastMin(y0, y1);
            long maxY = FastMath.FastMax(y0, y1);

            double radiusX = (maxX - minX) / 2.0;
            double radiusY = (maxY - minY) / 2.0;
            if (radiusX == 0.0 || radiusY == 0.0)
            {
                DrawRectangle(canvas, width, height, x0, y0, x1, y1, colorVal, thickness, true);
                return;
            }

            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;

            int clipMinX = (int)(minX > 0 ? minX : 0);
            int clipMaxX = (int)(maxX < width - 1 ? maxX : width - 1);
            int clipMinY = (int)(minY > 0 ? minY : 0);
            int clipMaxY = (int)(maxY < height - 1 ? maxY : height - 1);

            for (int py = clipMinY; py <= clipMaxY; py++)
            {
                uint* row = canvas + ((long)py * width);
                for (int px = clipMinX; px <= clipMaxX; px++)
                {
                    if (!IsInsideEllipse(px, py, centerX, centerY, radiusX, radiusY)) continue;
                    if (!filled &&
                        IsInsideEllipse(px - thickness, py - thickness, centerX, centerY, radiusX, radiusY) &&
                        IsInsideEllipse(px + thickness, py - thickness, centerX, centerY, radiusX, radiusY) &&
                        IsInsideEllipse(px - thickness, py + thickness, centerX, centerY, radiusX, radiusY) &&
                        IsInsideEllipse(px + thickness, py + thickness, centerX, centerY, radiusX, radiusY))
                        continue;
                    *(row + px) = colorVal;
                }
            }
        }

        private static bool IsInsideEllipse(double px, double py, double centerX, double centerY, double radiusX, double radiusY)
        {
            double normalizedX = (px - centerX) / radiusX;
            double normalizedY = (py - centerY) / radiusY;
            return normalizedX * normalizedX + normalizedY * normalizedY <= 1.0;
        }

        // PERFORMANCE: O(H) Scanline Flood Fill Algorithm
        // Reduces Space Complexity (Stack Memory) from O(W*H) down to O(H).
        // Instead of recursively expanding in 4 directions, this blasts through horizontal lines instantly
        // and only caches vertical jump points, completely preventing Stack Overflow exceptions on large images.
        public static unsafe void FloodFill(uint* canvas, int width, int height, int startX, int startY, uint replacementRgba)
        {
            if (canvas == null) throw new System.ArgumentNullException(nameof(canvas));
            if (width <= 0) throw new System.ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new System.ArgumentOutOfRangeException(nameof(height));
            if (startX < 0 || startX >= width || startY < 0 || startY >= height) return;

            uint targetRgba = *(canvas + (startY * width + startX));
            if (targetRgba == replacementRgba) return;

            int initialStackSize = FastMath.FastMax(
                BackendConfig.Math.StepPositive,
                checked(FastMath.FastMulPow2(height, BackendConfig.Procedural.FloodStackHeightShift)));
            int[] stackX = ArrayPool<int>.Shared.Rent(initialStackSize);
            int[] stackY = ArrayPool<int>.Shared.Rent(initialStackSize);
            int stackPointer = 0;
            try
            {
                PushPoint(ref stackX, ref stackY, ref stackPointer, startX, startY);

                while (stackPointer > 0)
                {
                    stackPointer--;
                    int cx = stackX[stackPointer];
                    int cy = stackY[stackPointer];
                    
                    int lx = cx;
                    while (lx >= 0 && *(canvas + (cy * width + lx)) == targetRgba) lx--;
                    lx++;

                    int rx = cx;
                    while (rx < width && *(canvas + (cy * width + rx)) == targetRgba) rx++;
                    rx--;

                    uint* rowPtr = canvas + (cy * width + lx);
                    for (int x = lx; x <= rx; x++)
                    {
                        *rowPtr = replacementRgba;
                        rowPtr++;
                    }

                    bool spanAbove = false;
                    bool spanBelow = false;

                    for (int x = lx; x <= rx; x++)
                    {
                        if (cy > 0)
                        {
                            bool matches = (*(canvas + ((cy - 1) * width + x)) == targetRgba);
                            if (!spanAbove && matches)
                            {
                                PushPoint(ref stackX, ref stackY, ref stackPointer, x, cy - 1);
                                spanAbove = true;
                            }
                            else if (spanAbove && !matches) spanAbove = false;
                        }

                        if (cy < height - 1)
                        {
                            bool matches = (*(canvas + ((cy + 1) * width + x)) == targetRgba);
                            if (!spanBelow && matches)
                            {
                                PushPoint(ref stackX, ref stackY, ref stackPointer, x, cy + 1);
                                spanBelow = true;
                            }
                            else if (spanBelow && !matches) spanBelow = false;
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(stackX);
                ArrayPool<int>.Shared.Return(stackY);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PushPoint(ref int[] stackX, ref int[] stackY, ref int count, int x, int y)
        {
            if (count == stackX.Length)
            {
                int newSize = checked(stackX.Length * BackendConfig.Procedural.StackGrowthMultiplier);
                int[] newStackX = ArrayPool<int>.Shared.Rent(newSize);
                int[] newStackY = ArrayPool<int>.Shared.Rent(newSize);
                System.Array.Copy(stackX, newStackX, count);
                System.Array.Copy(stackY, newStackY, count);
                ArrayPool<int>.Shared.Return(stackX);
                ArrayPool<int>.Shared.Return(stackY);
                stackX = newStackX;
                stackY = newStackY;
            }

            stackX[count] = x;
            stackY[count] = y;
            count++;
        }
    }
}
