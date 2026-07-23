using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Export;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Memory;
using EFYVBackend.Core.Models;
using EFYV.Runtime.Media;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        // These vectors were captured from the pre-consolidation implementation at
        // repository commit 765af8cb3815dc0cca55ef85f3e020759db7aeda. They are the
        // compatibility gate for the shared media/artifact extraction.
        private const string GoldenSourceCommit = "765af8cb3815dc0cca55ef85f3e020759db7aeda";
        private static void TestCompatibilityGoldenVectors()
        {
            var values = CaptureCompatibilityVectors();
            if (Environment.GetEnvironmentVariable("EFYV_CAPTURE_GOLDEN") == "1")
            {
                foreach (KeyValuePair<string, string> item in values)
                    Console.WriteLine("GOLDEN " + item.Key + "=" + item.Value);
                return;
            }

            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source_commit"] = GoldenSourceCommit,
                ["crc32"] = "CBF43926",
                ["rng_xorshift32_v1"] = "F89B3E70,75FB4A9A,89A89D0E,DB2B114A,9943B4AB,1502CB40,C13743D5,00F31913,19F8579B,726CFDDE,573E2D95,C3145EB3",
                ["rgba_blend_v1"] = "010000FF,FF509820,923D313E,01020304,FFFEFEFE",
                ["schema_layout_v1"] = "256,16900,0,4,260,516,64",
                ["schema_bytes_v1_sha256"] = "4914FD829003D6294FA8CE3D6CE7FC37B11ABC190B8F56B3880EA6078E6E85F2",
                ["save_file_v1_sha256"] = "16C1512A3490AC4DCC17AAC134582B43AD5346AD3FE7799F99803F693A17126A",
                ["artifact_json_v5_sha256"] = "7F256AA7BFAA419AFE89D49574AEBC155EB0AD0E821CC7376CA3D4CE0B3CFC5A",
                ["artifact_png_v1_sha256"] = "DF134C666412FF60678DAD26011355CC437A9096D6CC468E833681C282100C51",
            };
            foreach (KeyValuePair<string, string> item in expected)
                AssertEqual(item.Value, values[item.Key]);
        }

        private static unsafe void TestRuntimeKernelParity()
        {
            var random = new Random(0x5EED5);
            var source = new uint[1027];
            var initial = new uint[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                source[index] = (uint)random.NextInt64(0, 1L << 32);
                initial[index] = (uint)random.NextInt64(0, 1L << 32);
            }
            source[0] = 0u;
            source[1] = 0x01030201u;
            source[2] = 0x800000FFu;
            source[3] = 0xFFABCDEFu;
            initial[0] = 0x01020304u;
            initial[1] = 0xFF112233u;
            initial[2] = 0xFFFF0000u;
            initial[3] = 0u;

            bool nativeAvailable = RuntimeMediaKernel.TryEnableNativeV1();
            if (Environment.GetEnvironmentVariable("EFYV_REQUIRE_NATIVE_KERNEL") == "1") Assert(nativeAvailable);
            foreach (byte opacity in new byte[] { 0, 1, 73, 173, 254, 255 })
            foreach (byte threshold in new byte[] { 0, 1, 127, 254, 255 })
            {
                uint[] managed = (uint[])initial.Clone();
                RuntimeMediaKernel.UseManagedFallback();
                fixed (uint* destination = managed)
                fixed (uint* input = source)
                    RuntimeMediaKernel.BlendRgbaBatch(destination, input, managed.Length, opacity, threshold);
                if (!nativeAvailable) continue;
                Assert(RuntimeMediaKernel.TryEnableNativeV1());
                uint[] native = (uint[])initial.Clone();
                fixed (uint* destination = native)
                fixed (uint* input = source)
                    RuntimeMediaKernel.BlendRgbaBatch(destination, input, native.Length, opacity, threshold);
                AssertSequenceEqual(managed, native);
            }

            byte[] crcInput = new byte[8193];
            random.NextBytes(crcInput);
            RuntimeMediaKernel.UseManagedFallback();
            uint managedCrc = RuntimeMediaKernel.UpdateCrc32(0xFFFFFFFFu, crcInput.AsSpan(0, 4097));
            managedCrc = RuntimeMediaKernel.UpdateCrc32(managedCrc, crcInput.AsSpan(4097));
            if (nativeAvailable)
            {
                Assert(RuntimeMediaKernel.TryEnableNativeV1());
                uint nativeCrc = RuntimeMediaKernel.UpdateCrc32(0xFFFFFFFFu, crcInput.AsSpan(0, 4097));
                nativeCrc = RuntimeMediaKernel.UpdateCrc32(nativeCrc, crcInput.AsSpan(4097));
                AssertEqual(managedCrc, nativeCrc);
            }

            TestRuntimeKernelOverlapParity(source, nativeAvailable, destinationOffset: 1, sourceOffset: 0);
            TestRuntimeKernelOverlapParity(source, nativeAvailable, destinationOffset: 0, sourceOffset: 1);
            RuntimeMediaKernel.UseManagedFallback();
            AssertEqual(RuntimeMediaKernelMode.Managed, RuntimeMediaKernel.Mode);
        }

        private static unsafe void TestRuntimeKernelOverlapParity(uint[] values, bool nativeAvailable, int destinationOffset, int sourceOffset)
        {
            const int count = 257;
            uint[] managed = values.Take(count + 1).ToArray();
            RuntimeMediaKernel.UseManagedFallback();
            fixed (uint* data = managed)
                RuntimeMediaKernel.BlendRgbaBatch(data + destinationOffset, data + sourceOffset, count, 173, 3);
            if (!nativeAvailable) return;
            Assert(RuntimeMediaKernel.TryEnableNativeV1());
            uint[] native = values.Take(count + 1).ToArray();
            fixed (uint* data = native)
                RuntimeMediaKernel.BlendRgbaBatch(data + destinationOffset, data + sourceOffset, count, 173, 3);
            AssertSequenceEqual(managed, native);
        }

        private static unsafe Dictionary<string, string> CaptureCompatibilityVectors()
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source_commit"] = GoldenSourceCommit,
                ["crc32"] = FastCrc32.Compute(Encoding.ASCII.GetBytes("123456789")).ToString("X8", CultureInfo.InvariantCulture),
            };

            FastRandomState random = new FastRandomState(0xC0FFEEu);
            var draws = new uint[12];
            for (int i = 0; i < draws.Length; i++) draws[i] = random.NextUInt();
            values["rng_xorshift32_v1"] = string.Join(",", draws.Select(value => value.ToString("X8", CultureInfo.InvariantCulture)));

            uint[] destinations = { 0u, 0xFF203040u, 0x80402010u, 0x01020304u, 0xFFFFFFFFu };
            uint[] sources = { 0x010000FFu, 0x8080FF00u, 0x7F3366CCu, 0x00FFFFFFu, 0x80000000u };
            byte[] opacities = { 128, 255, 73, 255, 1 };
            for (int i = 0; i < destinations.Length; i++)
                FastMemory.BlendColor(ref destinations[i], sources[i], opacities[i]);
            values["rgba_blend_v1"] = string.Join(",", destinations.Select(value => value.ToString("X8", CultureInfo.InvariantCulture)));

            values["schema_layout_v1"] = string.Join(",", new[]
            {
                sizeof(FastSchemaBlock),
                sizeof(PlayerMetaSchema),
                (int)Marshal.OffsetOf<PlayerMetaSchema>(nameof(PlayerMetaSchema.TotalCoinsCollected)),
                (int)Marshal.OffsetOf<PlayerMetaSchema>(nameof(PlayerMetaSchema.LegacyStats)),
                (int)Marshal.OffsetOf<PlayerMetaSchema>(nameof(PlayerMetaSchema.LegacyAchievements)),
                (int)Marshal.OffsetOf<PlayerMetaSchema>(nameof(PlayerMetaSchema.ToonBlocks)),
                PlayerMetaSchema.MaxToons,
            });

            PlayerMetaSchema profile = PlayerMetaSchema.Default();
            profile.LegacyStats.SetInt((int)StatSchema.MaxHealth, 0x10203040);
            profile.LegacyAchievements.SetInt(0, unchecked((int)0x89ABCDEFu));
            FastSchemaBlock toon = default;
            toon.SetInt((int)ToonSchema.ToonIdHash, unchecked((int)0xA1B2C3D4u));
            profile.TrySetToonBlock(PlayerMetaSchema.MaxToons - 1, in toon);
            byte[] schemaBytes = new ReadOnlySpan<byte>(&profile, sizeof(PlayerMetaSchema)).ToArray();
            values["schema_bytes_v1_sha256"] = Convert.ToHexString(SHA256.HashData(schemaBytes));

            string root = Path.Combine(Path.GetTempPath(), "efyv-golden-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string savePath = Path.Combine(root, "profile.efyvsave");
                FastSaveEngine.SaveGame(savePath, ref profile);
                values["save_file_v1_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(savePath)));

                var properties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [BackendConfig.Exporter.FieldEntityName] = "golden-hero",
                    ["displayName"] = "Golden Hero",
                    ["level"] = 7,
                    ["enabled"] = true,
                };
                var hitboxes = new List<HitboxJson>
                {
                    new HitboxJson { frameIndex = 0, hitboxType = "hurt", x = 0.25f, y = -0.5f, width = 1.5f, height = 2.25f },
                };
                var metadata = new AtlasMetadataJson
                {
                    formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                    frameWidth = 1,
                    frameHeight = 1,
                    atlasWidth = 2,
                    atlasHeight = 1,
                    animations = new List<AnimationMetadataJson>
                    {
                        new AnimationMetadataJson
                        {
                            name = "Idle",
                            fps = 12,
                            startFrame = 0,
                            frameCount = 2,
                            frameDurationsMs = new List<int> { 83, 84 },
                            loopStart = 0,
                            loopEnd = 1,
                            pingPong = false,
                            effects = new List<EffectDescriptorJson>
                            {
                                new EffectDescriptorJson { name = "flash", effectType = BackendConfig.Exporter.EffectTypeFlash, trigger = "frame", colorRgba = 0xAABBCCDDu, durationMs = 25, strength = 0.5f },
                            },
                        },
                    },
                };
                var attachments = new List<AttachmentJson>
                {
                    new AttachmentJson { frameIndex = 0, subElement = "golden-sword", x = 1, y = -2, zOrder = 3, flipX = true },
                };
                var tileset = new TilesetManifestJson { tileSize = 1, tiles = new List<string> { "floor", "wall" } };
                var pixels = new[] { new GoldenPackedRgba(0x04030201u), new GoldenPackedRgba(0xA0B0C0D0u) };
                FastExporter.PushToUnityLiveHook(root, "HeroData", properties, hitboxes, pixels, 2, 1, metadata, null, attachments, tileset);
                values["artifact_json_v5_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "golden-hero.efyvlaby"))));
                values["artifact_png_v1_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "golden-hero.png"))));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            return values;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly struct GoldenPackedRgba
        {
            private readonly uint value;
            public GoldenPackedRgba(uint value) { this.value = value; }
        }
    }
}
