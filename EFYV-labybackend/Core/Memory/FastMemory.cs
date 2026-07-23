using System;
using System.Runtime.CompilerServices;
using EFYV.Runtime.Media;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Memory
{
    public static class FastMemory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Copy<T>(T[] src, T[] dest) where T : unmanaged
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (src.Length > dest.Length) throw new ArgumentException(null, nameof(dest));

            fixed (T* pSrc = src, pDest = dest)
            {
                Buffer.MemoryCopy(pSrc, pDest, dest.Length * sizeof(T), src.Length * sizeof(T));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Clear<T>(T[] array) where T : unmanaged
        {
            if (array == null) throw new ArgumentNullException(nameof(array));

            fixed (T* ptr = array)
            {
                Unsafe.InitBlock(ptr, BackendConfig.Memory.ClearedByte, (uint)(array.Length * sizeof(T)));
            }
        }

        // Unified 32-bit Integer Alpha Blending API
        // Used by LabyMake to flatten canvas layers.
        // Used by Labyrinth to dynamically bake damage overlay colors (like flashing red) directly into sprites.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlendColor(ref uint destRgba, uint srcRgba)
        {
            RgbaCompositor.BlendPixel(ref destRgba, srcRgba);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlendColor(ref uint destRgba, uint srcRgba, byte opacity)
        {
            RgbaCompositor.BlendPixel(ref destRgba, srcRgba, opacity);
        }

        // PERFORMANCE: Bulk Layer Blending
        // Offloads the flattening loop entirely to the C-Level backend.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void BlendLayer(uint* destArray, uint* srcArray, int totalPixels, int transparentAlphaThreshold)
        {
            BlendLayer(destArray, srcArray, totalPixels, transparentAlphaThreshold, BackendConfig.Math.NormalizedMax);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void BlendLayer(uint* destArray, uint* srcArray, int totalPixels, int transparentAlphaThreshold, float layerOpacity)
        {
            if (totalPixels < 0) throw new ArgumentOutOfRangeException(nameof(totalPixels));
            if ((uint)transparentAlphaThreshold > BackendConfig.Pixel.OpaqueAlpha) throw new ArgumentOutOfRangeException(nameof(transparentAlphaThreshold));
            if (float.IsNaN(layerOpacity)) throw new ArgumentOutOfRangeException(nameof(layerOpacity));
            if (totalPixels == 0) return;
            if (destArray == null) throw new ArgumentNullException(nameof(destArray));
            if (srcArray == null) throw new ArgumentNullException(nameof(srcArray));

            float clampedOpacity = Math.FastMath.FastClamp(
                layerOpacity,
                BackendConfig.Math.NormalizedMin,
                BackendConfig.Math.NormalizedMax);
            byte opacity = (byte)(clampedOpacity * BackendConfig.Pixel.OpaqueAlpha + BackendConfig.Math.ColorHalf);
            if (opacity == BackendConfig.Pixel.TransparentAlpha) return;

            RuntimeMediaKernel.BlendRgbaBatch(destArray, srcArray, totalPixels, opacity, (byte)transparentAlphaThreshold);
        }

        // PERFORMANCE AUDIT: Unsafe 2D-to-1D Memory Access
        // Normal array indexing (e.g., `array[i]`) forces C# to check if `i` is out of bounds, costing CPU cycles.
        // We bypass this entirely by using `Unsafe.Add()`. We pass a reference to the very first element (array[0]),
        // and tell the compiler to blindly jump ahead in memory by `(y * width) + x`. No bounds checking, zero overhead.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write2DArrayUnsafe<T>(ref T firstElement, int width, int x, int y, T value)
        {
            Unsafe.Add(ref firstElement, (y * width) + x) = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Read2DArrayUnsafe<T>(ref T firstElement, int width, int x, int y)
        {
            return Unsafe.Add(ref firstElement, (y * width) + x);
        }

        // PERFORMANCE: Nearest Neighbor Scaler for Viewport Zooming
        // Instantly blits a small raw canvas array onto a larger screen buffer without any anti-aliasing (perfect for Pixel Art)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void ScaleBlitNearestNeighbor(
            uint* srcArray, int srcWidth, int srcHeight,
            uint* destArray, int destWidth, int destHeight,
            float scale, int offsetX, int offsetY)
        {
            if (srcArray == null) throw new ArgumentNullException(nameof(srcArray));
            if (destArray == null) throw new ArgumentNullException(nameof(destArray));
            if (srcWidth <= 0) throw new ArgumentOutOfRangeException(nameof(srcWidth));
            if (srcHeight <= 0) throw new ArgumentOutOfRangeException(nameof(srcHeight));
            if (destWidth <= 0) throw new ArgumentOutOfRangeException(nameof(destWidth));
            if (destHeight <= 0) throw new ArgumentOutOfRangeException(nameof(destHeight));
            if (scale <= BackendConfig.Math.NormalizedMin || float.IsNaN(scale) || float.IsInfinity(scale)) throw new ArgumentOutOfRangeException(nameof(scale));

            float invScale = BackendConfig.Math.NormalizedMax / scale;
            uint* destPtr = destArray;
            for (int destY = 0; destY < destHeight; destY++)
            {
                float sourceY = (destY - offsetY) * invScale;
                int srcY = (int)sourceY;
                bool validY = sourceY >= BackendConfig.Math.NormalizedMin && (uint)srcY < (uint)srcHeight;
                
                for (int destX = 0; destX < destWidth; destX++)
                {
                    float sourceX = (destX - offsetX) * invScale;
                    int srcX = (int)sourceX;

                    if (validY && sourceX >= BackendConfig.Math.NormalizedMin && (uint)srcX < (uint)srcWidth)
                    {
                        *destPtr = *(srcArray + (srcY * srcWidth + srcX));
                    }
                    else
                    {
                        *destPtr = default;
                    }
                    destPtr++;
                }
            }
        }

        // PERFORMANCE: Stamp Blitting for Asset Bank / Decals
        // Blits a smaller source array (like an eye or a wheel) onto a specific X/Y coordinate of a destination canvas.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void StampBlit(
            uint* srcArray, int srcWidth, int srcHeight,
            uint* destArray, int destWidth, int destHeight,
            int destX, int destY)
        {
            if (srcArray == null) throw new ArgumentNullException(nameof(srcArray));
            if (destArray == null) throw new ArgumentNullException(nameof(destArray));
            if (srcWidth <= 0) throw new ArgumentOutOfRangeException(nameof(srcWidth));
            if (srcHeight <= 0) throw new ArgumentOutOfRangeException(nameof(srcHeight));
            if (destWidth <= 0) throw new ArgumentOutOfRangeException(nameof(destWidth));
            if (destHeight <= 0) throw new ArgumentOutOfRangeException(nameof(destHeight));

            uint* srcPtr = srcArray;
            for (int sy = 0; sy < srcHeight; sy++)
            {
                int dy = destY + sy;
                if ((uint)dy >= (uint)destHeight) 
                {
                    srcPtr += srcWidth; // Skip entire row
                    continue;
                }

                uint* destRow = destArray + dy * destWidth;
                for (int sx = 0; sx < srcWidth; sx++)
                {
                    int dx = destX + sx;
                    
                    uint srcPixel = *srcPtr;
                    srcPtr++; // Advance source

                    if ((uint)dx >= (uint)destWidth) continue;

                    byte alpha = (byte)(srcPixel >> BackendConfig.Pixel.AlphaShift);
                    if (alpha > BackendConfig.Pixel.TransparentAlpha)
                    {
                        BlendColor(ref *(destRow + dx), srcPixel);
                    }
                }
            }
        }

    }
}
