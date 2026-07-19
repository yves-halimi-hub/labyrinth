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

            // The horizontal pass converts source pixels to alpha-premultiplied bytes so
            // that fully transparent neighbors contribute no color; scratch therefore
            // holds premultiplied averages. The vertical pass sums those premultiplied
            // bytes directly and un-premultiplies once when packing the destination,
            // which keeps transparent sprite edges from darkening toward halo colors.
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
                    AddPixelPremultiplied(*(src + y * width + clampedX), ref rSum, ref gSum, ref bSum, ref aSum);
                }

                for (int x = 0; x < width; x++)
                {
                    *scratchPtr++ = PackAverages(rSum, gSum, bSum, aSum, reciprocal);

                    int leftX = FastMath.FastMax(0, x - radius);
                    SubtractPixelPremultiplied(*(src + y * width + leftX), ref rSum, ref gSum, ref bSum, ref aSum);

                    int rightX = FastMath.FastMin(
                        width - BackendConfig.Pixel.KernelCenterPixel,
                        x + radius + BackendConfig.Pixel.KernelCenterPixel);
                    AddPixelPremultiplied(*(src + y * width + rightX), ref rSum, ref gSum, ref bSum, ref aSum);
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
                    *(dest + y * width + x) = PackAveragesUnpremultiplied(rSum, gSum, bSum, aSum, reciprocal);

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
        private static void AddPixelPremultiplied(uint pixel, ref int r, ref int g, ref int b, ref int a)
        {
            int alpha = (byte)(pixel >> BackendConfig.Pixel.AlphaShift);
            r += PremultiplyChannel((byte)pixel, alpha);
            g += PremultiplyChannel((byte)(pixel >> BackendConfig.Pixel.GreenShift), alpha);
            b += PremultiplyChannel((byte)(pixel >> BackendConfig.Pixel.BlueShift), alpha);
            a += alpha;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubtractPixelPremultiplied(uint pixel, ref int r, ref int g, ref int b, ref int a)
        {
            int alpha = (byte)(pixel >> BackendConfig.Pixel.AlphaShift);
            r -= PremultiplyChannel((byte)pixel, alpha);
            g -= PremultiplyChannel((byte)(pixel >> BackendConfig.Pixel.GreenShift), alpha);
            b -= PremultiplyChannel((byte)(pixel >> BackendConfig.Pixel.BlueShift), alpha);
            a -= alpha;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PremultiplyChannel(int channel, int alpha)
        {
            return (channel * alpha + BackendConfig.Pixel.OpaqueAlpha / 2) / BackendConfig.Pixel.OpaqueAlpha;
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
        private static uint PackAveragesUnpremultiplied(int r, int g, int b, int a, uint reciprocal)
        {
            uint averageA = AverageChannel(a, reciprocal);
            if (averageA == BackendConfig.Pixel.TransparentAlpha) return 0u;
            uint averageR = UnpremultiplyChannel(AverageChannel(r, reciprocal), averageA);
            uint averageG = UnpremultiplyChannel(AverageChannel(g, reciprocal), averageA);
            uint averageB = UnpremultiplyChannel(AverageChannel(b, reciprocal), averageA);
            return averageR |
                (averageG << BackendConfig.Pixel.GreenShift) |
                (averageB << BackendConfig.Pixel.BlueShift) |
                (averageA << BackendConfig.Pixel.AlphaShift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint UnpremultiplyChannel(uint premultiplied, uint alpha)
        {
            uint channel = (premultiplied * BackendConfig.Pixel.OpaqueAlpha + alpha / 2) / alpha;
            return channel > BackendConfig.Pixel.OpaqueAlpha ? BackendConfig.Pixel.OpaqueAlpha : channel;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint AverageChannel(int sum, uint reciprocal)
        {
            uint average = (uint)(((ulong)(uint)sum * reciprocal + BackendConfig.Pixel.FixedPointRounding) >> BackendConfig.Pixel.FixedPointShift);
            return average > BackendConfig.Pixel.OpaqueAlpha ? BackendConfig.Pixel.OpaqueAlpha : average;
        }

        // --------------------------------------------------------------------
        // Item #7 destructive filter primitives. All three tolerate src == dest
        // (they stage through scratch storage or operate strictly per pixel)
        // and treat "silhouette" as any pixel with alpha > 0.
        // --------------------------------------------------------------------

        // 1px expand of the opaque silhouette: every fully transparent pixel
        // that touches a silhouette pixel in its 8-neighborhood becomes
        // outlineRgba; every other pixel is copied unchanged (silhouette pixels
        // are never recolored).
        public static unsafe void Outline(uint* src, uint* dest, int width, int height, uint outlineRgba)
        {
            int pixelCount = ValidateSurfaceArguments(src, dest, width, height);

            uint[] rentedScratch = ArrayPool<uint>.Shared.Rent(pixelCount);
            try
            {
                fixed (uint* scratch = rentedScratch)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            uint pixel = *(src + y * width + x);
                            scratch[y * width + x] =
                                (byte)(pixel >> BackendConfig.Pixel.AlphaShift) == BackendConfig.Pixel.TransparentAlpha &&
                                TouchesSilhouette(src, width, height, x, y)
                                    ? outlineRgba
                                    : pixel;
                        }
                    }
                    CopyPixels(scratch, dest, pixelCount);
                }
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(rentedScratch);
            }
        }

        // Outline + blur composite: the silhouette plus its 1px expansion is
        // flooded with glowRgba, box-blurred by radius (0 keeps a hard rim),
        // and the original pixels are alpha-composited back on top - a soft
        // halo BEHIND the sprite.
        public static unsafe void Glow(uint* src, uint* dest, int width, int height, uint glowRgba, int radius)
        {
            int pixelCount = ValidateSurfaceArguments(src, dest, width, height);
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));

            uint[] rentedHalo = ArrayPool<uint>.Shared.Rent(pixelCount);
            uint[] rentedScratch = ArrayPool<uint>.Shared.Rent(pixelCount);
            try
            {
                fixed (uint* halo = rentedHalo)
                fixed (uint* scratch = rentedScratch)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            uint pixel = *(src + y * width + x);
                            bool inHalo =
                                (byte)(pixel >> BackendConfig.Pixel.AlphaShift) != BackendConfig.Pixel.TransparentAlpha ||
                                TouchesSilhouette(src, width, height, x, y);
                            halo[y * width + x] = inHalo ? glowRgba : BackendConfig.Deformation.TransparentPixel;
                        }
                    }

                    if (radius > 0) BoxBlur(halo, halo, scratch, width, height, radius);

                    for (int index = 0; index < pixelCount; index++)
                    {
                        uint composed = halo[index];
                        FastMemory.BlendColor(ref composed, *(src + index));
                        *(dest + index) = composed;
                    }
                }
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(rentedHalo);
                ArrayPool<uint>.Shared.Return(rentedScratch);
            }
        }

        // Per-pixel HSV shift: hue rotates by hueDeltaDegrees (wrapping),
        // saturation/value move by their deltas (clamped to [0, 1]). Alpha is
        // preserved and fully transparent pixels are copied bit-exact.
        public static unsafe void ColorShift(
            uint* src,
            uint* dest,
            int width,
            int height,
            float hueDeltaDegrees,
            float saturationDelta,
            float valueDelta)
        {
            int pixelCount = ValidateSurfaceArguments(src, dest, width, height);
            if (float.IsNaN(hueDeltaDegrees) || float.IsInfinity(hueDeltaDegrees))
                throw new ArgumentOutOfRangeException(nameof(hueDeltaDegrees));
            if (float.IsNaN(saturationDelta) || float.IsInfinity(saturationDelta))
                throw new ArgumentOutOfRangeException(nameof(saturationDelta));
            if (float.IsNaN(valueDelta) || float.IsInfinity(valueDelta))
                throw new ArgumentOutOfRangeException(nameof(valueDelta));

            for (int index = 0; index < pixelCount; index++)
            {
                uint pixel = *(src + index);
                byte alpha = (byte)(pixel >> BackendConfig.Pixel.AlphaShift);
                *(dest + index) = alpha == BackendConfig.Pixel.TransparentAlpha
                    ? pixel
                    : ShiftPixelHsv(pixel, alpha, hueDeltaDegrees, saturationDelta, valueDelta);
            }
        }

        private static uint ShiftPixelHsv(
            uint pixel,
            byte alpha,
            float hueDeltaDegrees,
            float saturationDelta,
            float valueDelta)
        {
            float red = (byte)pixel / (float)BackendConfig.Pixel.OpaqueAlpha;
            float green = (byte)(pixel >> BackendConfig.Pixel.GreenShift) / (float)BackendConfig.Pixel.OpaqueAlpha;
            float blue = (byte)(pixel >> BackendConfig.Pixel.BlueShift) / (float)BackendConfig.Pixel.OpaqueAlpha;

            RgbToHsv(red, green, blue, out float hue, out float saturation, out float value);

            hue = (hue + hueDeltaDegrees) % BackendConfig.Effects.HueFullCircleDegrees;
            if (hue < BackendConfig.Math.NormalizedMin) hue += BackendConfig.Effects.HueFullCircleDegrees;
            saturation = FastMath.FastClamp(
                saturation + saturationDelta,
                BackendConfig.Math.NormalizedMin,
                BackendConfig.Math.NormalizedMax);
            value = FastMath.FastClamp(
                value + valueDelta,
                BackendConfig.Math.NormalizedMin,
                BackendConfig.Math.NormalizedMax);

            HsvToRgb(hue, saturation, value, out red, out green, out blue);

            return PackChannel(red) |
                (PackChannel(green) << BackendConfig.Pixel.GreenShift) |
                (PackChannel(blue) << BackendConfig.Pixel.BlueShift) |
                ((uint)alpha << BackendConfig.Pixel.AlphaShift);
        }

        private static void RgbToHsv(
            float red,
            float green,
            float blue,
            out float hue,
            out float saturation,
            out float value)
        {
            float max = FastMath.FastMax(red, FastMath.FastMax(green, blue));
            float min = FastMath.FastMin(red, FastMath.FastMin(green, blue));
            float chroma = max - min;

            value = max;
            saturation = max <= BackendConfig.Math.NormalizedMin
                ? BackendConfig.Math.NormalizedMin
                : chroma / max;

            if (chroma <= BackendConfig.Math.NormalizedMin)
            {
                hue = BackendConfig.Math.NormalizedMin;
                return;
            }

            float sector;
            if (max == red)
            {
                sector = (green - blue) / chroma;
                if (sector < BackendConfig.Math.NormalizedMin)
                    sector += BackendConfig.Effects.HueFullCircleDegrees / BackendConfig.Effects.HueSectorDegrees;
            }
            else if (max == green)
            {
                sector = (blue - red) / chroma + BackendConfig.Effects.HueSectorParityModulus;
            }
            else
            {
                sector = (red - green) / chroma +
                    BackendConfig.Effects.HueSectorParityModulus * BackendConfig.Effects.HueSectorParityModulus;
            }
            hue = sector * BackendConfig.Effects.HueSectorDegrees;
        }

        private static void HsvToRgb(
            float hue,
            float saturation,
            float value,
            out float red,
            out float green,
            out float blue)
        {
            float chroma = value * saturation;
            float sectorPosition = hue / BackendConfig.Effects.HueSectorDegrees;
            float parity = sectorPosition % BackendConfig.Effects.HueSectorParityModulus - BackendConfig.Math.NormalizedMax;
            if (parity < BackendConfig.Math.NormalizedMin) parity = -parity;
            float intermediate = chroma * (BackendConfig.Math.NormalizedMax - parity);
            float offset = value - chroma;

            float sectorRed;
            float sectorGreen;
            float sectorBlue;
            int sector = (int)sectorPosition;
            if (sector <= 0) { sectorRed = chroma; sectorGreen = intermediate; sectorBlue = BackendConfig.Math.NormalizedMin; }
            else if (sector == 1) { sectorRed = intermediate; sectorGreen = chroma; sectorBlue = BackendConfig.Math.NormalizedMin; }
            else if (sector == 2) { sectorRed = BackendConfig.Math.NormalizedMin; sectorGreen = chroma; sectorBlue = intermediate; }
            else if (sector == 3) { sectorRed = BackendConfig.Math.NormalizedMin; sectorGreen = intermediate; sectorBlue = chroma; }
            else if (sector == 4) { sectorRed = intermediate; sectorGreen = BackendConfig.Math.NormalizedMin; sectorBlue = chroma; }
            else { sectorRed = chroma; sectorGreen = BackendConfig.Math.NormalizedMin; sectorBlue = intermediate; }

            red = sectorRed + offset;
            green = sectorGreen + offset;
            blue = sectorBlue + offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PackChannel(float normalized)
        {
            float scaled = normalized * BackendConfig.Pixel.OpaqueAlpha + BackendConfig.Math.ColorHalf;
            if (scaled <= BackendConfig.Math.NormalizedMin) return 0u;
            if (scaled >= BackendConfig.Pixel.OpaqueAlpha) return BackendConfig.Pixel.OpaqueAlpha;
            return (uint)scaled;
        }

        private static unsafe bool TouchesSilhouette(uint* src, int width, int height, int x, int y)
        {
            int expand = BackendConfig.Effects.OutlineExpandRadius;
            int minY = FastMath.FastMax(0, y - expand);
            int maxY = FastMath.FastMin(height - BackendConfig.Pixel.KernelCenterPixel, y + expand);
            int minX = FastMath.FastMax(0, x - expand);
            int maxX = FastMath.FastMin(width - BackendConfig.Pixel.KernelCenterPixel, x + expand);
            for (int neighborY = minY; neighborY <= maxY; neighborY++)
            {
                for (int neighborX = minX; neighborX <= maxX; neighborX++)
                {
                    if (neighborX == x && neighborY == y) continue;
                    uint neighbor = *(src + neighborY * width + neighborX);
                    if ((byte)(neighbor >> BackendConfig.Pixel.AlphaShift) != BackendConfig.Pixel.TransparentAlpha)
                        return true;
                }
            }
            return false;
        }

        private static unsafe int ValidateSurfaceArguments(uint* src, uint* dest, int width, int height)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            return checked(width * height);
        }
    }
}
