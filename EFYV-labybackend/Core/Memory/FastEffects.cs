using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using EFYVBackend.Core.Math;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Memory
{
    public static class FastEffects
    {
        public static unsafe void BoxBlur(uint* src, uint* dest, int width, int height, int radius)
        {
            int pixelCount = ValidateBlurArguments(src, dest, width, height, radius);
            if (radius == 0)
            {
                CopyPixels(src, dest, pixelCount);
                return;
            }

            uint[] rentedScratch = ArrayPool<uint>.Shared.Rent(pixelCount);
            try
            {
                fixed (uint* scratch = rentedScratch)
                {
                    BoxBlurCore(src, dest, scratch, width, height, radius);
                }
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(rentedScratch);
            }
        }

        public static unsafe void BoxBlur(uint* src, uint* dest, uint* scratch, int width, int height, int radius)
        {
            int pixelCount = ValidateBlurArguments(src, dest, width, height, radius);
            if (radius == 0)
            {
                CopyPixels(src, dest, pixelCount);
                return;
            }
            if (scratch == null) throw new ArgumentNullException(nameof(scratch));
            if (scratch == src || scratch == dest) throw new ArgumentException(null, nameof(scratch));

            BoxBlurCore(src, dest, scratch, width, height, radius);
        }

        private static unsafe int ValidateBlurArguments(uint* src, uint* dest, int width, int height, int radius)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            _ = checked(radius * BackendConfig.Pixel.KernelRadiusMultiplier + BackendConfig.Pixel.KernelCenterPixel);
            return checked(width * height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyPixels(uint* src, uint* dest, int pixelCount)
        {
            long byteCount = checked((long)pixelCount * sizeof(uint));
            Buffer.MemoryCopy(src, dest, byteCount, byteCount);
        }

        private static unsafe void BoxBlurCore(uint* src, uint* dest, uint* scratch, int width, int height, int radius)
        {
            int divisor = checked(BackendConfig.Pixel.KernelRadiusMultiplier * radius + BackendConfig.Pixel.KernelCenterPixel);
            uint reciprocal = (uint)((BackendConfig.Pixel.KernelCenterPixel << BackendConfig.Pixel.FixedPointShift) / divisor);

            uint* scratchPtr = scratch;
            for (int y = 0; y < height; y++)
            {
                int rSum = 0;
                int gSum = 0;
                int bSum = 0;
                int aSum = 0;

                for (int i = -radius; i <= radius; i++)
                {
                    int clampedX = FastMath.FastClamp(i, 0, width - BackendConfig.Pixel.KernelCenterPixel);
                    AddPixel(*(src + y * width + clampedX), ref rSum, ref gSum, ref bSum, ref aSum);
                }

                for (int x = 0; x < width; x++)
                {
                    *scratchPtr++ = PackAverages(rSum, gSum, bSum, aSum, reciprocal);

                    int leftX = FastMath.FastMax(0, x - radius);
                    SubtractPixel(*(src + y * width + leftX), ref rSum, ref gSum, ref bSum, ref aSum);

                    int rightX = FastMath.FastMin(
                        width - BackendConfig.Pixel.KernelCenterPixel,
                        x + radius + BackendConfig.Pixel.KernelCenterPixel);
                    AddPixel(*(src + y * width + rightX), ref rSum, ref gSum, ref bSum, ref aSum);
                }
            }

            for (int x = 0; x < width; x++)
            {
                int rSum = 0;
                int gSum = 0;
                int bSum = 0;
                int aSum = 0;

                for (int i = -radius; i <= radius; i++)
                {
                    int clampedY = FastMath.FastClamp(i, 0, height - BackendConfig.Pixel.KernelCenterPixel);
                    AddPixel(*(scratch + clampedY * width + x), ref rSum, ref gSum, ref bSum, ref aSum);
                }

                for (int y = 0; y < height; y++)
                {
                    *(dest + y * width + x) = PackAverages(rSum, gSum, bSum, aSum, reciprocal);

                    int topY = FastMath.FastMax(0, y - radius);
                    SubtractPixel(*(scratch + topY * width + x), ref rSum, ref gSum, ref bSum, ref aSum);

                    int bottomY = FastMath.FastMin(
                        height - BackendConfig.Pixel.KernelCenterPixel,
                        y + radius + BackendConfig.Pixel.KernelCenterPixel);
                    AddPixel(*(scratch + bottomY * width + x), ref rSum, ref gSum, ref bSum, ref aSum);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddPixel(uint pixel, ref int r, ref int g, ref int b, ref int a)
        {
            r += (byte)pixel;
            g += (byte)(pixel >> BackendConfig.Pixel.GreenShift);
            b += (byte)(pixel >> BackendConfig.Pixel.BlueShift);
            a += (byte)(pixel >> BackendConfig.Pixel.AlphaShift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubtractPixel(uint pixel, ref int r, ref int g, ref int b, ref int a)
        {
            r -= (byte)pixel;
            g -= (byte)(pixel >> BackendConfig.Pixel.GreenShift);
            b -= (byte)(pixel >> BackendConfig.Pixel.BlueShift);
            a -= (byte)(pixel >> BackendConfig.Pixel.AlphaShift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PackAverages(int r, int g, int b, int a, uint reciprocal)
        {
            uint averageR = AverageChannel(r, reciprocal);
            uint averageG = AverageChannel(g, reciprocal);
            uint averageB = AverageChannel(b, reciprocal);
            uint averageA = AverageChannel(a, reciprocal);
            return averageR |
                (averageG << BackendConfig.Pixel.GreenShift) |
                (averageB << BackendConfig.Pixel.BlueShift) |
                (averageA << BackendConfig.Pixel.AlphaShift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint AverageChannel(int sum, uint reciprocal)
        {
            uint average = (uint)(((ulong)(uint)sum * reciprocal + BackendConfig.Pixel.FixedPointRounding) >> BackendConfig.Pixel.FixedPointShift);
            return average > BackendConfig.Pixel.OpaqueAlpha ? BackendConfig.Pixel.OpaqueAlpha : average;
        }
    }
}
