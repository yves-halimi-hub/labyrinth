using System;
using System.Collections.Generic;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Tools;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using SharedBackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;
using ToolsBrushShape = EFYVBackend.Core.Math.Algorithms.BrushShape;

internal static partial class Program
{
    private static void TestToolsPencilThickLineReferenceModel()
    {
        var random = new Random(0x70015EED);
        ToolsBrushShape[] shapes = { ToolsBrushShape.Square, ToolsBrushShape.Circle };
        for (int iteration = 0; iteration < 48; iteration++)
        {
            int width = iteration == 0 ? 1 : 3 + random.Next(28);
            int height = iteration == 1 ? 1 : 3 + random.Next(22);
            int brush = 1 + random.Next(9);
            ToolsBrushShape shape = shapes[random.Next(2)];
            uint ink = Pack(
                (byte)random.Next(256),
                (byte)random.Next(256),
                (byte)random.Next(256),
                (byte)(1 + random.Next(255)));
            var frame = new Frame(width, height);
            var pencil = new PencilTool
            {
                BrushSize = brush,
                BrushShape = shape,
                CurrentColor = new PixelColor { Rgba = ink }
            };
            Require(pencil.BrushSize == brush);

            var expected = new bool[width * height];
            int anchorX = random.Next(width);
            int anchorY = random.Next(height);
            pencil.OnPointerDown(null, frame, anchorX, anchorY);
            ToolsRefPencilSegment(expected, width, height, anchorX, anchorY, anchorX, anchorY, brush, shape);

            for (int step = 0; step < 4; step++)
            {
                if (random.Next(3) == 0)
                {
                    int offX = random.Next(2) == 0 ? -1 - random.Next(5) : width + random.Next(5);
                    pencil.OnPointerDrag(null, frame, offX, random.Next(height));
                    continue;
                }
                int nextX = random.Next(width);
                int nextY = random.Next(height);
                pencil.OnPointerDrag(null, frame, nextX, nextY);
                ToolsRefPencilSegment(expected, width, height, anchorX, anchorY, nextX, nextY, brush, shape);
                anchorX = nextX;
                anchorY = nextY;
            }

            for (int index = 0; index < expected.Length; index++)
                Require(frame.Layers[0].Pixels[index].Rgba == (expected[index] ? ink : 0u));

            pencil.OnPointerUp(null, frame, anchorX, anchorY);
            uint[] settled = CopyRgba(frame.Layers[0]);
            pencil.OnPointerDrag(null, frame, width / 2, height / 2);
            RequireRgbaEqual(settled, frame.Layers[0]);
        }

        var pencilClamp = new PencilTool();
        pencilClamp.BrushSize = 0;
        Require(pencilClamp.BrushSize == Config.Tool.Pencil.DefaultBrushSize);
        pencilClamp.BrushSize = -100;
        Require(pencilClamp.BrushSize == Config.Tool.Pencil.DefaultBrushSize);
        pencilClamp.BrushSize = int.MinValue;
        Require(pencilClamp.BrushSize == Config.Tool.Pencil.DefaultBrushSize);
        pencilClamp.BrushSize = Config.Tool.MaxBrushSize;
        Require(pencilClamp.BrushSize == Config.Tool.MaxBrushSize);

        uint[] adversarialColors = { 0x80000000u, 0xFFFFFFFFu, 0x7FFFFFFFu, 0x00000001u, 0xDEADBEEFu, 0u };
        foreach (uint value in adversarialColors)
        {
            pencilClamp.CurrentColor = new PixelColor { Rgba = value };
            Require(pencilClamp.CurrentColor.Rgba == value);
        }
        pencilClamp.ActiveLayerIndex = int.MinValue;
        Require(pencilClamp.ActiveLayerIndex == int.MinValue);

        // Gesture never starts when the layer guard fails at pointer-down time.
        var guardFrame = new Frame(7, 7);
        var guardPencil = new PencilTool { CurrentColor = Color(9, 9, 9, 255) };
        guardPencil.ActiveLayerIndex = guardFrame.Layers.Count;
        guardPencil.OnPointerDown(null, guardFrame, 3, 3);
        guardPencil.ActiveLayerIndex = 0;
        guardPencil.OnPointerDrag(null, guardFrame, 5, 5);
        Require(CountOpaque(guardFrame.Layers[0]) == 0);

        // The effective brush is capped by MAX(width, height): a 5-wide brush on a 1x9
        // canvas therefore stays THICK and floods rows above/below the stroke; only a
        // 1x1 canvas degrades a thick brush to the thin single-pixel path.
        var thickNarrow = new Frame(1, 9);
        var thickPencil = new PencilTool
        {
            BrushSize = 5,
            BrushShape = ToolsBrushShape.Square,
            CurrentColor = Color(1, 1, 1, 255)
        };
        thickPencil.OnPointerDown(null, thickNarrow, 0, 2);
        thickPencil.OnPointerDrag(null, thickNarrow, 0, 6);
        for (int y = 0; y < 9; y++)
            Require(thickNarrow.Layers[0].GetPixel(0, y).A != 0);

        var thinFrame = new Frame(1, 1);
        var thinPencil = new PencilTool
        {
            BrushSize = 5,
            BrushShape = ToolsBrushShape.Circle,
            CurrentColor = Color(1, 1, 1, 255)
        };
        thinPencil.OnPointerDown(null, thinFrame, 0, 0);
        Require(thinFrame.Layers[0].Pixels[0].Rgba == Pack(1, 1, 1, 255));
    }

    private static void TestToolsFillFloodReferenceModel()
    {
        var random = new Random(0x0F111ED);
        uint[] palette = { 0u, Pack(200, 0, 0, 255), Pack(0, 200, 0, 128) };
        uint replacement = Pack(1, 2, 3, 255);
        for (int iteration = 0; iteration < 30; iteration++)
        {
            int width = 1 + random.Next(40);
            int height = 1 + random.Next(28);
            var frame = new Frame(width, height);
            Layer layer = frame.Layers[0];
            for (int index = 0; index < layer.Pixels.Length; index++)
                layer.Pixels[index].Rgba = palette[random.Next(palette.Length)];

            int startX = random.Next(width);
            int startY = random.Next(height);
            uint[] expected = CopyRgba(layer);
            ToolsRefFloodFill(expected, width, height, startX, startY, replacement);

            var fill = new FillTool { CurrentColor = new PixelColor { Rgba = replacement } };
            fill.OnPointerDown(null, frame, startX, startY);
            RequireRgbaEqual(expected, layer);

            // Refilling the same start point with the same color is a no-op.
            fill.OnPointerDown(null, frame, startX, startY);
            RequireRgbaEqual(expected, layer);
        }

        // Pixels with equal RGB but different alpha are distinct fill regions (exact uint equality).
        var alphaFrame = new Frame(3, 1);
        alphaFrame.Layers[0].Pixels[0].Rgba = Pack(5, 5, 5, 255);
        alphaFrame.Layers[0].Pixels[1].Rgba = Pack(5, 5, 5, 254);
        alphaFrame.Layers[0].Pixels[2].Rgba = Pack(5, 5, 5, 255);
        var alphaFill = new FillTool { CurrentColor = Color(7, 7, 7, 255) };
        alphaFill.OnPointerDown(null, alphaFrame, 0, 0);
        Require(alphaFrame.Layers[0].Pixels[0].Rgba == Pack(7, 7, 7, 255));
        Require(alphaFrame.Layers[0].Pixels[1].Rgba == Pack(5, 5, 5, 254));
        Require(alphaFrame.Layers[0].Pixels[2].Rgba == Pack(5, 5, 5, 255));

        // Fill targets only the active layer.
        var layered = new Frame(4, 4);
        layered.Layers.Add(new Layer("upper", 4, 4));
        var layeredFill = new FillTool { CurrentColor = Color(0, 0, 9, 255) };
        layeredFill.ActiveLayerIndex = 1;
        layeredFill.OnPointerDown(null, layered, 2, 2);
        Require(CountOpaque(layered.Layers[0]) == 0);
        Require(CountOpaque(layered.Layers[1]) == 16);

        // Null frame and 1x1 canvas boundaries.
        layeredFill.OnPointerDown(null, null, 0, 0);
        var single = new Frame(1, 1);
        var singleFill = new FillTool { CurrentColor = Color(3, 2, 1, 255) };
        singleFill.OnPointerDown(null, single, 0, 0);
        Require(single.Layers[0].Pixels[0].Rgba == Pack(3, 2, 1, 255));
    }

    private static void TestToolsStampBlitReferenceModel()
    {
        var random = new Random(0x57A3B17);
        for (int iteration = 0; iteration < 40; iteration++)
        {
            int stampWidth = 1 + random.Next(6);
            int stampHeight = 1 + random.Next(6);
            int destWidth = 1 + random.Next(12);
            int destHeight = 1 + random.Next(12);

            var stampPixels = new uint[stampWidth * stampHeight];
            for (int index = 0; index < stampPixels.Length; index++)
            {
                int kind = random.Next(4);
                byte alpha = kind == 0 ? (byte)0 : (kind == 1 ? (byte)255 : (byte)(1 + random.Next(255)));
                stampPixels[index] = Pack(
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    alpha);
            }

            var frame = new Frame(destWidth, destHeight);
            Layer layer = frame.Layers[0];
            for (int index = 0; index < layer.Pixels.Length; index++)
            {
                int kind = random.Next(4);
                byte alpha = kind == 0 ? (byte)0 : (kind == 1 ? (byte)255 : (byte)(1 + random.Next(255)));
                layer.Pixels[index].Rgba = Pack(
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    alpha);
            }

            int pointerX = random.Next(-6, destWidth + 6);
            int pointerY = random.Next(-6, destHeight + 6);
            uint[] expected = CopyRgba(layer);
            int stampX = pointerX - (stampWidth >> Config.Tool.Stamp.CenterDivisorPower);
            int stampY = pointerY - (stampHeight >> Config.Tool.Stamp.CenterDivisorPower);
            for (int sourceY = 0; sourceY < stampHeight; sourceY++)
            {
                int destY = stampY + sourceY;
                if (destY < 0 || destY >= destHeight) continue;
                for (int sourceX = 0; sourceX < stampWidth; sourceX++)
                {
                    int destX = stampX + sourceX;
                    if (destX < 0 || destX >= destWidth) continue;
                    uint source = stampPixels[sourceY * stampWidth + sourceX];
                    if ((byte)(source >> 24) == 0) continue;
                    int destIndex = destY * destWidth + destX;
                    expected[destIndex] = ReferenceBlend(expected[destIndex], source, 255);
                }
            }

            var stamp = new StampTool
            {
                Mode = StampToolMode.BakePixels, // legacy destructive mode (item #6)
                ActiveSubElement = new SubElement("brush", stampWidth, stampHeight, stampPixels)
            };
            stamp.OnPointerDown(null, frame, pointerX, pointerY);
            RequireRgbaEqual(expected, layer);
            for (int index = 0; index < stampPixels.Length; index++)
                Require(stamp.ActiveSubElement.Pixels[index] == stampPixels[index]);
        }

        // Stamps target only the active layer; a null frame is ignored.
        var layered = new Frame(5, 5);
        layered.Layers.Add(new Layer("upper", 5, 5));
        var layeredStamp = new StampTool
        {
            Mode = StampToolMode.BakePixels, // legacy destructive mode (item #6)
            ActiveSubElement = new SubElement("dot", 1, 1, new[] { Pack(9, 8, 7, 255) })
        };
        layeredStamp.ActiveLayerIndex = 1;
        layeredStamp.OnPointerDown(null, null, 2, 2);
        layeredStamp.OnPointerDown(null, layered, 2, 2);
        Require(CountOpaque(layered.Layers[0]) == 0);
        Require(layered.Layers[1].GetPixel(2, 2).Rgba == Pack(9, 8, 7, 255));
    }

    private static void TestToolsHitboxGestureStateMachine()
    {
        var frame = new Frame(24, 18);
        var tool = new HitboxTool();
        Require(tool.ActiveHitboxKey == Config.Hitbox.DefaultKeyHurtbox);

        var random = new Random(0x417B0);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            string key = "Gesture" + (iteration % 5);
            tool.ActiveHitboxKey = key;
            int startX = random.Next(-30, 60);
            int startY = random.Next(-30, 60);
            tool.OnPointerDown(null, frame, startX, startY);
            int dragCount = random.Next(3);
            for (int drag = 0; drag < dragCount; drag++)
            {
                int dragX = random.Next(-30, 60);
                int dragY = random.Next(-30, 60);
                tool.OnPointerDrag(null, frame, dragX, dragY);
                ToolsRequireHitbox(frame, key, startX, startY, dragX, dragY);
            }
            int upX = random.Next(-30, 60);
            int upY = random.Next(-30, 60);
            tool.OnPointerUp(null, frame, upX, upY);
            ToolsRequireHitbox(frame, key, startX, startY, upX, upY);
        }

        // Drag without a preceding down is inert.
        var freshFrame = new Frame(16, 16);
        var fresh = new HitboxTool { ActiveHitboxKey = "Orphan" };
        fresh.OnPointerDrag(null, freshFrame, 3, 3);
        Require(!freshFrame.Hitboxes.ContainsKey("Orphan"));

        // Down with a null frame never arms the gesture.
        fresh.OnPointerDown(null, null, 1, 1);
        fresh.OnPointerUp(null, freshFrame, 9, 9);
        Require(!freshFrame.Hitboxes.ContainsKey("Orphan"));

        // Mid-gesture null frames are skipped without ending the gesture.
        fresh.OnPointerDown(null, freshFrame, 2, 2);
        fresh.OnPointerDrag(null, null, 14, 14);
        Require(!freshFrame.Hitboxes.ContainsKey("Orphan"));
        fresh.OnPointerUp(null, freshFrame, 10, 6);
        ToolsRequireHitbox(freshFrame, "Orphan", 2, 2, 10, 6);

        // Documents current behavior: switching the key mid-gesture writes the rectangle
        // (anchored at the original down point) under the NEW key and leaves the old key alone.
        var switching = new HitboxTool { ActiveHitboxKey = "First" };
        var switchFrame = new Frame(32, 32);
        switching.OnPointerDown(null, switchFrame, 4, 4);
        switching.ActiveHitboxKey = "Second";
        switching.OnPointerUp(null, switchFrame, 12, 20);
        Require(!switchFrame.Hitboxes.ContainsKey("First"));
        ToolsRequireHitbox(switchFrame, "Second", 4, 4, 12, 20);

        // Nulling the key mid-gesture suppresses the write entirely but still ends the gesture.
        switching.ActiveHitboxKey = "Third";
        switching.OnPointerDown(null, switchFrame, 1, 1);
        switching.ActiveHitboxKey = null;
        switching.OnPointerUp(null, switchFrame, 9, 9);
        Require(!switchFrame.Hitboxes.ContainsKey("Third"));
        switching.ActiveHitboxKey = "Third";
        switching.OnPointerDrag(null, switchFrame, 5, 5);
        Require(!switchFrame.Hitboxes.ContainsKey("Third"));

        // A zero-area gesture produces a zero-size hitbox at the point.
        var pointTool = new HitboxTool { ActiveHitboxKey = "Point" };
        pointTool.OnPointerDown(null, switchFrame, 8, 8);
        pointTool.OnPointerUp(null, switchFrame, 8, 8);
        HitboxData point = switchFrame.Hitboxes["Point"];
        Require(point.X == 8f / Config.Hitbox.PixelsPerUnit);
        Require(point.Y == 8f / Config.Hitbox.PixelsPerUnit);
        Require(point.Width == 0f && point.Height == 0f);
    }

    private static void TestToolsTileMakerWrapModelAndGuards()
    {
        var clamped = new TileMakerTool();
        Require(clamped.TileSize == Config.Tool.TileMaker.DefaultTileSize);
        Require(clamped.BrushSize == Config.Tool.Pencil.DefaultBrushSize);
        clamped.TileSize = 0;
        Require(clamped.TileSize == Config.Tool.TileMaker.MinTileSize);
        clamped.TileSize = Config.Tool.TileMaker.MinTileSize - 1;
        Require(clamped.TileSize == Config.Tool.TileMaker.MinTileSize);
        clamped.TileSize = int.MinValue;
        Require(clamped.TileSize == Config.Tool.TileMaker.MinTileSize);
        clamped.TileSize = Config.Tool.TileMaker.MaxTileSize + 1;
        Require(clamped.TileSize == Config.Tool.TileMaker.MaxTileSize);
        clamped.TileSize = int.MaxValue;
        Require(clamped.TileSize == Config.Tool.TileMaker.MaxTileSize);
        clamped.TileSize = Config.Tool.TileMaker.MinTileSize;
        Require(clamped.TileSize == Config.Tool.TileMaker.MinTileSize);
        clamped.BrushSize = 0;
        Require(clamped.BrushSize == Config.Tool.Pencil.DefaultBrushSize);
        clamped.BrushSize = int.MinValue;
        Require(clamped.BrushSize == Config.Tool.Pencil.DefaultBrushSize);

        // Out-of-bounds anchors are rejected outright.
        var guardFrame = new Frame(16, 16);
        var guardTool = new TileMakerTool { CurrentColor = Color(2, 2, 2, 255) };
        guardTool.Execute(guardFrame, -1, 0);
        guardTool.Execute(guardFrame, 0, -1);
        guardTool.Execute(guardFrame, 16, 0);
        guardTool.Execute(guardFrame, 0, 16);
        guardTool.Execute(null, 3, 3);
        Require(CountOpaque(guardFrame.Layers[0]) == 0);

        // OnPointerDown and OnPointerDrag are exact aliases of Execute.
        var executed = new Frame(20, 20);
        var down = new Frame(20, 20);
        var dragged = new Frame(20, 20);
        var alias = new TileMakerTool { TileSize = 8, BrushSize = 3, CurrentColor = Color(4, 5, 6, 255) };
        alias.Execute(executed, 9, 9);
        alias.OnPointerDown(null, down, 9, 9);
        alias.OnPointerDrag(null, dragged, 9, 9);
        uint[] executedPixels = CopyRgba(executed.Layers[0]);
        RequireRgbaEqual(executedPixels, down.Layers[0]);
        RequireRgbaEqual(executedPixels, dragged.Layers[0]);

        // Randomized multi-stroke reference model with wrapping at tile boundaries.
        var random = new Random(0x711EAA);
        int[] tileSizes = { 8, 9, 16 };
        for (int configuration = 0; configuration < 6; configuration++)
        {
            const int width = 37;
            const int height = 29;
            var frame = new Frame(width, height);
            var tool = new TileMakerTool
            {
                TileSize = tileSizes[configuration % tileSizes.Length],
                BrushSize = 1 + random.Next(9),
                CurrentColor = Color(1, 2, 3, 255)
            };
            var expected = new HashSet<int>();
            for (int stroke = 0; stroke < 30; stroke++)
            {
                int x = random.Next(width);
                int y = random.Next(height);
                tool.Execute(frame, x, y);

                int tileSize = tool.TileSize;
                int originX = (x / tileSize) * tileSize;
                int originY = (y / tileSize) * tileSize;
                int effective = Math.Min(
                    Math.Min(tool.BrushSize, tileSize),
                    Math.Max(width, height));
                int minimumOffset = -(effective >> 1);
                int maximumOffset = minimumOffset + effective - 1;
                for (int offsetY = minimumOffset; offsetY <= maximumOffset; offsetY++)
                {
                    int pixelY = originY + PositiveMod(y - originY + offsetY, tileSize);
                    if (pixelY < 0 || pixelY >= height) continue;
                    for (int offsetX = minimumOffset; offsetX <= maximumOffset; offsetX++)
                    {
                        int pixelX = originX + PositiveMod(x - originX + offsetX, tileSize);
                        if (pixelX >= 0 && pixelX < width) expected.Add(pixelY * width + pixelX);
                    }
                }
            }
            for (int index = 0; index < frame.Layers[0].Pixels.Length; index++)
                Require((frame.Layers[0].Pixels[index].A != 0) == expected.Contains(index));
        }
    }

    private static void TestToolsMovingToolDefaultsAndGeneratorEquivalence()
    {
        var moving = new MovingTool();
        Require(moving.ActiveMode == MovingTool.MovementType.ToonWalk);
        Require(moving.WalkSplitY == Config.Tool.Moving.DefaultWalkSplitY);
        Require(moving.WalkBounceAmp == Config.Tool.Moving.DefaultWalkBounceAmp);
        Require(moving.WalkStrideAmp == Config.Tool.Moving.DefaultWalkStrideAmp);
        Require(moving.WalkFrameCount == Config.Tool.Moving.DefaultWalkFrameCount);
        Require(moving.JitterFrameCount == Config.Tool.Moving.DefaultJitterFrameCount);
        for (int index = 0; index < Config.Tool.Moving.JitterOctantCount; index++)
        {
            Require(moving.GetJitterAmplitude(index) == Config.Tool.Moving.DefaultJitterAmp);
            Require(moving.GetJitterFrequency(index) == Config.Tool.Moving.DefaultJitterFreq);
        }
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GetJitterFrequency(-1));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            moving.GetJitterFrequency(Config.Tool.Moving.JitterOctantCount));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            moving.GetJitterAmplitude(Config.Tool.Moving.JitterOctantCount));
        RequireThrows<ArgumentOutOfRangeException>(() => moving.SetJitterAmplitude(-1, 1f));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            moving.SetJitterAmplitude(Config.Tool.Moving.JitterOctantCount, 1f));
        RequireThrows<ArgumentOutOfRangeException>(() => moving.SetJitterFrequency(-1, 1f));

        var source = new Frame(12, 10);
        var random = new Random(0x30017);
        for (int index = 0; index < source.Layers[0].Pixels.Length; index++)
            source.Layers[0].Pixels[index].Rgba = random.Next(3) == 0
                ? 0u
                : Pack((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
        var upper = new Layer("upper", 12, 10) { Opacity = 0.5f };
        for (int index = 0; index < upper.Pixels.Length; index++)
            upper.Pixels[index].Rgba = random.Next(2) == 0
                ? 0u
                : Pack((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        source.Layers.Add(upper);
        source.Hitboxes["Kick"] = new HitboxData { X = 0.5f, Y = 0.25f, Width = 0.25f, Height = 0.125f };

        // ToonWalk output must be byte-identical to calling the generator API directly.
        moving.WalkFrameCount = 4;
        moving.WalkSplitY = 6;
        moving.WalkBounceAmp = 1.25f;
        moving.WalkStrideAmp = 3.5f;
        AnimationState actualWalk = moving.GenerateAnimation(source);
        AnimationState expectedWalk = new AnimationGeneratorAPI().GenerateWalkAnimation(
            Config.Animation.WalkAnimName, source, 4, 6, 1.25f, 3.5f);
        Require(actualWalk.StateName == Config.Animation.WalkAnimName);
        Require(actualWalk.FPS == Config.Animation.WalkDefaultFPS);
        Require(actualWalk.Frames.Count == 4);
        for (int frameIndex = 0; frameIndex < 4; frameIndex++)
        {
            Require(actualWalk.Frames[frameIndex].FrameIndex == frameIndex);
            Require(actualWalk.Frames[frameIndex].Layers.Count == 1);
            RequireRgbaEqual(
                CopyRgba(expectedWalk.Frames[frameIndex].Layers[0]),
                actualWalk.Frames[frameIndex].Layers[0]);
            Require(actualWalk.Frames[frameIndex].Hitboxes.Count == source.Hitboxes.Count);
            Require(actualWalk.Frames[frameIndex].Hitboxes["Kick"].Width == 0.25f);
        }

        // ElementJitter honors the per-octant gauge values exactly.
        moving.ActiveMode = MovingTool.MovementType.ElementJitter;
        moving.JitterFrameCount = 3;
        var amplitudes = new float[Config.Tool.Moving.JitterOctantCount];
        var frequencies = new float[Config.Tool.Moving.JitterOctantCount];
        for (int index = 0; index < Config.Tool.Moving.JitterOctantCount; index++)
        {
            amplitudes[index] = 0.5f + (index * 0.75f);
            frequencies[index] = 1f + (index * 0.5f);
            moving.SetJitterAmplitude(index, amplitudes[index]);
            moving.SetJitterFrequency(index, frequencies[index]);
            Require(moving.GetJitterAmplitude(index) == amplitudes[index]);
            Require(moving.GetJitterFrequency(index) == frequencies[index]);
        }
        AnimationState actualJitter = moving.GenerateAnimation(source);
        AnimationState expectedJitter = new AnimationGeneratorAPI().GenerateJitterAnimation(
            Config.Animation.JitterAnimName, source, 3, amplitudes, frequencies);
        Require(actualJitter.StateName == Config.Animation.JitterAnimName);
        Require(actualJitter.FPS == Config.Animation.JitterDefaultFPS);
        Require(actualJitter.Frames.Count == 3);
        for (int frameIndex = 0; frameIndex < 3; frameIndex++)
        {
            Require(actualJitter.Frames[frameIndex].FrameIndex == frameIndex);
            RequireRgbaEqual(
                CopyRgba(expectedJitter.Frames[frameIndex].Layers[0]),
                actualJitter.Frames[frameIndex].Layers[0]);
        }

        // Pointer-down only captures the split line in ToonWalk mode with a live frame.
        int preservedSplit = moving.WalkSplitY;
        moving.OnPointerDown(null, source, 0, 3);
        Require(moving.WalkSplitY == preservedSplit);
        moving.ActiveMode = MovingTool.MovementType.ToonWalk;
        moving.OnPointerDown(null, null, 0, 3);
        Require(moving.WalkSplitY == preservedSplit);
        moving.OnPointerDown(null, source, 123, 7);
        Require(moving.WalkSplitY == 7);

        // Exception contracts surface through the tool facade.
        RequireThrows<ArgumentNullException>(() => moving.GenerateAnimation(null));
        moving.WalkFrameCount = 0;
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GenerateAnimation(source));
        moving.WalkFrameCount = 4;
        moving.WalkSplitY = source.Height + 1;
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GenerateAnimation(source));
        moving.WalkSplitY = -1;
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GenerateAnimation(source));
        moving.WalkSplitY = source.Height;
        Require(moving.GenerateAnimation(source).Frames.Count == 4);
        moving.ActiveMode = MovingTool.MovementType.ElementJitter;
        moving.JitterFrameCount = 0;
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GenerateAnimation(source));
    }

    private static void TestToolsMapToolRngReferenceAndSequencing()
    {
        // Factory defaults.
        var defaults = new MapTool();
        Require(defaults.Seed == Config.Tool.Map.DefaultSeed);
        Require(defaults.OperationIndex == 0);
        Require(defaults.ScatterRadius == Config.Tool.Map.DefaultScatterRadius);
        Require(defaults.ScatterDensity == Config.Tool.Map.DefaultScatterDensity);
        Require(defaults.MinScaleJitter == Config.Tool.Map.MinScaleJitter);
        Require(defaults.MaxScaleJitter == Config.Tool.Map.MaxScaleJitter);
        Require(defaults.Mode == Config.Tool.Map.ModeScatter);
        Require(defaults.FillProbability == Config.Tool.Map.DefaultFillProbability);
        Require(defaults.BaseTileId == Config.Tool.Map.DefaultBaseTileId);
        Require(defaults.TargetTileId == Config.Tool.Map.DefaultTargetTileId);
        defaults.FillProbability = 2f;
        Require(defaults.FillProbability == 1f);
        defaults.FillProbability = -0.5f;
        Require(defaults.FillProbability == 0f);
        defaults.ScatterRadius = -3f;
        Require(defaults.ScatterRadius == 0f);

        // Full white-box reference model of the scatter RNG pipeline.
        var map = new FastGridMap(50, 40);
        var scatter = new MapTool
        {
            TargetMap = map,
            SelectedAsset = "Rock",
            Mode = Config.Tool.Map.ModeScatter,
            Seed = 0xBEEFCAFEu,
            ScatterDensity = 9,
            ScatterRadius = 4.5f,
            MinScaleJitter = 0.8f,
            MaxScaleJitter = 1.3f
        };
        MapOperationResult lastEvent = default;
        int eventCount = 0;
        scatter.OperationCompleted += result =>
        {
            eventCount++;
            lastEvent = result;
        };
        var model = new ToolsMapRngModel();
        for (int operation = 0; operation < 4; operation++)
        {
            int centerX = 12 + (operation * 3);
            int centerY = 20 - (operation * 2);
            MapOperationResult result = scatter.Execute(null, centerX, centerY);
            uint expectedSeed = model.Begin(0xBEEFCAFEu, operation);
            Require(result.Status == MapOperationStatus.Succeeded);
            Require(result.Mode == Config.Tool.Map.ModeScatter);
            Require(result.OperationSeed == expectedSeed);
            Require(result.AffectedCount == 9);
            Require(lastEvent.OperationSeed == expectedSeed && lastEvent.AffectedCount == 9);
            for (int index = 0; index < 9; index++)
            {
                float offsetX;
                float offsetY;
                model.Offset2D(4.5f, out offsetX, out offsetY);
                float scale = model.NextRange(0.8f, 1.3f);
                FastGridMap.MapPropData prop = map.Props[(operation * 9) + index];
                Require(prop.AssetKey == "Rock");
                Require(prop.X == centerX + (int)offsetX);
                Require(prop.Y == centerY + (int)offsetY);
                Require(prop.Scale == scale);
                Require(prop.Scale >= 0.8f && prop.Scale <= 1.3f);
                Require(Math.Abs(prop.X - centerX) <= 5 && Math.Abs(prop.Y - centerY) <= 5);
            }
        }
        Require(scatter.OperationIndex == 4 && eventCount == 4);

        // Re-assigning the SAME seed must not reset the sequence; a new seed must.
        scatter.Seed = 0xBEEFCAFEu;
        Require(scatter.OperationIndex == 4);
        scatter.Seed = 123u;
        Require(scatter.OperationIndex == 0);

        // Zero scatter density succeeds with no props placed.
        int propCount = map.Props.Count;
        scatter.ScatterDensity = 0;
        MapOperationResult empty = scatter.Execute(null, 5, 5);
        Require(empty.Status == MapOperationStatus.Succeeded && empty.AffectedCount == 0);
        Require(map.Props.Count == propCount && scatter.OperationIndex == 1);

        // Radius zero pins every prop exactly on the pointer.
        scatter.ScatterDensity = 5;
        scatter.ScatterRadius = 0f;
        scatter.Execute(null, 7, 9);
        Require(map.Props.Count == propCount + 5);
        for (int index = propCount; index < map.Props.Count; index++)
        {
            Require(map.Props[index].X == 7 && map.Props[index].Y == 9);
            Require(map.Props[index].Scale >= 0.8f && map.Props[index].Scale <= 1.3f);
        }

        // Noise fill: exact per-cell reference including the changed-count semantics.
        var noiseMap = new FastGridMap(9, 7);
        for (int index = 0; index < noiseMap.RawData.Length; index++)
            noiseMap.RawData[index] = (short)(index % 4 == 0 ? 5 : 0);
        var noise = new MapTool
        {
            TargetMap = noiseMap,
            Mode = Config.Tool.Map.ModeNoiseFill,
            Seed = 777u,
            FillProbability = 0.6f,
            TargetTileId = 5
        };
        var noiseModel = new ToolsMapRngModel();
        noiseModel.Begin(777u, 0);
        var expectedCells = (short[])noiseMap.RawData.Clone();
        int expectedChanged = 0;
        for (int index = 0; index < expectedCells.Length; index++)
        {
            if (noiseModel.NextUnitFloat() >= 0.6f) continue;
            if (expectedCells[index] != 5) expectedChanged++;
            expectedCells[index] = 5;
        }
        MapOperationResult noiseResult = noise.Execute(null, 0, 0);
        Require(noiseResult.Status == MapOperationStatus.Succeeded);
        Require(noiseResult.AffectedCount == expectedChanged);
        Require(expectedChanged > 0 && expectedChanged < expectedCells.Length);
        for (int index = 0; index < expectedCells.Length; index++)
            Require(noiseMap.RawData[index] == expectedCells[index]);

        // A colliding seed/index pair that XORs to zero falls back to the reserved seed.
        var fallback = new MapTool
        {
            TargetMap = new FastGridMap(2, 2),
            Mode = Config.Tool.Map.ModeNoiseFill,
            Seed = BackendConfig.Math.FnvPrime * (uint)SharedBackendConfig.UnitStep
        };
        Require(fallback.Execute(null, 0, 0).OperationSeed == BackendConfig.Random.FallbackSeed);

        // Tile mode: missing asset is reported per-mode. FIXED snapping: coordinates now
        // FLOOR-snap (integer division used to truncate toward zero, so -63 snapped to -32
        // instead of -64) and the snapped origin is clamped onto the map, so negative or
        // far-out pointer coordinates can no longer place props outside the map.
        var tileMap = new FastGridMap(4, 4);
        var tile = new MapTool { TargetMap = tileMap, Mode = Config.Tool.Map.ModeTile };
        MapOperationResult missingAsset = tile.Execute(null, 0, 0);
        Require(missingAsset.Status == MapOperationStatus.MissingSelectedAsset);
        Require(missingAsset.Mode == Config.Tool.Map.ModeTile && missingAsset.AffectedCount == 0);
        Require(tileMap.Props.Count == 0);
        tile.SelectedAsset = "Pillar";
        MapOperationResult negative = tile.Execute(null, -63, -1);
        Require(negative.Status == MapOperationStatus.Succeeded && negative.AffectedCount == 1);
        Require(tileMap.Props.Count == 1);
        Require(tileMap.Props[0].X == 0 && tileMap.Props[0].Y == 0);
        Require(tileMap.Props[0].Scale == Config.Tool.Map.DefaultObjectScale);
        // In-map coordinates keep the historical tile-aligned snap...
        int toolTileSize = Config.Tool.TileMaker.DefaultTileSize;
        tile.Execute(null, toolTileSize * 2 + 1, toolTileSize - 1);
        Require(tileMap.Props[1].X == toolTileSize * 2 && tileMap.Props[1].Y == 0);
        // ...and coordinates beyond the far edge clamp to the last tile's origin
        // ((Width-1) * tileSize; Width/Height are tile counts).
        tile.Execute(null, toolTileSize * 100, -1000000);
        Require(tileMap.Props[2].X == (tileMap.Width - 1) * toolTileSize && tileMap.Props[2].Y == 0);
        // An exactly tile-aligned negative coordinate floor-snaps to itself before clamping.
        tile.Execute(null, -toolTileSize, toolTileSize);
        Require(tileMap.Props[3].X == 0 && tileMap.Props[3].Y == toolTileSize);

        // Automata smooth: naive 8-neighbour reference (map edges count as target), applied twice
        // to also exercise the internal buffer reuse path.
        var autoRandom = new Random(0xA07A);
        var autoMap = new FastGridMap(11, 9);
        for (int index = 0; index < autoMap.RawData.Length; index++)
            autoMap.RawData[index] = (short)(autoRandom.Next(2) == 0 ? 3 : 0);
        var smooth = new MapTool
        {
            TargetMap = autoMap,
            Mode = Config.Tool.Map.ModeAutomataSmooth,
            TargetTileId = 3,
            BaseTileId = 0
        };
        short[] expectedPass = ToolsRefAutomataPass(autoMap.RawData, 11, 9, 3, 0);
        MapOperationResult firstPass = smooth.Execute(null, 0, 0);
        Require(firstPass.Status == MapOperationStatus.Succeeded);
        Require(firstPass.AffectedCount == autoMap.RawData.Length);
        for (int index = 0; index < expectedPass.Length; index++)
            Require(autoMap.RawData[index] == expectedPass[index]);
        expectedPass = ToolsRefAutomataPass(expectedPass, 11, 9, 3, 0);
        smooth.Execute(null, 0, 0);
        for (int index = 0; index < expectedPass.Length; index++)
            Require(autoMap.RawData[index] == expectedPass[index]);

        // Pointer-down without a project keeps the current seed; a project whose seed already
        // matches must not reset the running operation sequence.
        var pointerMap = new FastGridMap(8, 8);
        var pointer = new MapTool
        {
            TargetMap = pointerMap,
            SelectedAsset = "P",
            Mode = Config.Tool.Map.ModeTile,
            Seed = 4321u
        };
        pointer.OnPointerDown(null, null, 0, 0);
        Require(pointer.Seed == 4321u && pointer.OperationIndex == 1);
        var project = new EFYVProject(Config.Types.AssetTypeEnemyData) { DesignerSeed = 4321u };
        pointer.OnPointerDown(project, null, 0, 0);
        Require(pointer.Seed == 4321u && pointer.OperationIndex == 2);
    }

    private static void ToolsRefPencilSegment(
        bool[] expected,
        int width,
        int height,
        int x0,
        int y0,
        int x1,
        int y1,
        int brushSize,
        ToolsBrushShape shape)
    {
        int effective = Math.Min(brushSize, Math.Max(width, height));
        ReferenceLine(x0, y0, x1, y1, width, (pointX, pointY) =>
        {
            if (effective <= Config.Tool.Pencil.MinThickBrushSize)
            {
                if (pointX >= 0 && pointX < width && pointY >= 0 && pointY < height)
                    expected[(pointY * width) + pointX] = true;
                return;
            }

            int minimumOffset = -(effective >> 1);
            int maximumOffset = minimumOffset + effective - 1;
            bool pixelCentered = (effective & 1) != 0;
            for (int offsetY = minimumOffset; offsetY <= maximumOffset; offsetY++)
            {
                for (int offsetX = minimumOffset; offsetX <= maximumOffset; offsetX++)
                {
                    if (shape == ToolsBrushShape.Circle)
                    {
                        long radius;
                        long circleX;
                        long circleY;
                        if (pixelCentered)
                        {
                            radius = effective >> 1;
                            circleX = offsetX;
                            circleY = offsetY;
                        }
                        else
                        {
                            radius = effective;
                            circleX = (2L * offsetX) + 1;
                            circleY = (2L * offsetY) + 1;
                        }
                        if ((circleX * circleX) + (circleY * circleY) > radius * radius) continue;
                    }
                    int pixelX = pointX + offsetX;
                    int pixelY = pointY + offsetY;
                    if (pixelX >= 0 && pixelX < width && pixelY >= 0 && pixelY < height)
                        expected[(pixelY * width) + pixelX] = true;
                }
            }
        });
    }

    private static void ToolsRefFloodFill(
        uint[] grid,
        int width,
        int height,
        int startX,
        int startY,
        uint replacement)
    {
        uint target = grid[(startY * width) + startX];
        if (target == replacement) return;
        var pending = new Stack<int>();
        pending.Push((startY * width) + startX);
        while (pending.Count > 0)
        {
            int index = pending.Pop();
            if (grid[index] != target) continue;
            grid[index] = replacement;
            int x = index % width;
            int y = index / width;
            if (x > 0) pending.Push(index - 1);
            if (x < width - 1) pending.Push(index + 1);
            if (y > 0) pending.Push(index - width);
            if (y < height - 1) pending.Push(index + width);
        }
    }

    private static void ToolsRequireHitbox(
        Frame frame,
        string key,
        int startX,
        int startY,
        int currentX,
        int currentY)
    {
        float pixelsPerUnit = Config.Hitbox.PixelsPerUnit;
        int clampedStartX = Math.Max(Config.Canvas.MinCoordinate, Math.Min(startX, frame.Width));
        int clampedStartY = Math.Max(Config.Canvas.MinCoordinate, Math.Min(startY, frame.Height));
        int clampedCurrentX = Math.Max(Config.Canvas.MinCoordinate, Math.Min(currentX, frame.Width));
        int clampedCurrentY = Math.Max(Config.Canvas.MinCoordinate, Math.Min(currentY, frame.Height));
        float minX = Math.Min(clampedStartX, clampedCurrentX) / pixelsPerUnit;
        float minY = Math.Min(clampedStartY, clampedCurrentY) / pixelsPerUnit;
        float maxX = Math.Max(clampedStartX, clampedCurrentX) / pixelsPerUnit;
        float maxY = Math.Max(clampedStartY, clampedCurrentY) / pixelsPerUnit;
        HitboxData actual = frame.Hitboxes[key];
        Require(actual.X == minX && actual.Y == minY);
        Require(actual.Width == maxX - minX && actual.Height == maxY - minY);
    }

    private static short[] ToolsRefAutomataPass(
        short[] source,
        int width,
        int height,
        short targetTile,
        short baseTile)
    {
        var result = new short[source.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int neighborCount = 0;
                for (int neighborY = y - 1; neighborY <= y + 1; neighborY++)
                {
                    for (int neighborX = x - 1; neighborX <= x + 1; neighborX++)
                    {
                        if (neighborX == x && neighborY == y) continue;
                        if (neighborX < 0 || neighborX >= width ||
                            neighborY < 0 || neighborY >= height)
                        {
                            neighborCount++;
                            continue;
                        }
                        if (source[(neighborY * width) + neighborX] == targetTile) neighborCount++;
                    }
                }
                int index = (y * width) + x;
                if (neighborCount > BackendConfig.Procedural.DefaultSmoothThreshold)
                    result[index] = targetTile;
                else if (neighborCount < BackendConfig.Procedural.DefaultSmoothThreshold)
                    result[index] = baseTile;
                else
                    result[index] = source[index];
            }
        }
        return result;
    }

    private sealed class ToolsMapRngModel
    {
        private uint state;

        public uint Begin(uint seed, int operationIndex)
        {
            uint operationSeed = seed ^
                ((uint)(operationIndex + SharedBackendConfig.UnitStep) * BackendConfig.Math.FnvPrime);
            state = operationSeed == BackendConfig.Random.InvalidSeed
                ? BackendConfig.Random.FallbackSeed
                : operationSeed;
            return state;
        }

        public uint NextUInt()
        {
            state ^= state << BackendConfig.Random.XorShiftLeftA;
            state ^= state >> BackendConfig.Random.XorShiftRight;
            state ^= state << BackendConfig.Random.XorShiftLeftB;
            return state;
        }

        public float NextUnitFloat()
        {
            return NextUInt() * BackendConfig.Random.UIntToUnitFloat;
        }

        public float NextRange(float minimum, float maximum)
        {
            if (maximum <= minimum) return minimum;
            return minimum + ((maximum - minimum) * NextUnitFloat());
        }

        public void Offset2D(float radius, out float x, out float y)
        {
            float squareMagnitude;
            do
            {
                x = NextRange(-1f, 1f);
                y = NextRange(-1f, 1f);
                squareMagnitude = (x * x) + (y * y);
            }
            while (squareMagnitude > 1f || squareMagnitude == 0f);
            x *= radius;
            y *= radius;
        }
    }
}
