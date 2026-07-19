using System;
using System.Collections.Generic;
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
        private const ulong SaveCanaryBefore = 0x0123456789ABCDEFul;
        private const ulong SaveCanaryAfter = 0xFEDCBA9876543210ul;

        private static unsafe void TestImporterAndSaveAdversarial()
        {
            string directory = Path.Combine(Path.GetTempPath(), "EFYV-ImportSave-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                TestImporterMalformedInputs(directory);
                TestImporterValidEdges(directory);
                TestSaveExactRoundTripAndCanaries(directory);
                TestSaveTruncationAndCorruptionRejection(directory);
                TestSaveFileAccessFailures(directory);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void TestImporterMalformedInputs(string directory)
        {
            EFYVJsonFormat missing = FastImporter.ParseEfyvFile(Path.Combine(directory, "missing.efyv"));
            AssertDefaultImport(missing);
            AssertDefaultImport(FastImporter.ParseEfyvFile(null));
            AssertDefaultImport(FastImporter.ParseEfyvFile(string.Empty));
            AssertDefaultImport(FastImporter.ParseEfyvFile("   "));
            AssertDefaultImport(FastImporter.ParseEfyvFile(directory));

            string malformedPath = Path.Combine(directory, "malformed.efyv");
            string[] malformedDocuments =
            {
                string.Empty,
                "{",
                "[1,2,3]",
                "\"not an object\"",
                "{\"assetType\":",
                "{\"assetType\":1}",
                "{\"hitboxes\":{}}",
                "{\"atlas\":{\"formatVersion\":2147483648}}",
                "{\"properties\":{\"bad\":01}}",
                "{\"properties\":{\"bad\":NaN}}",
                "{\"properties\":{\"bad\":true} trailing"
            };
            for (int i = 0; i < malformedDocuments.Length; i++)
            {
                File.WriteAllText(malformedPath, malformedDocuments[i], new UTF8Encoding(false));
                byte[] before = File.ReadAllBytes(malformedPath);
                AssertThrows<JsonException>(() => FastImporter.ParseEfyvFile(malformedPath));
                AssertSequenceEqual(before, File.ReadAllBytes(malformedPath));
            }

            StringBuilder tooDeep = new StringBuilder("{\"properties\":{\"deep\":");
            for (int i = 0; i < 80; i++) tooDeep.Append('[');
            tooDeep.Append('0');
            for (int i = 0; i < 80; i++) tooDeep.Append(']');
            tooDeep.Append("}}");
            File.WriteAllText(malformedPath, tooDeep.ToString(), new UTF8Encoding(false));
            AssertThrows<JsonException>(() => FastImporter.ParseEfyvFile(malformedPath));

            File.WriteAllBytes(malformedPath, new byte[] { (byte)'{', (byte)'\"', 0xFF, (byte)'\"', (byte)':', (byte)'0', (byte)'}' });
            // System.Text.Json replaces malformed UTF-8 in an unknown property name. The
            // importer must still stay bounded, return no modeled data, and not alter input.
            byte[] invalidUtf8 = File.ReadAllBytes(malformedPath);
            AssertDefaultImport(FastImporter.ParseEfyvFile(malformedPath));
            AssertSequenceEqual(invalidUtf8, File.ReadAllBytes(malformedPath));

            File.WriteAllText(malformedPath, "null", new UTF8Encoding(false));
            AssertThrows<JsonException>(() => FastImporter.ParseEfyvFile(malformedPath));

            File.WriteAllText(malformedPath, "{}", new UTF8Encoding(false));
            using (FileStream locked = new FileStream(malformedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                AssertThrows<IOException>(() => FastImporter.ParseEfyvFile(malformedPath));
            }
        }

        private static void TestImporterValidEdges(string directory)
        {
            string path = Path.Combine(directory, "valid-with-unknowns.bin");
            const string json =
                "{\"assetType\":\"Entity\\u0020Type\",\"properties\":{" +
                "\"integer\":-2147483648,\"maximum\":18446744073709551615," +
                "\"nested\":{\"array\":[true,false,null,{\"text\":\"\\u263A\"}]}} ," +
                "\"hitboxes\":[{\"frameIndex\":2147483647,\"hitboxType\":null," +
                "\"x\":-1.5,\"y\":2.25,\"width\":0,\"height\":3.5}]," +
                "\"atlas\":null,\"unknown\":{\"ignored\":true}}";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            byte[] withBom = new byte[bytes.Length + 3];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Buffer.BlockCopy(bytes, 0, withBom, 3, bytes.Length);
            File.WriteAllBytes(path, withBom);

            EFYVJsonFormat imported = FastImporter.ParseEfyvFile(path);
            AssertEqual("Entity Type", imported.assetType);
            AssertEqual(3, imported.properties.Count);
            AssertEqual(int.MinValue, imported.properties["integer"].GetInt32());
            AssertEqual(ulong.MaxValue, imported.properties["maximum"].GetUInt64());
            AssertEqual(4, imported.properties["nested"].GetProperty("array").GetArrayLength());
            AssertEqual("\u263A", imported.properties["nested"].GetProperty("array")[3].GetProperty("text").GetString());
            AssertEqual(1, imported.hitboxes.Count);
            AssertEqual(int.MaxValue, imported.hitboxes[0].frameIndex);
            AssertEqual(null, imported.hitboxes[0].hitboxType);
            AssertNear(-1.5f, imported.hitboxes[0].x, 0f);
            AssertNear(0f, imported.hitboxes[0].width, 0f);
            Assert(!imported.atlas.HasValue);
            AssertSequenceEqual(withBom, File.ReadAllBytes(path));

            string duplicatePath = Path.Combine(directory, "duplicates.efyv");
            File.WriteAllText(
                duplicatePath,
                "{\"assetType\":\"first\",\"assetType\":\"last\",\"properties\":{},\"hitboxes\":[]}",
                new UTF8Encoding(false));
            AssertEqual("last", FastImporter.ParseEfyvFile(duplicatePath).assetType);

            string casingPath = Path.Combine(directory, "case-sensitive.efyv");
            File.WriteAllText(casingPath, "{\"AssetType\":\"wrong-case\",\"properties\":{},\"hitboxes\":[]}", new UTF8Encoding(false));
            AssertEqual(null, FastImporter.ParseEfyvFile(casingPath).assetType);
        }

        private static unsafe void TestSaveExactRoundTripAndCanaries(string directory)
        {
            string path = Path.Combine(directory, "roundtrip.save");
            PlayerMetaSchema profile = CreatePatternedProfile();
            byte[] expectedPayload = SaveStructBytes(profile);
            byte[] expectedFile = SaveFileBytes(expectedPayload);
            int saveSize = sizeof(PlayerMetaSchema);
            AssertEqual(saveSize, expectedPayload.Length);

            SaveEnvelope envelope = new SaveEnvelope
            {
                Before = SaveCanaryBefore,
                Value = profile,
                After = SaveCanaryAfter
            };
            FastSaveEngine.SaveGame(path, ref envelope.Value);
            AssertEqual(SaveCanaryBefore, envelope.Before);
            AssertEqual(SaveCanaryAfter, envelope.After);
            AssertSequenceEqual(expectedPayload, SaveStructBytes(envelope.Value));
            AssertEqual((long)expectedFile.Length, new FileInfo(path).Length);
            AssertSequenceEqual(expectedFile, File.ReadAllBytes(path));

            SaveEnvelope loadedEnvelope = new SaveEnvelope
            {
                Before = SaveCanaryBefore,
                After = SaveCanaryAfter
            };
            Assert(FastSaveEngine.LoadGame(path, out loadedEnvelope.Value));
            AssertEqual(SaveCanaryBefore, loadedEnvelope.Before);
            AssertEqual(SaveCanaryAfter, loadedEnvelope.After);
            AssertSequenceEqual(expectedPayload, SaveStructBytes(loadedEnvelope.Value));

            loadedEnvelope.Value.TotalCoinsCollected = 123;
            AssertSequenceEqual(expectedFile, File.ReadAllBytes(path));
            AssertSequenceEqual(expectedPayload, SaveStructBytes(profile));

            byte[] oversizedOldFile = new byte[expectedFile.Length + 257];
            for (int i = 0; i < oversizedOldFile.Length; i++) oversizedOldFile[i] = 0xA5;
            File.WriteAllBytes(path, oversizedOldFile);
            FastSaveEngine.SaveGame(path, ref profile);
            AssertEqual((long)expectedFile.Length, new FileInfo(path).Length);
            AssertSequenceEqual(expectedFile, File.ReadAllBytes(path));
        }

        private static unsafe void TestSaveTruncationAndCorruptionRejection(string directory)
        {
            string path = Path.Combine(directory, "adversarial.save");
            PlayerMetaSchema profile = CreatePatternedProfile();
            byte[] payload = SaveStructBytes(profile);
            byte[] valid = SaveFileBytes(payload);
            byte[] defaultBytes = SaveStructBytes(PlayerMetaSchema.Default());
            int[] truncatedLengths = { 0, 1, 3, 4, 11, 12, 255, valid.Length / 2, valid.Length - 1 };
            for (int i = 0; i < truncatedLengths.Length; i++)
            {
                int length = truncatedLengths[i];
                byte[] truncated = new byte[length];
                Buffer.BlockCopy(valid, 0, truncated, 0, length);
                File.WriteAllBytes(path, truncated);
                SaveEnvelope result = NewPoisonedSaveEnvelope();
                Assert(!FastSaveEngine.LoadGame(path, out result.Value));
                AssertEqual(SaveCanaryBefore, result.Before);
                AssertEqual(SaveCanaryAfter, result.After);
                AssertSequenceEqual(defaultBytes, SaveStructBytes(result.Value));
                AssertSequenceEqual(truncated, File.ReadAllBytes(path));
            }

            File.Delete(path);
            SaveEnvelope missing = NewPoisonedSaveEnvelope();
            Assert(!FastSaveEngine.LoadGame(path, out missing.Value));
            AssertEqual(SaveCanaryBefore, missing.Before);
            AssertEqual(SaveCanaryAfter, missing.After);
            AssertSequenceEqual(defaultBytes, SaveStructBytes(missing.Value));
            Assert(!FastSaveEngine.LoadGame(null, out PlayerMetaSchema nullPath));
            AssertSequenceEqual(defaultBytes, SaveStructBytes(nullPath));
            Assert(!FastSaveEngine.LoadGame(directory, out PlayerMetaSchema directoryPath));
            AssertSequenceEqual(defaultBytes, SaveStructBytes(directoryPath));

            // The #19 envelope FLIPPED the old permissive legacy reads: raw
            // header-less struct dumps, arbitrary exact-size bytes, trailing
            // garbage, wrong magic, wrong version, and any flipped payload bit
            // (CRC) are all rejected and restore the default profile.
            byte[] legacyRaw = payload; // pre-envelope format: payload only
            AssertSaveRejected(path, legacyRaw, defaultBytes);

            byte[] arbitrary = new byte[valid.Length];
            for (int i = 0; i < arbitrary.Length; i++) arbitrary[i] = (byte)(i * 197 + 31);
            AssertSaveRejected(path, arbitrary, defaultBytes);

            byte[] withTrailingCanary = new byte[valid.Length + 64];
            Buffer.BlockCopy(valid, 0, withTrailingCanary, 0, valid.Length);
            for (int i = valid.Length; i < withTrailingCanary.Length; i++) withTrailingCanary[i] = 0xE7;
            AssertSaveRejected(path, withTrailingCanary, defaultBytes);

            byte[] wrongMagic = (byte[])valid.Clone();
            wrongMagic[0] ^= 0x01;
            AssertSaveRejected(path, wrongMagic, defaultBytes);

            byte[] wrongVersion = (byte[])valid.Clone();
            wrongVersion[4] ^= 0x01;
            AssertSaveRejected(path, wrongVersion, defaultBytes);

            byte[] wrongChecksum = (byte[])valid.Clone();
            wrongChecksum[8] ^= 0x01;
            AssertSaveRejected(path, wrongChecksum, defaultBytes);

            Random random = new Random(0x5AFEC2C);
            for (int i = 0; i < 8; i++)
            {
                byte[] corruptPayload = (byte[])valid.Clone();
                int flippedIndex = 12 + random.Next(payload.Length);
                corruptPayload[flippedIndex] ^= (byte)(1 << random.Next(8));
                AssertSaveRejected(path, corruptPayload, defaultBytes);
            }

            // The pristine file still loads after the rejection gauntlet.
            File.WriteAllBytes(path, valid);
            Assert(FastSaveEngine.LoadGame(path, out PlayerMetaSchema restored));
            AssertSequenceEqual(payload, SaveStructBytes(restored));
        }

        private static void AssertSaveRejected(string path, byte[] fileBytes, byte[] defaultBytes)
        {
            File.WriteAllBytes(path, fileBytes);
            SaveEnvelope result = NewPoisonedSaveEnvelope();
            Assert(!FastSaveEngine.LoadGame(path, out result.Value));
            AssertEqual(SaveCanaryBefore, result.Before);
            AssertEqual(SaveCanaryAfter, result.After);
            AssertSequenceEqual(defaultBytes, SaveStructBytes(result.Value));
            AssertSequenceEqual(fileBytes, File.ReadAllBytes(path));
        }

        // Independent model of the #19 save envelope (CRC via the bitwise
        // reference implementation, not the product FastCrc32).
        private static byte[] SaveFileBytes(byte[] payload)
        {
            byte[] file = new byte[BackendConfig.Save.HeaderSizeBytes + payload.Length];
            BitConverter.GetBytes(BackendConfig.Save.MagicNumber).CopyTo(file, BackendConfig.Save.MagicOffset);
            BitConverter.GetBytes((uint)BackendConfig.Save.FormatVersion).CopyTo(file, BackendConfig.Save.VersionOffset);
            BitConverter.GetBytes(ComputeStandardPngCrc(payload)).CopyTo(file, BackendConfig.Save.ChecksumOffset);
            payload.CopyTo(file, BackendConfig.Save.HeaderSizeBytes);
            return file;
        }

        private static unsafe void TestSaveFileAccessFailures(string directory)
        {
            string path = Path.Combine(directory, "locked.save");
            PlayerMetaSchema profile = CreatePatternedProfile();
            byte[] baseline = SaveStructBytes(profile);
            File.WriteAllBytes(path, baseline);

            using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                AssertThrows<IOException>(() => FastSaveEngine.SaveGame(path, ref profile));
                SaveEnvelope loadResult = NewPoisonedSaveEnvelope();
                AssertThrows<IOException>(() => FastSaveEngine.LoadGame(path, out loadResult.Value));
                AssertEqual(SaveCanaryBefore, loadResult.Before);
                AssertEqual(SaveCanaryAfter, loadResult.After);
                AssertSequenceEqual(SaveStructBytes(PlayerMetaSchema.Default()), SaveStructBytes(loadResult.Value));
            }
            AssertSequenceEqual(baseline, File.ReadAllBytes(path));

            AssertThrows<ArgumentException>(() => FastSaveEngine.SaveGame(null, ref profile));
            AssertThrows<ArgumentException>(() => FastSaveEngine.SaveGame(string.Empty, ref profile));
            AssertSequenceEqual(baseline, File.ReadAllBytes(path));
        }

        private static unsafe PlayerMetaSchema CreatePatternedProfile()
        {
            PlayerMetaSchema profile = PlayerMetaSchema.Default();
            profile.TotalCoinsCollected = unchecked((int)0x6A5B4C3D);
            for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
            {
                profile.LegacyStats.SetInt(slot, unchecked((int)(0x13579BDFu + (uint)slot * 0x1020304u)));
                profile.LegacyAchievements.SetInt(slot, unchecked((int)(0xFEDCBA98u - (uint)slot * 0x01010101u)));
            }
            for (int toonIndex = 0; toonIndex < PlayerMetaSchema.MaxToons; toonIndex++)
            {
                FastSchemaBlock toon = default;
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                {
                    toon.SetInt(slot, unchecked((toonIndex + 1) * 1000003 + slot * 7919));
                }
                Assert(profile.TrySetToonBlock(toonIndex, in toon));
            }
            return profile;
        }

        private static unsafe byte[] SaveStructBytes(PlayerMetaSchema value)
        {
            PlayerMetaSchema* pointer = &value;
            return new ReadOnlySpan<byte>(pointer, sizeof(PlayerMetaSchema)).ToArray();
        }

        private static SaveEnvelope NewPoisonedSaveEnvelope()
        {
            return new SaveEnvelope
            {
                Before = SaveCanaryBefore,
                Value = CreatePatternedProfile(),
                After = SaveCanaryAfter
            };
        }

        private static void AssertDefaultImport(EFYVJsonFormat value)
        {
            AssertEqual(null, value.assetType);
            AssertEqual(null, value.baseAssetType);
            AssertEqual(null, value.properties);
            AssertEqual(null, value.hitboxes);
            Assert(!value.atlas.HasValue);
            Assert(!value.documentVersion.HasValue);
            AssertEqual(BackendConfig.Exporter.LegacyDocumentVersion, value.EffectiveDocumentVersion);
        }

        private static void TestExportAdversarial()
        {
            string directory = Path.Combine(Path.GetTempPath(), "EFYV-ExportAdversarial-" + Guid.NewGuid().ToString("N"));
            try
            {
                TestExportArgumentGuards(directory);
                TestExportMetadataGuards(directory);
                TestExportPrePublicationAtomicity(directory);
                TestAtlasPackingGuardsAndReferenceModel();
                TestPngEncoderAdversarialContract();
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void TestExportArgumentGuards(string root)
        {
            PackedRgba[] pixel = { new PackedRgba(0x44332211u) };
            Dictionary<string, object> properties = ExportProperties("Guarded");
            List<HitboxJson> hitboxes = new List<HitboxJson>();

            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(null, "Type", properties, hitboxes, pixel, 1, 1));
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(string.Empty, "Type", properties, hitboxes, pixel, 1, 1));
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook("   ", "Type", properties, hitboxes, pixel, 1, 1));
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(root, null, properties, hitboxes, pixel, 1, 1));
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(root, "", properties, hitboxes, pixel, 1, 1));
            AssertThrows<ArgumentNullException>(() => FastExporter.PushToUnityLiveHook(root, "Type", null, hitboxes, pixel, 1, 1));
            AssertThrows<ArgumentNullException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, null, pixel, 1, 1));
            AssertThrows<ArgumentNullException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, (PackedRgba[])null, 1, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, pixel, 0, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, pixel, 1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, pixel, -1, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, pixel, 1, -1));
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, new PackedRgba[2], 1, 1));
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, new byte[1], 1, 1));
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, new ulong[1], 1, 1));
            AssertThrows<OverflowException>(() => FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, pixel, int.MaxValue, 2));
            AssertNoExportFiles(root);

            string[] hostileNames =
            {
                "", " ", ".", "..", "../escape", "..\\escape", "nested/file", "nested\\file",
                "/absolute", "CON", "con.txt", "PRN", "AUX.data", "NUL", "COM1.foo", "LPT9", "trailing.", "trailing "
            };
            for (int i = 0; i < hostileNames.Length; i++)
            {
                Dictionary<string, object> hostile = ExportProperties(hostileNames[i]);
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(root, "Type", hostile, hitboxes, pixel, 1, 1));
                AssertNoExportFiles(root);
            }
            Assert(!File.Exists(Path.GetFullPath(Path.Combine(root, "..", "escape.png"))));
            Assert(!File.Exists(Path.GetFullPath(Path.Combine(root, "..", "escape" + BackendConfig.Exporter.EfyvExtension))));

            string fileAsRoot = Path.Combine(Path.GetTempPath(), "EFYV-RootFile-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(fileAsRoot, "do not replace");
            try
            {
                AssertThrows<IOException>(() => FastExporter.PushToUnityLiveHook(fileAsRoot, "Type", properties, hitboxes, pixel, 1, 1));
                AssertEqual("do not replace", File.ReadAllText(fileAsRoot));
            }
            finally
            {
                File.Delete(fileAsRoot);
            }

            // No identity property -> REJECT (#36). The old fallback minted a
            // "<Type>_Export" stem that aliased every unnamed export of a type.
            string fallbackRoot = Path.Combine(root, "fallback");
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(
                fallbackRoot,
                "SafeType",
                new Dictionary<string, object>(),
                hitboxes,
                pixel,
                1,
                1));
            AssertNoExportFiles(fallbackRoot);

            string numericRoot = Path.Combine(root, "numeric-name");
            Dictionary<string, object> numericName = ExportProperties(123456789);
            FastExporter.PushToUnityLiveHook(numericRoot, "Type", numericName, hitboxes, pixel, 1, 1);
            Assert(File.Exists(Path.Combine(numericRoot, "123456789.png")));
            Assert(File.Exists(Path.Combine(numericRoot, "123456789" + BackendConfig.Exporter.EfyvExtension)));
        }

        private static void TestExportMetadataGuards(string root)
        {
            string invalidRoot = Path.Combine(root, "invalid-metadata");
            PackedRgba[] pixels = new PackedRgba[6];
            Dictionary<string, object> properties = ExportProperties("Metadata");
            List<HitboxJson> hitboxes = new List<HitboxJson>();

            AtlasMetadataJson metadata = Metadata(3, 2, 1, 1);
            metadata.formatVersion = 0;
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentOutOfRangeException));
            metadata = Metadata(3, 2, 1, 1);
            metadata.formatVersion = BackendConfig.Exporter.CurrentFormatVersion + 1;
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentOutOfRangeException));

            metadata = Metadata(3, 2, 1, 1);
            metadata.frameWidth = 0;
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentOutOfRangeException));
            metadata = Metadata(3, 2, 1, 1);
            metadata.frameHeight = -1;
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentOutOfRangeException));
            metadata = Metadata(3, 2, 1, 1);
            metadata.atlasWidth = 4;
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentException));
            metadata = Metadata(3, 2, 1, 1);
            metadata.atlasHeight = 3;
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentException));
            metadata = Metadata(3, 2, 2, 1);
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentException));
            metadata = Metadata(3, 2, 1, 1);
            metadata.animations = null;
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentNullException));

            string[] badNames = { null, "", " ", "\t\r\n" };
            for (int i = 0; i < badNames.Length; i++)
            {
                metadata = Metadata(3, 2, 1, 1);
                metadata.animations[0] = Animation(badNames[i], 1, 0, 1);
                AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentException));
            }

            int[] badFps = { 0, -1, int.MinValue };
            for (int i = 0; i < badFps.Length; i++)
            {
                metadata = Metadata(3, 2, 1, 1);
                metadata.animations[0] = Animation("BadFps", badFps[i], 0, 1);
                AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentOutOfRangeException));
            }

            metadata = Metadata(3, 2, 1, 1);
            metadata.animations[0] = Animation("NegativeStart", 1, -1, 1);
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentOutOfRangeException));
            metadata = Metadata(3, 2, 1, 1);
            metadata.animations[0] = Animation("ZeroCount", 1, 0, 0);
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentOutOfRangeException));
            metadata = Metadata(3, 2, 1, 1);
            metadata.animations[0] = Animation("NegativeCount", 1, 0, -1);
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentOutOfRangeException));
            metadata = Metadata(3, 2, 1, 1);
            metadata.animations[0] = Animation("PastCapacity", 1, 5, 2);
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentException));
            // The shared validator uses long math (#16b): an int.MaxValue start
            // frame is plain past-capacity data, not an arithmetic overflow.
            metadata = Metadata(3, 2, 1, 1);
            metadata.animations[0] = Animation("Overflow", 1, int.MaxValue, 1);
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentException));

            metadata = Metadata(3, 2, 1, 1);
            metadata.animations = new List<AnimationMetadataJson>
            {
                Animation("First", 1, 1, 3),
                Animation("Overlap", 1, 3, 1)
            };
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentException));
            metadata.animations = new List<AnimationMetadataJson>
            {
                Animation("Later", 1, 4, 1),
                Animation("OutOfOrder", 1, 1, 1)
            };
            AssertInvalidMetadata(invalidRoot, properties, hitboxes, pixels, metadata, typeof(ArgumentException));

            string emptyRoot = Path.Combine(root, "empty-animations");
            metadata = Metadata(3, 2, 1, 1);
            metadata.animations.Clear();
            FastExporter.PushToUnityLiveHook(emptyRoot, "Type", properties, hitboxes, pixels, 3, 2, metadata);
            EFYVJsonFormat emptyImported = FastImporter.ParseEfyvFile(
                Path.Combine(emptyRoot, "Metadata" + BackendConfig.Exporter.EfyvExtension));
            Assert(emptyImported.atlas.HasValue);
            AssertEqual(0, emptyImported.atlas.Value.animations.Count);

            string gapsRoot = Path.Combine(root, "animation-gaps");
            metadata = Metadata(3, 2, 1, 1);
            metadata.animations = new List<AnimationMetadataJson>
            {
                Animation("Start", 1, 0, 1),
                Animation("Gap", int.MaxValue, 4, 2)
            };
            FastExporter.PushToUnityLiveHook(gapsRoot, "Type", properties, hitboxes, pixels, 3, 2, metadata);
            EFYVJsonFormat gapsImported = FastImporter.ParseEfyvFile(
                Path.Combine(gapsRoot, "Metadata" + BackendConfig.Exporter.EfyvExtension));
            AssertEqual(2, gapsImported.atlas.Value.animations.Count);
            AssertEqual(int.MaxValue, gapsImported.atlas.Value.animations[1].fps);
        }

        private static void TestExportPrePublicationAtomicity(string root)
        {
            string atomicRoot = Path.Combine(root, "atomic");
            Dictionary<string, object> properties = ExportProperties("AtomicAsset");
            List<HitboxJson> hitboxes = new List<HitboxJson>
            {
                new HitboxJson { frameIndex = 0, hitboxType = "Hurtbox", x = 0f, y = 0f, width = 1f, height = 1f }
            };
            PackedRgba[] baselinePixels =
            {
                new PackedRgba(0xFF010203u), new PackedRgba(0xFF040506u),
                new PackedRgba(0xFF070809u), new PackedRgba(0xFF0A0B0Cu)
            };
            FastExporter.PushToUnityLiveHook(atomicRoot, "Type", properties, hitboxes, baselinePixels, 2, 2);
            string pngPath = Path.Combine(atomicRoot, "AtomicAsset.png");
            string jsonPath = Path.Combine(atomicRoot, "AtomicAsset" + BackendConfig.Exporter.EfyvExtension);
            byte[] baselinePng = File.ReadAllBytes(pngPath);
            byte[] baselineJson = File.ReadAllBytes(jsonPath);
            PackedRgba[] changedPixels =
            {
                new PackedRgba(0xFFFFFFFFu), new PackedRgba(0xFFFFFFFFu),
                new PackedRgba(0xFFFFFFFFu), new PackedRgba(0xFFFFFFFFu)
            };

            CyclicPayload cycle = new CyclicPayload();
            cycle.Self = cycle;
            Dictionary<string, object> cyclicProperties = ExportProperties("AtomicAsset");
            cyclicProperties["cycle"] = cycle;
            AssertThrows<JsonException>(() => FastExporter.PushToUnityLiveHook(atomicRoot, "Type", cyclicProperties, hitboxes, changedPixels, 2, 2));
            AssertSequenceEqual(baselinePng, File.ReadAllBytes(pngPath));
            AssertSequenceEqual(baselineJson, File.ReadAllBytes(jsonPath));
            AssertOnlyPublishedPair(atomicRoot);

            Dictionary<string, object> nonFiniteProperty = ExportProperties("AtomicAsset");
            nonFiniteProperty["notFinite"] = double.NaN;
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(atomicRoot, "Type", nonFiniteProperty, hitboxes, changedPixels, 2, 2));
            AssertSequenceEqual(baselinePng, File.ReadAllBytes(pngPath));
            AssertSequenceEqual(baselineJson, File.ReadAllBytes(jsonPath));
            AssertOnlyPublishedPair(atomicRoot);

            List<HitboxJson> invalidHitboxes = new List<HitboxJson>(hitboxes)
            {
                new HitboxJson { frameIndex = 1, hitboxType = "Bad", x = float.PositiveInfinity, y = 0, width = 1, height = 1 }
            };
            AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(atomicRoot, "Type", properties, invalidHitboxes, changedPixels, 2, 2));
            AssertSequenceEqual(baselinePng, File.ReadAllBytes(pngPath));
            AssertSequenceEqual(baselineJson, File.ReadAllBytes(jsonPath));
            AssertOnlyPublishedPair(atomicRoot);

            ThrowingString throwingName = new ThrowingString();
            Dictionary<string, object> throwingProperties = ExportProperties(throwingName);
            AssertThrows<InvalidOperationException>(() => FastExporter.PushToUnityLiveHook(atomicRoot, "Type", throwingProperties, hitboxes, changedPixels, 2, 2));
            AssertSequenceEqual(baselinePng, File.ReadAllBytes(pngPath));
            AssertSequenceEqual(baselineJson, File.ReadAllBytes(jsonPath));
            AssertOnlyPublishedPair(atomicRoot);

            AssertSequenceEqual(
                new uint[] { 0xFF010203u, 0xFF040506u, 0xFF070809u, 0xFF0A0B0Cu },
                RgbaValues(baselinePixels));
            AssertSequenceEqual(
                new uint[] { 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu },
                RgbaValues(changedPixels));
        }

        private static void TestAtlasPackingGuardsAndReferenceModel()
        {
            PackedRgba sentinel = new PackedRgba(0xA5A5A5A5u);
            PackedRgba[] destination = new PackedRgba[12];
            for (int i = 0; i < destination.Length; i++) destination[i] = sentinel;
            PackedRgba[] source = { new PackedRgba(1), new PackedRgba(2), new PackedRgba(3), new PackedRgba(4) };
            uint[] destinationBefore = RgbaValues(destination);
            uint[] sourceBefore = RgbaValues(source);

            AssertThrows<ArgumentNullException>(() => FastExporter.PackFramesToAtlas((PackedRgba[])null, 4, source, 2, 2, 0, 0));
            AssertThrows<ArgumentNullException>(() => FastExporter.PackFramesToAtlas(destination, 4, (PackedRgba[])null, 2, 2, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PackFramesToAtlas(destination, 0, source, 2, 2, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PackFramesToAtlas(destination, 4, source, 0, 2, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PackFramesToAtlas(destination, 4, source, 2, 0, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PackFramesToAtlas(destination, 4, source, 2, 2, -1, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => FastExporter.PackFramesToAtlas(destination, 4, source, 2, 2, 0, -1));
            AssertThrows<ArgumentException>(() => FastExporter.PackFramesToAtlas(Array.Empty<PackedRgba>(), 4, source, 2, 2, 0, 0));
            AssertThrows<ArgumentException>(() => FastExporter.PackFramesToAtlas(new PackedRgba[11], 4, source, 2, 2, 0, 0));
            AssertThrows<ArgumentException>(() => FastExporter.PackFramesToAtlas(destination, 4, new PackedRgba[3], 2, 2, 0, 0));
            AssertThrows<ArgumentException>(() => FastExporter.PackFramesToAtlas(destination, 4, source, 2, 2, 3, 0));
            AssertThrows<ArgumentException>(() => FastExporter.PackFramesToAtlas(destination, 4, source, 2, 2, 0, 2));
            AssertThrows<OverflowException>(() => FastExporter.PackFramesToAtlas(destination, 4, source, int.MaxValue, 2, 0, 0));
            AssertThrows<OverflowException>(() => FastExporter.PackFramesToAtlas(destination, 4, source, 1, 1, int.MaxValue, 0));
            AssertSequenceEqual(destinationBefore, RgbaValues(destination));
            AssertSequenceEqual(sourceBefore, RgbaValues(source));

            Random random = new Random(0x5A17C0DE);
            for (int iteration = 0; iteration < 300; iteration++)
            {
                int atlasWidth = random.Next(1, 13);
                int atlasHeight = random.Next(1, 10);
                int frameWidth = random.Next(1, atlasWidth + 1);
                int frameHeight = random.Next(1, atlasHeight + 1);
                int destinationX = random.Next(0, atlasWidth - frameWidth + 1);
                int destinationY = random.Next(0, atlasHeight - frameHeight + 1);
                PackedRgba[] actual = new PackedRgba[atlasWidth * atlasHeight];
                PackedRgba[] expected = new PackedRgba[actual.Length];
                for (int i = 0; i < actual.Length; i++)
                {
                    uint value = unchecked((uint)random.Next() * 2654435761u);
                    actual[i] = new PackedRgba(value);
                    expected[i] = new PackedRgba(value);
                }
                int required = frameWidth * frameHeight;
                PackedRgba[] frame = new PackedRgba[required + random.Next(0, 4)];
                for (int i = 0; i < frame.Length; i++) frame[i] = new PackedRgba(unchecked((uint)(iteration * 1009 + i + 1)));
                uint[] frameSnapshot = RgbaValues(frame);

                for (int y = 0; y < frameHeight; y++)
                {
                    for (int x = 0; x < frameWidth; x++)
                    {
                        expected[(destinationY + y) * atlasWidth + destinationX + x] = frame[y * frameWidth + x];
                    }
                }
                FastExporter.PackFramesToAtlas(actual, atlasWidth, frame, frameWidth, frameHeight, destinationX, destinationY);
                AssertSequenceEqual(RgbaValues(expected), RgbaValues(actual));
                AssertSequenceEqual(frameSnapshot, RgbaValues(frame));
            }
        }

        private static void TestPngEncoderAdversarialContract()
        {
            PackedRgba[] pixel = { new PackedRgba(0x44332211u) };
            AssertThrows<ArgumentNullException>(() => FastPngEncoder.Write<PackedRgba>(null, pixel, 1, 1));
            using (MemoryStream readOnly = new MemoryStream(new byte[1], false))
            {
                AssertThrows<ArgumentException>(() => FastPngEncoder.Write(readOnly, pixel, 1, 1));
            }

            using (MemoryStream output = new MemoryStream())
            {
                AssertThrows<ArgumentNullException>(() => FastPngEncoder.Write<PackedRgba>(output, null, 1, 1));
                AssertThrows<ArgumentOutOfRangeException>(() => FastPngEncoder.Write(output, pixel, 0, 1));
                AssertThrows<ArgumentOutOfRangeException>(() => FastPngEncoder.Write(output, pixel, 1, 0));
                AssertThrows<ArgumentException>(() => FastPngEncoder.Write(output, new PackedRgba[2], 1, 1));
                AssertThrows<ArgumentException>(() => FastPngEncoder.Write(output, new byte[1], 1, 1));
                AssertThrows<ArgumentException>(() => FastPngEncoder.Write(output, new ulong[1], 1, 1));
                AssertThrows<OverflowException>(() => FastPngEncoder.Write(output, pixel, int.MaxValue, 2));
                AssertEqual(0L, output.Length);
            }

            PackedRgba[] pixels = new PackedRgba[257];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new PackedRgba(unchecked((uint)i * 0x01030507u));
            uint[] inputSnapshot = RgbaValues(pixels);
            byte[] first;
            using (MemoryStream stream = new MemoryStream())
            {
                FastPngEncoder.Write(stream, pixels, 257, 1);
                Assert(stream.CanWrite);
                AssertEqual(stream.Length, stream.Position);
                first = stream.ToArray();
            }
            using (MemoryStream stream = new MemoryStream())
            {
                FastPngEncoder.Write(stream, pixels, 257, 1);
                AssertSequenceEqual(first, stream.ToArray());
            }
            AssertSequenceEqual(inputSnapshot, RgbaValues(pixels));
        }

        private static void AssertInvalidMetadata(
            string root,
            Dictionary<string, object> properties,
            List<HitboxJson> hitboxes,
            PackedRgba[] pixels,
            AtlasMetadataJson metadata,
            Type expectedException)
        {
            assertions++;
            try
            {
                FastExporter.PushToUnityLiveHook(root, "Type", properties, hitboxes, pixels, 3, 2, metadata);
            }
            catch (Exception exception)
            {
                if (!expectedException.IsAssignableFrom(exception.GetType()))
                {
                    throw new InvalidOperationException(
                        "Expected " + expectedException.Name + ", got " + exception.GetType().Name + ".",
                        exception);
                }
                AssertNoExportFiles(root);
                return;
            }
            throw new InvalidOperationException("Expected " + expectedException.Name + ".");
        }

        private static Dictionary<string, object> ExportProperties(object name)
        {
            return new Dictionary<string, object>
            {
                [BackendConfig.Exporter.FieldEntityName] = name,
                ["canary"] = "unchanged"
            };
        }

        private static AtlasMetadataJson Metadata(int atlasWidth, int atlasHeight, int frameWidth, int frameHeight)
        {
            return new AtlasMetadataJson
            {
                formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                frameWidth = frameWidth,
                frameHeight = frameHeight,
                atlasWidth = atlasWidth,
                atlasHeight = atlasHeight,
                animations = new List<AnimationMetadataJson>
                {
                    Animation("Valid", 1, 0, 1)
                }
            };
        }

        private static AnimationMetadataJson Animation(string name, int fps, int startFrame, int frameCount)
        {
            return new AnimationMetadataJson
            {
                name = name,
                fps = fps,
                startFrame = startFrame,
                frameCount = frameCount
            };
        }

        private static void AssertNoExportFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Assert(true);
                return;
            }
            AssertEqual(0, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
        }

        private static void AssertOnlyPublishedPair(string directory)
        {
            string[] files = Directory.GetFiles(directory);
            AssertEqual(2, files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                Assert(!files[i].EndsWith(BackendConfig.Exporter.TemporaryExtension, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static uint[] RgbaValues(PackedRgba[] pixels)
        {
            uint[] values = new uint[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) values[i] = pixels[i].Rgba;
            return values;
        }

        private struct SaveEnvelope
        {
            public ulong Before;
            public PlayerMetaSchema Value;
            public ulong After;
        }

        private sealed class CyclicPayload
        {
            public CyclicPayload Self { get; set; }
        }

        private sealed class ThrowingString
        {
            public override string ToString()
            {
                throw new InvalidOperationException("Intentional conversion failure.");
            }
        }
    }
}
