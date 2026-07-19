using System;
using System.Collections.Generic;
using System.IO;
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
                Require(png.Pixels[index] == expected[index]);

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

    // b1-backend-png agent addition: end-to-end near-square grid atlas layout.
    // Five 2x2 frames must export as a 3x2 grid (6x4 atlas, row-major frame
    // placement matching the Unity importer's slice order), every frame must
    // extract back exactly via the FastExporter atlas helpers, the unused
    // trailing cell must stay fully transparent, and the published metadata
    // must declare the grid dimensions.
    private static void TestExportGridAtlasLayoutRoundTrip()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            EFYVProject project = CreateValidProject(root, 1);
            project.CanvasWidth = 2;
            project.CanvasHeight = 2;
            project.Animations.Clear();
            var animation = new AnimationState("Grid", 9);
            const int frameCount = 5;
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var frame = new Frame(2, 2, frameIndex);
                frame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = default;
                for (int pixel = 0; pixel < 4; pixel++)
                {
                    frame.Layers[0].Pixels[pixel].Rgba = Pack(
                        (byte)(frameIndex * 40 + pixel),
                        (byte)(200 - frameIndex),
                        (byte)pixel,
                        255);
                }
                animation.Frames.Add(frame);
            }
            project.Animations.Add(animation);

            EFYVBackend.Core.Export.FastExporter.ComputeAtlasLayout(
                frameCount, 2, 2, out int columns, out int rows);
            Require(columns == 3 && rows == 2);

            var engine = new ExportEngine(new ProjectValidator(new AssetSchemaService()));
            ExportResult result = engine.Export(project, CancellationToken.None);
            Require(result.FrameCount == frameCount);
            Require(result.AtlasWidth == 6 && result.AtlasHeight == 4);

            StoragePng png = StorageReadPng(result.ImagePath);
            Require(png.Width == 6 && png.Height == 4);

            var extracted = new uint[4];
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                EFYVBackend.Core.Export.FastExporter.GetAtlasFrameOrigin(
                    frameIndex, columns, 2, 2, out int originX, out int originY);
                Require(originX == (frameIndex % 3) * 2);
                Require(originY == (frameIndex / 3) * 2);
                EFYVBackend.Core.Export.FastExporter.ExtractFrameFromAtlas(
                    png.Pixels, png.Width, extracted, 2, 2, originX, originY);
                PixelColor[] flattened = animation.Frames[frameIndex].FlattenLayers();
                for (int pixel = 0; pixel < 4; pixel++)
                    Require(extracted[pixel] == flattened[pixel].Rgba);
            }

            // The sixth grid cell holds no frame and must remain transparent.
            EFYVBackend.Core.Export.FastExporter.GetAtlasFrameOrigin(
                frameCount, columns, 2, 2, out int emptyX, out int emptyY);
            EFYVBackend.Core.Export.FastExporter.ExtractFrameFromAtlas(
                png.Pixels, png.Width, extracted, 2, 2, emptyX, emptyY);
            for (int pixel = 0; pixel < 4; pixel++) Require(extracted[pixel] == 0u);

            JsonObject document = JsonNode.Parse(File.ReadAllText(result.MetadataPath)).AsObject();
            JsonObject atlas = document[BackendConfig.Exporter.FieldAtlas].AsObject();
            Require((int)atlas[BackendConfig.Exporter.FieldAtlasWidth] == 6);
            Require((int)atlas[BackendConfig.Exporter.FieldAtlasHeight] == 4);
            Require((int)atlas[BackendConfig.Exporter.FieldFrameWidth] == 2);
            Require((int)atlas[BackendConfig.Exporter.FieldFrameHeight] == 2);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // b2-pipeline-contract agent additions: the .efyvlaby document version +
    // baseAssetType contract (#16a/#16e), the snapshot-overload atlas caps and
    // identity rejection (#16b/#36), the sub-element format version constant,
    // and the retrying publish rollback (#12).
    // ------------------------------------------------------------------
    private static void TestExportDocumentVersionAndBaseAssetType()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            var validator = new ProjectValidator(schema);
            var engine = new ExportEngine(validator);

            EFYVProject project = CreateValidProject(root, 1);
            ExportResult result = engine.Export(project, CancellationToken.None);
            JsonObject document = JsonNode.Parse(File.ReadAllText(result.MetadataPath)).AsObject();

            // documentVersion is the FIRST top-level member; base types export
            // their own type as baseAssetType.
            var names = new List<string>();
            foreach (KeyValuePair<string, JsonNode> member in document) names.Add(member.Key);
            Require(names[0] == BackendConfig.Exporter.FieldDocumentVersion);
            Require((int)document[BackendConfig.Exporter.FieldDocumentVersion] ==
                BackendConfig.Exporter.CurrentDocumentVersion);
            Require((string)document[BackendConfig.Exporter.FieldAssetType] ==
                Config.Types.AssetTypeEnemyData);
            Require((string)document[BackendConfig.Exporter.FieldBaseAssetType] ==
                Config.Types.AssetTypeEnemyData);

            // A custom registered type exports its registered base type, giving
            // importers without the concrete class a factory to fall back to.
            Require(schema.RegisterAssetType(new AssetSchemaRegistration(
                "CustomTrapData", "Custom Trap", Config.Types.AssetTypeEnemyData)));
            EFYVProject custom = CreateValidProject(root, 1);
            custom.TargetAssetType = "CustomTrapData";
            custom.AssetProperties[SharedConfig.EntityNameField] = "CustomTrap";
            ExportResult customResult = engine.Export(custom, CancellationToken.None);
            JsonObject customDocument = JsonNode.Parse(File.ReadAllText(customResult.MetadataPath)).AsObject();
            Require((string)customDocument[BackendConfig.Exporter.FieldAssetType] == "CustomTrapData");
            Require((string)customDocument[BackendConfig.Exporter.FieldBaseAssetType] ==
                Config.Types.AssetTypeEnemyData);

            // The sub-element format version is its own constant (#16a): pin the
            // shipped value so a project-format bump cannot silently ride along.
            // Item #6 bumped it to 2 (pivot + default-transform header) and made
            // readers accept the whole supported range.
            Require(Config.Persistence.SubElementFormatVersion == 2);
            Require(Config.Persistence.MinSupportedSubElementFormatVersion == 1);
            Require(Config.Persistence.MinSupportedSubElementFormatVersion <=
                Config.Persistence.SubElementFormatVersion);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestExportSnapshotCapsAndIdentityReject()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var engine = new ExportEngine(new ProjectValidator(new AssetSchemaService()));

            // The snapshot overload bypasses ProjectValidator, so it must enforce
            // the shared atlas caps itself: 17 frames of a 4096x1 canvas need a
            // 5-column grid (20480px) over MaxAtlasDimension. 16 frames (4x4 at
            // exactly the cap) still export.
            EFYVProject oversized = CreateValidProject(root, 1);
            oversized.CanvasWidth = Config.Persistence.MaxCanvasDimension;
            oversized.CanvasHeight = 1;
            oversized.Animations.Clear();
            var animation = new AnimationState("Caps", Config.Animation.DefaultFPS);
            for (int index = 0; index < 17; index++)
                animation.Frames.Add(new Frame(oversized.CanvasWidth, oversized.CanvasHeight, index));
            oversized.Animations.Add(animation);
            RequireThrows<InvalidOperationException>(() => engine.Export(
                ProjectSnapshot.Capture(oversized), CancellationToken.None));
            string rawArt = Path.Combine(root, Config.Export.DirAssets, Config.Export.DirRawArt);
            Require(!Directory.Exists(rawArt) || Directory.GetFiles(rawArt).Length == 0);

            animation.Frames.RemoveAt(16);
            ExportResult atCap = engine.Export(ProjectSnapshot.Capture(oversized), CancellationToken.None);
            Require(atCap.AtlasWidth == Config.Export.MaxAtlasDimension);
            Require(atCap.AtlasHeight == 4);

            // A snapshot without any identity property is rejected (#36) instead
            // of publishing under a minted fallback stem.
            EFYVProject anonymous = CreateValidProject(root, 1);
            anonymous.AssetProperties.Remove(SharedConfig.EntityNameField);
            RequireThrows<ArgumentException>(() => engine.Export(
                ProjectSnapshot.Capture(anonymous), CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestExportPublishRetryRollback()
    {
        string root = NewTemporaryDirectory();
        try
        {
            string staging = Path.Combine(root, "staging");
            string published = Path.Combine(root, "published");
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

            // A metadata destination that stays locked through the whole bounded
            // retry window fails the pair-publish and rolls the image back.
            using (var locked = new FileStream(metadata, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                RequireThrows<IOException>(() =>
                    ExportEngine.PublishPair(stagedImage, image, stagedMetadata, metadata));
            }
            Require(File.ReadAllText(image) == "old-image");
            Require(File.ReadAllText(metadata) == "old-metadata");

            // With the lock gone the very same publish succeeds atomically.
            File.WriteAllText(stagedImage, "new-image");
            File.WriteAllText(stagedMetadata, "new-metadata");
            ExportEngine.PublishPair(stagedImage, image, stagedMetadata, metadata);
            Require(File.ReadAllText(image) == "new-image");
            Require(File.ReadAllText(metadata) == "new-metadata");
            Require(Directory.GetFiles(staging).Length == 0);
            Require(Directory.GetFiles(published).Length == 2);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // Item #27 content-hash suppression: a live republish producing byte-identical
    // output must not rewrite that artifact, and the PNG and .efyvlaby suppress
    // INDEPENDENTLY (a hitbox nudge rewrites only the metadata). The on-disk file
    // is overwritten with a sentinel between publishes: a suppressed publish
    // leaves the sentinel intact (proving no write), and ExportResult reports
    // exactly which artifact it touched.
    private static void TestExportContentHashSuppression()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            EFYVProject project = CreateValidProject(root, 1);
            project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = default;
            var engine = new ExportEngine(new ProjectValidator(new AssetSchemaService()));

            ExportResult first = engine.Export(project, CancellationToken.None);
            Require(first.ImageWritten && first.MetadataWritten);
            Require(File.Exists(first.ImagePath) && File.Exists(first.MetadataPath));

            // Byte-identical republish through the same engine suppresses BOTH.
            File.WriteAllText(first.ImagePath, "PNG-SENTINEL");
            File.WriteAllText(first.MetadataPath, "META-SENTINEL");
            ExportResult repeat = engine.Export(project, CancellationToken.None);
            Require(!repeat.ImageWritten && !repeat.MetadataWritten);
            Require(File.ReadAllText(repeat.ImagePath) == "PNG-SENTINEL");
            Require(File.ReadAllText(repeat.MetadataPath) == "META-SENTINEL");

            // A hitbox nudge changes ONLY the .efyvlaby: the PNG stays suppressed.
            EFYVBackend.Core.Models.HitboxData nudged =
                project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
            nudged.X = 0.25f;
            project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = nudged;
            ExportResult afterHitbox = engine.Export(project, CancellationToken.None);
            Require(!afterHitbox.ImageWritten);
            Require(afterHitbox.MetadataWritten);
            Require(File.ReadAllText(afterHitbox.ImagePath) == "PNG-SENTINEL");
            Require(File.ReadAllText(afterHitbox.MetadataPath) != "META-SENTINEL");

            // A pixel change rewrites the PNG; the now-unchanged metadata suppresses.
            File.WriteAllText(afterHitbox.ImagePath, "PNG-SENTINEL2");
            string metadataBefore = File.ReadAllText(afterHitbox.MetadataPath);
            project.Animations[0].Frames[0].Layers[0].Pixels[0].Rgba = Pack(9, 8, 7, 255);
            ExportResult afterPixel = engine.Export(project, CancellationToken.None);
            Require(afterPixel.ImageWritten);
            Require(File.ReadAllText(afterPixel.ImagePath) != "PNG-SENTINEL2");
            Require(!afterPixel.MetadataWritten);
            Require(File.ReadAllText(afterPixel.MetadataPath) == metadataBefore);

            // A fresh engine has no cache: it always writes on its first cycle.
            File.WriteAllText(afterPixel.ImagePath, "PNG-SENTINEL3");
            var freshEngine = new ExportEngine(new ProjectValidator(new AssetSchemaService()));
            ExportResult fresh = freshEngine.Export(project, CancellationToken.None);
            Require(fresh.ImageWritten && fresh.MetadataWritten);
            Require(File.ReadAllText(fresh.ImagePath) != "PNG-SENTINEL3");

            // External deletion forces a re-publish even with a warm cache.
            File.Delete(fresh.ImagePath);
            ExportResult afterDelete = freshEngine.Export(project, CancellationToken.None);
            Require(afterDelete.ImageWritten);
            Require(File.Exists(afterDelete.ImagePath));
            Require(!afterDelete.MetadataWritten);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // Item #27 metadata-only fast path: an export that knows no pixels changed
    // skips the PNG entirely - never re-packing or re-encoding the atlas - yet
    // still publishes an .efyvlaby whose atlas block pins the existing sheet. It
    // falls back to a full publish whenever the sibling PNG is absent, so a
    // first-ever (or post-deletion) export can never leave the sheet missing.
    private static void TestExportMetadataOnlyFastPath()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            EFYVProject project = CreateValidProject(root, 1);
            project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = default;
            var engine = new ExportEngine(new ProjectValidator(new AssetSchemaService()));

            // preferMetadataOnly with no existing PNG falls back to a full export.
            ExportResult firstMeta = engine.Export(
                ProjectSnapshot.Capture(project), CancellationToken.None, true);
            Require(firstMeta.ImageWritten);
            Require(File.Exists(firstMeta.ImagePath));

            // Nudge a hitbox, then a metadata-only publish never touches the PNG.
            File.WriteAllText(firstMeta.ImagePath, "PNG-SENTINEL");
            EFYVBackend.Core.Models.HitboxData nudged =
                project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
            nudged.X = 0.5f;
            project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = nudged;
            ExportResult metaOnly = engine.Export(
                ProjectSnapshot.Capture(project), CancellationToken.None, true);
            Require(!metaOnly.ImageWritten);
            Require(metaOnly.MetadataWritten);
            Require(File.ReadAllText(metaOnly.ImagePath) == "PNG-SENTINEL");

            // The published .efyvlaby carries the nudged hitbox and the real
            // sheet's frame dimensions (metadata-only still pins the atlas block).
            JsonObject document = JsonNode.Parse(File.ReadAllText(metaOnly.MetadataPath)).AsObject();
            JsonArray hitboxes = document[BackendConfig.Exporter.FieldHitboxes].AsArray();
            Require(hitboxes.Count == 1);
            Require((float)hitboxes[0][BackendConfig.Exporter.FieldX] == 0.5f);
            JsonObject atlas = document[BackendConfig.Exporter.FieldAtlas].AsObject();
            Require((int)atlas[BackendConfig.Exporter.FieldFrameWidth] == project.CanvasWidth);
            Require((int)atlas[BackendConfig.Exporter.FieldFrameHeight] == project.CanvasHeight);

            // A byte-identical metadata-only republish suppresses the .efyvlaby too.
            File.WriteAllText(metaOnly.MetadataPath, "META-SENTINEL");
            ExportResult metaRepeat = engine.Export(
                ProjectSnapshot.Capture(project), CancellationToken.None, true);
            Require(!metaRepeat.ImageWritten && !metaRepeat.MetadataWritten);
            Require(File.ReadAllText(metaRepeat.MetadataPath) == "META-SENTINEL");

            // Deleting the PNG makes even a metadata-only request fall back to full.
            File.Delete(metaRepeat.ImagePath);
            ExportResult fallback = engine.Export(
                ProjectSnapshot.Capture(project), CancellationToken.None, true);
            Require(fallback.ImageWritten);
            Require(File.Exists(fallback.ImagePath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // Writes the .efyvsub VERSION-2 body layout (with a well-formed default
    // pivot/transform header) so each corrupt case still exercises its
    // intended failure under the current format.
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
            writer.Write(0); // pivotX
            writer.Write(0); // pivotY
            writer.Write(0); // defaultOffsetX
            writer.Write(0); // defaultOffsetY
            writer.Write(Config.Attachment.DefaultZOrder);
            writer.Write((byte)0); // flip flags
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

    // Decodes an exported atlas through the production FastPngDecoder (the former
    // hand-rolled chunk/inflate reader here was deleted in its favor), keeping the
    // signature/IHDR/CRC/zlib checks in product code under test instead of
    // duplicating them in the harness.
    private static StoragePng StorageReadPng(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        uint[] pixels = EFYVBackend.Core.Export.FastPngDecoder.Read(bytes, out int width, out int height);
        Require(width > 0 && height > 0);
        Require(pixels.Length == checked(width * height));
        return new StoragePng(width, height, pixels);
    }

    private sealed class StoragePng
    {
        public int Width { get; }
        public int Height { get; }
        public uint[] Pixels { get; }

        public StoragePng(int width, int height, uint[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }
    }
}
