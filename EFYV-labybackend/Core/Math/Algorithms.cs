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
