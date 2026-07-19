// batch3.3 agent (item #10): animation and layer workflow depth - per-frame
// duration overrides (model + .efyvmake + .efyvlaby atlas), loop-range and
// ping-pong playback tags flowing model -> persistence -> export, the
// PreviewController timing rewrite honoring both, host-agnostic onion-skin
// compositing on ViewportController, cross-frame layer batch session commands,
// and the layer-preserving generator merge plus the bob/breathe and
// shake/hit-flash movement presets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    // ------------------------------------------------------------------
    // Frame.DurationMs + AnimationState playback tags: model contracts,
    // clone fidelity, snapshot capture, and .efyvmake round trip (including
    // legacy documents and the malformed-value corpus).
    // ------------------------------------------------------------------
    private static void TestAnimationTimingModelAndPersistence()
    {
        // Frame duration: 0 sentinel plus the bounded millisecond range.
        var frame = new Frame(4, 3, 0);
        Require(frame.DurationMs == Config.Animation.InheritFrameDurationMs);
        frame.DurationMs = Config.Animation.MinFrameDurationMs;
        Require(frame.DurationMs == Config.Animation.MinFrameDurationMs);
        frame.DurationMs = Config.Animation.MaxFrameDurationMs;
        frame.DurationMs = Config.Animation.InheritFrameDurationMs;
        RequireThrows<ArgumentOutOfRangeException>(() => frame.DurationMs = -1);
        RequireThrows<ArgumentOutOfRangeException>(
            () => frame.DurationMs = Config.Animation.MaxFrameDurationMs + 1);
        frame.DurationMs = 125;
        Frame clonedFrame = frame.Clone();
        Require(clonedFrame.DurationMs == 125);
        clonedFrame.DurationMs = 250;
        Require(frame.DurationMs == 125); // clone is isolated

        // Playback tags: defaults, validation, clone fidelity.
        var animation = new AnimationState("TagProbe", 8);
        Require(animation.LoopStartFrame == Config.Animation.DefaultLoopStartFrame);
        Require(animation.LoopEndFrame == Config.Animation.FullRangeLoopEnd);
        Require(!animation.PingPong);
        RequireThrows<ArgumentOutOfRangeException>(() => animation.LoopStartFrame = -1);
        RequireThrows<ArgumentOutOfRangeException>(
            () => animation.LoopEndFrame = Config.Animation.FullRangeLoopEnd - 1);
        animation.LoopStartFrame = 2;
        animation.LoopEndFrame = 5;
        animation.PingPong = true;
        animation.Frames.Add(frame.Clone());
        AnimationState clonedAnimation = animation.Clone();
        Require(clonedAnimation.LoopStartFrame == 2);
        Require(clonedAnimation.LoopEndFrame == 5);
        Require(clonedAnimation.PingPong);
        Require(clonedAnimation.Frames[0].DurationMs == 125);

        // Snapshot capture: raw values plus clamped effective accessors.
        var project = new EFYVProject(Config.Types.AssetTypeEnemyData);
        project.CanvasWidth = 4;
        project.CanvasHeight = 3;
        var stale = new AnimationState("Stale", 6);
        for (int index = 0; index < 3; index++)
        {
            var staleFrame = new Frame(4, 3, index);
            staleFrame.DurationMs = index == 1 ? 90 : Config.Animation.InheritFrameDurationMs;
            stale.Frames.Add(staleFrame);
        }
        stale.LoopStartFrame = 7;  // stale: past the last frame
        stale.LoopEndFrame = 9;    // stale: past the last frame
        stale.PingPong = true;
        project.Animations.Add(stale);
        ProjectSnapshot snapshot = ProjectSnapshot.Capture(project);
        AnimationSnapshot captured = snapshot.Animations[0];
        Require(captured.LoopStartFrame == 7 && captured.LoopEndFrame == 9 && captured.PingPong);
        Require(captured.EffectiveLoopStart == 2 && captured.EffectiveLoopEnd == 2);
        Require(captured.Frames[0].DurationMs == 0);
        Require(captured.Frames[1].DurationMs == 90);
        var explicitRange = new AnimationState("Explicit", 6);
        for (int index = 0; index < 4; index++) explicitRange.Frames.Add(new Frame(4, 3, index));
        explicitRange.LoopStartFrame = 1;
        explicitRange.LoopEndFrame = Config.Animation.FullRangeLoopEnd;
        project.Animations.Add(explicitRange);
        AnimationSnapshot fullRange = ProjectSnapshot.Capture(project).Animations[1];
        Require(fullRange.EffectiveLoopStart == 1 && fullRange.EffectiveLoopEnd == 3);

        // .efyvmake round trip preserves durations and tags exactly.
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject persisted = CreateValidProject(root, 3);
            persisted.Animations[0].Frames[0].DurationMs = 40;
            persisted.Animations[0].Frames[2].DurationMs = Config.Animation.MaxFrameDurationMs;
            persisted.Animations[0].LoopStartFrame = 1;
            persisted.Animations[0].LoopEndFrame = 2;
            persisted.Animations[0].PingPong = true;
            var persistence = new ProjectPersistenceService(root);
            string savedPath = persistence.SaveProject("timing", persisted, CancellationToken.None);
            EFYVProject loaded = persistence.LoadProject("timing");
            Require(loaded.Animations[0].Frames[0].DurationMs == 40);
            Require(loaded.Animations[0].Frames[1].DurationMs == Config.Animation.InheritFrameDurationMs);
            Require(loaded.Animations[0].Frames[2].DurationMs == Config.Animation.MaxFrameDurationMs);
            Require(loaded.Animations[0].LoopStartFrame == 1);
            Require(loaded.Animations[0].LoopEndFrame == 2);
            Require(loaded.Animations[0].PingPong);

            // Legacy documents (fields stripped) restore to the defaults - the
            // extension rides without a format-version bump exactly like the
            // palette section.
            JsonObject document = JsonNode.Parse(File.ReadAllText(savedPath)).AsObject();
            foreach (JsonNode animationNode in document["animations"].AsArray())
            {
                JsonObject animationObject = animationNode.AsObject();
                Require(animationObject.Remove("loopStart"));
                Require(animationObject.Remove("loopEnd"));
                Require(animationObject.Remove("pingPong"));
                foreach (JsonNode frameNode in animationObject["frames"].AsArray())
                    Require(frameNode.AsObject().Remove("durationMs"));
            }
            string legacyPath = persistence.GetProjectPath("legacytiming");
            File.WriteAllText(legacyPath, document.ToJsonString());
            EFYVProject legacy = persistence.LoadProject("legacytiming");
            Require(legacy.Animations[0].LoopStartFrame == Config.Animation.DefaultLoopStartFrame);
            Require(legacy.Animations[0].LoopEndFrame == Config.Animation.FullRangeLoopEnd);
            Require(!legacy.Animations[0].PingPong);
            foreach (Frame legacyFrame in legacy.Animations[0].Frames)
                Require(legacyFrame.DurationMs == Config.Animation.InheritFrameDurationMs);

            // Malformed-value corpus: each corrupt value rejects the document.
            void RequireCorruptRejected(Action<JsonObject> corrupt)
            {
                JsonObject corrupted = JsonNode.Parse(File.ReadAllText(savedPath)).AsObject();
                corrupt(corrupted);
                string corruptPath = persistence.GetProjectPath("corrupttiming");
                File.WriteAllText(corruptPath, corrupted.ToJsonString());
                RequireThrows<InvalidDataException>(() => persistence.LoadProject("corrupttiming"));
            }

            RequireCorruptRejected(o =>
                o["animations"][0]["frames"][0]["durationMs"] = -1);
            RequireCorruptRejected(o =>
                o["animations"][0]["frames"][1]["durationMs"] = Config.Animation.MaxFrameDurationMs + 1);
            RequireCorruptRejected(o => o["animations"][0]["loopStart"] = -1);
            RequireCorruptRejected(o =>
                o["animations"][0]["loopEnd"] = Config.Animation.FullRangeLoopEnd - 1);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // PreviewController: per-frame durations, loop ranges, ping-pong, and
    // large-elapsed cycle reduction.
    // ------------------------------------------------------------------
    private static void TestPreviewDurationsLoopRangeAndPingPong()
    {
        ProjectSnapshot CaptureTimed(
            int frameCount,
            int fps,
            int[] durations,
            int loopStart,
            int loopEnd,
            bool pingPong)
        {
            var project = new EFYVProject(Config.Types.AssetTypeEnemyData);
            project.CanvasWidth = 4;
            project.CanvasHeight = 3;
            var animation = new AnimationState("Timed", fps);
            for (int index = 0; index < frameCount; index++)
            {
                var frame = new Frame(4, 3, index);
                if (durations != null) frame.DurationMs = durations[index];
                frame.Layers[0].Pixels[0].Rgba = Pack((byte)(index + 1), 0, 0, 255);
                animation.Frames.Add(frame);
            }
            animation.LoopStartFrame = loopStart;
            animation.LoopEndFrame = loopEnd;
            animation.PingPong = pingPong;
            project.Animations.Add(animation);
            return ProjectSnapshot.Capture(project);
        }

        // Mixed per-frame durations: fps 10 (100ms default), frame 0 overridden
        // to 200ms and frame 2 to 50ms.
        var preview = new PreviewController { IsLooping = true };
        preview.Load(CaptureTimed(3, 10, new[] { 200, 0, 50 }, 0, -1, false), 0);
        preview.Play();
        Require(preview.Current.CurrentFrameDurationMs == 200);
        Require(!preview.Tick(TimeSpan.FromMilliseconds(199)));   // 199 < 200
        Require(preview.Tick(TimeSpan.FromMilliseconds(1)));      // exact boundary
        Require(preview.Current.FrameIndex == 1);
        Require(preview.Current.CurrentFrameDurationMs == 0);     // inherit sentinel
        Require(!preview.Tick(TimeSpan.FromMilliseconds(99)));
        Require(preview.Tick(TimeSpan.FromMilliseconds(1)));
        Require(preview.Current.FrameIndex == 2);
        Require(preview.Tick(TimeSpan.FromMilliseconds(50)));     // short frame
        Require(preview.Current.FrameIndex == 0);
        // One full 350ms cycle in a single tick lands exactly back on frame 0.
        Require(preview.Tick(TimeSpan.FromMilliseconds(350)));
        Require(preview.Current.FrameIndex == 0);
        // A giant elapsed reduces modulo the 350ms cycle: 1 hour = 3,600,000ms
        // = 10285 cycles + 250ms -> 250ms consumes frames 0 (200) then sits
        // 50ms into frame 1 (100ms).
        Require(preview.Tick(TimeSpan.FromHours(1)));
        Require(preview.Current.FrameIndex == 1);
        Require(preview.Tick(TimeSpan.FromMilliseconds(50)));
        Require(preview.Current.FrameIndex == 2);

        // Residual survives Pause/Play, dies on Seek/Stop - same contract as
        // the fps-only model.
        preview.Load(CaptureTimed(3, 10, new[] { 200, 0, 50 }, 0, -1, false), 0);
        preview.Play();
        Require(!preview.Tick(TimeSpan.FromMilliseconds(150)));
        preview.Pause();
        Require(!preview.Tick(TimeSpan.FromHours(2)));
        preview.Play();
        Require(preview.Tick(TimeSpan.FromMilliseconds(50)));
        Require(preview.Current.FrameIndex == 1);
        Require(!preview.Tick(TimeSpan.FromMilliseconds(99)));
        preview.SeekFrame(2);
        Require(!preview.Tick(TimeSpan.FromMilliseconds(49)));    // residual discarded
        Require(preview.Tick(TimeSpan.FromMilliseconds(1)));
        Require(preview.Current.FrameIndex == 0);

        // Ping-pong over the full range: 0,1,2,1,0,1,2,...
        preview = new PreviewController { IsLooping = true };
        preview.Load(CaptureTimed(3, 10, null, 0, -1, true), 0);
        preview.Play();
        Require(preview.Current.PingPong);
        int[] expectedPingPong = { 1, 2, 1, 0, 1, 2, 1, 0, 1 };
        for (int step = 0; step < expectedPingPong.Length; step++)
        {
            Require(preview.Tick(TimeSpan.FromMilliseconds(100)));
            Require(preview.Current.FrameIndex == expectedPingPong[step]);
        }
        // Ping-pong cycle reduction: 3 frames -> 4-step period; one hour at
        // 10fps is 36000 steps = 9000 whole periods from (1, forward), so the
        // playhead returns exactly to frame 1 heading forward.
        Require(preview.Tick(TimeSpan.FromHours(1)));
        Require(preview.Current.FrameIndex == expectedPingPong[^1]);
        Require(preview.Tick(TimeSpan.FromMilliseconds(100)));
        Require(preview.Current.FrameIndex == 2);

        // Loop range with intro (non-ping-pong): 4 frames, range [1..2]:
        // 0,1,2,1,2,...; the range is reported through the snapshot.
        preview = new PreviewController { IsLooping = true };
        preview.Load(CaptureTimed(4, 10, null, 1, 2, false), 0);
        preview.Play();
        Require(preview.Current.LoopStartFrame == 1 && preview.Current.LoopEndFrame == 2);
        int[] expectedRange = { 1, 2, 1, 2, 1 };
        for (int step = 0; step < expectedRange.Length; step++)
        {
            Require(preview.Tick(TimeSpan.FromMilliseconds(100)));
            Require(preview.Current.FrameIndex == expectedRange[step]);
        }
        var rangePixels = new PixelColor[12];
        preview.CopyCurrentPixelsTo(rangePixels);
        Require(rangePixels[0].R == 2); // frame index 1 marker

        // Ping-pong inside a loop range with intro: 5 frames, [1..3]:
        // 0,1,2,3,2,1,2,3,...
        preview = new PreviewController { IsLooping = true };
        preview.Load(CaptureTimed(5, 10, null, 1, 3, true), 0);
        preview.Play();
        int[] expectedRangedPingPong = { 1, 2, 3, 2, 1, 2, 3, 2, 1, 2 };
        for (int step = 0; step < expectedRangedPingPong.Length; step++)
        {
            Require(preview.Tick(TimeSpan.FromMilliseconds(100)));
            Require(preview.Current.FrameIndex == expectedRangedPingPong[step]);
        }

        // A one-frame loop range consumes time without moving and still
        // reports progress.
        preview = new PreviewController { IsLooping = true };
        preview.Load(CaptureTimed(3, 10, null, 2, 2, false), 0);
        preview.Play();
        Require(preview.Tick(TimeSpan.FromMilliseconds(100)));
        Require(preview.Current.FrameIndex == 1);
        Require(preview.Tick(TimeSpan.FromMilliseconds(100)));
        Require(preview.Current.FrameIndex == 2);
        Require(preview.Tick(TimeSpan.FromSeconds(10)));
        Require(preview.Current.FrameIndex == 2);
        Require(preview.Current.State == PreviewPlaybackState.Playing);

        // Stale tags (past the frame count) clamp to the last frame.
        preview = new PreviewController { IsLooping = true };
        preview.Load(CaptureTimed(2, 10, null, 7, 9, false), 0);
        preview.Play();
        Require(preview.Current.LoopStartFrame == 1 && preview.Current.LoopEndFrame == 1);
        Require(preview.Tick(TimeSpan.FromMilliseconds(100)));
        Require(preview.Current.FrameIndex == 1);
        Require(preview.Tick(TimeSpan.FromMilliseconds(300)));
        Require(preview.Current.FrameIndex == 1);

        // Non-looping playback IGNORES the tags: forward once, pause at the
        // end, residual dropped - even with ping-pong and a loop range set.
        preview = new PreviewController { IsLooping = false };
        preview.Load(CaptureTimed(3, 10, new[] { 0, 50, 0 }, 1, 2, true), 0);
        preview.Play();
        Require(preview.Tick(TimeSpan.FromMilliseconds(100)));
        Require(preview.Current.FrameIndex == 1);
        Require(preview.Tick(TimeSpan.FromMilliseconds(50)));
        Require(preview.Current.FrameIndex == 2);
        Require(preview.Tick(TimeSpan.FromDays(2)));
        Require(preview.Current.FrameIndex == 2);
        Require(preview.Current.State == PreviewPlaybackState.Paused);
        preview.Play();
        Require(preview.Tick(TimeSpan.FromSeconds(1)));
        Require(preview.Current.FrameIndex == 2);
        Require(preview.Current.State == PreviewPlaybackState.Paused);
    }

    // ------------------------------------------------------------------
    // Session commands: per-frame duration, loop range, ping-pong.
    // ------------------------------------------------------------------
    private static void TestSessionFrameDurationAndPlaybackTagCommands()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 3);
            using (DesignerSession session = DesignerSession.Create("TimingCmds", project, root))
            {
                session.AutosaveEnabled = false;
                Require(session.SelectFrame(0, 0));
                session.History.Clear();
                AnimationState animation = project.Animations[0];

                // Duration: undoable, no-op-safe, validated.
                session.SetFrameDurationMs(1, 150);
                Require(animation.Frames[1].DurationMs == 150);
                Require(session.Current.History.UndoCount == 1);
                session.SetFrameDurationMs(1, 150); // no-op records nothing
                Require(session.Current.History.UndoCount == 1);
                Require(session.Undo());
                Require(animation.Frames[1].DurationMs == Config.Animation.InheritFrameDurationMs);
                Require(session.Redo());
                Require(animation.Frames[1].DurationMs == 150);
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetFrameDurationMs(3, 100));
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetFrameDurationMs(-1, 100));
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetFrameDurationMs(0, -5));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.SetFrameDurationMs(0, Config.Animation.MaxFrameDurationMs + 1));
                Require(session.Current.IsDirty);

                // Loop range: both members swap as ONE command.
                session.History.Clear();
                session.SetAnimationLoopRange(0, 1, 2);
                Require(animation.LoopStartFrame == 1 && animation.LoopEndFrame == 2);
                Require(session.Current.History.UndoCount == 1);
                session.SetAnimationLoopRange(0, 1, 2); // no-op
                Require(session.Current.History.UndoCount == 1);
                session.SetAnimationLoopRange(0, 0, Config.Animation.FullRangeLoopEnd);
                Require(session.Current.History.UndoCount == 2);
                Require(session.Undo());
                Require(animation.LoopStartFrame == 1 && animation.LoopEndFrame == 2);
                Require(session.Undo());
                Require(animation.LoopStartFrame == Config.Animation.DefaultLoopStartFrame);
                Require(animation.LoopEndFrame == Config.Animation.FullRangeLoopEnd);
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetAnimationLoopRange(0, -1, 2));
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetAnimationLoopRange(0, 3, 3));
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetAnimationLoopRange(0, 2, 1));
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetAnimationLoopRange(0, 0, 3));
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetAnimationLoopRange(1, 0, 0));

                // Ping-pong toggle.
                session.History.Clear();
                session.SetAnimationPingPong(0, true);
                Require(animation.PingPong);
                session.SetAnimationPingPong(0, true); // no-op
                Require(session.Current.History.UndoCount == 1);
                Require(session.Undo());
                Require(!animation.PingPong);
                Require(session.Redo());
                Require(animation.PingPong);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Cross-frame layer batch operations: one undoable command across ALL
    // frames of the selected animation, atomic up-front validation.
    // ------------------------------------------------------------------
    private static void TestSessionBatchLayerOpsAcrossFrames()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 3);
            using (DesignerSession session = DesignerSession.Create("BatchLayers", project, root))
            {
                session.AutosaveEnabled = false;
                Require(session.SelectFrame(0, 0));
                session.History.Clear();
                AnimationState animation = project.Animations[0];

                // Divergent starting names: undo must restore each exactly.
                animation.Frames[0].Layers[0].Name = "Base-A";
                animation.Frames[1].Layers[0].Name = "Base-B";
                animation.Frames[2].Layers[0].Name = "Base-C";

                // AddLayerToAllFrames: one command, one distinct instance per frame.
                session.AddLayerToAllFrames("Shade");
                Require(session.Current.History.UndoCount == 1);
                foreach (Frame frame in animation.Frames)
                {
                    Require(frame.Layers.Count == 2);
                    Require(frame.Layers[1].Name == "Shade");
                    Require(frame.Layers[1].Width == project.CanvasWidth);
                }
                Require(!ReferenceEquals(animation.Frames[0].Layers[1], animation.Frames[1].Layers[1]));
                Layer addedToFrame1 = animation.Frames[1].Layers[1];
                Require(session.Undo());
                foreach (Frame frame in animation.Frames) Require(frame.Layers.Count == 1);
                Require(session.Redo());
                Require(ReferenceEquals(animation.Frames[1].Layers[1], addedToFrame1));

                // RenameLayerInAllFrames: undo restores the divergent originals.
                session.RenameLayerInAllFrames(0, "Base");
                foreach (Frame frame in animation.Frames) Require(frame.Layers[0].Name == "Base");
                Require(session.Undo());
                Require(animation.Frames[0].Layers[0].Name == "Base-A");
                Require(animation.Frames[1].Layers[0].Name == "Base-B");
                Require(animation.Frames[2].Layers[0].Name == "Base-C");
                Require(session.Redo());
                RequireThrows<ArgumentException>(() => session.RenameLayerInAllFrames(0, "  "));
                int beforeNoOp = session.Current.History.UndoCount;
                session.RenameLayerInAllFrames(0, "Base"); // no-op: all equal already
                Require(session.Current.History.UndoCount == beforeNoOp);

                // SetLayerVisibilityInAllFrames: mixed before-values round-trip.
                animation.Frames[1].Layers[1].IsVisible = false;
                session.SetLayerVisibilityInAllFrames(1, true);
                foreach (Frame frame in animation.Frames) Require(frame.Layers[1].IsVisible);
                Require(session.Undo());
                Require(animation.Frames[0].Layers[1].IsVisible);
                Require(!animation.Frames[1].Layers[1].IsVisible);
                Require(animation.Frames[2].Layers[1].IsVisible);
                Require(session.Redo());
                foreach (Frame frame in animation.Frames) Require(frame.Layers[1].IsVisible);

                // RemoveLayerFromAllFrames: undo reinserts the SAME instances.
                Layer removedFromFrame2 = animation.Frames[2].Layers[1];
                session.RemoveLayerFromAllFrames(1);
                foreach (Frame frame in animation.Frames) Require(frame.Layers.Count == 1);
                Require(session.Undo());
                Require(ReferenceEquals(animation.Frames[2].Layers[1], removedFromFrame2));
                Require(session.Redo());

                // Atomic validation: an index valid in SOME frames only mutates
                // nothing and records nothing.
                animation.Frames[0].Layers.Add(new Layer("Lonely", project.CanvasWidth, project.CanvasHeight));
                int historyBefore = session.Current.History.UndoCount;
                RequireThrows<InvalidOperationException>(() => session.RenameLayerInAllFrames(1, "Nope"));
                RequireThrows<InvalidOperationException>(() => session.SetLayerVisibilityInAllFrames(1, false));
                RequireThrows<InvalidOperationException>(() => session.RemoveLayerFromAllFrames(1));
                Require(session.Current.History.UndoCount == historyBefore);
                Require(animation.Frames[0].Layers[1].Name == "Lonely");
                animation.Frames[0].Layers.RemoveAt(1);

                // Removing the LAST layer of any frame is refused for the whole batch.
                RequireThrows<InvalidOperationException>(() => session.RemoveLayerFromAllFrames(0));

                // Layer-count cap guards the add batch.
                while (animation.Frames[1].Layers.Count < Config.Persistence.MaxLayersPerFrame)
                    animation.Frames[1].Layers.Add(new Layer("Fill", project.CanvasWidth, project.CanvasHeight));
                RequireThrows<InvalidOperationException>(() => session.AddLayerToAllFrames("Overflow"));
                while (animation.Frames[1].Layers.Count > 1)
                    animation.Frames[1].Layers.RemoveAt(animation.Frames[1].Layers.Count - 1);

                // An active gesture blocks every batch op.
                session.ActiveTool = new PencilTool();
                Require(session.PointerDown(0, 0));
                RequireThrows<InvalidOperationException>(() => session.AddLayerToAllFrames("Mid"));
                RequireThrows<InvalidOperationException>(() => session.RenameLayerInAllFrames(0, "Mid"));
                RequireThrows<InvalidOperationException>(() => session.SetLayerVisibilityInAllFrames(0, false));
                RequireThrows<InvalidOperationException>(() => session.RemoveLayerFromAllFrames(0));
                session.PointerUp(0, 0);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Generator presets (bob/breathe + shake/hit-flash) and the
    // layer-preserving regenerate merge.
    // ------------------------------------------------------------------
    private static void TestGeneratorPresetsAndLayerPreservingMerge()
    {
        // Preset facade equivalence: MovingTool output is byte-identical to
        // the generator API, and generated layers carry the designated name.
        var source = new Frame(10, 8);
        var random = new Random(0x0B0B);
        for (int index = 0; index < source.Layers[0].Pixels.Length; index++)
            source.Layers[0].Pixels[index].Rgba = random.Next(3) == 0
                ? 0u
                : Pack((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
        source.Hitboxes["Kick"] = new HitboxData { X = 0.125f, Y = 0.25f, Width = 0.25f, Height = 0.125f };

        var moving = new MovingTool
        {
            ActiveMode = MovingTool.MovementType.BobBreathe,
            BobFrameCount = 5,
            BobAmplitude = 2.5f,
            BreatheAmplitude = 0.2f
        };
        Require(moving.BobAmplitude == 2.5f && moving.BreatheAmplitude == 0.2f && moving.BobFrameCount == 5);
        AnimationState actualBob = moving.GenerateAnimation(source);
        AnimationState expectedBob = new AnimationGeneratorAPI().GenerateBobAnimation(
            Config.Animation.BobAnimName, source, 5, 2.5f, 0.2f);
        Require(actualBob.StateName == Config.Animation.BobAnimName);
        Require(actualBob.FPS == Config.Animation.BobDefaultFPS);
        Require(actualBob.Frames.Count == 5);
        for (int frameIndex = 0; frameIndex < 5; frameIndex++)
        {
            Require(actualBob.Frames[frameIndex].Layers.Count == 1);
            Require(actualBob.Frames[frameIndex].Layers[0].Name == Config.Animation.GeneratedLayerName);
            RequireRgbaEqual(
                CopyRgba(expectedBob.Frames[frameIndex].Layers[0]),
                actualBob.Frames[frameIndex].Layers[0]);
            Require(actualBob.Frames[frameIndex].Hitboxes.Count == source.Hitboxes.Count);
        }

        moving.ActiveMode = MovingTool.MovementType.ShakeHitFlash;
        moving.ShakeFrameCount = 4;
        moving.ShakeAmplitude = 3f;
        moving.FlashStrength = 0.5f;
        AnimationState actualShake = moving.GenerateAnimation(source);
        AnimationState expectedShake = new AnimationGeneratorAPI().GenerateShakeFlashAnimation(
            Config.Animation.ShakeAnimName, source, 4, 3f, 0.5f);
        Require(actualShake.StateName == Config.Animation.ShakeAnimName);
        Require(actualShake.FPS == Config.Animation.ShakeDefaultFPS);
        Require(actualShake.Frames.Count == 4);
        for (int frameIndex = 0; frameIndex < 4; frameIndex++)
        {
            RequireRgbaEqual(
                CopyRgba(expectedShake.Frames[frameIndex].Layers[0]),
                actualShake.Frames[frameIndex].Layers[0]);
        }
        // The impact frame is brighter than (or equal to) the source wherever
        // opaque content exists - the flash is strongest at t=0.
        bool brightened = false;
        PixelColor[] flatSource = source.FlattenLayers();
        Layer impact = actualShake.Frames[0].Layers[0];
        for (int index = 0; index < impact.Pixels.Length; index++)
        {
            if (flatSource[index].A == 0) continue;
            Require(impact.Pixels[index].R >= flatSource[index].R);
            Require(impact.Pixels[index].G >= flatSource[index].G);
            Require(impact.Pixels[index].B >= flatSource[index].B);
            Require(impact.Pixels[index].A == flatSource[index].A);
            if (impact.Pixels[index].R > flatSource[index].R) brightened = true;
        }
        Require(brightened);

        // Preset guard rails surface through the facade.
        moving.ActiveMode = MovingTool.MovementType.BobBreathe;
        moving.BobFrameCount = 0;
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GenerateAnimation(source));
        moving.BobFrameCount = 4;
        moving.BreatheAmplitude = 0.95f; // over MaxBreatheAmplitude
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GenerateAnimation(source));
        moving.BreatheAmplitude = Config.Tool.Moving.DefaultBreatheAmp;
        moving.ActiveMode = MovingTool.MovementType.ShakeHitFlash;
        moving.FlashStrength = 1.5f;
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GenerateAnimation(source));
        moving.FlashStrength = Config.Tool.Moving.DefaultFlashStrength;

        // MergeOntoTargetLayer contracts.
        RequireThrows<ArgumentNullException>(
            () => AnimationGeneratorAPI.MergeOntoTargetLayer(null, actualBob, "Generated"));
        RequireThrows<ArgumentNullException>(
            () => AnimationGeneratorAPI.MergeOntoTargetLayer(actualBob, null, "Generated"));
        RequireThrows<ArgumentException>(
            () => AnimationGeneratorAPI.MergeOntoTargetLayer(actualBob, actualBob, " "));

        // Session regenerate is layer-preserving end to end.
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("MergeGen", project, root))
            {
                session.AutosaveEnabled = false;
                Require(session.SelectFrame(0, 0));
                var walker = new MovingTool { WalkFrameCount = 4 };
                // Give the base frame content so generation is non-trivial.
                project.Animations[0].Frames[0].Layers[0].SetPixel(3, 3, Color(200, 40, 30, 255));

                AnimationState first = session.GenerateAnimation(walker);
                Require(project.Animations.Count == 2);
                Require(first.Frames.Count == 4);
                Require(first.Frames[0].Layers[0].Name == Config.Animation.GeneratedLayerName);

                // Manual touch-ups on the generated animation: an extra layer
                // with painted pixels, a custom hitbox, a duration override,
                // playback tags, and a tweak of the generated layer's opacity.
                var touchUp = new Layer("TouchUp", project.CanvasWidth, project.CanvasHeight);
                touchUp.SetPixel(1, 1, Color(9, 9, 9, 255));
                first.Frames[1].Layers.Add(touchUp);
                first.Frames[1].DurationMs = 175;
                first.Frames[1].Hitboxes["Manual"] = new HitboxData { X = 0.5f, Y = 0.5f, Width = 0.25f, Height = 0.25f };
                first.Frames[1].Layers[0].Opacity = 0.5f;
                first.LoopStartFrame = 1;
                first.PingPong = true;

                // Regenerate from the SAME base frame (generation moves the
                // selection onto the generated animation, so re-select first).
                Require(session.SelectFrame(0, 0));
                session.History.Clear();
                AnimationState merged = session.GenerateAnimation(walker);
                Require(ReferenceEquals(project.Animations[1], merged));
                Require(!ReferenceEquals(merged, first));
                Require(session.Current.History.UndoCount == 1); // ONE undoable command

                // Touch-ups survive: extra layer (cloned, pixels intact),
                // hitboxes, duration, playback tags, generated-layer opacity.
                Frame mergedFrame = merged.Frames[1];
                Require(mergedFrame.Layers.Count == 2);
                Require(mergedFrame.Layers[0].Name == Config.Animation.GeneratedLayerName);
                Require(mergedFrame.Layers[0].Opacity == 0.5f);
                Require(mergedFrame.Layers[1].Name == "TouchUp");
                Require(!ReferenceEquals(mergedFrame.Layers[1], touchUp));
                Require(mergedFrame.Layers[1].GetPixel(1, 1).Rgba == Pack(9, 9, 9, 255));
                Require(mergedFrame.DurationMs == 175);
                Require(mergedFrame.Hitboxes.ContainsKey("Manual"));
                Require(merged.LoopStartFrame == 1);
                Require(merged.PingPong);

                // The generated layer's PIXELS regenerated (same tool state =>
                // identical to a fresh generation's frame 1 content).
                AnimationState freshReference = new MovingTool
                {
                    WalkFrameCount = 4,
                    WalkSplitY = walker.WalkSplitY,
                    WalkBounceAmp = walker.WalkBounceAmp,
                    WalkStrideAmp = walker.WalkStrideAmp
                }.GenerateAnimation(project.Animations[0].Frames[0]);
                RequireRgbaEqual(CopyRgba(freshReference.Frames[1].Layers[0]), mergedFrame.Layers[0]);

                // Undo swaps the ORIGINAL animation object back untouched.
                Require(session.Undo());
                Require(ReferenceEquals(project.Animations[1], first));
                Require(ReferenceEquals(first.Frames[1].Layers[1], touchUp));
                Require(session.Redo());
                Require(ReferenceEquals(project.Animations[1], merged));

                // A frame whose target layer was renamed away keeps ALL its
                // layers and gains the regenerated content at the bottom.
                merged.Frames[2].Layers[0].Name = "Repurposed";
                Require(session.SelectFrame(0, 0));
                AnimationState reinserted = session.GenerateAnimation(walker);
                Require(reinserted.Frames[2].Layers.Count == 2);
                Require(reinserted.Frames[2].Layers[0].Name == Config.Animation.GeneratedLayerName);
                Require(reinserted.Frames[2].Layers[1].Name == "Repurposed");

                // Regenerating with a larger frame count appends generated
                // frames; with a smaller one the extras are dropped.
                walker.WalkFrameCount = 6;
                Require(session.SelectFrame(0, 0));
                AnimationState grown = session.GenerateAnimation(walker);
                Require(grown.Frames.Count == 6);
                Require(grown.Frames[5].Layers.Count == 1);
                Require(grown.Frames[5].Layers[0].Name == Config.Animation.GeneratedLayerName);
                walker.WalkFrameCount = 2;
                Require(session.SelectFrame(0, 0));
                AnimationState shrunk = session.GenerateAnimation(walker);
                Require(shrunk.Frames.Count == 2);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Export: the atlas animations carry per-frame durations and playback
    // tags, defaults are omitted, stale loop ranges export clamped.
    // ------------------------------------------------------------------
    private static void TestExportAtlasTimingMetadataFlow()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var engine = new ExportEngine(new ProjectValidator(new AssetSchemaService()));

            // Feature-free project: none of the optional members appear.
            EFYVProject plain = CreateValidProject(root, 2);
            ExportResult plainResult = engine.Export(plain, CancellationToken.None);
            EFYVJsonFormat plainDocument = FastImporter.ParseEfyvFile(plainResult.MetadataPath);
            AnimationMetadataJson plainAnimation = plainDocument.atlas.Value.animations[0];
            Require(plainAnimation.frameDurationsMs == null);
            Require(!plainAnimation.loopStart.HasValue);
            Require(!plainAnimation.loopEnd.HasValue);
            Require(!plainAnimation.pingPong.HasValue);
            Require(plainDocument.EffectiveDocumentVersion == BackendConfig.Exporter.CurrentDocumentVersion);

            // Timed project: overrides + tags flow into the atlas block with
            // the raw 0 sentinel preserved and fps kept as the fallback.
            EFYVProject timed = CreateValidProject(root, 3);
            timed.AssetProperties[SharedConfig.EntityNameField] = "TimedEnemy";
            timed.Animations[0].Frames[0].DurationMs = 120;
            timed.Animations[0].Frames[2].DurationMs = 45;
            timed.Animations[0].LoopStartFrame = 1;
            timed.Animations[0].LoopEndFrame = 1;
            timed.Animations[0].PingPong = true;
            ExportResult timedResult = engine.Export(timed, CancellationToken.None);
            EFYVJsonFormat timedDocument = FastImporter.ParseEfyvFile(timedResult.MetadataPath);
            AnimationMetadataJson timedAnimation = timedDocument.atlas.Value.animations[0];
            Require(timedAnimation.fps == timed.Animations[0].FPS);
            Require(timedAnimation.frameDurationsMs.Count == 3);
            Require(timedAnimation.frameDurationsMs[0] == 120);
            Require(timedAnimation.frameDurationsMs[1] == Config.Animation.InheritFrameDurationMs);
            Require(timedAnimation.frameDurationsMs[2] == 45);
            Require(timedAnimation.loopStart.Value == 1);
            Require(timedAnimation.loopEnd.Value == 1);
            Require(timedAnimation.pingPong.Value);
            // What the exporter writes always passes the shared validator.
            Require(EFYVBackend.Core.Export.FastExporter.TryValidateAtlasMetadata(
                timedDocument.atlas.Value, out EFYVBackend.Core.Export.AtlasMetadataError timedError));
            Require(timedError == EFYVBackend.Core.Export.AtlasMetadataError.None);

            // Stale tags export CLAMPED; a loop end that clamps onto the last
            // frame is a default again and is omitted.
            EFYVProject stale = CreateValidProject(root, 3);
            stale.AssetProperties[SharedConfig.EntityNameField] = "StaleEnemy";
            stale.Animations[0].LoopStartFrame = 7;
            stale.Animations[0].LoopEndFrame = 9;
            ExportResult staleResult = engine.Export(stale, CancellationToken.None);
            AnimationMetadataJson staleAnimation =
                FastImporter.ParseEfyvFile(staleResult.MetadataPath).atlas.Value.animations[0];
            Require(staleAnimation.loopStart.Value == 2);
            Require(!staleAnimation.loopEnd.HasValue); // clamped to last frame = default
            Require(!staleAnimation.pingPong.HasValue);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Onion skinning: ComposeOnionSkin reference compositing, edge clamping,
    // settings validation, and the screen-buffer overload.
    // ------------------------------------------------------------------
    private static unsafe void TestViewportOnionSkinComposite()
    {
        // Settings validation.
        var settings = new OnionSkinSettings();
        Require(settings.PreviousFrameCount == Config.OnionSkin.DefaultPreviousFrames);
        Require(settings.NextFrameCount == Config.OnionSkin.DefaultNextFrames);
        RequireThrows<ArgumentOutOfRangeException>(() => settings.PreviousFrameCount = -1);
        RequireThrows<ArgumentOutOfRangeException>(
            () => settings.PreviousFrameCount = Config.OnionSkin.MaxNeighborFrames + 1);
        RequireThrows<ArgumentOutOfRangeException>(() => settings.NextFrameCount = -1);
        RequireThrows<ArgumentOutOfRangeException>(() => settings.PreviousAlpha = -0.01f);
        RequireThrows<ArgumentOutOfRangeException>(() => settings.NextAlpha = 1.01f);
        RequireThrows<ArgumentOutOfRangeException>(() => settings.AlphaFalloff = float.NaN);

        // Distinct opaque markers per frame at separate pixels so every ghost
        // contribution is attributable.
        const int width = 4;
        const int height = 2;
        var frames = new List<Frame>();
        for (int index = 0; index < 4; index++)
        {
            var frame = new Frame(width, height, index);
            frame.Layers[0].SetPixel(index, 0, Color((byte)(40 * (index + 1)), 10, 20, 255));
            frame.Layers[0].SetPixel(index, 1, Color(5, (byte)(30 * (index + 1)), 40, 128));
            frames.Add(frame);
        }

        settings.PreviousFrameCount = 2;
        settings.NextFrameCount = 1;
        settings.PreviousAlpha = 0.5f;
        settings.NextAlpha = 0.25f;
        settings.AlphaFalloff = 0.5f;

        var viewport = new ViewportController();
        var actual = new PixelColor[width * height];
        viewport.ComposeOnionSkin(frames, 2, settings, actual);

        // Reference composite: ghosts farthest-to-nearest per side (previous
        // side first), then the current frame at full strength, all through
        // the same BlendLayer primitive.
        PixelColor[] expected = new PixelColor[width * height];
        void BlendInto(PixelColor[] destination, Frame frame, float alpha)
        {
            PixelColor[] flattened = frame.FlattenLayers();
            fixed (PixelColor* destinationBase = destination)
            fixed (PixelColor* sourceBase = flattened)
            {
                EFYVBackend.Core.Memory.FastMemory.BlendLayer(
                    (uint*)destinationBase,
                    (uint*)sourceBase,
                    destination.Length,
                    Config.Layer.TransparentAlpha,
                    alpha);
            }
        }
        BlendInto(expected, frames[0], 0.5f * 0.5f); // distance 2 -> falloff applied once
        BlendInto(expected, frames[1], 0.5f);
        BlendInto(expected, frames[3], 0.25f);
        BlendInto(expected, frames[2], Config.Common.UnitScale);
        for (int index = 0; index < expected.Length; index++)
            Require(expected[index].Rgba == actual[index].Rgba);
        // Sanity: ghosts really contribute (the frame-0 marker pixel is not
        // empty and not the full-strength source value).
        int ghostPixel = 0 * width + 0;
        Require(actual[ghostPixel].Rgba != 0u);
        Require(actual[ghostPixel].Rgba != frames[0].Layers[0].GetPixel(0, 0).Rgba);

        // Edge clamping, no wrap-around: at frame 0 no previous ghosts exist.
        var atStart = new PixelColor[width * height];
        viewport.ComposeOnionSkin(frames, 0, settings, atStart);
        PixelColor[] expectedStart = new PixelColor[width * height];
        BlendInto(expectedStart, frames[1], 0.25f);
        BlendInto(expectedStart, frames[0], Config.Common.UnitScale);
        for (int index = 0; index < expectedStart.Length; index++)
            Require(expectedStart[index].Rgba == atStart[index].Rgba);
        // The last frame's marker must NOT appear (no wrap).
        Require(atStart[3].Rgba == 0u);

        // Zero-alpha sides contribute nothing.
        settings.PreviousAlpha = 0f;
        var noPrevious = new PixelColor[width * height];
        viewport.ComposeOnionSkin(frames, 2, settings, noPrevious);
        PixelColor[] expectedNoPrevious = new PixelColor[width * height];
        BlendInto(expectedNoPrevious, frames[3], 0.25f);
        BlendInto(expectedNoPrevious, frames[2], Config.Common.UnitScale);
        for (int index = 0; index < expectedNoPrevious.Length; index++)
            Require(expectedNoPrevious[index].Rgba == noPrevious[index].Rgba);
        settings.PreviousAlpha = 0.5f;

        // Guards: nulls, bad index, wrong destination size, dimension mismatch.
        RequireThrows<ArgumentNullException>(
            () => viewport.ComposeOnionSkin(null, 0, settings, actual));
        RequireThrows<ArgumentNullException>(
            () => viewport.ComposeOnionSkin(frames, 0, null, actual));
        RequireThrows<ArgumentNullException>(
            () => viewport.ComposeOnionSkin(frames, 0, settings, null));
        RequireThrows<ArgumentOutOfRangeException>(
            () => viewport.ComposeOnionSkin(frames, -1, settings, actual));
        RequireThrows<ArgumentOutOfRangeException>(
            () => viewport.ComposeOnionSkin(frames, frames.Count, settings, actual));
        RequireThrows<ArgumentException>(
            () => viewport.ComposeOnionSkin(frames, 0, settings, new PixelColor[3]));
        var mismatched = new List<Frame>(frames) { [1] = new Frame(width + 1, height, 1) };
        RequireThrows<InvalidOperationException>(
            () => viewport.ComposeOnionSkin(mismatched, 2, settings, actual));

        // Screen-buffer overload: identical to composing then scale-blitting
        // through the same viewport transform.
        const int screenWidth = 9;
        const int screenHeight = 5;
        var expectedScreen = new uint[screenWidth * screenHeight];
        var composed = new PixelColor[width * height];
        viewport.ComposeOnionSkin(frames, 2, settings, composed);
        fixed (PixelColor* composedBase = composed)
        fixed (uint* screenBase = expectedScreen)
        {
            EFYVBackend.Core.Memory.FastMemory.ScaleBlitNearestNeighbor(
                (uint*)composedBase, width, height,
                screenBase, screenWidth, screenHeight,
                viewport.ZoomLevel, viewport.OffsetX, viewport.OffsetY);
        }
        var actualScreen = new uint[screenWidth * screenHeight];
        viewport.RenderToScreenBuffer(frames, 2, settings, null, actualScreen, screenWidth, screenHeight);
        for (int index = 0; index < expectedScreen.Length; index++)
            Require(expectedScreen[index] == actualScreen[index]);

        RequireThrows<ArgumentException>(() => viewport.RenderToScreenBuffer(
            frames, 2, settings, null, new uint[4], screenWidth, screenHeight));
        RequireThrows<ArgumentOutOfRangeException>(() => viewport.RenderToScreenBuffer(
            frames, 2, settings, null, actualScreen, 0, screenHeight));
    }
}
