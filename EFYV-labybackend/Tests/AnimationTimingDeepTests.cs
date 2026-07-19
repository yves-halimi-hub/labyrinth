using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EFYVBackend.Core.Export;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    // Item #10: atlas timing/playback metadata contract (frameDurationsMs,
    // loopStart/loopEnd, pingPong, documentVersion range) and the bob/breathe +
    // shake/hit-flash deformation presets against managed reference models.
    internal static partial class Program
    {
        // ------------------------------------------------------------------
        // atlas timing metadata validation and wire round trip
        // ------------------------------------------------------------------
        private static void TestAnimationTimingAtlasContract()
        {
            // 4x4 frames on a 16x8 atlas => capacity 8; two animations of 3 + 5.
            AtlasMetadataJson Valid()
            {
                return new AtlasMetadataJson
                {
                    formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                    frameWidth = 4,
                    frameHeight = 4,
                    atlasWidth = 16,
                    atlasHeight = 8,
                    animations = new List<AnimationMetadataJson>
                    {
                        new AnimationMetadataJson { name = "Intro", fps = 8, startFrame = 0, frameCount = 3 },
                        new AnimationMetadataJson { name = "Loop", fps = 12, startFrame = 3, frameCount = 5 }
                    }
                };
            }

            // Baseline without optional fields stays valid.
            Assert(FastExporter.TryValidateAtlasMetadata(Valid(), out AtlasMetadataError baseline));
            AssertEqual(AtlasMetadataError.None, baseline);

            // Fully populated optional fields validate: sentinel 0 entries mix
            // with real overrides, the loop range is animation-local.
            AtlasMetadataJson populated = Valid();
            AnimationMetadataJson loopAnimation = populated.animations[1];
            loopAnimation.frameDurationsMs = new List<int>
            {
                0, 40, BackendConfig.Exporter.MaxFrameDurationMs, 0, 125
            };
            loopAnimation.loopStart = 1;
            loopAnimation.loopEnd = 3;
            loopAnimation.pingPong = true;
            populated.animations[1] = loopAnimation;
            Assert(FastExporter.TryValidateAtlasMetadata(populated, out AtlasMetadataError populatedError));
            AssertEqual(AtlasMetadataError.None, populatedError);

            // Boundary loop values: start 0 / end frameCount-1 explicit.
            AtlasMetadataJson explicitFull = Valid();
            AnimationMetadataJson fullAnimation = explicitFull.animations[0];
            fullAnimation.loopStart = 0;
            fullAnimation.loopEnd = 2;
            fullAnimation.pingPong = false;
            explicitFull.animations[0] = fullAnimation;
            Assert(FastExporter.TryValidateAtlasMetadata(explicitFull, out AtlasMetadataError explicitError));
            AssertEqual(AtlasMetadataError.None, explicitError);

            // Rejection matrix for the new fields (all mutate the 5-frame
            // "Loop" animation at index 1).
            void AssertTimingError(Func<AnimationMetadataJson, AnimationMetadataJson> mutate, AtlasMetadataError expected)
            {
                AtlasMetadataJson broken = Valid();
                broken.animations[1] = mutate(broken.animations[1]);
                Assert(!FastExporter.TryValidateAtlasMetadata(broken, out AtlasMetadataError error));
                AssertEqual(expected, error);
            }

            AssertTimingError(
                a => { a.frameDurationsMs = new List<int> { 10, 10 }; return a; },
                AtlasMetadataError.AnimationFrameDurations); // wrong count (2 != 5)
            AssertTimingError(
                a => { a.frameDurationsMs = new List<int> { 0, 0, 0, 0, -1 }; return a; },
                AtlasMetadataError.AnimationFrameDurations); // negative entry
            AssertTimingError(
                a =>
                {
                    a.frameDurationsMs = new List<int>
                    {
                        0, 0, 0, 0, BackendConfig.Exporter.MaxFrameDurationMs + 1
                    };
                    return a;
                },
                AtlasMetadataError.AnimationFrameDurations); // over the cap
            AssertTimingError(
                a => { a.loopStart = -1; return a; },
                AtlasMetadataError.AnimationLoopRange);
            AssertTimingError(
                a => { a.loopStart = 5; return a; },
                AtlasMetadataError.AnimationLoopRange); // == frameCount
            AssertTimingError(
                a => { a.loopStart = 3; a.loopEnd = 2; return a; },
                AtlasMetadataError.AnimationLoopRange); // end < start
            AssertTimingError(
                a => { a.loopEnd = 5; return a; },
                AtlasMetadataError.AnimationLoopRange); // end == frameCount
            AssertTimingError(
                a => { a.loopEnd = -2; return a; },
                AtlasMetadataError.AnimationLoopRange);

            // The exporter's hand-written writer + reflection importer round
            // trip the fields exactly and OMIT absent optional members.
            string root = Path.Combine(
                Path.GetTempPath(), "EFYVAnimTiming-" + Guid.NewGuid().ToString("N"));
            try
            {
                var properties = new Dictionary<string, object> { ["entityName"] = "TimingProbe" };
                var pixels = new uint[16 * 8];
                FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", properties, new List<HitboxJson>(), pixels, 16, 8, populated, "EnemyData");

                string jsonPath = Path.Combine(root, "TimingProbe" + BackendConfig.Exporter.EfyvExtension);
                EFYVJsonFormat imported = FastImporter.ParseEfyvFile(jsonPath);
                AssertEqual(BackendConfig.Exporter.CurrentDocumentVersion, imported.EffectiveDocumentVersion);
                Assert(imported.atlas.HasValue);
                List<AnimationMetadataJson> animations = imported.atlas.Value.animations;
                AssertEqual(2, animations.Count);

                // Animation 0 carried no optional fields: absent on the wire.
                AssertEqual(null, animations[0].frameDurationsMs);
                Assert(!animations[0].loopStart.HasValue);
                Assert(!animations[0].loopEnd.HasValue);
                Assert(!animations[0].pingPong.HasValue);

                // Animation 1 round-trips every populated value bit-exactly.
                AssertSequenceEqual(
                    new[] { 0, 40, BackendConfig.Exporter.MaxFrameDurationMs, 0, 125 },
                    animations[1].frameDurationsMs.ToArray());
                AssertEqual(1, animations[1].loopStart.Value);
                AssertEqual(3, animations[1].loopEnd.Value);
                AssertEqual(true, animations[1].pingPong.Value);

                // Raw JSON shape: the defaults-only animation object contains
                // exactly the four base members (no null-spam), the populated
                // one exactly the eight.
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(jsonPath)))
                {
                    JsonElement animationArray = document.RootElement
                        .GetProperty(BackendConfig.Exporter.FieldAtlas)
                        .GetProperty(BackendConfig.Exporter.FieldAnimations);
                    var baseNames = new List<string>();
                    foreach (JsonProperty property in animationArray[0].EnumerateObject())
                        baseNames.Add(property.Name);
                    AssertSequenceEqual(
                        new[]
                        {
                            BackendConfig.Exporter.FieldName,
                            BackendConfig.Exporter.FieldFps,
                            BackendConfig.Exporter.FieldStartFrame,
                            BackendConfig.Exporter.FieldFrameCount
                        },
                        baseNames.ToArray());
                    var populatedNames = new List<string>();
                    foreach (JsonProperty property in animationArray[1].EnumerateObject())
                        populatedNames.Add(property.Name);
                    AssertSequenceEqual(
                        new[]
                        {
                            BackendConfig.Exporter.FieldName,
                            BackendConfig.Exporter.FieldFps,
                            BackendConfig.Exporter.FieldStartFrame,
                            BackendConfig.Exporter.FieldFrameCount,
                            BackendConfig.Exporter.FieldFrameDurationsMs,
                            BackendConfig.Exporter.FieldLoopStart,
                            BackendConfig.Exporter.FieldLoopEnd,
                            BackendConfig.Exporter.FieldPingPong
                        },
                        populatedNames.ToArray());
                }

                // Exporting INVALID timing metadata is rejected up front.
                AtlasMetadataJson invalid = Valid();
                AnimationMetadataJson broken = invalid.animations[1];
                broken.frameDurationsMs = new List<int> { 1 };
                invalid.animations[1] = broken;
                AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", properties, new List<HitboxJson>(), pixels, 16, 8, invalid, null));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }

            // Legacy documents (no optional members) parse with null optionals.
            string legacyDirectory = Path.Combine(
                Path.GetTempPath(), "EFYVAnimTimingLegacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(legacyDirectory);
            try
            {
                string legacyPath = Path.Combine(
                    legacyDirectory, "legacy" + BackendConfig.Exporter.EfyvExtension);
                File.WriteAllText(
                    legacyPath,
                    "{\"assetType\":\"EnemyData\",\"properties\":{},\"hitboxes\":[]," +
                    "\"atlas\":{\"formatVersion\":1,\"frameWidth\":4,\"frameHeight\":4," +
                    "\"atlasWidth\":8,\"atlasHeight\":4,\"animations\":[" +
                    "{\"name\":\"walk\",\"fps\":6,\"startFrame\":0,\"frameCount\":2}]}}");
                EFYVJsonFormat legacy = FastImporter.ParseEfyvFile(legacyPath);
                AssertEqual(BackendConfig.Exporter.LegacyDocumentVersion, legacy.EffectiveDocumentVersion);
                AnimationMetadataJson legacyAnimation = legacy.atlas.Value.animations[0];
                AssertEqual(null, legacyAnimation.frameDurationsMs);
                Assert(!legacyAnimation.loopStart.HasValue);
                Assert(!legacyAnimation.loopEnd.HasValue);
                Assert(!legacyAnimation.pingPong.HasValue);
                Assert(FastExporter.TryValidateAtlasMetadata(
                    legacy.atlas.Value, out AtlasMetadataError legacyError));
                AssertEqual(AtlasMetadataError.None, legacyError);
            }
            finally
            {
                Directory.Delete(legacyDirectory, true);
            }
        }

        // ------------------------------------------------------------------
        // bob/breathe reference sweep
        // ------------------------------------------------------------------
        private static unsafe void TestAnimationTimingBobBreatheReference()
        {
            // Guard contracts.
            AssertThrows<ArgumentNullException>(() => AnimationTimingBobNullCase(true));
            AssertThrows<ArgumentNullException>(() => AnimationTimingBobNullCase(false));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWithDims(0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWithDims(1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWith(float.NaN, 1f, 0f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWith(-0.001f, 1f, 0f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWith(1.001f, 1f, 0f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWith(0.5f, float.NaN, 0f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWith(0.5f, float.PositiveInfinity, 0f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWith(0.5f, 1f, float.NaN));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWith(
                0.5f, 1f, BackendConfig.Deformation.MaxBreatheAmplitude + 0.0001f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingBobWith(
                0.5f, 1f, -BackendConfig.Deformation.MaxBreatheAmplitude - 0.0001f));

            // Zero amplitudes are a verbatim copy at ANY time (scale stays
            // exactly 1 and the offset exactly 0).
            Random random = new Random(0x0B0B5EED);
            for (int probe = 0; probe < 8; probe++)
            {
                int width = random.Next(1, 12);
                int height = random.Next(1, 12);
                uint[] source = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                for (int i = 0; i < width * height; i++) source[i + 1] = NextUInt(random);
                uint[] destination = CreateGuardedPixels(width * height, GuardPixelB, GuardPixelA);
                float timeT = (float)random.NextDouble();
                fixed (uint* sourceBase = source)
                fixed (uint* destinationBase = destination)
                {
                    FastDeformation.GenerateBobBreatheFrame(
                        sourceBase + 1, destinationBase + 1, width, height, timeT, 0f, 0f);
                }
                AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                for (int i = 0; i < width * height; i++) AssertEqual(source[i + 1], destination[i + 1]);
            }

            // Randomized reference sweep, including the breathe cap boundary.
            float[] times = { 0f, 0.124f, 0.25f, 0.5f, 0.5001f, 0.75f, 0.9999f, 1f };
            for (int iteration = 0; iteration < 120; iteration++)
            {
                int width = random.Next(1, 17);
                int height = random.Next(1, 17);
                float bobAmp = (float)(random.NextDouble() * 12d - 6d);
                float breatheAmp = iteration % 7 == 0
                    ? BackendConfig.Deformation.MaxBreatheAmplitude * (random.Next(2) == 0 ? 1f : -1f)
                    : (float)((random.NextDouble() * 2d - 1d) * BackendConfig.Deformation.MaxBreatheAmplitude);
                float timeT = times[iteration % times.Length];

                uint[] source = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                for (int i = 0; i < width * height; i++) source[i + 1] = NextUInt(random);
                uint[] destination = CreateGuardedPixels(width * height, GuardPixelB, GuardPixelA);
                uint[] expected = AnimationTimingReferenceBobFrame(
                    source, 1, width, height, timeT, bobAmp, breatheAmp);
                fixed (uint* sourceBase = source)
                fixed (uint* destinationBase = destination)
                {
                    FastDeformation.GenerateBobBreatheFrame(
                        sourceBase + 1, destinationBase + 1, width, height, timeT, bobAmp, breatheAmp);
                }
                AssertPixelGuards(source, GuardPixelA, GuardPixelB);
                AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], destination[i + 1]);
            }

            // Pure bob at quarter cycle: the whole image shifts by a uniform
            // integer row delta with transparent fill (semantic, not just
            // reference-equality): wave(0.25) is the positive sine peak.
            const int bobWidth = 3;
            const int bobHeight = 9;
            uint[] bobSource = CreateGuardedPixels(bobWidth * bobHeight, GuardPixelA, GuardPixelB);
            for (int i = 0; i < bobWidth * bobHeight; i++) bobSource[i + 1] = (uint)(0xFF000000u | (uint)(i + 1));
            uint[] bobDestination = CreateGuardedPixels(bobWidth * bobHeight, GuardPixelB, GuardPixelA);
            fixed (uint* sourceBase = bobSource)
            fixed (uint* destinationBase = bobDestination)
            {
                FastDeformation.GenerateBobBreatheFrame(
                    sourceBase + 1, destinationBase + 1, bobWidth, bobHeight, 0.25f, 3f, 0f);
            }
            float quarterRad = FastMath.WrapRadians(0.25f * BackendConfig.Math.TwoPI - BackendConfig.Math.PI);
            float quarterWave = quarterRad *
                (BackendConfig.Math.TaylorSinA - BackendConfig.Math.TaylorSinB * System.Math.Abs(quarterRad));
            Assert(quarterWave < 0f); // sine of -pi/2 region: content shifts down
            for (int y = 0; y < bobHeight; y++)
            {
                int srcY = (bobHeight - 1) + (int)(y - (bobHeight - 1) + quarterWave * 3f);
                for (int x = 0; x < bobWidth; x++)
                {
                    uint expectedPixel = (uint)srcY < (uint)bobHeight
                        ? bobSource[srcY * bobWidth + x + 1]
                        : BackendConfig.Deformation.TransparentPixel;
                    AssertEqual(expectedPixel, bobDestination[y * bobWidth + x + 1]);
                }
            }
        }

        // ------------------------------------------------------------------
        // shake/hit-flash reference sweep
        // ------------------------------------------------------------------
        private static unsafe void TestAnimationTimingShakeFlashReference()
        {
            AssertThrows<ArgumentNullException>(() => AnimationTimingShakeNullCase(true));
            AssertThrows<ArgumentNullException>(() => AnimationTimingShakeNullCase(false));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWithDims(0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWithDims(1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWith(float.NaN, 1f, 0.5f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWith(-0.001f, 1f, 0.5f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWith(1.001f, 1f, 0.5f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWith(0.5f, float.NaN, 0.5f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWith(0.5f, float.NegativeInfinity, 0.5f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWith(0.5f, 1f, float.NaN));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWith(0.5f, 1f, -0.001f));
            AssertThrows<ArgumentOutOfRangeException>(() => AnimationTimingShakeWith(0.5f, 1f, 1.001f));

            Random random = new Random(0x5A4EF1A5);
            float[] times = { 0f, 0.1f, 0.25f, 0.4999f, 0.5f, 0.75f, 0.9f, 1f };
            for (int iteration = 0; iteration < 120; iteration++)
            {
                int width = random.Next(1, 17);
                int height = random.Next(1, 17);
                float shakeAmp = (float)(random.NextDouble() * 10d - 5d);
                float flashStrength = (float)random.NextDouble();
                float timeT = times[iteration % times.Length];

                uint[] source = CreateGuardedPixels(width * height, GuardPixelA, GuardPixelB);
                for (int i = 0; i < width * height; i++)
                    source[i + 1] = iteration % 3 == 0 && random.Next(3) == 0 ? 0u : NextUInt(random);
                uint[] destination = CreateGuardedPixels(width * height, GuardPixelB, GuardPixelA);
                uint[] expected = AnimationTimingReferenceShakeFlashFrame(
                    source, 1, width, height, timeT, shakeAmp, flashStrength);
                fixed (uint* sourceBase = source)
                fixed (uint* destinationBase = destination)
                {
                    FastDeformation.GenerateShakeFlashFrame(
                        sourceBase + 1, destinationBase + 1, width, height, timeT, shakeAmp, flashStrength);
                }
                AssertPixelGuards(source, GuardPixelA, GuardPixelB);
                AssertPixelGuards(destination, GuardPixelB, GuardPixelA);
                for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], destination[i + 1]);
            }

            // Semantics at the impact frame (timeT = 0): no shake offset (the
            // sine phase sits on a zero crossing), maximum flash: every opaque
            // channel moves toward white by exactly floor(flash*255)/255,
            // alpha and transparent pixels untouched.
            const int flashSize = 4;
            uint[] flashSource = CreateGuardedPixels(flashSize * flashSize, GuardPixelA, GuardPixelB);
            flashSource[0 + 1] = 0u;                       // transparent stays untouched
            flashSource[1 + 1] = 0xFF000000u;              // opaque black
            flashSource[2 + 1] = 0x80FF8040u;              // half alpha, mixed rgb
            for (int i = 3; i < flashSize * flashSize; i++)
                flashSource[i + 1] = NextUInt(new Random(i));
            uint[] flashDestination = CreateGuardedPixels(flashSize * flashSize, GuardPixelB, GuardPixelA);
            const float impactFlash = 0.75f;
            fixed (uint* sourceBase = flashSource)
            fixed (uint* destinationBase = flashDestination)
            {
                FastDeformation.GenerateShakeFlashFrame(
                    sourceBase + 1, destinationBase + 1, flashSize, flashSize, 0f, 4f, impactFlash);
            }
            int flashNumerator = (int)(impactFlash * BackendConfig.Math.ColorMaxByte);
            for (int i = 0; i < flashSize * flashSize; i++)
            {
                uint pixel = flashSource[i + 1];
                uint alpha = (pixel >> 24) & 0xFFu;
                uint expectedPixel;
                if (alpha == 0u)
                {
                    // Alpha-0 pixels skip the flash entirely: verbatim pass-through.
                    expectedPixel = pixel;
                }
                else
                {
                    uint red = pixel & 0xFFu;
                    uint green = (pixel >> 8) & 0xFFu;
                    uint blue = (pixel >> 16) & 0xFFu;
                    red += (uint)((255 - red) * (uint)flashNumerator / 255u);
                    green += (uint)((255 - green) * (uint)flashNumerator / 255u);
                    blue += (uint)((255 - blue) * (uint)flashNumerator / 255u);
                    expectedPixel = red | (green << 8) | (blue << 16) | (alpha << 24);
                }
                AssertEqual(expectedPixel, flashDestination[i + 1]);
                AssertEqual(alpha, (flashDestination[i + 1] >> 24) & 0xFFu); // alpha preserved
            }

            // Zero amplitudes and zero flash: verbatim copy at any time.
            uint[] plainSource = CreateGuardedPixels(9, GuardPixelA, GuardPixelB);
            for (int i = 0; i < 9; i++) plainSource[i + 1] = NextUInt(new Random(77 + i));
            uint[] plainDestination = CreateGuardedPixels(9, GuardPixelB, GuardPixelA);
            fixed (uint* sourceBase = plainSource)
            fixed (uint* destinationBase = plainDestination)
            {
                FastDeformation.GenerateShakeFlashFrame(
                    sourceBase + 1, destinationBase + 1, 3, 3, 0.37f, 0f, 0f);
            }
            for (int i = 0; i < 9; i++) AssertEqual(plainSource[i + 1], plainDestination[i + 1]);
        }

        // ------------------------------------------------------------------
        // reference implementations + throw-helpers
        // ------------------------------------------------------------------

        private static uint[] AnimationTimingReferenceBobFrame(
            uint[] source, int sourceOffset, int width, int height,
            float timeT, float bobAmp, float breatheAmp)
        {
            uint[] destination = new uint[width * height];
            float rad = AnimationTimingWrapRadians(timeT * BackendConfig.Math.TwoPI - BackendConfig.Math.PI);
            float wave = rad * (BackendConfig.Math.TaylorSinA -
                BackendConfig.Math.TaylorSinB * System.Math.Abs(rad));
            float bobOffset = wave * bobAmp;
            float scaleY = BackendConfig.Math.NormalizedMax + wave * breatheAmp;
            int anchorY = height - 1;
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                int srcY = anchorY + (int)((y - anchorY + bobOffset) / scaleY);
                for (int x = 0; x < width; x++, index++)
                {
                    destination[index] = (uint)srcY < (uint)height
                        ? source[sourceOffset + srcY * width + x]
                        : BackendConfig.Deformation.TransparentPixel;
                }
            }
            return destination;
        }

        private static uint[] AnimationTimingReferenceShakeFlashFrame(
            uint[] source, int sourceOffset, int width, int height,
            float timeT, float shakeAmp, float flashStrength)
        {
            uint[] destination = new uint[width * height];
            float decay = BackendConfig.Math.NormalizedMax - timeT;
            float phase = AnimationTimingWrapRadians(
                timeT * BackendConfig.Deformation.ShakeOscillations * BackendConfig.Math.TwoPI -
                BackendConfig.Math.PI);
            float wave = phase * (BackendConfig.Math.TaylorSinA -
                BackendConfig.Math.TaylorSinB * System.Math.Abs(phase));
            int shakeOffset = (int)(wave * shakeAmp * decay);
            int flashNumerator = (int)(flashStrength * decay * BackendConfig.Math.ColorMaxByte);
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++, index++)
                {
                    int srcX = x - shakeOffset;
                    uint pixel = (uint)srcX < (uint)width
                        ? source[sourceOffset + y * width + srcX]
                        : BackendConfig.Deformation.TransparentPixel;
                    uint alpha = (pixel >> 24) & 0xFFu;
                    if (flashNumerator > 0 && alpha != 0u)
                    {
                        uint red = pixel & 0xFFu;
                        uint green = (pixel >> 8) & 0xFFu;
                        uint blue = (pixel >> 16) & 0xFFu;
                        red += (255u - red) * (uint)flashNumerator / 255u;
                        green += (255u - green) * (uint)flashNumerator / 255u;
                        blue += (255u - blue) * (uint)flashNumerator / 255u;
                        pixel = red | (green << 8) | (blue << 16) | (alpha << 24);
                    }
                    destination[index] = pixel;
                }
            }
            return destination;
        }

        private static float AnimationTimingWrapRadians(float x)
        {
            x %= BackendConfig.Math.TwoPI;
            if (x > BackendConfig.Math.PI) return x - BackendConfig.Math.TwoPI;
            if (x < -BackendConfig.Math.PI) return x + BackendConfig.Math.TwoPI;
            return x;
        }

        private static unsafe void AnimationTimingBobNullCase(bool nullSource)
        {
            uint pixel = 0;
            if (nullSource) FastDeformation.GenerateBobBreatheFrame(null, &pixel, 1, 1, 0f, 0f, 0f);
            else FastDeformation.GenerateBobBreatheFrame(&pixel, null, 1, 1, 0f, 0f, 0f);
        }

        private static unsafe void AnimationTimingBobWithDims(int width, int height)
        {
            uint pixel = 0;
            FastDeformation.GenerateBobBreatheFrame(&pixel, &pixel, width, height, 0f, 0f, 0f);
        }

        private static unsafe void AnimationTimingBobWith(float timeT, float bobAmp, float breatheAmp)
        {
            uint source = 0;
            uint destination = 0;
            FastDeformation.GenerateBobBreatheFrame(&source, &destination, 1, 1, timeT, bobAmp, breatheAmp);
        }

        private static unsafe void AnimationTimingShakeNullCase(bool nullSource)
        {
            uint pixel = 0;
            if (nullSource) FastDeformation.GenerateShakeFlashFrame(null, &pixel, 1, 1, 0f, 0f, 0f);
            else FastDeformation.GenerateShakeFlashFrame(&pixel, null, 1, 1, 0f, 0f, 0f);
        }

        private static unsafe void AnimationTimingShakeWithDims(int width, int height)
        {
            uint pixel = 0;
            FastDeformation.GenerateShakeFlashFrame(&pixel, &pixel, width, height, 0f, 0f, 0f);
        }

        private static unsafe void AnimationTimingShakeWith(float timeT, float shakeAmp, float flashStrength)
        {
            uint source = 0;
            uint destination = 0;
            FastDeformation.GenerateShakeFlashFrame(&source, &destination, 1, 1, timeT, shakeAmp, flashStrength);
        }
    }
}
