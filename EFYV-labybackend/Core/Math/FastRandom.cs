using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Math
{
    // Instance-based XOR-Shift 32-bit PRNG state.
    // Produces the EXACT same stream as the static FastRandom facade below (which delegates to a
    // shared instance), so consumers that need an isolated deterministic stream (tools, generators)
    // no longer have to duplicate the xorshift line-for-line.
    public struct FastRandomState
    {
        // Number of value bits in the 32-bit state; used by the Lemire multiply-shift reduction.
        private const int UIntBitCount = 32;

        private uint state;

        public FastRandomState(uint seed)
        {
            state = seed == BackendConfig.Random.InvalidSeed ? BackendConfig.Random.FallbackSeed : seed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextUInt()
        {
            state ^= state << BackendConfig.Random.XorShiftLeftA;
            state ^= state >> BackendConfig.Random.XorShiftRight;
            state ^= state << BackendConfig.Random.XorShiftLeftB;
            return state;
        }

        // Normalizes one draw into [0, 1) using the configured reciprocal.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextUnitFloat()
        {
            return NextUInt() * BackendConfig.Random.UIntToUnitFloat;
        }

        // Half-open [min, max). Keeps the historical modulo reduction so every seeded sequence
        // pinned by the verification suites stays bit-identical; the span is widened to 64-bit so
        // ranges wider than int.MaxValue no longer overflow (they used to collapse toward min).
        // The modulo carries a negligible bias for spans that do not divide 2^32; use
        // RangeUnbiased when statistical exactness matters more than sequence compatibility.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Range(int min, int max)
        {
            if (max <= min) return min;
            long span = (long)max - min;
            return (int)(min + (long)(NextUInt() % (ulong)span));
        }

        // Half-open [min, max) with Lemire's multiply-shift reduction plus the debiasing rejection
        // step. NOT sequence-compatible with Range: it maps the same raw draw to a different value
        // and may consume extra draws, so only new call sites should adopt it.
        public int RangeUnbiased(int min, int max)
        {
            if (max <= min) return min;
            uint span = (uint)((long)max - min);
            uint draw = NextUInt();
            ulong product = (ulong)draw * span;
            uint low = (uint)product;
            if (low < span)
            {
                // (2^32 - span) % span, computed in 64-bit to avoid the wrap.
                uint threshold = (uint)(((1ul << UIntBitCount) - span) % span);
                while (low < threshold)
                {
                    draw = NextUInt();
                    product = (ulong)draw * span;
                    low = (uint)product;
                }
            }
            return (int)(min + (long)(product >> UIntBitCount));
        }

        // Mirrors the static float overload exactly (always consumes one draw, even for
        // empty or inverted ranges).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Range(float min, float max)
        {
            float normalized = NextUnitFloat();
            return min + (max - min) * normalized;
        }

        // Mirrors the static rejection-sampling disk exactly (same draw order and loop bounds).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InsideUnitCircle(out float x, out float y)
        {
            float sqrMagnitude;
            do
            {
                x = Range(-BackendConfig.Math.NormalizedMax, BackendConfig.Math.NormalizedMax);
                y = Range(-BackendConfig.Math.NormalizedMax, BackendConfig.Math.NormalizedMax);
                sqrMagnitude = (x * x) + (y * y);
            }
            while (sqrMagnitude > BackendConfig.Math.NormalizedMax ||
                   sqrMagnitude == BackendConfig.Math.NormalizedMin);
        }
    }

    // Ultra-optimized XOR-Shift 32-bit PRNG (Pseudo-Random Number Generator).
    // Bypasses the heavy C++ native interop of UnityEngine.Random and the ThreadSafe locks of System.Random.
    // Capable of producing millions of random numbers per frame with virtually 0 CPU time.
    // Static facade over one shared FastRandomState; seeds and sequences are identical to the
    // historical implementation.
    public static class FastRandom
    {
        private static FastRandomState sharedState = new FastRandomState(BackendConfig.Random.DefaultSeed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Next()
        {
            return sharedState.NextUInt();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetSeed(uint seed)
        {
            sharedState = new FastRandomState(seed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Range(int min, int max)
        {
            return sharedState.Range(min, max);
        }

        // Lemire multiply-shift variant; see FastRandomState.RangeUnbiased for the contract.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RangeUnbiased(int min, int max)
        {
            return sharedState.RangeUnbiased(min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Range(float min, float max)
        {
            return sharedState.Range(min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void InsideUnitCircle(out float x, out float y)
        {
            sharedState.InsideUnitCircle(out x, out y);
        }
    }
}
