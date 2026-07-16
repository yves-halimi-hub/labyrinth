using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Math
{
    // Ultra-optimized XOR-Shift 32-bit PRNG (Pseudo-Random Number Generator).
    // Bypasses the heavy C++ native interop of UnityEngine.Random and the ThreadSafe locks of System.Random.
    // Capable of producing millions of random numbers per frame with virtually 0 CPU time.
    public static class FastRandom
    {
        private static uint state = BackendConfig.Random.DefaultSeed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Next()
        {
            state ^= state << BackendConfig.Random.XorShiftLeftA;
            state ^= state >> BackendConfig.Random.XorShiftRight;
            state ^= state << BackendConfig.Random.XorShiftLeftB;
            return state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetSeed(uint seed)
        {
            state = seed == BackendConfig.Random.InvalidSeed ? BackendConfig.Random.FallbackSeed : seed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Range(int min, int max)
        {
            if (max <= min) return min;
            return min + (int)(Next() % (max - min));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Range(float min, float max)
        {
            // Normalize Next() into the unit interval with the configured reciprocal.
            float normalized = Next() * BackendConfig.Random.UIntToUnitFloat;
            return min + (max - min) * normalized;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void InsideUnitCircle(out float x, out float y)
        {
            // Rejection sampling is uniform over the disk, bounded, and avoids trig/square-root work.
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
}
