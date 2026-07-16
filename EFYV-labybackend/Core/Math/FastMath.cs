using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Math
{
    public static class FastMath
    {
        // PERFORMANCE: Optimized circular distribution angle (in radians)
        // Completely bypasses degrees/radians conversion overhead
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetCircleDistributionAngleRad(int index, int totalCount)
        {
            if (totalCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(totalCount));
            return (index / (float)totalCount) * BackendConfig.Math.TwoPI;
        }

        // Bitwise Absolute Value - Bypasses branching (if statements) entirely for ultra-fast CPU pipelining
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Abs(int val)
        {
            int mask = val >> BackendConfig.Math.IntSignBitShift;
            return (val + mask) ^ mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float FastAbs(float val)
        {
            int i = *(int*)&val;
            i &= BackendConfig.Math.FloatSignMask;
            return *(float*)&i;
        }

        public enum FacingDirection { Up, Down, Left, Right }

        // PERFORMANCE: 100% Branchless 4-way direction resolution via memory-cast float analysis
        // Extracts exponent/mantissa directly to bypass all floating point math unit branching overhead.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe FacingDirection Get4WayDirection(float moveX, float moveY)
        {
            uint ix = *(uint*)&moveX;
            uint iy = *(uint*)&moveY;
            
            // Clear sign bits for ultra-fast absolute value
            uint ax = ix & BackendConfig.Math.FloatSignMask;
            uint ay = iy & BackendConfig.Math.FloatSignMask;

            // Extract sign bits (0 for positive, 1 for negative)
            uint sx = ix >> BackendConfig.Math.IntSignBitShift;
            uint sy = iy >> BackendConfig.Math.IntSignBitShift;

            // Float magnitude comparison is perfectly mirrored in integer bitwise representation
            uint isYGreater = ay > ax ? BackendConfig.Math.DirectionVerticalFlag : BackendConfig.Math.DirectionHorizontalFlag;

            // Constructs the Enum directly: 
            // isYGreater == 1 => sy ? 1(Down) : 0(Up)
            // isYGreater == 0 => sx ? 2(Left) : 3(Right)
            uint result = (isYGreater * sy) + ((isYGreater ^ BackendConfig.Math.DirectionVerticalFlag) * (BackendConfig.Math.DirectionHorizontalBias - sx));
            return (FacingDirection)result;
        }

        // PERFORMANCE: Distance Squared check. Bypasses Math.Sqrt() overhead.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSqr(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return (dx * dx) + (dy * dy);
        }

        // PERFORMANCE: Fast branchless floating point overrides
        // FMAX and FMIN are heavily optimized by modern FPUs natively
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastMax(float a, float b)
        {
            return a > b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastMin(float a, float b)
        {
            return a < b ? a : b;
        }

        // Direct comparisons avoid overflow in subtraction-based branchless variants.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FastMin(int a, int b)
        {
            return a < b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FastMax(int a, int b)
        {
            return a > b ? a : b;
        }

        // Branchless Clamp for integers
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FastClamp(int val, int min, int max)
        {
            return FastMax(min, FastMin(max, val));
        }
        
        // Fast Clamp for floats
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastClamp(float val, float min, float max)
        {
            return FastMax(min, FastMin(max, val));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FastCeilToInt(float value)
        {
            int truncated = (int)value;
            return value > truncated ? truncated + BackendConfig.Math.StepPositive : truncated;
        }

        // Branchless Linear Interpolation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastLerp(float a, float b, float t)
        {
            return a + (b - a) * FastClamp(t, BackendConfig.Math.NormalizedMin, BackendConfig.Math.NormalizedMax);
        }

        // Extremely fast and simple String Hashing (FNV-1a algorithm)
        // Used to store strings as 32-bit integers in strict memory structs without allocations
        public static int FastHash(string str)
        {
            if (string.IsNullOrEmpty(str)) return BackendConfig.Serialization.NullHash;
            unchecked
            {
                uint hash = BackendConfig.Math.FnvOffsetBasis;
                for (int i = 0; i < str.Length; i++)
                {
                    hash ^= str[i];
                    hash *= BackendConfig.Math.FnvPrime;
                }
                return (int)hash;
            }
        }

        // Bitwise Division by Power of Two (e.g., dividing by 2, 4, 8, 16)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FastDivPow2(int val, int power)
        {
            return val >> power;
        }

        // Bitwise Multiplication by Power of Two
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FastMulPow2(int val, int power)
        {
            return val << power;
        }

        // Generic fast wrap that avoids double modulo for negative coordinates
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FastWrap(int val, int max)
        {
            if (max <= 0) throw new System.ArgumentOutOfRangeException(nameof(max));
            int r = val % max;
            return r < 0 ? r + max : r;
        }

        // Fast inverse square root with one Newton-Raphson refinement.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float FastInvSqrt(float number)
        {
            float halfNumber = number * BackendConfig.Math.InvSqrtInputHalf;
            int i = *(int*)&number;
            i = BackendConfig.Math.QuakeMagicNumber - (i >> BackendConfig.Math.SingleBitShift);
            float y = *(float*)&i;
            y *= BackendConfig.Math.InvSqrtThreeHalves - (halfNumber * y * y);
            return y;
        }

        // High-performance Vector Normalization
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FastNormalize(ref float x, ref float y)
        {
            float sqrMag = (x * x) + (y * y);
            if (sqrMag == BackendConfig.Math.NormalizedMin) return;
            
            float invSqrt = FastInvSqrt(sqrMag);
            x *= invSqrt;
            y *= invSqrt;
        }

        // PRE-COMPUTED TRIGONOMETRY (DEPRECATED)
        // We have entirely replaced memory-bound LUTs with our pure ALU Taylor Approximation
        // to prevent CPU L1 Cache evictions.

        // PERFORMANCE: Zero-Memory Taylor Series Parabolic Approximation
        // Extremely fast sine approximation without memory array lookups.
        // The accepted precision loss avoids memory access when the CPU cache is heavily contested.
        // Input `x` is in radians from negative pi to pi.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastSinTaylor(float x)
        {
            return FastSinApproxNormalized(WrapRadians(x));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastCosTaylor(float x)
        {
            x = WrapRadians(x) + BackendConfig.Math.PI_HALF;
            if (x > BackendConfig.Math.PI) x -= BackendConfig.Math.TwoPI;
            return FastSinApproxNormalized(x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FastSinCosTaylor(float x, out float sin, out float cos)
        {
            x = WrapRadians(x);
            sin = FastSinApproxNormalized(x);
            float cosineInput = x + BackendConfig.Math.PI_HALF;
            if (cosineInput > BackendConfig.Math.PI) cosineInput -= BackendConfig.Math.TwoPI;
            cos = FastSinApproxNormalized(cosineInput);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float WrapRadians(float x)
        {
            x %= BackendConfig.Math.TwoPI;
            if (x > BackendConfig.Math.PI) return x - BackendConfig.Math.TwoPI;
            if (x < -BackendConfig.Math.PI) return x + BackendConfig.Math.TwoPI;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float FastSinApproxNormalized(float x)
        {
            return x * (BackendConfig.Math.TaylorSinA - BackendConfig.Math.TaylorSinB * FastAbs(x));
        }

        // PERFORMANCE: Fast vector offset generation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetRandomOffset2D(float radius, out float offsetX, out float offsetY)
        {
            if (radius < BackendConfig.Math.NormalizedMin || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new System.ArgumentOutOfRangeException(nameof(radius));
            FastRandom.InsideUnitCircle(out float randX, out float randY);
            offsetX = randX * radius;
            offsetY = randY * radius;
        }
    }
}
