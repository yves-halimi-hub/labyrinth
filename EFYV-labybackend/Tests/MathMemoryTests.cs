using System;
using System.Collections.Generic;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Memory;
using EFYVBackend.Core.Physics;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        private const uint GuardPixelA = 0xA1B2C3D4u;
        private const uint GuardPixelB = 0x5E6F7081u;

        private static unsafe void TestMathReferenceAndEdges()
        {
            int[] ints = { int.MinValue, int.MinValue + 1, -1000000, -1, 0, 1, 1000000, int.MaxValue };
            for (int i = 0; i < ints.Length; i++)
            {
                for (int j = 0; j < ints.Length; j++)
                {
                    AssertEqual(System.Math.Min(ints[i], ints[j]), FastMath.FastMin(ints[i], ints[j]));
                    AssertEqual(System.Math.Max(ints[i], ints[j]), FastMath.FastMax(ints[i], ints[j]));
                }
            }
            for (int value = -10000; value <= 10000; value++)
                AssertEqual(System.Math.Abs(value), FastMath.Abs(value));
            AssertEqual(int.MinValue, FastMath.Abs(int.MinValue));

            float[] floats =
            {
                float.NegativeInfinity, -float.MaxValue, -100.5f, -0f, 0f, 100.5f,
                float.MaxValue, float.PositiveInfinity
            };
            for (int i = 0; i < floats.Length; i++)
            {
                float absolute = FastMath.FastAbs(floats[i]);
                Assert(!IsNegativeBitSet(absolute));
                AssertEqual(System.Math.Abs(floats[i]), absolute);
            }
            float negativeNaN = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00042));
            float absoluteNaN = FastMath.FastAbs(negativeNaN);
            Assert(float.IsNaN(absoluteNaN));
            Assert(!IsNegativeBitSet(absoluteNaN));

            AssertEqual(FastMath.FacingDirection.Right, FastMath.Get4WayDirection(0f, 0f));
            for (int x = -20; x <= 20; x++)
            {
                for (int y = -20; y <= 20; y++)
                    AssertEqual(ReferenceFacing(x, y), FastMath.Get4WayDirection(x, y));
            }

            Random random = new Random(0x4D415448);
            for (int i = 0; i < 20000; i++)
            {
                float x1 = (float)(random.NextDouble() * 20000d - 10000d);
                float y1 = (float)(random.NextDouble() * 20000d - 10000d);
                float x2 = (float)(random.NextDouble() * 20000d - 10000d);
                float y2 = (float)(random.NextDouble() * 20000d - 10000d);
                float expectedDistance = (x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2);
                AssertNear(expectedDistance, FastMath.DistanceSqr(x1, y1, x2, y2), 0f);

                float low = (float)(random.NextDouble() * 100d - 50d);
                float high = low + (float)random.NextDouble() * 100f;
                float value = (float)(random.NextDouble() * 300d - 150d);
                AssertNear(System.Math.Clamp(value, low, high), FastMath.FastClamp(value, low, high), 0f);
                float t = (float)(random.NextDouble() * 3d - 1d);
                float clampedT = System.Math.Clamp(t, 0f, 1f);
                AssertNear(x1 + (x2 - x1) * clampedT, FastMath.FastLerp(x1, x2, t), 0.001f);
            }

            for (int value = -10000; value <= 10000; value++)
            {
                AssertEqual(System.Math.Clamp(value, -7000, 8000), FastMath.FastClamp(value, -7000, 8000));
                for (int divisor = 1; divisor <= 97; divisor++)
                    AssertEqual(PositiveModulo(value, divisor), FastMath.FastWrap(value, divisor));
            }
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.FastWrap(1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.FastWrap(1, -1));
            AssertEqual(0, FastMath.FastWrap(int.MinValue, 1));
            Assert(FastMath.FastWrap(int.MinValue, int.MaxValue) >= 0);

            for (int i = -10000; i <= 10000; i++)
            {
                float value = i / 37f;
                AssertEqual((int)System.Math.Ceiling(value), FastMath.FastCeilToInt(value));
            }
            for (int power = 0; power <= 20; power++)
            {
                int factor = 1 << power;
                for (int value = -1000; value <= 1000; value++)
                {
                    AssertEqual(value << power, FastMath.FastMulPow2(value, power));
                    AssertEqual(value >> power, FastMath.FastDivPow2(value, power));
                    if (value >= 0) AssertEqual(value / factor, FastMath.FastDivPow2(value, power));
                }
            }

            string[] hashes = { null, string.Empty, "a", "A", "EFYV", "\0", "\u05e9\u05dc\u05d5\u05dd", "\ud83d\ude80", new string('x', 4096) };
            for (int i = 0; i < hashes.Length; i++)
                AssertEqual(ReferenceFnv1A(hashes[i]), FastMath.FastHash(hashes[i]));

            float maxSinError = 0f;
            float maxCosError = 0f;
            for (int i = -20000; i <= 20000; i++)
            {
                float angle = i * BackendConfig.Math.TwoPI / 137f;
                float expectedSin = (float)System.Math.Sin(angle);
                float expectedCos = (float)System.Math.Cos(angle);
                float actualSin = FastMath.FastSinTaylor(angle);
                float actualCos = FastMath.FastCosTaylor(angle);
                maxSinError = System.Math.Max(maxSinError, System.Math.Abs(expectedSin - actualSin));
                maxCosError = System.Math.Max(maxCosError, System.Math.Abs(expectedCos - actualCos));
                Assert(System.Math.Abs(expectedSin - actualSin) <= 0.057f);
                Assert(System.Math.Abs(expectedCos - actualCos) <= 0.057f);
                FastMath.FastSinCosTaylor(angle, out float pairedSin, out float pairedCos);
                AssertNear(actualSin, pairedSin, 0f);
                AssertNear(actualCos, pairedCos, 0f);
                float wrapped = FastMath.WrapRadians(angle);
                Assert(wrapped >= -BackendConfig.Math.PI && wrapped <= BackendConfig.Math.PI);
            }
            Assert(maxSinError > 0.04f);
            Assert(maxCosError > 0.04f);
            Assert(float.IsNaN(FastMath.WrapRadians(float.NaN)));
            Assert(float.IsNaN(FastMath.WrapRadians(float.PositiveInfinity)));

            for (int total = 1; total <= 128; total++)
            {
                for (int index = -total; index <= total * 2; index++)
                {
                    float expectedAngle = index / (float)total * BackendConfig.Math.TwoPI;
                    AssertNear(expectedAngle, FastMath.GetCircleDistributionAngleRad(index, total), 0f);
                }
            }
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.GetCircleDistributionAngleRad(0, -1));

            for (int i = 0; i < 30000; i++)
            {
                float x = (float)(random.NextDouble() * 2000d - 1000d);
                float y = (float)(random.NextDouble() * 2000d - 1000d);
                if (x == 0f && y == 0f) x = 1f;
                float originalCross = x * y - y * x;
                FastMath.FastNormalize(ref x, ref y);
                float magnitude = (float)System.Math.Sqrt(x * x + y * y);
                Assert(System.Math.Abs(1f - magnitude) <= 0.0018f);
                AssertEqual(0f, originalCross);
            }
            float zeroX = 0f;
            float zeroY = 0f;
            FastMath.FastNormalize(ref zeroX, ref zeroY);
            AssertEqual(0f, zeroX);
            AssertEqual(0f, zeroY);
        }

        private static void TestRandomContracts()
        {
            uint[] seeds = { 0u, 1u, 2u, 0x12345678u, uint.MaxValue };
            for (int seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
            {
                uint expectedState = seeds[seedIndex] == 0u ? 1u : seeds[seedIndex];
                FastRandom.SetSeed(seeds[seedIndex]);
                for (int i = 0; i < 1000; i++)
                {
                    expectedState = ReferenceXorShift(expectedState);
                    AssertEqual(expectedState, FastRandom.Next());
                }
            }

            FastRandom.SetSeed(0xC0FFEEu);
            uint[] sequence = new uint[512];
            for (int i = 0; i < sequence.Length; i++) sequence[i] = FastRandom.Next();
            FastRandom.SetSeed(0xC0FFEEu);
            for (int i = 0; i < sequence.Length; i++) AssertEqual(sequence[i], FastRandom.Next());

            int[] bins = new int[17];
            FastRandom.SetSeed(0x13579BDFu);
            for (int i = 0; i < 170000; i++)
            {
                int value = FastRandom.Range(-8, 9);
                Assert(value >= -8 && value < 9);
                bins[value + 8]++;
            }
            for (int i = 0; i < bins.Length; i++) Assert(bins[i] > 9300 && bins[i] < 10700);

            FastRandom.SetSeed(98765u);
            uint expectedNext = FastRandom.Next();
            FastRandom.SetSeed(98765u);
            AssertEqual(10, FastRandom.Range(10, 10));
            AssertEqual(10, FastRandom.Range(10, -100));
            AssertEqual(expectedNext, FastRandom.Next());

            FastRandom.SetSeed(0x2468ACEu);
            for (int i = 0; i < 100000; i++)
            {
                float value = FastRandom.Range(-17.25f, 93.5f);
                Assert(value >= -17.25f && value <= 93.5f);
            }

            FastRandom.SetSeed(0x10203040u);
            double sumX = 0d;
            double sumY = 0d;
            double sumRadiusSquared = 0d;
            const int diskSamples = 100000;
            for (int i = 0; i < diskSamples; i++)
            {
                FastRandom.InsideUnitCircle(out float x, out float y);
                float radiusSquared = x * x + y * y;
                Assert(radiusSquared > 0f && radiusSquared <= 1f);
                sumX += x;
                sumY += y;
                sumRadiusSquared += radiusSquared;
            }
            Assert(System.Math.Abs(sumX / diskSamples) < 0.01d);
            Assert(System.Math.Abs(sumY / diskSamples) < 0.01d);
            Assert(System.Math.Abs(sumRadiusSquared / diskSamples - 0.5d) < 0.01d);

            FastRandom.SetSeed(0xABCDu);
            for (int i = 0; i < 10000; i++)
            {
                FastMath.GetRandomOffset2D(500f, out float x, out float y);
                Assert(x * x + y * y <= 250000.1f);
            }
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.GetRandomOffset2D(-float.Epsilon, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.GetRandomOffset2D(float.NaN, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.GetRandomOffset2D(float.PositiveInfinity, out _, out _));
        }

        private static unsafe void TestUnsafeMemoryGuards()
        {
            int[] source = { int.MinValue, -1, 0, 1, int.MaxValue };
            int[] destination = { 91, 92, 93, 94, 95, 96, 97 };
            FastMemory.Copy(source, destination);
            for (int i = 0; i < source.Length; i++) AssertEqual(source[i], destination[i]);
            AssertEqual(96, destination[5]);
            AssertEqual(97, destination[6]);
            AssertThrows<ArgumentNullException>(() => FastMemory.Copy<int>(null, destination));
            AssertThrows<ArgumentNullException>(() => FastMemory.Copy(source, null));
            AssertThrows<ArgumentException>(() => FastMemory.Copy(destination, source));

            ulong[] clear = { ulong.MaxValue, 1ul, 0xAABBCCDDEEFF0011ul };
            FastMemory.Clear(clear);
            for (int i = 0; i < clear.Length; i++) AssertEqual(0ul, clear[i]);
            AssertThrows<ArgumentNullException>(() => FastMemory.Clear<int>(null));
            FastMemory.Clear(Array.Empty<int>());
            FastMemory.Copy(Array.Empty<int>(), Array.Empty<int>());

            int[] matrix = new int[35];
            ref int first = ref matrix[0];
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 7; x++)
                {
                    FastMemory.Write2DArrayUnsafe(ref first, 7, x, y, y * 100 + x);
                    AssertEqual(y * 100 + x, FastMemory.Read2DArrayUnsafe(ref first, 7, x, y));
                }
            }

            uint[] guardedDest = CreateGuardedPixels(257, GuardPixelA, GuardPixelB);
            uint[] guardedSrc = CreateGuardedPixels(257, GuardPixelB, GuardPixelA);
            Random random = new Random(0xB1EAD);
            for (int i = 1; i <= 257; i++)
            {
                guardedDest[i] = NextUInt(random);
                guardedSrc[i] = NextUInt(random);
            }
            uint[] expected = new uint[257];
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = guardedDest[i + 1];
                ReferenceBlend(ref expected[i], guardedSrc[i + 1], byte.MaxValue);
            }
            fixed (uint* destinationBase = guardedDest)
            fixed (uint* sourceBase = guardedSrc)
            {
                FastMemory.BlendLayer(destinationBase + 1, sourceBase + 1, 257, 0);
            }
            AssertPixelGuards(guardedDest, GuardPixelA, GuardPixelB);
            AssertPixelGuards(guardedSrc, GuardPixelB, GuardPixelA);
            for (int i = 0; i < expected.Length; i++) AssertColorNear(expected[i], guardedDest[i + 1], 1);

            uint before = guardedDest[20];
            fixed (uint* destinationBase = guardedDest)
            fixed (uint* sourceBase = guardedSrc)
            {
                FastMemory.BlendLayer(destinationBase + 1, sourceBase + 1, 0, 0);
                FastMemory.BlendLayer(destinationBase + 1, sourceBase + 1, 257, 255, 1f);
            }
            AssertEqual(before, guardedDest[20]);
            AssertThrows<ArgumentOutOfRangeException>(() => BlendLayerWithCount(guardedDest, guardedSrc, -1));
            AssertThrows<ArgumentOutOfRangeException>(() => BlendLayerWithThreshold(guardedDest, guardedSrc, -1));
            AssertThrows<ArgumentOutOfRangeException>(() => BlendLayerWithThreshold(guardedDest, guardedSrc, 256));
            AssertThrows<ArgumentOutOfRangeException>(() => BlendLayerWithOpacity(guardedDest, guardedSrc, float.NaN));

            for (int i = 0; i < 100000; i++)
            {
                uint dest = NextUInt(random);
                uint src = NextUInt(random);
                byte opacity = (byte)random.Next(256);
                uint expectedColor = dest;
                ReferenceBlend(ref expectedColor, src, opacity);
                uint actual = dest;
                FastMemory.BlendColor(ref actual, src, opacity);
                AssertColorNear(expectedColor, actual, 1);
            }
        }

        private static unsafe void TestDrawingReferenceModels()
        {
            const int width = 29;
            const int height = 23;
            Random random = new Random(0xD12A);
            for (int iteration = 0; iteration < 1000; iteration++)
            {
                uint[] guarded = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                uint[] expected = new uint[width * height];
                int x0 = random.Next(-20, width + 20);
                int y0 = random.Next(-20, height + 20);
                int x1 = random.Next(-20, width + 20);
                int y1 = random.Next(-20, height + 20);
                uint color = NextUInt(random);
                ReferenceDrawLine(expected, width, height, x0, y0, x1, y1, color);
                fixed (uint* pixels = guarded)
                    Algorithms.DrawLineBresenham(pixels + 1, width, height, x0, y0, x1, y1, color);
                AssertPixelGuards(guarded, GuardPixelA, GuardPixelB);
                for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], guarded[i + 1]);
            }

            for (int brushSize = 1; brushSize <= 12; brushSize++)
            {
                foreach (Algorithms.BrushShape shape in Enum.GetValues(typeof(Algorithms.BrushShape)))
                {
                    uint[] guarded = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                    uint[] expected = new uint[width * height];
                    ReferenceDrawThickLine(expected, width, height, -3, 2, width + 2, height - 3, 0xDEADBEEFu, brushSize, shape);
                    fixed (uint* pixels = guarded)
                        Algorithms.DrawThickLineBresenham(pixels + 1, width, height, -3, 2, width + 2, height - 3, 0xDEADBEEFu, brushSize, shape);
                    AssertPixelGuards(guarded, GuardPixelA, GuardPixelB);
                    for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], guarded[i + 1]);
                }
            }

            AssertThrows<ArgumentNullException>(() => Algorithms.DrawLineBresenham(null, 1, 1, 0, 0, 0, 0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => DrawOnSinglePixel(0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => DrawOnSinglePixel(1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => DrawThickInvalidBrushSize());

            const int fillWidth = 31;
            const int fillHeight = 27;
            for (int iteration = 0; iteration < 400; iteration++)
            {
                uint[] guarded = CreateGuardedPixels(fillWidth * fillHeight, GuardPixelA, GuardPixelB);
                uint[] expected = new uint[fillWidth * fillHeight];
                for (int i = 0; i < expected.Length; i++)
                {
                    uint value = (uint)random.Next(5);
                    guarded[i + 1] = value;
                    expected[i] = value;
                }
                int startX = random.Next(-2, fillWidth + 2);
                int startY = random.Next(-2, fillHeight + 2);
                uint replacement = (uint)random.Next(5, 10);
                ReferenceFloodFill(expected, fillWidth, fillHeight, startX, startY, replacement);
                fixed (uint* pixels = guarded)
                    Algorithms.FloodFill(pixels + 1, fillWidth, fillHeight, startX, startY, replacement);
                AssertPixelGuards(guarded, GuardPixelA, GuardPixelB);
                for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], guarded[i + 1]);
            }

            uint[] pathological = CreateGuardedPixels(128 * 128, GuardPixelA, GuardPixelB);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++) pathological[1 + y * 128 + x] = (uint)((x + y) & 1);
            fixed (uint* pixels = pathological)
                Algorithms.FloodFill(pixels + 1, 128, 128, 64, 64, 7u);
            AssertPixelGuards(pathological, GuardPixelA, GuardPixelB);
        }

        private static unsafe void TestPixelReferenceModels()
        {
            Random random = new Random(0x51CA1E);
            for (int iteration = 0; iteration < 500; iteration++)
            {
                int srcWidth = random.Next(1, 12);
                int srcHeight = random.Next(1, 12);
                int destWidth = random.Next(1, 20);
                int destHeight = random.Next(1, 20);
                float scale = 0.15f + (float)random.NextDouble() * 4f;
                int offsetX = random.Next(-15, 16);
                int offsetY = random.Next(-15, 16);
                uint[] source = CreateGuardedPixels(srcWidth * srcHeight, GuardPixelA, GuardPixelB);
                uint[] destination = CreateGuardedPixels(destWidth * destHeight, GuardPixelB, GuardPixelA);
                uint[] expected = new uint[destWidth * destHeight];
                for (int i = 0; i < srcWidth * srcHeight; i++) source[i + 1] = NextUInt(random);
                ReferenceScale(source, 1, srcWidth, srcHeight, expected, destWidth, destHeight, scale, offsetX, offsetY);
                fixed (uint* sourceBase = source)
                fixed (uint* destinationBase = destination)
                {
                    FastMemory.ScaleBlitNearestNeighbor(sourceBase + 1, srcWidth, srcHeight,
                        destinationBase + 1, destWidth, destHeight, scale, offsetX, offsetY);
                }
                AssertPixelGuards(source, GuardPixelA, GuardPixelB);
                AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], destination[i + 1]);
            }

            for (int iteration = 0; iteration < 500; iteration++)
            {
                int srcWidth = random.Next(1, 10);
                int srcHeight = random.Next(1, 10);
                int destWidth = random.Next(1, 17);
                int destHeight = random.Next(1, 17);
                int destX = random.Next(-12, 18);
                int destY = random.Next(-12, 18);
                uint[] source = CreateGuardedPixels(srcWidth * srcHeight, GuardPixelA, GuardPixelB);
                uint[] destination = CreateGuardedPixels(destWidth * destHeight, GuardPixelB, GuardPixelA);
                uint[] expected = new uint[destWidth * destHeight];
                for (int i = 0; i < srcWidth * srcHeight; i++) source[i + 1] = NextUInt(random);
                for (int i = 0; i < expected.Length; i++)
                {
                    destination[i + 1] = NextUInt(random);
                    expected[i] = destination[i + 1];
                }
                ReferenceStamp(source, 1, srcWidth, srcHeight, expected, destWidth, destHeight, destX, destY);
                fixed (uint* sourceBase = source)
                fixed (uint* destinationBase = destination)
                {
                    FastMemory.StampBlit(sourceBase + 1, srcWidth, srcHeight,
                        destinationBase + 1, destWidth, destHeight, destX, destY);
                }
                AssertPixelGuards(source, GuardPixelA, GuardPixelB);
                AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                for (int i = 0; i < expected.Length; i++) AssertColorNear(expected[i], destination[i + 1], 1);
            }

            AssertThrows<ArgumentOutOfRangeException>(() => ScaleInvalid(0f));
            AssertThrows<ArgumentOutOfRangeException>(() => ScaleInvalid(float.NaN));
            AssertThrows<ArgumentOutOfRangeException>(() => ScaleInvalid(float.PositiveInfinity));
            AssertThrows<ArgumentOutOfRangeException>(() => StampInvalidDimensions());
        }

        private static unsafe void TestBlurReferenceModel()
        {
            Random random = new Random(0xB10B10);
            for (int iteration = 0; iteration < 350; iteration++)
            {
                int width = random.Next(1, 18);
                int height = random.Next(1, 18);
                int radius = random.Next(0, 10);
                int count = width * height;
                uint[] source = CreateGuardedPixels(count, GuardPixelA, GuardPixelB);
                uint[] destination = CreateGuardedPixels(count, GuardPixelB, GuardPixelA);
                uint[] scratch = CreateGuardedPixels(count, 0x11223344u, 0x55667788u);
                uint[] referenceSource = new uint[count];
                for (int i = 0; i < count; i++)
                {
                    source[i + 1] = NextUInt(random);
                    referenceSource[i] = source[i + 1];
                }
                uint[] expected = ReferenceSeparableBlur(referenceSource, width, height, radius);
                fixed (uint* sourceBase = source)
                fixed (uint* destinationBase = destination)
                fixed (uint* scratchBase = scratch)
                {
                    FastEffects.BoxBlur(sourceBase + 1, destinationBase + 1, scratchBase + 1, width, height, radius);
                }
                AssertPixelGuards(source, GuardPixelA, GuardPixelB);
                AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                AssertPixelGuards(scratch, 0x11223344u, 0x55667788u);
                for (int i = 0; i < count; i++) AssertColorNear(expected[i], destination[i + 1], 2);
                for (int i = 0; i < count; i++) AssertEqual(referenceSource[i], source[i + 1]);
            }

            uint[] inPlace = new uint[81];
            for (int i = 0; i < inPlace.Length; i++) inPlace[i] = NextUInt(random);
            uint[] expectedInPlace = ReferenceSeparableBlur((uint[])inPlace.Clone(), 9, 9, 4);
            fixed (uint* pixels = inPlace) FastEffects.BoxBlur(pixels, pixels, 9, 9, 4);
            for (int i = 0; i < inPlace.Length; i++) AssertColorNear(expectedInPlace[i], inPlace[i], 2);

            AssertThrows<ArgumentNullException>(() => BlurNullSource());
            AssertThrows<ArgumentNullException>(() => BlurNullDestination());
            AssertThrows<ArgumentOutOfRangeException>(() => BlurInvalid(0, 1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => BlurInvalid(1, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => BlurInvalid(1, 1, -1));
            AssertThrows<OverflowException>(() => BlurInvalid(1, 1, int.MaxValue));
            AssertThrows<ArgumentException>(() => BlurAliasedScratch(true));
            AssertThrows<ArgumentException>(() => BlurAliasedScratch(false));
        }

        private static unsafe void TestProceduralAndDeformation()
        {
            Random random = new Random(0xCE11);
            for (int iteration = 0; iteration < 500; iteration++)
            {
                int width = random.Next(1, 24);
                int height = random.Next(1, 24);
                short target = 7;
                short baseline = -3;
                int threshold = random.Next(-2, 11);
                short[] grid = new short[width * height];
                short[] original = new short[grid.Length];
                short[] buffer = new short[grid.Length];
                for (int i = 0; i < grid.Length; i++)
                {
                    grid[i] = random.Next(2) == 0 ? target : baseline;
                    original[i] = grid[i];
                    buffer[i] = short.MaxValue;
                }
                short[] expected = ReferenceCellular(original, width, height, target, baseline, threshold);
                FastProceduralGen.SmoothCellularAutomata(grid, buffer, width, height, target, baseline, threshold);
                AssertSequenceEqual(expected, grid);
                AssertSequenceEqual(expected, buffer);
            }
            AssertThrows<ArgumentNullException>(() => FastProceduralGen.SmoothCellularAutomata(null, new short[1], 1, 1, 1, 0));
            AssertThrows<ArgumentNullException>(() => FastProceduralGen.SmoothCellularAutomata(new short[1], null, 1, 1, 1, 0));
            short[] aliased = new short[1];
            AssertThrows<ArgumentException>(() => FastProceduralGen.SmoothCellularAutomata(aliased, aliased, 1, 1, 1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastProceduralGen.SmoothCellularAutomata(new short[1], new short[1], 0, 1, 1, 0));
            AssertThrows<OverflowException>(() => FastProceduralGen.SmoothCellularAutomata(new short[0], new short[0], int.MaxValue, 2, 1, 0));

            const int walkWidth = 7;
            const int walkHeight = 6;
            uint[] walkSource = CreateGuardedPixels(walkWidth * walkHeight, GuardPixelA, GuardPixelB);
            uint[] walkDestination = CreateGuardedPixels(walkWidth * walkHeight, GuardPixelB, GuardPixelA);
            for (int i = 0; i < walkWidth * walkHeight; i++) walkSource[i + 1] = (uint)(i + 1);
            uint[] expectedWalk = ReferenceWalk(walkSource, 1, walkWidth, walkHeight, 0.25f, 3, 2f, 2f);
            fixed (uint* sourceBase = walkSource)
            fixed (uint* destinationBase = walkDestination)
            {
                FastDeformation.GenerateWalkFrame(sourceBase + 1, destinationBase + 1,
                    walkWidth, walkHeight, 0.25f, 3, 2f, 2f);
            }
            AssertPixelGuards(walkSource, GuardPixelA, GuardPixelB);
            AssertPixelGuards(walkDestination, GuardPixelB, GuardPixelA);
            for (int i = 0; i < expectedWalk.Length; i++) AssertEqual(expectedWalk[i], walkDestination[i + 1]);

            float[] amplitudes = new float[BackendConfig.Deformation.OctantCount];
            float[] frequencies = new float[BackendConfig.Deformation.OctantCount];
            for (int i = 0; i < frequencies.Length; i++) frequencies[i] = i + 1;
            uint[] jitterDestination = CreateGuardedPixels(walkWidth * walkHeight, GuardPixelB, GuardPixelA);
            fixed (uint* sourceBase = walkSource)
            fixed (uint* destinationBase = jitterDestination)
            fixed (float* amplitudePointer = amplitudes)
            fixed (float* frequencyPointer = frequencies)
            {
                FastDeformation.GenerateJitterFrame(sourceBase + 1, destinationBase + 1,
                    walkWidth, walkHeight, 0.75f, amplitudePointer, frequencyPointer);
            }
            AssertPixelGuards(jitterDestination, GuardPixelB, GuardPixelA);
            for (int i = 0; i < walkWidth * walkHeight; i++) AssertEqual(walkSource[i + 1], jitterDestination[i + 1]);

            AssertThrows<ArgumentOutOfRangeException>(() => WalkInvalidTime(float.NaN));
            AssertThrows<ArgumentOutOfRangeException>(() => WalkInvalidTime(-0.001f));
            AssertThrows<ArgumentOutOfRangeException>(() => WalkInvalidTime(1.001f));
            AssertThrows<ArgumentOutOfRangeException>(() => WalkInvalidSplit(-1));
            AssertThrows<ArgumentOutOfRangeException>(() => WalkInvalidSplit(2));
            AssertThrows<ArgumentOutOfRangeException>(() => WalkInvalidAmplitude(float.PositiveInfinity));
            AssertThrows<ArgumentOutOfRangeException>(() => JitterInvalidValue(true));
            AssertThrows<ArgumentOutOfRangeException>(() => JitterInvalidValue(false));
        }

        private static void TestPhysicsReference()
        {
            Random random = new Random(0x50485953);
            for (int i = 0; i < 100000; i++)
            {
                float dirX = (float)(random.NextDouble() * 20d - 10d);
                float dirY = (float)(random.NextDouble() * 20d - 10d);
                float speed = (float)(random.NextDouble() * 1000d - 500d);
                float delta = (float)(random.NextDouble() * 4d - 2d);
                float x = (float)(random.NextDouble() * 10000d - 5000d);
                float y = (float)(random.NextDouble() * 10000d - 5000d);
                float expectedX = x + dirX * (speed * delta);
                float expectedY = y + dirY * (speed * delta);
                FastPhysics.CalculateTranslation(dirX, dirY, speed, delta, ref x, ref y);
                AssertEqual(expectedX, x);
                AssertEqual(expectedY, y);
            }
            float stationaryX = float.MaxValue;
            float stationaryY = -float.MaxValue;
            FastPhysics.CalculateTranslation(1f, 1f, 100f, 0f, ref stationaryX, ref stationaryY);
            AssertEqual(float.MaxValue, stationaryX);
            AssertEqual(-float.MaxValue, stationaryY);
        }

        private static bool IsNegativeBitSet(float value) => BitConverter.SingleToInt32Bits(value) < 0;

        private static FastMath.FacingDirection ReferenceFacing(float x, float y)
        {
            if (System.Math.Abs(y) > System.Math.Abs(x))
                return y < 0 ? FastMath.FacingDirection.Down : FastMath.FacingDirection.Up;
            return x < 0 ? FastMath.FacingDirection.Left : FastMath.FacingDirection.Right;
        }

        private static int ReferenceFnv1A(string value)
        {
            if (string.IsNullOrEmpty(value)) return BackendConfig.Serialization.NullHash;
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        // ------------------------------------------------------------------
        // rectangle and ellipse shape rasterizer reference models (item #9)
        // ------------------------------------------------------------------
        private static unsafe void TestShapeRasterizerReferenceModels()
        {
            const int width = 29;
            const int height = 23;
            Random random = new Random(0x5A9E0001);

            for (int iteration = 0; iteration < 500; iteration++)
            {
                int x0 = random.Next(-20, width + 20);
                int y0 = random.Next(-20, height + 20);
                int x1 = random.Next(-20, width + 20);
                int y1 = random.Next(-20, height + 20);
                int thickness = 1 + random.Next(9);
                bool filled = random.Next(2) == 0;
                uint color = NextUInt(random) | 0xFF000000u;

                uint[] guardedRect = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                uint[] expectedRect = new uint[width * height];
                ReferenceDrawRectangle(expectedRect, width, height, x0, y0, x1, y1, color, thickness, filled);
                fixed (uint* pixels = guardedRect)
                    Algorithms.DrawRectangle(pixels + 1, width, height, x0, y0, x1, y1, color, thickness, filled);
                AssertPixelGuards(guardedRect, GuardPixelA, GuardPixelB);
                for (int i = 0; i < expectedRect.Length; i++) AssertEqual(expectedRect[i], guardedRect[i + 1]);

                uint[] guardedEllipse = CreateGuardedPixels(width * height, GuardPixelB, GuardPixelA);
                uint[] expectedEllipse = new uint[width * height];
                ReferenceDrawEllipse(expectedEllipse, width, height, x0, y0, x1, y1, color, thickness, filled);
                fixed (uint* pixels = guardedEllipse)
                    Algorithms.DrawEllipse(pixels + 1, width, height, x0, y0, x1, y1, color, thickness, filled);
                AssertPixelGuards(guardedEllipse, GuardPixelB, GuardPixelA);
                for (int i = 0; i < expectedEllipse.Length; i++) AssertEqual(expectedEllipse[i], guardedEllipse[i + 1]);
            }

            // Far off-canvas anchors terminate (bounded by the canvas sweep) and
            // clip exactly like the reference model.
            {
                uint[] guarded = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                uint[] expected = new uint[width * height];
                ReferenceDrawRectangle(expected, width, height, -100000, -100000, 100000, 100000, 7u, 3, false);
                fixed (uint* pixels = guarded)
                    Algorithms.DrawRectangle(pixels + 1, width, height, -100000, -100000, 100000, 100000, 7u, 3, false);
                AssertPixelGuards(guarded, GuardPixelA, GuardPixelB);
                // The border band lies entirely off-canvas, so nothing is drawn.
                for (int i = 0; i < expected.Length; i++)
                {
                    AssertEqual(0u, expected[i]);
                    AssertEqual(expected[i], guarded[i + 1]);
                }

                uint[] guardedFilled = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                fixed (uint* pixels = guardedFilled)
                    Algorithms.DrawEllipse(pixels + 1, width, height, -100000, -100000, 100000, 100000, 7u, 1, true);
                AssertPixelGuards(guardedFilled, GuardPixelA, GuardPixelB);
                // The canvas sits well inside the huge ellipse: fully covered.
                for (int i = 0; i < width * height; i++) AssertEqual(7u, guardedFilled[i + 1]);
            }

            // Degenerate boxes: a single point and a flat (zero-radius) ellipse
            // degrade to their filled bounding box.
            {
                uint[] guarded = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                fixed (uint* pixels = guarded)
                {
                    Algorithms.DrawRectangle(pixels + 1, width, height, 4, 5, 4, 5, 9u, 2, false);
                    Algorithms.DrawEllipse(pixels + 1, width, height, 10, 5, 10, 5, 11u, 1, false);
                    Algorithms.DrawEllipse(pixels + 1, width, height, 2, 9, 12, 9, 13u, 1, false);
                }
                AssertPixelGuards(guarded, GuardPixelA, GuardPixelB);
                AssertEqual(9u, guarded[1 + (5 * width) + 4]);
                AssertEqual(11u, guarded[1 + (5 * width) + 10]);
                for (int x = 2; x <= 12; x++) AssertEqual(13u, guarded[1 + (9 * width) + x]);
            }

            // Outline rings never develop gaps: every boundary pixel of the
            // filled ellipse (a filled pixel with an unfilled 4-neighbor or on
            // the box edge) is also part of the 1-pixel outline.
            foreach (var box in new[] { new[] { 2, 3, 22, 17 }, new[] { 0, 0, 28, 6 }, new[] { 5, 2, 9, 20 } })
            {
                uint[] filledPixels = new uint[width * height];
                uint[] outlinePixels = new uint[width * height];
                fixed (uint* filledPtr = filledPixels)
                fixed (uint* outlinePtr = outlinePixels)
                {
                    Algorithms.DrawEllipse(filledPtr, width, height, box[0], box[1], box[2], box[3], 1u, 1, true);
                    Algorithms.DrawEllipse(outlinePtr, width, height, box[0], box[1], box[2], box[3], 1u, 1, false);
                }
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = (y * width) + x;
                        if (filledPixels[index] == 0u)
                        {
                            // The outline is a subset of the filled ellipse.
                            AssertEqual(0u, outlinePixels[index]);
                            continue;
                        }
                        bool boundary =
                            x == 0 || filledPixels[index - 1] == 0u ||
                            x == width - 1 || filledPixels[index + 1] == 0u ||
                            y == 0 || filledPixels[index - width] == 0u ||
                            y == height - 1 || filledPixels[index + width] == 0u;
                        if (boundary) AssertEqual(1u, outlinePixels[index]);
                    }
                }

                // A ring thicker than both radii degrades to the solid fill.
                uint[] thickRing = new uint[width * height];
                fixed (uint* thickPtr = thickRing)
                    Algorithms.DrawEllipse(thickPtr, width, height, box[0], box[1], box[2], box[3], 1u, width + height, false);
                AssertSequenceEqual(filledPixels, thickRing);
            }

            // Guard contracts shared with the other drawing entry points.
            AssertThrows<ArgumentNullException>(() => Algorithms.DrawRectangle(null, 1, 1, 0, 0, 0, 0, 1u, 1, true));
            AssertThrows<ArgumentNullException>(() => Algorithms.DrawEllipse(null, 1, 1, 0, 0, 0, 0, 1u, 1, true));
            AssertThrows<ArgumentOutOfRangeException>(() => ShapeOnSinglePixel(0, 1, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => ShapeOnSinglePixel(1, 0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => ShapeOnSinglePixel(1, 1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => EllipseOnSinglePixel(0, 1, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => EllipseOnSinglePixel(1, 0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => EllipseOnSinglePixel(1, 1, -1));
        }

        private static unsafe void ShapeOnSinglePixel(int width, int height, int thickness)
        {
            uint pixel = 0;
            Algorithms.DrawRectangle(&pixel, width, height, 0, 0, 0, 0, 1u, thickness, false);
        }

        private static unsafe void EllipseOnSinglePixel(int width, int height, int thickness)
        {
            uint pixel = 0;
            Algorithms.DrawEllipse(&pixel, width, height, 0, 0, 0, 0, 1u, thickness, false);
        }

        private static void ReferenceDrawRectangle(
            uint[] canvas, int width, int height,
            int x0, int y0, int x1, int y1,
            uint color, int thickness, bool filled)
        {
            long minX = Math.Min(x0, x1);
            long maxX = Math.Max(x0, x1);
            long minY = Math.Min(y0, y1);
            long maxY = Math.Max(y0, y1);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x < minX || x > maxX || y < minY || y > maxY) continue;
                    bool inHole = !filled &&
                        x >= minX + thickness && x <= maxX - thickness &&
                        y >= minY + thickness && y <= maxY - thickness;
                    if (!inHole) canvas[(y * width) + x] = color;
                }
            }
        }

        private static void ReferenceDrawEllipse(
            uint[] canvas, int width, int height,
            int x0, int y0, int x1, int y1,
            uint color, int thickness, bool filled)
        {
            long minX = Math.Min(x0, x1);
            long maxX = Math.Max(x0, x1);
            long minY = Math.Min(y0, y1);
            long maxY = Math.Max(y0, y1);
            double radiusX = (maxX - minX) / 2.0;
            double radiusY = (maxY - minY) / 2.0;
            if (radiusX == 0.0 || radiusY == 0.0)
            {
                ReferenceDrawRectangle(canvas, width, height, x0, y0, x1, y1, color, thickness, true);
                return;
            }

            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x < minX || x > maxX || y < minY || y > maxY) continue;
                    if (!ReferenceInsideEllipse(x, y, centerX, centerY, radiusX, radiusY)) continue;
                    if (!filled)
                    {
                        // Morphological ring: survive erosion by the
                        // thickness-radius square (all four corners inside,
                        // which suffices for a convex shape) and the pixel is
                        // NOT part of the outline.
                        bool eroded =
                            ReferenceInsideEllipse(x - thickness, y - thickness, centerX, centerY, radiusX, radiusY) &&
                            ReferenceInsideEllipse(x + thickness, y - thickness, centerX, centerY, radiusX, radiusY) &&
                            ReferenceInsideEllipse(x - thickness, y + thickness, centerX, centerY, radiusX, radiusY) &&
                            ReferenceInsideEllipse(x + thickness, y + thickness, centerX, centerY, radiusX, radiusY);
                        if (eroded) continue;
                    }
                    canvas[(y * width) + x] = color;
                }
            }
        }

        private static bool ReferenceInsideEllipse(
            double x, double y, double centerX, double centerY, double radiusX, double radiusY)
        {
            double normalizedX = (x - centerX) / radiusX;
            double normalizedY = (y - centerY) / radiusY;
            return (normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1.0;
        }

        private static uint ReferenceXorShift(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private static uint NextUInt(Random random)
        {
            return (uint)random.Next(1 << 16) | ((uint)random.Next(1 << 16) << 16);
        }

        private static uint[] CreateGuardedPixels(int count, uint before, uint after)
        {
            uint[] pixels = new uint[count + 2];
            pixels[0] = before;
            pixels[pixels.Length - 1] = after;
            return pixels;
        }

        private static void AssertPixelGuards(uint[] pixels, uint before, uint after)
        {
            AssertEqual(before, pixels[0]);
            AssertEqual(after, pixels[pixels.Length - 1]);
        }

        private static void ReferenceBlend(ref uint destination, uint source, byte opacity)
        {
            int sourceAlpha = (byte)(source >> 24);
            sourceAlpha = (sourceAlpha * opacity + 127) / 255;
            if (sourceAlpha == 0) return;
            uint adjustedSource = (source & 0x00FFFFFFu) | ((uint)sourceAlpha << 24);
            if (sourceAlpha == 255 || (byte)(destination >> 24) == 0)
            {
                destination = adjustedSource;
                return;
            }

            double sa = sourceAlpha / 255d;
            double da = (byte)(destination >> 24) / 255d;
            double outA = sa + da * (1d - sa);
            int r = RoundByte(((byte)source * sa + (byte)destination * da * (1d - sa)) / outA);
            int g = RoundByte(((byte)(source >> 8) * sa + (byte)(destination >> 8) * da * (1d - sa)) / outA);
            int b = RoundByte(((byte)(source >> 16) * sa + (byte)(destination >> 16) * da * (1d - sa)) / outA);
            int a = RoundByte(outA * 255d);
            destination = (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);
        }

        private static int RoundByte(double value)
        {
            return System.Math.Clamp((int)System.Math.Floor(value + 0.5d), 0, 255);
        }

        private static void AssertColorNear(uint expected, uint actual, int tolerance)
        {
            for (int shift = 0; shift <= 24; shift += 8)
            {
                int expectedChannel = (byte)(expected >> shift);
                int actualChannel = (byte)(actual >> shift);
                Assert(System.Math.Abs(expectedChannel - actualChannel) <= tolerance);
            }
        }

        private static unsafe void BlendLayerWithCount(uint[] destination, uint[] source, int count)
        {
            fixed (uint* destinationPointer = destination)
            fixed (uint* sourcePointer = source)
                FastMemory.BlendLayer(destinationPointer, sourcePointer, count, 0);
        }

        private static unsafe void BlendLayerWithThreshold(uint[] destination, uint[] source, int threshold)
        {
            fixed (uint* destinationPointer = destination)
            fixed (uint* sourcePointer = source)
                FastMemory.BlendLayer(destinationPointer, sourcePointer, 1, threshold);
        }

        private static unsafe void BlendLayerWithOpacity(uint[] destination, uint[] source, float opacity)
        {
            fixed (uint* destinationPointer = destination)
            fixed (uint* sourcePointer = source)
                FastMemory.BlendLayer(destinationPointer, sourcePointer, 1, 0, opacity);
        }

        private static void ReferenceDrawLine(uint[] canvas, int width, int height, int x0, int y0, int x1, int y1, uint color)
        {
            int dx = System.Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -System.Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                if ((uint)x0 < (uint)width && (uint)y0 < (uint)height) canvas[y0 * width + x0] = color;
                if (x0 == x1 && y0 == y1) return;
                int doubled = error * 2;
                if (doubled >= dy) { error += dy; x0 += sx; }
                if (doubled <= dx) { error += dx; y0 += sy; }
            }
        }

        private static void ReferenceDrawThickLine(uint[] canvas, int width, int height, int x0, int y0, int x1, int y1, uint color, int brushSize, Algorithms.BrushShape shape)
        {
            int dx = System.Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -System.Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            int minOffset = -(brushSize >> 1);
            int maxOffset = minOffset + brushSize - 1;
            bool odd = (brushSize & 1) != 0;
            while (true)
            {
                for (int by = minOffset; by <= maxOffset; by++)
                {
                    for (int bx = minOffset; bx <= maxOffset; bx++)
                    {
                        if (shape == Algorithms.BrushShape.Circle)
                        {
                            long radius = odd ? brushSize >> 1 : brushSize;
                            long circleX = odd ? bx : bx * 2L + 1L;
                            long circleY = odd ? by : by * 2L + 1L;
                            if (circleX * circleX + circleY * circleY > radius * radius) continue;
                        }
                        int x = x0 + bx;
                        int y = y0 + by;
                        if ((uint)x < (uint)width && (uint)y < (uint)height) canvas[y * width + x] = color;
                    }
                }
                if (x0 == x1 && y0 == y1) return;
                int doubled = error * 2;
                if (doubled >= dy) { error += dy; x0 += sx; }
                if (doubled <= dx) { error += dx; y0 += sy; }
            }
        }

        private static void ReferenceFloodFill(uint[] canvas, int width, int height, int startX, int startY, uint replacement)
        {
            if ((uint)startX >= (uint)width || (uint)startY >= (uint)height) return;
            uint target = canvas[startY * width + startX];
            if (target == replacement) return;
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(startY * width + startX);
            canvas[startY * width + startX] = replacement;
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % width;
                int y = index / width;
                TryFillReference(canvas, width, height, x - 1, y, target, replacement, queue);
                TryFillReference(canvas, width, height, x + 1, y, target, replacement, queue);
                TryFillReference(canvas, width, height, x, y - 1, target, replacement, queue);
                TryFillReference(canvas, width, height, x, y + 1, target, replacement, queue);
            }
        }

        private static void TryFillReference(uint[] canvas, int width, int height, int x, int y, uint target, uint replacement, Queue<int> queue)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
            int index = y * width + x;
            if (canvas[index] != target) return;
            canvas[index] = replacement;
            queue.Enqueue(index);
        }

        private static unsafe void DrawOnSinglePixel(int width, int height)
        {
            uint pixel = 0;
            Algorithms.DrawLineBresenham(&pixel, width, height, 0, 0, 0, 0, 1);
        }

        private static unsafe void DrawThickInvalidBrushSize()
        {
            uint pixel = 0;
            Algorithms.DrawThickLineBresenham(&pixel, 1, 1, 0, 0, 0, 0, 1, 0, Algorithms.BrushShape.Circle);
        }

        private static void ReferenceScale(uint[] source, int sourceOffset, int srcWidth, int srcHeight, uint[] destination, int destWidth, int destHeight, float scale, int offsetX, int offsetY)
        {
            float inverse = 1f / scale;
            for (int y = 0; y < destHeight; y++)
            {
                float sourceY = (y - offsetY) * inverse;
                int srcY = (int)sourceY;
                for (int x = 0; x < destWidth; x++)
                {
                    float sourceX = (x - offsetX) * inverse;
                    int srcX = (int)sourceX;
                    destination[y * destWidth + x] = sourceY >= 0f && sourceX >= 0f &&
                        (uint)srcX < (uint)srcWidth && (uint)srcY < (uint)srcHeight
                        ? source[sourceOffset + srcY * srcWidth + srcX]
                        : 0u;
                }
            }
        }

        private static void ReferenceStamp(uint[] source, int sourceOffset, int srcWidth, int srcHeight, uint[] destination, int destWidth, int destHeight, int destX, int destY)
        {
            for (int sy = 0; sy < srcHeight; sy++)
            {
                for (int sx = 0; sx < srcWidth; sx++)
                {
                    int dx = destX + sx;
                    int dy = destY + sy;
                    if ((uint)dx >= (uint)destWidth || (uint)dy >= (uint)destHeight) continue;
                    uint sourcePixel = source[sourceOffset + sy * srcWidth + sx];
                    if ((byte)(sourcePixel >> 24) != 0)
                        ReferenceBlend(ref destination[dy * destWidth + dx], sourcePixel, byte.MaxValue);
                }
            }
        }

        private static unsafe void ScaleInvalid(float scale)
        {
            uint source = 1;
            uint destination = 0;
            FastMemory.ScaleBlitNearestNeighbor(&source, 1, 1, &destination, 1, 1, scale, 0, 0);
        }

        private static unsafe void StampInvalidDimensions()
        {
            uint source = 1;
            uint destination = 0;
            FastMemory.StampBlit(&source, 0, 1, &destination, 1, 1, 0, 0);
        }

        // Mirrors FastEffects.BoxBlur exactly: the horizontal pass converts source
        // pixels to alpha-premultiplied bytes and averages them with the same
        // 16-bit fixed-point reciprocal the product uses; the vertical pass
        // averages those premultiplied bytes and un-premultiplies once at the end
        // (color sums divided by the alpha sum), so transparent neighbors cannot
        // darken sprite edges.
        private static uint[] ReferenceSeparableBlur(uint[] source, int width, int height, int radius)
        {
            if (radius == 0) return (uint[])source.Clone();
            int divisor = radius * 2 + 1;
            uint reciprocal = (uint)((1L << 16) / divisor);
            uint[] horizontal = new uint[source.Length];
            uint[] destination = new uint[source.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    horizontal[y * width + x] = AverageWindow(source, width, height, x, y, radius, reciprocal, true, true, false);
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    destination[y * width + x] = AverageWindow(horizontal, width, height, x, y, radius, reciprocal, false, false, true);
            }
            return destination;
        }

        private static uint AverageWindow(
            uint[] pixels,
            int width,
            int height,
            int x,
            int y,
            int radius,
            uint reciprocal,
            bool horizontal,
            bool premultiplyInput,
            bool unpremultiplyOutput)
        {
            int r = 0;
            int g = 0;
            int b = 0;
            int a = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                int sampleX = horizontal ? System.Math.Clamp(x + offset, 0, width - 1) : x;
                int sampleY = horizontal ? y : System.Math.Clamp(y + offset, 0, height - 1);
                uint pixel = pixels[sampleY * width + sampleX];
                int alpha = (byte)(pixel >> 24);
                if (premultiplyInput)
                {
                    r += ((byte)pixel * alpha + 127) / 255;
                    g += ((byte)(pixel >> 8) * alpha + 127) / 255;
                    b += ((byte)(pixel >> 16) * alpha + 127) / 255;
                }
                else
                {
                    r += (byte)pixel;
                    g += (byte)(pixel >> 8);
                    b += (byte)(pixel >> 16);
                }
                a += alpha;
            }

            uint averageR = FixedPointAverage(r, reciprocal);
            uint averageG = FixedPointAverage(g, reciprocal);
            uint averageB = FixedPointAverage(b, reciprocal);
            uint averageA = FixedPointAverage(a, reciprocal);
            if (unpremultiplyOutput)
            {
                if (averageA == 0) return 0u;
                averageR = UnpremultiplyReference(averageR, averageA);
                averageG = UnpremultiplyReference(averageG, averageA);
                averageB = UnpremultiplyReference(averageB, averageA);
            }
            return averageR | (averageG << 8) | (averageB << 16) | (averageA << 24);
        }

        private static uint FixedPointAverage(int sum, uint reciprocal)
        {
            uint average = (uint)(((ulong)(uint)sum * reciprocal + 32768) >> 16);
            return average > 255 ? 255 : average;
        }

        private static uint UnpremultiplyReference(uint premultiplied, uint alpha)
        {
            uint channel = (premultiplied * 255 + alpha / 2) / alpha;
            return channel > 255 ? 255 : channel;
        }

        private static unsafe void BlurNullSource()
        {
            uint destination = 0;
            FastEffects.BoxBlur(null, &destination, 1, 1, 0);
        }

        private static unsafe void BlurNullDestination()
        {
            uint source = 0;
            FastEffects.BoxBlur(&source, null, 1, 1, 0);
        }

        private static unsafe void BlurInvalid(int width, int height, int radius)
        {
            uint source = 0;
            uint destination = 0;
            FastEffects.BoxBlur(&source, &destination, width, height, radius);
        }

        private static unsafe void BlurAliasedScratch(bool aliasSource)
        {
            uint source = 0;
            uint destination = 0;
            FastEffects.BoxBlur(&source, &destination, aliasSource ? &source : &destination, 1, 1, 1);
        }

        private static short[] ReferenceCellular(short[] source, int width, int height, short target, short baseline, int threshold)
        {
            short[] result = new short[source.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int neighbors = 0;
                    for (int ny = y - 1; ny <= y + 1; ny++)
                    {
                        for (int nx = x - 1; nx <= x + 1; nx++)
                        {
                            if (nx == x && ny == y) continue;
                            if ((uint)nx >= (uint)width || (uint)ny >= (uint)height || source[ny * width + nx] == target) neighbors++;
                        }
                    }
                    result[y * width + x] = neighbors > threshold ? target : neighbors < threshold ? baseline : source[y * width + x];
                }
            }
            return result;
        }

        private static uint[] ReferenceWalk(uint[] source, int sourceOffset, int width, int height, float time, int splitY, float bounceAmplitude, float strideAmplitude)
        {
            uint[] destination = new uint[width * height];
            float bounceTime = time * 2f;
            if (bounceTime >= 1f) bounceTime -= 1f;
            int bounce = (int)(System.Math.Sin(bounceTime * System.Math.PI * 2d - System.Math.PI) * bounceAmplitude);
            float strideBase = (float)System.Math.Sin(time * System.Math.PI * 2d - System.Math.PI) * strideAmplitude;
            for (int y = 0; y < height; y++)
            {
                bool legs = y >= splitY;
                int stride = legs ? (int)(strideBase * ((y - splitY) / (float)(height - splitY))) : 0;
                for (int x = 0; x < width; x++)
                {
                    int sourceX = legs ? (x < width / 2 ? x - stride : x + stride) : x;
                    int sourceY = legs ? y : y - bounce;
                    destination[y * width + x] = (uint)sourceX < (uint)width && (uint)sourceY < (uint)height
                        ? source[sourceOffset + sourceY * width + sourceX]
                        : 0u;
                }
            }
            return destination;
        }

        private static unsafe void WalkInvalidTime(float time)
        {
            uint source = 0;
            uint destination = 0;
            FastDeformation.GenerateWalkFrame(&source, &destination, 1, 1, time, 0, 0f, 0f);
        }

        private static unsafe void WalkInvalidSplit(int split)
        {
            uint source = 0;
            uint destination = 0;
            FastDeformation.GenerateWalkFrame(&source, &destination, 1, 1, 0f, split, 0f, 0f);
        }

        private static unsafe void WalkInvalidAmplitude(float amplitude)
        {
            uint source = 0;
            uint destination = 0;
            FastDeformation.GenerateWalkFrame(&source, &destination, 1, 1, 0f, 0, amplitude, 0f);
        }

        private static unsafe void JitterInvalidValue(bool amplitude)
        {
            uint source = 0;
            uint destination = 0;
            float* amplitudes = stackalloc float[BackendConfig.Deformation.OctantCount];
            float* frequencies = stackalloc float[BackendConfig.Deformation.OctantCount];
            amplitudes[3] = amplitude ? float.NaN : 0f;
            frequencies[3] = amplitude ? 1f : float.PositiveInfinity;
            FastDeformation.GenerateJitterFrame(&source, &destination, 1, 1, 0.5f, amplitudes, frequencies);
        }
    }
}
