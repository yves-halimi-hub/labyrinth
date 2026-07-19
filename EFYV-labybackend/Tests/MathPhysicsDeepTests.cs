using System;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Physics;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        // ------------------------------------------------------------------
        // math float edges and facing corners
        // ------------------------------------------------------------------
        private static void TestMathPhysicsFloatEdgeContracts()
        {
            float positiveNaN = BitConverter.Int32BitsToSingle(0x7FC00000);
            float negativeNaN = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00000));
            float negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));

            // Signed zero: the raw sign BIT decides horizontal facing, so -0f behaves as negative
            // even though -0f == 0f. Documents current behavior.
            AssertEqual(FastMath.FacingDirection.Left, FastMath.Get4WayDirection(negativeZero, 0f));
            AssertEqual(FastMath.FacingDirection.Right, FastMath.Get4WayDirection(0f, negativeZero));
            AssertEqual(FastMath.FacingDirection.Left, FastMath.Get4WayDirection(negativeZero, negativeZero));
            AssertEqual(FastMath.FacingDirection.Right, FastMath.Get4WayDirection(5f, negativeZero));

            // NaN is compared by raw magnitude bits (larger than any finite float), and its sign
            // bit picks the direction. Documents current behavior.
            AssertEqual(FastMath.FacingDirection.Right, FastMath.Get4WayDirection(positiveNaN, 1f));
            AssertEqual(FastMath.FacingDirection.Left, FastMath.Get4WayDirection(negativeNaN, 1f));
            AssertEqual(FastMath.FacingDirection.Up, FastMath.Get4WayDirection(1f, positiveNaN));
            AssertEqual(FastMath.FacingDirection.Down, FastMath.Get4WayDirection(1f, negativeNaN));

            AssertEqual(FastMath.FacingDirection.Left, FastMath.Get4WayDirection(float.NegativeInfinity, 100f));
            AssertEqual(FastMath.FacingDirection.Up, FastMath.Get4WayDirection(100f, float.PositiveInfinity));
            // Equal infinite magnitudes tie, and ties resolve horizontally.
            AssertEqual(FastMath.FacingDirection.Right, FastMath.Get4WayDirection(float.PositiveInfinity, float.NegativeInfinity));
            AssertEqual(FastMath.FacingDirection.Left, FastMath.Get4WayDirection(float.NegativeInfinity, float.PositiveInfinity));

            // Ternary float min/max: on a false comparison the SECOND operand is returned, so a NaN
            // in slot 'a' is dropped while a NaN in slot 'b' propagates, and -0f/+0f ties keep 'b'.
            AssertEqual(1f, FastMath.FastMax(positiveNaN, 1f));
            Assert(float.IsNaN(FastMath.FastMax(1f, positiveNaN)));
            AssertEqual(1f, FastMath.FastMin(positiveNaN, 1f));
            Assert(float.IsNaN(FastMath.FastMin(1f, positiveNaN)));
            AssertEqual(0, BitConverter.SingleToInt32Bits(FastMath.FastMin(negativeZero, 0f)));
            AssertEqual(unchecked((int)0x80000000), BitConverter.SingleToInt32Bits(FastMath.FastMin(0f, negativeZero)));
            AssertEqual(0, BitConverter.SingleToInt32Bits(FastMath.FastMax(negativeZero, 0f)));
            AssertEqual(unchecked((int)0x80000000), BitConverter.SingleToInt32Bits(FastMath.FastMax(0f, negativeZero)));

            Assert(float.IsNaN(FastMath.FastClamp(positiveNaN, 0f, 1f)));
            AssertEqual(2f, FastMath.FastClamp(2f, 0f, positiveNaN)); // NaN max bound is silently ignored
            AssertEqual(0.5f, FastMath.FastClamp(0.5f, positiveNaN, 1f)); // NaN min bound is silently ignored
            Assert(float.IsNaN(FastMath.FastLerp(1f, 2f, positiveNaN)));

            AssertEqual(0, FastMath.FastCeilToInt(negativeZero));
            AssertEqual(-2147483648, FastMath.FastCeilToInt(-2147483648f));
            // FIXED contract: NaN maps to a deterministic 0 and out-of-range magnitudes clamp
            // to the nearest representable int. Previously the runtime-defined cast leaked
            // through and the unconditional +1 wrapped huge POSITIVE inputs to large NEGATIVE
            // ceilings.
            AssertEqual(0, FastMath.FastCeilToInt(positiveNaN));
            AssertEqual(0, FastMath.FastCeilToInt(negativeNaN));
            AssertEqual(int.MinValue, FastMath.FastCeilToInt(float.NegativeInfinity));
            AssertEqual(int.MinValue, FastMath.FastCeilToInt(-1e10f));
            AssertEqual(int.MaxValue, FastMath.FastCeilToInt(2147483648f));
            AssertEqual(int.MaxValue, FastMath.FastCeilToInt(3e9f));
            AssertEqual(int.MaxValue, FastMath.FastCeilToInt(1e10f));
            AssertEqual(int.MaxValue, FastMath.FastCeilToInt(float.PositiveInfinity));
            // Boundary floats just inside the clamp threshold still ceil exactly.
            AssertEqual(2147483520, FastMath.FastCeilToInt(2147483520f));
            AssertEqual(-2147483520, FastMath.FastCeilToInt(-2147483520f));

            AssertEqual(0f, FastMath.WrapRadians(BackendConfig.Math.TwoPI));
            AssertEqual(0f, FastMath.WrapRadians(-BackendConfig.Math.TwoPI));
            AssertEqual(BackendConfig.Math.PI, FastMath.WrapRadians(BackendConfig.Math.PI));
            AssertEqual(-BackendConfig.Math.PI, FastMath.WrapRadians(-BackendConfig.Math.PI));
            float[] gnarlyAngles =
            {
                1e6f, -1e6f, 1e10f, -1e10f, 3.4e38f, -3.4e38f, 7.1f, -7.1f,
                1e-20f, float.Epsilon, 12345.678f, -12345.678f
            };
            for (int i = 0; i < gnarlyAngles.Length; i++)
            {
                float wrapped = FastMath.WrapRadians(gnarlyAngles[i]);
                AssertEqual(
                    BitConverter.SingleToInt32Bits(MathPhysicsWrapRadians(gnarlyAngles[i])),
                    BitConverter.SingleToInt32Bits(wrapped));
                Assert(wrapped >= -BackendConfig.Math.PI && wrapped <= BackendConfig.Math.PI);
            }
        }

        // ------------------------------------------------------------------
        // normalize inverse sqrt bit-exact reference
        // ------------------------------------------------------------------
        private static void TestMathPhysicsNormalizeBitExact()
        {
            Random random = new Random(0x1B57C0);
            for (int i = 0; i < 5000; i++)
            {
                float x = (float)(random.NextDouble() * 2000d - 1000d);
                float y = (float)(random.NextDouble() * 2000d - 1000d);
                MathPhysicsAssertNormalizeBitExact(x, y);
            }
            float[] scales = { 1e-15f, 1e-6f, 1e-3f, 1e3f, 1e6f, 1e15f, 1e18f };
            for (int scaleIndex = 0; scaleIndex < scales.Length; scaleIndex++)
            {
                for (int i = 0; i < 64; i++)
                {
                    float x = (float)(random.NextDouble() * 2d - 1d) * scales[scaleIndex];
                    float y = (float)(random.NextDouble() * 2d - 1d) * scales[scaleIndex];
                    MathPhysicsAssertNormalizeBitExact(x, y);
                }
            }

            // FIXED: vectors whose squared magnitude would overflow to +infinity are first
            // rescaled by their largest absolute component, so huge vectors now normalize to
            // unit length (within the fast-inverse-sqrt tolerance) instead of going non-finite.
            float hugeX = 2e19f;
            float hugeY = 0f;
            FastMath.FastNormalize(ref hugeX, ref hugeY);
            AssertNear(1f, hugeX, 0.002f);
            AssertEqual(0f, hugeY);
            float hugeNegativeX = -3e19f;
            float hugePositiveY = 3e19f;
            FastMath.FastNormalize(ref hugeNegativeX, ref hugePositiveY);
            AssertNear(-0.70711f, hugeNegativeX, 0.002f);
            AssertNear(0.70711f, hugePositiveY, 0.002f);

            // FIXED: squared magnitudes that underflow below the smallest normal float (or all
            // the way to zero) reroute through the same rescaled path instead of staying put.
            float tinyX = 1e-30f;
            float tinyY = 0f;
            FastMath.FastNormalize(ref tinyX, ref tinyY);
            AssertNear(1f, tinyX, 0.002f);
            AssertEqual(0f, tinyY);
            float subnormalX = 3e-39f;
            float subnormalY = -4e-39f;
            FastMath.FastNormalize(ref subnormalX, ref subnormalY);
            AssertNear(0.6f, subnormalX, 0.002f);
            AssertNear(-0.8f, subnormalY, 0.002f);

            // An exact-zero vector still stays exactly zero.
            float zeroInputX = 0f;
            float zeroInputY = 0f;
            FastMath.FastNormalize(ref zeroInputX, ref zeroInputY);
            AssertEqual(0f, zeroInputX);
            AssertEqual(0f, zeroInputY);

            // Non-finite inputs remain unsupported and keep producing non-finite outputs.
            float infiniteX = float.PositiveInfinity;
            float infiniteY = 1f;
            FastMath.FastNormalize(ref infiniteX, ref infiniteY);
            Assert(!float.IsFinite(infiniteX) || !float.IsFinite(infiniteY));
        }

        // ------------------------------------------------------------------
        // random range exact replay and overflow spans
        // ------------------------------------------------------------------
        private static void TestMathPhysicsRandomExactReplay()
        {
            (int Min, int Max)[] intRanges =
            {
                (0, 1), (5, 4), (10, 10), (-8, 9), (0, int.MaxValue),
                (int.MinValue, int.MinValue + 1), (int.MaxValue - 3, int.MaxValue),
                (int.MinValue, int.MaxValue), (-2000000000, 2000000000)
            };
            uint[] seeds = { 1u, 0xDEADBEEFu, 0x7F4A2C11u };
            for (int seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
            {
                for (int rangeIndex = 0; rangeIndex < intRanges.Length; rangeIndex++)
                {
                    FastRandom.SetSeed(seeds[seedIndex]);
                    uint referenceState = seeds[seedIndex] == 0u ? 1u : seeds[seedIndex];
                    for (int draw = 0; draw < 200; draw++)
                    {
                        AssertEqual(
                            MathPhysicsRangeInt(ref referenceState, intRanges[rangeIndex].Min, intRanges[rangeIndex].Max),
                            FastRandom.Range(intRanges[rangeIndex].Min, intRanges[rangeIndex].Max));
                    }
                    AssertEqual(ReferenceXorShift(referenceState), FastRandom.Next());
                }
            }

            // FIXED: the span is now computed as (long)max - min, so ranges wider than
            // int.MaxValue (which used to collapse onto the bottom of the interval - the full
            // range ALWAYS returned min) cover the whole requested interval. The exact draws
            // are pinned by the reference replay above; this checks the coverage itself.
            FastRandom.SetSeed(0xCAFEF00Du);
            bool sawPositive = false;
            for (int draw = 0; draw < 100; draw++)
            {
                if (FastRandom.Range(int.MinValue, int.MaxValue) > 0) sawPositive = true;
            }
            Assert(sawPositive);
            FastRandom.SetSeed(0xCAFEF00Du);
            bool sawBeyondOldCollapseBand = false;
            for (int draw = 0; draw < 100; draw++)
            {
                int value = FastRandom.Range(-2000000000, 2000000000);
                Assert(value >= -2000000000 && value < 2000000000);
                if (value > -1705032705) sawBeyondOldCollapseBand = true;
            }
            Assert(sawBeyondOldCollapseBand);

            (float Min, float Max)[] floatRanges =
            {
                (0f, 1f), (5f, 5f), (10f, -10f), (-17.25f, 93.5f),
                (-1e30f, 1e30f), (float.MinValue, float.MaxValue)
            };
            for (int seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
            {
                for (int rangeIndex = 0; rangeIndex < floatRanges.Length; rangeIndex++)
                {
                    FastRandom.SetSeed(seeds[seedIndex]);
                    uint referenceState = seeds[seedIndex] == 0u ? 1u : seeds[seedIndex];
                    for (int draw = 0; draw < 200; draw++)
                    {
                        AssertEqual(
                            BitConverter.SingleToInt32Bits(
                                MathPhysicsRangeFloat(ref referenceState, floatRanges[rangeIndex].Min, floatRanges[rangeIndex].Max)),
                            BitConverter.SingleToInt32Bits(
                                FastRandom.Range(floatRanges[rangeIndex].Min, floatRanges[rangeIndex].Max)));
                    }
                    AssertEqual(ReferenceXorShift(referenceState), FastRandom.Next());
                }
            }

            // Unlike the int overload, the float overload consumes PRNG state even when the bounds
            // are equal. Documents current behavior.
            FastRandom.SetSeed(42u);
            AssertEqual(5f, FastRandom.Range(5f, 5f));
            AssertEqual(ReferenceXorShift(ReferenceXorShift(42u)), FastRandom.Next());

            FastRandom.SetSeed(0xFEEDBEEFu);
            uint circleState = 0xFEEDBEEFu;
            for (int i = 0; i < 20000; i++)
            {
                FastRandom.InsideUnitCircle(out float actualX, out float actualY);
                MathPhysicsInsideUnitCircle(ref circleState, out float expectedX, out float expectedY);
                AssertEqual(BitConverter.SingleToInt32Bits(expectedX), BitConverter.SingleToInt32Bits(actualX));
                AssertEqual(BitConverter.SingleToInt32Bits(expectedY), BitConverter.SingleToInt32Bits(actualY));
            }
            AssertEqual(ReferenceXorShift(circleState), FastRandom.Next());

            FastRandom.SetSeed(777u);
            uint offsetState = 777u;
            for (int i = 0; i < 2000; i++)
            {
                FastMath.GetRandomOffset2D(123.5f, out float actualX, out float actualY);
                MathPhysicsInsideUnitCircle(ref offsetState, out float expectedX, out float expectedY);
                AssertEqual(BitConverter.SingleToInt32Bits(expectedX * 123.5f), BitConverter.SingleToInt32Bits(actualX));
                AssertEqual(BitConverter.SingleToInt32Bits(expectedY * 123.5f), BitConverter.SingleToInt32Bits(actualY));
            }
            AssertEqual(ReferenceXorShift(offsetState), FastRandom.Next());

            // Radius zero is accepted and produces exact zero offsets.
            FastRandom.SetSeed(31337u);
            FastMath.GetRandomOffset2D(0f, out float zeroX, out float zeroY);
            AssertEqual(0f, zeroX);
            AssertEqual(0f, zeroY);
        }

        // ------------------------------------------------------------------
        // flood fill guards, no-ops, and stack growth
        // ------------------------------------------------------------------
        private static unsafe void TestMathPhysicsFloodFillAdversarial()
        {
            AssertThrows<ArgumentNullException>(MathPhysicsFloodFillNullCanvas);
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsFloodFillWithDims(0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsFloodFillWithDims(1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsFloodFillWithDims(-1, 1));

            // Filling with the target's own color is a no-op even on a fully connected canvas.
            uint[] noOp = CreateGuardedPixels(64, GuardPixelA, GuardPixelB);
            for (int i = 0; i < 64; i++) noOp[i + 1] = 5u;
            fixed (uint* pixels = noOp) Algorithms.FloodFill(pixels + 1, 8, 8, 3, 3, 5u);
            AssertPixelGuards(noOp, GuardPixelA, GuardPixelB);
            for (int i = 0; i < 64; i++) AssertEqual(5u, noOp[i + 1]);

            // Far out-of-bounds start coordinates return silently without touching pixels.
            int[] badStarts = { int.MinValue, -1, 8, int.MaxValue };
            for (int badIndex = 0; badIndex < badStarts.Length; badIndex++)
            {
                fixed (uint* pixels = noOp)
                {
                    Algorithms.FloodFill(pixels + 1, 8, 8, badStarts[badIndex], 0, 9u);
                    Algorithms.FloodFill(pixels + 1, 8, 8, 0, badStarts[badIndex], 9u);
                }
            }
            AssertPixelGuards(noOp, GuardPixelA, GuardPixelB);
            for (int i = 0; i < 64; i++) AssertEqual(5u, noOp[i + 1]);

            // Single-column and single-row canvases fill only the contiguous run.
            uint[] runPattern = { 1u, 1u, 2u, 1u, 1u, 1u, 2u, 1u, 1u };
            uint[] column = CreateGuardedPixels(9, GuardPixelA, GuardPixelB);
            uint[] expectedColumn = new uint[9];
            for (int i = 0; i < 9; i++)
            {
                column[i + 1] = runPattern[i];
                expectedColumn[i] = runPattern[i];
            }
            ReferenceFloodFill(expectedColumn, 1, 9, 0, 4, 7u);
            fixed (uint* pixels = column) Algorithms.FloodFill(pixels + 1, 1, 9, 0, 4, 7u);
            AssertPixelGuards(column, GuardPixelA, GuardPixelB);
            for (int i = 0; i < 9; i++) AssertEqual(expectedColumn[i], column[i + 1]);

            uint[] row = CreateGuardedPixels(9, GuardPixelB, GuardPixelA);
            uint[] expectedRow = new uint[9];
            for (int i = 0; i < 9; i++)
            {
                row[i + 1] = runPattern[i];
                expectedRow[i] = runPattern[i];
            }
            ReferenceFloodFill(expectedRow, 9, 1, 4, 0, 7u);
            fixed (uint* pixels = row) Algorithms.FloodFill(pixels + 1, 9, 1, 4, 0, 7u);
            AssertPixelGuards(row, GuardPixelB, GuardPixelA);
            for (int i = 0; i < 9; i++) AssertEqual(expectedRow[i], row[i + 1]);

            // Comb pattern: the middle row seeds ~1025 pending points at once, forcing the pooled
            // stack (initial capacity height << 2 = 12) through several growth cycles.
            const int combWidth = 1025;
            const int combHeight = 3;
            uint[] comb = CreateGuardedPixels(combWidth * combHeight, GuardPixelA, GuardPixelB);
            uint[] expectedComb = new uint[combWidth * combHeight];
            for (int x = 0; x < combWidth; x++)
            {
                comb[1 + x] = (x & 1) == 0 ? 1u : 9u;
                comb[1 + combWidth + x] = 1u;
                comb[1 + combWidth * 2 + x] = (x & 1) == 1 ? 1u : 9u;
            }
            for (int i = 0; i < expectedComb.Length; i++) expectedComb[i] = comb[i + 1];
            ReferenceFloodFill(expectedComb, combWidth, combHeight, 512, 1, 3u);
            fixed (uint* pixels = comb) Algorithms.FloodFill(pixels + 1, combWidth, combHeight, 512, 1, 3u);
            AssertPixelGuards(comb, GuardPixelA, GuardPixelB);
            for (int i = 0; i < expectedComb.Length; i++) AssertEqual(expectedComb[i], comb[i + 1]);
        }

        // ------------------------------------------------------------------
        // thick brush degenerate and far-clipped lines
        // ------------------------------------------------------------------
        private static unsafe void TestMathPhysicsThickLineDegenerate()
        {
            AssertThrows<ArgumentNullException>(MathPhysicsThickLineNullCanvas);
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsThickLineWithDims(0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsThickLineWithDims(1, 0));
            AssertThrows<ArgumentOutOfRangeException>(MathPhysicsThickLineNegativeBrush);

            const int width = 11;
            const int height = 9;
            for (int brushSize = 1; brushSize <= 14; brushSize++)
            {
                foreach (Algorithms.BrushShape shape in Enum.GetValues(typeof(Algorithms.BrushShape)))
                {
                    // Degenerate point stroke: start == end stamps the brush exactly once.
                    uint[] guarded = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                    uint[] expected = new uint[width * height];
                    ReferenceDrawThickLine(expected, width, height, 5, 4, 5, 4, 0x12345678u, brushSize, shape);
                    fixed (uint* pixels = guarded)
                        Algorithms.DrawThickLineBresenham(pixels + 1, width, height, 5, 4, 5, 4, 0x12345678u, brushSize, shape);
                    AssertPixelGuards(guarded, GuardPixelA, GuardPixelB);
                    for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], guarded[i + 1]);
                }
            }

            // A brush far larger than the canvas clips cleanly; the square variant paints everything.
            foreach (Algorithms.BrushShape shape in Enum.GetValues(typeof(Algorithms.BrushShape)))
            {
                uint[] guarded = CreateGuardedPixels(width * height, GuardPixelB, GuardPixelA);
                uint[] expected = new uint[width * height];
                ReferenceDrawThickLine(expected, width, height, 5, 4, 5, 4, 0xABCDEF01u, 40, shape);
                fixed (uint* pixels = guarded)
                    Algorithms.DrawThickLineBresenham(pixels + 1, width, height, 5, 4, 5, 4, 0xABCDEF01u, 40, shape);
                AssertPixelGuards(guarded, GuardPixelB, GuardPixelA);
                for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], guarded[i + 1]);
                if (shape == Algorithms.BrushShape.Square)
                {
                    for (int i = 0; i < expected.Length; i++) AssertEqual(0xABCDEF01u, guarded[i + 1]);
                }
            }

            // A 6000-step line whose endpoints are far outside still clips exactly.
            uint[] clipped = CreateGuardedPixels(13 * 11, GuardPixelA, GuardPixelB);
            uint[] expectedClipped = new uint[13 * 11];
            ReferenceDrawLine(expectedClipped, 13, 11, -3000, -2999, 3000, 2999, 0xFFEE1122u);
            fixed (uint* pixels = clipped)
                Algorithms.DrawLineBresenham(pixels + 1, 13, 11, -3000, -2999, 3000, 2999, 0xFFEE1122u);
            AssertPixelGuards(clipped, GuardPixelA, GuardPixelB);
            bool clippedAnything = false;
            for (int i = 0; i < expectedClipped.Length; i++)
            {
                AssertEqual(expectedClipped[i], clipped[i + 1]);
                if (clipped[i + 1] != 0u) clippedAnything = true;
            }
            Assert(clippedAnything);

            // A line that never enters the canvas leaves it untouched.
            uint[] outside = CreateGuardedPixels(13 * 11, GuardPixelB, GuardPixelA);
            fixed (uint* pixels = outside)
                Algorithms.DrawLineBresenham(pixels + 1, 13, 11, -5, -1, 20, -1, 0xFFFFFFFFu);
            AssertPixelGuards(outside, GuardPixelB, GuardPixelA);
            for (int i = 0; i < 13 * 11; i++) AssertEqual(0u, outside[i + 1]);
        }

        // ------------------------------------------------------------------
        // walk frame taylor reference sweep
        // ------------------------------------------------------------------
        private static unsafe void TestMathPhysicsWalkFrameSweep()
        {
            AssertThrows<ArgumentNullException>(MathPhysicsWalkNullSource);
            AssertThrows<ArgumentNullException>(MathPhysicsWalkNullDestination);
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsWalkWithDims(0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsWalkWithDims(1, 0));

            const int width = 8;
            const int height = 6;
            uint[] source = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
            for (int i = 0; i < width * height; i++) source[i + 1] = 0xFF000000u | (uint)(i + 1);
            float[] times = { 0f, 0.124f, 0.25f, 0.4999f, 0.5f, 0.5001f, 0.75f, 0.9999f, 1f };
            (float Bounce, float Stride)[] amplitudePairs =
            {
                (2.5f, 3.5f), (-2.5f, 3.5f), (2.5f, -3.5f), (0f, 0f), (5.9f, 7.3f)
            };
            for (int timeIndex = 0; timeIndex < times.Length; timeIndex++)
            {
                for (int splitY = 0; splitY <= height; splitY++)
                {
                    for (int pairIndex = 0; pairIndex < amplitudePairs.Length; pairIndex++)
                    {
                        uint[] destination = CreateGuardedPixels(width * height, GuardPixelB, GuardPixelA);
                        uint[] expected = MathPhysicsReferenceWalkFrame(
                            source, 1, width, height, times[timeIndex], splitY,
                            amplitudePairs[pairIndex].Bounce, amplitudePairs[pairIndex].Stride);
                        fixed (uint* sourceBase = source)
                        fixed (uint* destinationBase = destination)
                        {
                            FastDeformation.GenerateWalkFrame(
                                sourceBase + 1, destinationBase + 1, width, height, times[timeIndex], splitY,
                                amplitudePairs[pairIndex].Bounce, amplitudePairs[pairIndex].Stride);
                        }
                        AssertPixelGuards(source, GuardPixelA, GuardPixelB);
                        AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                        for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], destination[i + 1]);
                    }
                }
            }

            // Width 1: every leg pixel is in the "right half" (x >= width / 2) and shears by +stride.
            uint[] columnSource = CreateGuardedPixels(5, GuardPixelA, GuardPixelB);
            for (int i = 0; i < 5; i++) columnSource[i + 1] = (uint)(0xAA00u + i);
            for (int splitY = 0; splitY <= 5; splitY++)
            {
                uint[] destination = CreateGuardedPixels(5, GuardPixelB, GuardPixelA);
                uint[] expected = MathPhysicsReferenceWalkFrame(columnSource, 1, 1, 5, 0.75f, splitY, 1.5f, 2.5f);
                fixed (uint* sourceBase = columnSource)
                fixed (uint* destinationBase = destination)
                {
                    FastDeformation.GenerateWalkFrame(sourceBase + 1, destinationBase + 1, 1, 5, 0.75f, splitY, 1.5f, 2.5f);
                }
                AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], destination[i + 1]);
            }
        }

        // ------------------------------------------------------------------
        // jitter frame full reference model
        // ------------------------------------------------------------------
        private static unsafe void TestMathPhysicsJitterReference()
        {
            AssertThrows<ArgumentNullException>(() => MathPhysicsJitterNullCase(0));
            AssertThrows<ArgumentNullException>(() => MathPhysicsJitterNullCase(1));
            AssertThrows<ArgumentNullException>(() => MathPhysicsJitterNullCase(2));
            AssertThrows<ArgumentNullException>(() => MathPhysicsJitterNullCase(3));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsJitterWithDims(0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsJitterWithDims(1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsJitterWithTime(float.NaN));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsJitterWithTime(-0.001f));
            AssertThrows<ArgumentOutOfRangeException>(() => MathPhysicsJitterWithTime(1.001f));

            Random random = new Random(0x0C7A17);
            for (int iteration = 0; iteration < 150; iteration++)
            {
                int width = random.Next(1, 19);
                int height = random.Next(1, 19);
                float timeT = (float)random.NextDouble();
                float[] amplitudes = new float[BackendConfig.Deformation.OctantCount];
                float[] frequencies = new float[BackendConfig.Deformation.OctantCount];
                for (int i = 0; i < amplitudes.Length; i++)
                {
                    amplitudes[i] = (float)(random.NextDouble() * 12d - 6d);
                    frequencies[i] = (float)(random.NextDouble() * 6d - 3d);
                }
                uint[] source = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                for (int i = 0; i < width * height; i++) source[i + 1] = NextUInt(random);
                uint[] destination = CreateGuardedPixels(width * height, GuardPixelB, GuardPixelA);
                uint[] expected = MathPhysicsReferenceJitterFrame(source, 1, width, height, timeT, amplitudes, frequencies);
                fixed (uint* sourceBase = source)
                fixed (uint* destinationBase = destination)
                fixed (float* amplitudeBase = amplitudes)
                fixed (float* frequencyBase = frequencies)
                {
                    FastDeformation.GenerateJitterFrame(
                        sourceBase + 1, destinationBase + 1, width, height, timeT, amplitudeBase, frequencyBase);
                }
                AssertPixelGuards(source, GuardPixelA, GuardPixelB);
                AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], destination[i + 1]);
            }

            // Semantic octant check: with only octant 0 active, exactly the pixels in the
            // { dx > dy >= 0 } cone move (displacement magnitude is >= 2 pixels there) and every
            // other pixel is copied verbatim.
            const int coneSize = 17;
            const int coneCenter = 8;
            uint[] coneSource = CreateGuardedPixels(coneSize * coneSize, GuardPixelA, GuardPixelB);
            for (int i = 0; i < coneSize * coneSize; i++) coneSource[i + 1] = (uint)(i + 1);
            float[] coneAmplitudes = new float[BackendConfig.Deformation.OctantCount];
            float[] coneFrequencies = new float[BackendConfig.Deformation.OctantCount];
            coneAmplitudes[0] = 3f;
            for (int i = 0; i < coneFrequencies.Length; i++) coneFrequencies[i] = 0.25f;
            uint[] coneDestination = CreateGuardedPixels(coneSize * coneSize, GuardPixelB, GuardPixelA);
            fixed (uint* sourceBase = coneSource)
            fixed (uint* destinationBase = coneDestination)
            fixed (float* amplitudeBase = coneAmplitudes)
            fixed (float* frequencyBase = coneFrequencies)
            {
                FastDeformation.GenerateJitterFrame(
                    sourceBase + 1, destinationBase + 1, coneSize, coneSize, 1f, amplitudeBase, frequencyBase);
            }
            AssertPixelGuards(coneDestination, GuardPixelB, GuardPixelA);
            for (int y = 0; y < coneSize; y++)
            {
                for (int x = 0; x < coneSize; x++)
                {
                    int dx = x - coneCenter;
                    int dy = y - coneCenter;
                    uint sourceValue = coneSource[1 + y * coneSize + x];
                    uint destinationValue = coneDestination[1 + y * coneSize + x];
                    if (dy >= 0 && dx > dy) Assert(destinationValue != sourceValue);
                    else AssertEqual(sourceValue, destinationValue);
                }
            }

            // FIXED guard: the POST-multiplication phase is validated, so a finite frequency
            // whose phase overflows to infinity after the TwoPI multiplication (e.g.
            // float.MaxValue) now throws up front - the destination stays untouched instead of
            // being silently wiped to transparent through the runtime-defined (int)NaN cast.
            float[] hugeFrequencies = new float[BackendConfig.Deformation.OctantCount];
            float[] hugeAmplitudes = new float[BackendConfig.Deformation.OctantCount];
            for (int i = 0; i < hugeFrequencies.Length; i++)
            {
                hugeFrequencies[i] = float.MaxValue;
                hugeAmplitudes[i] = 2f;
            }
            uint[] hugeDestination = CreateGuardedPixels(coneSize * coneSize, GuardPixelB, GuardPixelA);
            AssertThrows<ArgumentOutOfRangeException>(
                () => MathPhysicsJitterWithValues(coneSource, hugeDestination, coneSize, 1f, hugeAmplitudes, hugeFrequencies));
            AssertPixelGuards(hugeDestination, GuardPixelB, GuardPixelA);
            for (int i = 0; i < coneSize * coneSize; i++) AssertEqual(0u, hugeDestination[i + 1]);

            // A large-but-safe frequency (phase * TwoPI stays finite) still renders and matches
            // the reference model exactly.
            float[] safeFrequencies = new float[BackendConfig.Deformation.OctantCount];
            for (int i = 0; i < safeFrequencies.Length; i++) safeFrequencies[i] = 1e37f;
            uint[] safeDestination = CreateGuardedPixels(coneSize * coneSize, GuardPixelB, GuardPixelA);
            uint[] expectedSafe = MathPhysicsReferenceJitterFrame(
                coneSource, 1, coneSize, coneSize, 1f, hugeAmplitudes, safeFrequencies);
            MathPhysicsJitterWithValues(coneSource, safeDestination, coneSize, 1f, hugeAmplitudes, safeFrequencies);
            AssertPixelGuards(safeDestination, GuardPixelB, GuardPixelA);
            for (int i = 0; i < coneSize * coneSize; i++) AssertEqual(expectedSafe[i], safeDestination[i + 1]);

            // A float.MaxValue amplitude with a comfortably sub-unit sine sample keeps the
            // derived offsets finite and is accepted (only non-finite offsets are rejected).
            float[] maxAmplitudes = new float[BackendConfig.Deformation.OctantCount];
            float[] gentleFrequencies = new float[BackendConfig.Deformation.OctantCount];
            for (int i = 0; i < maxAmplitudes.Length; i++)
            {
                maxAmplitudes[i] = float.MaxValue;
                gentleFrequencies[i] = 0.125f;
            }
            uint[] maxAmpDestination = CreateGuardedPixels(coneSize * coneSize, GuardPixelB, GuardPixelA);
            uint[] expectedMaxAmp = MathPhysicsReferenceJitterFrame(
                coneSource, 1, coneSize, coneSize, 1f, maxAmplitudes, gentleFrequencies);
            MathPhysicsJitterWithValues(coneSource, maxAmpDestination, coneSize, 1f, maxAmplitudes, gentleFrequencies);
            AssertPixelGuards(maxAmpDestination, GuardPixelB, GuardPixelA);
            for (int i = 0; i < coneSize * coneSize; i++) AssertEqual(expectedMaxAmp[i], maxAmpDestination[i + 1]);
        }

        private static unsafe void MathPhysicsJitterWithValues(
            uint[] guardedSource, uint[] guardedDestination, int size, float timeT,
            float[] amplitudes, float[] frequencies)
        {
            fixed (uint* sourceBase = guardedSource)
            fixed (uint* destinationBase = guardedDestination)
            fixed (float* amplitudeBase = amplitudes)
            fixed (float* frequencyBase = frequencies)
            {
                FastDeformation.GenerateJitterFrame(
                    sourceBase + 1, destinationBase + 1, size, size, timeT, amplitudeBase, frequencyBase);
            }
        }

        // ------------------------------------------------------------------
        // cellular third-value and threshold extremes
        // ------------------------------------------------------------------
        private static void TestMathPhysicsCellularEdges()
        {
            // Tiles that are neither target nor baseline survive exactly at the equality threshold.
            short[] baseGrid =
            {
                7, -3, 42, 7, 0,
                -3, 7, 7, 42, 7,
                7, 42, -3, 7, 7,
                0, 7, 7, -3, 42
            };
            for (int threshold = -1; threshold <= 9; threshold++)
            {
                short[] grid = (short[])baseGrid.Clone();
                short[] buffer = new short[grid.Length];
                short[] expected = ReferenceCellular(baseGrid, 5, 4, 7, -3, threshold);
                FastProceduralGen.SmoothCellularAutomata(grid, buffer, 5, 4, 7, -3, threshold);
                AssertSequenceEqual(expected, grid);
                AssertSequenceEqual(expected, buffer);
            }

            // target == baseline still preserves foreign tiles at the equality threshold.
            short[] sameGrid = { 5, 42, 5, 42, 5, 42, 5, 42, 5 };
            short[] sameExpected = ReferenceCellular(sameGrid, 3, 3, 5, 5, 4);
            short[] sameActual = (short[])sameGrid.Clone();
            FastProceduralGen.SmoothCellularAutomata(sameActual, new short[9], 3, 3, 5, 5, 4);
            AssertSequenceEqual(sameExpected, sameActual);

            // 1x1 grid: all eight neighbors are border cells and count as target, so the outcome
            // is decided purely by the threshold (8 > t => target, 8 < t => base, 8 == t => keep).
            short[] single = { 13 };
            FastProceduralGen.SmoothCellularAutomata(single, new short[1], 1, 1, 99, -9, 4);
            AssertEqual((short)99, single[0]);
            single[0] = 13;
            FastProceduralGen.SmoothCellularAutomata(single, new short[1], 1, 1, 99, -9, 8);
            AssertEqual((short)13, single[0]);
            single[0] = 13;
            FastProceduralGen.SmoothCellularAutomata(single, new short[1], 1, 1, 99, -9, 9);
            AssertEqual((short)-9, single[0]);
            single[0] = 13;
            FastProceduralGen.SmoothCellularAutomata(single, new short[1], 1, 1, 99, -9, int.MinValue);
            AssertEqual((short)99, single[0]);
            single[0] = 13;
            FastProceduralGen.SmoothCellularAutomata(single, new short[1], 1, 1, 99, -9, int.MaxValue);
            AssertEqual((short)-9, single[0]);
        }

        // ------------------------------------------------------------------
        // translation special-value propagation
        // ------------------------------------------------------------------
        private static void TestMathPhysicsTranslationSpecials()
        {
            float positiveNaN = BitConverter.Int32BitsToSingle(0x7FC00000);
            float negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
            float[] specials =
            {
                0f, negativeZero, 1f, -1f, float.Epsilon, 1e38f,
                float.PositiveInfinity, float.NegativeInfinity, positiveNaN
            };
            for (int directionIndex = 0; directionIndex < specials.Length; directionIndex++)
            {
                for (int speedIndex = 0; speedIndex < specials.Length; speedIndex++)
                {
                    for (int deltaIndex = 0; deltaIndex < specials.Length; deltaIndex++)
                    {
                        float direction = specials[directionIndex];
                        float speed = specials[speedIndex];
                        float delta = specials[deltaIndex];
                        float expectedX = 1.5f + direction * (speed * delta);
                        float expectedY = -2.25f + direction * (speed * delta);
                        float positionX = 1.5f;
                        float positionY = -2.25f;
                        FastPhysics.CalculateTranslation(direction, direction, speed, delta, ref positionX, ref positionY);
                        // NaN payloads/signs are not stable across separately-JITed expressions,
                        // so NaN expectations only require any NaN.
                        MathPhysicsAssertBitExactOrBothNaN(expectedX, positionX);
                        MathPhysicsAssertBitExactOrBothNaN(expectedY, positionY);
                    }
                }
            }

            // Zero deltaTime with an infinite direction poisons that axis with NaN (inf * 0),
            // while a finite axis stays put. Documents current behavior.
            float nanX = 10f;
            float steadyY = 20f;
            FastPhysics.CalculateTranslation(float.PositiveInfinity, 0f, 100f, 0f, ref nanX, ref steadyY);
            Assert(float.IsNaN(nanX));
            AssertEqual(20f, steadyY);

            // Scalar overflow saturates the moving axis to infinity and poisons a zero-direction
            // axis with NaN (0 * inf). Documents current behavior.
            float infX = 3e38f;
            float poisonedY = 0f;
            FastPhysics.CalculateTranslation(1f, 0f, 3e38f, 2f, ref infX, ref poisonedY);
            Assert(float.IsPositiveInfinity(infX));
            Assert(float.IsNaN(poisonedY));
        }

        // ------------------------------------------------------------------
        // MathPhysics-private reference implementations and helpers
        // ------------------------------------------------------------------

        private static float MathPhysicsWrapRadians(float x)
        {
            x %= BackendConfig.Math.TwoPI;
            if (x > BackendConfig.Math.PI) return x - BackendConfig.Math.TwoPI;
            if (x < -BackendConfig.Math.PI) return x + BackendConfig.Math.TwoPI;
            return x;
        }

        private static float MathPhysicsSinApprox(float x)
        {
            return x * (BackendConfig.Math.TaylorSinA - BackendConfig.Math.TaylorSinB * System.Math.Abs(x));
        }

        private static float MathPhysicsReferenceInvSqrt(float number)
        {
            float halfNumber = number * BackendConfig.Math.InvSqrtInputHalf;
            int i = BitConverter.SingleToInt32Bits(number);
            i = BackendConfig.Math.QuakeMagicNumber - (i >> BackendConfig.Math.SingleBitShift);
            float y = BitConverter.Int32BitsToSingle(i);
            y *= BackendConfig.Math.InvSqrtThreeHalves - (halfNumber * y * y);
            return y;
        }

        private static void MathPhysicsReferenceNormalize(ref float x, ref float y)
        {
            float sqrMagnitude = (x * x) + (y * y);
            if (sqrMagnitude == BackendConfig.Math.NormalizedMin) return;
            float invSqrt = MathPhysicsReferenceInvSqrt(sqrMagnitude);
            x *= invSqrt;
            y *= invSqrt;
        }

        private static void MathPhysicsAssertNormalizeBitExact(float x, float y)
        {
            float expectedX = x;
            float expectedY = y;
            MathPhysicsReferenceNormalize(ref expectedX, ref expectedY);
            float actualX = x;
            float actualY = y;
            FastMath.FastNormalize(ref actualX, ref actualY);
            MathPhysicsAssertBitExactOrBothNaN(expectedX, actualX);
            MathPhysicsAssertBitExactOrBothNaN(expectedY, actualY);
        }

        private static void MathPhysicsAssertBitExactOrBothNaN(float expected, float actual)
        {
            if (float.IsNaN(expected)) Assert(float.IsNaN(actual));
            else AssertEqual(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));
        }

        // Runtime conversion of a non-constant float to int; out-of-range and NaN results are
        // runtime-defined (int.MinValue on x64 .NET 8).
        private static int MathPhysicsTruncatedCast(float value)
        {
            return unchecked((int)value);
        }

        private static int MathPhysicsRangeInt(ref uint state, int min, int max)
        {
            if (max <= min) return min;
            state = ReferenceXorShift(state);
            // Mirrors the FIXED product semantics: the span is widened to 64-bit so ranges
            // wider than int.MaxValue no longer overflow (draws for narrow spans are
            // unchanged, keeping every other seeded pin bit-identical).
            ulong span = (ulong)((long)max - min);
            return (int)(min + (long)(state % span));
        }

        private static float MathPhysicsRangeFloat(ref uint state, float min, float max)
        {
            state = ReferenceXorShift(state);
            float normalized = state * BackendConfig.Random.UIntToUnitFloat;
            return min + (max - min) * normalized;
        }

        private static void MathPhysicsInsideUnitCircle(ref uint state, out float x, out float y)
        {
            float sqrMagnitude;
            do
            {
                x = MathPhysicsRangeFloat(ref state, -BackendConfig.Math.NormalizedMax, BackendConfig.Math.NormalizedMax);
                y = MathPhysicsRangeFloat(ref state, -BackendConfig.Math.NormalizedMax, BackendConfig.Math.NormalizedMax);
                sqrMagnitude = (x * x) + (y * y);
            }
            while (sqrMagnitude > BackendConfig.Math.NormalizedMax ||
                   sqrMagnitude == BackendConfig.Math.NormalizedMin);
        }

        private static uint[] MathPhysicsReferenceWalkFrame(
            uint[] source, int sourceOffset, int width, int height,
            float timeT, int splitY, float bounceAmp, float strideAmp)
        {
            uint[] destination = new uint[width * height];
            float bounceT = timeT * BackendConfig.Deformation.BounceFrequencyMultiplier;
            if (bounceT >= BackendConfig.Deformation.NormalizedCycle) bounceT -= BackendConfig.Deformation.NormalizedCycle;
            float bounceRad = bounceT * BackendConfig.Math.TwoPI - BackendConfig.Math.PI;
            float strideRad = timeT * BackendConfig.Math.TwoPI - BackendConfig.Math.PI;
            float bounceOffset = MathPhysicsSinApprox(bounceRad) * bounceAmp;
            float strideOffsetBase = MathPhysicsSinApprox(strideRad) * strideAmp;
            int index = 0;
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
                for (int x = 0; x < width; x++, index++)
                {
                    int srcX = x;
                    int srcY = y;
                    if (isLegs)
                    {
                        int halfWidth = width >> 1;
                        srcX = x < halfWidth ? x - strideOffset : x + strideOffset;
                    }
                    else
                    {
                        srcY = srcYBody;
                    }
                    destination[index] = (uint)srcX < (uint)width && (uint)srcY < (uint)height
                        ? source[sourceOffset + srcY * width + srcX]
                        : BackendConfig.Deformation.TransparentPixel;
                }
            }
            return destination;
        }

        // Independent branchy decode of BackendConfig.Deformation.PackedOctantLookup:
        // octants run counterclockwise from +x (y pointing down), ties on |dx| == |dy| go to the
        // y-dominant slot, and zero components count as positive sign.
        private static int MathPhysicsReferenceOctant(int dx, int dy)
        {
            bool negativeX = dx < 0;
            bool negativeY = dy < 0;
            bool yDominant = System.Math.Abs((long)dy) >= System.Math.Abs((long)dx);
            if (!negativeX && !negativeY) return yDominant ? 1 : 0;
            if (negativeX && !negativeY) return yDominant ? 2 : 3;
            if (negativeX) return yDominant ? 5 : 4;
            return yDominant ? 6 : 7;
        }

        private static uint[] MathPhysicsReferenceJitterFrame(
            uint[] source, int sourceOffset, int width, int height,
            float timeT, float[] amplitudes, float[] frequencies)
        {
            uint[] destination = new uint[width * height];
            float[] frameOffsets = new float[BackendConfig.Deformation.OctantCount];
            for (int i = 0; i < frameOffsets.Length; i++)
            {
                float phase = timeT * frequencies[i];
                float rad = MathPhysicsWrapRadians(phase * BackendConfig.Math.TwoPI - BackendConfig.Math.PI);
                frameOffsets[i] = MathPhysicsSinApprox(rad) * amplitudes[i];
            }
            int cx = width >> 1;
            int cy = height >> 1;
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                int dy = y - cy;
                for (int x = 0; x < width; x++, index++)
                {
                    int dx = x - cx;
                    if (dx == 0 && dy == 0)
                    {
                        destination[index] = source[sourceOffset + y * width + x];
                        continue;
                    }
                    float offset = frameOffsets[MathPhysicsReferenceOctant(dx, dy)];
                    float fdx = dx;
                    float fdy = dy;
                    MathPhysicsReferenceNormalize(ref fdx, ref fdy);
                    int srcX = x + (int)(fdx * offset);
                    int srcY = y + (int)(fdy * offset);
                    destination[index] = (uint)srcX < (uint)width && (uint)srcY < (uint)height
                        ? source[sourceOffset + srcY * width + srcX]
                        : BackendConfig.Deformation.TransparentPixel;
                }
            }
            return destination;
        }

        private static unsafe void MathPhysicsFloodFillNullCanvas()
        {
            Algorithms.FloodFill(null, 1, 1, 0, 0, 1u);
        }

        private static unsafe void MathPhysicsFloodFillWithDims(int width, int height)
        {
            uint pixel = 0;
            Algorithms.FloodFill(&pixel, width, height, 0, 0, 1u);
        }

        private static unsafe void MathPhysicsThickLineNullCanvas()
        {
            Algorithms.DrawThickLineBresenham(null, 1, 1, 0, 0, 0, 0, 1u, 1, Algorithms.BrushShape.Square);
        }

        private static unsafe void MathPhysicsThickLineWithDims(int width, int height)
        {
            uint pixel = 0;
            Algorithms.DrawThickLineBresenham(&pixel, width, height, 0, 0, 0, 0, 1u, 1, Algorithms.BrushShape.Square);
        }

        private static unsafe void MathPhysicsThickLineNegativeBrush()
        {
            uint pixel = 0;
            Algorithms.DrawThickLineBresenham(&pixel, 1, 1, 0, 0, 0, 0, 1u, -3, Algorithms.BrushShape.Circle);
        }

        private static unsafe void MathPhysicsWalkNullSource()
        {
            uint destination = 0;
            FastDeformation.GenerateWalkFrame(null, &destination, 1, 1, 0f, 0, 0f, 0f);
        }

        private static unsafe void MathPhysicsWalkNullDestination()
        {
            uint source = 0;
            FastDeformation.GenerateWalkFrame(&source, null, 1, 1, 0f, 0, 0f, 0f);
        }

        private static unsafe void MathPhysicsWalkWithDims(int width, int height)
        {
            uint source = 0;
            uint destination = 0;
            FastDeformation.GenerateWalkFrame(&source, &destination, width, height, 0f, 0, 0f, 0f);
        }

        private static unsafe void MathPhysicsJitterNullCase(int missingPointer)
        {
            uint source = 0;
            uint destination = 0;
            float* values = stackalloc float[BackendConfig.Deformation.OctantCount];
            FastDeformation.GenerateJitterFrame(
                missingPointer == 0 ? null : &source,
                missingPointer == 1 ? null : &destination,
                1, 1, 0.5f,
                missingPointer == 2 ? null : values,
                missingPointer == 3 ? null : values);
        }

        private static unsafe void MathPhysicsJitterWithDims(int width, int height)
        {
            uint source = 0;
            uint destination = 0;
            float* values = stackalloc float[BackendConfig.Deformation.OctantCount];
            FastDeformation.GenerateJitterFrame(&source, &destination, width, height, 0.5f, values, values);
        }

        private static unsafe void MathPhysicsJitterWithTime(float timeT)
        {
            uint source = 0;
            uint destination = 0;
            float* values = stackalloc float[BackendConfig.Deformation.OctantCount];
            FastDeformation.GenerateJitterFrame(&source, &destination, 1, 1, timeT, values, values);
        }

        // ------------------------------------------------------------------
        // FastRandomState instance streams and unbiased range (batch1/backend-core)
        // ------------------------------------------------------------------
        private static void TestMathPhysicsInstanceRandomState()
        {
            // The instance PRNG replays the exact same stream as the static facade for
            // the same seed (the facade now delegates to a shared FastRandomState).
            FastRandom.SetSeed(0xB1B1CAFEu);
            FastRandomState instance = new FastRandomState(0xB1B1CAFEu);
            for (int draw = 0; draw < 256; draw++) AssertEqual(FastRandom.Next(), instance.NextUInt());

            // The reserved invalid seed maps to the same fallback on both paths.
            FastRandom.SetSeed(BackendConfig.Random.InvalidSeed);
            FastRandomState fallback = new FastRandomState(BackendConfig.Random.InvalidSeed);
            for (int draw = 0; draw < 16; draw++) AssertEqual(FastRandom.Next(), fallback.NextUInt());

            // Two instances are fully isolated: interleaved draws match independent
            // reference replays of each seed.
            FastRandomState first = new FastRandomState(1234u);
            FastRandomState second = new FastRandomState(987654321u);
            uint firstReference = 1234u;
            uint secondReference = 987654321u;
            for (int draw = 0; draw < 64; draw++)
            {
                firstReference = ReferenceXorShift(firstReference);
                AssertEqual(firstReference, first.NextUInt());
                secondReference = ReferenceXorShift(secondReference);
                AssertEqual(secondReference, second.NextUInt());
            }

            // Struct copies fork the stream: advancing the copy leaves the original put.
            FastRandomState original = new FastRandomState(777u);
            original.NextUInt();
            FastRandomState forked = original;
            uint forkedDraw = forked.NextUInt();
            AssertEqual(forkedDraw, original.NextUInt());

            // NextUnitFloat, Range, and InsideUnitCircle mirror the static overloads
            // draw-for-draw and bit-for-bit.
            FastRandom.SetSeed(0x5EEDBEEFu);
            FastRandomState mirrored = new FastRandomState(0x5EEDBEEFu);
            for (int draw = 0; draw < 64; draw++)
            {
                AssertEqual(FastRandom.Range(-17, 4242), mirrored.Range(-17, 4242));
                AssertEqual(
                    BitConverter.SingleToInt32Bits(FastRandom.Range(-2.5f, 9.75f)),
                    BitConverter.SingleToInt32Bits(mirrored.Range(-2.5f, 9.75f)));
                FastRandom.InsideUnitCircle(out float staticX, out float staticY);
                mirrored.InsideUnitCircle(out float instanceX, out float instanceY);
                AssertEqual(BitConverter.SingleToInt32Bits(staticX), BitConverter.SingleToInt32Bits(instanceX));
                AssertEqual(BitConverter.SingleToInt32Bits(staticY), BitConverter.SingleToInt32Bits(instanceY));
            }

            // Empty and inverted int ranges return min WITHOUT consuming a draw on both
            // the modulo and the unbiased reductions.
            FastRandomState quiet = new FastRandomState(31337u);
            AssertEqual(5, quiet.Range(5, 5));
            AssertEqual(9, quiet.Range(9, -9));
            AssertEqual(12, quiet.RangeUnbiased(12, 3));
            FastRandomState untouched = new FastRandomState(31337u);
            AssertEqual(untouched.NextUInt(), quiet.NextUInt());

            // The 64-bit span fix applies to the instance API too: the full int range
            // covers positives instead of collapsing onto min.
            FastRandomState wide = new FastRandomState(0xCAFEF00Du);
            bool sawInstancePositive = false;
            for (int draw = 0; draw < 100; draw++)
            {
                int value = wide.Range(int.MinValue, int.MaxValue);
                Assert(value >= int.MinValue && value < int.MaxValue);
                if (value > 0) sawInstancePositive = true;
            }
            Assert(sawInstancePositive);

            // RangeUnbiased: exact replay against a reference Lemire multiply-shift
            // (with debiasing rejection) over the same raw xorshift stream, for spans
            // with and without rejection thresholds, tiny and full-width.
            (int Min, int Max)[] unbiasedRanges =
            {
                (0, 3),
                (-7, 6),
                (0, 256),
                (int.MinValue, int.MaxValue),
                (-1000000000, 1000000001)
            };
            for (int rangeIndex = 0; rangeIndex < unbiasedRanges.Length; rangeIndex++)
            {
                uint referenceState = 0x00C0FFEEu + (uint)rangeIndex;
                FastRandomState unbiased = new FastRandomState(referenceState);
                for (int draw = 0; draw < 500; draw++)
                {
                    int expected = MathPhysicsReferenceLemire(
                        ref referenceState, unbiasedRanges[rangeIndex].Min, unbiasedRanges[rangeIndex].Max);
                    int actual = unbiased.RangeUnbiased(unbiasedRanges[rangeIndex].Min, unbiasedRanges[rangeIndex].Max);
                    AssertEqual(expected, actual);
                    Assert(actual >= unbiasedRanges[rangeIndex].Min && actual < unbiasedRanges[rangeIndex].Max);
                }
            }

            // Distribution smoke test: a span-3 histogram is roughly balanced (the
            // whole point of the debiasing rejection step).
            FastRandomState histogram = new FastRandomState(0xD1CE5EEDu);
            int[] buckets = new int[3];
            for (int draw = 0; draw < 30000; draw++) buckets[histogram.RangeUnbiased(0, 3)]++;
            for (int bucket = 0; bucket < buckets.Length; bucket++)
                Assert(buckets[bucket] > 9000 && buckets[bucket] < 11000);

            // The static facade exposes the same unbiased reduction over the shared stream.
            FastRandom.SetSeed(0x00C0FFEEu);
            uint facadeReference = 0x00C0FFEEu;
            for (int draw = 0; draw < 100; draw++)
                AssertEqual(MathPhysicsReferenceLemire(ref facadeReference, 0, 3), FastRandom.RangeUnbiased(0, 3));
        }

        private static int MathPhysicsReferenceLemire(ref uint state, int min, int max)
        {
            if (max <= min) return min;
            uint span = (uint)((long)max - min);
            state = ReferenceXorShift(state);
            ulong product = (ulong)state * span;
            if ((uint)product < span)
            {
                uint threshold = (uint)((0x1_0000_0000ul - span) % span);
                while ((uint)product < threshold)
                {
                    state = ReferenceXorShift(state);
                    product = (ulong)state * span;
                }
            }
            return (int)(min + (long)(product >> 32));
        }

        // ------------------------------------------------------------------
        // Procedural maze / rooms generators and border rule (batch1/backend-core)
        // ------------------------------------------------------------------
        private static void TestMathPhysicsProceduralGenerators()
        {
            // Border rule: the compatibility overload is bit-identical to the explicit
            // TreatAsTarget spelling, and TreatAsEmpty matches a naive 8-neighbour
            // reference that counts off-map cells as nothing.
            Random borderRandom = new Random(0xB07DE7);
            for (int iteration = 0; iteration < 8; iteration++)
            {
                int width = 2 + borderRandom.Next(9);
                int height = 2 + borderRandom.Next(7);
                short[] initial = new short[width * height];
                for (int cell = 0; cell < initial.Length; cell++) initial[cell] = (short)(borderRandom.Next(2) == 0 ? 4 : 0);

                short[] compat = (short[])initial.Clone();
                short[] explicitTarget = (short[])initial.Clone();
                short[] scratch = new short[initial.Length];
                FastProceduralGen.SmoothCellularAutomata(compat, scratch, width, height, 4, 0);
                FastProceduralGen.SmoothCellularAutomata(
                    explicitTarget, new short[initial.Length], width, height, 4, 0,
                    BackendConfig.Procedural.DefaultSmoothThreshold, CellularBorderRule.TreatAsTarget);
                for (int cell = 0; cell < compat.Length; cell++) AssertEqual(compat[cell], explicitTarget[cell]);

                short[] treatEmpty = (short[])initial.Clone();
                FastProceduralGen.SmoothCellularAutomata(
                    treatEmpty, new short[initial.Length], width, height, 4, 0,
                    BackendConfig.Procedural.DefaultSmoothThreshold, CellularBorderRule.TreatAsEmpty);
                short[] expectedEmpty = MathPhysicsReferenceAutomataPass(
                    initial, width, height, 4, 0, BackendConfig.Procedural.DefaultSmoothThreshold, false);
                for (int cell = 0; cell < treatEmpty.Length; cell++) AssertEqual(expectedEmpty[cell], treatEmpty[cell]);
            }
            short[] ruleGuardGrid = new short[4];
            AssertThrows<ArgumentOutOfRangeException>(() => FastProceduralGen.SmoothCellularAutomata(
                ruleGuardGrid, new short[4], 2, 2, 1, 0,
                BackendConfig.Procedural.DefaultSmoothThreshold, (CellularBorderRule)99));

            // Maze: deterministic from the seed, carved on odd coordinates, walls on the
            // border and on even-even lattice points, and a PERFECT maze - the carved
            // floor forms a spanning tree over the cell lattice (connected, loop-free).
            const int mazeWidth = 21;
            const int mazeHeight = 15;
            const short mazeWall = 9;
            const short mazeFloor = 2;
            short[] maze = new short[mazeWidth * mazeHeight];
            short[] mazeBuffer = new short[mazeWidth * mazeHeight];
            FastProceduralGen.GenerateMazeRecursiveBacktracker(maze, mazeBuffer, mazeWidth, mazeHeight, 0xABCD1234u, mazeWall, mazeFloor);
            short[] mazeReplay = new short[mazeWidth * mazeHeight];
            FastProceduralGen.GenerateMazeRecursiveBacktracker(mazeReplay, new short[maze.Length], mazeWidth, mazeHeight, 0xABCD1234u, mazeWall, mazeFloor);
            for (int cell = 0; cell < maze.Length; cell++) AssertEqual(maze[cell], mazeReplay[cell]);
            short[] mazeOther = new short[mazeWidth * mazeHeight];
            FastProceduralGen.GenerateMazeRecursiveBacktracker(mazeOther, new short[maze.Length], mazeWidth, mazeHeight, 0xABCD1235u, mazeWall, mazeFloor);
            bool anyDifferent = false;
            for (int cell = 0; cell < maze.Length; cell++)
            {
                if (maze[cell] != mazeOther[cell]) anyDifferent = true;
            }
            Assert(anyDifferent);
            int mazeCellsX = (mazeWidth - 1) / 2;
            int mazeCellsY = (mazeHeight - 1) / 2;
            int mazeFloorCount = 0;
            for (int y = 0; y < mazeHeight; y++)
            {
                for (int x = 0; x < mazeWidth; x++)
                {
                    short tile = maze[y * mazeWidth + x];
                    Assert(tile == mazeWall || tile == mazeFloor);
                    if (tile == mazeFloor) mazeFloorCount++;
                    bool isBorder = x == 0 || y == 0 || x == mazeWidth - 1 || y == mazeHeight - 1;
                    if (isBorder) AssertEqual(mazeWall, tile);
                    if (x % 2 == 0 && y % 2 == 0) AssertEqual(mazeWall, tile);
                    bool isCell = x % 2 == 1 && y % 2 == 1 && x < mazeCellsX * 2 && y < mazeCellsY * 2;
                    if (isCell) AssertEqual(mazeFloor, tile);
                }
            }
            // Spanning tree over n cells: n cell tiles + (n - 1) carved wall tiles.
            int mazeCellCount = mazeCellsX * mazeCellsY;
            AssertEqual(mazeCellCount * 2 - 1, mazeFloorCount);
            AssertEqual(mazeFloorCount, MathPhysicsCountReachableTiles(maze, mazeWidth, mazeHeight, 1, 1, mazeFloor));

            // Too-small maps hold no carvable cell and stay entirely wall.
            short[] tinyMaze = new short[2 * 2];
            FastProceduralGen.GenerateMazeRecursiveBacktracker(tinyMaze, new short[4], 2, 2, 7u, mazeWall, mazeFloor);
            for (int cell = 0; cell < tinyMaze.Length; cell++) AssertEqual(mazeWall, tinyMaze[cell]);

            // Shared buffer validation for both generators.
            AssertThrows<ArgumentNullException>(
                () => FastProceduralGen.GenerateMazeRecursiveBacktracker(null, mazeBuffer, 3, 3, 1u, 1, 0));
            AssertThrows<ArgumentNullException>(
                () => FastProceduralGen.GenerateMazeRecursiveBacktracker(maze, null, mazeWidth, mazeHeight, 1u, 1, 0));
            AssertThrows<ArgumentException>(
                () => FastProceduralGen.GenerateMazeRecursiveBacktracker(maze, maze, mazeWidth, mazeHeight, 1u, 1, 0));
            AssertThrows<ArgumentException>(
                () => FastProceduralGen.GenerateMazeRecursiveBacktracker(maze, new short[1], mazeWidth, mazeHeight, 1u, 1, 0));
            AssertThrows<ArgumentOutOfRangeException>(
                () => FastProceduralGen.GenerateMazeRecursiveBacktracker(maze, mazeBuffer, 0, mazeHeight, 1u, 1, 0));
            AssertThrows<ArgumentOutOfRangeException>(
                () => FastProceduralGen.GenerateMazeRecursiveBacktracker(maze, mazeBuffer, mazeWidth, -1, 1u, 1, 0));

            // Rooms and corridors: deterministic, bounded away from the border, fully
            // connected, with the returned count matching what the reference recount
            // of the rooms-only buffer implies (every accepted room is floor there).
            const int roomsWidth = 48;
            const int roomsHeight = 36;
            const short roomsWall = 5;
            const short roomsFloor = 7;
            short[] rooms = new short[roomsWidth * roomsHeight];
            short[] roomsBuffer = new short[roomsWidth * roomsHeight];
            int placed = FastProceduralGen.GenerateRoomsAndCorridors(
                rooms, roomsBuffer, roomsWidth, roomsHeight, 0xFEED5EEDu, roomsWall, roomsFloor, 40);
            Assert(placed >= 2);
            short[] roomsReplay = new short[rooms.Length];
            int placedReplay = FastProceduralGen.GenerateRoomsAndCorridors(
                roomsReplay, new short[rooms.Length], roomsWidth, roomsHeight, 0xFEED5EEDu, roomsWall, roomsFloor, 40);
            AssertEqual(placed, placedReplay);
            for (int cell = 0; cell < rooms.Length; cell++) AssertEqual(rooms[cell], roomsReplay[cell]);
            int roomsFloorCount = 0;
            int firstFloorX = -1;
            int firstFloorY = -1;
            for (int y = 0; y < roomsHeight; y++)
            {
                for (int x = 0; x < roomsWidth; x++)
                {
                    short tile = rooms[y * roomsWidth + x];
                    Assert(tile == roomsWall || tile == roomsFloor);
                    if (tile != roomsFloor) continue;
                    roomsFloorCount++;
                    if (firstFloorX < 0)
                    {
                        firstFloorX = x;
                        firstFloorY = y;
                    }
                    Assert(x > 0 && y > 0 && x < roomsWidth - 1 && y < roomsHeight - 1);
                    // The rooms-only bookkeeping buffer never marks floor where the
                    // final map has wall.
                    Assert(roomsBuffer[y * roomsWidth + x] == roomsFloor || roomsBuffer[y * roomsWidth + x] == roomsWall);
                }
            }
            Assert(roomsFloorCount > 0);
            AssertEqual(
                roomsFloorCount,
                MathPhysicsCountReachableTiles(rooms, roomsWidth, roomsHeight, firstFloorX, firstFloorY, roomsFloor));
            for (int cell = 0; cell < rooms.Length; cell++)
            {
                if (roomsBuffer[cell] == roomsFloor) AssertEqual(roomsFloor, rooms[cell]);
            }

            // Zero attempts leaves a solid wall map; negative attempts throw.
            short[] noRooms = new short[6 * 6];
            AssertEqual(0, FastProceduralGen.GenerateRoomsAndCorridors(noRooms, new short[36], 6, 6, 1u, 3, 4, 0));
            for (int cell = 0; cell < noRooms.Length; cell++) AssertEqual((short)3, noRooms[cell]);
            AssertThrows<ArgumentOutOfRangeException>(
                () => FastProceduralGen.GenerateRoomsAndCorridors(noRooms, new short[36], 6, 6, 1u, 3, 4, -1));

            // A map too small for the minimum room span accepts nothing but still
            // consumes the deterministic per-attempt draws without faulting.
            short[] crampedRooms = new short[4 * 4];
            AssertEqual(0, FastProceduralGen.GenerateRoomsAndCorridors(crampedRooms, new short[16], 4, 4, 42u, 3, 4, 25));
            for (int cell = 0; cell < crampedRooms.Length; cell++) AssertEqual((short)3, crampedRooms[cell]);
        }

        private static short[] MathPhysicsReferenceAutomataPass(
            short[] source, int width, int height, short targetTile, short baseTile, int threshold, bool borderCountsAsTarget)
        {
            short[] destination = new short[source.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int neighborCount = 0;
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            if (offsetX == 0 && offsetY == 0) continue;
                            int nx = x + offsetX;
                            int ny = y + offsetY;
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                if (source[ny * width + nx] == targetTile) neighborCount++;
                            }
                            else if (borderCountsAsTarget)
                            {
                                neighborCount++;
                            }
                        }
                    }
                    if (neighborCount > threshold) destination[y * width + x] = targetTile;
                    else if (neighborCount < threshold) destination[y * width + x] = baseTile;
                    else destination[y * width + x] = source[y * width + x];
                }
            }
            return destination;
        }

        private static int MathPhysicsCountReachableTiles(
            short[] tiles, int width, int height, int startX, int startY, short walkableId)
        {
            if (tiles[startY * width + startX] != walkableId) return 0;
            bool[] visited = new bool[tiles.Length];
            int[] deltaX = { -1, 1, 0, 0 };
            int[] deltaY = { 0, 0, -1, 1 };
            System.Collections.Generic.Queue<(int X, int Y)> frontier = new System.Collections.Generic.Queue<(int X, int Y)>();
            frontier.Enqueue((startX, startY));
            visited[startY * width + startX] = true;
            int reached = 1;
            while (frontier.Count > 0)
            {
                (int x, int y) = frontier.Dequeue();
                for (int direction = 0; direction < deltaX.Length; direction++)
                {
                    int nx = x + deltaX[direction];
                    int ny = y + deltaY[direction];
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                    int index = ny * width + nx;
                    if (visited[index] || tiles[index] != walkableId) continue;
                    visited[index] = true;
                    reached++;
                    frontier.Enqueue((nx, ny));
                }
            }
            return reached;
        }
    }
}
