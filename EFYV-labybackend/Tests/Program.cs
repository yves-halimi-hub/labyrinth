using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Export;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Memory;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        private static int assertions;

        private static void Main()
        {
            var tests = new (string Name, Action Body)[]
            {
                ("deterministic hash and safe schemas", TestHashAndSchemas),
                ("pool and swap-list invariants", TestCollections),
                ("safe file path policy", TestSafePathPolicy),
                ("periodic trig and bounded random", TestMathAndRandom),
                ("clipped drawing, fill, scale, and blend", TestPixelAlgorithms),
                ("box blur destination contract", TestBoxBlur),
                ("PNG, JSON metadata, path, and atlas export", TestExporter),
                ("binary save truncation reset", TestSaveEngine),
                ("grid and viewport validation", TestGridAndViewport)
                ,("schema block memory and model wrappers", TestSchemaMemoryAndModels)
                ,("randomized pool, registry, and swap-list models", TestRandomizedCollections)
                ,("randomized grid and ring-buffer models", TestRandomizedGridAndRingBuffer)
                ,("math reference calculations and edge cases", TestMathReferenceAndEdges)
                ,("random generator deterministic and range contracts", TestRandomContracts)
                ,("unsafe memory primitives and guard regions", TestUnsafeMemoryGuards)
                ,("drawing and flood-fill reference models", TestDrawingReferenceModels)
                ,("scaling, stamping, and alpha reference models", TestPixelReferenceModels)
                ,("blur reference model and guard regions", TestBlurReferenceModel)
                ,("procedural and deformation reference behavior", TestProceduralAndDeformation)
                ,("importer and save adversarial files", TestImporterAndSaveAdversarial)
                ,("export validation, atomicity, and atlas guards", TestExportAdversarial)
                ,("physics reference calculations", TestPhysicsReference)
            };

            int failures = 0;
            for (int i = 0; i < tests.Length; i++)
            {
                try
                {
                    tests[i].Body();
                    Console.WriteLine("PASS " + tests[i].Name);
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + tests[i].Name + ": " + exception);
                }
            }

            Console.WriteLine(assertions + " assertions across " + tests.Length + " test groups.");
            if (failures != 0) Environment.Exit(1);
        }

        private static void TestHashAndSchemas()
        {
            const string value = "EFYV-Labyrinth";
            const int expectedHash = -1366890085;
            AssertEqual(expectedHash, FastMath.FastHash(value));
            AssertEqual(expectedHash, FastMath.FastHash(value));
            AssertEqual(BackendConfig.Serialization.NullHash, FastMath.FastHash(null));
            AssertEqual(BackendConfig.Serialization.NullHash, FastMath.FastHash(string.Empty));

            PlayerData player = default;
            player.ActiveToonId = value;
            AssertEqual(expectedHash, player.Block.GetInt((int)PlayerSchema.ActiveToonIdHash));

            PlayerMetaSchema profile = PlayerMetaSchema.Default();
            FastSchemaBlock toon = default;
            toon.SetInt((int)ToonSchema.Level, 37);
            Assert(profile.TrySetToonBlock(3, in toon));
            Assert(profile.TryGetToonBlock(3, out FastSchemaBlock copied));
            AssertEqual(37, copied.GetInt((int)ToonSchema.Level));
            toon.SetInt((int)ToonSchema.Level, 99);
            AssertEqual(37, copied.GetInt((int)ToonSchema.Level));
            Assert(!profile.TryGetToonBlock(-1, out _));
            Assert(!profile.TryGetToonBlock(PlayerMetaSchema.MaxToons, out _));
            Assert(!profile.TrySetToonBlock(PlayerMetaSchema.MaxToons, in toon));

            EntityData entity = default;
            entity.ActiveListIndex = BackendConfig.Collections.UnregisteredListIndex;
            entity.Phase2HealthThreshold = 50f;
            AssertEqual(BackendConfig.Collections.UnregisteredListIndex, entity.ActiveListIndex);
            AssertEqual(50f, entity.Phase2HealthThreshold);
        }

        private static void TestCollections()
        {
            int factoryCalls = 0;
            FastPool<object> pool = new FastPool<object>(3, () =>
            {
                factoryCalls++;
                return new object();
            });
            AssertEqual(0, factoryCalls);
            AssertEqual(3, pool.Capacity);
            AssertEqual(0, pool.CreatedCount);
            AssertEqual(0, pool.AvailableCount);

            object first = pool.Rent();
            AssertEqual(1, factoryCalls);
            pool.Return(first);
            AssertEqual(1, pool.AvailableCount);
            AssertThrows<InvalidOperationException>(() => pool.Return(first));
            AssertThrows<ArgumentException>(() => pool.Return(new object()));

            Assert(ReferenceEquals(first, pool.Rent()));
            pool.Prewarm(3);
            AssertEqual(3, pool.CreatedCount);
            AssertEqual(2, pool.AvailableCount);
            AssertEqual(3, factoryCalls);
            pool.Prewarm(3);
            AssertEqual(3, factoryCalls);
            pool.Return(first);
            AssertEqual(3, pool.AvailableCount);

            object a = pool.Rent();
            object b = pool.Rent();
            object c = pool.Rent();
            Assert(a != null && b != null && c != null);
            Assert(!ReferenceEquals(a, b) && !ReferenceEquals(a, c) && !ReferenceEquals(b, c));
            AssertEqual<object>(null, pool.Rent());
            pool.Return(a);
            AssertThrows<InvalidOperationException>(() => pool.Return(a));

            FastPool<AlwaysEqual> identityPool = new FastPool<AlwaysEqual>(2, () => new AlwaysEqual());
            identityPool.Prewarm(2);
            AlwaysEqual identityA = identityPool.Rent();
            AlwaysEqual identityB = identityPool.Rent();
            Assert(!ReferenceEquals(identityA, identityB));

            FastPoolRegistry<object>.Clear();
            FastPoolRegistry<object>.RegisterPool(4, 2, () => new object());
            Assert(FastPoolRegistry<object>.Prewarm(4, 2));
            Assert(!FastPoolRegistry<object>.Prewarm(404, 1));
            FastPoolRegistry<object>.Clear();

            FastSwapList<Trackable> list = new FastSwapList<Trackable>(4);
            Trackable one = new Trackable();
            Trackable two = new Trackable();
            Trackable three = new Trackable();
            Trackable four = new Trackable();
            list.Add(one);
            list.Add(two);
            list.Add(three);
            list.Add(four);
            AssertThrows<InvalidOperationException>(() => list.Add(two));
            Assert(!(list.Items is List<Trackable>));
            AssertThrows<NotSupportedException>(() => ((IList<Trackable>)list.Items).Add(new Trackable()));

            list.Remove(one);
            AssertEqual(3, list.Count);
            Assert(ReferenceEquals(four, list[0]));
            AssertEqual(0, four.ActiveListIndex);
            AssertEqual(-1, one.ActiveListIndex);

            list.Remove(two);
            AssertEqual(2, list.Count);
            Assert(ReferenceEquals(three, list[1]));
            AssertEqual(1, three.ActiveListIndex);

            list.Remove(three);
            AssertEqual(1, list.Count);
            list.Remove(three);
            AssertEqual(1, list.Count);
            list.Clear();
            AssertEqual(0, list.Count);
            AssertEqual(-1, four.ActiveListIndex);
        }

        private static void TestMathAndRandom()
        {
            const float tolerance = 0.001f;
            float pi = BackendConfig.Math.PI;
            float twoPi = BackendConfig.Math.TwoPI;
            AssertNear(0f, FastMath.FastSinTaylor(0f), tolerance);
            AssertNear(0f, FastMath.FastSinTaylor(pi), tolerance);
            AssertNear(0f, FastMath.FastSinTaylor(twoPi), tolerance);
            AssertNear(0f, FastMath.FastSinTaylor(twoPi * 2f), tolerance);
            AssertNear(1f, FastMath.FastCosTaylor(0f), tolerance);
            AssertNear(-1f, FastMath.FastCosTaylor(pi), tolerance);
            AssertNear(1f, FastMath.FastCosTaylor(twoPi), tolerance);
            AssertNear(1f, FastMath.FastCosTaylor(twoPi * 2f), tolerance);
            AssertNear(FastMath.FastSinTaylor(0.73f), FastMath.FastSinTaylor(0.73f + twoPi * 4f), tolerance);
            AssertNear(FastMath.FastCosTaylor(-0.42f), FastMath.FastCosTaylor(-0.42f - twoPi * 4f), tolerance);
            FastMath.FastSinCosTaylor(0.73f + twoPi * 4f, out float pairedSin, out float pairedCos);
            AssertNear(FastMath.FastSinTaylor(0.73f), pairedSin, tolerance);
            AssertNear(FastMath.FastCosTaylor(0.73f), pairedCos, tolerance);
            AssertEqual(int.MinValue, FastMath.FastMin(int.MinValue, int.MaxValue));
            AssertEqual(int.MaxValue, FastMath.FastMax(int.MinValue, int.MaxValue));
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.GetCircleDistributionAngleRad(0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.FastWrap(1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastMath.GetRandomOffset2D(-1f, out _, out _));

            AssertNormalized(1f, 0f);
            AssertNormalized(3f, 4f);
            AssertNormalized(-12f, 5f);
            float zeroX = 0f;
            float zeroY = 0f;
            FastMath.FastNormalize(ref zeroX, ref zeroY);
            AssertEqual(0f, zeroX);
            AssertEqual(0f, zeroY);

            FastRandom.SetSeed(123456u);
            const int sampleCount = 50000;
            double sumX = 0d;
            double sumY = 0d;
            double sumRadiusSquared = 0d;
            for (int i = 0; i < sampleCount; i++)
            {
                FastRandom.InsideUnitCircle(out float x, out float y);
                float radiusSquared = x * x + y * y;
                Assert(radiusSquared > 0f && radiusSquared <= 1f);
                sumX += x;
                sumY += y;
                sumRadiusSquared += radiusSquared;
            }
            Assert(System.Math.Abs(sumX / sampleCount) < 0.02d);
            Assert(System.Math.Abs(sumY / sampleCount) < 0.02d);
            double meanRadiusSquared = sumRadiusSquared / sampleCount;
            Assert(meanRadiusSquared > 0.47d && meanRadiusSquared < 0.53d);
        }

        private static void TestSafePathPolicy()
        {
            Assert(SafePathPolicy.IsSafeFileStem("Hero"));
            Assert(SafePathPolicy.IsSafeFileStem("Hero.v2"));
            Assert(!SafePathPolicy.IsSafeFileStem(null));
            Assert(!SafePathPolicy.IsSafeFileStem(string.Empty));
            Assert(!SafePathPolicy.IsSafeFileStem("   "));
            Assert(!SafePathPolicy.IsSafeFileStem("."));
            Assert(!SafePathPolicy.IsSafeFileStem(".."));
            Assert(!SafePathPolicy.IsSafeFileStem("Hero."));
            Assert(!SafePathPolicy.IsSafeFileStem("Hero "));
            Assert(!SafePathPolicy.IsSafeFileStem("CON"));
            Assert(!SafePathPolicy.IsSafeFileStem("nul.txt"));
            Assert(!SafePathPolicy.IsSafeFileStem("COM1"));
            Assert(!SafePathPolicy.IsSafeFileStem("lpt9.data"));
            Assert(SafePathPolicy.IsSafeFileStem("COM10"));

            string root = Path.Combine(Path.GetTempPath(), "EFYVPath-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string expected = Path.GetFullPath(Path.Combine(root, "Hero.png"));
                AssertEqual(expected, SafePathPolicy.GetContainedPath(root + Path.DirectorySeparatorChar, "Hero.png"));
                AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(root, "../escape.png"));
                AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(root, Path.Combine("nested", "file.png")));
                AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(" ", "file.png"));
                AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(root, " "));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void AssertNormalized(float x, float y)
        {
            FastMath.FastNormalize(ref x, ref y);
            float length = (float)System.Math.Sqrt(x * x + y * y);
            AssertNear(1f, length, 0.002f);
        }

        private static unsafe void TestPixelAlgorithms()
        {
            uint[] guarded = new uint[11];
            guarded[0] = 0xDEADBEEFu;
            guarded[10] = 0xCAFEBABEu;
            fixed (uint* basePointer = guarded)
            {
                uint* canvas = basePointer + 1;
                Algorithms.DrawLineBresenham(canvas, 3, 3, -2, 1, 4, 1, 0xFFFFFFFFu);
                AssertEqual(0xFFFFFFFFu, canvas[3]);
                AssertEqual(0xFFFFFFFFu, canvas[4]);
                AssertEqual(0xFFFFFFFFu, canvas[5]);
                Algorithms.DrawThickLineBresenham(canvas, 3, 3, -1, 0, -1, 2, 0xAABBCCDDu, 3, Algorithms.BrushShape.Square);
                AssertEqual(0xAABBCCDDu, canvas[0]);
                AssertEqual(0xAABBCCDDu, canvas[3]);
                AssertEqual(0xAABBCCDDu, canvas[6]);
                bool invalidBrushRejected = false;
                try
                {
                    Algorithms.DrawThickLineBresenham(canvas, 3, 3, 0, 0, 1, 1, 1u, 0, Algorithms.BrushShape.Circle);
                }
                catch (ArgumentOutOfRangeException)
                {
                    invalidBrushRejected = true;
                }
                Assert(invalidBrushRejected);
            }
            AssertEqual(0xDEADBEEFu, guarded[0]);
            AssertEqual(0xCAFEBABEu, guarded[10]);

            uint[] fill = new uint[64 * 64];
            for (int i = 0; i < fill.Length; i++) fill[i] = 7u;
            fixed (uint* fillPointer = fill)
            {
                Algorithms.FloodFill(fillPointer, 64, 64, 32, 32, 9u);
            }
            for (int i = 0; i < fill.Length; i++) AssertEqual(9u, fill[i]);

            uint[] source = { 1u, 2u, 3u, 4u };
            uint[] destination = new uint[16];
            for (int i = 0; i < destination.Length; i++) destination[i] = 99u;
            fixed (uint* sourcePointer = source)
            fixed (uint* destinationPointer = destination)
            {
                FastMemory.ScaleBlitNearestNeighbor(sourcePointer, 2, 2, destinationPointer, 4, 4, 1f, 1, 1);
                bool invalidScaleRejected = false;
                try
                {
                    FastMemory.ScaleBlitNearestNeighbor(sourcePointer, 2, 2, destinationPointer, 4, 4, float.NaN, 0, 0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    invalidScaleRejected = true;
                }
                Assert(invalidScaleRejected);
            }
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    uint expected = x >= 1 && x <= 2 && y >= 1 && y <= 2
                        ? source[(y - 1) * 2 + x - 1]
                        : 0u;
                    AssertEqual(expected, destination[y * 4 + x]);
                }
            }

            uint color = 0u;
            FastMemory.BlendColor(ref color, 0x800000FFu);
            AssertEqual(0x800000FFu, color);
            color = 0xFFFF0000u;
            FastMemory.BlendColor(ref color, 0x800000FFu);
            AssertEqual(0xFF7F0080u, color);
            color = 0u;
            FastMemory.BlendColor(ref color, 0x800000FFu);
            FastMemory.BlendColor(ref color, 0x8000FF00u);
            AssertEqual(0xC000AA55u, color);

            uint[] layerDestination = { 0u };
            uint[] layerSource = { 0xFF0000FFu };
            fixed (uint* layerDestinationPointer = layerDestination)
            fixed (uint* layerSourcePointer = layerSource)
            {
                FastMemory.BlendLayer(layerDestinationPointer, layerSourcePointer, 1, 0, 0.5f);
                bool invalidOpacityRejected = false;
                try
                {
                    FastMemory.BlendLayer(layerDestinationPointer, layerSourcePointer, 1, 0, float.NaN);
                }
                catch (ArgumentOutOfRangeException)
                {
                    invalidOpacityRejected = true;
                }
                Assert(invalidOpacityRejected);
            }
            AssertEqual(0x800000FFu, layerDestination[0]);
        }

        private static unsafe void TestBoxBlur()
        {
            uint[] source = { 1u, 2u, 3u, 4u };
            uint[] destination = new uint[source.Length];
            fixed (uint* sourcePointer = source)
            fixed (uint* destinationPointer = destination)
            {
                FastEffects.BoxBlur(sourcePointer, destinationPointer, 2, 2, 0);
            }
            AssertSequenceEqual(source, destination);

            uint[] solidSource = new uint[16];
            uint[] solidDestination = new uint[16];
            for (int i = 0; i < solidSource.Length; i++) solidSource[i] = 0xFFFFFFFFu;
            fixed (uint* sourcePointer = solidSource)
            fixed (uint* destinationPointer = solidDestination)
            {
                FastEffects.BoxBlur(sourcePointer, destinationPointer, 4, 4, 1);
            }
            for (int i = 0; i < solidDestination.Length; i++) AssertEqual(0xFFFFFFFFu, solidDestination[i]);
            for (int i = 0; i < solidSource.Length; i++) AssertEqual(0xFFFFFFFFu, solidSource[i]);

            uint[] impulseSource = new uint[9];
            uint[] impulseDestination = new uint[9];
            uint[] scratch = new uint[9];
            impulseSource[4] = 0xFFFFFFFFu;
            fixed (uint* sourcePointer = impulseSource)
            fixed (uint* destinationPointer = impulseDestination)
            fixed (uint* scratchPointer = scratch)
            {
                FastEffects.BoxBlur(sourcePointer, destinationPointer, scratchPointer, 3, 3, 1);
            }
            for (int i = 0; i < impulseDestination.Length; i++) AssertEqual(0x1C1C1C1Cu, impulseDestination[i]);
            AssertEqual(0xFFFFFFFFu, impulseSource[4]);
            AssertEqual(0u, impulseSource[0]);

            uint[] deformationSource = { 1u, 2u, 3u, 4u };
            uint[] deformationDestination = new uint[4];
            fixed (uint* sourcePointer = deformationSource)
            fixed (uint* destinationPointer = deformationDestination)
            {
                FastDeformation.GenerateWalkFrame(sourcePointer, destinationPointer, 2, 2, 0.5f, 2, 1f, 1f);
            }
            AssertEqual(4, deformationDestination.Length);
        }

        private static void TestExporter()
        {
            string directory = Path.Combine(Path.GetTempPath(), "EFYVBackend-" + Guid.NewGuid().ToString("N"));
            try
            {
                JsonElement objectElement = JsonDocument.Parse("{\"nested\":7}").RootElement.Clone();
                Dictionary<string, object> properties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = "AtlasHero",
                    ["bool"] = true,
                    ["byte"] = (byte)2,
                    ["sbyte"] = (sbyte)-3,
                    ["short"] = (short)-4,
                    ["ushort"] = (ushort)5,
                    ["int"] = -6,
                    ["uint"] = 7u,
                    ["long"] = -8L,
                    ["ulong"] = ulong.MaxValue,
                    ["float"] = 1.25f,
                    ["double"] = 2.5d,
                    ["decimal"] = 3.75m,
                    ["null"] = null,
                    ["element"] = objectElement,
                    ["enum"] = SampleEnum.Second,
                    ["array"] = new[] { 4, 5, 6 }
                };
                List<HitboxJson> hitboxes = new List<HitboxJson>
                {
                    new HitboxJson { frameIndex = 1, hitboxType = "Hurtbox", x = 2f, y = 3f, width = 4f, height = 5f }
                };
                PackedRgba[] pixels =
                {
                    new PackedRgba(0xFF0000FFu),
                    new PackedRgba(0xFF00FF00u),
                    new PackedRgba(0xFFFF0000u),
                    new PackedRgba(0x00FFFFFFu)
                };
                AtlasMetadataJson metadata = new AtlasMetadataJson
                {
                    formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                    frameWidth = 1,
                    frameHeight = 1,
                    atlasWidth = 2,
                    atlasHeight = 2,
                    animations = new List<AnimationMetadataJson>
                    {
                        new AnimationMetadataJson { name = "Idle", fps = 8, startFrame = 0, frameCount = 2 },
                        new AnimationMetadataJson { name = "Run", fps = 12, startFrame = 2, frameCount = 2 }
                    }
                };

                FastExporter.PushToUnityLiveHook(
                    directory + Path.DirectorySeparatorChar,
                    "LivingEntityData",
                    properties,
                    hitboxes,
                    pixels,
                    2,
                    2,
                    metadata);

                string pngPath = Path.Combine(directory, "AtlasHero" + BackendConfig.Exporter.PngExtension);
                string jsonPath = Path.Combine(directory, "AtlasHero" + BackendConfig.Exporter.EfyvExtension);
                Assert(File.Exists(pngPath));
                Assert(File.Exists(jsonPath));
                AssertEqual(2, Directory.GetFiles(directory).Length);

                ParsedPng parsed = ParseAndValidatePng(pngPath);
                AssertEqual(2, parsed.Width);
                AssertEqual(2, parsed.Height);
                byte[] expectedRaw =
                {
                    0, 255, 0, 0, 255, 0, 255, 0, 255,
                    0, 0, 0, 255, 255, 255, 255, 255, 0
                };
                AssertSequenceEqual(expectedRaw, parsed.RawPixels);

                EFYVJsonFormat imported = FastImporter.ParseEfyvFile(jsonPath);
                AssertEqual("LivingEntityData", imported.assetType);
                Assert(imported.atlas.HasValue);
                AssertEqual(BackendConfig.Exporter.CurrentFormatVersion, imported.atlas.Value.formatVersion);
                AssertEqual(2, imported.atlas.Value.animations.Count);
                AssertEqual("Idle", imported.atlas.Value.animations[0].name);
                AssertEqual("Run", imported.atlas.Value.animations[1].name);
                AssertEqual(1, imported.hitboxes.Count);

                using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(jsonPath)))
                {
                    JsonElement serializedProperties = document.RootElement.GetProperty(BackendConfig.Exporter.FieldProperties);
                    AssertEqual(JsonValueKind.True, serializedProperties.GetProperty("bool").ValueKind);
                    AssertEqual(-3, serializedProperties.GetProperty("sbyte").GetInt32());
                    AssertEqual(ulong.MaxValue, serializedProperties.GetProperty("ulong").GetUInt64());
                    AssertEqual(3.75m, serializedProperties.GetProperty("decimal").GetDecimal());
                    AssertEqual(JsonValueKind.Null, serializedProperties.GetProperty("null").ValueKind);
                    AssertEqual(7, serializedProperties.GetProperty("element").GetProperty("nested").GetInt32());
                    AssertEqual((int)SampleEnum.Second, serializedProperties.GetProperty("enum").GetInt32());
                    AssertEqual(3, serializedProperties.GetProperty("array").GetArrayLength());
                }

                pixels[0] = new PackedRgba(0xFF112233u);
                FastExporter.PushToUnityLiveHook(directory, "LivingEntityData", properties, hitboxes, pixels, 2, 2, metadata);
                ParsedPng overwritten = ParseAndValidatePng(pngPath);
                AssertEqual((byte)0x33, overwritten.RawPixels[1]);
                AssertEqual((byte)0x22, overwritten.RawPixels[2]);
                AssertEqual((byte)0x11, overwritten.RawPixels[3]);
                AssertEqual(2, Directory.GetFiles(directory).Length);

                string assetNameDirectory = Path.Combine(directory, "asset-name-fallback");
                Dictionary<string, object> assetNameProperties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldAssetName] = "DesignerAsset"
                };
                FastExporter.PushToUnityLiveHook(
                    assetNameDirectory,
                    "GameAssetData",
                    assetNameProperties,
                    new List<HitboxJson>(),
                    new[] { new PackedRgba(0xFFFFFFFFu) },
                    1,
                    1);
                string assetNameJsonPath = Path.Combine(assetNameDirectory, "DesignerAsset" + BackendConfig.Exporter.EfyvExtension);
                Assert(File.Exists(assetNameJsonPath));
                Assert(!FastImporter.ParseEfyvFile(assetNameJsonPath).atlas.HasValue);

                const int widePixelCount = 17000;
                PackedRgba[] widePixels = new PackedRgba[widePixelCount];
                for (int i = 0; i < widePixels.Length; i++)
                {
                    widePixels[i] = new PackedRgba(
                        0xFF5A0000u | (uint)(i & 0xFF) | ((uint)((i >> 8) & 0xFF) << 8));
                }
                string wideDirectory = Path.Combine(directory, "wide-atlas");
                Dictionary<string, object> wideProperties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = "WideAtlas"
                };
                FastExporter.PushToUnityLiveHook(
                    wideDirectory,
                    "GameAssetData",
                    wideProperties,
                    new List<HitboxJson>(),
                    widePixels,
                    widePixelCount,
                    1);
                ParsedPng widePng = ParseAndValidatePng(
                    Path.Combine(wideDirectory, "WideAtlas" + BackendConfig.Exporter.PngExtension));
                AssertEqual(1 + widePixelCount * 4, widePng.RawPixels.Length);
                AssertEqual((byte)0, widePng.RawPixels[0]);
                AssertWidePixel(widePng.RawPixels, 0);
                AssertWidePixel(widePng.RawPixels, 16383);
                AssertWidePixel(widePng.RawPixels, 16384);
                AssertWidePixel(widePng.RawPixels, widePixelCount - 1);

                EFYVJsonFormat legacy = JsonSerializer.Deserialize<EFYVJsonFormat>(
                    "{\"assetType\":\"Legacy\",\"properties\":{},\"hitboxes\":[]}");
                Assert(!legacy.atlas.HasValue);

                Dictionary<string, object> invalidName = new Dictionary<string, object>(properties);
                invalidName[BackendConfig.Exporter.FieldEntityName] = "../escape";
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(directory, "Type", invalidName, hitboxes, pixels, 2, 2));
                invalidName[BackendConfig.Exporter.FieldEntityName] = "CON";
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(directory, "Type", invalidName, hitboxes, pixels, 2, 2));
                invalidName[BackendConfig.Exporter.FieldEntityName] = "";
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(directory, "Type", invalidName, hitboxes, pixels, 2, 2));
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(directory, "Type", properties, hitboxes, new PackedRgba[3], 2, 2));
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(directory, "Type", properties, hitboxes, new byte[4], 2, 2));

                PackedRgba[] destinationAtlas = new PackedRgba[8];
                PackedRgba[] frame = { new PackedRgba(1u), new PackedRgba(2u), new PackedRgba(3u), new PackedRgba(4u) };
                FastExporter.PackFramesToAtlas(destinationAtlas, 4, frame, 2, 2, 2, 0);
                AssertEqual(1u, destinationAtlas[2].Rgba);
                AssertEqual(2u, destinationAtlas[3].Rgba);
                AssertEqual(3u, destinationAtlas[6].Rgba);
                AssertEqual(4u, destinationAtlas[7].Rgba);
                AssertThrows<ArgumentException>(() => FastExporter.PackFramesToAtlas(destinationAtlas, 4, frame, 2, 2, 3, 0));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void TestSaveEngine()
        {
            string path = Path.Combine(Path.GetTempPath(), "EFYVSave-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                PlayerMetaSchema profile = PlayerMetaSchema.Default();
                profile.TotalCoinsCollected = 42;
                FastSaveEngine.SaveGame(path, ref profile);
                Assert(FastSaveEngine.LoadGame(path, out PlayerMetaSchema loaded));
                AssertEqual(42, loaded.TotalCoinsCollected);

                File.WriteAllBytes(path, new byte[] { 0x7F, 0x7F, 0x7F, 0x7F, 1, 2, 3, 4 });
                Assert(!FastSaveEngine.LoadGame(path, out PlayerMetaSchema truncated));
                AssertEqual(BackendConfig.Schema.InitialTotalCoins, truncated.TotalCoinsCollected);
                AssertEqual(
                    BackendConfig.Schema.DefaultStatMultiplier,
                    truncated.LegacyStats.GetFloat((int)StatSchema.MaxHealth));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void TestGridAndViewport()
        {
            AssertThrows<ArgumentOutOfRangeException>(() => new FastGridMap(0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => new FastRingBufferViewport(1, 0));
            FastGridMap map = new FastGridMap(8, 4);
            map.SetTile(3, 2, 17);
            AssertEqual((short)17, map.GetTile(3, 2));
            AssertEqual(BackendConfig.Collections.EmptyTileId, map.GetTile(-1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => map.GetVisibleBounds(0, 0, 1, 1, 0, 0, out _, out _, out _, out _));

            short[] cellular = new short[9];
            short[] cellularBuffer = new short[9];
            FastProceduralGen.SmoothCellularAutomata(cellular, cellularBuffer, 3, 3, 1, 0);
            AssertThrows<ArgumentException>(() => FastProceduralGen.SmoothCellularAutomata(cellular, new short[8], 3, 3, 1, 0));

            FastRingBufferViewport viewport = new FastRingBufferViewport(8, 4);
            viewport.GetRingBufferIndex(-1, -1, out int x, out int y);
            AssertEqual(7, x);
            AssertEqual(3, y);
            Assert(viewport.HasViewportShifted(0, 0));
            viewport.UpdatePreviousBounds(0, 0);
            Assert(!viewport.HasViewportShifted(0, 0));
        }

        private static ParsedPng ParseAndValidatePng(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Assert(bytes.Length > signature.Length);
            for (int i = 0; i < signature.Length; i++) AssertEqual(signature[i], bytes[i]);

            int offset = signature.Length;
            int width = 0;
            int height = 0;
            List<string> chunkNames = new List<string>();
            List<byte> idat = new List<byte>();
            while (offset < bytes.Length)
            {
                uint lengthValue = ReadUInt32BigEndian(bytes, offset);
                Assert(lengthValue <= int.MaxValue);
                int length = (int)lengthValue;
                offset += 4;
                Assert(offset + 4 + length + 4 <= bytes.Length);
                string name = Encoding.ASCII.GetString(bytes, offset, 4);
                byte[] typeAndData = new byte[4 + length];
                Buffer.BlockCopy(bytes, offset, typeAndData, 0, typeAndData.Length);
                offset += 4;
                byte[] data = new byte[length];
                Buffer.BlockCopy(bytes, offset, data, 0, length);
                offset += length;
                uint storedCrc = ReadUInt32BigEndian(bytes, offset);
                offset += 4;
                AssertEqual(ComputeStandardPngCrc(typeAndData), storedCrc);
                chunkNames.Add(name);

                if (name == "IHDR")
                {
                    AssertEqual(13, data.Length);
                    width = checked((int)ReadUInt32BigEndian(data, 0));
                    height = checked((int)ReadUInt32BigEndian(data, 4));
                    AssertEqual((byte)8, data[8]);
                    AssertEqual((byte)6, data[9]);
                    AssertEqual((byte)0, data[10]);
                    AssertEqual((byte)0, data[11]);
                    AssertEqual((byte)0, data[12]);
                }
                else if (name == "IDAT")
                {
                    idat.AddRange(data);
                }
                else if (name == "IEND")
                {
                    AssertEqual(0, data.Length);
                }
            }
            AssertEqual(bytes.Length, offset);
            AssertEqual(3, chunkNames.Count);
            AssertEqual("IHDR", chunkNames[0]);
            AssertEqual("IDAT", chunkNames[1]);
            AssertEqual("IEND", chunkNames[2]);

            using (MemoryStream compressed = new MemoryStream(idat.ToArray()))
            using (ZLibStream inflater = new ZLibStream(compressed, CompressionMode.Decompress))
            using (MemoryStream raw = new MemoryStream())
            {
                inflater.CopyTo(raw);
                return new ParsedPng(width, height, raw.ToArray());
            }
        }

        private static uint ComputeStandardPngCrc(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < bytes.Length; i++)
            {
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1u) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static void AssertWidePixel(byte[] rawPixels, int pixelIndex)
        {
            int offset = 1 + pixelIndex * 4;
            AssertEqual((byte)(pixelIndex & 0xFF), rawPixels[offset]);
            AssertEqual((byte)((pixelIndex >> 8) & 0xFF), rawPixels[offset + 1]);
            AssertEqual((byte)0x5A, rawPixels[offset + 2]);
            AssertEqual((byte)0xFF, rawPixels[offset + 3]);
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) |
                ((uint)bytes[offset + 1] << 16) |
                ((uint)bytes[offset + 2] << 8) |
                bytes[offset + 3];
        }

        private static void Assert(bool condition)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Assertion failed.");
        }

        private static void AssertEqual<T>(T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }

        private static void AssertNear(float expected, float actual, float tolerance)
        {
            assertions++;
            if (float.IsNaN(actual) || System.Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException("Expected " + expected + " +/- " + tolerance + ", got " + actual + ".");
        }

        private static void AssertSequenceEqual<T>(T[] expected, T[] actual)
        {
            AssertEqual(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], actual[i]);
        }

        private static void AssertThrows<TException>(Action action) where TException : Exception
        {
            assertions++;
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
        }

        private sealed class Trackable : IFastListTrackable
        {
            public int ActiveListIndex { get; set; } = BackendConfig.Collections.UnregisteredListIndex;
        }

        private sealed class AlwaysEqual
        {
            public override bool Equals(object obj) => obj is AlwaysEqual;

            public override int GetHashCode() => 1;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PackedRgba
        {
            public uint Rgba;

            public PackedRgba(uint rgba)
            {
                Rgba = rgba;
            }
        }

        private enum SampleEnum
        {
            First,
            Second
        }

        private sealed class ParsedPng
        {
            public int Width { get; }
            public int Height { get; }
            public byte[] RawPixels { get; }

            public ParsedPng(int width, int height, byte[] rawPixels)
            {
                Width = width;
                Height = height;
                RawPixels = rawPixels;
            }
        }
    }
}
