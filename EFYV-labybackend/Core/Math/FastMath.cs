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
        // DELIBERATE edge semantics (the branchless bit inspection defines the contract):
        // - The raw sign BIT decides horizontal facing, so -0f behaves as negative (Left) even
        //   though -0f == 0f, and (0f, 0f) resolves to Right.
        // - Magnitudes compare by raw absolute bits, so NaN outranks every finite value and its
        //   sign bit picks the direction; infinities compare like huge finite values.
        // - Equal |x| and |y| ties resolve horizontally (Left/Right).
        // Gameplay only ever feeds finite movement axes; the special values must stay stable
        // because the verification suites pin them as the documented contract.
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

        // Ceiling with DEFINED behavior on the full float range (clamping, never throwing):
        // - NaN returns 0 (a deterministic no-op offset instead of the runtime-defined cast).
        // - Values at or above 2^31 (including +infinity) clamp to int.MaxValue.
        // - Values at or below -2^31 (including -infinity) clamp to int.MinValue.
        // Previously out-of-range inputs leaked the runtime-defined (int) cast and the
        // unconditional +1 could wrap a huge positive input to a large NEGATIVE ceiling.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FastCeilToInt(float value)
        {
            // 2^31 is exactly representable as a float; int.MaxValue itself is not.
            const float IntRangeMagnitude = 2147483648f;
            if (float.IsNaN(value)) return 0;
            if (value >= IntRangeMagnitude) return int.MaxValue;
            if (value <= -IntRangeMagnitude) return int.MinValue;
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

        // High-performance Vector Normalization.
        // The common path (finite, normal squared magnitude) is bit-identical to the historical
        // implementation. Vectors whose squared magnitude would overflow to infinity or underflow
        // below the smallest normal float are first rescaled by their largest absolute component,
        // so huge (e.g. 2e19) and tiny (e.g. 1e-30) finite vectors now normalize correctly instead
        // of returning non-finite garbage or staying unnormalized. An exact-zero vector stays zero.
        // Non-finite inputs still produce non-finite outputs (documented, not a supported input).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FastNormalize(ref float x, ref float y)
        {
            // Smallest positive normal float: below this the quake-style inverse sqrt loses
            // its exponent trick, so we reroute through the rescaled path.
            const float MinNormalFloat = 1.17549435e-38f;
            float sqrMag = (x * x) + (y * y);
            if (sqrMag == BackendConfig.Math.NormalizedMin && x == BackendConfig.Math.NormalizedMin && y == BackendConfig.Math.NormalizedMin) return;

            if (sqrMag >= MinNormalFloat && sqrMag <= float.MaxValue)
            {
                float invSqrt = FastInvSqrt(sqrMag);
                x *= invSqrt;
                y *= invSqrt;
                return;
            }

            // Overflow/underflow rescue: divide by the largest absolute component so the squared
            // magnitude lands in [1, 2], then normalize the rescaled vector.
            float absX = FastAbs(x);
            float absY = FastAbs(y);
            float maxComponent = absX > absY ? absX : absY;
            float rescaledX = x / maxComponent;
            float rescaledY = y / maxComponent;
            float rescaledSqrMag = (rescaledX * rescaledX) + (rescaledY * rescaledY);
            float rescaledInvSqrt = FastInvSqrt(rescaledSqrMag);
            x = rescaledX * rescaledInvSqrt;
            y = rescaledY * rescaledInvSqrt;
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

        // Parabolic sine approximation y = x*(A - B*|x|) with A = 4/pi, B = 4/pi^2 for x in
        // [-pi, pi]. REVIEWED, REFINEMENT DELIBERATELY OMITTED: the classic second step
        // (y = P*(y*|y| - y) + y, P ~= 0.225) would cut the ~0.056 max error to ~0.001 at the
        // cost of one extra multiply-add per call, but every animation reference model in the
        // three verification suites (walk/jitter frame generators and their pixel-exact pins)
        // is built on THIS exact polynomial - adding the term would shift generated pixels and
        // invalidate exported animation fixtures. If precision requirements ever grow, add a
        // separate refined function instead of changing this one. Note |y| can marginally
        // exceed 1 near the extremes (max ~= A^2/(4B) with float rounding), which consumers
        // like GenerateJitterFrame account for when validating derived offsets.
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
