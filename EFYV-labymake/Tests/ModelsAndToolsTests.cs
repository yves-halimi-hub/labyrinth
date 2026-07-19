using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Tools;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

internal static partial class Program
{
    private static void TestModelContractsAndCloneIsolation()
    {
        RequireThrows<ArgumentOutOfRangeException>(() => new Frame(0, 1));
        RequireThrows<ArgumentOutOfRangeException>(() => new Frame(1, 0));
        RequireThrows<ArgumentOutOfRangeException>(() => new Layer("bad", -1, 1));
        RequireThrows<ArgumentOutOfRangeException>(() => new AnimationState("bad", 0));
        RequireThrows<ArgumentNullException>(() => new SubElement("bad", 1, 1, null));
        RequireThrows<ArgumentException>(() => new SubElement("bad", 2, 2, new uint[3]));

        var project = new EFYVProject(Config.Types.AssetTypeEnemyData);
        Require(project.CanvasWidth == Config.Canvas.DefaultWidth);
        Require(project.CanvasHeight == Config.Canvas.DefaultHeight);
        Require(project.DesignerSeed == Config.Tool.Map.DefaultSeed);
        Require(project.Animations.Count == 0 && project.AssetProperties.Count == 0);

        var layer = new Layer("source", 5, 4);
        layer.IsVisible = false;
        layer.Opacity = 0.375f;
        layer.SetPixel(1, 2, Color(10, 20, 30, 40));
        layer.SetPixel(-1, 2, Color(255, 255, 255, 255));
        layer.SetPixel(5, 2, Color(255, 255, 255, 255));
        Require(layer.GetPixel(1, 2).Rgba == Pack(10, 20, 30, 40));
        Require(layer.GetPixel(-1, 2).Rgba == 0u);
        Require(layer.GetPixel(5, 2).Rgba == 0u);
        RequireThrows<ArgumentNullException>(() => layer.CopyPixelsFrom(null));
        RequireThrows<ArgumentException>(() => layer.CopyPixelsFrom(new PixelColor[19]));

        Layer clone = layer.Clone("clone");
        Require(clone.Name == "clone" && !clone.IsVisible && clone.Opacity == 0.375f);
        Require(!ReferenceEquals(layer.Pixels, clone.Pixels));
        clone.SetPixel(1, 2, Color(99, 88, 77, 66));
        Require(layer.GetPixel(1, 2).Rgba == Pack(10, 20, 30, 40));
        layer.Clear();
        Require(CountOpaque(layer) == 0);
        Require(clone.GetPixel(1, 2).Rgba == Pack(99, 88, 77, 66));

        var frame = new Frame(5, 4, 7);
        frame.Layers[0].SetPixel(4, 3, Color(1, 2, 3, 255));
        frame.Hitboxes["Attack"] = new HitboxData { X = 1, Y = 2, Width = 3, Height = 4 };
        Frame frameClone = frame.Clone();
        Require(frameClone.FrameIndex == 7 && frameClone.Width == 5 && frameClone.Height == 4);
        Require(!ReferenceEquals(frame.Layers, frameClone.Layers));
        Require(!ReferenceEquals(frame.Layers[0], frameClone.Layers[0]));
        Require(!ReferenceEquals(frame.Hitboxes, frameClone.Hitboxes));
        frameClone.Layers[0].SetPixel(4, 3, Color(9, 9, 9, 255));
        frameClone.Hitboxes["Attack"] = default;
        Require(frame.Layers[0].GetPixel(4, 3).R == 1);
        Require(frame.Hitboxes["Attack"].Width == 3);
        RequireThrows<ArgumentNullException>(() => frame.CopyHitboxesFrom(null));
        RequireThrows<ArgumentException>(() => frame.FlattenLayers(4, 4));
        RequireThrows<ArgumentException>(() => frame.FlattenLayers(5, 3));
        RequireThrows<ArgumentNullException>(() => frame.FlattenLayers(null));
        RequireThrows<ArgumentException>(() => frame.FlattenLayers(new PixelColor[1]));

        var animation = new AnimationState("Idle", 17);
        animation.Frames.Add(frame);
        AnimationState animationClone = animation.Clone();
        Require(animationClone.StateName == "Idle" && animationClone.FPS == 17);
        Require(!ReferenceEquals(animation.Frames[0], animationClone.Frames[0]));
        animationClone.Frames[0].FrameIndex = 123;
        Require(animation.Frames[0].FrameIndex == 7);

        uint[] source = { Pack(1, 2, 3, 4), Pack(5, 6, 7, 8) };
        var element = new SubElement("eye", 2, 1, source);
        source[0] = 0;
        Require(element.Pixels[0] == Pack(1, 2, 3, 4));
    }

    private static void TestPixelPackingAndRandomizedCompositing()
    {
        var random = new Random(0x51A7);
        for (int iteration = 0; iteration < 5000; iteration++)
        {
            uint initial = NextUInt(random);
            var pixel = new PixelColor { Rgba = initial };
            byte red = (byte)random.Next(256);
            pixel.R = red;
            Require(pixel.R == red);
            Require(pixel.G == (byte)(initial >> 8));
            Require(pixel.B == (byte)(initial >> 16));
            Require(pixel.A == (byte)(initial >> 24));

            byte green = (byte)random.Next(256);
            byte blue = (byte)random.Next(256);
            byte alpha = (byte)random.Next(256);
            pixel.G = green;
            pixel.B = blue;
            pixel.A = alpha;
            Require(pixel.Rgba == Pack(red, green, blue, alpha));
            Require(pixel.IsTransparent == (alpha == 0));
        }

        var frame = new Frame(23, 17);
        frame.Layers.Clear();
        var expected = new uint[23 * 17];
        for (int layerIndex = 0; layerIndex < 7; layerIndex++)
        {
            var layer = new Layer("random", 23, 17)
            {
                IsVisible = layerIndex != 2,
                Opacity = layerIndex == 4 ? 0f : (float)random.NextDouble()
            };
            for (int pixelIndex = 0; pixelIndex < layer.Pixels.Length; pixelIndex++)
                layer.Pixels[pixelIndex].Rgba = NextUInt(random);
            frame.Layers.Add(layer);

            if (!layer.IsVisible || layer.Opacity <= 0f) continue;
            byte opacity = (byte)(layer.Opacity * 255f + 0.5f);
            for (int pixelIndex = 0; pixelIndex < expected.Length; pixelIndex++)
                expected[pixelIndex] = ReferenceBlend(expected[pixelIndex], layer.Pixels[pixelIndex].Rgba, opacity);
        }

        PixelColor[] actual = frame.FlattenLayers();
        for (int index = 0; index < actual.Length; index++)
            Require(actual[index].Rgba == expected[index]);

        PixelColor[] reused = new PixelColor[actual.Length];
        for (int index = 0; index < reused.Length; index++) reused[index].Rgba = 0xDEADBEEFu;
        frame.FlattenLayers(reused);
        for (int index = 0; index < reused.Length; index++) Require(reused[index].Rgba == expected[index]);

        frame.Layers.Add(new Layer("wrong", 1, 1));
        RequireThrows<InvalidOperationException>(() => frame.FlattenLayers(reused));
    }

    private static void TestDrawingToolsExactnessAndCanaries()
    {
        uint ink = Pack(20, 40, 60, 255);
        var frame = new Frame(17, 13);
        var pencil = new PencilTool { CurrentColor = new PixelColor { Rgba = ink }, BrushSize = 1 };
        pencil.OnPointerDrag(null, frame, 16, 12);
        Require(CountOpaque(frame.Layers[0]) == 0);
        pencil.OnPointerDown(null, frame, 1, 1);
        pencil.OnPointerDrag(null, frame, 15, 11);
        pencil.OnPointerUp(null, frame, 15, 11);

        var expected = new HashSet<int>();
        ReferenceLine(1, 1, 15, 11, frame.Width, (x, y) => expected.Add(y * frame.Width + x));
        for (int index = 0; index < frame.Layers[0].Pixels.Length; index++)
            Require(frame.Layers[0].Pixels[index].Rgba == (expected.Contains(index) ? ink : 0u));

        uint[] before = CopyRgba(frame.Layers[0]);
        pencil.OnPointerDown(null, frame, -1, 3);
        pencil.OnPointerDrag(null, frame, 5, 3);
        pencil.OnPointerUp(null, frame, 5, 3);
        RequireRgbaEqual(before, frame.Layers[0]);
        pencil.ActiveLayerIndex = -1;
        pencil.OnPointerDown(null, frame, 2, 2);
        pencil.OnPointerUp(null, frame, 2, 2);
        RequireRgbaEqual(before, frame.Layers[0]);

        for (int size = 1; size <= 8; size++)
        {
            foreach (EFYVBackend.Core.Math.Algorithms.BrushShape shape in
                new[] { EFYVBackend.Core.Math.Algorithms.BrushShape.Square,
                    EFYVBackend.Core.Math.Algorithms.BrushShape.Circle })
            {
                Frame tap = DrawPencilTap(size, shape);
                int minimumOffset = -(size / 2);
                int maximumOffset = minimumOffset + size - 1;
                for (int y = 0; y < tap.Height; y++)
                for (int x = 0; x < tap.Width; x++)
                {
                    int dx = x - 4;
                    int dy = y - 4;
                    bool inside = dx >= minimumOffset && dx <= maximumOffset &&
                        dy >= minimumOffset && dy <= maximumOffset;
                    if (inside && shape == EFYVBackend.Core.Math.Algorithms.BrushShape.Circle)
                    {
                        float radius = (size & 1) == 0 ? size * 0.5f : size / 2;
                        float centerOffset = (minimumOffset + maximumOffset) * 0.5f;
                        float cx = dx - centerOffset;
                        float cy = dy - centerOffset;
                        inside = (cx * cx) + (cy * cy) <= radius * radius;
                    }
                    Require((tap.Layers[0].GetPixel(x, y).A != 0) == inside);
                }
            }
        }
    }

    private static void TestFillAndStampAdversarialCases()
    {
        const int width = 67;
        const int height = 53;
        var frame = new Frame(width, height);
        Layer layer = frame.Layers[0];
        PixelColor wall = Color(255, 255, 255, 255);
        PixelColor fillColor = Color(7, 9, 11, 255);
        for (int y = 0; y < height; y++) layer.SetPixel(width / 2, y, wall);
        for (int x = 0; x < width; x++) layer.SetPixel(x, height / 2, wall);

        var fill = new FillTool { CurrentColor = fillColor };
        fill.OnPointerDown(null, frame, 0, 0);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            uint expected = x == width / 2 || y == height / 2
                ? wall.Rgba
                : (x < width / 2 && y < height / 2 ? fillColor.Rgba : 0u);
            Require(layer.GetPixel(x, y).Rgba == expected);
        }

        uint[] stable = CopyRgba(layer);
        fill.OnPointerDown(null, frame, 0, 0);
        fill.OnPointerDown(null, frame, -1, 0);
        fill.OnPointerDown(null, frame, width, 0);
        fill.ActiveLayerIndex = int.MaxValue;
        fill.OnPointerDown(null, frame, 1, 1);
        RequireRgbaEqual(stable, layer);

        uint transparent = Pack(1, 2, 3, 0);
        uint[] stampPixels =
        {
            Pack(1, 0, 0, 255), transparent, Pack(3, 0, 0, 255),
            Pack(4, 0, 0, 255), Pack(5, 0, 0, 128), Pack(6, 0, 0, 255),
            Pack(7, 0, 0, 255), Pack(8, 0, 0, 255), Pack(9, 0, 0, 255)
        };
        // Item #6: BakePixels is the LEGACY destructive mode; the default
        // mode places repositionable attachments (covered by
        // SubElementPipelineTests).
        var stamp = new StampTool
        {
            Mode = StampToolMode.BakePixels,
            ActiveSubElement = new SubElement("stamp", 3, 3, stampPixels)
        };
        var destination = new Frame(4, 4);
        for (int i = 0; i < destination.Layers[0].Pixels.Length; i++)
            destination.Layers[0].Pixels[i].Rgba = Pack(20, 30, 40, 255);
        stamp.OnPointerDown(null, destination, 0, 0);
        Require(destination.Layers[0].GetPixel(0, 0).Rgba ==
            ReferenceBlend(Pack(20, 30, 40, 255), stampPixels[4], 255));
        Require(destination.Layers[0].GetPixel(1, 0).R == 6);
        Require(destination.Layers[0].GetPixel(0, 1).R == 8);
        Require(destination.Layers[0].GetPixel(1, 1).R == 9);
        Require(destination.Layers[0].GetPixel(3, 3).R == 20);
        for (int i = 0; i < stampPixels.Length; i++) Require(stamp.ActiveSubElement.Pixels[i] == stampPixels[i]);

        uint[] unchanged = CopyRgba(destination.Layers[0]);
        stamp.ActiveLayerIndex = -1;
        stamp.OnPointerDown(null, destination, 2, 2);
        stamp.ActiveLayerIndex = 0;
        stamp.ActiveSubElement = null;
        stamp.OnPointerDown(null, destination, 2, 2);
        RequireRgbaEqual(unchanged, destination.Layers[0]);
    }

    private static void TestTileAndHitboxReferenceModels()
    {
        int[] tileSizes = { 8, 9, 16, 31, 128 };
        int[] brushSizes = { 1, 2, 3, 7, 33, 256 };
        foreach (int tileSize in tileSizes)
        foreach (int brushSize in brushSizes)
        {
            var frame = new Frame(141, 137);
            var tile = new TileMakerTool
            {
                TileSize = tileSize,
                BrushSize = brushSize,
                CurrentColor = Color(1, 2, 3, 255)
            };
            int x = Math.Min(tileSize + 1, frame.Width - 1);
            int y = Math.Min(tileSize - 1, frame.Height - 1);
            tile.Execute(frame, x, y);

            int effective = Math.Min(Math.Min(brushSize, Math.Max(8, Math.Min(128, tileSize))),
                Math.Max(frame.Width, frame.Height));
            int originX = (x / tile.TileSize) * tile.TileSize;
            int originY = (y / tile.TileSize) * tile.TileSize;
            var expected = new HashSet<int>();
            int min = -(effective / 2);
            int max = min + effective - 1;
            for (int oy = min; oy <= max; oy++)
            for (int ox = min; ox <= max; ox++)
            {
                int px = originX + PositiveMod(x - originX + ox, tile.TileSize);
                int py = originY + PositiveMod(y - originY + oy, tile.TileSize);
                if (px >= 0 && py >= 0 && px < frame.Width && py < frame.Height)
                    expected.Add(py * frame.Width + px);
            }
            for (int index = 0; index < frame.Layers[0].Pixels.Length; index++)
                Require((frame.Layers[0].Pixels[index].A != 0) == expected.Contains(index));
        }

        var hitFrame = new Frame(64, 48);
        var hitbox = new HitboxTool { ActiveHitboxKey = "Attack" };
        hitbox.OnPointerDown(null, hitFrame, 50, 40);
        hitbox.OnPointerDrag(null, hitFrame, -100, -100);
        hitbox.OnPointerUp(null, hitFrame, -100, -100);
        HitboxData value = hitFrame.Hitboxes["Attack"];
        Require(value.X == 0f && value.Y == 0f);
        Require(value.Width == 50f / Config.Hitbox.PixelsPerUnit);
        Require(value.Height == 40f / Config.Hitbox.PixelsPerUnit);

        hitbox.OnPointerDown(null, hitFrame, int.MinValue, int.MaxValue);
        hitbox.OnPointerUp(null, hitFrame, int.MaxValue, int.MinValue);
        value = hitFrame.Hitboxes["Attack"];
        Require(value.X == 0f && value.Y == 0f);
        Require(value.Width == hitFrame.Width / Config.Hitbox.PixelsPerUnit);
        Require(value.Height == hitFrame.Height / Config.Hitbox.PixelsPerUnit);

        hitbox.ActiveHitboxKey = " ";
        int count = hitFrame.Hitboxes.Count;
        hitbox.OnPointerDown(null, hitFrame, 1, 1);
        hitbox.OnPointerUp(null, hitFrame, 2, 2);
        Require(hitFrame.Hitboxes.Count == count);
        hitbox.ActiveHitboxKey = "Later";
        hitbox.OnPointerUp(null, hitFrame, 4, 4);
        Require(!hitFrame.Hitboxes.ContainsKey("Later"));
    }

    private static void TestAnimationGenerationBoundaries()
    {
        var generator = new AnimationGeneratorAPI();
        RequireThrows<ArgumentNullException>(() => generator.GenerateWalkAnimation("x", null, 1, 0, 0, 0));
        RequireThrows<ArgumentOutOfRangeException>(() => generator.GenerateWalkAnimation("x", new Frame(2, 2), 0, 0, 0, 0));
        RequireThrows<ArgumentOutOfRangeException>(() => generator.GenerateWalkAnimation("x", new Frame(2, 2), 1, 3, 0, 0));
        RequireThrows<ArgumentNullException>(() => generator.GenerateJitterAnimation("x", new Frame(2, 2), 1, null, new float[8]));
        RequireThrows<ArgumentException>(() => generator.GenerateJitterAnimation("x", new Frame(2, 2), 1, new float[7], new float[8]));

        var source = new Frame(11, 9, 77);
        for (int i = 0; i < source.Layers[0].Pixels.Length; i++)
            source.Layers[0].Pixels[i].Rgba = (i % 3 == 0) ? Pack((byte)i, 20, 30, 255) : 0u;
        source.Hitboxes["Attack"] = new HitboxData { X = 1, Y = 1, Width = 2, Height = 3 };
        uint[] sourceBefore = CopyRgba(source.Layers[0]);

        AnimationState walk = generator.GenerateWalkAnimation("walk", source, 5, source.Height, 0f, 0f);
        Require(walk.Frames.Count == 5 && walk.FPS == Config.Animation.WalkDefaultFPS);
        AnimationState jitter = generator.GenerateJitterAnimation(
            "jitter", source, 5, new float[8], new float[8]);
        Require(jitter.Frames.Count == 5 && jitter.FPS == Config.Animation.JitterDefaultFPS);
        for (int i = 0; i < 5; i++)
        {
            Require(walk.Frames[i].FrameIndex == i && jitter.Frames[i].FrameIndex == i);
            Require(walk.Frames[i].Hitboxes["Attack"].Height == 3);
            Require(jitter.Frames[i].Hitboxes["Attack"].Width == 2);
            RequireRgbaEqual(sourceBefore, walk.Frames[i].Layers[0]);
            RequireRgbaEqual(sourceBefore, jitter.Frames[i].Layers[0]);
            Require(!ReferenceEquals(walk.Frames[i].Layers[0].Pixels, source.Layers[0].Pixels));
        }
        walk.Frames[0].Layers[0].Pixels[0].Rgba = 123;
        Require(walk.Frames[1].Layers[0].Pixels[0].Rgba == sourceBefore[0]);
        RequireRgbaEqual(sourceBefore, source.Layers[0]);

        var moving = new MovingTool();
        RequireThrows<ArgumentNullException>(() => new MovingTool(null));
        moving.OnPointerDown(null, source, 0, int.MaxValue);
        Require(moving.WalkSplitY == source.Height);
        moving.OnPointerDown(null, source, 0, int.MinValue);
        Require(moving.WalkSplitY == 0);
        moving.ActiveMode = MovingTool.MovementType.ElementJitter;
        moving.JitterFrameCount = 2;
        Require(moving.GenerateAnimation(source).Frames.Count == 2);
    }

    private static void TestSnapshotDeepImmutability()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 2);
            project.DesignerSeed = 0xCAFEBABEu;
            project.AssetProperties["number"] = 12;
            project.AssetProperties["reference"] = new MutableText("before");
            using (JsonDocument document = JsonDocument.Parse("{\"a\":1}"))
                project.AssetProperties["json"] = document.RootElement.Clone();
            project.Animations[0].Frames[0].Layers[0].SetPixel(1, 1, Color(1, 2, 3, 255));
            project.Animations[0].Frames[0].Hitboxes["Attack"] =
                new HitboxData { X = 1, Y = 2, Width = 3, Height = 4 };

            ProjectSnapshot snapshot = ProjectSnapshot.Capture(project);
            project.TargetAssetType = "changed";
            project.UnityProjectPath = "changed";
            project.DesignerSeed = 1;
            project.AssetProperties["number"] = 99;
            project.Animations[0].StateName = "changed";
            project.Animations[0].Frames[0].Layers[0].Clear();
            project.Animations[0].Frames[0].Hitboxes.Clear();
            project.Animations.Clear();

            Require(snapshot.TargetAssetType == Config.Types.AssetTypeEnemyData);
            Require(snapshot.DesignerSeed == 0xCAFEBABEu);
            Require((int)snapshot.AssetProperties["number"] == 12);
            Require((string)snapshot.AssetProperties["reference"] == "before");
            Require(((JsonElement)snapshot.AssetProperties["json"]).GetProperty("a").GetInt32() == 1);
            Require(snapshot.TotalFrameCount == 2);
            Require(snapshot.TotalHitboxCount == 3);
            Require(snapshot.Animations[0].StateName == "Idle");
            Require(snapshot.Animations[0].StartFrame == 0);
            Require(snapshot.Animations[0].Frames[0].Hitboxes.Count == 2);
            var pixels = new PixelColor[snapshot.Animations[0].Frames[0].PixelCount];
            snapshot.Animations[0].Frames[0].CopyPixelsTo(pixels);
            Require(pixels[1 + Config.Canvas.DefaultWidth].Rgba == Pack(1, 2, 3, 255));
            pixels[0].Rgba = 0xFFFFFFFFu;
            var secondCopy = new PixelColor[pixels.Length];
            snapshot.Animations[0].Frames[0].CopyPixelsTo(secondCopy);
            Require(secondCopy[0].Rgba != 0xFFFFFFFFu);
            RequireThrows<ArgumentNullException>(() => snapshot.Animations[0].Frames[0].CopyPixelsTo(null));
            RequireThrows<ArgumentException>(() => snapshot.Animations[0].Frames[0].CopyPixelsTo(new PixelColor[1]));

            Dictionary<string, object> copied = snapshot.CopyAssetProperties();
            copied.Clear();
            Require(snapshot.AssetProperties.Count > 0);
            RequireThrows<NotSupportedException>(() =>
                ((IDictionary<string, object>)snapshot.AssetProperties).Add("bad", 1));
            RequireThrows<NotSupportedException>(() =>
                ((IList<AnimationSnapshot>)snapshot.Animations).Add(null));
            RequireThrows<ArgumentNullException>(() => ProjectSnapshot.Capture(null));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestViewportTransformAndRenderBoundaries()
    {
        var viewport = new ViewportController();
        for (int i = 0; i < 1000; i++) viewport.OnScroll(-1);
        Require(viewport.ZoomLevel == Config.Viewport.MinZoom);
        for (int i = 0; i < 1000; i++) viewport.OnScroll(1);
        Require(viewport.ZoomLevel == Config.Viewport.MaxZoom);
        float stable = viewport.ZoomLevel;
        viewport.OnScroll(float.NaN);
        Require(viewport.ZoomLevel == stable);
        viewport.Pan(-37, 91);
        Require(viewport.OffsetX == -37 && viewport.OffsetY == 91);

        int canvasX;
        int canvasY;
        viewport.ScreenToCanvas(-37, 91, out canvasX, out canvasY);
        Require(canvasX == 0 && canvasY == 0);
        viewport.OnScroll(-1, 163, 291);
        viewport.ScreenToCanvas(163, 291, out canvasX, out canvasY);
        Require(Math.Abs(canvasX - 10) <= 1 && Math.Abs(canvasY - 10) <= 1);

        var frame = new Frame(2, 2);
        frame.Layers[0].Pixels[0].Rgba = Pack(1, 0, 0, 255);
        frame.Layers[0].Pixels[1].Rgba = Pack(2, 0, 0, 255);
        frame.Layers[0].Pixels[2].Rgba = Pack(3, 0, 0, 255);
        frame.Layers[0].Pixels[3].Rgba = Pack(4, 0, 0, 255);
        var identity = new ViewportController();
        uint[] output = new uint[4];
        identity.RenderToScreenBuffer(frame, output, 2, 2);
        Require(output[0] == Pack(1, 0, 0, 255));
        Require(output[1] == Pack(2, 0, 0, 255));
        Require(output[2] == Pack(3, 0, 0, 255));
        Require(output[3] == Pack(4, 0, 0, 255));
        RequireThrows<ArgumentNullException>(() => identity.RenderToScreenBuffer(null, output, 2, 2));
        RequireThrows<ArgumentNullException>(() => identity.RenderToScreenBuffer(frame, null, 2, 2));
        RequireThrows<ArgumentOutOfRangeException>(() => identity.RenderToScreenBuffer(frame, output, 0, 2));
        RequireThrows<ArgumentException>(() => identity.RenderToScreenBuffer(frame, new uint[3], 2, 2));
    }

    private static uint ReferenceBlend(uint destination, uint source, byte opacity)
    {
        uint sourceAlpha = source >> 24;
        sourceAlpha = ((sourceAlpha * opacity) + 127u) / 255u;
        source = (source & 0x00FFFFFFu) | (sourceAlpha << 24);
        if (sourceAlpha == 0) return destination;
        if (sourceAlpha == 255) return source;
        uint destinationAlpha = destination >> 24;
        if (destinationAlpha == 0) return source;
        uint inverse = 255u - sourceAlpha;
        if (destinationAlpha == 255)
        {
            uint red = (((source & 255u) * sourceAlpha) + ((destination & 255u) * inverse) + 127u) / 255u;
            uint green = ((((source >> 8) & 255u) * sourceAlpha) + (((destination >> 8) & 255u) * inverse) + 127u) / 255u;
            uint blue = ((((source >> 16) & 255u) * sourceAlpha) + (((destination >> 16) & 255u) * inverse) + 127u) / 255u;
            return red | (green << 8) | (blue << 16) | 0xFF000000u;
        }
        uint alphaNumerator = sourceAlpha * 255u + destinationAlpha * inverse;
        uint outAlpha = (alphaNumerator + 127u) / 255u;
        uint r = (((source & 255u) * sourceAlpha * 255u) + ((destination & 255u) * destinationAlpha * inverse) + (alphaNumerator >> 1)) / alphaNumerator;
        uint g = ((((source >> 8) & 255u) * sourceAlpha * 255u) + (((destination >> 8) & 255u) * destinationAlpha * inverse) + (alphaNumerator >> 1)) / alphaNumerator;
        uint b = ((((source >> 16) & 255u) * sourceAlpha * 255u) + (((destination >> 16) & 255u) * destinationAlpha * inverse) + (alphaNumerator >> 1)) / alphaNumerator;
        return r | (g << 8) | (b << 16) | (outAlpha << 24);
    }

    private static uint NextUInt(Random random)
    {
        return (uint)random.Next(1 << 16) | ((uint)random.Next(1 << 16) << 16);
    }

    private static uint[] CopyRgba(Layer layer)
    {
        var result = new uint[layer.Pixels.Length];
        for (int i = 0; i < result.Length; i++) result[i] = layer.Pixels[i].Rgba;
        return result;
    }

    private static void RequireRgbaEqual(uint[] expected, Layer actual)
    {
        Require(expected.Length == actual.Pixels.Length);
        for (int i = 0; i < expected.Length; i++) Require(expected[i] == actual.Pixels[i].Rgba);
    }

    private static void ReferenceLine(int x0, int y0, int x1, int y1, int width, Action<int, int> plot)
    {
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            plot(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int doubled = 2 * error;
            if (doubled >= dy) { error += dy; x0 += sx; }
            if (doubled <= dx) { error += dx; y0 += sy; }
        }
    }

    private static int PositiveMod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private sealed class MutableText
    {
        private readonly string value;
        public MutableText(string value) { this.value = value; }
        public override string ToString() => value;
    }
}
