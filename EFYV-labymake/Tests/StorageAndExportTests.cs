using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.IO;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    private static void TestPathPolicyAttackCorpus()
    {
        string[] safe = { "Hero", "enemy_01", "A-B", "name.with.dot", "COM10", "LPT0" };
        foreach (string value in safe) Require(DesignerPathPolicy.IsSafeFileStem(value));

        string[] unsafeNames =
        {
            null, "", " ", ".", "..", "../escape", "..\\escape", "folder/file",
            "folder\\file", "name.", "name ", "CON", "con.txt", "PRN", "AUX", "NUL",
            "COM1", "com9.json", "LPT1", "lpt9.txt", "bad:name", "bad*name", "bad?name"
        };
        foreach (string value in unsafeNames) Require(!DesignerPathPolicy.IsSafeFileStem(value));

        string root = NewTemporaryDirectory();
        try
        {
            string child = DesignerPathPolicy.GetContainedPath(root, "child.dat");
            Require(Path.GetDirectoryName(child) == Path.GetFullPath(root));
            RequireThrows<ArgumentException>(() => DesignerPathPolicy.GetContainedPath(null, "x"));
            RequireThrows<ArgumentException>(() => DesignerPathPolicy.GetContainedPath(root, null));
            RequireThrows<ArgumentException>(() => DesignerPathPolicy.GetContainedPath(root, "../escape"));
            RequireThrows<ArgumentException>(() => DesignerPathPolicy.GetContainedPath(root, "nested/file"));
            Require(DesignerPathPolicy.GetContainedPath(root, Path.GetFullPath(child)) == child);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestAssetBankRoundTripAndCorruption()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var bank = new AssetBankManager(root);
            var alpha = new SubElement("Alpha", 2, 2, new uint[] { 1, 2, 3, 4 });
            var zulu = new SubElement("Zulu", 1, 2, new uint[] { 5, 6 });
            bank.SaveSubElement(zulu);
            bank.SaveSubElement(alpha);
            bank.SaveSubElement(new SubElement("Alpha", 2, 2, new uint[] { 9, 8, 7, 6 }));

            List<SubElement> loaded = bank.LoadAllSubElements();
            Require(loaded.Count == 2);
            Require(loaded[0].Name == "Alpha" && loaded[1].Name == "Zulu");
            Require(loaded[0].Pixels[0] == 9 && loaded[0].Pixels[3] == 6);
            loaded[0].Pixels[0] = 100;
            Require(bank.LoadAllSubElements()[0].Pixels[0] == 9);

            int failures = 0;
            var failedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bank.LoadFailed += (path, exception) =>
            {
                failures++;
                Require(exception != null);
                failedPaths.Add(Path.GetFileName(path));
            };
            File.WriteAllBytes(Path.Combine(root, "00-empty" + Config.Export.ExtensionEfyvSub), Array.Empty<byte>());
            StorageWriteCorruptSubElement(
                Path.Combine(root, "01-version" + Config.Export.ExtensionEfyvSub),
                Config.Persistence.SubElementFormatVersion + 1,
                "BadVersion",
                1,
                1,
                1,
                new uint[] { 1 });
            StorageWriteCorruptSubElement(
                Path.Combine(root, "02-count" + Config.Export.ExtensionEfyvSub),
                Config.Persistence.SubElementFormatVersion,
                "BadCount",
                2,
                2,
                3,
                new uint[] { 1, 2, 3 });
            StorageWriteCorruptSubElement(
                Path.Combine(root, "03-name" + Config.Export.ExtensionEfyvSub),
                Config.Persistence.SubElementFormatVersion,
                "../escape",
                1,
                1,
                1,
                new uint[] { 1 });

            loaded = bank.LoadAllSubElements();
            Require(loaded.Count == 2 && failures == 4 && failedPaths.Count == 4);
            RequireThrows<ArgumentException>(() =>
                bank.SaveSubElement(new SubElement("../escape", 1, 1, new uint[] { 0 })));

            var frame = new Frame(4, 3);
            for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
                frame.Layers[0].SetPixel(
                    x,
                    y,
                    new PixelColor { Rgba = Pack((byte)(1 + y * frame.Width + x), 0, 0, 255) });
            SubElement crop = bank.ExtractFromCanvas(frame, 1, 1, 2, 2, "Crop");
            Require((byte)crop.Pixels[0] == 6 && (byte)crop.Pixels[1] == 7);
            Require((byte)crop.Pixels[2] == 10 && (byte)crop.Pixels[3] == 11);
            RequireThrows<ArgumentOutOfRangeException>(() => bank.ExtractFromCanvas(frame, 3, 2, 2, 2, "Bad"));
            RequireThrows<ArgumentException>(() => bank.ExtractFromCanvas(frame, 0, 0, 1, 1, "CON"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestPersistenceMalformedDocumentCorpus()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
            EFYVProject project = CreateValidProject(root, 1);
            string path = persistence.SaveProject("Corpus", project, CancellationToken.None);
            JsonObject baseline = JsonNode.Parse(File.ReadAllText(path)).AsObject();

            Action<JsonObject>[] mutations =
            {
                document => document["formatVersion"] = Config.Persistence.ProjectFormatVersion + 1,
                document => document["targetAssetType"] = "UnknownData",
                document => document["canvasWidth"] = 0,
                document => document["assetProperties"] = null,
                document => document["animations"] = null,
                document => StorageAnimation(document)["fps"] = 0,
                document => StorageFrame(document)["layers"] = null,
                document => StorageLayer(document)["rgbaBytes"] = Convert.ToBase64String(new byte[3]),
                document =>
                {
                    JsonArray hitboxes = StorageFrame(document)["hitboxes"].AsArray();
                    hitboxes.Add(hitboxes[0].DeepClone());
                },
                document => StorageHitbox(document)["x"] = 1000f
            };

            for (int index = 0; index < mutations.Length; index++)
            {
                JsonObject malformed = baseline.DeepClone().AsObject();
                mutations[index](malformed);
                File.WriteAllText(path, malformed.ToJsonString());
                RequireThrows<InvalidDataException>(() => persistence.LoadProject("Corpus"));
            }

            File.WriteAllText(path, "{not-json");
            RequireThrows<System.Text.Json.JsonException>(() => persistence.LoadProject("Corpus"));
            File.WriteAllText(path, "null");
            RequireThrows<InvalidDataException>(() => persistence.LoadProject("Corpus"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestPersistenceSnapshotsAndAtomicCancellation()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
            EFYVProject project = CreateValidProject(root, 1);
            Layer layer = project.Animations[0].Frames[0].Layers[0];
            layer.SetPixel(1, 1, Color(10, 20, 30, 255));
            project.AssetProperties[SharedConfig.BaseSpeedField] = 4f;
            ProjectPersistenceSnapshot snapshot = ProjectPersistenceSnapshot.Capture(project);

            layer.SetPixel(1, 1, Color(90, 80, 70, 255));
            project.AssetProperties[SharedConfig.BaseSpeedField] = 9f;
            persistence.SaveProject("Snapshot", snapshot, CancellationToken.None);
            EFYVProject restored = persistence.LoadProject("Snapshot");
            Require(restored.Animations[0].Frames[0].Layers[0].GetPixel(1, 1).R == 10);
            Require(Convert.ToSingle(restored.AssetProperties[SharedConfig.BaseSpeedField]) == 4f);

            string path = persistence.GetProjectPath("Snapshot");
            byte[] committed = File.ReadAllBytes(path);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                RequireThrows<OperationCanceledException>(() =>
                    persistence.SaveProject("Snapshot", project, cancellation.Token));
            }
            byte[] afterCancellation = File.ReadAllBytes(path);
            Require(committed.Length == afterCancellation.Length);
            for (int index = 0; index < committed.Length; index++)
                Require(committed[index] == afterCancellation[index]);
            Require(Directory.GetFiles(root).Length == 1);

            RequireThrows<ArgumentNullException>(() => ProjectPersistenceSnapshot.Capture(null));
            RequireThrows<ArgumentNullException>(() =>
                persistence.SaveProject("Null", (ProjectPersistenceSnapshot)null, CancellationToken.None));

            persistence.SaveAutosave("Snapshot", snapshot, CancellationToken.None);
            Require(persistence.AutosaveExists("Snapshot"));
            persistence.DeleteAutosave("Snapshot");
            persistence.DeleteAutosave("Snapshot");
            Require(!persistence.AutosaveExists("Snapshot"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestExportAtlasPixelsAndAdversarialPublication()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            EFYVProject project = CreateValidProject(root, 1);
            project.CanvasWidth = 2;
            project.CanvasHeight = 2;
            project.Animations.Clear();
            var animation = new AnimationState("Exact", 7);
            var first = new Frame(2, 2, 0);
            first.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = default;
            first.Layers[0].Pixels[0].Rgba = Pack(1, 2, 3, 4);
            first.Layers[0].Pixels[1].Rgba = Pack(5, 6, 7, 8);
            first.Layers[0].Pixels[2].Rgba = Pack(9, 10, 11, 12);
            first.Layers[0].Pixels[3].Rgba = Pack(13, 14, 15, 16);
            var second = new Frame(2, 2, 1);
            second.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = default;
            second.Layers[0].Pixels[0].Rgba = Pack(21, 22, 23, 24);
            second.Layers[0].Pixels[1].Rgba = Pack(25, 26, 27, 28);
            second.Layers[0].Pixels[2].Rgba = Pack(29, 30, 31, 32);
            second.Layers[0].Pixels[3].Rgba = Pack(33, 34, 35, 36);
            animation.Frames.Add(first);
            animation.Frames.Add(second);
            project.Animations.Add(animation);

            var validator = new ProjectValidator(new AssetSchemaService());
            var engine = new ExportEngine(validator);
            ProjectValidationResult exactValidation = validator.Validate(
                project,
                ProjectValidationScope.Export);
            if (!exactValidation.IsValid)
            {
                var diagnostic = new StringBuilder();
                foreach (ProjectIssue issue in exactValidation.Issues)
                    diagnostic.Append(issue.Code).Append(':').Append(issue.Subject).Append(';');
                throw new InvalidOperationException(diagnostic.ToString());
            }
            ExportResult result = engine.Export(project, CancellationToken.None);
            StoragePng png = StorageReadPng(result.ImagePath);
            Require(png.Width == 4 && png.Height == 2);
            uint[] expected =
            {
                first.Layers[0].Pixels[0].Rgba, first.Layers[0].Pixels[1].Rgba,
                second.Layers[0].Pixels[0].Rgba, second.Layers[0].Pixels[1].Rgba,
                first.Layers[0].Pixels[2].Rgba, first.Layers[0].Pixels[3].Rgba,
                second.Layers[0].Pixels[2].Rgba, second.Layers[0].Pixels[3].Rgba
            };
            for (int index = 0; index < expected.Length; index++)
                Require(StorageReadRawPixel(png.Raw, png.Width, index) == expected[index]);

            string transactionRoot = Path.Combine(root, "transaction");
            string staging = Path.Combine(transactionRoot, "staging");
            string published = Path.Combine(transactionRoot, "published");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(published);
            string stagedImage = Path.Combine(staging, "image.new");
            string stagedMetadata = Path.Combine(staging, "metadata.new");
            string image = Path.Combine(published, "image.png");
            string metadata = Path.Combine(published, "metadata.efyvlaby");
            File.WriteAllText(stagedImage, "new-image");
            File.WriteAllText(stagedMetadata, "new-metadata");
            File.WriteAllText(image, "old-image");
            File.WriteAllText(metadata, "old-metadata");
            ExportEngine.PublishPair(stagedImage, image, stagedMetadata, metadata);
            Require(File.ReadAllText(image) == "new-image");
            Require(File.ReadAllText(metadata) == "new-metadata");
            Require(Directory.GetFiles(staging).Length == 0);
            Require(Directory.GetFiles(published).Length == 2);

            stagedImage = Path.Combine(staging, "image.fail");
            stagedMetadata = Path.Combine(staging, "metadata.fail");
            string absentImage = Path.Combine(published, "absent.png");
            string invalidMetadata = Path.Combine(published, "metadata-directory");
            File.WriteAllText(stagedImage, "transient-image");
            File.WriteAllText(stagedMetadata, "transient-metadata");
            Directory.CreateDirectory(invalidMetadata);
            RequireThrows<IOException>(() => ExportEngine.PublishPair(
                stagedImage,
                absentImage,
                stagedMetadata,
                invalidMetadata));
            Require(!File.Exists(absentImage));

            EFYVProject invalid = CreateValidProject(root, 1);
            invalid.AssetProperties[SharedConfig.BaseSpeedField] = float.NaN;
            RequireThrows<ProjectValidationException>(() => engine.Export(invalid, CancellationToken.None));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                RequireThrows<OperationCanceledException>(() => engine.Export(project, cancellation.Token));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void StorageWriteCorruptSubElement(
        string path,
        int version,
        string name,
        int width,
        int height,
        int declaredPixelCount,
        uint[] pixels)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(version);
            writer.Write(name);
            writer.Write(width);
            writer.Write(height);
            writer.Write(declaredPixelCount);
            foreach (uint pixel in pixels) writer.Write(pixel);
        }
    }

    private static JsonObject StorageAnimation(JsonObject document)
    {
        return document["animations"].AsArray()[0].AsObject();
    }

    private static JsonObject StorageFrame(JsonObject document)
    {
        return StorageAnimation(document)["frames"].AsArray()[0].AsObject();
    }

    private static JsonObject StorageLayer(JsonObject document)
    {
        return StorageFrame(document)["layers"].AsArray()[0].AsObject();
    }

    private static JsonObject StorageHitbox(JsonObject document)
    {
        return StorageFrame(document)["hitboxes"].AsArray()[0].AsObject();
    }

    private static StoragePng StorageReadPng(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        Require(bytes.Length > signature.Length);
        for (int index = 0; index < signature.Length; index++) Require(bytes[index] == signature[index]);

        int offset = signature.Length;
        int width = 0;
        int height = 0;
        using (var compressed = new MemoryStream())
        {
            while (offset < bytes.Length)
            {
                int length = StorageReadBigEndianInt32(bytes, offset);
                offset += 4;
                string type = Encoding.ASCII.GetString(bytes, offset, 4);
                offset += 4;
                Require(length >= 0 && offset + length + 4 <= bytes.Length);
                if (type == "IHDR")
                {
                    Require(length == 13);
                    width = StorageReadBigEndianInt32(bytes, offset);
                    height = StorageReadBigEndianInt32(bytes, offset + 4);
                    Require(bytes[offset + 8] == 8 && bytes[offset + 9] == 6);
                }
                else if (type == "IDAT")
                {
                    compressed.Write(bytes, offset, length);
                }
                offset += length + 4;
                if (type == "IEND") break;
            }

            Require(width > 0 && height > 0);
            compressed.Position = 0;
            using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress))
            using (var raw = new MemoryStream())
            {
                inflater.CopyTo(raw);
                byte[] rawBytes = raw.ToArray();
                Require(rawBytes.Length == checked(height * (1 + width * 4)));
                for (int row = 0; row < height; row++) Require(rawBytes[row * (1 + width * 4)] == 0);
                return new StoragePng(width, height, rawBytes);
            }
        }
    }

    private static uint StorageReadRawPixel(byte[] raw, int width, int linearIndex)
    {
        int row = linearIndex / width;
        int column = linearIndex % width;
        int offset = row * (1 + width * 4) + 1 + column * 4;
        return Pack(raw[offset], raw[offset + 1], raw[offset + 2], raw[offset + 3]);
    }

    private static int StorageReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) |
            (bytes[offset + 1] << 16) |
            (bytes[offset + 2] << 8) |
            bytes[offset + 3];
    }

    private sealed class StoragePng
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Raw { get; }

        public StoragePng(int width, int height, byte[] raw)
        {
            Width = width;
            Height = height;
            Raw = raw;
        }
    }
}
