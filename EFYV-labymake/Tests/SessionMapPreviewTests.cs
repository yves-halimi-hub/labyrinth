using System;
using System.IO;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Tools;
using EFYVBackend.Core.Collections;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    private static void TestDesignerSessionCrudAndGestureRollback()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            var session = DesignerSession.Create("Crud", project, root);
            session.AutosaveEnabled = false;
            int stateEvents = 0;
            session.StateChanged += snapshot =>
            {
                stateEvents++;
                Require(snapshot.Validation != null);
            };
            try
            {
                Require(!session.SelectFrame(-1, 0));
                Require(!session.SelectFrame(0, 99));
                Require(session.SelectFrame(0, 0));

                var addedAnimation = new AnimationState("Run", 9);
                addedAnimation.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight));
                session.AddAnimation(addedAnimation);
                Require(project.Animations.Count == 2 && ReferenceEquals(project.Animations[1], addedAnimation));
                Require(session.Undo() && project.Animations.Count == 1);
                Require(session.Redo() && project.Animations.Count == 2);

                AnimationState duplicateAnimation = session.DuplicateAnimation(0);
                Require(project.Animations.Count == 3 && !ReferenceEquals(project.Animations[0], duplicateAnimation));
                Require(!ReferenceEquals(project.Animations[0].Frames[0], duplicateAnimation.Frames[0]));
                Require(session.Undo() && project.Animations.Count == 2);
                Require(session.Redo() && project.Animations.Count == 3);

                string oldName = project.Animations[0].StateName;
                session.RenameAnimation(0, "Renamed");
                Require(project.Animations[0].StateName == "Renamed");
                Require(session.Undo() && project.Animations[0].StateName == oldName);
                Require(session.Redo() && project.Animations[0].StateName == "Renamed");
                RequireThrows<ArgumentException>(() => session.RenameAnimation(0, " "));

                int oldFps = project.Animations[0].FPS;
                session.SetAnimationFps(0, 23);
                Require(project.Animations[0].FPS == 23);
                Require(session.Undo() && project.Animations[0].FPS == oldFps);
                Require(session.Redo() && project.Animations[0].FPS == 23);
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetAnimationFps(0, 0));

                AnimationState movedAnimation = project.Animations[0];
                session.MoveAnimation(0, 2);
                Require(ReferenceEquals(project.Animations[2], movedAnimation));
                Require(session.Undo() && ReferenceEquals(project.Animations[0], movedAnimation));
                Require(session.Redo() && ReferenceEquals(project.Animations[2], movedAnimation));
                session.RemoveAnimation(2);
                Require(project.Animations.Count == 2);
                Require(session.Undo() && ReferenceEquals(project.Animations[2], movedAnimation));
                Require(session.Redo() && project.Animations.Count == 2);

                Require(session.SelectFrame(0, 0));
                AnimationState selectedAnimation = project.Animations[0];
                int initialFrames = selectedAnimation.Frames.Count;
                Frame addedFrame = session.AddFrame();
                Require(selectedAnimation.Frames.Count == initialFrames + 1);
                Require(addedFrame.FrameIndex == initialFrames);
                Require(session.Undo() && selectedAnimation.Frames.Count == initialFrames);
                Require(session.Redo() && selectedAnimation.Frames.Count == initialFrames + 1);

                Frame duplicatedFrame = session.DuplicateFrame(0);
                Require(selectedAnimation.Frames.Count == initialFrames + 2);
                Require(!ReferenceEquals(selectedAnimation.Frames[0], duplicatedFrame));
                Require(session.Undo() && selectedAnimation.Frames.Count == initialFrames + 1);
                Require(session.Redo() && selectedAnimation.Frames.Count == initialFrames + 2);
                session.MoveFrame(0, selectedAnimation.Frames.Count - 1);
                Require(selectedAnimation.Frames[selectedAnimation.Frames.Count - 1].FrameIndex ==
                    selectedAnimation.Frames.Count - 1);
                Require(session.Undo());
                Require(session.Redo());
                int removeFrameIndex = selectedAnimation.Frames.Count - 1;
                Frame removedFrame = selectedAnimation.Frames[removeFrameIndex];
                session.RemoveFrame(removeFrameIndex);
                Require(selectedAnimation.Frames.Count == initialFrames + 1);
                Require(session.Undo() && ReferenceEquals(selectedAnimation.Frames[removeFrameIndex], removedFrame));
                Require(session.Redo() && selectedAnimation.Frames.Count == initialFrames + 1);

                Require(session.SelectFrame(0, 0));
                Frame frame = session.CurrentFrame;
                int initialLayers = frame.Layers.Count;
                Layer addedLayer = session.AddLayer("Ink");
                Require(frame.Layers.Count == initialLayers + 1 && ReferenceEquals(frame.Layers[1], addedLayer));
                Require(session.Undo() && frame.Layers.Count == initialLayers);
                Require(session.Redo() && frame.Layers.Count == initialLayers + 1);
                Layer duplicateLayer = session.DuplicateLayer(0);
                Require(frame.Layers.Count == initialLayers + 2 && !ReferenceEquals(frame.Layers[0], duplicateLayer));
                Require(session.Undo() && frame.Layers.Count == initialLayers + 1);
                Require(session.Redo() && frame.Layers.Count == initialLayers + 2);
                session.MoveLayer(0, 2);
                Require(session.Undo());
                Require(session.Redo());
                session.RenameLayer(0, "Foreground");
                Require(frame.Layers[0].Name == "Foreground");
                Require(session.Undo());
                Require(session.Redo() && frame.Layers[0].Name == "Foreground");
                session.SetLayerVisibility(0, false);
                Require(!frame.Layers[0].IsVisible);
                Require(session.Undo() && frame.Layers[0].IsVisible);
                Require(session.Redo() && !frame.Layers[0].IsVisible);
                session.SetLayerOpacity(0, 0.25f);
                Require(frame.Layers[0].Opacity == 0.25f);
                Require(session.Undo() && frame.Layers[0].Opacity == Config.Layer.DefaultOpacity);
                Require(session.Redo() && frame.Layers[0].Opacity == 0.25f);
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetLayerOpacity(0, float.NaN));
                session.RemoveLayer(frame.Layers.Count - 1);
                Require(frame.Layers.Count == initialLayers + 1);
                Require(session.Undo() && frame.Layers.Count == initialLayers + 2);
                Require(session.Redo() && frame.Layers.Count == initialLayers + 1);

                object oldSpeed = project.AssetProperties[SharedConfig.BaseSpeedField];
                Require(session.SetProperty(SharedConfig.BaseSpeedField, 12f).Succeeded);
                Require(Convert.ToSingle(project.AssetProperties[SharedConfig.BaseSpeedField]) == 12f);
                Require(session.Undo());
                Require(Equals(project.AssetProperties[SharedConfig.BaseSpeedField], oldSpeed));
                Require(session.Redo());
                Require(session.SetProperty("unknown", 1).Status == PropertyEditStatus.UnknownField);

                session.History.Clear();
                frame.Layers[0].IsVisible = true;
                frame.Layers[0].Opacity = 1f;
                frame.Layers[0].Clear();
                session.ActiveTool = null;
                Require(!session.PointerDown(1, 1) && !session.PointerDrag(1, 1) && !session.PointerUp(1, 1));

                session.ActiveTool = new SessionThrowingTool(SessionThrowPhase.Down);
                RequireThrows<InvalidOperationException>(() => session.PointerDown(1, 1));
                Require(frame.Layers[0].GetPixel(1, 1).Rgba == 0u && !session.History.CanUndo);

                session.ActiveTool = new SessionThrowingTool(SessionThrowPhase.Drag);
                Require(session.PointerDown(1, 1));
                RequireThrows<InvalidOperationException>(() => session.PointerDrag(2, 2));
                Require(frame.Layers[0].GetPixel(1, 1).Rgba == 0u);
                Require(frame.Layers[0].GetPixel(2, 2).Rgba == 0u && !session.History.CanUndo);

                session.ActiveTool = new SessionThrowingTool(SessionThrowPhase.Up);
                Require(session.PointerDown(1, 1));
                RequireThrows<InvalidOperationException>(() => session.PointerUp(2, 2));
                Require(frame.Layers[0].GetPixel(1, 1).Rgba == 0u);
                Require(frame.Layers[0].GetPixel(2, 2).Rgba == 0u && !session.History.CanUndo);

                session.ActiveTool = new PencilTool { CurrentColor = Color(4, 5, 6, 255) };
                Require(session.PointerDown(3, 3));
                session.CancelGesture();
                Require(frame.Layers[0].GetPixel(3, 3).Rgba == 0u && !session.History.CanUndo);
                session.CancelGesture();
                Require(stateEvents > 0);
            }
            finally
            {
                session.Dispose();
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestPreviewStateMachineExtremeTiming()
    {
        var preview = new PreviewController();
        int events = 0;
        preview.StateChanged += snapshot =>
        {
            events++;
            Require(snapshot.FrameIndex >= 0);
        };
        Require(preview.Current.State == PreviewPlaybackState.Empty);
        preview.Play();
        preview.Pause();
        preview.Stop();
        Require(events == 0);
        RequireThrows<InvalidOperationException>(() => preview.CopyCurrentPixelsTo(new PixelColor[1]));
        RequireThrows<ArgumentOutOfRangeException>(() => preview.SeekFrame(0));
        RequireThrows<ArgumentNullException>(() => preview.Load(null, 0));

        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 3);
            project.Animations[0].FPS = 4;
            for (int index = 0; index < 3; index++)
                project.Animations[0].Frames[index].Layers[0].Pixels[0].Rgba =
                    Pack((byte)(index + 1), 0, 0, 255);
            ProjectSnapshot snapshot = ProjectSnapshot.Capture(project);
            RequireThrows<ArgumentOutOfRangeException>(() => preview.Load(snapshot, -1));
            RequireThrows<ArgumentOutOfRangeException>(() => preview.Load(snapshot, 1));
            preview.Load(snapshot, 0);
            Require(preview.Current.State == PreviewPlaybackState.Stopped && preview.Current.FrameIndex == 0);
            RequireThrows<ArgumentOutOfRangeException>(() => preview.Tick(TimeSpan.FromTicks(-1)));
            Require(!preview.Tick(TimeSpan.Zero));

            preview.Play();
            Require(!preview.Tick(TimeSpan.FromMilliseconds(100)));
            Require(preview.Tick(TimeSpan.FromMilliseconds(150)));
            Require(preview.Current.FrameIndex == 1);
            preview.Pause();
            Require(!preview.Tick(TimeSpan.FromHours(1)) && preview.Current.FrameIndex == 1);
            preview.SeekFrame(2);
            PixelColor[] pixels = new PixelColor[project.CanvasWidth * project.CanvasHeight];
            preview.CopyCurrentPixelsTo(pixels);
            Require(pixels[0].R == 3 && pixels[0].A == 255);
            RequireThrows<ArgumentException>(() => preview.CopyCurrentPixelsTo(new PixelColor[1]));

            preview.IsLooping = false;
            preview.Play();
            Require(preview.Tick(TimeSpan.FromDays(1)));
            Require(preview.Current.FrameIndex == 2 && preview.Current.State == PreviewPlaybackState.Paused);
            preview.Stop();
            Require(preview.Current.FrameIndex == 0 && preview.Current.State == PreviewPlaybackState.Stopped);

            preview.IsLooping = true;
            preview.Play();
            TimeSpan beyondIntFrames = TimeSpan.FromSeconds(
                ((double)int.MaxValue + 6d) / project.Animations[0].FPS);
            decimal exactFrames = ((decimal)beyondIntFrames.Ticks * project.Animations[0].FPS) /
                TimeSpan.TicksPerSecond;
            int expectedFrame = (int)((long)decimal.Floor(exactFrames) % 3L);
            Require(preview.Tick(beyondIntFrames));
            Require(preview.Current.FrameIndex == expectedFrame);
            Require(events >= 8);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // LoadPreview validation scope (#30): the preview only requires STRUCTURAL
    // validity, so schema-level gaps (an empty identity name while sketching) no
    // longer block playback; structural breakage still does.
    private static void TestSessionPreviewStructuralValidationScope()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 3);
            project.AssetProperties[SharedConfig.EntityNameField] = "   ";
            project.Animations[0].FPS = 10;
            for (int index = 0; index < 3; index++)
                project.Animations[0].Frames[index].Layers[0].Pixels[0].Rgba =
                    Pack((byte)(index + 1), 0, 0, 255);
            using (DesignerSession session = DesignerSession.Create("StructuralPreview", project, root))
            {
                session.AutosaveEnabled = false;
                // The designer-scope session validation still flags the empty name...
                Require(!session.Current.Validation.IsValid);
                // ...but the preview loads and plays regardless.
                session.LoadPreview(0);
                Require(session.Preview.Current.State == PreviewPlaybackState.Stopped);
                session.Preview.Play();
                Require(session.Preview.Tick(TimeSpan.FromMilliseconds(100)));
                Require(session.Preview.Current.FrameIndex == 1);
                var pixels = new PixelColor[project.CanvasWidth * project.CanvasHeight];
                session.Preview.CopyCurrentPixelsTo(pixels);
                Require(pixels[0].R == 2 && pixels[0].A == 255);

                // Structural breakage still blocks the preview.
                project.Animations[0].Frames.Add(
                    new Frame(project.CanvasWidth + 1, project.CanvasHeight));
                RequireThrows<ProjectValidationException>(() => session.LoadPreview(0));
                project.Animations[0].Frames.RemoveAt(3);
                session.LoadPreview(0);
                Require(session.Preview.Current.FrameCount == 3);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // Item #27: validation is deferred off the per-command hot path (MarkDirty
    // no longer validates synchronously) but a reader always gets a CURRENT
    // result. Proven behaviorally: the result reflects the post-command state,
    // is cached between reads (compute-once), recomputes after the next edit,
    // and every published snapshot still carries a non-null current validation.
    private static void TestSessionValidationDeferredComputeOnDemand()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("DeferredValidation", project, root))
            {
                session.AutosaveEnabled = false;

                // The constructor computed a baseline; with no edit between reads
                // the SAME cached instance comes back (the validate ran once).
                ProjectValidationResult baseline = session.Current.Validation;
                Require(baseline != null && baseline.IsValid);
                Require(ReferenceEquals(baseline, session.Current.Validation));

                // A property the toolbar accepts but the validator rejects: reading
                // AFTER the command surfaces the new (invalid) result, so it was
                // resolved on demand rather than frozen at construction.
                session.SetProperty(SharedConfig.EntityNameField, "../escape");
                ProjectValidationResult afterBreak = session.Current.Validation;
                Require(!ReferenceEquals(baseline, afterBreak));
                Require(!afterBreak.IsValid);
                Require(ContainsIssue(afterBreak, ProjectIssueCode.InvalidIdentityName));
                // Still cached until the next dirtying edit.
                Require(ReferenceEquals(afterBreak, session.Current.Validation));

                // Fixing the identity and reading again surfaces the valid result.
                session.SetProperty(SharedConfig.EntityNameField, "FixedName");
                ProjectValidationResult afterFix = session.Current.Validation;
                Require(!ReferenceEquals(afterBreak, afterFix));
                Require(afterFix.IsValid);

                // The published snapshot carries a current, non-null validation too.
                var publishedValidations = new System.Collections.Generic.List<ProjectValidationResult>();
                session.StateChanged += snapshot =>
                {
                    Require(snapshot.Validation != null);
                    publishedValidations.Add(snapshot.Validation);
                };
                session.SetProperty(SharedConfig.EntityNameField, "   ");
                Require(publishedValidations.Count >= 1);
                Require(!publishedValidations[publishedValidations.Count - 1].IsValid);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestMapToolDeterminismAndModes()
    {
        var missing = new MapTool();
        int events = 0;
        missing.OperationCompleted += result =>
        {
            events++;
            Require(result.OperationSeed != 0u);
        };
        MapOperationResult result = missing.Execute(null, 0, 0);
        Require(result.Status == MapOperationStatus.MissingTargetMap && result.AffectedCount == 0);
        missing.TargetMap = new FastGridMap(8, 7);
        result = missing.Execute(null, 0, 0);
        Require(result.Status == MapOperationStatus.MissingSelectedAsset);
        missing.Mode = "unsupported";
        result = missing.Execute(null, 0, 0);
        Require(result.Status == MapOperationStatus.UnsupportedMode && events == 3);

        var leftMap = new FastGridMap(32, 24);
        var rightMap = new FastGridMap(32, 24);
        MapTool left = SessionCreateScatterTool(leftMap, 0x12345678u);
        MapTool right = SessionCreateScatterTool(rightMap, 0x12345678u);
        for (int operation = 0; operation < 5; operation++)
        {
            MapOperationResult leftResult = left.Execute(null, 10 + operation, 11 - operation);
            MapOperationResult rightResult = right.Execute(null, 10 + operation, 11 - operation);
            Require(leftResult.Status == MapOperationStatus.Succeeded);
            Require(leftResult.OperationSeed == rightResult.OperationSeed);
            Require(leftResult.AffectedCount == 17 && rightResult.AffectedCount == 17);
        }
        Require(left.OperationIndex == 5 && right.OperationIndex == 5);
        Require(leftMap.Props.Count == rightMap.Props.Count && leftMap.Props.Count == 85);
        for (int index = 0; index < leftMap.Props.Count; index++)
        {
            FastGridMap.MapPropData leftProp = leftMap.Props[index];
            FastGridMap.MapPropData rightProp = rightMap.Props[index];
            Require(leftProp.AssetKey == rightProp.AssetKey);
            Require(leftProp.X == rightProp.X && leftProp.Y == rightProp.Y);
            Require(leftProp.Scale == rightProp.Scale);
            Require(leftProp.Scale >= 0.75f && leftProp.Scale <= 1.25f);
        }

        left.ResetSequence();
        leftMap.Props.Clear();
        MapOperationResult repeated = left.Execute(null, 10, 11);
        Require(repeated.OperationSeed == rightResultSeedForFirst(right));
        Require(leftMap.Props.Count == 17);
        left.ScatterDensity = int.MaxValue;
        Require(left.ScatterDensity == Config.Tool.Map.MaxScatterDensity);
        left.ScatterDensity = -1;
        Require(left.ScatterDensity == 0);
        RequireThrows<ArgumentOutOfRangeException>(() => left.FillProbability = float.NaN);
        RequireThrows<ArgumentOutOfRangeException>(() => left.MinScaleJitter = float.PositiveInfinity);
        RequireThrows<ArgumentOutOfRangeException>(() => left.MaxScaleJitter = float.NegativeInfinity);

        var tileMap = new FastGridMap(8, 8);
        var tile = new MapTool
        {
            TargetMap = tileMap,
            SelectedAsset = "Pyramid",
            Mode = Config.Tool.Map.ModeTile
        };
        result = tile.Execute(null, 63, 65);
        Require(result.Status == MapOperationStatus.Succeeded && result.AffectedCount == 1);
        Require(tileMap.Props.Count == 1);
        Require(tileMap.Props[0].X == 32 && tileMap.Props[0].Y == 64);

        var noiseA = new FastGridMap(13, 11);
        var noiseB = new FastGridMap(13, 11);
        var noiseLeft = new MapTool
        {
            TargetMap = noiseA,
            Mode = Config.Tool.Map.ModeNoiseFill,
            Seed = 99,
            FillProbability = 0.4f,
            TargetTileId = 7
        };
        var noiseRight = new MapTool
        {
            TargetMap = noiseB,
            Mode = Config.Tool.Map.ModeNoiseFill,
            Seed = 99,
            FillProbability = 0.4f,
            TargetTileId = 7
        };
        MapOperationResult noiseResult = noiseLeft.Execute(null, 0, 0);
        Require(noiseResult.OperationSeed == noiseRight.Execute(null, 0, 0).OperationSeed);
        for (int index = 0; index < noiseA.RawData.Length; index++)
            Require(noiseA.RawData[index] == noiseB.RawData[index]);
        noiseLeft.FillProbability = 0f;
        Require(noiseLeft.Execute(null, 0, 0).AffectedCount == 0);
        noiseLeft.FillProbability = 1f;
        noiseLeft.TargetTileId = 9;
        Require(noiseLeft.Execute(null, 0, 0).AffectedCount == noiseA.RawData.Length);
        for (int index = 0; index < noiseA.RawData.Length; index++) Require(noiseA.RawData[index] == 9);

        noiseLeft.Mode = Config.Tool.Map.ModeAutomataSmooth;
        noiseLeft.BaseTileId = 2;
        noiseLeft.TargetTileId = 9;
        Require(noiseLeft.Execute(null, 0, 0).AffectedCount == noiseA.RawData.Length);
        for (int index = 0; index < noiseA.RawData.Length; index++)
            Require(noiseA.RawData[index] == 2 || noiseA.RawData[index] == 9);

        EFYVProject seededProject = new EFYVProject(Config.Types.AssetTypeEnemyData) { DesignerSeed = 1234u };
        tile.OnPointerDown(seededProject, null, 0, 0);
        Require(tile.Seed == 1234u && tile.OperationIndex == 1);
    }

    private static MapTool SessionCreateScatterTool(FastGridMap map, uint seed)
    {
        return new MapTool
        {
            TargetMap = map,
            SelectedAsset = "Obelisk",
            Mode = Config.Tool.Map.ModeScatter,
            Seed = seed,
            ScatterDensity = 17,
            ScatterRadius = 6f,
            MinScaleJitter = 0.75f,
            MaxScaleJitter = 1.25f
        };
    }

    private static uint rightResultSeedForFirst(MapTool tool)
    {
        tool.ResetSequence();
        tool.TargetMap.Props.Clear();
        return tool.Execute(null, 10, 11).OperationSeed;
    }

    private enum SessionThrowPhase
    {
        Down,
        Drag,
        Up
    }

    private sealed class SessionThrowingTool : Tool, ILayerTool
    {
        private readonly SessionThrowPhase phase;

        public int ActiveLayerIndex { get; set; }

        public SessionThrowingTool(SessionThrowPhase phase)
        {
            this.phase = phase;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            currentFrame.Layers[ActiveLayerIndex].SetPixel(x, y, Color(200, 1, 1, 255));
            if (phase == SessionThrowPhase.Down) throw new InvalidOperationException();
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            currentFrame.Layers[ActiveLayerIndex].SetPixel(x, y, Color(201, 1, 1, 255));
            if (phase == SessionThrowPhase.Drag) throw new InvalidOperationException();
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            currentFrame.Layers[ActiveLayerIndex].SetPixel(x, y, Color(202, 1, 1, 255));
            if (phase == SessionThrowPhase.Up) throw new InvalidOperationException();
        }
    }
}
