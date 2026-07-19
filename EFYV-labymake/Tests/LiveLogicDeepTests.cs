using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;
using LiveLogicFastMath = EFYVBackend.Core.Math.FastMath;

internal static partial class Program
{
    // ------------------------------------------------------------------
    // PreviewController: cumulative reference model. The controller keeps a
    // per-tick residual accumulator; because it accumulates in exact decimal
    // arithmetic, the frame index must always equal the floor of the TOTAL
    // elapsed frame time, applied modulo (looping) or clamped (one-shot).
    // ------------------------------------------------------------------
    private static void TestLiveLogicPreviewCumulativeReferenceModel()
    {
        int[][] configurations =
        {
            new[] { 1, 1 },
            new[] { 2, 240 },
            new[] { 3, 7 },
            new[] { 5, 12 },
            new[] { 7, 24 }
        };

        var random = new Random(0x11CE55);
        foreach (int[] configuration in configurations)
        {
            int frameCount = configuration[0];
            int fps = configuration[1];
            ProjectSnapshot snapshot = LiveLogicCaptureMarkerProject(frameCount, fps);

            var preview = new PreviewController { IsLooping = true };
            int events = 0;
            preview.StateChanged += ignored => events++;
            preview.Load(snapshot, 0);
            preview.Play();
            int expectedEvents = 2;
            Require(preview.Current.State == PreviewPlaybackState.Playing);
            Require(preview.Current.AnimationIndex == 0 && preview.Current.FrameCount == frameCount);
            Require(preview.Current.FPS == fps && preview.Current.IsLooping);

            decimal total = decimal.Zero;
            long floorBefore = 0;
            for (int iteration = 0; iteration < 250; iteration++)
            {
                long ticks = LiveLogicNextTickDuration(random);
                bool advanced = preview.Tick(TimeSpan.FromTicks(ticks));
                total += ((decimal)ticks * fps) / TimeSpan.TicksPerSecond;
                long floorAfter = (long)decimal.Floor(total);
                Require(advanced == (floorAfter > floorBefore));
                if (advanced) expectedEvents++;
                Require(preview.Current.FrameIndex == (int)(floorAfter % frameCount));
                Require(preview.Current.State == PreviewPlaybackState.Playing);
                floorBefore = floorAfter;
            }
            Require(events == expectedEvents);
            var pixels = new PixelColor[12];
            preview.CopyCurrentPixelsTo(pixels);
            Require(pixels[0].R == (byte)((floorBefore % frameCount) + 1));

            // Non-looping: the playhead pauses on the last frame exactly when the
            // cumulative frame floor reaches the frame count, never before.
            preview = new PreviewController { IsLooping = false };
            preview.Load(snapshot, 0);
            preview.Play();
            total = decimal.Zero;
            floorBefore = 0;
            bool paused = false;
            int maxTick = (int)((2 * TimeSpan.TicksPerSecond) / fps);
            for (int iteration = 0; iteration < (12 * frameCount) + 6; iteration++)
            {
                long ticks = paused || iteration >= 12 * frameCount
                    ? TimeSpan.TicksPerSecond * 4
                    : random.Next(0, maxTick);
                bool advanced = preview.Tick(TimeSpan.FromTicks(ticks));
                if (paused)
                {
                    Require(!advanced);
                    Require(preview.Current.FrameIndex == frameCount - 1);
                    Require(preview.Current.State == PreviewPlaybackState.Paused);
                    continue;
                }

                total += ((decimal)ticks * fps) / TimeSpan.TicksPerSecond;
                long floorAfter = (long)decimal.Floor(total);
                if (floorAfter >= frameCount)
                {
                    paused = true;
                    Require(advanced);
                    Require(preview.Current.FrameIndex == frameCount - 1);
                    Require(preview.Current.State == PreviewPlaybackState.Paused);
                }
                else
                {
                    Require(advanced == (floorAfter > floorBefore));
                    Require(preview.Current.FrameIndex == (int)floorAfter);
                    Require(preview.Current.State == PreviewPlaybackState.Playing);
                }
                floorBefore = floorAfter;
            }
            Require(paused);

            // Resuming after the terminal pause immediately re-pauses on the last frame.
            preview.Play();
            Require(preview.Current.State == PreviewPlaybackState.Playing);
            Require(preview.Tick(TimeSpan.FromSeconds(10)));
            Require(preview.Current.FrameIndex == frameCount - 1);
            Require(preview.Current.State == PreviewPlaybackState.Paused);
        }
    }

    private static void TestLiveLogicPreviewResidualAndEmptyStateMachine()
    {
        ProjectSnapshot snapshot = LiveLogicCaptureMarkerProject(5, 10);
        var preview = new PreviewController { IsLooping = true };
        preview.Load(snapshot, 0);
        preview.Play();

        // Sub-frame progress survives Pause/Play untouched...
        Require(!preview.Tick(TimeSpan.FromMilliseconds(90)));  // 0.9 frames pending
        preview.Pause();
        Require(!preview.Tick(TimeSpan.FromHours(1)));          // paused: time does not flow
        Require(preview.Current.FrameIndex == 0);
        preview.Play();
        Require(preview.Tick(TimeSpan.FromMilliseconds(20)));   // 0.9 + 0.2 crosses one frame
        Require(preview.Current.FrameIndex == 1);

        // ...but SeekFrame discards it.
        Require(!preview.Tick(TimeSpan.FromMilliseconds(80)));  // 0.1 + 0.8 = 0.9 pending
        preview.SeekFrame(3);
        Require(preview.Current.FrameIndex == 3);
        Require(!preview.Tick(TimeSpan.FromMilliseconds(90)));  // reset: only 0.9 pending again
        Require(preview.Tick(TimeSpan.FromMilliseconds(10)));
        Require(preview.Current.FrameIndex == 4);
        var pixels = new PixelColor[12];
        preview.CopyCurrentPixelsTo(pixels);
        Require(pixels[0].R == 5);

        // Stop rewinds and clears the accumulator.
        preview.Stop();
        Require(preview.Current.FrameIndex == 0);
        Require(preview.Current.State == PreviewPlaybackState.Stopped);
        preview.Play();
        Require(!preview.Tick(TimeSpan.FromMilliseconds(90)));

        // An exact whole-cycle advance reports progress without moving the frame.
        preview.Stop();
        preview.Play();
        Require(preview.Tick(TimeSpan.FromMilliseconds(500))); // 5 frames = one full loop
        Require(preview.Current.FrameIndex == 0);

        // Zero-frame animation state machine.
        var project = new EFYVProject(Config.Types.AssetTypeEnemyData);
        project.CanvasWidth = 4;
        project.CanvasHeight = 3;
        var emptyAnimation = new AnimationState("LiveLogicEmpty", 6);
        var fullAnimation = new AnimationState("LiveLogicFull", 8);
        fullAnimation.Frames.Add(new Frame(4, 3, 0));
        fullAnimation.Frames.Add(new Frame(4, 3, 1));
        project.Animations.Add(emptyAnimation);
        project.Animations.Add(fullAnimation);
        ProjectSnapshot mixed = ProjectSnapshot.Capture(project);

        var emptyPreview = new PreviewController();
        int emptyEvents = 0;
        emptyPreview.StateChanged += ignored => emptyEvents++;
        emptyPreview.Load(mixed, 0);
        Require(emptyEvents == 1);
        Require(emptyPreview.Current.State == PreviewPlaybackState.Empty);
        Require(emptyPreview.Current.FrameCount == 0 && emptyPreview.Current.FPS == 6);
        emptyPreview.Play();
        Require(emptyEvents == 1 && emptyPreview.Current.State == PreviewPlaybackState.Empty);
        emptyPreview.Pause();
        Require(emptyEvents == 1);
        Require(!emptyPreview.Tick(TimeSpan.FromHours(1)));
        RequireThrows<ArgumentOutOfRangeException>(() => emptyPreview.SeekFrame(0));
        RequireThrows<InvalidOperationException>(() =>
            emptyPreview.CopyCurrentPixelsTo(new PixelColor[12]));

        // FIXED (#30): Stop() on a zero-frame animation keeps the Empty state (and
        // publishes no event), so the Empty guard in CopyCurrentPixelsTo keeps
        // throwing its intended InvalidOperationException instead of an
        // out-of-range frame lookup.
        emptyPreview.Stop();
        Require(emptyPreview.Current.State == PreviewPlaybackState.Empty);
        Require(emptyEvents == 1);
        RequireThrows<InvalidOperationException>(() =>
            emptyPreview.CopyCurrentPixelsTo(new PixelColor[12]));
        emptyPreview.Play();
        Require(emptyPreview.Current.State == PreviewPlaybackState.Empty);

        // Loading the populated sibling animation recovers the machine.
        emptyPreview.Load(mixed, 1);
        Require(emptyPreview.Current.State == PreviewPlaybackState.Stopped);
        Require(emptyPreview.Current.AnimationIndex == 1 && emptyPreview.Current.FrameCount == 2);
        Require(emptyPreview.Current.FPS == 8);
        emptyPreview.Play();
        Require(emptyPreview.Current.State == PreviewPlaybackState.Playing);
    }

    // ------------------------------------------------------------------
    // ViewportController: full per-pixel nearest-neighbor reference model of
    // RenderToScreenBuffer (safe indexed reimplementation of the unsafe blit),
    // across zoom levels, pans, screen shapes and internal buffer reuse.
    // ------------------------------------------------------------------
    private static void TestLiveLogicViewportRenderReferenceModel()
    {
        var random = new Random(0x51DE0);
        Frame frameA = LiveLogicRandomFrame(random, 13, 9, true);
        Frame frameB = LiveLogicRandomFrame(random, 7, 5, false);

        int[][] cases =
        {
            new[] { 0, 0, 0, 26, 18 },
            new[] { 3, 5, 4, 26, 18 },
            new[] { -1, -7, -3, 26, 18 },
            new[] { -5, 3, 2, 26, 18 },
            new[] { 100, -2, 300, 26, 18 },
            new[] { 0, 100, -50, 16, 16 }
        };
        foreach (int[] parameters in cases)
        {
            var viewport = new ViewportController();
            int scrolls = parameters[0];
            for (int i = 0; i < Math.Abs(scrolls); i++) viewport.OnScroll(scrolls > 0 ? 1f : -1f);
            viewport.Pan(parameters[1], parameters[2]);
            LiveLogicRequireRenderMatchesReference(viewport, frameA, parameters[3], parameters[4]);
        }

        // One controller rendering differently sized frames must resize its
        // internal flatten buffer in both directions without stale pixels.
        var reused = new ViewportController();
        reused.OnScroll(1f);
        reused.OnScroll(1f);
        reused.Pan(1, 1);
        LiveLogicRequireRenderMatchesReference(reused, frameA, 20, 14);
        LiveLogicRequireRenderMatchesReference(reused, frameB, 20, 14);
        LiveLogicRequireRenderMatchesReference(reused, frameA, 20, 14);

        var guard = new ViewportController();
        RequireThrows<ArgumentOutOfRangeException>(() =>
            guard.RenderToScreenBuffer(frameA, new uint[4], 2, 0));
        RequireThrows<OverflowException>(() =>
            guard.RenderToScreenBuffer(frameA, new uint[4], int.MaxValue, 2));
    }

    private static void TestLiveLogicViewportTransformContracts()
    {
        var viewport = new ViewportController();
        Require(viewport.ZoomLevel == Config.Viewport.DefaultZoomLevel);
        Require(viewport.OffsetX == Config.Viewport.DefaultOffsetX);
        Require(viewport.OffsetY == Config.Viewport.DefaultOffsetY);
        viewport.OnScroll(0f);
        Require(viewport.ZoomLevel == Config.Viewport.DefaultZoomLevel);
        viewport.OnScroll(-0f); // negative zero is neutral, not a zoom-out
        Require(viewport.ZoomLevel == Config.Viewport.DefaultZoomLevel);
        viewport.OnScroll(float.PositiveInfinity);
        Require(viewport.ZoomLevel == Config.Viewport.DefaultZoomLevel + Config.Viewport.ZoomStep);
        float zoomAfterUp = viewport.ZoomLevel;
        viewport.OnScroll(float.NegativeInfinity);
        Require(viewport.ZoomLevel == zoomAfterUp - Config.Viewport.ZoomStep);

        // Exactly-clamped minimum zoom gives bit-exact screen->canvas doubling.
        var mini = new ViewportController();
        for (int i = 0; i < 3; i++) mini.OnScroll(-1f);
        Require(mini.ZoomLevel == Config.Viewport.MinZoom);
        int canvasX;
        int canvasY;
        mini.ScreenToCanvas(-1, 0, out canvasX, out canvasY);
        Require(canvasX == -2 && canvasY == 0);
        mini.ScreenToCanvas(1, 3, out canvasX, out canvasY);
        Require(canvasX == 2 && canvasY == 6);
        mini.Pan(3, 0);
        mini.ScreenToCanvas(2, 0, out canvasX, out canvasY);
        Require(canvasX == -2 && canvasY == 0);

        // At maximum zoom the int cast truncates toward zero, so screen points on
        // BOTH sides of the origin collapse onto canvas coordinate 0.
        var maxi = new ViewportController();
        for (int i = 0; i < 200; i++) maxi.OnScroll(1f);
        Require(maxi.ZoomLevel == Config.Viewport.MaxZoom);
        maxi.ScreenToCanvas(19, -19, out canvasX, out canvasY);
        Require(canvasX == 0 && canvasY == 0);
        maxi.ScreenToCanvas(39, -39, out canvasX, out canvasY);
        Require(canvasX == 1 && canvasY == -1);

        // Anchored zoom recomputes offsets from the pre-zoom canvas anchor.
        var anchored = new ViewportController();
        anchored.Pan(5, -7);
        int beforeX;
        int beforeY;
        anchored.ScreenToCanvas(40, 30, out beforeX, out beforeY);
        Require(beforeX == 35 && beforeY == 37);
        anchored.OnScroll(1f, 40, 30);
        float anchoredZoom = anchored.ZoomLevel;
        Require(anchoredZoom == Config.Viewport.DefaultZoomLevel + Config.Viewport.ZoomStep);
        Require(anchored.OffsetX == 40 - (int)(35 * anchoredZoom));
        Require(anchored.OffsetY == 30 - (int)(37 * anchoredZoom));
        int afterX;
        int afterY;
        anchored.ScreenToCanvas(40, 30, out afterX, out afterY);
        Require(Math.Abs(afterX - beforeX) <= 1 && Math.Abs(afterY - beforeY) <= 1);

        // Anchored zoom-out at the clamp is a bit-exact no-op round trip.
        var clamped = new ViewportController();
        for (int i = 0; i < 3; i++) clamped.OnScroll(-1f);
        clamped.Pan(2, 2);
        int clampedX;
        int clampedY;
        clamped.ScreenToCanvas(10, 10, out clampedX, out clampedY);
        Require(clampedX == 16 && clampedY == 16);
        clamped.OnScroll(-1f, 10, 10);
        Require(clamped.ZoomLevel == Config.Viewport.MinZoom);
        Require(clamped.OffsetX == 2 && clamped.OffsetY == 2);
        clamped.ScreenToCanvas(10, 10, out clampedX, out clampedY);
        Require(clampedX == 16 && clampedY == 16);

        // NaN through the anchored overload keeps zoom (and here, exact offsets).
        clamped.OnScroll(float.NaN, 6, 6);
        Require(clamped.ZoomLevel == Config.Viewport.MinZoom);
        Require(clamped.OffsetX == 2 && clamped.OffsetY == 2);

        var panner = new ViewportController();
        panner.Pan(3, 4);
        panner.Pan(-10, 2);
        Require(panner.OffsetX == -7 && panner.OffsetY == 6);
    }

    // ------------------------------------------------------------------
    // ToolbarAPI: category enumeration reference model, instance freshness,
    // registration growth, and the untested normalization corners.
    // ------------------------------------------------------------------
    private static void TestLiveLogicToolbarCategoryOrderAndNormalization()
    {
        var schema = new AssetSchemaService();
        var toolbar = new ToolbarAPI(schema);

        var expectedCategories = new List<string[]>();
        foreach (SchemaDefinition type in schema.GetAvailableTypes())
        {
            if (type.IsDirectional)
            {
                expectedCategories.Add(new[]
                {
                    type.DisplayName + Config.Entity.SuffixUp, type.AssetType,
                    type.DisplayName, Config.Entity.FacingUp
                });
                expectedCategories.Add(new[]
                {
                    type.DisplayName + Config.Entity.SuffixDown, type.AssetType,
                    type.DisplayName, Config.Entity.FacingDown
                });
                expectedCategories.Add(new[]
                {
                    type.DisplayName + Config.Entity.SuffixLeft, type.AssetType,
                    type.DisplayName, Config.Entity.FacingLeft
                });
                expectedCategories.Add(new[]
                {
                    type.DisplayName + Config.Entity.SuffixRight, type.AssetType,
                    type.DisplayName, Config.Entity.FacingRight
                });
            }
            else
            {
                expectedCategories.Add(new[]
                {
                    type.DisplayName, type.AssetType, type.DisplayName, Config.Entity.FacingNone
                });
            }
        }

        List<DesignableCategory> actual = toolbar.GetDesignableCategoryDefinitions();
        Require(actual.Count == expectedCategories.Count);
        for (int index = 0; index < actual.Count; index++)
        {
            Require(actual[index].Label == expectedCategories[index][0]);
            Require(actual[index].AssetType == expectedCategories[index][1]);
            Require(actual[index].DisplayName == expectedCategories[index][2]);
            Require(actual[index].Facing == expectedCategories[index][3]);
        }

        List<string> labels = toolbar.GetDesignableCategories();
        Require(labels.Count == actual.Count);
        for (int index = 0; index < labels.Count; index++)
            Require(labels[index] == actual[index].Label);

        // Each call returns a fresh list; callers cannot poison the toolbar.
        Require(!ReferenceEquals(toolbar.GetDesignableCategoryDefinitions(), actual));
        actual.Clear();
        Require(toolbar.GetDesignableCategoryDefinitions().Count == expectedCategories.Count);

        // Registering a derived type immediately grows the category surface.
        Require(schema.RegisterAssetType(new AssetSchemaRegistration(
            "LiveLogicCustomEnemyData",
            "LiveLogic Custom Enemy",
            Config.Types.AssetTypeEnemyData)));
        Require(toolbar.GetDesignableCategoryDefinitions().Count ==
            expectedCategories.Count + Config.Entity.DirectionalVariantCount);
        EFYVProject custom = toolbar.CreateNewProject(
            "LiveLogic Custom Enemy" + Config.Entity.SuffixLeft);
        Require(custom != null && custom.TargetAssetType == "LiveLogicCustomEnemyData");
        Require(Equals(custom.AssetProperties[Config.Entity.KeyFacing], Config.Entity.FacingLeft));

        // Category lookup is ordinal case-sensitive.
        string enemyLabel = SharedConfig.EnemyDisplayName + Config.Entity.SuffixDown;
        Require(enemyLabel.ToUpperInvariant() != enemyLabel);
        Require(toolbar.CreateNewProject(enemyLabel.ToUpperInvariant()) == null);

        // CreateDefaultValue contract per value kind.
        Require(Equals(ToolbarAPI.CreateDefaultValue(SchemaValueKind.Float), Config.Types.DefaultFloat));
        Require(Equals(ToolbarAPI.CreateDefaultValue(SchemaValueKind.Integer), Config.Types.DefaultInt));
        Require(Equals(ToolbarAPI.CreateDefaultValue(SchemaValueKind.Text), Config.Types.DefaultString));
        Require(ToolbarAPI.CreateDefaultValue(SchemaValueKind.Unknown) == null);
        Require(ToolbarAPI.CreateDefaultValue((SchemaValueKind)99) == null);

        // Normalization corners not covered elsewhere.
        RequireNormalization(SchemaValueKind.Float, ulong.MaxValue, true, (float)ulong.MaxValue);
        RequireNormalization(SchemaValueKind.Float, long.MinValue, true, (float)long.MinValue);
        RequireNormalization(SchemaValueKind.Float, double.NaN, false, null);
        RequireNormalization(SchemaValueKind.Float, double.NegativeInfinity, false, null);
        // Current behavior: enums slip through the float guard via IConvertible...
        RequireNormalization(SchemaValueKind.Float, SchemaValueKind.Text, true, 3f);
        // ...but are rejected by the integer whitelist.
        RequireNormalization(SchemaValueKind.Integer, SchemaValueKind.Text, false, null);
        RequireNormalization(SchemaValueKind.Integer, sbyte.MinValue, true, (int)sbyte.MinValue);
        RequireNormalization(SchemaValueKind.Integer, ushort.MaxValue, true, (int)ushort.MaxValue);
        RequireNormalization(SchemaValueKind.Integer, (long)int.MaxValue, true, int.MaxValue);
        RequireNormalization(SchemaValueKind.Integer, ((long)int.MaxValue) + 1L, false, null);
        RequireNormalization(SchemaValueKind.Integer, ((ulong)int.MaxValue) + 1UL, false, null);
        RequireNormalization(SchemaValueKind.Text, new string('x', 4096), true, new string('x', 4096));

        // GetEditableProperties passes stored values through without re-normalizing.
        EFYVProject enemy = toolbar.CreateNewProject(enemyLabel);
        enemy.AssetProperties[SharedConfig.BaseSpeedField] = "garbage";
        DesignerProperty garbled = FindDesignerProperty(
            toolbar.GetEditableProperties(enemy),
            SharedConfig.BaseSpeedField);
        Require(Equals(garbled.Value, "garbage"));
        enemy.AssetProperties[SharedConfig.BaseSpeedField] = 3f;

        // Failed edits leave the property bag completely untouched.
        var before = new Dictionary<string, object>(enemy.AssetProperties);
        Require(toolbar.TrySetProperty(enemy, SharedConfig.BaseSpeedField, -0.001f).Status ==
            PropertyEditStatus.OutOfRange);
        Require(toolbar.TrySetProperty(enemy, SharedConfig.BaseSpeedField, float.NaN).Status ==
            PropertyEditStatus.InvalidValue);
        Require(toolbar.TrySetProperty(enemy, Config.Entity.KeyFacing, "Diagonal").Status ==
            PropertyEditStatus.InvalidChoice);
        Require(toolbar.TrySetProperty(enemy, "liveLogicMissing", 1f).Status ==
            PropertyEditStatus.UnknownField);
        Require(enemy.AssetProperties.Count == before.Count);
        foreach (KeyValuePair<string, object> pair in before)
            Require(Equals(enemy.AssetProperties[pair.Key], pair.Value));

        // The toolbar does not path-police free text; ProjectValidator is the gate.
        Require(toolbar.TrySetProperty(enemy, SharedConfig.EntityNameField, "../escape").Succeeded);
        ProjectValidationResult validation = new ProjectValidator(schema).Validate(enemy);
        Require(ContainsIssue(validation, ProjectIssueCode.InvalidIdentityName));
    }

    // ------------------------------------------------------------------
    // AnimationGeneratorAPI walk generation: exhaustive per-pixel comparison
    // against a safe, bounds-checked managed reimplementation of the
    // deformation spec (guards the unsafe pointer arithmetic).
    // ------------------------------------------------------------------
    private static void TestLiveLogicWalkAnimationReferenceModel()
    {
        var random = new Random(0x3A11C);
        const int width = 17;
        const int height = 12;
        Frame baseFrame = LiveLogicRandomFrame(random, width, height, true);
        baseFrame.Hitboxes["LiveLogicBox"] = new HitboxData
        {
            X = 0.125f,
            Y = 0.25f,
            Width = 0.375f,
            Height = 0.5f
        };
        PixelColor[] flat = baseFrame.FlattenLayers();
        uint[] baseLayerBefore = CopyRgba(baseFrame.Layers[0]);
        uint[] overlayBefore = CopyRgba(baseFrame.Layers[1]);

        var generator = new AnimationGeneratorAPI();
        int[] splits = { 0, 5, height };
        float[][] amplitudePairs =
        {
            new[] { 0f, 0f },
            new[] { 2.5f, -3.25f },
            new[] { -1.5f, 6f }
        };
        foreach (int splitY in splits)
        foreach (float[] amplitudes in amplitudePairs)
        {
            AnimationState walk = generator.GenerateWalkAnimation(
                "LiveLogicWalk",
                baseFrame,
                5,
                splitY,
                amplitudes[0],
                amplitudes[1]);
            Require(walk.StateName == "LiveLogicWalk");
            Require(walk.FPS == Config.Animation.WalkDefaultFPS);
            Require(walk.Frames.Count == 5);
            for (int frameIndex = 0; frameIndex < 5; frameIndex++)
            {
                Frame frame = walk.Frames[frameIndex];
                Require(frame.FrameIndex == frameIndex);
                Require(frame.Width == width && frame.Height == height);
                Require(frame.Layers.Count == 1);
                Require(frame.Hitboxes.Count == baseFrame.Hitboxes.Count);
                Require(frame.Hitboxes["LiveLogicBox"].Width == 0.375f);

                uint[] expected = LiveLogicReferenceWalkFrame(
                    flat,
                    width,
                    height,
                    (float)frameIndex / 5,
                    splitY,
                    amplitudes[0],
                    amplitudes[1]);
                PixelColor[] actual = frame.Layers[0].Pixels;
                for (int pixel = 0; pixel < expected.Length; pixel++)
                    Require(actual[pixel].Rgba == expected[pixel]);

                // Non-vacuity guard: mid-cycle frames with non-zero amplitudes must
                // actually displace pixels, or this whole comparison proves nothing.
                if (frameIndex == 2 && amplitudes[0] != 0f && splitY == 5)
                    Require(!LiveLogicPixelsEqualFlat(actual, flat));
            }
        }

        // Frame zero is always the identity of the flattened base (sin(-pi) ~ 0),
        // even with large deformation amplitudes.
        AnimationState identity = generator.GenerateWalkAnimation(
            "LiveLogicIdentity", baseFrame, 3, 5, 9.5f, -9.5f);
        for (int pixel = 0; pixel < flat.Length; pixel++)
            Require(identity.Frames[0].Layers[0].Pixels[pixel].Rgba == flat[pixel].Rgba);

        // The source frame is never mutated by generation.
        RequireRgbaEqual(baseLayerBefore, baseFrame.Layers[0]);
        RequireRgbaEqual(overlayBefore, baseFrame.Layers[1]);

        RequireThrows<ArgumentOutOfRangeException>(() =>
            generator.GenerateWalkAnimation("x", baseFrame, 2, -1, 0f, 0f));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            generator.GenerateWalkAnimation("x", baseFrame, 2, 3, float.NaN, 0f));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            generator.GenerateWalkAnimation("x", baseFrame, 2, 3, 0f, float.PositiveInfinity));
    }

    // ------------------------------------------------------------------
    // AnimationGeneratorAPI jitter generation: with uniform per-octant gauges
    // the octant lookup cancels out, so a safe radial-displacement model must
    // reproduce every pixel; also input-array immutability and NaN contracts.
    // ------------------------------------------------------------------
    private static void TestLiveLogicJitterAnimationReferenceModel()
    {
        var random = new Random(0x7E4B2);
        var generator = new AnimationGeneratorAPI();
        int[][] sizes = { new[] { 15, 11 }, new[] { 8, 6 } };
        float[][] gauges =
        {
            new[] { 0f, 0f },
            new[] { 2.75f, 1f },
            new[] { -3.25f, 2.5f }
        };
        foreach (int[] size in sizes)
        {
            int width = size[0];
            int height = size[1];
            Frame baseFrame = LiveLogicRandomFrame(random, width, height, true);
            PixelColor[] flat = baseFrame.FlattenLayers();

            foreach (float[] gauge in gauges)
            {
                var amplitudes = new float[Config.Tool.Moving.JitterOctantCount];
                var frequencies = new float[Config.Tool.Moving.JitterOctantCount];
                for (int index = 0; index < amplitudes.Length; index++)
                {
                    amplitudes[index] = gauge[0];
                    frequencies[index] = gauge[1];
                }

                AnimationState jitter = generator.GenerateJitterAnimation(
                    "LiveLogicJitter", baseFrame, 6, amplitudes, frequencies);
                Require(jitter.FPS == Config.Animation.JitterDefaultFPS);
                Require(jitter.Frames.Count == 6);
                int centerX = width / 2;
                int centerY = height / 2;
                for (int frameIndex = 0; frameIndex < 6; frameIndex++)
                {
                    uint[] expected = LiveLogicReferenceJitterFrame(
                        flat,
                        width,
                        height,
                        (float)frameIndex / 6,
                        gauge[0],
                        gauge[1]);
                    PixelColor[] actual = jitter.Frames[frameIndex].Layers[0].Pixels;
                    for (int pixel = 0; pixel < expected.Length; pixel++)
                        Require(actual[pixel].Rgba == expected[pixel]);
                    // The exact center pixel is never displaced.
                    Require(actual[(centerY * width) + centerX].Rgba ==
                        flat[(centerY * width) + centerX].Rgba);
                    // Non-vacuity guard: a mid-cycle frame with real gauges displaces.
                    if (frameIndex == 2 && gauge[0] != 0f)
                        Require(!LiveLogicPixelsEqualFlat(actual, flat));
                }

                // Generation must not write back into the gauge arrays.
                for (int index = 0; index < amplitudes.Length; index++)
                {
                    Require(amplitudes[index] == gauge[0]);
                    Require(frequencies[index] == gauge[1]);
                }
            }

            var poisonedAmplitudes = new float[Config.Tool.Moving.JitterOctantCount];
            var poisonedFrequencies = new float[Config.Tool.Moving.JitterOctantCount];
            poisonedAmplitudes[3] = float.NaN;
            RequireThrows<ArgumentOutOfRangeException>(() => generator.GenerateJitterAnimation(
                "x", baseFrame, 2, poisonedAmplitudes, poisonedFrequencies));
            poisonedAmplitudes[3] = 0f;
            poisonedFrequencies[7] = float.PositiveInfinity;
            RequireThrows<ArgumentOutOfRangeException>(() => generator.GenerateJitterAnimation(
                "x", baseFrame, 2, poisonedAmplitudes, poisonedFrequencies));
        }
    }

    // ------------------------------------------------------------------
    // TaskDebounceScheduler: the production scheduler's synchronous-completion
    // and cancellation contracts (everything else in the suite uses manual
    // schedulers, so this class was previously untested).
    // ------------------------------------------------------------------
    private static async Task TestLiveLogicTaskDebounceSchedulerContract()
    {
        var scheduler = new TaskDebounceScheduler();
        Require(scheduler.UtcNow.Offset == TimeSpan.Zero);
        DateTimeOffset first = scheduler.UtcNow;
        DateTimeOffset second = scheduler.UtcNow;
        Require(second >= first);
        Require((DateTimeOffset.UtcNow - second).Duration() < TimeSpan.FromMinutes(5));

        Require(scheduler.Delay(TimeSpan.Zero, CancellationToken.None).IsCompletedSuccessfully);
        Require(scheduler.Delay(TimeSpan.FromTicks(-1), CancellationToken.None).IsCompletedSuccessfully);
        Require(scheduler.Delay(TimeSpan.FromDays(-2), CancellationToken.None).IsCompletedSuccessfully);
        // Task.Delay's "-1ms means infinite" sentinel is short-circuited to a
        // synchronous completion by the <= zero guard.
        Require(scheduler.Delay(TimeSpan.FromMilliseconds(-1), CancellationToken.None)
            .IsCompletedSuccessfully);

        using (var preCancelled = new CancellationTokenSource())
        {
            preCancelled.Cancel();
            // A zero delay wins over an already-cancelled token...
            Require(scheduler.Delay(TimeSpan.Zero, preCancelled.Token).IsCompletedSuccessfully);
            // ...while a positive delay surfaces the cancellation immediately.
            Task cancelled = scheduler.Delay(TimeSpan.FromMinutes(10), preCancelled.Token);
            Require(cancelled.IsCanceled);
            bool observed = false;
            try { await cancelled; }
            catch (OperationCanceledException) { observed = true; }
            Require(observed);
        }

        using (var cancellation = new CancellationTokenSource())
        {
            Task pending = scheduler.Delay(TimeSpan.FromMinutes(10), cancellation.Token);
            Require(!pending.IsCompleted);
            cancellation.Cancel();
            bool observed = false;
            try { await pending; }
            catch (OperationCanceledException) { observed = true; }
            Require(observed && pending.IsCanceled);
        }
    }

    // ------------------------------------------------------------------
    // LiveDebugController with a SynchronizationContext: captures AND events
    // must be marshalled through the context (previously untested path).
    // ------------------------------------------------------------------
    private static async Task TestLiveLogicLiveDebugEventContextPath()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            var validator = new ProjectValidator(schema);
            var engine = new ExportEngine(validator);
            var scheduler = new ManualScheduler();
            var context = new LiveLogicManualSyncContext();
            EFYVProject project = CreateValidProject(root, 1);
            int testThread = Environment.CurrentManagedThreadId;

            using (var live = new LiveDebugController(
                engine,
                validator,
                scheduler,
                TimeSpan.FromSeconds(1),
                context))
            {
                var states = new List<LiveDebugState>();
                var eventThreads = new List<int>();
                live.StateChanged += snapshot =>
                {
                    states.Add(snapshot.State);
                    eventThreads.Add(Environment.CurrentManagedThreadId);
                };

                live.StartWatching();
                Require(states.Count == 0 && context.Pending >= 1); // deferred until drained
                context.DrainAll();
                Require(states.Count == 1 && states[0] == LiveDebugState.Watching);

                int captures = 0;
                int captureThread = -1;
                long requestId = live.NotifyProjectChanged(() =>
                {
                    captures++;
                    captureThread = Environment.CurrentManagedThreadId;
                    return project;
                });
                Task flush = live.FlushAsync();
                AsyncReleaseWhenPending(scheduler, 1);
                Require(SpinWait.SpinUntil(() =>
                {
                    context.DrainAll();
                    return flush.IsCompleted;
                }, 10000));
                await flush;
                context.DrainAll();

                Require(captures == 1 && captureThread == testThread);
                Require(live.Current.State == LiveDebugState.Succeeded);
                Require(live.Current.RequestId == requestId);
                Require(live.Current.Export != null && File.Exists(live.Current.Export.MetadataPath));
                Require(states[states.Count - 1] == LiveDebugState.Succeeded);
                Require(states.Contains(LiveDebugState.Scheduled));
                Require(states.Contains(LiveDebugState.Exporting));
                foreach (int thread in eventThreads) Require(thread == testThread);

                // A capture failure raised on the context still lands in Failed.
                live.NotifyProjectChanged(() => throw new InvalidDataException("live-logic capture"));
                Task failedFlush = live.FlushAsync();
                AsyncReleaseWhenPending(scheduler, 1);
                Require(SpinWait.SpinUntil(() =>
                {
                    context.DrainAll();
                    return failedFlush.IsCompleted;
                }, 10000));
                await failedFlush;
                context.DrainAll();
                Require(live.Current.State == LiveDebugState.Failed);
                Require(live.Current.Exception is InvalidDataException);
                Require(states[states.Count - 1] == LiveDebugState.Failed);
                foreach (int thread in eventThreads) Require(thread == testThread);
            }
            Require(context.Pending == 0);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // LiveDebugController timestamps come from the injected scheduler clock;
    // engine-level failures (not just validation/capture) reach Failed;
    // CancelPending without work is silent; disposal cancels in-flight work;
    // and the default-scheduler constructor exports immediately.
    // ------------------------------------------------------------------
    private static async Task TestLiveLogicLiveDebugTimeAndFailurePaths()
    {
        string root = NewTemporaryDirectory();
        try
        {
            string assetsDirectory = Path.Combine(root, Config.Export.DirAssets);
            Directory.CreateDirectory(assetsDirectory);
            var schema = new AssetSchemaService();
            var validator = new ProjectValidator(schema);
            var engine = new ExportEngine(validator);
            var scheduler = new LiveLogicTimeScheduler(
                new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero));
            EFYVProject project = CreateValidProject(root, 1);

            var live = new LiveDebugController(engine, validator, scheduler, TimeSpan.FromSeconds(1), null);
            int events = 0;
            live.StateChanged += ignored => Interlocked.Increment(ref events);
            try
            {
                DateTimeOffset start = scheduler.UtcNow;
                Require(live.Current.State == LiveDebugState.Stopped);
                Require(live.Current.ChangedAt == start && !live.Current.LastSyncedAt.HasValue);

                live.StartWatching();
                Require(live.Current.ChangedAt == start);

                DateTimeOffset syncTime = start.AddMinutes(5);
                long firstRequest = live.NotifyProjectChanged(project);
                scheduler.AdvanceTo(syncTime);
                LiveLogicWaitForPending(scheduler, 1);
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Succeeded);
                Require(live.Current.RequestId == firstRequest);
                Require(live.Current.LastSyncedAt == syncTime && live.Current.ChangedAt == syncTime);
                Require(live.Current.Validation != null && live.Current.Validation.IsValid);

                // Validation failure keeps the last-synced marker.
                DateTimeOffset invalidTime = syncTime.AddSeconds(30);
                scheduler.AdvanceTo(invalidTime);
                EFYVProject invalid = CreateValidProject(root, 1);
                invalid.Animations.Clear();
                live.NotifyProjectChanged(invalid);
                LiveLogicWaitForPending(scheduler, 1);
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.ValidationFailed);
                Require(live.Current.LastSyncedAt == syncTime && live.Current.ChangedAt == invalidTime);

                // Engine failure AFTER validation passes: a file squats on the
                // RawArt directory path, so the export itself throws IOException.
                string rawArtObstruction = Path.Combine(assetsDirectory, Config.Export.DirRawArt);
                if (Directory.Exists(rawArtObstruction)) Directory.Delete(rawArtObstruction, true);
                File.WriteAllText(rawArtObstruction, "live-logic obstruction");
                live.NotifyProjectChanged(project);
                LiveLogicWaitForPending(scheduler, 1);
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Failed);
                Require(live.Current.Exception is IOException && live.Current.Export == null);
                Require(live.Current.LastSyncedAt == syncTime);
                File.Delete(rawArtObstruction);

                // CancelPending with no in-flight request is a silent no-op.
                int eventsBefore = Volatile.Read(ref events);
                long requestBefore = live.Current.RequestId;
                live.CancelPending();
                Require(live.Current.State == LiveDebugState.Failed);
                Require(live.Current.RequestId == requestBefore);
                Require(Volatile.Read(ref events) == eventsBefore);

                // ExportNowAsync supersedes a pending debounced request.
                long pendingRequest = live.NotifyProjectChanged(project);
                LiveLogicWaitForPending(scheduler, 1);
                await live.ExportNowAsync(project);
                Require(live.Current.State == LiveDebugState.Succeeded);
                Require(live.Current.RequestId == pendingRequest + 1);
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Succeeded);
                Require(live.Current.RequestId == pendingRequest + 1);
                Require(Volatile.Read(ref events) > eventsBefore);
            }
            finally
            {
                live.Dispose();
            }

            // Disposing with a pending request lands in Cancelled and the
            // orphaned task completes quietly.
            var disposable = new LiveDebugController(
                engine, validator, scheduler, TimeSpan.FromSeconds(1), null);
            disposable.StartWatching();
            disposable.NotifyProjectChanged(project);
            Task orphan = disposable.FlushAsync();
            LiveLogicWaitForPending(scheduler, 1);
            disposable.Dispose();
            Require(disposable.Current.State == LiveDebugState.Cancelled);
            Require(!disposable.Current.IsPending);
            await orphan;
            Require(disposable.Current.State == LiveDebugState.Cancelled);

            // Default-scheduler constructor: ExportNowAsync completes with no
            // debounce wait because the real scheduler short-circuits zero delays.
            using (var immediate = new LiveDebugController(engine, validator))
            {
                await immediate.ExportNowAsync(project);
                Require(immediate.Current.State == LiveDebugState.Succeeded);
                Require(immediate.Current.LastSyncedAt.HasValue);
                Require(immediate.Current.Export != null);
                Require(File.Exists(immediate.Current.Export.ImagePath));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Item #27: the live debug loop reads the accumulated dirty scope to pick a
    // metadata-only publish path when no exported pixels changed, and content
    // hashing suppresses byte-identical rewrites. A sentinel on the published
    // PNG proves whether the loop rewrote it.
    // ------------------------------------------------------------------
    private static async Task TestLiveLogicScopeMetadataOnlyFastPath()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            var validator = new ProjectValidator(schema);
            var engine = new ExportEngine(validator);
            var scheduler = new ManualScheduler();
            EFYVProject project = CreateValidProject(root, 1);
            project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = default;

            using (var live = new LiveDebugController(
                engine, validator, scheduler, TimeSpan.FromSeconds(1), null))
            {
                live.StartWatching();

                // First publish (default Everything scope) writes the PNG.
                live.NotifyProjectChanged(() => project, DesignerDirtyScope.Everything);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount >= 1, 2000));
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Succeeded);
                Require(live.Current.Export.ImageWritten);
                string png = live.Current.Export.ImagePath;
                File.WriteAllText(png, "LIVE-SENTINEL");

                // A hitbox-only edit takes the metadata-only path: PNG untouched.
                EFYVBackend.Core.Models.HitboxData nudged =
                    project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
                nudged.X = 0.25f;
                project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = nudged;
                live.NotifyProjectChanged(() => project, DesignerDirtyScope.Hitboxes);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount >= 1, 2000));
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Succeeded);
                Require(!live.Current.Export.ImageWritten);
                Require(live.Current.Export.MetadataWritten);
                Require(File.ReadAllText(png) == "LIVE-SENTINEL");

                // A pixel scope accumulated alongside a property scope (two edits
                // inside one debounce window) still forces a full publish - the
                // Pixels bit survives the union.
                project.Animations[0].Frames[0].Layers[0].Pixels[1].Rgba = Pack(1, 2, 3, 255);
                live.NotifyProjectChanged(() => project, DesignerDirtyScope.Properties);
                live.NotifyProjectChanged(() => project, DesignerDirtyScope.Pixels);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount >= 2, 2000));
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Succeeded);
                Require(live.Current.Export.ImageWritten);
                Require(File.ReadAllText(png) != "LIVE-SENTINEL");

                // With the PNG now current again, a property-only edit is
                // metadata-only and a byte-identical republish suppresses both.
                File.WriteAllText(png, "LIVE-SENTINEL2");
                live.NotifyProjectChanged(() => project, DesignerDirtyScope.Properties);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount >= 1, 2000));
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Succeeded);
                Require(!live.Current.Export.ImageWritten);
                Require(!live.Current.Export.MetadataWritten);
                Require(File.ReadAllText(png) == "LIVE-SENTINEL2");
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // LiveLogic-prefixed helpers (private to this file).
    // ------------------------------------------------------------------

    private static long LiveLogicNextTickDuration(Random random)
    {
        int pick = random.Next(10);
        if (pick == 0) return 0;
        if (pick == 1) return 1;
        if (pick == 2) return TimeSpan.TicksPerDay;
        return random.Next(1, 40000000);
    }

    private static ProjectSnapshot LiveLogicCaptureMarkerProject(int frameCount, int fps)
    {
        var project = new EFYVProject(Config.Types.AssetTypeEnemyData);
        project.CanvasWidth = 4;
        project.CanvasHeight = 3;
        var animation = new AnimationState("LiveLogicMarkers", fps);
        for (int index = 0; index < frameCount; index++)
        {
            var frame = new Frame(4, 3, index);
            frame.Layers[0].Pixels[0].Rgba = Pack((byte)(index + 1), 0, 0, 255);
            animation.Frames.Add(frame);
        }
        project.Animations.Add(animation);
        return ProjectSnapshot.Capture(project);
    }

    private static Frame LiveLogicRandomFrame(Random random, int width, int height, bool layered)
    {
        var frame = new Frame(width, height);
        Layer baseLayer = frame.Layers[0];
        for (int index = 0; index < baseLayer.Pixels.Length; index++)
            baseLayer.Pixels[index].Rgba = random.Next(4) == 0 ? 0u : LiveLogicNextRgba(random);
        if (layered)
        {
            var overlay = new Layer("LiveLogicOverlay", width, height) { Opacity = 0.4f };
            for (int index = 0; index < overlay.Pixels.Length; index++)
                overlay.Pixels[index].Rgba = LiveLogicNextRgba(random);
            frame.Layers.Add(overlay);
            var hidden = new Layer("LiveLogicHidden", width, height) { IsVisible = false };
            for (int index = 0; index < hidden.Pixels.Length; index++)
                hidden.Pixels[index].Rgba = LiveLogicNextRgba(random);
            frame.Layers.Add(hidden);
        }
        return frame;
    }

    private static bool LiveLogicPixelsEqualFlat(PixelColor[] actual, PixelColor[] flat)
    {
        for (int index = 0; index < actual.Length; index++)
        {
            if (actual[index].Rgba != flat[index].Rgba) return false;
        }
        return true;
    }

    private static uint LiveLogicNextRgba(Random random)
    {
        return Pack(
            (byte)random.Next(256),
            (byte)random.Next(256),
            (byte)random.Next(256),
            (byte)random.Next(256));
    }

    private static void LiveLogicRequireRenderMatchesReference(
        ViewportController viewport,
        Frame frame,
        int screenWidth,
        int screenHeight)
    {
        var screen = new uint[screenWidth * screenHeight];
        for (int index = 0; index < screen.Length; index++) screen[index] = 0xCAFEBABEu;
        viewport.RenderToScreenBuffer(frame, screen, screenWidth, screenHeight);
        uint[] expected = LiveLogicReferenceViewportRender(
            frame,
            screenWidth,
            screenHeight,
            viewport.ZoomLevel,
            viewport.OffsetX,
            viewport.OffsetY);
        for (int index = 0; index < screen.Length; index++) Require(screen[index] == expected[index]);
    }

    private static uint[] LiveLogicReferenceViewportRender(
        Frame frame,
        int screenWidth,
        int screenHeight,
        float zoom,
        int offsetX,
        int offsetY)
    {
        PixelColor[] flat = frame.FlattenLayers();
        float invScale = 1f / zoom;
        var expected = new uint[screenWidth * screenHeight];
        for (int destY = 0; destY < screenHeight; destY++)
        {
            float sourceY = (destY - offsetY) * invScale;
            int srcY = (int)sourceY;
            bool validY = sourceY >= 0f && srcY >= 0 && srcY < frame.Height;
            for (int destX = 0; destX < screenWidth; destX++)
            {
                float sourceX = (destX - offsetX) * invScale;
                int srcX = (int)sourceX;
                expected[(destY * screenWidth) + destX] =
                    validY && sourceX >= 0f && srcX >= 0 && srcX < frame.Width
                        ? flat[(srcY * frame.Width) + srcX].Rgba
                        : SharedConfig.TransparentRgba;
            }
        }
        return expected;
    }

    private static uint[] LiveLogicReferenceWalkFrame(
        PixelColor[] flat,
        int width,
        int height,
        float timeT,
        int splitY,
        float bounceAmp,
        float strideAmp)
    {
        float bounceT = timeT * BackendConfig.Deformation.BounceFrequencyMultiplier;
        if (bounceT >= BackendConfig.Deformation.NormalizedCycle)
            bounceT -= BackendConfig.Deformation.NormalizedCycle;
        float bounceRad = (bounceT * BackendConfig.Math.TwoPI) - BackendConfig.Math.PI;
        float strideRad = (timeT * BackendConfig.Math.TwoPI) - BackendConfig.Math.PI;
        float bounceOffset = LiveLogicFastMath.FastSinTaylor(bounceRad) * bounceAmp;
        float strideOffsetBase = LiveLogicFastMath.FastSinTaylor(strideRad) * strideAmp;

        var expected = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            int srcYBody = y - (int)bounceOffset;
            bool isLegs = y >= splitY;
            int strideOffset = 0;
            if (isLegs)
            {
                float depth = (y - splitY) / (float)(height - splitY);
                strideOffset = (int)(strideOffsetBase * depth);
            }
            for (int x = 0; x < width; x++)
            {
                int srcX = x;
                int srcY = y;
                if (isLegs)
                {
                    int halfWidth = width / 2;
                    srcX = x < halfWidth ? x - strideOffset : x + strideOffset;
                }
                else
                {
                    srcY = srcYBody;
                }
                expected[(y * width) + x] = srcX >= 0 && srcX < width && srcY >= 0 && srcY < height
                    ? flat[(srcY * width) + srcX].Rgba
                    : SharedConfig.TransparentRgba;
            }
        }
        return expected;
    }

    private static uint[] LiveLogicReferenceJitterFrame(
        PixelColor[] flat,
        int width,
        int height,
        float timeT,
        float amplitude,
        float frequency)
    {
        float phase = timeT * frequency;
        float offset = LiveLogicFastMath.FastSinTaylor(
            (phase * BackendConfig.Math.TwoPI) - BackendConfig.Math.PI) * amplitude;
        int centerX = width / 2;
        int centerY = height / 2;
        var expected = new uint[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int deltaX = x - centerX;
            int deltaY = y - centerY;
            int srcX = x;
            int srcY = y;
            if (deltaX != 0 || deltaY != 0)
            {
                float directionX = deltaX;
                float directionY = deltaY;
                LiveLogicFastMath.FastNormalize(ref directionX, ref directionY);
                srcX = x + (int)(directionX * offset);
                srcY = y + (int)(directionY * offset);
            }
            expected[(y * width) + x] = srcX >= 0 && srcX < width && srcY >= 0 && srcY < height
                ? flat[(srcY * width) + srcX].Rgba
                : SharedConfig.TransparentRgba;
        }
        return expected;
    }

    private static void LiveLogicWaitForPending(LiveLogicTimeScheduler scheduler, int minimum)
    {
        Require(SpinWait.SpinUntil(() => scheduler.PendingCount >= minimum, 5000));
    }

    private sealed class LiveLogicManualSyncContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<KeyValuePair<SendOrPostCallback, object>> queue =
            new ConcurrentQueue<KeyValuePair<SendOrPostCallback, object>>();

        public int Pending => queue.Count;

        public override void Post(SendOrPostCallback callback, object state)
        {
            queue.Enqueue(new KeyValuePair<SendOrPostCallback, object>(callback, state));
        }

        public void DrainAll()
        {
            KeyValuePair<SendOrPostCallback, object> item;
            while (queue.TryDequeue(out item)) item.Key(item.Value);
        }
    }

    private sealed class LiveLogicTimeScheduler : IDebounceScheduler
    {
        private readonly object gate = new object();
        private readonly List<TaskCompletionSource<bool>> pending =
            new List<TaskCompletionSource<bool>>();
        private DateTimeOffset now;

        public LiveLogicTimeScheduler(DateTimeOffset start)
        {
            now = start;
        }

        public DateTimeOffset UtcNow
        {
            get { lock (gate) return now; }
        }

        public int PendingCount
        {
            get { lock (gate) return pending.Count; }
        }

        public void AdvanceTo(DateTimeOffset value)
        {
            lock (gate) now = value;
        }

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay <= TimeSpan.Zero) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            lock (gate) pending.Add(completion);
            return completion.Task;
        }

        public void ReleaseAll()
        {
            TaskCompletionSource<bool>[] released;
            lock (gate)
            {
                released = pending.ToArray();
                pending.Clear();
            }
            foreach (var completion in released) completion.TrySetResult(true);
        }
    }
}
