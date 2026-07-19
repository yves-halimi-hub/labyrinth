using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
    private static void TestModelsPersistenceModelContractsAndLayerReference()
    {
        // AnimationState constructor and clone contracts.
        var defaultFps = new AnimationState("MPDefault");
        Require(defaultFps.FPS == Config.Animation.DefaultFPS);
        Require(defaultFps.StateName == "MPDefault" && defaultFps.Frames.Count == 0);
        RequireThrows<ArgumentOutOfRangeException>(() => new AnimationState("bad", -3));
        AnimationState emptyClone = defaultFps.Clone();
        Require(emptyClone.StateName == "MPDefault");
        Require(emptyClone.FPS == Config.Animation.DefaultFPS);
        Require(emptyClone.Frames.Count == 0 && !ReferenceEquals(emptyClone.Frames, defaultFps.Frames));
        defaultFps.StateName = "MPRenamed";
        defaultFps.FPS = 31;
        Require(defaultFps.StateName == "MPRenamed" && defaultFps.FPS == 31);
        Require(emptyClone.StateName == "MPDefault");

        // Clone preserves per-layer settings, per-frame indices and hitbox tables.
        var multi = new AnimationState("MPMulti", 9);
        var baseFrame = new Frame(4, 3, 5);
        baseFrame.Layers[0].SetPixel(1, 1, Color(9, 8, 7, 255));
        var upper = new Layer("MPUpper", 4, 3) { IsVisible = false, Opacity = 0.625f };
        upper.SetPixel(2, 2, Color(1, 2, 3, 4));
        baseFrame.Layers.Add(upper);
        baseFrame.Hitboxes["MPBox"] = MatrixHitbox(0.125f, 0.0625f, 0.0625f, 0.125f);
        multi.Frames.Add(baseFrame);
        multi.Frames.Add(new Frame(4, 3, 6));
        AnimationState multiClone = multi.Clone();
        Require(multiClone.Frames.Count == 2);
        Require(multiClone.Frames[0].FrameIndex == 5 && multiClone.Frames[1].FrameIndex == 6);
        Require(multiClone.Frames[0].Layers.Count == 2);
        Require(multiClone.Frames[0].Layers[1].Name == "MPUpper");
        Require(!multiClone.Frames[0].Layers[1].IsVisible);
        Require(multiClone.Frames[0].Layers[1].Opacity == 0.625f);
        Require(multiClone.Frames[0].Layers[1].GetPixel(2, 2).Rgba == Pack(1, 2, 3, 4));
        Require(multiClone.Frames[0].Hitboxes["MPBox"].Width == 0.0625f);

        // EFYVProject data passthrough.
        var project = new EFYVProject("MPCustomType");
        Require(project.TargetAssetType == "MPCustomType");
        project.TargetAssetType = "MPChanged";
        project.UnityProjectPath = "C:/mp/unity";
        project.CanvasWidth = 17;
        project.CanvasHeight = 23;
        project.DesignerSeed = uint.MaxValue;
        Require(project.TargetAssetType == "MPChanged");
        Require(project.UnityProjectPath == "C:/mp/unity");
        Require(project.CanvasWidth == 17 && project.CanvasHeight == 23);
        Require(project.DesignerSeed == uint.MaxValue);
        project.DesignerSeed = 0u;
        Require(project.DesignerSeed == 0u);

        // Frame constructor matrix.
        var indexOnly = new Frame(11);
        Require(indexOnly.Width == Config.Canvas.DefaultWidth);
        Require(indexOnly.Height == Config.Canvas.DefaultHeight);
        Require(indexOnly.FrameIndex == 11);
        var sized = new Frame(5, 6);
        Require(sized.FrameIndex == Config.Frame.DefaultIndex);
        Require(sized.Layers.Count == 1);
        Require(sized.Layers[0].Name == Config.Layer.DefaultName);
        Require(sized.Layers[0].IsVisible == Config.Layer.DefaultVisibility);
        Require(sized.Layers[0].Opacity == Config.Layer.DefaultOpacity);
        Require(sized.Hitboxes.Count == 1 && sized.Hitboxes.ContainsKey(Config.Hitbox.DefaultKeyHurtbox));
        // The seeded hurtbox uses HitboxData's EXPLICIT defaults (unit size), unlike
        // default(HitboxData) which stays all-zero because the struct ctor is skipped.
        EFYVBackend.Core.Models.HitboxData defaultBox = sized.Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
        Require(defaultBox.X == BackendConfig.Models.HitboxDefaultPosition);
        Require(defaultBox.Y == BackendConfig.Models.HitboxDefaultPosition);
        Require(defaultBox.Width == BackendConfig.Models.HitboxDefaultSize);
        Require(defaultBox.Height == BackendConfig.Models.HitboxDefaultSize);
        EFYVBackend.Core.Models.HitboxData zeroBox = default;
        Require(zeroBox.X == 0f && zeroBox.Y == 0f && zeroBox.Width == 0f && zeroBox.Height == 0f);
        var negativeIndex = new Frame(2, 2, -7);
        Require(negativeIndex.FrameIndex == -7);

        // FlattenLayers(width, height) happy path returns fresh, equal arrays.
        sized.Layers[0].SetPixel(4, 5, Color(31, 32, 33, 255));
        PixelColor[] byDims = sized.FlattenLayers(5, 6);
        PixelColor[] plain = sized.FlattenLayers();
        Require(!ReferenceEquals(byDims, plain) && byDims.Length == 30);
        for (int index = 0; index < byDims.Length; index++) Require(byDims[index].Rgba == plain[index].Rgba);
        Require(byDims[(5 * 5) + 4].Rgba == Pack(31, 32, 33, 255));

        // CopyHitboxesFrom fully replaces the destination table.
        var hitboxSource = new Frame(2, 2);
        hitboxSource.Hitboxes.Clear();
        hitboxSource.Hitboxes["MPOnly"] = MatrixHitbox(1f, 2f, 3f, 4f);
        var hitboxDestination = new Frame(2, 2);
        hitboxDestination.Hitboxes["MPStale"] = MatrixHitbox(0f, 0f, 9f, 0f);
        hitboxDestination.CopyHitboxesFrom(hitboxSource);
        Require(hitboxDestination.Hitboxes.Count == 1);
        Require(hitboxDestination.Hitboxes["MPOnly"].Height == 4f);

        // Layer.Clone() default naming and the opacity clamp band.
        var layer = new Layer("MPBase", 3, 3);
        Layer suffixClone = layer.Clone();
        Require(suffixClone.Name == "MPBase" + Config.Layer.CopySuffix);
        layer.Opacity = -0.5f;
        Require(layer.Opacity == 0f);
        layer.Opacity = 0f;
        Require(layer.Opacity == 0f);
        layer.Opacity = 1.5f;
        Require(layer.Opacity == 1f);
        layer.Opacity = 1f;
        Require(layer.Opacity == 1f);
        layer.Opacity = 0.375f;
        Require(layer.Opacity == 0.375f);
        layer.Opacity = float.Epsilon;
        Require(layer.Opacity == float.Epsilon);
        RequireThrows<ArgumentOutOfRangeException>(() => layer.Opacity = float.NegativeInfinity);

        // PixelColor's explicit default constructor is transparent.
        Require(new PixelColor().Rgba == Config.Color.TransparentPixelRgba);
        Require(new PixelColor().IsTransparent);

        // SubElement metadata setters and the checked size contract.
        var element = new SubElement("MPElement", 2, 3, new uint[6]);
        element.Name = "MPElementRenamed";
        element.Width = 9;
        element.Height = 11;
        Require(element.Name == "MPElementRenamed" && element.Width == 9 && element.Height == 11);
        Require(element.Pixels.Length == 6);
        RequireThrows<OverflowException>(() => new SubElement("MPHuge", 65536, 65537, new uint[1]));

        // Randomized SetPixel/GetPixel reference model: exact row-major mapping,
        // out-of-bounds writes are no-ops, and the unsafe write path never corrupts
        // any neighbouring pixel (the full-buffer sweep would catch it).
        const int width = 13;
        const int height = 7;
        var random = new Random(0x4D505331);
        var subject = new Layer("MPModel", width, height);
        var model = new uint[width * height];
        for (int operation = 0; operation < 4000; operation++)
        {
            int x = random.Next(-3, width + 3);
            int y = random.Next(-3, height + 3);
            bool inBounds = x >= 0 && y >= 0 && x < width && y < height;
            if (random.Next(4) == 0)
            {
                Require(subject.GetPixel(x, y).Rgba == (inBounds ? model[(y * width) + x] : 0u));
                continue;
            }
            uint color = NextUInt(random);
            subject.SetPixel(x, y, new PixelColor { Rgba = color });
            if (inBounds) model[(y * width) + x] = color;
            if (operation % 500 == 0)
            {
                for (int index = 0; index < model.Length; index++)
                    Require(subject.Pixels[index].Rgba == model[index]);
            }
        }
        for (int index = 0; index < model.Length; index++)
            Require(subject.Pixels[index].Rgba == model[index]);
        subject.Clear();
        for (int index = 0; index < model.Length; index++) Require(subject.Pixels[index].Rgba == 0u);

        // FIXED: the Layer constructor now sizes its pixel buffer with a CHECKED
        // width*height multiply, matching SubElement's size contract. 65536x65537 used
        // to wrap to a 65536-element buffer while Width/Height still reported the full
        // canvas, so any nominally in-bounds SetPixel/GetPixel with y >= 1 reached far
        // beyond the buffer through the unchecked FastMemory unsafe path.
        int wrapWidth = 65536;
        int wrapHeight = 65537;
        RequireThrows<OverflowException>(() => new Layer("MPWrapped", wrapWidth, wrapHeight));
        RequireThrows<OverflowException>(() => new Layer("MPWrappedMax", int.MaxValue, int.MaxValue));
        // The largest non-wrapping area still constructs (Width/Height and buffer agree).
        var tall = new Layer("MPTall", 1, 65537);
        Require(tall.Width == 1 && tall.Height == 65537);
        Require(tall.Pixels.Length == 65537);
    }

    private static void TestModelsPersistenceSnapshotCompositionAndPropertyCapture()
    {
        var project = new EFYVProject(Config.Types.AssetTypeEnemyData)
        {
            CanvasWidth = 4,
            CanvasHeight = 3,
            UnityProjectPath = "MPUnity",
            DesignerSeed = 777u
        };
        project.AssetProperties["mpNull"] = null;
        project.AssetProperties["mpBool"] = true;
        project.AssetProperties["mpInt"] = -12;
        project.AssetProperties["mpUInt"] = 3000000000u;
        project.AssetProperties["mpDouble"] = 2.5d;
        project.AssetProperties["mpEnum"] = DayOfWeek.Friday;
        project.AssetProperties["mpText"] = "kept";

        var walk = new AnimationState("MPWalk", 8);
        var frameA = new Frame(4, 3, 0);
        frameA.Layers[0].SetPixel(0, 0, Color(200, 0, 0, 255));
        var half = new Layer("MPHalf", 4, 3) { Opacity = 0.5f };
        half.SetPixel(0, 0, Color(0, 200, 0, 255));
        half.SetPixel(3, 2, Color(0, 0, 200, 128));
        frameA.Layers.Add(half);
        var hidden = new Layer("MPHidden", 4, 3) { IsVisible = false };
        hidden.SetPixel(1, 1, Color(255, 255, 255, 255));
        frameA.Layers.Add(hidden);
        frameA.Hitboxes["MPAttack"] = MatrixHitbox(0.0625f, 0.0625f, 0.125f, 0.0625f);
        var frameB = new Frame(4, 3, 1);
        frameB.Layers[0].SetPixel(2, 1, Color(5, 6, 7, 8));
        walk.Frames.Add(frameA);
        walk.Frames.Add(frameB);

        var idle = new AnimationState("MPIdle", 3);
        var frameC = new Frame(4, 3, 0);
        frameC.Hitboxes.Clear();
        var frameD = new Frame(4, 3, 1);
        var frameE = new Frame(4, 3, 2);
        idle.Frames.Add(frameC);
        idle.Frames.Add(frameD);
        idle.Frames.Add(frameE);

        project.Animations.Add(walk);
        project.Animations.Add(idle);

        PixelColor[][] expectedPixels =
        {
            frameA.FlattenLayers(), frameB.FlattenLayers(),
            frameC.FlattenLayers(), frameD.FlattenLayers(), frameE.FlattenLayers()
        };

        ProjectSnapshot snapshot = ProjectSnapshot.Capture(project);
        Require(snapshot.TargetAssetType == Config.Types.AssetTypeEnemyData);
        Require(snapshot.UnityProjectPath == "MPUnity");
        Require(snapshot.CanvasWidth == 4 && snapshot.CanvasHeight == 3);
        Require(snapshot.DesignerSeed == 777u);
        Require(snapshot.Animations.Count == 2);
        Require(snapshot.TotalFrameCount == 5);
        // frameA: Hurtbox + MPAttack; frameB: Hurtbox; frameC: none; frameD/E: Hurtbox each.
        Require(snapshot.TotalHitboxCount == 5);
        Require(snapshot.Animations[0].StateName == "MPWalk" && snapshot.Animations[0].FPS == 8);
        Require(snapshot.Animations[0].StartFrame == 0 && snapshot.Animations[0].Frames.Count == 2);
        Require(snapshot.Animations[1].StateName == "MPIdle" && snapshot.Animations[1].FPS == 3);
        Require(snapshot.Animations[1].StartFrame == 2 && snapshot.Animations[1].Frames.Count == 3);

        int cursor = 0;
        foreach (AnimationSnapshot animation in snapshot.Animations)
        {
            foreach (FrameSnapshot frame in animation.Frames)
            {
                Require(frame.Width == 4 && frame.Height == 3 && frame.PixelCount == 12);
                var copied = new PixelColor[frame.PixelCount];
                frame.CopyPixelsTo(copied);
                for (int index = 0; index < copied.Length; index++)
                    Require(copied[index].Rgba == expectedPixels[cursor][index].Rgba);
                cursor++;
            }
        }
        Require(cursor == 5);

        // Hitbox snapshots carry the key and exact geometry; frameC captured empty.
        FrameSnapshot snappedA = snapshot.Animations[0].Frames[0];
        Require(snappedA.Hitboxes.Count == 2);
        bool sawHurtbox = false;
        bool sawAttack = false;
        foreach (HitboxSnapshot hitbox in snappedA.Hitboxes)
        {
            if (hitbox.Key == Config.Hitbox.DefaultKeyHurtbox)
            {
                sawHurtbox = true;
                Require(hitbox.X == BackendConfig.Models.HitboxDefaultPosition);
                Require(hitbox.Y == BackendConfig.Models.HitboxDefaultPosition);
                Require(hitbox.Width == BackendConfig.Models.HitboxDefaultSize);
                Require(hitbox.Height == BackendConfig.Models.HitboxDefaultSize);
            }
            else if (hitbox.Key == "MPAttack")
            {
                sawAttack = true;
                Require(hitbox.X == 0.0625f && hitbox.Y == 0.0625f);
                Require(hitbox.Width == 0.125f && hitbox.Height == 0.0625f);
            }
        }
        Require(sawHurtbox && sawAttack);
        Require(snapshot.Animations[1].Frames[0].Hitboxes.Count == 0);

        // Property capture matrix: value types and strings pass through by value,
        // arbitrary references were already covered (they stringify).
        Require(snapshot.AssetProperties["mpNull"] == null);
        Require((bool)snapshot.AssetProperties["mpBool"]);
        Require((int)snapshot.AssetProperties["mpInt"] == -12);
        Require((uint)snapshot.AssetProperties["mpUInt"] == 3000000000u);
        Require((double)snapshot.AssetProperties["mpDouble"] == 2.5d);
        Require((DayOfWeek)snapshot.AssetProperties["mpEnum"] == DayOfWeek.Friday);
        Require(ReferenceEquals(snapshot.AssetProperties["mpText"], project.AssetProperties["mpText"]));
    }

    private static void TestModelsPersistenceDocumentBytesAndPropertyRoundTrip()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var schema = new AssetSchemaService();
            var persistence = new ProjectPersistenceService(root, schema);
            Require(persistence.ProjectDirectory == Path.GetFullPath(root));
            Require(persistence.GetAutosavePath("MPDoc") ==
                persistence.GetProjectPath("MPDoc") + Config.Persistence.AutosaveSuffix);
            Require(persistence.GetProjectPath("MPDoc") ==
                Path.Combine(Path.GetFullPath(root), "MPDoc" + Config.Persistence.ProjectExtension));

            EFYVProject project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 3, 2);
            project.UnityProjectPath = root;
            project.DesignerSeed = 0xFEEDF00Du;
            project.AssetProperties["mpInt"] = 12;
            project.AssetProperties["mpLong"] = 5000000000L;
            project.AssetProperties["mpDouble"] = 2.5d;
            project.AssetProperties["mpBool"] = true;
            project.AssetProperties["mpNull"] = null;
            project.AssetProperties["mpString"] = "text";
            project.AssetProperties["mpFloatWhole"] = 4f;
            project.AssetProperties["mpFloatFraction"] = 7.5f;

            Frame frame = project.Animations[0].Frames[0];
            frame.FrameIndex = 42;
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                frame.Layers[0].SetPixel(x, y, Color(
                    (byte)(x + 1),
                    (byte)(y + 1),
                    7,
                    (byte)(x == 0 ? 0 : 9)));
            var upper = new Layer("MPUpper", 3, 2) { IsVisible = false, Opacity = 0.25f };
            upper.SetPixel(2, 1, Color(250, 251, 252, 253));
            frame.Layers.Add(upper);
            frame.Hitboxes["MPBox"] = MatrixHitbox(0.0625f, 0.0625f, 0.0625f, 0.0625f);

            string path = persistence.SaveProject("MPDoc", project, CancellationToken.None);
            Require(path == persistence.GetProjectPath("MPDoc"));

            // Byte-level document checks: camelCase envelope + exact RGBA byte layout.
            JsonObject document = JsonNode.Parse(File.ReadAllText(path)).AsObject();
            Require((int)document["formatVersion"] == Config.Persistence.ProjectFormatVersion);
            Require((string)document["targetAssetType"] == Config.Types.AssetTypeEnemyData);
            Require((int)document["canvasWidth"] == 3 && (int)document["canvasHeight"] == 2);
            Require((uint)document["designerSeed"] == 0xFEEDF00Du);
            Require(document["assetProperties"]["mpNull"] == null);
            Require((bool)document["assetProperties"]["mpBool"]);
            Require((long)document["assetProperties"]["mpLong"] == 5000000000L);

            JsonObject animationNode = document["animations"].AsArray()[0].AsObject();
            Require((string)animationNode["stateName"] == "Idle");
            Require((int)animationNode["fps"] == 12);
            JsonObject frameNode = animationNode["frames"].AsArray()[0].AsObject();
            Require((int)frameNode["frameIndex"] == 42);
            JsonArray layerNodes = frameNode["layers"].AsArray();
            Require(layerNodes.Count == 2);
            JsonObject upperNode = layerNodes[1].AsObject();
            Require((string)upperNode["name"] == "MPUpper");
            Require(!(bool)upperNode["isVisible"]);
            Require((float)upperNode["opacity"] == 0.25f);

            byte[] rgba = Convert.FromBase64String((string)layerNodes[0]["rgbaBytes"]);
            Require(rgba.Length == 3 * 2 * Config.Color.RgbaChannelCount);
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
            {
                int byteIndex = ((y * 3) + x) * Config.Color.RgbaChannelCount;
                Require(rgba[byteIndex + Config.Color.RedByteOffset] == x + 1);
                Require(rgba[byteIndex + Config.Color.GreenByteOffset] == y + 1);
                Require(rgba[byteIndex + Config.Color.BlueByteOffset] == 7);
                Require(rgba[byteIndex + Config.Color.AlphaByteOffset] == (x == 0 ? 0 : 9));
            }
            byte[] upperRgba = Convert.FromBase64String((string)upperNode["rgbaBytes"]);
            int cornerIndex = ((1 * 3) + 2) * Config.Color.RgbaChannelCount;
            Require(upperRgba[cornerIndex + Config.Color.RedByteOffset] == 250);
            Require(upperRgba[cornerIndex + Config.Color.GreenByteOffset] == 251);
            Require(upperRgba[cornerIndex + Config.Color.BlueByteOffset] == 252);
            Require(upperRgba[cornerIndex + Config.Color.AlphaByteOffset] == 253);

            bool sawBox = false;
            foreach (JsonNode hitboxNode in frameNode["hitboxes"].AsArray())
            {
                if ((string)hitboxNode["key"] != "MPBox") continue;
                sawBox = true;
                Require((float)hitboxNode["x"] == 0.0625f && (float)hitboxNode["y"] == 0.0625f);
                Require((float)hitboxNode["width"] == 0.0625f && (float)hitboxNode["height"] == 0.0625f);
            }
            Require(sawBox);

            // Full restore fidelity: per-layer pixels, layer metadata, hitboxes, frame index.
            EFYVProject restored = persistence.LoadProject("MPDoc");
            Require(restored.TargetAssetType == Config.Types.AssetTypeEnemyData);
            Require(restored.DesignerSeed == 0xFEEDF00Du);
            Require(restored.CanvasWidth == 3 && restored.CanvasHeight == 2);
            Frame restoredFrame = restored.Animations[0].Frames[0];
            Require(restoredFrame.FrameIndex == 42);
            Require(restoredFrame.Layers.Count == 2);
            RequireRgbaEqual(CopyRgba(frame.Layers[0]), restoredFrame.Layers[0]);
            RequireRgbaEqual(CopyRgba(frame.Layers[1]), restoredFrame.Layers[1]);
            Require(restoredFrame.Layers[1].Name == "MPUpper");
            Require(!restoredFrame.Layers[1].IsVisible);
            Require(restoredFrame.Layers[1].Opacity == 0.25f);
            Require(restoredFrame.Hitboxes.Count == frame.Hitboxes.Count);
            Require(restoredFrame.Hitboxes["MPBox"].Width == 0.0625f);
            Require(restoredFrame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox].Width == 0f);

            // Documents current behavior: JSON round-trips retype properties by VALUE —
            // whole floats come back as int and fractional floats as double.
            Require(restored.AssetProperties["mpInt"] is int);
            Require((int)restored.AssetProperties["mpInt"] == 12);
            Require(restored.AssetProperties["mpLong"] is long);
            Require((long)restored.AssetProperties["mpLong"] == 5000000000L);
            Require(restored.AssetProperties["mpDouble"] is double);
            Require((double)restored.AssetProperties["mpDouble"] == 2.5d);
            Require(restored.AssetProperties["mpBool"] is bool && (bool)restored.AssetProperties["mpBool"]);
            Require(restored.AssetProperties["mpNull"] == null);
            Require((string)restored.AssetProperties["mpString"] == "text");
            Require(restored.AssetProperties["mpFloatWhole"] is int);
            Require((int)restored.AssetProperties["mpFloatWhole"] == 4);
            Require(restored.AssetProperties["mpFloatFraction"] is double);
            Require((double)restored.AssetProperties["mpFloatFraction"] == 7.5d);
            // ...and the retyped values still validate cleanly.
            Require(new ProjectValidator(schema).Validate(restored).IsValid);

            // Save -> load -> save is byte-stable.
            persistence.SaveProject("MPDocSecond", restored, CancellationToken.None);
            byte[] firstBytes = File.ReadAllBytes(persistence.GetProjectPath("MPDoc"));
            byte[] secondBytes = File.ReadAllBytes(persistence.GetProjectPath("MPDocSecond"));
            Require(firstBytes.Length == secondBytes.Length);
            for (int index = 0; index < firstBytes.Length; index++)
                Require(firstBytes[index] == secondBytes[index]);

            // The autosave load path round-trips through the same document pipeline.
            persistence.SaveAutosave("MPDoc", project, CancellationToken.None);
            EFYVProject restoredAutosave = persistence.LoadAutosave("MPDoc");
            RequireRgbaEqual(
                CopyRgba(frame.Layers[0]),
                restoredAutosave.Animations[0].Frames[0].Layers[0]);
            Require(File.Exists(path));
            RequireThrows<FileNotFoundException>(() => persistence.LoadProject("MPMissing"));
            RequireThrows<FileNotFoundException>(() => persistence.LoadAutosave("MPMissing"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestModelsPersistenceSaveGuardsAndFailureAtomicity()
    {
        RequireThrows<ArgumentException>(() => new ProjectPersistenceService(null));
        RequireThrows<ArgumentException>(() => new ProjectPersistenceService("   "));

        string root = NewTemporaryDirectory();
        try
        {
            RequireThrows<ArgumentNullException>(() => new ProjectPersistenceService(root, null));
            string nested = Path.Combine(root, "mp-nested", "deep");
            var nestedPersistence = new ProjectPersistenceService(nested);
            Require(Directory.Exists(nested));
            Require(nestedPersistence.ProjectDirectory == Path.GetFullPath(nested));

            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());

            // Unsafe stems are rejected across the whole path API family.
            RequireThrows<ArgumentException>(() => persistence.GetAutosavePath("../escape"));
            RequireThrows<ArgumentException>(() => persistence.GetAutosavePath("CON"));
            RequireThrows<ArgumentException>(() => persistence.AutosaveExists("bad*stem"));
            RequireThrows<ArgumentException>(() => persistence.DeleteAutosave("bad?stem"));
            RequireThrows<ArgumentException>(() => persistence.LoadProject("name."));

            // SAVE-side document validation (the malformed corpus only exercised load).
            EFYVProject unsafeHitbox = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            unsafeHitbox.Animations[0].Frames[0].Hitboxes["MPEvil"] = MatrixHitbox(1000f, 0f, 0f, 0f);
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MPGuard", unsafeHitbox, CancellationToken.None));

            EFYVProject zeroCanvas = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            zeroCanvas.CanvasWidth = 0;
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MPGuard", zeroCanvas, CancellationToken.None));

            EFYVProject hugeCanvas = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            hugeCanvas.CanvasHeight = Config.Persistence.MaxCanvasDimension + 1;
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MPGuard", hugeCanvas, CancellationToken.None));

            EFYVProject unknownType = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            unknownType.TargetAssetType = "MPUnknownData";
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MPGuard", unknownType, CancellationToken.None));

            EFYVProject blankState = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            blankState.Animations[0].StateName = "  ";
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MPGuard", blankState, CancellationToken.None));

            EFYVProject tooManyAnimations = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 1, 1);
            AnimationState sharedAnimation = tooManyAnimations.Animations[0];
            for (int index = tooManyAnimations.Animations.Count;
                index <= Config.Persistence.MaxAnimations;
                index++)
                tooManyAnimations.Animations.Add(sharedAnimation);
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MPGuard", tooManyAnimations, CancellationToken.None));

            EFYVProject tooManyLayers = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 1, 1);
            Layer sharedLayer = tooManyLayers.Animations[0].Frames[0].Layers[0];
            for (int index = tooManyLayers.Animations[0].Frames[0].Layers.Count;
                index <= Config.Persistence.MaxLayersPerFrame;
                index++)
                tooManyLayers.Animations[0].Frames[0].Layers.Add(sharedLayer);
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MPGuard", tooManyLayers, CancellationToken.None));

            EFYVProject atlasOverflow = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 4096, 1);
            Frame sharedFrame = atlasOverflow.Animations[0].Frames[0];
            for (int index = 1; index < 5; index++) atlasOverflow.Animations[0].Frames.Add(sharedFrame);
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MPGuard", atlasOverflow, CancellationToken.None));

            // No guard failure may leave litter: validation happens before any file IO.
            Require(Directory.GetFiles(root).Length == 0);

            // A serializer failure mid-save preserves the committed bytes and cleans temps.
            EFYVProject stable = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 4, 4);
            string stablePath = persistence.SaveProject("MPStable", stable, CancellationToken.None);
            byte[] committed = File.ReadAllBytes(stablePath);
            stable.AssetProperties["mpPoison"] = double.NaN;
            bool threw = false;
            try
            {
                persistence.SaveProject("MPStable", stable, CancellationToken.None);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Require(threw);
            byte[] surviving = File.ReadAllBytes(stablePath);
            Require(committed.Length == surviving.Length);
            for (int index = 0; index < committed.Length; index++)
                Require(committed[index] == surviving[index]);
            Require(Directory.GetFiles(root).Length == 1);

            // Path policy corners the existing attack corpus does not cover.
            Require(DesignerPathPolicy.IsSafeFileStem("com0.dat"));
            Require(DesignerPathPolicy.IsSafeFileStem(".hidden"));
            Require(!DesignerPathPolicy.IsSafeFileStem("aux.tar.gz"));
            Require(!DesignerPathPolicy.IsSafeFileStem("..."));
            Require(!DesignerPathPolicy.IsSafeFileStem("tab\tname"));
            string absoluteOutside = Path.Combine(Path.GetTempPath(), "mp-absolute-escape.txt");
            RequireThrows<ArgumentException>(() =>
                DesignerPathPolicy.GetContainedPath(root, absoluteOutside));
            Require(DesignerPathPolicy.GetContainedPath(root, "sub/../inside.dat") ==
                Path.Combine(Path.GetFullPath(root), "inside.dat"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestModelsPersistenceExportAtlasMetadataAndPublication()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            var validator = new ProjectValidator(schema);
            var engine = new ExportEngine(validator);

            // Multi-animation atlas with a multi-layer frame: exact pixels and metadata.
            EFYVProject project = CreateValidProject(root, 1);
            project.CanvasWidth = 2;
            project.CanvasHeight = 3;
            project.Animations.Clear();
            var first = new AnimationState("MPFirst", 7);
            var second = new AnimationState("MPSecond", 11);
            var frames = new Frame[3];
            for (int index = 0; index < frames.Length; index++)
            {
                var frame = new Frame(2, 3, index);
                // Zero the seeded hurtbox: its explicit unit-size default would exceed
                // the 2x3 canvas unit bounds and fail export validation.
                frame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = default;
                for (int pixel = 0; pixel < 6; pixel++)
                    frame.Layers[0].Pixels[pixel].Rgba = Pack(
                        (byte)((index * 40) + pixel),
                        (byte)pixel,
                        (byte)index,
                        255);
                frames[index] = frame;
            }
            var overlay = new Layer("MPOverlay", 2, 3) { Opacity = 0.5f };
            overlay.SetPixel(1, 2, Color(0, 250, 0, 255));
            frames[0].Layers.Add(overlay);
            frames[1].Hitboxes["MPAttack"] = MatrixHitbox(0.0625f, 0.125f, 0.0625f, 0.0625f);
            first.Frames.Add(frames[0]);
            first.Frames.Add(frames[1]);
            second.Frames.Add(frames[2]);
            project.Animations.Add(first);
            project.Animations.Add(second);

            PixelColor[][] expected =
            {
                frames[0].FlattenLayers(), frames[1].FlattenLayers(), frames[2].FlattenLayers()
            };

            ExportResult result = engine.Export(project, CancellationToken.None);
            Require(result.FrameCount == 3 && result.HitboxCount == 4);
            // Grid atlas layout (b1-backend-png): 3 frames of 2x3 pack into a
            // near-square 2x2 grid (row-major), not a single 6x3 row.
            Require(result.AtlasWidth == 4 && result.AtlasHeight == 6);
            string rawArt = Path.Combine(root, Config.Export.DirAssets, Config.Export.DirRawArt);
            Require(result.ImagePath == Path.Combine(
                rawArt,
                "VerificationEnemy" + Config.Entity.FileSuffixDown + BackendConfig.Exporter.PngExtension));
            Require(result.MetadataPath == Path.Combine(
                rawArt,
                "VerificationEnemy" + Config.Entity.FileSuffixDown + BackendConfig.Exporter.EfyvExtension));

            StoragePng png = StorageReadPng(result.ImagePath);
            Require(png.Width == 4 && png.Height == 6);
            for (int frameIndex = 0; frameIndex < 3; frameIndex++)
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 2; x++)
            {
                int originX = (frameIndex % 2) * 2;
                int originY = (frameIndex / 2) * 3;
                uint actual = png.Pixels[((originY + y) * png.Width) + originX + x];
                Require(actual == expected[frameIndex][(y * 2) + x].Rgba);
            }

            using (JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(result.MetadataPath)))
            {
                JsonElement rootElement = metadata.RootElement;
                Require(rootElement.GetProperty(BackendConfig.Exporter.FieldAssetType).GetString() ==
                    Config.Types.AssetTypeEnemyData);
                Require(rootElement.GetProperty(BackendConfig.Exporter.FieldProperties)
                    .GetProperty(BackendConfig.Exporter.FieldEntityName).GetString() == "VerificationEnemy");

                JsonElement hitboxes = rootElement.GetProperty(BackendConfig.Exporter.FieldHitboxes);
                Require(hitboxes.GetArrayLength() == 4);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement entry in hitboxes.EnumerateArray())
                {
                    int frameIndex = entry.GetProperty(BackendConfig.Exporter.FieldFrameIndex).GetInt32();
                    string type = entry.GetProperty(BackendConfig.Exporter.FieldHitboxType).GetString();
                    Require(seen.Add(frameIndex + ":" + type));
                    if (frameIndex == 1 && type == "MPAttack")
                    {
                        Require(entry.GetProperty(BackendConfig.Exporter.FieldX).GetSingle() == 0.0625f);
                        Require(entry.GetProperty(BackendConfig.Exporter.FieldY).GetSingle() == 0.125f);
                        Require(entry.GetProperty(BackendConfig.Exporter.FieldWidth).GetSingle() == 0.0625f);
                        Require(entry.GetProperty(BackendConfig.Exporter.FieldHeight).GetSingle() == 0.0625f);
                    }
                }
                Require(seen.Contains("0:" + Config.Hitbox.DefaultKeyHurtbox));
                Require(seen.Contains("1:" + Config.Hitbox.DefaultKeyHurtbox));
                Require(seen.Contains("1:MPAttack"));
                Require(seen.Contains("2:" + Config.Hitbox.DefaultKeyHurtbox));

                JsonElement atlas = rootElement.GetProperty(BackendConfig.Exporter.FieldAtlas);
                Require(atlas.GetProperty(BackendConfig.Exporter.FieldFormatVersion).GetInt32() ==
                    BackendConfig.Exporter.CurrentFormatVersion);
                Require(atlas.GetProperty(BackendConfig.Exporter.FieldFrameWidth).GetInt32() == 2);
                Require(atlas.GetProperty(BackendConfig.Exporter.FieldFrameHeight).GetInt32() == 3);
                Require(atlas.GetProperty(BackendConfig.Exporter.FieldAtlasWidth).GetInt32() == 4);
                Require(atlas.GetProperty(BackendConfig.Exporter.FieldAtlasHeight).GetInt32() == 6);
                JsonElement animations = atlas.GetProperty(BackendConfig.Exporter.FieldAnimations);
                Require(animations.GetArrayLength() == 2);
                Require(animations[0].GetProperty(BackendConfig.Exporter.FieldName).GetString() == "MPFirst");
                Require(animations[0].GetProperty(BackendConfig.Exporter.FieldFps).GetInt32() == 7);
                Require(animations[0].GetProperty(BackendConfig.Exporter.FieldStartFrame).GetInt32() == 0);
                Require(animations[0].GetProperty(BackendConfig.Exporter.FieldFrameCount).GetInt32() == 2);
                Require(animations[1].GetProperty(BackendConfig.Exporter.FieldName).GetString() == "MPSecond");
                Require(animations[1].GetProperty(BackendConfig.Exporter.FieldFps).GetInt32() == 11);
                Require(animations[1].GetProperty(BackendConfig.Exporter.FieldStartFrame).GetInt32() == 2);
                Require(animations[1].GetProperty(BackendConfig.Exporter.FieldFrameCount).GetInt32() == 1);
            }

            // All four facings publish to distinct file stems.
            string[] facings =
            {
                Config.Entity.FacingUp, Config.Entity.FacingDown,
                Config.Entity.FacingLeft, Config.Entity.FacingRight
            };
            string[] suffixes =
            {
                Config.Entity.FileSuffixUp, Config.Entity.FileSuffixDown,
                Config.Entity.FileSuffixLeft, Config.Entity.FileSuffixRight
            };
            for (int index = 0; index < facings.Length; index++)
            {
                project.AssetProperties[SharedConfig.FacingField] = facings[index];
                ExportResult facingResult = engine.Export(project, CancellationToken.None);
                Require(facingResult.ImagePath == Path.Combine(
                    rawArt,
                    "VerificationEnemy" + suffixes[index] + BackendConfig.Exporter.PngExtension));
                Require(File.Exists(facingResult.ImagePath) && File.Exists(facingResult.MetadataPath));
            }

            // Non-directional assets fall back to the assetName identity and get no suffix.
            EFYVProject asset = CreateValidatorProject(Config.Types.AssetTypeGameAssetData, 4, 4);
            asset.UnityProjectPath = root;
            asset.AssetProperties[SharedConfig.AssetNameField] = "MPGameAsset";
            ExportResult assetResult = engine.Export(asset, CancellationToken.None);
            Require(assetResult.ImagePath == Path.Combine(
                rawArt,
                "MPGameAsset" + BackendConfig.Exporter.PngExtension));
            Require(assetResult.FrameCount == 1 && assetResult.HitboxCount == 1);
            StoragePng assetPng = StorageReadPng(assetResult.ImagePath);
            Require(assetPng.Width == 4 && assetPng.Height == 4);

            // Snapshot-level export contracts.
            RequireThrows<ArgumentNullException>(() =>
                engine.Export((ProjectSnapshot)null, CancellationToken.None));
            RequireThrows<InvalidOperationException>(() => engine.Export(
                ProjectSnapshot.Capture(new EFYVProject(Config.Types.AssetTypeEnemyData)),
                CancellationToken.None));

            // AtomicReplace: create-new and replace-existing leave no backups behind.
            string replaceRoot = Path.Combine(root, "mp-replace");
            Directory.CreateDirectory(replaceRoot);
            string source = Path.Combine(replaceRoot, "incoming.tmp");
            string destination = Path.Combine(replaceRoot, "settled.dat");
            File.WriteAllText(source, "mp-first");
            ExportEngine.AtomicReplace(source, destination);
            Require(!File.Exists(source) && File.ReadAllText(destination) == "mp-first");
            File.WriteAllText(source, "mp-second");
            ExportEngine.AtomicReplace(source, destination);
            Require(!File.Exists(source) && File.ReadAllText(destination) == "mp-second");
            Require(Directory.GetFiles(replaceRoot).Length == 1);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task TestModelsPersistenceAutosaveContextDispatch()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
            var scheduler = new ManualScheduler();
            var context = new ModelsPersistenceInlineContext();
            EFYVProject project = CreateValidProject(root, 1);

            using (var autosave = new AutosaveController(
                persistence,
                scheduler,
                TimeSpan.FromSeconds(1),
                context))
            {
                var states = new List<AutosaveState>();
                var stateGate = new object();
                autosave.StateChanged += snapshot =>
                {
                    lock (stateGate) states.Add(snapshot.State);
                };

                int captures = 0;
                bool capturedInsideContextPost = false;
                long firstId = autosave.Schedule("MPCtx", () =>
                {
                    captures++;
                    capturedInsideContextPost = context.ActiveDepth > 0;
                    return ProjectPersistenceSnapshot.Capture(project);
                });
                Require(firstId > 0);
                Require(autosave.Current.State == AutosaveState.Scheduled);
                Require(autosave.Current.ProjectName == "MPCtx");
                Require(autosave.Current.Path == persistence.GetAutosavePath("MPCtx"));
                AsyncReleaseWhenPending(scheduler, 1);
                await autosave.FlushAsync();
                Require(autosave.Current.State == AutosaveState.Succeeded);
                Require(autosave.Current.LastSavedAt.HasValue);
                Require(captures == 1 && capturedInsideContextPost);
                Require(persistence.AutosaveExists("MPCtx"));
                lock (stateGate)
                {
                    Require(states.Count == 3);
                    Require(states[0] == AutosaveState.Scheduled);
                    Require(states[1] == AutosaveState.Saving);
                    Require(states[2] == AutosaveState.Succeeded);
                }
                // One post for the deferred capture plus one per state transition.
                Require(context.PostCount == 4);

                // A factory failure propagates through the context capture path.
                long secondId = autosave.Schedule(
                    "MPCtxFail",
                    () => throw new InvalidDataException("mp-capture-failed"));
                Require(secondId > firstId);
                AsyncReleaseWhenPending(scheduler, 1);
                await autosave.FlushAsync();
                Require(autosave.Current.State == AutosaveState.Failed);
                Require(autosave.Current.Exception is InvalidDataException);
                Require(autosave.Current.Exception.Message == "mp-capture-failed");
                Require(!persistence.AutosaveExists("MPCtxFail"));

                // Cancel with no pending work is a silent no-op: no state change, no events.
                int eventsBefore;
                lock (stateGate) eventsBefore = states.Count;
                int postsBefore = context.PostCount;
                autosave.Cancel();
                Require(autosave.Current.State == AutosaveState.Failed);
                lock (stateGate) Require(states.Count == eventsBefore);
                Require(context.PostCount == postsBefore);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private sealed class ModelsPersistenceInlineContext : SynchronizationContext
    {
        private int postCount;
        private int activeDepth;

        public int PostCount => Volatile.Read(ref postCount);
        public int ActiveDepth => Volatile.Read(ref activeDepth);

        public override void Post(SendOrPostCallback callback, object state)
        {
            Interlocked.Increment(ref postCount);
            Interlocked.Increment(ref activeDepth);
            try
            {
                callback(state);
            }
            finally
            {
                Interlocked.Decrement(ref activeDepth);
            }
        }
    }
}
