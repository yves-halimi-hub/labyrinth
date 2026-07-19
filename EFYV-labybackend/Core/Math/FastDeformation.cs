using System;
using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Math
{
    public static class FastDeformation
    {
        // ---------------------------------------------------------
        // PROCEDURAL TOON WALK
        // Generates a single frame of a walking animation from a static sprite.
        // ---------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void GenerateWalkFrame(
            uint* src, uint* dest, int width, int height,
            float timeT, // Normalized animation progress
            int splitY,  // The row index separating body (top) from legs (bottom)
            float bounceAmp, // How much the body bounces up and down
            float strideAmp  // How much the legs stretch/shear forward and backward
        )
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (timeT < BackendConfig.Math.NormalizedMin || timeT > BackendConfig.Math.NormalizedMax || float.IsNaN(timeT))
                throw new ArgumentOutOfRangeException(nameof(timeT));
            if (splitY < 0 || splitY > height) throw new ArgumentOutOfRangeException(nameof(splitY));
            if (float.IsNaN(bounceAmp) || float.IsInfinity(bounceAmp)) throw new ArgumentOutOfRangeException(nameof(bounceAmp));
            if (float.IsNaN(strideAmp) || float.IsInfinity(strideAmp)) throw new ArgumentOutOfRangeException(nameof(strideAmp));

            // PERFORMANCE: Zero-Memory Taylor Series Approximation
            // Replaces memory array lookups with pure ALU math to prevent CPU L1 Cache evictions.
            float bounceT = timeT * BackendConfig.Deformation.BounceFrequencyMultiplier;
            if (bounceT >= BackendConfig.Deformation.NormalizedCycle) bounceT -= BackendConfig.Deformation.NormalizedCycle;
            
            float bounceRad = bounceT * BackendConfig.Math.TwoPI - BackendConfig.Math.PI;
            float strideRad = timeT * BackendConfig.Math.TwoPI - BackendConfig.Math.PI;

            float bounceOffset = FastMath.FastSinApproxNormalized(bounceRad) * bounceAmp;
            float strideOffsetBase = FastMath.FastSinApproxNormalized(strideRad) * strideAmp;

            uint* destPtr = dest;
            for (int y = 0; y < height; y++)
            {
                int srcYBody = y - (int)bounceOffset;
                bool isLegs = y >= splitY;
                int strideOffset = 0;
                if (isLegs)
                {
                    float depth = (y - splitY) / (float)(height - splitY);
                    strideOffset = (int)(strideOffsetBase * depth);
                }

                for (int x = 0; x < width; x++)
                {
                    int srcX = x;
                    int srcY = y;

                    if (isLegs)
                    {
                        int halfWidth = FastMath.FastDivPow2(width, BackendConfig.Math.SingleBitShift);
                        srcX = (x < halfWidth) ? x - strideOffset : x + strideOffset;
                    }
                    else
                    {
                        srcY = srcYBody;
                    }

                    // Blit pixel if within bounds
                    if ((uint)srcX < (uint)width && (uint)srcY < (uint)height)
                    {
                        *destPtr = *(src + (srcY * width + srcX));
                    }
                    else
                    {
                        *destPtr = BackendConfig.Deformation.TransparentPixel;
                    }
                    destPtr++;
                }
            }
        }

        // ---------------------------------------------------------
        // 8-DIRECTIONAL RADIAL JITTER
        // Deforms the image radially using 8 configurable sine waves (one for each 45-degree octant)
        // ---------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void GenerateJitterFrame(
            uint* src, uint* dest, int width, int height,
            float timeT, 
            float* amplitudes, // Array of 8 floats
            float* frequencies // Array of 8 floats
        )
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (amplitudes == null) throw new ArgumentNullException(nameof(amplitudes));
            if (frequencies == null) throw new ArgumentNullException(nameof(frequencies));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (timeT < BackendConfig.Math.NormalizedMin || timeT > BackendConfig.Math.NormalizedMax || float.IsNaN(timeT))
                throw new ArgumentOutOfRangeException(nameof(timeT));

            int cx = FastMath.FastDivPow2(width, BackendConfig.Math.SingleBitShift);
            int cy = FastMath.FastDivPow2(height, BackendConfig.Math.SingleBitShift);

            // Pre-calculate the 8 wave offsets for this specific frame using Taylor ALU approximation
            float* frameOffsets = stackalloc float[BackendConfig.Deformation.OctantCount];
            for (int i = 0; i < BackendConfig.Deformation.OctantCount; i++)
            {
                if (float.IsNaN(amplitudes[i]) || float.IsInfinity(amplitudes[i]))
                    throw new ArgumentOutOfRangeException(nameof(amplitudes));
                if (float.IsNaN(frequencies[i]) || float.IsInfinity(frequencies[i]))
                    throw new ArgumentOutOfRangeException(nameof(frequencies));
                // Validate the POST-multiplication phase: finite inputs can still overflow to
                // infinity after the TwoPI multiplication (e.g. frequency = float.MaxValue),
                // which used to wrap into NaN offsets and silently wipe the whole frame to
                // transparent through the runtime-defined (int)NaN truncation.
                float phaseRad = timeT * frequencies[i] * BackendConfig.Math.TwoPI - BackendConfig.Math.PI;
                if (float.IsNaN(phaseRad) || float.IsInfinity(phaseRad))
                    throw new ArgumentOutOfRangeException(nameof(frequencies));
                float rad = FastMath.WrapRadians(phaseRad);
                frameOffsets[i] = FastMath.FastSinApproxNormalized(rad) * amplitudes[i];
                // Defensive final-offset validation (the sine approximation can slightly
                // exceed |1|, so a float.MaxValue amplitude could overflow the product).
                if (float.IsNaN(frameOffsets[i]) || float.IsInfinity(frameOffsets[i]))
                    throw new ArgumentOutOfRangeException(nameof(amplitudes));
            }

            uint* destPtr = dest;
            for (int y = 0; y < height; y++)
            {
                int dy = y - cy;
                for (int x = 0; x < width; x++)
                {
                    int dx = x - cx;
                    
                    // Very fast octant resolution (0 to 7) without using expensive Atan2
                    int octant = GetOctant(dx, dy);

                    // If center pixel, no displacement
                    if (dx == 0 && dy == 0)
                    {
                        *destPtr = *(src + (y * width + x));
                        destPtr++;
                        continue;
                    }

                    float offset = frameOffsets[octant];
                    
                    // Fast normalization
                    float fdx = dx;
                    float fdy = dy;
                    FastMath.FastNormalize(ref fdx, ref fdy);

                    int srcX = x + (int)(fdx * offset);
                    int srcY = y + (int)(fdy * offset);

                    // Unsigned cast bounds check is 2x faster than checking >= 0 and < bounds
                    if ((uint)srcX < (uint)width && (uint)srcY < (uint)height)
                    {
                        *destPtr = *(src + (srcY * width + srcX));
                    }
                    else
                    {
                        *destPtr = BackendConfig.Deformation.TransparentPixel;
                    }
                    destPtr++;
                }
            }
        }

        // ---------------------------------------------------------
        // BOB / BREATHE (item #10 preset)
        // One sine cycle of vertical bob (translation) plus an optional
        // "breathe" vertical squash-and-stretch anchored at the bottom row
        // (feet stay planted). timeT = 0 is the identity frame.
        // ---------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void GenerateBobBreatheFrame(
            uint* src, uint* dest, int width, int height,
            float timeT,
            float bobAmp,     // vertical translation amplitude in pixels
            float breatheAmp  // vertical scale amplitude, |amp| <= MaxBreatheAmplitude
        )
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (timeT < BackendConfig.Math.NormalizedMin || timeT > BackendConfig.Math.NormalizedMax || float.IsNaN(timeT))
                throw new ArgumentOutOfRangeException(nameof(timeT));
            if (float.IsNaN(bobAmp) || float.IsInfinity(bobAmp)) throw new ArgumentOutOfRangeException(nameof(bobAmp));
            if (float.IsNaN(breatheAmp) ||
                breatheAmp < -BackendConfig.Deformation.MaxBreatheAmplitude ||
                breatheAmp > BackendConfig.Deformation.MaxBreatheAmplitude)
                throw new ArgumentOutOfRangeException(nameof(breatheAmp));

            float rad = FastMath.WrapRadians(timeT * BackendConfig.Math.TwoPI - BackendConfig.Math.PI);
            float wave = FastMath.FastSinApproxNormalized(rad);
            float bobOffset = wave * bobAmp;
            // The breathe cap guarantees the scale stays strictly positive.
            float scaleY = BackendConfig.Math.NormalizedMax + wave * breatheAmp;
            int anchorY = height - 1;

            uint* destPtr = dest;
            for (int y = 0; y < height; y++)
            {
                // Inverse mapping: which source row shows at destination row y.
                int srcY = anchorY + (int)((y - anchorY + bobOffset) / scaleY);
                if ((uint)srcY < (uint)height)
                {
                    uint* srcRow = src + (srcY * width);
                    for (int x = 0; x < width; x++)
                    {
                        *destPtr = *(srcRow + x);
                        destPtr++;
                    }
                }
                else
                {
                    for (int x = 0; x < width; x++)
                    {
                        *destPtr = BackendConfig.Deformation.TransparentPixel;
                        destPtr++;
                    }
                }
            }
        }

        // ---------------------------------------------------------
        // SHAKE / HIT-FLASH (item #10 preset)
        // Horizontal shake (ShakeOscillations sine cycles) and a white flash,
        // both decaying linearly over the cycle: the flash is strongest at
        // timeT = 0 (the impact) and both vanish as timeT approaches 1.
        // ---------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void GenerateShakeFlashFrame(
            uint* src, uint* dest, int width, int height,
            float timeT,
            float shakeAmp,      // horizontal shake amplitude in pixels
            float flashStrength  // white-flash strength at impact, [0, 1]
        )
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (timeT < BackendConfig.Math.NormalizedMin || timeT > BackendConfig.Math.NormalizedMax || float.IsNaN(timeT))
                throw new ArgumentOutOfRangeException(nameof(timeT));
            if (float.IsNaN(shakeAmp) || float.IsInfinity(shakeAmp)) throw new ArgumentOutOfRangeException(nameof(shakeAmp));
            if (float.IsNaN(flashStrength) ||
                flashStrength < BackendConfig.Math.NormalizedMin ||
                flashStrength > BackendConfig.Math.NormalizedMax)
                throw new ArgumentOutOfRangeException(nameof(flashStrength));

            float decay = BackendConfig.Math.NormalizedMax - timeT;
            float phase = FastMath.WrapRadians(
                timeT * BackendConfig.Deformation.ShakeOscillations * BackendConfig.Math.TwoPI - BackendConfig.Math.PI);
            int shakeOffset = (int)(FastMath.FastSinApproxNormalized(phase) * shakeAmp * decay);
            // Fixed-point flash numerator (0..255): c' = c + (255 - c) * flash / 255.
            int flashNumerator = (int)(flashStrength * decay * BackendConfig.Math.ColorMaxByte);

            uint* destPtr = dest;
            for (int y = 0; y < height; y++)
            {
                uint* srcRow = src + (y * width);
                for (int x = 0; x < width; x++)
                {
                    int srcX = x - shakeOffset;
                    uint pixel = (uint)srcX < (uint)width
                        ? *(srcRow + srcX)
                        : BackendConfig.Deformation.TransparentPixel;

                    uint alpha = (pixel >> BackendConfig.Pixel.AlphaShift) & BackendConfig.Math.ColorMaxByte;
                    if (flashNumerator > 0 && alpha != BackendConfig.Pixel.TransparentAlpha)
                    {
                        uint red = pixel & BackendConfig.Math.ColorMaxByte;
                        uint green = (pixel >> BackendConfig.Pixel.GreenShift) & BackendConfig.Math.ColorMaxByte;
                        uint blue = (pixel >> BackendConfig.Pixel.BlueShift) & BackendConfig.Math.ColorMaxByte;
                        red += (uint)(((BackendConfig.Math.ColorMaxByte - red) * (uint)flashNumerator) / BackendConfig.Math.ColorMaxByte);
                        green += (uint)(((BackendConfig.Math.ColorMaxByte - green) * (uint)flashNumerator) / BackendConfig.Math.ColorMaxByte);
                        blue += (uint)(((BackendConfig.Math.ColorMaxByte - blue) * (uint)flashNumerator) / BackendConfig.Math.ColorMaxByte);
                        pixel = red |
                            (green << BackendConfig.Pixel.GreenShift) |
                            (blue << BackendConfig.Pixel.BlueShift) |
                            (alpha << BackendConfig.Pixel.AlphaShift);
                    }

                    *destPtr = pixel;
                    destPtr++;
                }
            }
        }

        // 100% Branchless Bitwise Octant Resolution
        // Uses BackendConfig.Deformation.PackedOctantLookup to resolve directions without Atan2 or branches.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetOctant(int dx, int dy)
        {
            // Extract sign bits: 0 if positive, 1 if negative
            uint sx = (uint)dx >> BackendConfig.Math.IntSignBitShift;
            uint sy = (uint)dy >> BackendConfig.Math.IntSignBitShift;

            // Bitwise absolute value
            int adx = (dx + (dx >> BackendConfig.Math.IntSignBitShift)) ^ (dx >> BackendConfig.Math.IntSignBitShift);
            int ady = (dy + (dy >> BackendConfig.Math.IntSignBitShift)) ^ (dy >> BackendConfig.Math.IntSignBitShift);

            // 1 if ady >= adx, 0 otherwise
            int diff = ady - adx;
            uint isYGreater = (uint)~diff >> BackendConfig.Math.IntSignBitShift;

            // Construct 3-bit index (0 to 7)
            int index = (int)((sy << BackendConfig.Deformation.OctantYSignShift) | (sx << BackendConfig.Deformation.OctantXSignShift) | isYGreater);

            // Extract the 3-bit answer from our packed integer LUT
            return (BackendConfig.Deformation.PackedOctantLookup >> (index * BackendConfig.Deformation.BitsPerOctant)) & BackendConfig.Deformation.OctantMask;
        }
    }
}
