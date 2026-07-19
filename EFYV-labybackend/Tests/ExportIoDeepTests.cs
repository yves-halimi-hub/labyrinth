using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Export;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        // ------------------------------------------------------------------
        // PNG encoder: full byte-level reference model for the stored path.
        // Reimplements the PNG container (signature/IHDR/IDAT/IEND with CRCs)
        // plus the zlib stored-block stream and Adler-32 naively, and compares
        // the stored-mode encoder output byte for byte, including stored-block
        // boundaries. The compressed default is verified structurally: it must
        // decode back to the exact input pixels via FastPngDecoder.
        // ------------------------------------------------------------------
        private static void TestExportIoPngReferenceBytes()
        {
            (int Width, int Height)[] fixedSizes =
            {
                (1, 1), (1, 3), (3, 1), (2, 2), (5, 7),
                (64, 22),   // raw 5654 bytes: crosses the 5552-byte deferred Adler modulo window
                (64, 255),  // raw 65535 bytes: exactly one full final stored block
                (64, 256),  // raw 65792 bytes: full non-final block + 257-byte final block
                (16384, 1)  // raw 65537 bytes: full non-final block + 2-byte final block
            };
            Random random = new Random(0x0E10C0DE);
            for (int i = 0; i < fixedSizes.Length; i++)
            {
                ExportIoAssertPngMatchesReference(fixedSizes[i].Width, fixedSizes[i].Height, random);
            }
            for (int iteration = 0; iteration < 25; iteration++)
            {
                ExportIoAssertPngMatchesReference(random.Next(1, 41), random.Next(1, 41), random);
            }

            // Flat pixel-art-like content must actually shrink under the compressed
            // default relative to the stored representation.
            uint[] flat = new uint[128 * 128];
            for (int i = 0; i < flat.Length; i++) flat[i] = 0xFF3366CCu;
            byte[] flatCompressed = ExportIoEncodePng(flat, 128, 128);
            byte[] flatStored = ExportIoEncodePng(flat, 128, 128, false);
            Assert(flatCompressed.Length < flatStored.Length / 10);
        }

        private static void ExportIoAssertPngMatchesReference(int width, int height, Random random)
        {
            uint[] pixels = new uint[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (uint)random.Next(1 << 16) | ((uint)random.Next(1 << 16) << 16);
            }
            byte[] expected = ExportIoReferencePng(pixels, width, height);
            byte[] stored = ExportIoEncodePng(pixels, width, height, false);
            AssertSequenceEqual(expected, stored);

            byte[] compressed = ExportIoEncodePng(pixels, width, height);
            uint[] decoded = FastPngDecoder.Read(compressed, out int decodedWidth, out int decodedHeight);
            AssertEqual(width, decodedWidth);
            AssertEqual(height, decodedHeight);
            AssertSequenceEqual(pixels, decoded);
        }

        private static byte[] ExportIoEncodePng<T>(T[] pixels, int width, int height) where T : unmanaged
        {
            using (MemoryStream stream = new MemoryStream())
            {
                FastPngEncoder.Write(stream, pixels, width, height);
                return stream.ToArray();
            }
        }

        private static byte[] ExportIoEncodePng<T>(T[] pixels, int width, int height, bool compressed) where T : unmanaged
        {
            using (MemoryStream stream = new MemoryStream())
            {
                FastPngEncoder.Write(stream, pixels, width, height, compressed);
                return stream.ToArray();
            }
        }

        private static byte[] ExportIoReferencePng(uint[] pixels, int width, int height)
        {
            byte[] raw = new byte[checked((width * 4 + 1) * height)];
            int rawIndex = 0;
            int pixelIndex = 0;
            for (int y = 0; y < height; y++)
            {
                raw[rawIndex++] = 0;
                for (int x = 0; x < width; x++)
                {
                    uint value = pixels[pixelIndex++];
                    raw[rawIndex++] = (byte)value;
                    raw[rawIndex++] = (byte)(value >> 8);
                    raw[rawIndex++] = (byte)(value >> 16);
                    raw[rawIndex++] = (byte)(value >> 24);
                }
            }

            List<byte> idat = new List<byte>(raw.Length + 64);
            idat.Add(0x78);
            idat.Add(0x01);
            int offset = 0;
            while (offset < raw.Length)
            {
                int blockLength = System.Math.Min(65535, raw.Length - offset);
                bool final = offset + blockLength == raw.Length;
                idat.Add(final ? (byte)1 : (byte)0);
                idat.Add((byte)blockLength);
                idat.Add((byte)(blockLength >> 8));
                ushort inverse = (ushort)~blockLength;
                idat.Add((byte)inverse);
                idat.Add((byte)(inverse >> 8));
                for (int i = 0; i < blockLength; i++) idat.Add(raw[offset + i]);
                offset += blockLength;
            }
            uint adlerA = 1;
            uint adlerB = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                adlerA = (adlerA + raw[i]) % 65521u;
                adlerB = (adlerB + adlerA) % 65521u;
            }
            uint adler = (adlerB << 16) | adlerA;
            idat.Add((byte)(adler >> 24));
            idat.Add((byte)(adler >> 16));
            idat.Add((byte)(adler >> 8));
            idat.Add((byte)adler);

            List<byte> file = new List<byte>(idat.Count + 64);
            file.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            byte[] ihdr = new byte[13];
            ExportIoWriteUInt32BE(ihdr, 0, (uint)width);
            ExportIoWriteUInt32BE(ihdr, 4, (uint)height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            ExportIoAppendChunk(file, "IHDR", ihdr);
            ExportIoAppendChunk(file, "IDAT", idat.ToArray());
            ExportIoAppendChunk(file, "IEND", Array.Empty<byte>());
            return file.ToArray();
        }

        private static void ExportIoAppendChunk(List<byte> file, string type, byte[] data)
        {
            byte[] lengthBytes = new byte[4];
            ExportIoWriteUInt32BE(lengthBytes, 0, (uint)data.Length);
            file.AddRange(lengthBytes);
            byte[] typeAndData = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) typeAndData[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, typeAndData, 4, data.Length);
            file.AddRange(typeAndData);
            byte[] crcBytes = new byte[4];
            ExportIoWriteUInt32BE(crcBytes, 0, ComputeStandardPngCrc(typeAndData));
            file.AddRange(crcBytes);
        }

        private static void ExportIoWriteUInt32BE(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }

        // ------------------------------------------------------------------
        // PNG encoder: any 4-byte unmanaged element type must produce the exact
        // same bytes in both encoding modes, and the encoder must work on a
        // forward-only stream that rejects Seek/Position/Length.
        // ------------------------------------------------------------------
        private static void TestExportIoPngGenericPixelsAndStreams()
        {
            Random random = new Random(0x1E4E71C5);
            const int width = 19;
            const int height = 11;
            uint[] asUInt = new uint[width * height];
            for (int i = 0; i < asUInt.Length; i++)
            {
                asUInt[i] = (uint)random.Next(1 << 16) | ((uint)random.Next(1 << 16) << 16);
            }
            PackedRgba[] asPacked = new PackedRgba[asUInt.Length];
            float[] asFloat = new float[asUInt.Length];
            ExportIoTwoUShorts[] asPairs = new ExportIoTwoUShorts[asUInt.Length];
            int[] asInt = new int[asUInt.Length];
            for (int i = 0; i < asUInt.Length; i++)
            {
                asPacked[i] = new PackedRgba(asUInt[i]);
                asFloat[i] = BitConverter.UInt32BitsToSingle(asUInt[i]);
                asPairs[i] = new ExportIoTwoUShorts { Low = (ushort)asUInt[i], High = (ushort)(asUInt[i] >> 16) };
                asInt[i] = unchecked((int)asUInt[i]);
            }

            byte[] baseline = ExportIoEncodePng(asUInt, width, height);
            uint[] decodedBaseline = FastPngDecoder.Read(baseline, out int decodedWidth, out int decodedHeight);
            AssertEqual(width, decodedWidth);
            AssertEqual(height, decodedHeight);
            AssertSequenceEqual(asUInt, decodedBaseline);
            AssertSequenceEqual(baseline, ExportIoEncodePng(asPacked, width, height));
            AssertSequenceEqual(baseline, ExportIoEncodePng(asFloat, width, height));
            AssertSequenceEqual(baseline, ExportIoEncodePng(asPairs, width, height));
            AssertSequenceEqual(baseline, ExportIoEncodePng(asInt, width, height));

            byte[] storedBaseline = ExportIoEncodePng(asUInt, width, height, false);
            AssertSequenceEqual(ExportIoReferencePng(asUInt, width, height), storedBaseline);
            AssertSequenceEqual(storedBaseline, ExportIoEncodePng(asPacked, width, height, false));
            AssertSequenceEqual(storedBaseline, ExportIoEncodePng(asFloat, width, height, false));
            AssertSequenceEqual(storedBaseline, ExportIoEncodePng(asPairs, width, height, false));
            AssertSequenceEqual(storedBaseline, ExportIoEncodePng(asInt, width, height, false));

            using (ExportIoForwardOnlyStream forward = new ExportIoForwardOnlyStream())
            {
                FastPngEncoder.Write(forward, asUInt, width, height);
                AssertSequenceEqual(baseline, forward.ToArray());
            }
            using (ExportIoForwardOnlyStream forward = new ExportIoForwardOnlyStream())
            {
                FastPngEncoder.Write(forward, asUInt, width, height, false);
                AssertSequenceEqual(storedBaseline, forward.ToArray());
            }
        }

        // ------------------------------------------------------------------
        // Exporter naming: entityName/assetName precedence including null values
        // stored under the keys, the fallback stem, hostile fallback types, and
        // culture-invariant name formatting.
        // ------------------------------------------------------------------
        private static void TestExportIoExporterNaming()
        {
            string root = Path.Combine(Path.GetTempPath(), "EFYVExportIoNames-" + Guid.NewGuid().ToString("N"));
            try
            {
                PackedRgba[] pixel = { new PackedRgba(0x11223344u) };
                List<HitboxJson> hitboxes = new List<HitboxJson>();

                string entityNullDir = Path.Combine(root, "entity-null");
                Dictionary<string, object> entityNull = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = null,
                    [BackendConfig.Exporter.FieldAssetName] = "AssetOnly"
                };
                FastExporter.PushToUnityLiveHook(entityNullDir, "TypeX", entityNull, hitboxes, pixel, 1, 1);
                Assert(File.Exists(Path.Combine(entityNullDir, "AssetOnly" + BackendConfig.Exporter.PngExtension)));
                Assert(File.Exists(Path.Combine(entityNullDir, "AssetOnly" + BackendConfig.Exporter.EfyvExtension)));
                Assert(!File.Exists(Path.Combine(
                    entityNullDir,
                    "TypeX" + BackendConfig.Exporter.ExportSuffix + BackendConfig.Exporter.PngExtension)));

                string bothDir = Path.Combine(root, "both-names");
                Dictionary<string, object> both = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldAssetName] = "AssetLoses",
                    [BackendConfig.Exporter.FieldEntityName] = "EntityWins"
                };
                FastExporter.PushToUnityLiveHook(bothDir, "TypeX", both, hitboxes, pixel, 1, 1);
                Assert(File.Exists(Path.Combine(bothDir, "EntityWins" + BackendConfig.Exporter.PngExtension)));
                Assert(!File.Exists(Path.Combine(bothDir, "AssetLoses" + BackendConfig.Exporter.PngExtension)));

                // No-identity exports are rejected (#36): a null-valued assetName
                // (with no entityName) publishes nothing - the old fallback minted
                // a type-suffixed stem here while the importer collapsed the same
                // file onto "UnknownEntity".
                string assetNullDir = Path.Combine(root, "asset-null");
                Dictionary<string, object> assetNull = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldAssetName] = null
                };
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(assetNullDir, "TypeY", assetNull, hitboxes, pixel, 1, 1));
                AssertNoExportFiles(assetNullDir);

                string hostileTypeDir = Path.Combine(root, "hostile-type");
                Dictionary<string, object> unnamed = new Dictionary<string, object>();
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(hostileTypeDir, "Bad/Type", unnamed, hitboxes, pixel, 1, 1));
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(hostileTypeDir, "Bad\\Type", unnamed, hitboxes, pixel, 1, 1));
                AssertNoExportFiles(hostileTypeDir);

                // A reserved-device asset TYPE no longer smuggles a fallback stem
                // through: without an identity property the export is rejected.
                string reservedTypeDir = Path.Combine(root, "reserved-type");
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(reservedTypeDir, "CON", unnamed, hitboxes, pixel, 1, 1));
                AssertNoExportFiles(reservedTypeDir);

                string cultureDir = Path.Combine(root, "culture");
                CultureInfo originalCulture = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                    Dictionary<string, object> numericName = new Dictionary<string, object>
                    {
                        [BackendConfig.Exporter.FieldEntityName] = 1.5d
                    };
                    FastExporter.PushToUnityLiveHook(cultureDir, "TypeZ", numericName, hitboxes, pixel, 1, 1);
                }
                finally
                {
                    CultureInfo.CurrentCulture = originalCulture;
                }
                Assert(File.Exists(Path.Combine(cultureDir, "1.5" + BackendConfig.Exporter.PngExtension)));
                Assert(!File.Exists(Path.Combine(cultureDir, "1,5" + BackendConfig.Exporter.PngExtension)));
                EFYVJsonFormat cultureImported = FastImporter.ParseEfyvFile(
                    Path.Combine(cultureDir, "1.5" + BackendConfig.Exporter.EfyvExtension));
                AssertEqual(1.5d, cultureImported.properties[BackendConfig.Exporter.FieldEntityName].GetDouble());
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        // ------------------------------------------------------------------
        // Exporter JSON fidelity: keys and values that need escaping, extreme
        // numeric values, and the generic serializer fallback branch, verified
        // through a full export -> import round trip.
        // ------------------------------------------------------------------
        private static void TestExportIoExporterJsonFidelity()
        {
            string root = Path.Combine(Path.GetTempPath(), "EFYVExportIoJson-" + Guid.NewGuid().ToString("N"));
            try
            {
                Guid guidValue = new Guid("8f3c9d2e-4b7a-4c1d-9e5f-a6b7c8d90123");
                Dictionary<string, object> properties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = "EscapeAsset",
                    ["quote\"key"] = "quote\"value",
                    ["back\\slash"] = "control" + (char)1 + "value",
                    ["new\nline\tkey"] = "line\r\nvalue",
                    ["שלום🚀"] = "☺ש",
                    [""] = "empty-key-value",
                    ["floatMax"] = float.MaxValue,
                    ["floatEpsilon"] = float.Epsilon,
                    ["doubleEpsilon"] = double.Epsilon,
                    ["longMin"] = long.MinValue,
                    ["charValue"] = 'ש',
                    ["guidValue"] = guidValue,
                    ["nestedDictionary"] = new Dictionary<string, object> { ["inner"] = 42 },
                    ["stringArray"] = new[] { "a\"b", "c\\d" }
                };
                List<HitboxJson> hitboxes = new List<HitboxJson>
                {
                    new HitboxJson { frameIndex = 3, hitboxType = "hit" + (char)7 + "box\"quoted\"", x = 1f, y = 2f, width = 3f, height = 4f }
                };
                FastExporter.PushToUnityLiveHook(
                    root, "Läby\"Type\nX", properties, hitboxes, new[] { new PackedRgba(0xFF00FF00u) }, 1, 1);

                string jsonPath = Path.Combine(root, "EscapeAsset" + BackendConfig.Exporter.EfyvExtension);
                EFYVJsonFormat imported = FastImporter.ParseEfyvFile(jsonPath);
                AssertEqual("Läby\"Type\nX", imported.assetType);
                AssertEqual(properties.Count, imported.properties.Count);
                AssertEqual("quote\"value", imported.properties["quote\"key"].GetString());
                AssertEqual("control" + (char)1 + "value", imported.properties["back\\slash"].GetString());
                AssertEqual("line\r\nvalue", imported.properties["new\nline\tkey"].GetString());
                AssertEqual("☺ש", imported.properties["שלום🚀"].GetString());
                AssertEqual("empty-key-value", imported.properties[""].GetString());
                AssertEqual(float.MaxValue, imported.properties["floatMax"].GetSingle());
                AssertEqual(float.Epsilon, imported.properties["floatEpsilon"].GetSingle());
                AssertEqual(double.Epsilon, imported.properties["doubleEpsilon"].GetDouble());
                AssertEqual(long.MinValue, imported.properties["longMin"].GetInt64());
                AssertEqual("ש", imported.properties["charValue"].GetString());
                AssertEqual(guidValue, Guid.Parse(imported.properties["guidValue"].GetString()));
                AssertEqual(42, imported.properties["nestedDictionary"].GetProperty("inner").GetInt32());
                AssertEqual(2, imported.properties["stringArray"].GetArrayLength());
                AssertEqual("a\"b", imported.properties["stringArray"][0].GetString());
                AssertEqual("c\\d", imported.properties["stringArray"][1].GetString());
                AssertEqual(1, imported.hitboxes.Count);
                AssertEqual("hit" + (char)7 + "box\"quoted\"", imported.hitboxes[0].hitboxType);
                AssertEqual(3, imported.hitboxes[0].frameIndex);

                // The published file is valid standalone UTF-8 JSON.
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(jsonPath)))
                {
                    AssertEqual(JsonValueKind.Object, document.RootElement.ValueKind);
                }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        // ------------------------------------------------------------------
        // Importer edge documents beyond the malformed corpus already covered:
        // empty atlas objects, duplicate keys, surrounding whitespace, UTF-16
        // input, numeric coercions, deep-but-legal nesting, and null structs.
        // ------------------------------------------------------------------
        private static void TestExportIoImporterEdgeDocuments()
        {
            string directory = Path.Combine(Path.GetTempPath(), "EFYVExportIoImport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "edge" + BackendConfig.Exporter.EfyvExtension);

                ExportIoWriteDoc(path, "{\"atlas\":{}}");
                EFYVJsonFormat emptyAtlas = FastImporter.ParseEfyvFile(path);
                Assert(emptyAtlas.atlas.HasValue);
                AssertEqual(0, emptyAtlas.atlas.Value.formatVersion);
                AssertEqual(0, emptyAtlas.atlas.Value.frameWidth);
                AssertEqual(0, emptyAtlas.atlas.Value.atlasHeight);
                AssertEqual(null, emptyAtlas.atlas.Value.animations);

                ExportIoWriteDoc(path, "{\"atlas\":{\"animations\":[]}}");
                AssertEqual(0, FastImporter.ParseEfyvFile(path).atlas.Value.animations.Count);

                // Documents current behavior: duplicate property keys keep the LAST value.
                ExportIoWriteDoc(path, "{\"properties\":{\"k\":1,\"k\":2}}");
                AssertEqual(2, FastImporter.ParseEfyvFile(path).properties["k"].GetInt32());

                ExportIoWriteDoc(path, "  \r\n\t {\"assetType\":\"ws\"} \r\n\t ");
                AssertEqual("ws", FastImporter.ParseEfyvFile(path).assetType);

                // UTF-16LE "{}" with BOM is not valid UTF-8 input.
                File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE, 0x7B, 0x00, 0x7D, 0x00 });
                AssertThrows<JsonException>(() => FastImporter.ParseEfyvFile(path));

                ExportIoWriteDoc(
                    path,
                    "{\"hitboxes\":[{\"frameIndex\":7,\"unknown\":[1,{\"a\":2}],\"x\":1e2,\"y\":-0.0,\"width\":2147483648}]}");
                EFYVJsonFormat coerced = FastImporter.ParseEfyvFile(path);
                AssertEqual(1, coerced.hitboxes.Count);
                AssertEqual(7, coerced.hitboxes[0].frameIndex);
                AssertEqual(null, coerced.hitboxes[0].hitboxType);
                AssertNear(100f, coerced.hitboxes[0].x, 0f);
                AssertNear(2147483648f, coerced.hitboxes[0].width, 0f);
                AssertNear(0f, coerced.hitboxes[0].height, 0f);

                StringBuilder deep = new StringBuilder("{\"properties\":{\"deep\":");
                for (int i = 0; i < 60; i++) deep.Append('[');
                deep.Append('0');
                for (int i = 0; i < 60; i++) deep.Append(']');
                deep.Append("}}");
                ExportIoWriteDoc(path, deep.ToString());
                AssertEqual(JsonValueKind.Array, FastImporter.ParseEfyvFile(path).properties["deep"].ValueKind);

                ExportIoWriteDoc(path, "{\"hitboxes\":[null]}");
                AssertThrows<JsonException>(() => FastImporter.ParseEfyvFile(path));

                ExportIoWriteDoc(path, "{\"assetType\":\"x\" /*comment*/}");
                AssertThrows<JsonException>(() => FastImporter.ParseEfyvFile(path));

                ExportIoWriteDoc(path, "{\"assetType\":\"x\",}");
                AssertThrows<JsonException>(() => FastImporter.ParseEfyvFile(path));

                ExportIoWriteDoc(path, "{\"assetType\":null}");
                EFYVJsonFormat nullAsset = FastImporter.ParseEfyvFile(path);
                AssertEqual(null, nullAsset.assetType);
                AssertEqual(null, nullAsset.properties);
                Assert(!nullAsset.atlas.HasValue);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void ExportIoWriteDoc(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------
        // Save format: pins the exact on-disk byte layout - the #19 versioned
        // envelope {magic, version, CRC32} followed by the raw PlayerMetaSchema
        // payload (size, field offsets, per-slot addressing) - so that any
        // accidental layout change breaks loudly instead of corrupting saves.
        // ------------------------------------------------------------------
        private static unsafe void TestExportIoSaveFormatLayout()
        {
            const int headerSize = 12;
            const int legacyStatsOffset = 4;
            const int legacyAchievementsOffset = 260;
            const int toonBlocksOffset = 516;
            const int expectedPayloadSize = 16900;
            const int expectedFileSize = headerSize + expectedPayloadSize;
            AssertEqual(
                expectedPayloadSize,
                sizeof(int) + 2 * BackendConfig.Schema.BlockSizeBytes + PlayerMetaSchema.MaxToons * BackendConfig.Schema.BlockSizeBytes);
            AssertEqual(expectedPayloadSize, sizeof(PlayerMetaSchema));
            AssertEqual(expectedPayloadSize, PlayerMetaSchema.ExpectedSizeBytes);
            AssertEqual(headerSize, BackendConfig.Save.HeaderSizeBytes);

            PlayerMetaSchema probe = default;
            byte* probeBase = (byte*)&probe;
            AssertEqual(0L, (byte*)&probe.TotalCoinsCollected - probeBase);
            AssertEqual((long)legacyStatsOffset, (byte*)&probe.LegacyStats - probeBase);
            AssertEqual((long)legacyAchievementsOffset, (byte*)&probe.LegacyAchievements - probeBase);
            AssertEqual((long)toonBlocksOffset, probe.ToonBlocks - probeBase);

            byte[] baseline = SaveStructBytes(PlayerMetaSchema.Default());
            AssertEqual(expectedPayloadSize, baseline.Length);

            PlayerMetaSchema coins = PlayerMetaSchema.Default();
            coins.TotalCoinsCollected = unchecked((int)0xDEADBEEF);
            ExportIoAssertBytesDiffOnlyAt(baseline, SaveStructBytes(coins), 0, BitConverter.GetBytes(unchecked((int)0xDEADBEEF)));

            PlayerMetaSchema statsLow = PlayerMetaSchema.Default();
            statsLow.LegacyStats.SetInt(0, 0x12345678);
            ExportIoAssertBytesDiffOnlyAt(baseline, SaveStructBytes(statsLow), legacyStatsOffset, BitConverter.GetBytes(0x12345678));

            PlayerMetaSchema statsHigh = PlayerMetaSchema.Default();
            statsHigh.LegacyStats.SetInt(63, 0x0BADF00D);
            ExportIoAssertBytesDiffOnlyAt(baseline, SaveStructBytes(statsHigh), legacyStatsOffset + 63 * 4, BitConverter.GetBytes(0x0BADF00D));

            PlayerMetaSchema achievementsLow = PlayerMetaSchema.Default();
            achievementsLow.LegacyAchievements.SetInt(0, -1);
            ExportIoAssertBytesDiffOnlyAt(baseline, SaveStructBytes(achievementsLow), legacyAchievementsOffset, BitConverter.GetBytes(-1));

            PlayerMetaSchema achievementsHigh = PlayerMetaSchema.Default();
            achievementsHigh.LegacyAchievements.SetInt(63, int.MaxValue);
            ExportIoAssertBytesDiffOnlyAt(baseline, SaveStructBytes(achievementsHigh), legacyAchievementsOffset + 63 * 4, BitConverter.GetBytes(int.MaxValue));

            ExportIoAssertToonSlotOffset(baseline, 0, 0, 0x11112222);
            ExportIoAssertToonSlotOffset(baseline, 63, 63, 0x33334444);
            ExportIoAssertToonSlotOffset(baseline, 7, 31, 0x55556666);
            Random random = new Random(0x5AFE10);
            for (int i = 0; i < 30; i++)
            {
                ExportIoAssertToonSlotOffset(
                    baseline,
                    random.Next(PlayerMetaSchema.MaxToons),
                    random.Next(FastSchemaBlock.MaxSize),
                    random.Next(1, int.MaxValue));
            }

            // The bytes on disk are the envelope header followed by exactly the
            // in-memory payload bytes.
            string directory = Path.Combine(Path.GetTempPath(), "EFYVExportIoLayout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string defaultPath = Path.Combine(directory, "default.save");
                PlayerMetaSchema defaultProfile = PlayerMetaSchema.Default();
                FastSaveEngine.SaveGame(defaultPath, ref defaultProfile);
                byte[] diskBaseline = File.ReadAllBytes(defaultPath);
                AssertSequenceEqual(ExportIoSaveEnvelopeBytes(baseline), diskBaseline);
                AssertEqual(1, Directory.GetFiles(directory).Length); // no temp leftovers

                // Header fields pinned little-endian at fixed offsets, with the
                // CRC verified against the independent bitwise reference model.
                AssertEqual(0x56594645u, BitConverter.ToUInt32(diskBaseline, 0)); // "EFYV"
                AssertEqual((byte)'E', diskBaseline[0]);
                AssertEqual((byte)'F', diskBaseline[1]);
                AssertEqual((byte)'Y', diskBaseline[2]);
                AssertEqual((byte)'V', diskBaseline[3]);
                AssertEqual(1u, BitConverter.ToUInt32(diskBaseline, 4));
                AssertEqual(ComputeStandardPngCrc(baseline), BitConverter.ToUInt32(diskBaseline, 8));

                string lastSlotPath = Path.Combine(directory, "last.save");
                PlayerMetaSchema lastProfile = PlayerMetaSchema.Default();
                FastSchemaBlock lastBlock = default;
                lastBlock.SetInt(63, unchecked((int)0xCAFED00D));
                Assert(lastProfile.TrySetToonBlock(63, in lastBlock));
                FastSaveEngine.SaveGame(lastSlotPath, ref lastProfile);
                AssertEqual((long)expectedFileSize, new FileInfo(lastSlotPath).Length);
                byte[] lastSlotBytes = File.ReadAllBytes(lastSlotPath);
                ExportIoAssertBytesDiffOnlyAt(
                    baseline,
                    ExportIoSavePayload(lastSlotBytes),
                    expectedPayloadSize - 4,
                    BitConverter.GetBytes(unchecked((int)0xCAFED00D)));
                AssertEqual(
                    ComputeStandardPngCrc(ExportIoSavePayload(lastSlotBytes)),
                    BitConverter.ToUInt32(lastSlotBytes, 8));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        // Reference model of the #19 save envelope: {magic, version, CRC32} in
        // little-endian followed by the raw payload bytes.
        private static byte[] ExportIoSaveEnvelopeBytes(byte[] payload)
        {
            byte[] file = new byte[BackendConfig.Save.HeaderSizeBytes + payload.Length];
            BitConverter.GetBytes(BackendConfig.Save.MagicNumber).CopyTo(file, BackendConfig.Save.MagicOffset);
            BitConverter.GetBytes((uint)BackendConfig.Save.FormatVersion).CopyTo(file, BackendConfig.Save.VersionOffset);
            BitConverter.GetBytes(ComputeStandardPngCrc(payload)).CopyTo(file, BackendConfig.Save.ChecksumOffset);
            payload.CopyTo(file, BackendConfig.Save.HeaderSizeBytes);
            return file;
        }

        private static byte[] ExportIoSavePayload(byte[] fileBytes)
        {
            byte[] payload = new byte[fileBytes.Length - BackendConfig.Save.HeaderSizeBytes];
            Buffer.BlockCopy(fileBytes, BackendConfig.Save.HeaderSizeBytes, payload, 0, payload.Length);
            return payload;
        }

        private static unsafe void ExportIoAssertToonSlotOffset(byte[] baseline, int toonIndex, int slot, int value)
        {
            PlayerMetaSchema profile = PlayerMetaSchema.Default();
            FastSchemaBlock block = default;
            block.SetInt(slot, value);
            Assert(profile.TrySetToonBlock(toonIndex, in block));
            int offset = 516 + toonIndex * BackendConfig.Schema.BlockSizeBytes + slot * 4;
            ExportIoAssertBytesDiffOnlyAt(baseline, SaveStructBytes(profile), offset, BitConverter.GetBytes(value));
        }

        private static void ExportIoAssertBytesDiffOnlyAt(byte[] baseline, byte[] modified, int start, byte[] expectedRegion)
        {
            AssertEqual(baseline.Length, modified.Length);
            for (int i = 0; i < baseline.Length; i++)
            {
                byte expected = i >= start && i < start + expectedRegion.Length
                    ? expectedRegion[i - start]
                    : baseline[i];
                AssertEqual(expected, modified[i]);
            }
        }

        // ------------------------------------------------------------------
        // Save engine: file-sharing semantics and error paths not covered by
        // the exclusive-lock adversarial tests.
        // ------------------------------------------------------------------
        private static unsafe void TestExportIoSaveSharingAndErrors()
        {
            string directory = Path.Combine(Path.GetTempPath(), "EFYVExportIoShare-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "share.save");
                PlayerMetaSchema profile = CreatePatternedProfile();
                byte[] payloadBaseline = SaveStructBytes(profile);
                FastSaveEngine.SaveGame(path, ref profile);
                byte[] fileBaseline = File.ReadAllBytes(path);
                AssertSequenceEqual(ExportIoSaveEnvelopeBytes(payloadBaseline), fileBaseline);

                using (FileStream reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // A concurrent reader that shares read access does not block loading...
                    Assert(FastSaveEngine.LoadGame(path, out PlayerMetaSchema concurrent));
                    AssertSequenceEqual(payloadBaseline, SaveStructBytes(concurrent));
                    // ...but does block the atomic swap (even after the bounded
                    // retry), and the live file must survive untouched.
                    AssertThrows<IOException>(() => FastSaveEngine.SaveGame(path, ref profile));
                }
                AssertSequenceEqual(fileBaseline, File.ReadAllBytes(path));
                AssertEqual(1, Directory.GetFiles(directory).Length); // failed publish left no temp file
                Assert(FastSaveEngine.LoadGame(path, out PlayerMetaSchema afterwards));
                AssertSequenceEqual(payloadBaseline, SaveStructBytes(afterwards));

                string missingDirectoryPath = Path.Combine(directory, "no-such-dir", "nested", "file.save");
                AssertThrows<IOException>(() => FastSaveEngine.SaveGame(missingDirectoryPath, ref profile));
                Assert(!Directory.Exists(Path.Combine(directory, "no-such-dir")));

                // Saving onto an existing directory path is an IOException from the
                // atomic move (#19 flipped the old in-place UnauthorizedAccessException),
                // and the directory must survive.
                AssertThrows<IOException>(() => FastSaveEngine.SaveGame(directory, ref profile));
                Assert(Directory.Exists(directory));

                Assert(!FastSaveEngine.LoadGame(string.Empty, out PlayerMetaSchema emptyPath));
                AssertSequenceEqual(SaveStructBytes(PlayerMetaSchema.Default()), SaveStructBytes(emptyPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        // ------------------------------------------------------------------
        // SafePathPolicy: reference model for the stem rules plus containment
        // behaviors of GetContainedPath that no other test touches.
        // ------------------------------------------------------------------
        private static void TestExportIoSafePathPolicyModel()
        {
            string[] safeStems =
            {
                "com0", "COM0", "lpt0", "con2", "CONX", "prn2", "a..b", "..hidden", ".gitignore",
                "x.CON", "auxiliary", "COM１", "ＣＯＮ", " leading", "a b", "a,b", "1.5", "NULL", "aux2",
                new string('x', 128), new string('x', 124) + ".dat"
            };
            for (int i = 0; i < safeStems.Length; i++)
            {
                Assert(SafePathPolicy.IsSafeFileStem(safeStems[i]));
                Assert(ExportIoReferenceSafeStem(safeStems[i]));
            }
            string[] unsafeStems =
            {
                "aux", "AuX.txt.gz", "com9.tar.gz", "lpt1", "LPT9.data", "prn", "nul.", "...", "a.", "a ",
                " ", "a\tb", "a|b", "a<b", "a>b", "a:b", "a*b", "a?b", "a\"b", "a" + (char)0 + "b", "dir/leaf", "dir\\leaf",
                new string('x', 129), new string('x', 125) + ".dat", "a" + (char)27 + "b"
            };
            for (int i = 0; i < unsafeStems.Length; i++)
            {
                Assert(!SafePathPolicy.IsSafeFileStem(unsafeStems[i]));
                Assert(!ExportIoReferenceSafeStem(unsafeStems[i]));
            }

            Random random = new Random(0x50AF37);
            char[] alphabet =
            {
                'a', 'Z', '0', '9', '.', ' ', '_', '-', '/', '\\', ':', '*', '\t',
                'C', 'O', 'N', 'M', 'L', 'P', 'T', '1', 'U', 'X', 'א'
            };
            string[] reservedSeeds = { "CON", "com", "LpT", "AUX", "NUL", "PRN", "COM4", "lpt9", "cOm0" };
            for (int iteration = 0; iteration < 4000; iteration++)
            {
                StringBuilder builder = new StringBuilder();
                if (random.Next(4) == 0) builder.Append(reservedSeeds[random.Next(reservedSeeds.Length)]);
                int length = random.Next(0, 11);
                for (int i = 0; i < length; i++) builder.Append(alphabet[random.Next(alphabet.Length)]);
                string stem = builder.ToString();
                AssertEqual(ExportIoReferenceSafeStem(stem), SafePathPolicy.IsSafeFileStem(stem));
            }

            // GetContainedPath is pure path arithmetic: nothing below is created on disk.
            char separator = Path.DirectorySeparatorChar;
            string root = Path.Combine(Path.GetTempPath(), "EFYVExportIoPolicy-" + Guid.NewGuid().ToString("N"));
            string canonical = Path.GetFullPath(Path.Combine(root, "art.png"));
            AssertEqual(canonical, SafePathPolicy.GetContainedPath(root, "art.png"));
            AssertEqual(canonical, SafePathPolicy.GetContainedPath(root + separator + separator, "art.png"));
            AssertEqual(
                canonical,
                SafePathPolicy.GetContainedPath(root.Replace(separator, Path.AltDirectorySeparatorChar), "art.png"));

            // Drive-root containment exercises the normalize-root early return.
            string driveRoot = Path.GetPathRoot(Path.GetTempPath());
            AssertEqual(
                Path.GetFullPath(Path.Combine(driveRoot, "EFYVExportIoDriveRoot.png")),
                SafePathPolicy.GetContainedPath(driveRoot, "EFYVExportIoDriveRoot.png"));

            AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(root, ".."));
            AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(root, "."));
            AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(root, ".." + separator + "out.png"));
            AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(root, Path.GetTempPath() + "outside.png"));
            AssertThrows<ArgumentException>(() => SafePathPolicy.GetContainedPath(root, "sub" + separator + "in.png"));

            // Documents current behavior: routes that RESOLVE back inside the root
            // are accepted (containment is judged on the resolved path).
            AssertEqual(canonical, SafePathPolicy.GetContainedPath(root, "sub" + separator + ".." + separator + "art.png"));
            AssertEqual(canonical, SafePathPolicy.GetContainedPath(root, canonical));
            if (separator == '\\')
            {
                // Windows-only: the containment comparison is case-insensitive.
                string reentry = ".." + separator + Path.GetFileName(root).ToUpperInvariant() + separator + "art.png";
                string reentryResult = SafePathPolicy.GetContainedPath(root, reentry);
                Assert(string.Equals(Path.GetDirectoryName(reentryResult), root, StringComparison.OrdinalIgnoreCase));
                AssertEqual("art.png", Path.GetFileName(reentryResult));
            }

            // Whatever Windows full-path normalization does to a trailing dot, the
            // result must stay directly inside the root.
            string trailingDot = SafePathPolicy.GetContainedPath(root, "trail.png.");
            AssertEqual(root, Path.GetDirectoryName(trailingDot));
            Assert(trailingDot.StartsWith(root + separator, StringComparison.Ordinal));
        }

        private static bool ExportIoReferenceSafeStem(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value == "." || value == "..") return false;
            if (value.Length > 128) return false;
            if (value[value.Length - 1] == '.' || value[value.Length - 1] == ' ') return false;
            if (!string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)) return false;
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (value.IndexOfAny(new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' }) >= 0) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < ' ') return false;
            }
            int dot = value.IndexOf('.');
            string baseName = (dot < 0 ? value : value.Substring(0, dot)).ToUpperInvariant();
            if (baseName == "CON" || baseName == "PRN" || baseName == "AUX" || baseName == "NUL") return false;
            if (baseName.Length == 4 &&
                (baseName.StartsWith("COM", StringComparison.Ordinal) || baseName.StartsWith("LPT", StringComparison.Ordinal)) &&
                baseName[3] >= '1' && baseName[3] <= '9')
            {
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // PackFramesToAtlas is element-size generic: the row stride math must
        // stay correct for 1-, 2-, and 8-byte elements, not only 4-byte pixels.
        // ------------------------------------------------------------------
        private static void TestExportIoAtlasPackGenericSizes()
        {
            ushort[] atlas16 = new ushort[5 * 4];
            for (int i = 0; i < atlas16.Length; i++) atlas16[i] = (ushort)(1000 + i);
            ushort[] frame16 = { 1, 2, 3, 4, 5, 6 };
            ushort[] expected16 = (ushort[])atlas16.Clone();
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 2; x++) expected16[(1 + y) * 5 + 3 + x] = frame16[y * 2 + x];
            }
            FastExporter.PackFramesToAtlas(atlas16, 5, frame16, 2, 3, 3, 1);
            AssertSequenceEqual(expected16, atlas16);

            byte[] atlas8 = new byte[7 * 3];
            for (int i = 0; i < atlas8.Length; i++) atlas8[i] = (byte)(200 + i);
            byte[] frame8 = { 9, 8, 7, 6, 5, 4 };
            byte[] expected8 = (byte[])atlas8.Clone();
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 3; x++) expected8[y * 7 + 2 + x] = frame8[y * 3 + x];
            }
            FastExporter.PackFramesToAtlas(atlas8, 7, frame8, 3, 2, 2, 0);
            AssertSequenceEqual(expected8, atlas8);

            ulong[] atlas64 = new ulong[3 * 3];
            for (int i = 0; i < atlas64.Length; i++) atlas64[i] = 0xAAAA000000000000ul + (ulong)i;
            ulong[] frame64 = { 0x1111ul, 0x2222ul };
            ulong[] expected64 = (ulong[])atlas64.Clone();
            expected64[1 * 3 + 2] = frame64[0];
            expected64[2 * 3 + 2] = frame64[1];
            FastExporter.PackFramesToAtlas(atlas64, 3, frame64, 1, 2, 2, 1);
            AssertSequenceEqual(expected64, atlas64);

            Random random = new Random(0x6E14C);
            for (int iteration = 0; iteration < 60; iteration++)
            {
                int atlasWidth = random.Next(1, 10);
                int atlasHeight = random.Next(1, 8);
                int frameWidth = random.Next(1, atlasWidth + 1);
                int frameHeight = random.Next(1, atlasHeight + 1);
                int destX = random.Next(0, atlasWidth - frameWidth + 1);
                int destY = random.Next(0, atlasHeight - frameHeight + 1);
                ushort[] atlas = new ushort[atlasWidth * atlasHeight];
                for (int i = 0; i < atlas.Length; i++) atlas[i] = (ushort)random.Next(65536);
                ushort[] frame = new ushort[frameWidth * frameHeight];
                for (int i = 0; i < frame.Length; i++) frame[i] = (ushort)random.Next(65536);
                ushort[] expected = (ushort[])atlas.Clone();
                for (int y = 0; y < frameHeight; y++)
                {
                    for (int x = 0; x < frameWidth; x++)
                    {
                        expected[(destY + y) * atlasWidth + destX + x] = frame[y * frameWidth + x];
                    }
                }
                FastExporter.PackFramesToAtlas(atlas, atlasWidth, frame, frameWidth, frameHeight, destX, destY);
                AssertSequenceEqual(expected, atlas);
            }
        }

        // ------------------------------------------------------------------
        // b1-backend-png agent additions below: PNG decoder round trip and
        // adversarial corpus, plus atlas grid layout and frame extraction.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // FastPngDecoder: encode-with-FastPngEncoder / decode-with-FastPngDecoder
        // property tests over randomized sizes and three content classes, both
        // encoder modes, both decoder entry points, and hand-filtered scanlines
        // covering all five PNG filter types.
        // ------------------------------------------------------------------
        private static void TestExportIoPngDecoderRoundTrip()
        {
            Random random = new Random(0x0DEC0DE5);
            for (int iteration = 0; iteration < 60; iteration++)
            {
                int width = random.Next(1, 49);
                int height = random.Next(1, 49);
                uint[] pixels = new uint[width * height];
                int contentClass = iteration % 3;
                uint[] palette =
                {
                    0x00000000u, 0xFF3366CCu, 0xFFCC6633u, 0x80FFFFFFu, 0xFF000000u
                };
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (contentClass == 0)
                    {
                        pixels[i] = (uint)random.Next(1 << 16) | ((uint)random.Next(1 << 16) << 16);
                    }
                    else if (contentClass == 1)
                    {
                        pixels[i] = palette[random.Next(palette.Length)];
                    }
                    else
                    {
                        int x = i % width;
                        int y = i / width;
                        pixels[i] = (uint)(x & 0xFF) |
                            ((uint)(y & 0xFF) << 8) |
                            ((uint)((x + y) & 0xFF) << 16) |
                            (0xFFu << 24);
                    }
                }

                byte[] compressed = ExportIoEncodePng(pixels, width, height);
                uint[] decodedCompressed = FastPngDecoder.Read(compressed, out int compressedWidth, out int compressedHeight);
                AssertEqual(width, compressedWidth);
                AssertEqual(height, compressedHeight);
                AssertSequenceEqual(pixels, decodedCompressed);

                byte[] stored = ExportIoEncodePng(pixels, width, height, false);
                uint[] decodedStored = FastPngDecoder.Read(stored, out int storedWidth, out int storedHeight);
                AssertEqual(width, storedWidth);
                AssertEqual(height, storedHeight);
                AssertSequenceEqual(pixels, decodedStored);

                using (MemoryStream stream = new MemoryStream(compressed))
                {
                    uint[] decodedFromStream = FastPngDecoder.Read(stream, out int streamWidth, out int streamHeight);
                    AssertEqual(width, streamWidth);
                    AssertEqual(height, streamHeight);
                    AssertSequenceEqual(pixels, decodedFromStream);
                }
            }

            // Foreign PNGs use per-row filters the encoder never emits; every filter
            // type must unfilter back to the same pixels.
            Random filterRandom = new Random(0x0F117E55);
            for (byte filterType = 0; filterType <= 4; filterType++)
            {
                const int width = 7;
                const int height = 5;
                uint[] pixels = new uint[width * height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = (uint)filterRandom.Next(1 << 16) | ((uint)filterRandom.Next(1 << 16) << 16);
                }
                byte[] png = ExportIoBuildFilteredPng(pixels, width, height, filterType);
                uint[] decoded = FastPngDecoder.Read(png, out int decodedWidth, out int decodedHeight);
                AssertEqual(width, decodedWidth);
                AssertEqual(height, decodedHeight);
                AssertSequenceEqual(pixels, decoded);
            }
        }

        private static byte[] ExportIoBuildFilteredPng(uint[] pixels, int width, int height, byte filterType)
        {
            int stride = width * 4;
            byte[] unfiltered = new byte[stride * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                unfiltered[i * 4] = (byte)pixels[i];
                unfiltered[i * 4 + 1] = (byte)(pixels[i] >> 8);
                unfiltered[i * 4 + 2] = (byte)(pixels[i] >> 16);
                unfiltered[i * 4 + 3] = (byte)(pixels[i] >> 24);
            }

            byte[] raw = new byte[(stride + 1) * height];
            for (int y = 0; y < height; y++)
            {
                raw[y * (stride + 1)] = filterType;
                for (int i = 0; i < stride; i++)
                {
                    int left = i >= 4 ? unfiltered[y * stride + i - 4] : 0;
                    int above = y > 0 ? unfiltered[(y - 1) * stride + i] : 0;
                    int aboveLeft = y > 0 && i >= 4 ? unfiltered[(y - 1) * stride + i - 4] : 0;
                    int predictor;
                    if (filterType == 0) predictor = 0;
                    else if (filterType == 1) predictor = left;
                    else if (filterType == 2) predictor = above;
                    else if (filterType == 3) predictor = (left + above) >> 1;
                    else predictor = ExportIoPaeth(left, above, aboveLeft);
                    raw[y * (stride + 1) + 1 + i] = (byte)(unfiltered[y * stride + i] - predictor);
                }
            }

            return ExportIoBuildPngFile(
                ("IHDR", ExportIoBuildIhdr((uint)width, (uint)height, 8, 6, 0, 0, 0)),
                ("IDAT", ExportIoZlibCompress(raw)),
                ("IEND", Array.Empty<byte>()));
        }

        private static int ExportIoPaeth(int left, int above, int aboveLeft)
        {
            int estimate = left + above - aboveLeft;
            int distanceLeft = System.Math.Abs(estimate - left);
            int distanceAbove = System.Math.Abs(estimate - above);
            int distanceAboveLeft = System.Math.Abs(estimate - aboveLeft);
            if (distanceLeft <= distanceAbove && distanceLeft <= distanceAboveLeft) return left;
            if (distanceAbove <= distanceAboveLeft) return above;
            return aboveLeft;
        }

        private static byte[] ExportIoBuildIhdr(
            uint width,
            uint height,
            byte bitDepth,
            byte colorType,
            byte compression,
            byte filterMethod,
            byte interlace)
        {
            byte[] ihdr = new byte[13];
            ExportIoWriteUInt32BE(ihdr, 0, width);
            ExportIoWriteUInt32BE(ihdr, 4, height);
            ihdr[8] = bitDepth;
            ihdr[9] = colorType;
            ihdr[10] = compression;
            ihdr[11] = filterMethod;
            ihdr[12] = interlace;
            return ihdr;
        }

        private static byte[] ExportIoZlibCompress(byte[] raw)
        {
            using (MemoryStream compressed = new MemoryStream())
            {
                using (System.IO.Compression.ZLibStream deflater = new System.IO.Compression.ZLibStream(
                    compressed, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    deflater.Write(raw, 0, raw.Length);
                }
                return compressed.ToArray();
            }
        }

        private static byte[] ExportIoBuildPngFile(params (string Type, byte[] Data)[] chunks)
        {
            List<byte> file = new List<byte>();
            file.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            for (int i = 0; i < chunks.Length; i++)
            {
                ExportIoAppendChunk(file, chunks[i].Type, chunks[i].Data);
            }
            return file.ToArray();
        }

        // ------------------------------------------------------------------
        // FastPngDecoder: adversarial corpus. Every malformed file must fail
        // with the decoder's documented ArgumentException (or the argument
        // guards' ArgumentNullException) and never a raw crash; benign
        // variations (ancillary chunks, split IDAT) must decode.
        // ------------------------------------------------------------------
        private static void TestExportIoPngDecoderAdversarial()
        {
            uint[] pixels = { 0xFF112233u, 0x80445566u, 0x00778899u, 0xFFAABBCCu };
            byte[] valid = ExportIoEncodePng(pixels, 2, 2);
            byte[] rawScanlines =
            {
                0, 0x33, 0x22, 0x11, 0xFF, 0x66, 0x55, 0x44, 0x80,
                0, 0x99, 0x88, 0x77, 0x00, 0xCC, 0xBB, 0xAA, 0xFF
            };
            byte[] validIdat = ExportIoZlibCompress(rawScanlines);
            byte[] validIhdr = ExportIoBuildIhdr(2, 2, 8, 6, 0, 0, 0);

            AssertThrows<ArgumentNullException>(() => FastPngDecoder.Read((byte[])null, out _, out _));
            AssertThrows<ArgumentNullException>(() => FastPngDecoder.Read((Stream)null, out _, out _));
            using (ExportIoForwardOnlyStream writeOnly = new ExportIoForwardOnlyStream())
            {
                AssertThrows<ArgumentException>(() => FastPngDecoder.Read(writeOnly, out _, out _));
            }

            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(Array.Empty<byte>(), out _, out _));
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(new byte[] { 0x89, 0x50 }, out _, out _));
            byte[] badSignature = (byte[])valid.Clone();
            badSignature[0] = 0x88;
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(badSignature, out _, out _));

            // Any flipped payload byte breaks that chunk's CRC.
            byte[] corruptPayload = (byte[])valid.Clone();
            corruptPayload[8 + 8 + 2] ^= 0x01; // inside IHDR data
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(corruptPayload, out _, out _));
            byte[] corruptCrc = (byte[])valid.Clone();
            corruptCrc[8 + 8 + 13] ^= 0x01; // IHDR CRC field
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(corruptCrc, out _, out _));

            // Truncation anywhere after the signature is rejected.
            int[] truncationPoints = { 8, 9, 8 + 8, valid.Length - 13, valid.Length - 1 };
            for (int i = 0; i < truncationPoints.Length; i++)
            {
                byte[] truncated = new byte[truncationPoints[i]];
                Buffer.BlockCopy(valid, 0, truncated, 0, truncated.Length);
                AssertThrows<ArgumentException>(() => FastPngDecoder.Read(truncated, out _, out _));
            }

            byte[] trailing = new byte[valid.Length + 1];
            Buffer.BlockCopy(valid, 0, trailing, 0, valid.Length);
            trailing[valid.Length] = 0x00;
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(trailing, out _, out _));

            // IHDR rejections: unsupported depth/type/methods and hostile dimensions.
            (byte Depth, byte Color, byte Compression, byte Filter, byte Interlace)[] badHeaders =
            {
                (16, 6, 0, 0, 0), (1, 6, 0, 0, 0), (8, 2, 0, 0, 0), (8, 0, 0, 0, 0),
                (8, 6, 1, 0, 0), (8, 6, 0, 1, 0), (8, 6, 0, 0, 1)
            };
            for (int i = 0; i < badHeaders.Length; i++)
            {
                byte[] png = ExportIoBuildPngFile(
                    ("IHDR", ExportIoBuildIhdr(
                        2, 2,
                        badHeaders[i].Depth,
                        badHeaders[i].Color,
                        badHeaders[i].Compression,
                        badHeaders[i].Filter,
                        badHeaders[i].Interlace)),
                    ("IDAT", validIdat),
                    ("IEND", Array.Empty<byte>()));
                AssertThrows<ArgumentException>(() => FastPngDecoder.Read(png, out _, out _));
            }
            uint[][] badDimensions =
            {
                new[] { 0u, 2u }, new[] { 2u, 0u },
                new[] { 0x80000000u, 2u }, new[] { 2u, 0x80000000u },
                new[] { 65536u, 65536u } // width * height overflows Int32
            };
            for (int i = 0; i < badDimensions.Length; i++)
            {
                byte[] png = ExportIoBuildPngFile(
                    ("IHDR", ExportIoBuildIhdr(badDimensions[i][0], badDimensions[i][1], 8, 6, 0, 0, 0)),
                    ("IDAT", validIdat),
                    ("IEND", Array.Empty<byte>()));
                AssertThrows<ArgumentException>(() => FastPngDecoder.Read(png, out _, out _));
            }

            // Structural chunk-ordering violations.
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(ExportIoBuildPngFile(
                ("IDAT", validIdat),
                ("IHDR", validIhdr),
                ("IEND", Array.Empty<byte>())), out _, out _));
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(ExportIoBuildPngFile(
                ("IHDR", validIhdr),
                ("IHDR", validIhdr),
                ("IDAT", validIdat),
                ("IEND", Array.Empty<byte>())), out _, out _));
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(ExportIoBuildPngFile(
                ("IHDR", validIhdr),
                ("IEND", Array.Empty<byte>())), out _, out _));
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(ExportIoBuildPngFile(
                ("IHDR", validIhdr),
                ("IDAT", validIdat),
                ("IEND", new byte[] { 1 })), out _, out _));

            // IDAT payload failures: garbage zlib, too little raw data, too much raw
            // data, and an unknown scanline filter type.
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(ExportIoBuildPngFile(
                ("IHDR", validIhdr),
                ("IDAT", new byte[] { 1, 2, 3, 4, 5 }),
                ("IEND", Array.Empty<byte>())), out _, out _));
            byte[] shortRaw = new byte[rawScanlines.Length - 1];
            Buffer.BlockCopy(rawScanlines, 0, shortRaw, 0, shortRaw.Length);
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(ExportIoBuildPngFile(
                ("IHDR", validIhdr),
                ("IDAT", ExportIoZlibCompress(shortRaw)),
                ("IEND", Array.Empty<byte>())), out _, out _));
            byte[] longRaw = new byte[rawScanlines.Length + 1];
            Buffer.BlockCopy(rawScanlines, 0, longRaw, 0, rawScanlines.Length);
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(ExportIoBuildPngFile(
                ("IHDR", validIhdr),
                ("IDAT", ExportIoZlibCompress(longRaw)),
                ("IEND", Array.Empty<byte>())), out _, out _));
            byte[] badFilterRaw = (byte[])rawScanlines.Clone();
            badFilterRaw[0] = 5;
            AssertThrows<ArgumentException>(() => FastPngDecoder.Read(ExportIoBuildPngFile(
                ("IHDR", validIhdr),
                ("IDAT", ExportIoZlibCompress(badFilterRaw)),
                ("IEND", Array.Empty<byte>())), out _, out _));

            // Benign variations decode: ancillary chunks anywhere after IHDR, and the
            // IDAT stream split across multiple chunks.
            byte[] firstIdatHalf = new byte[validIdat.Length / 2];
            byte[] secondIdatHalf = new byte[validIdat.Length - firstIdatHalf.Length];
            Buffer.BlockCopy(validIdat, 0, firstIdatHalf, 0, firstIdatHalf.Length);
            Buffer.BlockCopy(validIdat, firstIdatHalf.Length, secondIdatHalf, 0, secondIdatHalf.Length);
            byte[] benign = ExportIoBuildPngFile(
                ("IHDR", validIhdr),
                ("tEXt", new byte[] { (byte)'k', 0, (byte)'v' }),
                ("IDAT", firstIdatHalf),
                ("IDAT", secondIdatHalf),
                ("tIME", new byte[] { 7, 230, 1, 2, 3, 4, 5 }),
                ("IEND", Array.Empty<byte>()));
            uint[] decodedBenign = FastPngDecoder.Read(benign, out int benignWidth, out int benignHeight);
            AssertEqual(2, benignWidth);
            AssertEqual(2, benignHeight);
            AssertSequenceEqual(pixels, decodedBenign);

            // The input buffer is never modified by a successful or failing decode.
            byte[] inputSnapshot = (byte[])valid.Clone();
            FastPngDecoder.Read(valid, out _, out _);
            AssertSequenceEqual(inputSnapshot, valid);
        }

        // ------------------------------------------------------------------
        // Atlas grid layout: near-square ComputeAtlasLayout, the row-major
        // frame-origin helper, and ExtractFrameFromAtlas as the exact inverse
        // of PackFramesToAtlas.
        // ------------------------------------------------------------------
        private static void TestExportIoAtlasLayoutHelpers()
        {
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ComputeAtlasLayout(0, 1, 1, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ComputeAtlasLayout(-1, 1, 1, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ComputeAtlasLayout(1, 0, 1, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ComputeAtlasLayout(1, 1, 0, out _, out _));
            AssertThrows<OverflowException>(() => FastExporter.ComputeAtlasLayout(int.MaxValue, int.MaxValue, 1, out _, out _));

            (int FrameCount, int Columns, int Rows)[] expectedLayouts =
            {
                (1, 1, 1), (2, 2, 1), (3, 2, 2), (4, 2, 2), (5, 3, 2), (6, 3, 2),
                (7, 3, 3), (9, 3, 3), (10, 4, 3), (16, 4, 4), (17, 5, 4),
                (64, 8, 8), (100, 10, 10), (101, 11, 10)
            };
            for (int i = 0; i < expectedLayouts.Length; i++)
            {
                FastExporter.ComputeAtlasLayout(expectedLayouts[i].FrameCount, 3, 5, out int columns, out int rows);
                AssertEqual(expectedLayouts[i].Columns, columns);
                AssertEqual(expectedLayouts[i].Rows, rows);
            }

            for (int frameCount = 1; frameCount <= 500; frameCount++)
            {
                FastExporter.ComputeAtlasLayout(frameCount, 1, 1, out int columns, out int rows);
                Assert(columns >= 1 && rows >= 1);
                Assert((long)columns * rows >= frameCount);
                Assert(columns >= rows);                       // landscape-or-square
                Assert((long)(rows - 1) * columns < frameCount); // no fully empty rows
                Assert((long)(columns - 1) * (columns - 1) < frameCount); // smallest covering square
            }

            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.GetAtlasFrameOrigin(-1, 1, 1, 1, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.GetAtlasFrameOrigin(0, 0, 1, 1, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.GetAtlasFrameOrigin(0, 1, 0, 1, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.GetAtlasFrameOrigin(0, 1, 1, 0, out _, out _));
            AssertThrows<OverflowException>(() => FastExporter.GetAtlasFrameOrigin(2, 3, int.MaxValue, 1, out _, out _));
            AssertThrows<OverflowException>(() => FastExporter.GetAtlasFrameOrigin(int.MaxValue, 1, 1, 2, out _, out _));

            FastExporter.GetAtlasFrameOrigin(5, 3, 4, 6, out int originX, out int originY);
            AssertEqual(8, originX);  // column 2 of 3, frame width 4
            AssertEqual(6, originY);  // row 1, frame height 6

            // Extraction guards mirror the packing guards.
            PackedRgba[] atlasGuard = new PackedRgba[12];
            PackedRgba[] frameGuard = new PackedRgba[4];
            AssertThrows<ArgumentNullException>(() => FastExporter.ExtractFrameFromAtlas((PackedRgba[])null, 4, frameGuard, 2, 2, 0, 0));
            AssertThrows<ArgumentNullException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, (PackedRgba[])null, 2, 2, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 0, frameGuard, 2, 2, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, frameGuard, 0, 2, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, frameGuard, 2, 0, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, frameGuard, 2, 2, -1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, frameGuard, 2, 2, 0, -1));
            AssertThrows<ArgumentException>(() => FastExporter.ExtractFrameFromAtlas(Array.Empty<PackedRgba>(), 4, frameGuard, 2, 2, 0, 0));
            AssertThrows<ArgumentException>(() => FastExporter.ExtractFrameFromAtlas(new PackedRgba[11], 4, frameGuard, 2, 2, 0, 0));
            AssertThrows<ArgumentException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, new PackedRgba[3], 2, 2, 0, 0));
            AssertThrows<ArgumentException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, frameGuard, 2, 2, 3, 0));
            AssertThrows<ArgumentException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, frameGuard, 2, 2, 0, 2));
            AssertThrows<OverflowException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, frameGuard, int.MaxValue, 2, 0, 0));
            AssertThrows<OverflowException>(() => FastExporter.ExtractFrameFromAtlas(atlasGuard, 4, frameGuard, 1, 1, int.MaxValue, 0));

            // Pack-then-extract is the identity for every frame of a grid layout, and
            // a larger destination buffer keeps its tail untouched.
            Random random = new Random(0x6A11A5);
            for (int iteration = 0; iteration < 40; iteration++)
            {
                int frameWidth = random.Next(1, 7);
                int frameHeight = random.Next(1, 7);
                int frameCount = random.Next(1, 13);
                FastExporter.ComputeAtlasLayout(frameCount, frameWidth, frameHeight, out int columns, out int rows);
                int atlasWidth = columns * frameWidth;
                int atlasHeight = rows * frameHeight;
                uint[] atlas = new uint[atlasWidth * atlasHeight];
                uint[][] frames = new uint[frameCount][];
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    frames[frameIndex] = new uint[frameWidth * frameHeight];
                    for (int i = 0; i < frames[frameIndex].Length; i++)
                    {
                        frames[frameIndex][i] = (uint)random.Next() ^ ((uint)frameIndex << 24);
                    }
                    FastExporter.GetAtlasFrameOrigin(frameIndex, columns, frameWidth, frameHeight, out int destX, out int destY);
                    Assert(destX + frameWidth <= atlasWidth);
                    Assert(destY + frameHeight <= atlasHeight);
                    FastExporter.PackFramesToAtlas(atlas, atlasWidth, frames[frameIndex], frameWidth, frameHeight, destX, destY);
                }
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    FastExporter.GetAtlasFrameOrigin(frameIndex, columns, frameWidth, frameHeight, out int sourceX, out int sourceY);
                    uint[] extracted = new uint[frameWidth * frameHeight + 3];
                    extracted[frameWidth * frameHeight] = 0xDEADBEEFu;
                    extracted[frameWidth * frameHeight + 1] = 0xCAFEBABEu;
                    extracted[frameWidth * frameHeight + 2] = 0x0BADF00Du;
                    FastExporter.ExtractFrameFromAtlas(atlas, atlasWidth, extracted, frameWidth, frameHeight, sourceX, sourceY);
                    for (int i = 0; i < frames[frameIndex].Length; i++) AssertEqual(frames[frameIndex][i], extracted[i]);
                    AssertEqual(0xDEADBEEFu, extracted[frameWidth * frameHeight]);
                    AssertEqual(0xCAFEBABEu, extracted[frameWidth * frameHeight + 1]);
                    AssertEqual(0x0BADF00Du, extracted[frameWidth * frameHeight + 2]);
                }
            }

            // Element-size genericity of the extraction path (2- and 8-byte elements).
            ushort[] atlas16 = new ushort[6 * 4];
            for (int i = 0; i < atlas16.Length; i++) atlas16[i] = (ushort)(3000 + i);
            ushort[] extracted16 = new ushort[6];
            FastExporter.ExtractFrameFromAtlas(atlas16, 6, extracted16, 2, 3, 4, 1);
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 2; x++) AssertEqual(atlas16[(1 + y) * 6 + 4 + x], extracted16[y * 2 + x]);
            }
            ulong[] atlas64 = new ulong[3 * 3];
            for (int i = 0; i < atlas64.Length; i++) atlas64[i] = 0x5150000000000000ul + (ulong)i;
            ulong[] extracted64 = new ulong[2];
            FastExporter.ExtractFrameFromAtlas(atlas64, 3, extracted64, 1, 2, 2, 1);
            AssertEqual(atlas64[1 * 3 + 2], extracted64[0]);
            AssertEqual(atlas64[2 * 3 + 2], extracted64[1]);
        }

        // ------------------------------------------------------------------
        // b2-pipeline-contract agent additions below: the shared atlas-metadata
        // validator (#16b), FastImporter.TryParse (#16c), the bounded IO retry
        // (#12), the shared CRC-32 (#19), and the .efyvlaby document version +
        // baseAssetType contract (#16a/#16e).
        // ------------------------------------------------------------------

        private static AtlasMetadataJson ExportIoAtlas(
            int atlasWidth,
            int atlasHeight,
            int frameWidth,
            int frameHeight,
            params AnimationMetadataJson[] animations)
        {
            return new AtlasMetadataJson
            {
                formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                frameWidth = frameWidth,
                frameHeight = frameHeight,
                atlasWidth = atlasWidth,
                atlasHeight = atlasHeight,
                animations = new List<AnimationMetadataJson>(animations)
            };
        }

        private static AnimationMetadataJson ExportIoAnimation(string name, int fps, int start, int count)
        {
            return new AnimationMetadataJson { name = name, fps = fps, startFrame = start, frameCount = count };
        }

        private static void ExportIoAssertAtlasError(AtlasMetadataJson metadata, AtlasMetadataError expected)
        {
            Assert(!FastExporter.TryValidateAtlasMetadata(metadata, out AtlasMetadataError error));
            AssertEqual(expected, error);
        }

        private static void TestExportIoSharedAtlasValidator()
        {
            int maxDim = EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasDimension;

            // Valid shapes, including gaps between animations and a full-capacity fill.
            AtlasMetadataJson valid = ExportIoAtlas(6, 4, 2, 2,
                ExportIoAnimation("Idle", 8, 0, 2), ExportIoAnimation("Run", 12, 4, 2));
            Assert(FastExporter.TryValidateAtlasMetadata(valid, out AtlasMetadataError noError));
            AssertEqual(AtlasMetadataError.None, noError);
            Assert(FastExporter.TryValidateAtlasMetadata(valid, 6, 4, out _));
            Assert(FastExporter.TryValidateAtlasMetadata(ExportIoAtlas(4, 4, 2, 2), out _));
            Assert(FastExporter.TryValidateAtlasMetadata(
                ExportIoAtlas(4, 4, 2, 2, ExportIoAnimation("Full", 1, 0, 4)), out _));

            // Per-cause classifications.
            AtlasMetadataJson wrongVersion = valid;
            wrongVersion.formatVersion++;
            ExportIoAssertAtlasError(wrongVersion, AtlasMetadataError.FormatVersion);
            AtlasMetadataJson zeroFrame = valid;
            zeroFrame.frameWidth = 0;
            ExportIoAssertAtlasError(zeroFrame, AtlasMetadataError.FrameDimensions);
            AtlasMetadataJson negativeFrame = valid;
            negativeFrame.frameHeight = -2;
            ExportIoAssertAtlasError(negativeFrame, AtlasMetadataError.FrameDimensions);
            AtlasMetadataJson zeroAtlas = valid;
            zeroAtlas.atlasWidth = 0;
            ExportIoAssertAtlasError(zeroAtlas, AtlasMetadataError.AtlasDimensions);
            Assert(!FastExporter.TryValidateAtlasMetadata(valid, 8, 4, out AtlasMetadataError mismatch));
            AssertEqual(AtlasMetadataError.DimensionMismatch, mismatch);
            AtlasMetadataJson misaligned = valid;
            misaligned.atlasWidth = 5;
            misaligned.animations = new List<AnimationMetadataJson>();
            ExportIoAssertAtlasError(misaligned, AtlasMetadataError.FrameAlignment);
            AtlasMetadataJson missingAnimations = valid;
            missingAnimations.animations = null;
            ExportIoAssertAtlasError(missingAnimations, AtlasMetadataError.AnimationsMissing);

            AtlasMetadataJson badName = ExportIoAtlas(6, 4, 2, 2, ExportIoAnimation("  ", 1, 0, 1));
            ExportIoAssertAtlasError(badName, AtlasMetadataError.AnimationName);
            AtlasMetadataJson badFps = ExportIoAtlas(6, 4, 2, 2, ExportIoAnimation("A", 0, 0, 1));
            ExportIoAssertAtlasError(badFps, AtlasMetadataError.AnimationFps);
            AtlasMetadataJson badStart = ExportIoAtlas(6, 4, 2, 2, ExportIoAnimation("A", 1, -1, 1));
            ExportIoAssertAtlasError(badStart, AtlasMetadataError.AnimationStartFrame);
            AtlasMetadataJson badCount = ExportIoAtlas(6, 4, 2, 2, ExportIoAnimation("A", 1, 0, 0));
            ExportIoAssertAtlasError(badCount, AtlasMetadataError.AnimationFrameCount);
            AtlasMetadataJson overlap = ExportIoAtlas(6, 4, 2, 2,
                ExportIoAnimation("A", 1, 0, 3), ExportIoAnimation("B", 1, 2, 1));
            ExportIoAssertAtlasError(overlap, AtlasMetadataError.AnimationOverlap);
            AtlasMetadataJson outOfOrder = ExportIoAtlas(6, 4, 2, 2,
                ExportIoAnimation("A", 1, 4, 1), ExportIoAnimation("B", 1, 0, 1));
            ExportIoAssertAtlasError(outOfOrder, AtlasMetadataError.AnimationOverlap);
            AtlasMetadataJson pastCapacity = ExportIoAtlas(6, 4, 2, 2, ExportIoAnimation("A", 1, 5, 2));
            ExportIoAssertAtlasError(pastCapacity, AtlasMetadataError.AnimationPastCapacity);
            // Long math (#16b): int.MaxValue never wraps into a false "valid".
            AtlasMetadataJson hugeStart = ExportIoAtlas(6, 4, 2, 2,
                ExportIoAnimation("A", 1, int.MaxValue, int.MaxValue));
            ExportIoAssertAtlasError(hugeStart, AtlasMetadataError.AnimationPastCapacity);

            // Unity caps enforced by the shared validator (the old exporter copy
            // skipped them entirely).
            AtlasMetadataJson tooWide = ExportIoAtlas(maxDim + 1, 1, 1, 1);
            ExportIoAssertAtlasError(tooWide, AtlasMetadataError.AtlasLimit);
            AtlasMetadataJson tooTall = ExportIoAtlas(1, maxDim + 1, 1, 1);
            ExportIoAssertAtlasError(tooTall, AtlasMetadataError.AtlasLimit);
            AtlasMetadataJson tooManyPixels = ExportIoAtlas(maxDim, maxDim, 1, 1);
            Assert((long)maxDim * maxDim > EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasPixelCount);
            ExportIoAssertAtlasError(tooManyPixels, AtlasMetadataError.AtlasLimit);
            AtlasMetadataJson atWidthCap = ExportIoAtlas(maxDim, 1, 1, 1);
            Assert(FastExporter.TryValidateAtlasMetadata(atWidthCap, out _));

            // FastExporter refuses to publish a capped atlas end to end.
            string root = Path.Combine(Path.GetTempPath(), "EFYVExportIoCaps-" + Guid.NewGuid().ToString("N"));
            try
            {
                PackedRgba[] oversized = new PackedRgba[maxDim + 1];
                AtlasMetadataJson oversizedMetadata = ExportIoAtlas(maxDim + 1, 1, 1, 1);
                Dictionary<string, object> properties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = "Capped"
                };
                AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PushToUnityLiveHook(
                    root, "Type", properties, new List<HitboxJson>(), oversized, maxDim + 1, 1, oversizedMetadata));
                AssertNoExportFiles(root);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void TestExportIoTryParseContract()
        {
            string directory = Path.Combine(Path.GetTempPath(), "EFYVExportIoTryParse-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "doc" + BackendConfig.Exporter.EfyvExtension);

                // Missing: no file, null/empty/whitespace paths, and directories.
                AssertEqual(EfyvParseResult.Missing, FastImporter.TryParse(path, out EFYVJsonFormat missing));
                AssertDefaultImport(missing);
                AssertEqual(EfyvParseResult.Missing, FastImporter.TryParse(null, out _));
                AssertEqual(EfyvParseResult.Missing, FastImporter.TryParse(string.Empty, out _));
                AssertEqual(EfyvParseResult.Missing, FastImporter.TryParse("   ", out _));
                AssertEqual(EfyvParseResult.Missing, FastImporter.TryParse(directory, out _));

                // Malformed: TryParse classifies where ParseEfyvFile throws, and
                // the out value is fully reset.
                string[] malformedDocuments = { "{", "[1]", "null", "{\"assetType\":1}", string.Empty };
                for (int i = 0; i < malformedDocuments.Length; i++)
                {
                    ExportIoWriteDoc(path, malformedDocuments[i]);
                    AssertEqual(EfyvParseResult.Malformed, FastImporter.TryParse(path, out EFYVJsonFormat malformed));
                    AssertDefaultImport(malformed);
                    AssertThrows<JsonException>(() => FastImporter.ParseEfyvFile(path));
                }

                // Valid: both entry points agree on the parsed content.
                ExportIoWriteDoc(
                    path,
                    "{\"documentVersion\":1,\"assetType\":\"EnemyData\",\"baseAssetType\":\"EnemyData\"," +
                    "\"properties\":{\"entityName\":\"P\"},\"hitboxes\":[]}");
                AssertEqual(EfyvParseResult.Valid, FastImporter.TryParse(path, out EFYVJsonFormat parsed));
                AssertEqual("EnemyData", parsed.assetType);
                AssertEqual("EnemyData", parsed.baseAssetType);
                AssertEqual(1, parsed.EffectiveDocumentVersion);
                AssertEqual("P", parsed.properties["entityName"].GetString());
                EFYVJsonFormat legacyApi = FastImporter.ParseEfyvFile(path);
                AssertEqual(parsed.assetType, legacyApi.assetType);
                AssertEqual(parsed.properties.Count, legacyApi.properties.Count);

                // I/O failures propagate from BOTH entry points (a lock is a
                // transient environment error, not a document state).
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    AssertThrows<IOException>(() => FastImporter.TryParse(path, out _));
                    AssertThrows<IOException>(() => FastImporter.ParseEfyvFile(path));
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void TestExportIoIoRetryContract()
        {
            // Success on first attempt: no delays.
            List<int> delays = new List<int>();
            int calls = 0;
            FastIoRetry.Run(() => calls++, delays.Add);
            AssertEqual(1, calls);
            AssertEqual(0, delays.Count);

            // Two transient IOExceptions then success: exactly 3 attempts with
            // the documented 20ms/50ms backoff sequence.
            delays.Clear();
            calls = 0;
            FastIoRetry.Run(
                () =>
                {
                    calls++;
                    if (calls < 3) throw new IOException("transient");
                },
                delays.Add);
            AssertEqual(3, calls);
            AssertEqual(2, delays.Count);
            AssertEqual(BackendConfig.IO.PublishRetryFirstDelayMilliseconds, delays[0]);
            AssertEqual(BackendConfig.IO.PublishRetryMaxDelayMilliseconds, delays[1]);

            // Persistent failure: bounded at PublishRetryAttempts, then rethrows.
            delays.Clear();
            calls = 0;
            AssertThrows<IOException>(() => FastIoRetry.Run(
                () =>
                {
                    calls++;
                    throw new IOException("persistent");
                },
                delays.Add));
            AssertEqual(BackendConfig.IO.PublishRetryAttempts, calls);
            AssertEqual(BackendConfig.IO.PublishRetryAttempts - 1, delays.Count);

            // DirectoryNotFoundException is an IOException subclass: still retried.
            delays.Clear();
            calls = 0;
            AssertThrows<DirectoryNotFoundException>(() => FastIoRetry.Run(
                () =>
                {
                    calls++;
                    throw new DirectoryNotFoundException();
                },
                delays.Add));
            AssertEqual(BackendConfig.IO.PublishRetryAttempts, calls);

            // Non-IO exceptions are never retried.
            delays.Clear();
            calls = 0;
            AssertThrows<InvalidOperationException>(() => FastIoRetry.Run(
                () =>
                {
                    calls++;
                    throw new InvalidOperationException();
                },
                delays.Add));
            AssertEqual(1, calls);
            AssertEqual(0, delays.Count);

            AssertThrows<ArgumentNullException>(() => FastIoRetry.Run(null));
            AssertThrows<ArgumentNullException>(() => FastIoRetry.Run(() => { }, null));

            // End to end: a destination locked for the whole window makes
            // PublishFile surface IOException and leaves both files intact.
            string directory = Path.Combine(Path.GetTempPath(), "EFYVExportIoRetry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string temporary = Path.Combine(directory, "staged.tmp");
                string destination = Path.Combine(directory, "dest.bin");
                File.WriteAllBytes(temporary, new byte[] { 1, 2, 3 });
                File.WriteAllBytes(destination, new byte[] { 9 });
                using (FileStream locked = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    AssertThrows<IOException>(() => FastExporter.PublishFile(temporary, destination));
                }
                AssertSequenceEqual(new byte[] { 9 }, File.ReadAllBytes(destination));
                Assert(File.Exists(temporary));

                // Once the lock is gone the same publish succeeds atomically.
                FastExporter.PublishFile(temporary, destination);
                AssertSequenceEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destination));
                Assert(!File.Exists(temporary));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void TestExportIoCrc32Reference()
        {
            AssertEqual(ComputeStandardPngCrc(Array.Empty<byte>()), FastCrc32.Compute(ReadOnlySpan<byte>.Empty));

            Random random = new Random(0x0C4C32);
            for (int iteration = 0; iteration < 40; iteration++)
            {
                byte[] data = new byte[random.Next(0, 300)];
                random.NextBytes(data);
                AssertEqual(ComputeStandardPngCrc(data), FastCrc32.Compute(data));

                // Incremental Update over arbitrary splits equals one-shot Compute.
                int split = data.Length == 0 ? 0 : random.Next(data.Length + 1);
                uint incremental = FastCrc32.Update(
                    BackendConfig.Exporter.Png.InitialCrc,
                    new ReadOnlySpan<byte>(data, 0, split));
                incremental = FastCrc32.Update(
                    incremental,
                    new ReadOnlySpan<byte>(data, split, data.Length - split));
                AssertEqual(FastCrc32.Compute(data), incremental ^ BackendConfig.Exporter.Png.FinalCrcMask);
            }

            // Known check value of CRC-32/ISO-HDLC over "123456789".
            AssertEqual(0xCBF43926u, FastCrc32.Compute(Encoding.ASCII.GetBytes("123456789")));
        }

        private static void TestExportIoDocumentVersionAndBaseAssetType()
        {
            string root = Path.Combine(Path.GetTempPath(), "EFYVExportIoDocVer-" + Guid.NewGuid().ToString("N"));
            try
            {
                Dictionary<string, object> properties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = "Versioned"
                };
                FastExporter.PushToUnityLiveHook(
                    root,
                    "CustomTrapData",
                    properties,
                    new List<HitboxJson>(),
                    new[] { new PackedRgba(0xFF00FF00u) },
                    1,
                    1,
                    new AtlasMetadataJson
                    {
                        formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                        frameWidth = 1,
                        frameHeight = 1,
                        atlasWidth = 1,
                        atlasHeight = 1,
                        animations = new List<AnimationMetadataJson>()
                    },
                    EFYVLabyrinthConfig.Shared.GameAssetAssetType);

                string jsonPath = Path.Combine(root, "Versioned" + BackendConfig.Exporter.EfyvExtension);
                EFYVJsonFormat imported = FastImporter.ParseEfyvFile(jsonPath);
                Assert(imported.documentVersion.HasValue);
                AssertEqual(BackendConfig.Exporter.CurrentDocumentVersion, imported.EffectiveDocumentVersion);
                AssertEqual("CustomTrapData", imported.assetType);
                AssertEqual(EFYVLabyrinthConfig.Shared.GameAssetAssetType, imported.baseAssetType);

                // documentVersion is the FIRST top-level member (stream parsers can
                // dispatch on it before reading anything else), and baseAssetType
                // immediately follows assetType.
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(jsonPath)))
                {
                    List<string> names = new List<string>();
                    foreach (JsonProperty property in document.RootElement.EnumerateObject()) names.Add(property.Name);
                    AssertEqual(BackendConfig.Exporter.FieldDocumentVersion, names[0]);
                    AssertEqual(BackendConfig.Exporter.FieldAssetType, names[1]);
                    AssertEqual(BackendConfig.Exporter.FieldBaseAssetType, names[2]);
                }

                // Without a base type the field is omitted entirely (legacy shape).
                Dictionary<string, object> plain = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = "NoBase"
                };
                FastExporter.PushToUnityLiveHook(root, "TypeZ", plain, new List<HitboxJson>(), new[] { new PackedRgba(1u) }, 1, 1);
                EFYVJsonFormat noBase = FastImporter.ParseEfyvFile(
                    Path.Combine(root, "NoBase" + BackendConfig.Exporter.EfyvExtension));
                AssertEqual(null, noBase.baseAssetType);
                AssertEqual(BackendConfig.Exporter.CurrentDocumentVersion, noBase.EffectiveDocumentVersion);

                // A pre-versioning legacy document (no documentVersion key) reads
                // as version 1 - backward compatible by construction.
                string legacyPath = Path.Combine(root, "Legacy" + BackendConfig.Exporter.EfyvExtension);
                ExportIoWriteDoc(legacyPath, "{\"assetType\":\"EnemyData\",\"properties\":{},\"hitboxes\":[]}");
                EFYVJsonFormat legacy = FastImporter.ParseEfyvFile(legacyPath);
                Assert(!legacy.documentVersion.HasValue);
                AssertEqual(BackendConfig.Exporter.LegacyDocumentVersion, legacy.EffectiveDocumentVersion);

                // Future versions round-trip through the model untouched.
                string futurePath = Path.Combine(root, "Future" + BackendConfig.Exporter.EfyvExtension);
                ExportIoWriteDoc(futurePath, "{\"documentVersion\":9,\"assetType\":\"EnemyData\"}");
                AssertEqual(9, FastImporter.ParseEfyvFile(futurePath).EffectiveDocumentVersion);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        private struct ExportIoTwoUShorts
        {
            public ushort Low;
            public ushort High;
        }

        private sealed class ExportIoForwardOnlyStream : Stream
        {
            private readonly MemoryStream inner = new MemoryStream();

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

            public byte[] ToArray() => inner.ToArray();

            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
