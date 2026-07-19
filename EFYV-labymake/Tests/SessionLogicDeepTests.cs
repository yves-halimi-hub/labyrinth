using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    private static void TestSessionLogicSessionConstructionAndSelection()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            RequireThrows<ArgumentException>(() => DesignerSession.Create(null, project, root));
            RequireThrows<ArgumentException>(() => DesignerSession.Create(" \t", project, root));
            RequireThrows<ArgumentException>(() => DesignerSession.Create("../escape", project, root));
            RequireThrows<ArgumentException>(() => DesignerSession.Create("CON", project, root));
            RequireThrows<ArgumentNullException>(() => DesignerSession.Create("Ok", null, root));

            var schema = new AssetSchemaService();
            var toolbar = new ToolbarAPI(schema);
            var validator = new ProjectValidator(schema);
            var persistence = new ProjectPersistenceService(root, schema);
            var autosave = new AutosaveController(persistence);
            var liveDebug = new LiveDebugController(new ExportEngine(validator), validator);
            var preview = new PreviewController();
            var history = new CommandManager();
            var clock = new TaskDebounceScheduler();
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", null, toolbar, validator, persistence, autosave, liveDebug, preview, history, clock));
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", project, null, validator, persistence, autosave, liveDebug, preview, history, clock));
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", project, toolbar, null, persistence, autosave, liveDebug, preview, history, clock));
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", project, toolbar, validator, null, autosave, liveDebug, preview, history, clock));
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", project, toolbar, validator, persistence, null, liveDebug, preview, history, clock));
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", project, toolbar, validator, persistence, autosave, null, preview, history, clock));
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", project, toolbar, validator, persistence, autosave, liveDebug, null, history, clock));
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", project, toolbar, validator, persistence, autosave, liveDebug, preview, null, clock));
            RequireThrows<ArgumentNullException>(() => new DesignerSession(
                "Ctor", project, toolbar, validator, persistence, autosave, liveDebug, preview, history, null));

            // Structural guards on a project with no animations at all.
            EFYVProject bare = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            bare.Animations.Clear();
            using (DesignerSession bareSession = DesignerSession.Create("Bare", bare, root))
            {
                bareSession.AutosaveEnabled = false;
                Require(bareSession.Current.AnimationIndex == Config.Common.NotFoundIndex);
                Require(bareSession.Current.FrameIndex == Config.Common.NotFoundIndex);
                Require(bareSession.CurrentFrame == null);
                bareSession.ActiveTool = new PencilTool();
                Require(!bareSession.PointerDown(1, 1));
                RequireThrows<InvalidOperationException>(() => bareSession.AddFrame());
                RequireThrows<InvalidOperationException>(() => bareSession.AddLayer("X"));
                RequireThrows<InvalidOperationException>(() => bareSession.DuplicateLayer(0));
                RequireThrows<InvalidOperationException>(() => bareSession.RemoveLayer(0));
                RequireThrows<InvalidOperationException>(() => bareSession.MoveLayer(0, 0));
                RequireThrows<InvalidOperationException>(() => bareSession.RenameLayer(0, "Y"));
                RequireThrows<InvalidOperationException>(() => bareSession.SetLayerVisibility(0, false));
                RequireThrows<InvalidOperationException>(() => bareSession.SetLayerOpacity(0, 0.5f));
                RequireThrows<ArgumentOutOfRangeException>(() => bareSession.DuplicateFrame(0));
                RequireThrows<ArgumentOutOfRangeException>(() => bareSession.RemoveFrame(0));
                RequireThrows<ArgumentOutOfRangeException>(() => bareSession.MoveFrame(0, 0));
                RequireThrows<ArgumentOutOfRangeException>(() => bareSession.RemoveAnimation(0));
                RequireThrows<ArgumentOutOfRangeException>(() => bareSession.DuplicateAnimation(0));
                RequireThrows<ArgumentOutOfRangeException>(() => bareSession.MoveAnimation(0, 0));
                RequireThrows<ArgumentOutOfRangeException>(() => bareSession.RenameAnimation(0, "Z"));
                RequireThrows<ArgumentOutOfRangeException>(() => bareSession.SetAnimationFps(0, 5));
                RequireThrows<ArgumentNullException>(() => bareSession.AddAnimation(null));
                RequireThrows<ArgumentNullException>(() => bareSession.GenerateAnimation(null));
                RequireThrows<InvalidOperationException>(() => bareSession.GenerateAnimation(new MovingTool()));
                Require(!bareSession.Current.IsDirty && bareSession.Current.ChangeVersion == 0);
            }

            // Selection is locked while a gesture is active; disposed sessions reject everything.
            EFYVProject locked = CreateValidProject(root, 1);
            DesignerSession lockedSession = DesignerSession.Create("Locked", locked, root);
            try
            {
                lockedSession.AutosaveEnabled = false;
                RequireThrows<ArgumentOutOfRangeException>(() => lockedSession.RemoveLayer(-1));
                RequireThrows<ArgumentOutOfRangeException>(() => lockedSession.MoveLayer(0, 1));
                lockedSession.ActiveTool = new PencilTool { CurrentColor = Color(1, 2, 3, 255) };
                Require(lockedSession.PointerDown(1, 1));
                Require(!lockedSession.PointerDown(2, 2));
                Require(!lockedSession.SelectFrame(0, 0));
                Require(lockedSession.PointerDrag(2, 2));
                Require(lockedSession.PointerUp(3, 3));
                Require(lockedSession.SelectFrame(0, 0));
            }
            finally
            {
                lockedSession.Dispose();
                lockedSession.Dispose();
            }
            RequireThrows<ObjectDisposedException>(() => lockedSession.PointerDown(1, 1));
            RequireThrows<ObjectDisposedException>(() => lockedSession.PointerDrag(1, 1));
            RequireThrows<ObjectDisposedException>(() => lockedSession.PointerUp(1, 1));
            RequireThrows<ObjectDisposedException>(() => lockedSession.CancelGesture());
            RequireThrows<ObjectDisposedException>(() => lockedSession.AddFrame());
            RequireThrows<ObjectDisposedException>(() => lockedSession.SetProperty("x", 1));
            RequireThrows<ObjectDisposedException>(() => lockedSession.AddAnimation(new AnimationState("X", 1)));
            RequireThrows<ObjectDisposedException>(() => lockedSession.LoadPreview(0));

            // SelectFirstFrame skips animations with no frames and null entries.
            EFYVProject sparse = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            sparse.Animations.Insert(0, new AnimationState("SessionLogicEmpty", 4));
            sparse.Animations.Insert(1, null);
            using (var sparseSession = new DesignerSession(
                "Sparse", sparse, toolbar, validator, persistence, autosave, liveDebug, preview, history, clock))
            {
                sparseSession.AutosaveEnabled = false;
                Require(sparseSession.Current.AnimationIndex == 2 && sparseSession.Current.FrameIndex == 0);
                Require(ReferenceEquals(sparseSession.CurrentFrame, sparse.Animations[2].Frames[0]));
                Require(!sparseSession.SelectFrame(0, 0));
                // FIXED (#20.2): a null animation entry makes SelectFrame return false,
                // matching SelectFirstFrame, instead of throwing NullReferenceException.
                Require(!sparseSession.SelectFrame(1, 0));
                Require(!sparseSession.SelectFrame(3, 0));
                Require(!sparseSession.SelectFrame(2, 1));
                Require(sparseSession.SelectFrame(2, 0));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestSessionLogicSessionNoOpAndIndexTracking()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 2);
            using (DesignerSession session = DesignerSession.Create("NoOp", project, root))
            {
                session.AutosaveEnabled = false;
                long expectedVersion = 0;
                Require(!session.Current.IsDirty && session.Current.ChangeVersion == 0);
                Require(!session.Undo() && !session.Redo());
                Require(session.Current.ChangeVersion == 0);

                AnimationState animation = project.Animations[0];
                Frame frame = session.CurrentFrame;
                Layer layer = frame.Layers[0];

                // No-op edits record no history and do not dirty the session.
                session.RenameAnimation(0, animation.StateName);
                session.SetAnimationFps(0, animation.FPS);
                session.MoveAnimation(0, 0);
                session.MoveFrame(0, 0);
                session.MoveFrame(1, 1);
                session.MoveLayer(0, 0);
                session.RenameLayer(0, layer.Name);
                session.SetLayerVisibility(0, layer.IsVisible);
                session.SetLayerOpacity(0, layer.Opacity);
                session.SetLayerOpacity(0, 2f);
                session.SetLayerOpacity(0, float.MaxValue);
                Require(session.SetProperty(
                    SharedConfig.BaseSpeedField,
                    project.AssetProperties[SharedConfig.BaseSpeedField]).Succeeded);
                Require(session.SetProperty(SharedConfig.BaseSpeedField, -1f).Status ==
                    PropertyEditStatus.OutOfRange);
                Require(session.SetProperty("session-logic-unknown", 1).Status ==
                    PropertyEditStatus.UnknownField);
                Require(!session.History.CanUndo && !session.History.CanRedo);
                Require(!session.Current.IsDirty && session.Current.ChangeVersion == 0);

                // A gesture that changes nothing records nothing.
                session.ActiveTool = new PencilTool { CurrentColor = Color(9, 9, 9, 255) };
                Require(session.PointerDown(-5, -5));
                Require(!session.PointerUp(-5, -5));
                Require(!session.Current.IsDirty && !session.History.CanUndo);
                Require(session.Current.ChangeVersion == 0);

                // A changing gesture, then repainting identical pixels.
                Require(session.PointerDown(1, 1));
                Require(session.PointerUp(1, 1));
                expectedVersion++;
                Require(session.Current.IsDirty && session.Current.ChangeVersion == expectedVersion);
                Require(session.History.Current.UndoCount == 1);
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(1, 1));
                Require(session.Current.ChangeVersion == expectedVersion);
                Require(session.History.Current.UndoCount == 1);

                // Opacity clamp that actually changes the value records history; undo stays dirty.
                session.SetLayerOpacity(0, -3f);
                expectedVersion++;
                Require(layer.Opacity == 0f && session.Current.ChangeVersion == expectedVersion);
                Require(session.Undo());
                expectedVersion++;
                Require(layer.Opacity == Config.Layer.DefaultOpacity);
                Require(session.Current.IsDirty && session.Current.ChangeVersion == expectedVersion);
                Require(session.Redo());
                expectedVersion++;
                Require(layer.Opacity == 0f);
                Require(session.Undo());
                expectedVersion++;

                // Selection index tracking through animation CRUD.
                var emptyAdd = new AnimationState("SessionLogicEmptyAdd", 6);
                session.AddAnimation(emptyAdd);
                expectedVersion++;
                Require(session.Current.AnimationIndex == 1);
                Require(session.Current.FrameIndex == Config.Common.NotFoundIndex);
                Require(session.CurrentFrame == null);
                Require(session.Current.ChangeVersion == expectedVersion);

                var filledAdd = new AnimationState("SessionLogicFilledAdd", 6);
                filledAdd.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight));
                session.AddAnimation(filledAdd);
                expectedVersion++;
                Require(session.Current.AnimationIndex == 2 && session.Current.FrameIndex == 0);
                Require(ReferenceEquals(session.CurrentFrame, filledAdd.Frames[0]));

                AnimationState duplicate = session.DuplicateAnimation(0);
                expectedVersion++;
                Require(session.Current.AnimationIndex == 1 && session.Current.FrameIndex == 0);
                Require(ReferenceEquals(project.Animations[1], duplicate));

                session.RemoveAnimation(1);
                expectedVersion++;
                Require(session.Current.AnimationIndex == 0 && session.Current.FrameIndex == 0);

                // Frame index tracking, including normalization of FrameIndex values.
                Require(session.SelectFrame(0, 1));
                Frame added = session.AddFrame();
                expectedVersion++;
                Require(session.Current.FrameIndex == 2 && added.FrameIndex == 2);
                Frame duplicatedFrame = session.DuplicateFrame(0);
                expectedVersion++;
                Require(session.Current.FrameIndex == 1);
                Require(ReferenceEquals(project.Animations[0].Frames[1], duplicatedFrame));
                Require(duplicatedFrame.FrameIndex == 1);
                Require(project.Animations[0].Frames[3].FrameIndex == 3);
                session.RemoveFrame(3);
                expectedVersion++;
                Require(session.Current.FrameIndex == 2);
                session.RemoveFrame(0);
                expectedVersion++;
                Require(session.Current.FrameIndex == 0);
                session.MoveFrame(0, 1);
                expectedVersion++;
                Require(session.Current.FrameIndex == 1);
                Require(project.Animations[0].Frames[0].FrameIndex == 0);
                Require(project.Animations[0].Frames[1].FrameIndex == 1);
                session.RemoveFrame(1);
                expectedVersion++;
                session.RemoveFrame(0);
                expectedVersion++;
                Require(session.Current.FrameIndex == Config.Common.NotFoundIndex);
                Require(session.CurrentFrame == null);
                Frame revived = session.AddFrame();
                expectedVersion++;
                Require(session.Current.FrameIndex == 0);
                Require(ReferenceEquals(session.CurrentFrame, revived));
                Require(session.Current.ChangeVersion == expectedVersion && session.Current.IsDirty);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestSessionLogicHitboxGestureAndGenerateReplace()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("HitboxGesture", project, root))
            {
                session.AutosaveEnabled = false;
                Frame frame = session.CurrentFrame;
                var hitboxTool = new HitboxTool { ActiveHitboxKey = "Strike" };
                session.ActiveTool = hitboxTool;

                Dictionary<string, HitboxData> before = SessionLogicCopyHitboxes(frame);
                Require(session.PointerDown(8, 4));
                Require(session.PointerDrag(24, 20));
                Require(session.PointerUp(32, 28));
                Require(session.History.Current.UndoCount == 1);
                Require(session.History.Current.UndoBytes ==
                    Config.Command.EstimatedCommandOverheadBytes +
                    Config.Command.EstimatedCommandOverheadBytes + "Strike".Length * sizeof(char));
                HitboxData strike = frame.Hitboxes["Strike"];
                Require(strike.X == 0.5f && strike.Y == 0.25f);
                Require(strike.Width == 1.5f && strike.Height == 1.5f);

                Require(session.Undo());
                SessionLogicRequireHitboxesEqual(before, frame);
                Require(session.Redo());
                strike = frame.Hitboxes["Strike"];
                Require(strike.X == 0.5f && strike.Y == 0.25f);
                Require(strike.Width == 1.5f && strike.Height == 1.5f);

                // Overwriting an existing hitbox restores the previous rectangle on undo.
                Dictionary<string, HitboxData> beforeSecond = SessionLogicCopyHitboxes(frame);
                Require(session.PointerDown(0, 0));
                Require(session.PointerUp(16, 16));
                strike = frame.Hitboxes["Strike"];
                Require(strike.X == 0f && strike.Y == 0f && strike.Width == 1f && strike.Height == 1f);
                Require(session.Undo());
                SessionLogicRequireHitboxesEqual(beforeSecond, frame);
                Require(session.Redo());

                // CancelGesture rolls back a mid-gesture hitbox mutation exactly.
                hitboxTool.ActiveHitboxKey = Config.Hitbox.DefaultKeyHurtbox;
                Dictionary<string, HitboxData> beforeHurt = SessionLogicCopyHitboxes(frame);
                Require(session.PointerDown(0, 0));
                Require(session.PointerDrag(8, 8));
                session.CancelGesture();
                SessionLogicRequireHitboxesEqual(beforeHurt, frame);
                Require(session.PointerDown(0, 0));
                Require(session.PointerUp(8, 8));
                HitboxData hurt = frame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
                Require(hurt.X == 0f && hurt.Y == 0f && hurt.Width == 0.5f && hurt.Height == 0.5f);
                Require(session.Undo());
                SessionLogicRequireHitboxesEqual(beforeHurt, frame);

                // GenerateAnimation twice with the same tool exercises the replace-in-place path.
                Require(session.SelectFrame(0, 0));
                session.History.Clear();
                var moving = new MovingTool { WalkFrameCount = 2 };
                AnimationState firstGenerated = session.GenerateAnimation(moving);
                Require(project.Animations.Count == 2);
                Require(ReferenceEquals(project.Animations[1], firstGenerated));
                Require(firstGenerated.StateName == Config.Animation.WalkAnimName);
                Require(session.Current.AnimationIndex == 1 && session.Current.FrameIndex == 0);
                AnimationState secondGenerated = session.GenerateAnimation(moving);
                Require(project.Animations.Count == 2);
                Require(ReferenceEquals(project.Animations[1], secondGenerated));
                Require(!ReferenceEquals(firstGenerated, secondGenerated));
                Require(session.Current.AnimationIndex == 1);
                Require(session.Undo());
                Require(ReferenceEquals(project.Animations[1], firstGenerated));
                Require(session.Redo());
                Require(ReferenceEquals(project.Animations[1], secondGenerated));

                // Setting a property that had no previous value removes it again on undo.
                project.AssetProperties.Remove(SharedConfig.BaseSpeedField);
                Require(session.SetProperty(SharedConfig.BaseSpeedField, 5f).Succeeded);
                Require((float)project.AssetProperties[SharedConfig.BaseSpeedField] == 5f);
                Require(session.Undo());
                Require(!project.AssetProperties.ContainsKey(SharedConfig.BaseSpeedField));
                Require(session.Redo());
                Require((float)project.AssetProperties[SharedConfig.BaseSpeedField] == 5f);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestSessionLogicGestureRandomizedDiffModel()
    {
        var random = new Random(0x5E5510);
        EFYVProject project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 16, 16);
        Frame frame = project.Animations[0].Frames[0];
        var ink = new Layer("SessionLogicInk", 16, 16);
        frame.Layers.Add(ink);
        int pixelCount = 16 * 16;
        for (int index = 0; index < pixelCount; index++)
        {
            frame.Layers[0].Pixels[index].Rgba = NextUInt(random);
            if (random.Next(3) == 0) ink.Pixels[index].Rgba = NextUInt(random);
        }

        string root = NewTemporaryDirectory();
        try
        {
            using (DesignerSession session = DesignerSession.Create("DiffModel", project, root))
            {
                session.AutosaveEnabled = false;
                var tool = new SessionLogicScribbleTool();
                session.ActiveTool = tool;
                var records = new List<SessionLogicGestureRecord>();
                long expectedBytes = 0;
                var model = new uint[2][];
                model[0] = CopyRgba(frame.Layers[0]);
                model[1] = CopyRgba(frame.Layers[1]);

                for (int gesture = 0; gesture < 50; gesture++)
                {
                    int layerIndex = random.Next(2);
                    Layer layer = frame.Layers[layerIndex];
                    tool.Reset(layerIndex);
                    var expectedAfter = (uint[])model[layerIndex].Clone();
                    int writes = 1 + random.Next(30);
                    for (int write = 0; write < writes; write++)
                    {
                        int index = random.Next(pixelCount);
                        uint value = random.Next(4) == 0 ? 0u : NextUInt(random);
                        tool.Indices.Add(index);
                        tool.Values.Add(value);
                        expectedAfter[index] = value;
                    }

                    uint[] beforePixels = model[layerIndex];
                    Require(session.PointerDown(0, 0));
                    Require(session.PointerDrag(0, 0));
                    bool changed = session.PointerUp(0, 0);
                    int diffCount = 0;
                    for (int index = 0; index < pixelCount; index++)
                    {
                        if (beforePixels[index] != expectedAfter[index]) diffCount++;
                    }
                    Require(changed == (diffCount > 0));
                    RequireRgbaEqual(expectedAfter, layer);
                    RequireRgbaEqual(model[1 - layerIndex], frame.Layers[1 - layerIndex]);
                    model[layerIndex] = expectedAfter;
                    if (diffCount > 0)
                    {
                        // Byte-exact history accounting for the pixel diff command.
                        expectedBytes += Config.Command.EstimatedCommandOverheadBytes +
                            (long)diffCount * (sizeof(int) + sizeof(uint) + sizeof(uint)) +
                            (long)layer.Name.Length * 2 * sizeof(char);
                        records.Add(new SessionLogicGestureRecord
                        {
                            LayerIndex = layerIndex,
                            Before = beforePixels,
                            After = expectedAfter
                        });
                    }
                    Require(session.History.Current.UndoCount == records.Count);
                    Require(session.History.Current.UndoBytes == expectedBytes);
                }
                Require(records.Count > 10);

                // Full undo walk compares every intermediate state to the model.
                for (int index = records.Count - 1; index >= 0; index--)
                {
                    Require(session.Undo());
                    model[records[index].LayerIndex] = records[index].Before;
                    RequireRgbaEqual(model[0], frame.Layers[0]);
                    RequireRgbaEqual(model[1], frame.Layers[1]);
                }
                Require(!session.Undo());

                // Full redo walk.
                for (int index = 0; index < records.Count; index++)
                {
                    Require(session.Redo());
                    model[records[index].LayerIndex] = records[index].After;
                    RequireRgbaEqual(model[0], frame.Layers[0]);
                    RequireRgbaEqual(model[1], frame.Layers[1]);
                }
                Require(!session.Redo());

                // Random undo/redo walk against the model.
                int pointer = records.Count;
                for (int step = 0; step < 200; step++)
                {
                    if (random.Next(2) == 0)
                    {
                        bool expected = pointer > 0;
                        Require(session.Undo() == expected);
                        if (expected)
                        {
                            pointer--;
                            model[records[pointer].LayerIndex] = records[pointer].Before;
                        }
                    }
                    else
                    {
                        bool expected = pointer < records.Count;
                        Require(session.Redo() == expected);
                        if (expected)
                        {
                            model[records[pointer].LayerIndex] = records[pointer].After;
                            pointer++;
                        }
                    }
                    RequireRgbaEqual(model[0], frame.Layers[0]);
                    RequireRgbaEqual(model[1], frame.Layers[1]);
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestSessionLogicCommandInternalsAndStructuralEdges()
    {
        // DelegateCommand / PropertyEditCommand argument and sizing contracts.
        RequireThrows<ArgumentNullException>(() => new DelegateCommand(null, () => { }, 0));
        RequireThrows<ArgumentNullException>(() => new DelegateCommand(() => { }, null, 0));
        Require(new DelegateCommand(() => { }, () => { }, -5L).EstimatedBytes ==
            Config.Command.EstimatedCommandOverheadBytes);
        Require(new DelegateCommand(() => { }, () => { }, 10L).EstimatedBytes ==
            Config.Command.EstimatedCommandOverheadBytes);
        Require(new DelegateCommand(() => { }, () => { }, 300L).EstimatedBytes == 300L);
        var properties = new Dictionary<string, object>();
        RequireThrows<ArgumentNullException>(() => new PropertyEditCommand(null, "f", false, null, 1));
        RequireThrows<ArgumentNullException>(() => new PropertyEditCommand(properties, null, false, null, 1));

        // FrameEditCapture scoping: what a tool cannot touch is not captured or diffed.
        var frame = new Frame(4, 4);
        var pencil = new PencilTool();
        RequireThrows<ArgumentNullException>(() => FrameEditCapture.Capture(null, pencil));
        RequireThrows<ArgumentNullException>(() => FrameEditCapture.Capture(frame, null));
        RequireThrows<ArgumentNullException>(() => new FrameEditCommand(null, FrameEditCapture.Capture(frame, pencil)));
        RequireThrows<ArgumentNullException>(() => new FrameEditCommand(frame, null));
        pencil.ActiveLayerIndex = -1;
        Require(FrameEditCapture.Capture(frame, pencil).Layers.Count == 0);
        pencil.ActiveLayerIndex = 9;
        Require(FrameEditCapture.Capture(frame, pencil).Layers.Count == 0);
        pencil.ActiveLayerIndex = 0;
        FrameEditCapture pencilCapture = FrameEditCapture.Capture(frame, pencil);
        Require(pencilCapture.Layers.Count == 1 && pencilCapture.Hitboxes == null);
        FrameEditCapture hitboxCapture = FrameEditCapture.Capture(frame, new HitboxTool());
        Require(hitboxCapture.Layers.Count == 0 && hitboxCapture.Hitboxes != null);
        Require(hitboxCapture.Hitboxes.Count == 1);
        Require(hitboxCapture.Hitboxes.ContainsKey(Config.Hitbox.DefaultKeyHurtbox));

        frame.Hitboxes["SessionLogicGhost"] = MatrixHitbox(0f, 0f, 0.1f, 0.1f);
        var pixelScoped = new FrameEditCommand(frame, pencilCapture);
        Require(!pixelScoped.HasChanges);
        Require(pixelScoped.EstimatedBytes == Config.Command.EstimatedCommandOverheadBytes);
        frame.Layers[0].Pixels[5].Rgba = 0xFFFFFFFFu;
        var hitboxScoped = new FrameEditCommand(frame, hitboxCapture);
        Require(hitboxScoped.HasChanges);
        hitboxScoped.Undo();
        Require(!frame.Hitboxes.ContainsKey("SessionLogicGhost"));
        Require(frame.Layers[0].Pixels[5].Rgba == 0xFFFFFFFFu);

        // Metadata-only diff (name, visibility, opacity, frame index) with exact byte estimate.
        var metadataFrame = new Frame(3, 3, 4);
        var metadataPencil = new PencilTool { ActiveLayerIndex = 0 };
        FrameEditCapture metadataCapture = FrameEditCapture.Capture(metadataFrame, metadataPencil);
        Layer metadataLayer = metadataFrame.Layers[0];
        metadataLayer.Name = "SessionLogicRenamed";
        metadataLayer.IsVisible = false;
        metadataLayer.Opacity = 0.25f;
        metadataFrame.FrameIndex = 9;
        var metadataCommand = new FrameEditCommand(metadataFrame, metadataCapture);
        Require(metadataCommand.HasChanges);
        Require(metadataCommand.EstimatedBytes ==
            Config.Command.EstimatedCommandOverheadBytes +
            (Config.Layer.DefaultName.Length + "SessionLogicRenamed".Length) * sizeof(char));
        metadataCommand.Undo();
        Require(metadataFrame.FrameIndex == 4);
        Require(metadataLayer.Name == Config.Layer.DefaultName);
        Require(metadataLayer.IsVisible && metadataLayer.Opacity == Config.Layer.DefaultOpacity);
        metadataCommand.Execute();
        Require(metadataFrame.FrameIndex == 9);
        Require(metadataLayer.Name == "SessionLogicRenamed");
        Require(!metadataLayer.IsVisible && metadataLayer.Opacity == 0.25f);

        // FrameIndex-only diff.
        var indexFrame = new Frame(2, 2, 0);
        FrameEditCapture indexCapture = FrameEditCapture.Capture(indexFrame, new HitboxTool());
        indexFrame.FrameIndex = 3;
        var indexCommand = new FrameEditCommand(indexFrame, indexCapture);
        Require(indexCommand.HasChanges);
        Require(indexCommand.EstimatedBytes == Config.Command.EstimatedCommandOverheadBytes);
        indexCommand.Undo();
        Require(indexFrame.FrameIndex == 0);
        indexCommand.Execute();
        Require(indexFrame.FrameIndex == 3);

        // Structural layer mutation during a gesture.
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            Frame target = project.Animations[0].Frames[0];
            using (DesignerSession session = DesignerSession.Create("Structural", project, root))
            {
                session.AutosaveEnabled = false;
                // FIXED (#20.3): a tool that removes the captured layer during a gesture
                // still makes the diffing FrameEditCommand constructor throw
                // InvalidOperationException, but rollback now restores the captured
                // before-state directly, so exactly ONE exception surfaces and the frame
                // gets its layer membership back with no history entry.
                Layer capturedLayer = target.Layers[0];
                session.ActiveTool = new SessionLogicLayerRemoveTool();
                Require(session.PointerDown(0, 0));
                RequireThrows<InvalidOperationException>(() => session.PointerUp(0, 0));
                Require(target.Layers.Count == 1);
                Require(ReferenceEquals(target.Layers[0], capturedLayer));
                Require(!session.History.CanUndo && !session.Current.IsDirty);

                // The gesture state was cleared and the layer restored, so the session
                // stays usable and the next gesture records normally.
                session.ActiveTool = new PencilTool { CurrentColor = Color(7, 7, 7, 255) };
                Require(session.PointerDown(1, 1));
                Require(session.PointerUp(1, 1));
                Require(session.History.Current.UndoCount == 1);

                // A layer ADDED by a tool mid-gesture is invisible to the diff: no history entry,
                // the addition silently persists (documents diff scoping).
                session.History.Clear();
                session.ActiveTool = new SessionLogicLayerAddTool();
                Require(session.PointerDown(0, 0));
                Require(!session.PointerUp(0, 0));
                Require(target.Layers.Count == 2 && !session.History.CanUndo);
                Require(!session.Undo());
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestSessionLogicCommandManagerThrowingCommands()
    {
        var manager = new CommandManager(8, 4096);
        int events = 0;
        CommandHistorySnapshot lastEvent = default;
        manager.HistoryChanged += snapshot =>
        {
            events++;
            lastEvent = snapshot;
        };
        var failedCommands = new List<ICommand>();
        var failedExceptions = new List<Exception>();
        var failedEventCounts = new List<int>();
        manager.CommandFailed += (command, exception) =>
        {
            failedCommands.Add(command);
            failedExceptions.Add(exception);
            failedEventCounts.Add(events);
        };
        int[] cell = { 0 };

        manager.ExecuteCommand(new SessionLogicCounterCommand(cell, 5));
        Require(cell[0] == 5 && events == 1 && manager.Current.UndoCount == 1);

        // A command that throws in Execute is never recorded and publishes no event.
        RequireThrows<InvalidDataException>(() =>
            manager.ExecuteCommand(new SessionLogicBombCommand(true, false)));
        Require(events == 1 && manager.Current.UndoCount == 1 && cell[0] == 5);
        Require(failedCommands.Count == 0);

        // FIXED (#20.4): a command that throws in Undo is dropped from BOTH stacks
        // (its replay cannot be trusted), HistoryChanged publishes the truthful new
        // counts BEFORE CommandFailed reports the dropped command, and the original
        // exception still propagates to the caller.
        var undoBomb = new SessionLogicBombCommand(false, true);
        manager.ExecuteCommand(undoBomb);
        Require(events == 2 && manager.Current.UndoCount == 2);
        Require(manager.Current.UndoBytes == 2L * Config.Command.EstimatedCommandOverheadBytes);
        RequireThrows<InvalidDataException>(() => manager.Undo());
        Require(manager.Current.UndoCount == 1 && manager.Current.RedoCount == 0);
        Require(manager.Current.UndoBytes == Config.Command.EstimatedCommandOverheadBytes);
        Require(manager.CanUndo && !manager.CanRedo);
        Require(events == 3);
        Require(lastEvent.UndoCount == 1 && lastEvent.RedoCount == 0);
        Require(failedCommands.Count == 1);
        Require(ReferenceEquals(failedCommands[0], undoBomb));
        Require(failedExceptions[0] is InvalidDataException);
        Require(failedEventCounts[0] == 3); // truthful counts were published first

        // Same contract for Redo: the throwing command is dropped from the redo
        // stack with a truthful HistoryChanged and a CommandFailed report.
        var redoBomb = new SessionLogicBombCommand(true, false);
        manager.RecordExecutedCommand(redoBomb);
        Require(events == 4 && manager.Current.UndoCount == 2);
        Require(manager.Undo());
        Require(events == 5 && manager.Current.RedoCount == 1);
        RequireThrows<InvalidDataException>(() => manager.Redo());
        Require(manager.Current.RedoCount == 0 && manager.Current.UndoCount == 1);
        Require(manager.Current.RedoBytes == 0 && events == 6);
        Require(lastEvent.RedoCount == 0 && lastEvent.UndoCount == 1);
        Require(failedCommands.Count == 2);
        Require(ReferenceEquals(failedCommands[1], redoBomb));
        Require(failedExceptions[1] is InvalidDataException);
        Require(failedEventCounts[1] == 6);

        // The surviving history still replays normally after the drops.
        Require(manager.Undo());
        Require(cell[0] == 0 && events == 7);
        Require(!manager.Undo());
        Require(manager.Current.UndoCount == 0 && manager.Current.UndoBytes == 0);
        Require(manager.Current.RedoCount == 1);
        Require(manager.Redo());
        Require(cell[0] == 5);
        Require(failedCommands.Count == 2);
    }

    // Selection policy (#20.1): history replay CLAMPS (animationIndex, frameIndex) -
    // the same indices are kept while they still address a frame, the frame index is
    // clamped into the selected animation's range, and otherwise selection falls back
    // to the first animation with frames - so CurrentFrame is null after a history
    // operation only when NO animation has frames.
    private static void TestSessionLogicUndoRedoSelectionClampPolicy()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 2);
            using (DesignerSession session = DesignerSession.Create("ClampPolicy", project, root))
            {
                session.AutosaveEnabled = false;

                // Undoing AddFrame while the new frame is selected clamps to the last
                // remaining frame instead of stranding a null CurrentFrame.
                Require(session.SelectFrame(0, 1));
                Frame added = session.AddFrame();
                Require(ReferenceEquals(session.CurrentFrame, added));
                Require(session.Undo());
                Require(session.Current.AnimationIndex == 0 && session.Current.FrameIndex == 1);
                Require(ReferenceEquals(session.CurrentFrame, project.Animations[0].Frames[1]));
                Require(session.Redo());
                Require(session.Current.FrameIndex == 1); // still-valid indices are kept
                Require(session.CurrentFrame != null);

                // Pointer input works immediately after a clamping undo (the original
                // bug made it silently no-op on the stale selection).
                Require(session.Undo());
                session.ActiveTool = new PencilTool { CurrentColor = Color(3, 3, 3, 255) };
                Require(session.PointerDown(0, 0));
                Require(session.PointerUp(0, 0));
                Require(session.Undo());

                // Undoing AddAnimation while it is selected falls back to the first
                // animation that has frames; redo keeps the now-valid selection (clamp,
                // not restore).
                var walk = new AnimationState("SessionLogicClampWalk", 7);
                walk.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight));
                session.AddAnimation(walk);
                Require(session.Current.AnimationIndex == 1 && session.Current.FrameIndex == 0);
                Require(session.Undo());
                Require(session.Current.AnimationIndex == 0 && session.Current.FrameIndex == 0);
                Require(session.CurrentFrame != null);
                Require(session.Redo());
                Require(session.Current.AnimationIndex == 0 && session.CurrentFrame != null);

                // Redoing RemoveFrame clamps the frame index down into range.
                Require(session.SelectFrame(0, 1));
                session.RemoveFrame(1);
                Require(session.Current.FrameIndex == 0);
                Require(session.Undo());
                Require(session.SelectFrame(0, 1));
                Require(session.Redo());
                Require(session.Current.FrameIndex == 0 && session.CurrentFrame != null);
                Require(session.Undo());

                // Layer operations require CurrentFrame; after every replay they must
                // never see a null frame while frames exist.
                Require(session.Undo());
                session.SetLayerVisibility(0, session.CurrentFrame.Layers[0].IsVisible);
                Require(session.Redo());
                session.SetLayerVisibility(0, session.CurrentFrame.Layers[0].IsVisible);
            }

            // A command that throws during replay still leaves a clamped selection:
            // the clamp runs even when the replayed command faults.
            EFYVProject faulty = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("ClampFault", faulty, root))
            {
                session.AutosaveEnabled = false;
                session.AddFrame();
                Require(session.Current.FrameIndex == 1);
                session.History.RecordExecutedCommand(new SessionLogicBombCommand(false, true));
                RequireThrows<InvalidDataException>(() => session.Undo());
                Require(session.Current.FrameIndex == 1 && session.CurrentFrame != null);
                Require(session.Undo()); // the surviving AddFrame command
                Require(session.Current.FrameIndex == 0 && session.CurrentFrame != null);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // Gesture rollback contract (#20.3): a gesture that structurally mutates
    // Frame.Layers rolls back from the captured before-state and surfaces exactly ONE
    // exception - the tool's own failure when it threw, otherwise the diff
    // constructor's InvalidOperationException for a displaced captured layer.
    private static void TestSessionLogicGestureRollbackContract()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            Frame target = project.Animations[0].Frames[0];
            var extra = new Layer("SessionLogicRollbackExtra", project.CanvasWidth, project.CanvasHeight);
            target.Layers.Add(extra);
            var random = new Random(0x0FF5E7);
            Layer baseLayer = target.Layers[0];
            for (int index = 0; index < baseLayer.Pixels.Length; index++)
            {
                baseLayer.Pixels[index].Rgba = NextUInt(random);
                extra.Pixels[index].Rgba = NextUInt(random);
            }
            uint[] basePixels = CopyRgba(baseLayer);
            uint[] extraPixels = CopyRgba(extra);
            string baseName = baseLayer.Name;

            using (DesignerSession session = DesignerSession.Create("RollbackContract", project, root))
            {
                session.AutosaveEnabled = false;

                // The tool scribbles, renames, hides, removes the captured layer AND
                // throws mid-drag: the ORIGINAL tool exception surfaces (never a
                // rollback fault) and the frame comes back exactly - membership by
                // reference, metadata, and captured-layer pixels.
                session.ActiveTool = new SessionLogicChaosTool
                {
                    RemoveFirstLayerOnDrag = true,
                    ThrowOn = SessionLogicChaosPhase.Drag
                };
                Require(session.PointerDown(0, 0));
                RequireThrows<InvalidDataException>(() => session.PointerDrag(1, 1));
                SessionLogicRequireRollbackRestored(
                    target, baseLayer, extra, baseName, basePixels, extraPixels);
                Require(!session.History.CanUndo && !session.Current.IsDirty);

                // A tool failure in PointerUp after the structural mutation also wins
                // over the would-be diff fault.
                session.ActiveTool = new SessionLogicChaosTool
                {
                    RemoveFirstLayerOnDrag = true,
                    ThrowOn = SessionLogicChaosPhase.Up
                };
                Require(session.PointerDown(0, 0));
                Require(session.PointerDrag(1, 1));
                RequireThrows<InvalidDataException>(() => session.PointerUp(2, 2));
                SessionLogicRequireRollbackRestored(
                    target, baseLayer, extra, baseName, basePixels, extraPixels);
                Require(!session.History.CanUndo && !session.Current.IsDirty);

                // Structural mutation without a tool exception: the diff constructor
                // detects the displaced captured layer (even though a sibling layer
                // with the same pixel count slid into its slot), PointerUp surfaces
                // that single InvalidOperationException, and the frame rolls back.
                session.ActiveTool = new SessionLogicChaosTool { RemoveFirstLayerOnDrag = true };
                Require(session.PointerDown(0, 0));
                Require(session.PointerDrag(1, 1));
                RequireThrows<InvalidOperationException>(() => session.PointerUp(2, 2));
                SessionLogicRequireRollbackRestored(
                    target, baseLayer, extra, baseName, basePixels, extraPixels);
                Require(!session.History.CanUndo && !session.Current.IsDirty);

                // CancelGesture after a structural mid-gesture mutation restores
                // cleanly without throwing at all.
                session.ActiveTool = new SessionLogicChaosTool { RemoveFirstLayerOnDrag = true };
                Require(session.PointerDown(0, 0));
                Require(session.PointerDrag(1, 1));
                session.CancelGesture();
                SessionLogicRequireRollbackRestored(
                    target, baseLayer, extra, baseName, basePixels, extraPixels);
                Require(!session.History.CanUndo && !session.Current.IsDirty);

                // The session remains fully usable: a normal gesture records and undoes.
                session.ActiveTool = new PencilTool { CurrentColor = Color(9, 1, 1, 255) };
                Require(session.PointerDown(0, 0));
                Require(session.PointerUp(0, 0));
                Require(session.History.Current.UndoCount == 1);
                Require(session.Undo());
                RequireRgbaEqual(basePixels, target.Layers[0]);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestSessionLogicValidatorBoundariesAndHitboxModel()
    {
        var schema = new AssetSchemaService();
        var validator = new ProjectValidator(schema);
        string enemy = Config.Types.AssetTypeEnemyData;

        // Exactly at each documented limit is legal; one past it is not.
        EFYVProject animationLimit = CreateValidatorProject(enemy, 8, 8);
        AnimationState reusableAnimation = animationLimit.Animations[0];
        while (animationLimit.Animations.Count < Config.Persistence.MaxAnimations)
            animationLimit.Animations.Add(reusableAnimation);
        ProjectValidationResult result = validator.Validate(animationLimit);
        Require(result.IsValid && !ContainsIssue(result, ProjectIssueCode.AnimationLimitExceeded));
        animationLimit.Animations.Add(reusableAnimation);
        Require(ContainsIssue(validator.Validate(animationLimit), ProjectIssueCode.AnimationLimitExceeded));

        EFYVProject frameLimit = CreateValidatorProject(enemy, 1, 1);
        Frame reusableFrame = frameLimit.Animations[0].Frames[0];
        while (frameLimit.Animations[0].Frames.Count < Config.Persistence.MaxFramesPerAnimation)
            frameLimit.Animations[0].Frames.Add(reusableFrame);
        result = validator.Validate(frameLimit);
        Require(result.IsValid && !ContainsIssue(result, ProjectIssueCode.FrameLimitExceeded));
        Require(!ContainsIssue(result, ProjectIssueCode.AtlasLimitExceeded));
        frameLimit.Animations[0].Frames.Add(reusableFrame);
        Require(ContainsIssue(validator.Validate(frameLimit), ProjectIssueCode.FrameLimitExceeded));

        EFYVProject layerLimit = CreateValidatorProject(enemy, 8, 8);
        Layer reusableLayer = layerLimit.Animations[0].Frames[0].Layers[0];
        while (layerLimit.Animations[0].Frames[0].Layers.Count < Config.Persistence.MaxLayersPerFrame)
            layerLimit.Animations[0].Frames[0].Layers.Add(reusableLayer);
        result = validator.Validate(layerLimit);
        Require(result.IsValid && !ContainsIssue(result, ProjectIssueCode.LayerLimitExceeded));
        layerLimit.Animations[0].Frames[0].Layers.Add(reusableLayer);
        Require(ContainsIssue(validator.Validate(layerLimit), ProjectIssueCode.LayerLimitExceeded));

        // Grid-atlas budget (b2-pipeline-contract agent): with the batch-1
        // near-square layout, 16 frames of a 4096x1 canvas form a 4x4 grid at
        // exactly the 16384px width cap (legal); a 17th frame needs a 5th
        // column and exceeds it. (The retired single-row model capped at 4.)
        EFYVProject atlasWidth = CreateValidatorProject(enemy, Config.Persistence.MaxCanvasDimension, 1);
        Frame wideFrame = atlasWidth.Animations[0].Frames[0];
        int gridColumns = Config.Export.MaxAtlasDimension / Config.Persistence.MaxCanvasDimension;
        int fittingFrames = gridColumns * gridColumns;
        while (atlasWidth.Animations[0].Frames.Count < fittingFrames)
            atlasWidth.Animations[0].Frames.Add(wideFrame);
        result = validator.Validate(atlasWidth);
        Require(result.IsValid && !ContainsIssue(result, ProjectIssueCode.AtlasLimitExceeded));
        atlasWidth.Animations[0].Frames.Add(wideFrame);
        Require(ContainsIssue(validator.Validate(atlasWidth), ProjectIssueCode.AtlasLimitExceeded));

        // Atlas pixel count exactly at MaxAtlasPixelCount is legal: 4 max-canvas
        // frames form a 2x2 grid of 8192x8192 = MaxAtlasPixelCount; a 5th frame
        // widens the grid past the pixel budget.
        Require((long)Config.Export.MaxAtlasDimension * Config.Persistence.MaxCanvasDimension ==
            Config.Export.MaxAtlasPixelCount);
        EFYVProject atlasPixels = CreateValidatorProject(
            enemy,
            Config.Persistence.MaxCanvasDimension,
            Config.Persistence.MaxCanvasDimension);
        Frame bigFrame = atlasPixels.Animations[0].Frames[0];
        while (atlasPixels.Animations[0].Frames.Count < 4)
            atlasPixels.Animations[0].Frames.Add(bigFrame);
        result = validator.Validate(atlasPixels);
        Require(result.IsValid && !ContainsIssue(result, ProjectIssueCode.AtlasLimitExceeded));
        atlasPixels.Animations[0].Frames.Add(bigFrame);
        Require(ContainsIssue(validator.Validate(atlasPixels), ProjectIssueCode.AtlasLimitExceeded));

        // Boss phase threshold: equality is legal, strictly-greater is flagged, and the rule is
        // skipped entirely when maxHealth is missing.
        EFYVProject boss = CreateValidatorProject(Config.Types.AssetTypeBossData, 8, 8);
        boss.AssetProperties[SharedConfig.MaxHealthField] = 50f;
        boss.AssetProperties[SharedConfig.Phase2HealthThresholdField] = 50f;
        Require(!ContainsIssue(
            validator.Validate(boss),
            ProjectIssueCode.BossPhaseThresholdExceedsMaxHealth));
        boss.AssetProperties[SharedConfig.Phase2HealthThresholdField] = 50.5f;
        Require(ContainsIssue(
            validator.Validate(boss),
            ProjectIssueCode.BossPhaseThresholdExceedsMaxHealth));
        boss.AssetProperties.Remove(SharedConfig.MaxHealthField);
        result = validator.Validate(boss);
        Require(!ContainsIssue(result, ProjectIssueCode.BossPhaseThresholdExceedsMaxHealth));
        Require(ContainsIssue(result, ProjectIssueCode.MissingProperty));

        // Negative values on already-covered codes.
        EFYVProject negativeFps = CreateValidatorProject(enemy, 8, 8);
        negativeFps.Animations[0].FPS = -3;
        Require(ContainsIssue(validator.Validate(negativeFps), ProjectIssueCode.InvalidFrameRate));
        EFYVProject negativeCanvas = CreateValidatorProject(enemy, 8, 8);
        negativeCanvas.CanvasWidth = -1;
        Require(ContainsIssue(
            validator.Validate(negativeCanvas),
            ProjectIssueCode.InvalidCanvasDimensions));

        // Randomized hitbox-bounds reference model with adversarial values (fixed seed).
        var random = new Random(0x0B0C5E);
        float limit = 8 / Config.Hitbox.PixelsPerUnit;
        float[] pool =
        {
            0f, -0f, 0.25f, 0.5f, 0.4999999f, 0.5000001f, -0.001f, 1f,
            float.Epsilon, 0.1f, 0.3f, float.NaN, float.PositiveInfinity, float.NegativeInfinity
        };
        for (int batch = 0; batch < 40; batch++)
        {
            EFYVProject hitboxProject = CreateValidatorProject(enemy, 8, 8);
            Frame hitboxFrame = hitboxProject.Animations[0].Frames[0];
            var expectedBad = new HashSet<string>(StringComparer.Ordinal);
            for (int key = 0; key < 8; key++)
            {
                string name = "SessionLogicH" + key;
                float x = pool[random.Next(pool.Length)];
                float y = pool[random.Next(pool.Length)];
                float width = pool[random.Next(pool.Length)];
                float height = pool[random.Next(pool.Length)];
                hitboxFrame.Hitboxes[name] = MatrixHitbox(x, y, width, height);
                bool bad = !SessionLogicIsFinite(x) || !SessionLogicIsFinite(y) ||
                    !SessionLogicIsFinite(width) || !SessionLogicIsFinite(height) ||
                    x < 0f || y < 0f || width < 0f || height < 0f ||
                    x > limit || y > limit || width > limit - x || height > limit - y;
                if (bad) expectedBad.Add(name);
            }
            ProjectValidationResult hitboxResult = validator.Validate(hitboxProject);
            for (int key = 0; key < 8; key++)
            {
                string name = "SessionLogicH" + key;
                bool flagged = false;
                foreach (ProjectIssue issue in hitboxResult.Issues)
                {
                    if (issue.Code == ProjectIssueCode.InvalidHitboxBounds && issue.Subject == name)
                        flagged = true;
                }
                Require(flagged == expectedBad.Contains(name));
            }
        }
    }

    private static void TestSessionLogicSchemaChainsAndValueKinds()
    {
        var schema = new AssetSchemaService();
        int builtInCount = schema.GetAvailableTypes().Count;
        string enemy = Config.Types.AssetTypeEnemyData;

        // Chained registration: derived-of-derived inherits fields and directionality.
        Require(schema.RegisterAssetType(new AssetSchemaRegistration(
            "SessionLogicChainAData", "SessionLogic Chain A", enemy)));
        Require(schema.RegisterAssetType(new AssetSchemaRegistration(
            "SessionLogicChainBData", "SessionLogic Chain B", "SessionLogicChainAData")));
        SchemaDefinition chainB = schema.GetTypeDefinition("SessionLogicChainBData");
        SchemaDefinition enemyDefinition = schema.GetTypeDefinition(enemy);
        Require(chainB != null && chainB.BaseAssetType == "SessionLogicChainAData");
        Require(chainB.IsDirectional == enemyDefinition.IsDirectional);
        Require(chainB.IdentityFieldName == enemyDefinition.IdentityFieldName);
        RequireSchemaFieldsEqual(chainB.Fields, enemyDefinition.Fields);
        Require(schema.GetAvailableTypes().Count == builtInCount + 2);

        // Self-referential and null-field registrations are rejected without mutating the service.
        Require(!schema.RegisterAssetType(new AssetSchemaRegistration(
            "SessionLogicSelfData", "SessionLogic Self", "SessionLogicSelfData")));
        Require(!schema.RegisterAssetType(new AssetSchemaRegistration(
            "SessionLogicOrphanData", "SessionLogic Orphan", null)));
        Require(!schema.RegisterAssetType(new AssetSchemaRegistration(
            null, "SessionLogic Null", enemy)));
        Require(!schema.RegisterAssetType(new AssetSchemaRegistration(
            "SessionLogicNoNameData", null, enemy)));
        Require(schema.GetAvailableTypes().Count == builtInCount + 2);

        // Manifest registration is order-dependent: a child listed before its base fails that pass.
        var fresh = new AssetSchemaService();
        int registered = fresh.RegisterManifest(new[]
        {
            new AssetSchemaRegistration("SessionLogicOrderBData", "SessionLogic Order B", "SessionLogicOrderAData"),
            new AssetSchemaRegistration("SessionLogicOrderAData", "SessionLogic Order A", enemy)
        });
        Require(registered == 1);
        Require(fresh.GetTypeDefinition("SessionLogicOrderBData") == null);
        Require(fresh.GetTypeDefinition("SessionLogicOrderAData") != null);
        Require(fresh.RegisterManifest(new[]
        {
            new AssetSchemaRegistration("SessionLogicOrderBData", "SessionLogic Order B", "SessionLogicOrderAData")
        }) == 1);

        // Registrations are per-instance: a fresh service knows nothing about the other's types.
        var isolated = new AssetSchemaService();
        Require(isolated.GetAvailableTypes().Count == builtInCount);
        Require(isolated.GetTypeDefinition("SessionLogicChainAData") == null);
        Require(!isolated.RegisterAssetType(new AssetSchemaRegistration(
            "SessionLogicLeafData", "SessionLogic Leaf", "SessionLogicChainAData")));

        // Documents current behavior: duplicate display names are permitted (only AssetType is keyed).
        Require(fresh.RegisterAssetType(new AssetSchemaRegistration(
            "SessionLogicDupDisplayData", "SessionLogic Order A", enemy)));

        // ResolveValueKind mapping is exact and case-sensitive.
        Require(AssetSchemaService.ResolveValueKind(Config.Types.FloatSingle) == SchemaValueKind.Float);
        Require(AssetSchemaService.ResolveValueKind(Config.Types.FloatLower) == SchemaValueKind.Float);
        Require(AssetSchemaService.ResolveValueKind(Config.Types.Int32) == SchemaValueKind.Integer);
        Require(AssetSchemaService.ResolveValueKind(Config.Types.IntLower) == SchemaValueKind.Integer);
        Require(AssetSchemaService.ResolveValueKind(Config.Types.StringUpper) == SchemaValueKind.Text);
        Require(AssetSchemaService.ResolveValueKind(Config.Types.StringLower) == SchemaValueKind.Text);
        Require(AssetSchemaService.ResolveValueKind(null) == SchemaValueKind.Unknown);
        Require(AssetSchemaService.ResolveValueKind(string.Empty) == SchemaValueKind.Unknown);
        Require(AssetSchemaService.ResolveValueKind("Double") == SchemaValueKind.Unknown);
        Require(AssetSchemaService.ResolveValueKind(
            Config.Types.FloatSingle.ToUpperInvariant()) == SchemaValueKind.Unknown);
        Require(AssetSchemaService.ResolveValueKind(Config.Types.Int32 + " ") == SchemaValueKind.Unknown);
    }

    private static void TestSessionLogicAssetBankBytesAndCorpus()
    {
        RequireThrows<ArgumentException>(() => new AssetBankManager(null));
        RequireThrows<ArgumentException>(() => new AssetBankManager(string.Empty));
        RequireThrows<ArgumentException>(() => new AssetBankManager("   "));

        string root = NewTemporaryDirectory();
        try
        {
            // The constructor creates missing nested directories.
            string nested = Path.Combine(root, "session", "logic");
            Require(!Directory.Exists(nested));
            var bank = new AssetBankManager(nested);
            Require(Directory.Exists(nested));

            // Byte-level serialization layout of a saved sub-element
            // (.efyvsub version 2, item #6: the pivot + default-transform
            // header sits between the dimensions and the pixel count).
            bank.SaveSubElement(new SubElement("Layout", 2, 1, new uint[] { 0x11223344u, 0xAABBCCDDu }));
            byte[] bytes = File.ReadAllBytes(
                Path.Combine(nested, "Layout" + Config.Export.ExtensionEfyvSub));
            Require(bytes.Length == 52);
            Require(BitConverter.ToInt32(bytes, 0) == Config.Persistence.SubElementFormatVersion);
            Require(bytes[4] == 6);
            Require(Encoding.UTF8.GetString(bytes, 5, 6) == "Layout");
            Require(BitConverter.ToInt32(bytes, 11) == 2);
            Require(BitConverter.ToInt32(bytes, 15) == 1);
            Require(BitConverter.ToInt32(bytes, 19) == 1);  // pivotX (center of width 2)
            Require(BitConverter.ToInt32(bytes, 23) == 0);  // pivotY (center of height 1)
            Require(BitConverter.ToInt32(bytes, 27) == 0);  // defaultOffsetX
            Require(BitConverter.ToInt32(bytes, 31) == 0);  // defaultOffsetY
            Require(BitConverter.ToInt32(bytes, 35) == Config.Attachment.DefaultZOrder);
            Require(bytes[39] == 0);                        // flip flags
            Require(BitConverter.ToInt32(bytes, 40) == 2);
            Require(BitConverter.ToUInt32(bytes, 44) == 0x11223344u);
            Require(BitConverter.ToUInt32(bytes, 48) == 0xAABBCCDDu);

            // Save-side guards.
            RequireThrows<ArgumentNullException>(() => bank.SaveSubElement(null));
            RequireThrows<ArgumentException>(() => bank.SaveSubElement(
                new SubElement("CON", 1, 1, new uint[1])));
            RequireThrows<ArgumentException>(() => bank.SaveSubElement(
                new SubElement("name.", 1, 1, new uint[1])));
            RequireThrows<ArgumentOutOfRangeException>(() => bank.SaveSubElement(new SubElement(
                "Wide",
                Config.Persistence.MaxSubElementDimension + 1,
                1,
                new uint[Config.Persistence.MaxSubElementDimension + 1])));

            // Extended corruption corpus routed through the LoadFailed event.
            string corpusDirectory = Path.Combine(root, "corpus");
            var corpusBank = new AssetBankManager(corpusDirectory);
            int failures = 0;
            corpusBank.LoadFailed += (path, exception) =>
            {
                failures++;
                Require(exception != null && path != null);
            };
            corpusBank.SaveSubElement(new SubElement("Valid", 1, 1, new uint[] { 42u }));
            File.WriteAllBytes(
                Path.Combine(corpusDirectory, "10-trailing" + Config.Export.ExtensionEfyvSub),
                SessionLogicSubElementBytes(
                    Config.Persistence.SubElementFormatVersion, "Trailing", 1, 1, 1,
                    new uint[] { 1u }, new byte[] { 0xEE }));
            File.WriteAllBytes(
                Path.Combine(corpusDirectory, "11-zerowidth" + Config.Export.ExtensionEfyvSub),
                SessionLogicSubElementBytes(
                    Config.Persistence.SubElementFormatVersion, "Zero", 0, 1, 0,
                    Array.Empty<uint>(), null));
            File.WriteAllBytes(
                Path.Combine(corpusDirectory, "12-toowide" + Config.Export.ExtensionEfyvSub),
                SessionLogicSubElementBytes(
                    Config.Persistence.SubElementFormatVersion, "Wide",
                    Config.Persistence.MaxSubElementDimension + 1, 1, 1,
                    new uint[] { 1u }, null));
            File.WriteAllBytes(
                Path.Combine(corpusDirectory, "13-negativecount" + Config.Export.ExtensionEfyvSub),
                SessionLogicSubElementBytes(
                    Config.Persistence.SubElementFormatVersion, "Negative", 1, 1, -1,
                    new uint[] { 1u }, null));
            File.WriteAllBytes(
                Path.Combine(corpusDirectory, "14-truncated" + Config.Export.ExtensionEfyvSub),
                SessionLogicSubElementBytes(
                    Config.Persistence.SubElementFormatVersion, "Truncated", 2, 2, 4,
                    new uint[] { 1u, 2u }, null));
            List<SubElement> loaded = corpusBank.LoadAllSubElements();
            Require(loaded.Count == 1 && loaded[0].Name == "Valid" && failures == 5);

            // BUG (documented current behavior): a malformed 7-bit-encoded string length raises
            // FormatException, which is missing from the catch list, so the whole scan aborts
            // instead of reporting a single LoadFailed file.
            string bombDirectory = Path.Combine(root, "bomb");
            var bombBank = new AssetBankManager(bombDirectory);
            bombBank.SaveSubElement(new SubElement("Fine", 1, 1, new uint[] { 7u }));
            string bombPath = Path.Combine(bombDirectory, "zz-bomb" + Config.Export.ExtensionEfyvSub);
            var bombBytes = new List<byte>(BitConverter.GetBytes(Config.Persistence.SubElementFormatVersion));
            bombBytes.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
            File.WriteAllBytes(bombPath, bombBytes.ToArray());
            RequireThrows<FormatException>(() => bombBank.LoadAllSubElements());
            File.Delete(bombPath);
            Require(bombBank.LoadAllSubElements().Count == 1);

            // Ordinal, case-sensitive load ordering (uppercase before lowercase).
            string orderDirectory = Path.Combine(root, "order");
            var orderBank = new AssetBankManager(orderDirectory);
            orderBank.SaveSubElement(new SubElement("aaa", 1, 1, new uint[1]));
            orderBank.SaveSubElement(new SubElement("Zulu", 1, 1, new uint[1]));
            orderBank.SaveSubElement(new SubElement("Alpha", 1, 1, new uint[1]));
            List<SubElement> ordered = orderBank.LoadAllSubElements();
            Require(ordered.Count == 3);
            Require(ordered[0].Name == "Alpha" && ordered[1].Name == "Zulu" && ordered[2].Name == "aaa");

            // ExtractFromCanvas reference model: every crop equals the flattened sub-rectangle.
            var random = new Random(0x0EB57);
            var frame = new Frame(12, 9);
            var second = new Layer("SessionLogicSecond", 12, 9) { Opacity = 0.5f };
            var hidden = new Layer("SessionLogicHidden", 12, 9) { IsVisible = false };
            frame.Layers.Add(second);
            frame.Layers.Add(hidden);
            foreach (Layer layer in frame.Layers)
            {
                for (int index = 0; index < layer.Pixels.Length; index++)
                    layer.Pixels[index].Rgba = NextUInt(random);
            }
            PixelColor[] flattened = frame.FlattenLayers();
            int[][] rectangles =
            {
                new[] { 0, 0, 12, 9 },
                new[] { 0, 0, 1, 1 },
                new[] { 11, 8, 1, 1 },
                new[] { 3, 2, 5, 4 },
                new[] { 0, 8, 12, 1 },
                new[] { 7, 0, 5, 9 }
            };
            foreach (int[] rectangle in rectangles)
            {
                int startX = rectangle[0];
                int startY = rectangle[1];
                int width = rectangle[2];
                int height = rectangle[3];
                SubElement crop = bank.ExtractFromCanvas(
                    frame, startX, startY, width, height, "SessionLogicCrop");
                Require(crop.Width == width && crop.Height == height);
                Require(crop.Pixels.Length == width * height);
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    Require(crop.Pixels[y * width + x] == flattened[(startY + y) * 12 + (startX + x)].Rgba);
            }

            // Extraction guard matrix.
            RequireThrows<ArgumentNullException>(() => bank.ExtractFromCanvas(null, 0, 0, 1, 1, "X"));
            RequireThrows<ArgumentException>(() => bank.ExtractFromCanvas(frame, 0, 0, 1, 1, null));
            RequireThrows<ArgumentOutOfRangeException>(() => bank.ExtractFromCanvas(frame, -1, 0, 1, 1, "X"));
            RequireThrows<ArgumentOutOfRangeException>(() => bank.ExtractFromCanvas(frame, 0, -1, 1, 1, "X"));
            RequireThrows<ArgumentOutOfRangeException>(() => bank.ExtractFromCanvas(frame, 12, 0, 1, 1, "X"));
            RequireThrows<ArgumentOutOfRangeException>(() => bank.ExtractFromCanvas(frame, 0, 9, 1, 1, "X"));
            RequireThrows<ArgumentOutOfRangeException>(() => bank.ExtractFromCanvas(frame, 0, 0, 13, 9, "X"));
            RequireThrows<ArgumentOutOfRangeException>(() => bank.ExtractFromCanvas(frame, 0, 0, 0, 1, "X"));
            RequireThrows<ArgumentOutOfRangeException>(() => bank.ExtractFromCanvas(frame, 0, 0, 1, 0, "X"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static Dictionary<string, HitboxData> SessionLogicCopyHitboxes(Frame frame)
    {
        return new Dictionary<string, HitboxData>(frame.Hitboxes, StringComparer.Ordinal);
    }

    private static void SessionLogicRequireHitboxesEqual(
        Dictionary<string, HitboxData> expected,
        Frame actual)
    {
        Require(expected.Count == actual.Hitboxes.Count);
        foreach (KeyValuePair<string, HitboxData> pair in expected)
        {
            HitboxData value;
            Require(actual.Hitboxes.TryGetValue(pair.Key, out value));
            Require(value.X == pair.Value.X && value.Y == pair.Value.Y);
            Require(value.Width == pair.Value.Width && value.Height == pair.Value.Height);
        }
    }

    private static bool SessionLogicIsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    // Writes the .efyvsub VERSION-2 layout (a version-1 body omits the
    // pivot/transform header; pass writeTransformHeader false for it).
    private static byte[] SessionLogicSubElementBytes(
        int version,
        string name,
        int width,
        int height,
        int declaredPixelCount,
        uint[] pixels,
        byte[] trailing,
        bool writeTransformHeader = true,
        int pivotX = 0,
        int pivotY = 0,
        byte flags = 0)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(version);
                writer.Write(name);
                writer.Write(width);
                writer.Write(height);
                if (writeTransformHeader)
                {
                    writer.Write(pivotX);
                    writer.Write(pivotY);
                    writer.Write(0); // defaultOffsetX
                    writer.Write(0); // defaultOffsetY
                    writer.Write(Config.Attachment.DefaultZOrder);
                    writer.Write(flags);
                }
                writer.Write(declaredPixelCount);
                foreach (uint pixel in pixels) writer.Write(pixel);
                if (trailing != null) writer.Write(trailing);
            }
            return stream.ToArray();
        }
    }

    private sealed class SessionLogicGestureRecord
    {
        public int LayerIndex;
        public uint[] Before;
        public uint[] After;
    }

    private sealed class SessionLogicScribbleTool : Tool, ILayerTool
    {
        public int ActiveLayerIndex { get; set; }
        public readonly List<int> Indices = new List<int>();
        public readonly List<uint> Values = new List<uint>();

        public void Reset(int layerIndex)
        {
            ActiveLayerIndex = layerIndex;
            Indices.Clear();
            Values.Clear();
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Apply(currentFrame, 0, Indices.Count / 3);
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Apply(currentFrame, Indices.Count / 3, (2 * Indices.Count) / 3);
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Apply(currentFrame, (2 * Indices.Count) / 3, Indices.Count);
        }

        private void Apply(Frame frame, int from, int to)
        {
            Layer layer = frame.Layers[ActiveLayerIndex];
            for (int index = from; index < to; index++)
                layer.Pixels[Indices[index]].Rgba = Values[index];
        }
    }

    private static void SessionLogicRequireRollbackRestored(
        Frame target,
        Layer baseLayer,
        Layer extra,
        string baseName,
        uint[] basePixels,
        uint[] extraPixels)
    {
        Require(target.Layers.Count == 2);
        Require(ReferenceEquals(target.Layers[0], baseLayer));
        Require(ReferenceEquals(target.Layers[1], extra));
        Require(target.Layers[0].Name == baseName);
        Require(target.Layers[0].IsVisible);
        RequireRgbaEqual(basePixels, target.Layers[0]);
        RequireRgbaEqual(extraPixels, target.Layers[1]);
    }

    private enum SessionLogicChaosPhase
    {
        None,
        Down,
        Drag,
        Up
    }

    private sealed class SessionLogicChaosTool : Tool, ILayerTool
    {
        public int ActiveLayerIndex { get; set; }
        public bool RemoveFirstLayerOnDrag { get; set; }
        public SessionLogicChaosPhase ThrowOn { get; set; } = SessionLogicChaosPhase.None;

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            currentFrame.Layers[ActiveLayerIndex].Pixels[0].Rgba = 0xDEADBEEFu;
            if (ThrowOn == SessionLogicChaosPhase.Down) throw new InvalidDataException();
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Layer layer = currentFrame.Layers[ActiveLayerIndex];
            layer.Name = "SessionLogicChaosRenamed";
            layer.IsVisible = false;
            if (RemoveFirstLayerOnDrag) currentFrame.Layers.RemoveAt(0);
            if (ThrowOn == SessionLogicChaosPhase.Drag) throw new InvalidDataException();
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (ThrowOn == SessionLogicChaosPhase.Up) throw new InvalidDataException();
        }
    }

    private sealed class SessionLogicLayerRemoveTool : Tool, ILayerTool
    {
        public int ActiveLayerIndex { get; set; }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            currentFrame.Layers.RemoveAt(0);
        }
    }

    private sealed class SessionLogicLayerAddTool : Tool, ILayerTool
    {
        public int ActiveLayerIndex { get; set; }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            currentFrame.Layers.Add(new Layer(
                "SessionLogicExtra",
                currentFrame.Width,
                currentFrame.Height));
        }
    }

    private sealed class SessionLogicCounterCommand : ICommand
    {
        private readonly int[] cell;
        private readonly int delta;

        public SessionLogicCounterCommand(int[] cell, int delta)
        {
            this.cell = cell;
            this.delta = delta;
        }

        public void Execute() => cell[0] += delta;
        public void Undo() => cell[0] -= delta;
    }

    private sealed class SessionLogicBombCommand : ICommand
    {
        private readonly bool throwOnExecute;
        private readonly bool throwOnUndo;

        public SessionLogicBombCommand(bool throwOnExecute, bool throwOnUndo)
        {
            this.throwOnExecute = throwOnExecute;
            this.throwOnUndo = throwOnUndo;
        }

        public void Execute()
        {
            if (throwOnExecute) throw new InvalidDataException();
        }

        public void Undo()
        {
            if (throwOnUndo) throw new InvalidDataException();
        }
    }
}
