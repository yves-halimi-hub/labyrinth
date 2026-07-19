using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

// batch3.8 agent: item #33 - linked 4-direction authoring (DirectionalState
// model + facing routing, undoable session enable/switch/mirror commands,
// export-scope completeness validation, .efyvmake directional section, the
// all-facings export/live-debug flow) and runtime-extensible asset fields
// (AssetSchemaService.RegisterAssetType custom fields + the export flow that
// carries them into .efyvlaby properties).
internal static partial class Program
{
    private static void TestDirectionalStateModelAndFacingRouting()
    {
        // Facing-name predicate: exactly the four canonical names.
        foreach (string facing in Config.Schema.FacingChoices)
            Require(DirectionalState.IsFacingName(facing));
        foreach (string invalid in new[] { null, "", " ", "down", "DOWN", "Diagonal", "Up " })
            Require(!DirectionalState.IsFacingName(invalid));

        RequireThrows<ArgumentException>(() => new DirectionalState(null));
        RequireThrows<ArgumentException>(() => new DirectionalState("down"));
        RequireThrows<ArgumentException>(() => new DirectionalState(Config.Entity.FacingNone));

        var state = new DirectionalState(SharedConfig.FacingDown);
        Require(state.ActiveFacing == SharedConfig.FacingDown);
        Require(state.GetInactiveFacingAnimations(SharedConfig.FacingUp).Count == 0);
        Require(state.GetInactiveFacingAnimations(SharedConfig.FacingLeft).Count == 0);
        Require(state.GetInactiveFacingAnimations(SharedConfig.FacingRight).Count == 0);
        // The active facing is NOT parked; asking for it (or junk) throws.
        RequireThrows<ArgumentException>(() => state.GetInactiveFacingAnimations(SharedConfig.FacingDown));
        RequireThrows<ArgumentException>(() => state.GetInactiveFacingAnimations("Diagonal"));
        RequireThrows<ArgumentException>(() => state.GetInactiveFacingAnimations(null));
        RequireThrows<ArgumentNullException>(() => state.SetInactiveSet(SharedConfig.FacingUp, null));
        RequireThrows<ArgumentException>(() => state.SetInactiveSet(SharedConfig.FacingDown, new List<AnimationState>()));

        // Switch moves animation sets BY REFERENCE between the live list and
        // the parked sets.
        var project = new EFYVProject(Config.Types.AssetTypeEnemyData);
        Require(!project.IsDirectional);
        RequireThrows<InvalidOperationException>(() => project.GetFacingAnimations(SharedConfig.FacingDown));
        project.Directional = state;
        Require(project.IsDirectional);

        var downIdle = new AnimationState("DownIdle", 4);
        downIdle.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, 0));
        project.Animations.Add(downIdle);
        Require(ReferenceEquals(project.GetFacingAnimations(SharedConfig.FacingDown), project.Animations));
        RequireThrows<ArgumentException>(() => project.GetFacingAnimations("Diagonal"));

        RequireThrows<ArgumentNullException>(() => state.Switch(null, SharedConfig.FacingUp));
        state.Switch(project, SharedConfig.FacingUp);
        Require(state.ActiveFacing == SharedConfig.FacingUp);
        Require(project.Animations.Count == 0);
        Require(state.GetInactiveFacingAnimations(SharedConfig.FacingDown).Count == 1);
        Require(ReferenceEquals(state.GetInactiveFacingAnimations(SharedConfig.FacingDown)[0], downIdle));
        RequireThrows<ArgumentException>(() => state.Switch(project, "Diagonal"));

        var upIdle = new AnimationState("UpIdle", 4);
        upIdle.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, 0));
        project.Animations.Add(upIdle);
        state.Switch(project, SharedConfig.FacingDown);
        Require(ReferenceEquals(project.Animations[0], downIdle));
        Require(ReferenceEquals(state.GetInactiveFacingAnimations(SharedConfig.FacingUp)[0], upIdle));

        // EnumerateAllAnimations spans the live list plus every parked set;
        // plain projects yield only the live list.
        int enumerated = 0;
        foreach (AnimationState animation in project.EnumerateAllAnimations()) { _ = animation; enumerated++; }
        Require(enumerated == 2);
        var plain = new EFYVProject(Config.Types.AssetTypeEnemyData);
        plain.Animations.Add(downIdle);
        enumerated = 0;
        foreach (AnimationState animation in plain.EnumerateAllAnimations()) { _ = animation; enumerated++; }
        Require(enumerated == 1);
    }

    private static void TestToolbarLinkedDirectionalCreation()
    {
        var schema = new AssetSchemaService();
        var toolbar = new ToolbarAPI(schema);

        EFYVProject linked = toolbar.CreateNewLinkedDirectionalProject(Config.Types.AssetTypeEnemyData);
        Require(linked != null && linked.IsDirectional);
        Require(linked.Directional.ActiveFacing == Config.Entity.DefaultActiveFacing);
        Require((string)linked.AssetProperties[SharedConfig.FacingField] == Config.Entity.DefaultActiveFacing);
        Require(linked.TargetAssetType == Config.Types.AssetTypeEnemyData);
        // Field defaults are populated exactly like CreateNewProject.
        Require(Convert.ToSingle(linked.AssetProperties[SharedConfig.MaxHealthField]) ==
            EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game.Player.DefaultMaxHealth);
        foreach (string facing in Config.Schema.FacingChoices)
        {
            if (facing == Config.Entity.DefaultActiveFacing) continue;
            Require(linked.GetFacingAnimations(facing).Count == 0);
        }

        // Unknown and non-directional types both return null.
        Require(toolbar.CreateNewLinkedDirectionalProject("NoSuchData") == null);
        Require(toolbar.CreateNewLinkedDirectionalProject(Config.Types.AssetTypeGameAssetData) == null);
        Require(toolbar.CreateNewLinkedDirectionalProject(null) == null);

        // The facing-switcher catalog is non-empty exactly for linked projects.
        Require(toolbar.GetFacingOptions(linked).Count == Config.Entity.DirectionalVariantCount);
        Require(toolbar.GetFacingOptions(new EFYVProject(Config.Types.AssetTypeEnemyData)).Count == 0);
        Require(toolbar.GetFacingOptions(null).Count == 0);
        // The catalog is a defensive copy, not the config array itself.
        IReadOnlyList<string> options = toolbar.GetFacingOptions(linked);
        Require(!ReferenceEquals(options, Config.Schema.FacingChoices));
        for (int index = 0; index < options.Count; index++)
            Require(options[index] == Config.Schema.FacingChoices[index]);
    }

    private static void TestSessionDirectionalEnableAndFacingSwitch()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("Directional", project, root))
            {
                session.AutosaveEnabled = false;
                Require(!session.IsDirectionalProject && session.ActiveFacing == null);
                RequireThrows<InvalidOperationException>(() => session.SwitchFacing(SharedConfig.FacingUp));
                RequireThrows<InvalidOperationException>(
                    () => session.GenerateMirroredFacing(SharedConfig.FacingRight, SharedConfig.FacingLeft));

                AnimationState downIdle = project.Animations[0];
                int undoBefore = session.History.Current.UndoCount;
                session.EnableDirectionalAuthoring();
                Require(session.IsDirectionalProject);
                // CreateValidProject uses the "(Down)" category, so the facing
                // property seeds the active facing.
                Require(session.ActiveFacing == SharedConfig.FacingDown);
                Require((string)project.AssetProperties[SharedConfig.FacingField] == SharedConfig.FacingDown);
                Require(session.History.Current.UndoCount == undoBefore + 1);
                RequireThrows<InvalidOperationException>(() => session.EnableDirectionalAuthoring());

                // Enable is undoable: revert drops the mode and keeps the
                // original facing property.
                Require(session.Undo());
                Require(!session.IsDirectionalProject && project.Directional == null);
                Require((string)project.AssetProperties[SharedConfig.FacingField] == SharedConfig.FacingDown);
                Require(session.Redo());
                Require(session.IsDirectionalProject && session.ActiveFacing == SharedConfig.FacingDown);

                // Switching parks the live list by reference and swaps in the
                // target facing's set.
                RequireThrows<ArgumentException>(() => session.SwitchFacing("Diagonal"));
                RequireThrows<ArgumentException>(() => session.SwitchFacing(null));
                Require(!session.SwitchFacing(SharedConfig.FacingDown));
                int undoAfterEnable = session.History.Current.UndoCount;
                Require(session.SwitchFacing(SharedConfig.FacingUp));
                Require(session.ActiveFacing == SharedConfig.FacingUp);
                Require(project.Animations.Count == 0 && session.CurrentFrame == null);
                Require((string)project.AssetProperties[SharedConfig.FacingField] == SharedConfig.FacingUp);
                Require(session.History.Current.UndoCount == undoAfterEnable + 1);

                var upIdle = new AnimationState("UpIdle", 4);
                upIdle.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, 0));
                session.AddAnimation(upIdle);
                Require(session.CurrentFrame != null);

                Require(session.SwitchFacing(SharedConfig.FacingDown));
                Require(ReferenceEquals(project.Animations[0], downIdle));
                Require(ReferenceEquals(
                    project.GetFacingAnimations(SharedConfig.FacingUp)[0], upIdle));

                // Undo chain: switch-back, add, switch-out - strict LIFO keeps
                // every command replaying against the facing it was recorded on.
                Require(session.Undo());
                Require(session.ActiveFacing == SharedConfig.FacingUp &&
                    ReferenceEquals(project.Animations[0], upIdle));
                Require(session.Undo());
                Require(project.Animations.Count == 0);
                Require(session.Undo());
                Require(session.ActiveFacing == SharedConfig.FacingDown &&
                    ReferenceEquals(project.Animations[0], downIdle));
                Require(session.Redo() && session.Redo() && session.Redo());
                Require(session.ActiveFacing == SharedConfig.FacingDown &&
                    ReferenceEquals(project.Animations[0], downIdle) &&
                    project.GetFacingAnimations(SharedConfig.FacingUp).Count == 1);

                // In directional mode the facing property IS the switcher.
                PropertyEditResult switched = session.SetProperty(SharedConfig.FacingField, SharedConfig.FacingLeft);
                Require(switched.Status == PropertyEditStatus.Success);
                Require(session.ActiveFacing == SharedConfig.FacingLeft);
                PropertyEditResult rejected = session.SetProperty(SharedConfig.FacingField, "Diagonal");
                Require(rejected.Status == PropertyEditStatus.InvalidChoice);
                Require(session.ActiveFacing == SharedConfig.FacingLeft);
                PropertyEditResult numeric = session.SetProperty(SharedConfig.FacingField, 3f);
                Require(numeric.Status == PropertyEditStatus.InvalidChoice);
                Require(session.Undo());
                Require(session.ActiveFacing == SharedConfig.FacingDown);
            }

            // A non-directional asset type cannot enable the mode; a project
            // created from a non-Down category seeds its own active facing.
            var schema = new AssetSchemaService();
            var toolbar = new ToolbarAPI(schema);
            EFYVProject asset = toolbar.CreateNewProject(SharedConfig.GameAssetDisplayName);
            asset.UnityProjectPath = root;
            asset.AssetProperties[SharedConfig.AssetNameField] = "FlatTile";
            var assetAnimation = new AnimationState("Idle", 4);
            assetAnimation.Frames.Add(new Frame(asset.CanvasWidth, asset.CanvasHeight, 0));
            asset.Animations.Add(assetAnimation);
            using (DesignerSession session = DesignerSession.Create("Flat", asset, root))
            {
                session.AutosaveEnabled = false;
                RequireThrows<InvalidOperationException>(() => session.EnableDirectionalAuthoring());
            }

            EFYVProject upProject = toolbar.CreateNewProject(
                SharedConfig.EnemyDisplayName + Config.Entity.SuffixUp);
            upProject.UnityProjectPath = root;
            upProject.AssetProperties[SharedConfig.EntityNameField] = "UpFacer";
            var upAnimation = new AnimationState("Idle", 4);
            upAnimation.Frames.Add(new Frame(upProject.CanvasWidth, upProject.CanvasHeight, 0));
            upProject.Animations.Add(upAnimation);
            using (DesignerSession session = DesignerSession.Create("UpFacer", upProject, root))
            {
                session.AutosaveEnabled = false;
                session.EnableDirectionalAuthoring();
                Require(session.ActiveFacing == SharedConfig.FacingUp);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestSessionMirrorGeneratedFacing()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("Mirror", project, root))
            {
                session.AutosaveEnabled = false;
                session.EnableDirectionalAuthoring();
                Require(session.SwitchFacing(SharedConfig.FacingRight));

                int width = project.CanvasWidth;
                var rightWalk = new AnimationState("Walk", 6)
                {
                    LoopStartFrame = 0,
                    PingPong = true
                };
                var frame = new Frame(width, project.CanvasHeight, 0);
                frame.Layers[0].SetPixel(0, 0, Color(255, 0, 0, 255));
                frame.Layers[0].SetPixel(1, 0, Color(0, 255, 0, 255));
                var hurtbox = EFYVBackend.Core.Models.HitboxData.CreateDefault();
                hurtbox.X = 0.5f;
                hurtbox.Y = 0.25f;
                hurtbox.Width = 1f;
                hurtbox.Height = 2f;
                frame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = hurtbox;
                frame.Attachments.Add(new SubElementAttachment("Blade", 2, 3, 0, false, true));
                rightWalk.Frames.Add(frame);
                session.AddAnimation(rightWalk);

                // Only the Left/Right horizontal pair mirrors.
                RequireThrows<ArgumentException>(
                    () => session.GenerateMirroredFacing(SharedConfig.FacingUp, SharedConfig.FacingDown));
                RequireThrows<ArgumentException>(
                    () => session.GenerateMirroredFacing(SharedConfig.FacingRight, SharedConfig.FacingUp));
                RequireThrows<ArgumentException>(
                    () => session.GenerateMirroredFacing("Diagonal", SharedConfig.FacingLeft));

                int undoBefore = session.History.Current.UndoCount;
                Require(session.GenerateMirroredFacing(SharedConfig.FacingRight, SharedConfig.FacingLeft) == 1);
                Require(session.History.Current.UndoCount == undoBefore + 1);

                IReadOnlyList<AnimationState> left = project.GetFacingAnimations(SharedConfig.FacingLeft);
                Require(left.Count == 1 && !ReferenceEquals(left[0], rightWalk));
                Require(left[0].StateName == "Walk" && left[0].FPS == 6 && left[0].PingPong);
                Frame mirrored = left[0].Frames[0];
                Require(mirrored.Layers[0].GetPixel(width - 1, 0).R == 255);
                Require(mirrored.Layers[0].GetPixel(width - 2, 0).G == 255);
                Require(mirrored.Layers[0].GetPixel(0, 0).Rgba == 0u);
                Require(mirrored.Layers[0].GetPixel(1, 0).Rgba == 0u);

                float span = width / Config.Hitbox.PixelsPerUnit;
                EFYVBackend.Core.Models.HitboxData mirroredBox =
                    mirrored.Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
                Require(mirroredBox.X == span - 0.5f - 1f);
                Require(mirroredBox.Y == 0.25f && mirroredBox.Width == 1f && mirroredBox.Height == 2f);

                Require(mirrored.Attachments.Count == 1);
                Require(mirrored.Attachments[0].SubElementName == "Blade");
                Require(mirrored.Attachments[0].X == width - 1 - 2);
                Require(mirrored.Attachments[0].Y == 3);
                Require(mirrored.Attachments[0].FlipX && mirrored.Attachments[0].FlipY);
                // The source frame is untouched.
                Require(frame.Attachments[0].X == 2 && !frame.Attachments[0].FlipX);

                Require(session.Undo());
                Require(project.GetFacingAnimations(SharedConfig.FacingLeft).Count == 0);
                Require(session.Redo());
                Require(project.GetFacingAnimations(SharedConfig.FacingLeft).Count == 1);

                // Mirroring back onto the ACTIVE facing replaces the live list;
                // a double mirror restores the original geometry exactly.
                Require(session.GenerateMirroredFacing(SharedConfig.FacingLeft, SharedConfig.FacingRight) == 1);
                Frame roundTripped = project.Animations[0].Frames[0];
                Require(session.CurrentFrame != null);
                Require(roundTripped.Layers[0].GetPixel(0, 0).R == 255);
                Require(roundTripped.Layers[0].GetPixel(1, 0).G == 255);
                EFYVBackend.Core.Models.HitboxData roundBox =
                    roundTripped.Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
                Require(roundBox.X == 0.5f && roundBox.Width == 1f);
                Require(roundTripped.Attachments[0].X == 2 && !roundTripped.Attachments[0].FlipX);
                Require(roundTripped.Attachments[0].FlipY);
            }

            // Source and target both empty: nothing is generated or recorded.
            EFYVProject blank = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("MirrorBlank", blank, root))
            {
                session.AutosaveEnabled = false;
                session.EnableDirectionalAuthoring();
                int undoBefore = session.History.Current.UndoCount;
                Require(session.GenerateMirroredFacing(SharedConfig.FacingRight, SharedConfig.FacingLeft) == 0);
                Require(session.History.Current.UndoCount == undoBefore);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestDirectionalValidationExportScope()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            var toolbar = new ToolbarAPI(schema);
            var validator = new ProjectValidator(schema);

            EFYVProject project = toolbar.CreateNewLinkedDirectionalProject(Config.Types.AssetTypeEnemyData);
            project.UnityProjectPath = root;
            project.AssetProperties[SharedConfig.EntityNameField] = "FacingProbe";
            var idle = new AnimationState("Idle", 4);
            idle.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, 0));
            project.Animations.Add(idle);

            // Designer/persistence/structural scopes ignore parked facings.
            Require(validator.Validate(project).IsValid);
            Require(validator.Validate(project, ProjectValidationScope.Persistence).IsValid);
            Require(validator.Validate(project, ProjectValidationScope.Structural).IsValid);

            ProjectValidationResult export = validator.Validate(project, ProjectValidationScope.Export);
            Require(!export.IsValid);
            var incomplete = new List<string>();
            foreach (ProjectIssue issue in export.Issues)
            {
                if (issue.Code == ProjectIssueCode.DirectionalFacingIncomplete)
                    incomplete.Add(issue.Subject);
            }
            Require(incomplete.Count == Config.Entity.DirectionalVariantCount - 1);
            Require(incomplete.Contains(SharedConfig.FacingUp));
            Require(incomplete.Contains(SharedConfig.FacingLeft));
            Require(incomplete.Contains(SharedConfig.FacingRight));
            Require(!incomplete.Contains(SharedConfig.FacingDown));

            // Filling facings clears their issue one by one.
            foreach (string facing in new[] { SharedConfig.FacingUp, SharedConfig.FacingLeft })
            {
                var animation = new AnimationState("Idle" + facing, 4);
                animation.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, 0));
                project.Directional.SetInactiveSet(facing, new List<AnimationState> { animation });
            }
            export = validator.Validate(project, ProjectValidationScope.Export);
            Require(!export.IsValid);
            Require(ContainsIssue(export, ProjectIssueCode.DirectionalFacingIncomplete));
            var rightIdle = new AnimationState("IdleRight", 4);
            rightIdle.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, 0));
            project.Directional.SetInactiveSet(
                SharedConfig.FacingRight, new List<AnimationState> { rightIdle });
            Require(validator.Validate(project, ProjectValidationScope.Export).IsValid);

            // A structurally broken parked facing fails at export scope only.
            project.Directional.GetInactiveSet(SharedConfig.FacingUp).Add(new AnimationState("NoFrames", 4));
            Require(validator.Validate(project).IsValid);
            export = validator.Validate(project, ProjectValidationScope.Export);
            Require(!export.IsValid && ContainsIssue(export, ProjectIssueCode.MissingFrames));
            project.Directional.GetInactiveSet(SharedConfig.FacingUp).RemoveAt(1);

            // An empty ACTIVE set still reports plain MissingAnimations, not the
            // directional code, and does so at designer scope too.
            project.Animations.Clear();
            ProjectValidationResult designer = validator.Validate(project);
            Require(!designer.IsValid && ContainsIssue(designer, ProjectIssueCode.MissingAnimations));
            Require(!ContainsIssue(designer, ProjectIssueCode.DirectionalFacingIncomplete));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestDirectionalPersistenceRoundTripAndCorpus()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var schema = new AssetSchemaService();
            var toolbar = new ToolbarAPI(schema);
            var persistence = new ProjectPersistenceService(root, schema);

            EFYVProject project = toolbar.CreateNewLinkedDirectionalProject(Config.Types.AssetTypeEnemyData);
            project.UnityProjectPath = root;
            project.AssetProperties[SharedConfig.EntityNameField] = "RoundTripper";
            var downIdle = new AnimationState("IdleDown", 4);
            var downFrame = new Frame(project.CanvasWidth, project.CanvasHeight, 0);
            downFrame.Layers[0].SetPixel(2, 3, Color(12, 34, 56, 255));
            downIdle.Frames.Add(downFrame);
            project.Animations.Add(downIdle);
            byte pixelSeed = 1;
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (facing == project.Directional.ActiveFacing) continue;
                var animation = new AnimationState("Idle" + facing, 5);
                var frame = new Frame(project.CanvasWidth, project.CanvasHeight, 0);
                frame.Layers[0].SetPixel(pixelSeed, 0, Color(pixelSeed, 0, 0, 255));
                animation.Frames.Add(frame);
                project.Directional.SetInactiveSet(facing, new List<AnimationState> { animation });
                pixelSeed++;
            }

            string path = persistence.SaveProject("Linked", project, CancellationToken.None);
            EFYVProject restored = persistence.LoadProject("Linked");
            Require(restored.IsDirectional);
            Require(restored.Directional.ActiveFacing == SharedConfig.FacingDown);
            Require((string)restored.AssetProperties[SharedConfig.FacingField] == SharedConfig.FacingDown);
            Require(restored.Animations.Count == 1 && restored.Animations[0].StateName == "IdleDown");
            Require(restored.Animations[0].Frames[0].Layers[0].GetPixel(2, 3).G == 34);
            pixelSeed = 1;
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (facing == SharedConfig.FacingDown) continue;
                IReadOnlyList<AnimationState> animations = restored.GetFacingAnimations(facing);
                Require(animations.Count == 1);
                Require(animations[0].StateName == "Idle" + facing && animations[0].FPS == 5);
                Require(animations[0].Frames[0].Layers[0].GetPixel(pixelSeed, 0).R == pixelSeed);
                pixelSeed++;
            }

            JsonObject baseline = JsonNode.Parse(File.ReadAllText(path)).AsObject();

            // The directional section's active facing is authoritative over a
            // hand-edited facing property.
            JsonObject tampered = baseline.DeepClone().AsObject();
            tampered["assetProperties"].AsObject()[SharedConfig.FacingField] = SharedConfig.FacingUp;
            File.WriteAllText(path, tampered.ToJsonString());
            EFYVProject resynced = persistence.LoadProject("Linked");
            Require((string)resynced.AssetProperties[SharedConfig.FacingField] == SharedConfig.FacingDown);

            // A document without the section is a LEGAL plain document.
            JsonObject legacy = baseline.DeepClone().AsObject();
            legacy.Remove("directional");
            File.WriteAllText(path, legacy.ToJsonString());
            EFYVProject plain = persistence.LoadProject("Linked");
            Require(!plain.IsDirectional && plain.Animations.Count == 1);
            legacy = baseline.DeepClone().AsObject();
            legacy["directional"] = null;
            File.WriteAllText(path, legacy.ToJsonString());
            Require(!persistence.LoadProject("Linked").IsDirectional);

            // Malformed directional sections reject with the persistence
            // contract's InvalidDataException.
            Action<JsonObject>[] mutations =
            {
                document => document["directional"].AsObject()["activeFacing"] = "Diagonal",
                document => document["directional"].AsObject()["activeFacing"] = null,
                document => document["directional"].AsObject()["down"] = new JsonArray(),
                document => document["directional"].AsObject()["up"] = null,
                document => document["directional"].AsObject().Remove("left")
            };
            foreach (Action<JsonObject> mutate in mutations)
            {
                JsonObject malformed = baseline.DeepClone().AsObject();
                mutate(malformed);
                File.WriteAllText(path, malformed.ToJsonString());
                RequireThrows<InvalidDataException>(() => persistence.LoadProject("Linked"));
            }

            // Restore the valid bytes, then verify the save gate rejects a
            // null entry inside a parked set.
            File.WriteAllText(path, baseline.ToJsonString());
            project.Directional.GetInactiveSet(SharedConfig.FacingUp).Add(null);
            RequireThrows<InvalidDataException>(
                () => persistence.SaveProject("Linked", project, CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestDirectionalExportAllFacings()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            EFYVProject project = BuildCompleteDirectionalProject(schema, root, "QuadWalker");
            var engine = new ExportEngine(new ProjectValidator(schema));

            // ONE export publishes all four facings; the returned pair is the
            // ACTIVE facing's.
            ExportResult active = engine.Export(project, CancellationToken.None);
            string rawArt = Path.Combine(root, Config.Export.DirAssets, Config.Export.DirRawArt);
            Require(active.MetadataPath == Path.Combine(
                rawArt, "QuadWalker" + Config.Entity.FileSuffixDown + BackendConfig.Exporter.EfyvExtension));
            foreach ((string Facing, string Suffix) pair in new[]
            {
                (SharedConfig.FacingUp, Config.Entity.FileSuffixUp),
                (SharedConfig.FacingDown, Config.Entity.FileSuffixDown),
                (SharedConfig.FacingLeft, Config.Entity.FileSuffixLeft),
                (SharedConfig.FacingRight, Config.Entity.FileSuffixRight)
            })
            {
                string metadataPath = Path.Combine(
                    rawArt, "QuadWalker" + pair.Suffix + BackendConfig.Exporter.EfyvExtension);
                string imagePath = Path.Combine(
                    rawArt, "QuadWalker" + pair.Suffix + BackendConfig.Exporter.PngExtension);
                Require(File.Exists(metadataPath) && File.Exists(imagePath));
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath)))
                {
                    JsonElement properties = document.RootElement.GetProperty(
                        BackendConfig.Exporter.FieldProperties);
                    Require(properties.GetProperty(SharedConfig.FacingField).GetString() == pair.Facing);
                    Require(document.RootElement.GetProperty(BackendConfig.Exporter.FieldAtlas)
                        .GetProperty(BackendConfig.Exporter.FieldAnimations)[0]
                        .GetProperty(BackendConfig.Exporter.FieldName).GetString() == "Idle" + pair.Facing);
                }
            }

            // ExportAllFacings returns per-facing results, catalog order with
            // the active facing LAST.
            IReadOnlyList<ExportResult> results = engine.ExportAllFacings(project, CancellationToken.None);
            Require(results.Count == Config.Entity.DirectionalVariantCount);
            Require(results[0].MetadataPath.EndsWith(
                Config.Entity.FileSuffixUp + BackendConfig.Exporter.EfyvExtension, StringComparison.Ordinal));
            Require(results[1].MetadataPath.EndsWith(
                Config.Entity.FileSuffixLeft + BackendConfig.Exporter.EfyvExtension, StringComparison.Ordinal));
            Require(results[2].MetadataPath.EndsWith(
                Config.Entity.FileSuffixRight + BackendConfig.Exporter.EfyvExtension, StringComparison.Ordinal));
            Require(results[3].MetadataPath == active.MetadataPath);

            // Plain projects cannot use the all-facings API; incomplete
            // directional projects fail export validation with the new code.
            RequireThrows<InvalidOperationException>(
                () => engine.ExportAllFacings(CreateValidProject(root, 1), CancellationToken.None));
            RequireThrows<ArgumentNullException>(
                () => engine.ExportAllFacings(null, CancellationToken.None));
            var toolbar = new ToolbarAPI(schema);
            EFYVProject incomplete = toolbar.CreateNewLinkedDirectionalProject(Config.Types.AssetTypeEnemyData);
            incomplete.UnityProjectPath = root;
            incomplete.AssetProperties[SharedConfig.EntityNameField] = "Incomplete";
            var idle = new AnimationState("Idle", 4);
            idle.Frames.Add(new Frame(incomplete.CanvasWidth, incomplete.CanvasHeight, 0));
            incomplete.Animations.Add(idle);
            try
            {
                engine.Export(incomplete, CancellationToken.None);
                Require(false);
            }
            catch (ProjectValidationException exception)
            {
                Require(ContainsIssue(exception.Validation, ProjectIssueCode.DirectionalFacingIncomplete));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task TestDirectionalLiveDebugPublishesAllFacings()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            EFYVProject project = BuildCompleteDirectionalProject(schema, root, "LivePush");
            var validator = new ProjectValidator(schema);
            var scheduler = new ManualScheduler();
            using (var live = new LiveDebugController(
                new ExportEngine(validator),
                validator,
                scheduler,
                TimeSpan.FromSeconds(1),
                null))
            {
                live.StartWatching();
                live.NotifyProjectChanged(project);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount > 0, 2000));
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Succeeded);
                // The reported pair is the ACTIVE facing's publish; all four
                // suffixed pairs are on disk from the single push.
                Require(live.Current.Export.MetadataPath.EndsWith(
                    Config.Entity.FileSuffixDown + BackendConfig.Exporter.EfyvExtension,
                    StringComparison.Ordinal));
                string rawArt = Path.Combine(root, Config.Export.DirAssets, Config.Export.DirRawArt);
                foreach (string suffix in new[]
                {
                    Config.Entity.FileSuffixUp,
                    Config.Entity.FileSuffixDown,
                    Config.Entity.FileSuffixLeft,
                    Config.Entity.FileSuffixRight
                })
                {
                    Require(File.Exists(Path.Combine(
                        rawArt, "LivePush" + suffix + BackendConfig.Exporter.EfyvExtension)));
                    Require(File.Exists(Path.Combine(
                        rawArt, "LivePush" + suffix + BackendConfig.Exporter.PngExtension)));
                }

                // An incomplete directional project fails validation before any
                // snapshot/export work happens.
                project.Directional.SetInactiveSet(SharedConfig.FacingUp, new List<AnimationState>());
                live.NotifyProjectChanged(project);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount > 0, 2000));
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.ValidationFailed);
                Require(ContainsIssue(
                    live.Current.Validation, ProjectIssueCode.DirectionalFacingIncomplete));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // A linked directional enemy project with one distinctly named, distinctly
    // painted single-frame animation per facing (active = Down).
    private static EFYVProject BuildCompleteDirectionalProject(
        AssetSchemaService schema,
        string unityRoot,
        string entityName)
    {
        var toolbar = new ToolbarAPI(schema);
        EFYVProject project = toolbar.CreateNewLinkedDirectionalProject(Config.Types.AssetTypeEnemyData);
        project.UnityProjectPath = unityRoot;
        project.AssetProperties[SharedConfig.EntityNameField] = entityName;
        byte seed = 10;
        foreach (string facing in Config.Schema.FacingChoices)
        {
            var animation = new AnimationState("Idle" + facing, 4);
            var frame = new Frame(project.CanvasWidth, project.CanvasHeight, 0);
            frame.Layers[0].SetPixel(0, 0, Color(seed, seed, seed, 255));
            animation.Frames.Add(frame);
            if (facing == project.Directional.ActiveFacing)
                project.Animations.Add(animation);
            else
                project.Directional.SetInactiveSet(facing, new List<AnimationState> { animation });
            seed += 10;
        }
        return project;
    }

    private static void TestSchemaCustomFieldRegistrationMatrix()
    {
        var schema = new AssetSchemaService();
        int baseFieldCount = schema.GetTypeDefinition(Config.Types.AssetTypeEnemyData).Fields.Count;

        // Success: three custom slots land on the derived type with designer
        // defaults, no range, and the kind's field type; base inheritance
        // (including IsDirectional) is untouched.
        Require(schema.RegisterAssetType(
            new AssetSchemaRegistration("SparkleEnemyData", "Sparkle Enemy", Config.Types.AssetTypeEnemyData),
            new[]
            {
                new CustomFieldRegistration("sparkleFactor", SchemaValueKind.Float),
                new CustomFieldRegistration("comboCount", SchemaValueKind.Integer),
                new CustomFieldRegistration("glowLabel", SchemaValueKind.Text)
            }));
        SchemaDefinition sparkle = schema.GetTypeDefinition("SparkleEnemyData");
        Require(sparkle != null && sparkle.IsDirectional);
        Require(sparkle.Fields.Count == baseFieldCount + 3);
        SchemaField floatField = FindField(sparkle, "sparkleFactor");
        Require(floatField.ValueKind == SchemaValueKind.Float);
        Require(floatField.FieldType == Config.Types.FloatSingle);
        Require(floatField.DisplayLabel == "sparkleFactor");
        Require((float)floatField.DefaultValue == Config.Types.DefaultFloat);
        Require(!floatField.HasRange && !floatField.IsRequired && !floatField.IsReadOnly);
        Require(floatField.Choices.Count == 0);
        SchemaField intField = FindField(sparkle, "comboCount");
        Require(intField.ValueKind == SchemaValueKind.Integer &&
            intField.FieldType == Config.Types.Int32 &&
            (int)intField.DefaultValue == Config.Types.DefaultInt);
        SchemaField textField = FindField(sparkle, "glowLabel");
        Require(textField.ValueKind == SchemaValueKind.Text &&
            textField.FieldType == Config.Types.StringUpper &&
            (string)textField.DefaultValue == Config.Types.DefaultString);

        // The zero-fields overload stays equivalent to the original API.
        Require(schema.RegisterAssetType(
            new AssetSchemaRegistration("PlainDerivedData", "Plain Derived", Config.Types.AssetTypeEnemyData)));
        Require(schema.GetTypeDefinition("PlainDerivedData").Fields.Count == baseFieldCount);
        RequireThrows<ArgumentNullException>(() => schema.RegisterAssetType(
            new AssetSchemaRegistration("NullFieldsData", "Null Fields", Config.Types.AssetTypeEnemyData),
            null));

        // The designer property surface speaks the custom fields natively.
        var toolbar = new ToolbarAPI(schema);
        EFYVProject project = toolbar.CreateNewProject("Sparkle Enemy" + Config.Entity.SuffixDown);
        Require(project != null);
        Require(toolbar.TrySetProperty(project, "sparkleFactor", 2.5f).Succeeded);
        Require(toolbar.TrySetProperty(project, "comboCount", 7).Succeeded);
        Require(toolbar.TrySetProperty(project, "glowLabel", "violet").Succeeded);
        Require(toolbar.TrySetProperty(project, "sparkleFactor", "words").Status ==
            PropertyEditStatus.InvalidValue);
        Require(toolbar.TrySetProperty(project, "comboCount", 1.5f).Status ==
            PropertyEditStatus.InvalidValue);
        Require(toolbar.TrySetProperty(project, "glowLabel", 3).Status ==
            PropertyEditStatus.InvalidValue);
        bool sawCustom = false;
        foreach (DesignerProperty property in toolbar.GetEditableProperties(project))
        {
            if (property.FieldName != "sparkleFactor") continue;
            sawCustom = true;
            Require((float)property.Value == 2.5f);
        }
        Require(sawCustom);

        // Rejection matrix: every invalid custom field rejects the WHOLE
        // registration (nothing lands).
        (string Name, SchemaValueKind Kind)[] rejected =
        {
            (null, SchemaValueKind.Float),
            ("", SchemaValueKind.Float),
            ("   ", SchemaValueKind.Float),
            (new string('a', Config.Schema.MaxCustomFieldNameLength + 1), SchemaValueKind.Float),
            (SharedConfig.MaxHealthField, SchemaValueKind.Float),
            (SharedConfig.IsWalkableField, SchemaValueKind.Integer),
            (SharedConfig.AssetNameField, SchemaValueKind.Text),
            (SharedConfig.AssetTypeField, SchemaValueKind.Text),
            ("plainBad", SchemaValueKind.Unknown)
        };
        foreach ((string Name, SchemaValueKind Kind) candidate in rejected)
        {
            Require(!schema.RegisterAssetType(
                new AssetSchemaRegistration("FailData", "Fail", Config.Types.AssetTypeEnemyData),
                new[] { new CustomFieldRegistration(candidate.Name, candidate.Kind) }));
            SchemaDefinition definition;
            Require(!schema.TryGetTypeDefinition("FailData", out definition));
        }
        Require(!schema.RegisterAssetType(
            new AssetSchemaRegistration("FailData", "Fail", Config.Types.AssetTypeEnemyData),
            new[]
            {
                new CustomFieldRegistration("twin", SchemaValueKind.Float),
                new CustomFieldRegistration("twin", SchemaValueKind.Integer)
            }));
        // The facing key is inherited on directional types AND reserved as a
        // routing key on non-directional ones - both paths reject it.
        Require(!schema.RegisterAssetType(
            new AssetSchemaRegistration("FailData", "Fail", Config.Types.AssetTypeEnemyData),
            new[] { new CustomFieldRegistration(SharedConfig.FacingField, SchemaValueKind.Text) }));
        Require(!schema.RegisterAssetType(
            new AssetSchemaRegistration("FailData", "Fail", Config.Types.AssetTypeGameAssetData),
            new[] { new CustomFieldRegistration(SharedConfig.FacingField, SchemaValueKind.Text) }));

        // Name-length boundary and the per-type count cap.
        Require(schema.RegisterAssetType(
            new AssetSchemaRegistration("LongNameData", "Long Name", Config.Types.AssetTypeEnemyData),
            new[]
            {
                new CustomFieldRegistration(
                    new string('a', Config.Schema.MaxCustomFieldNameLength), SchemaValueKind.Text)
            }));
        var capFields = new List<CustomFieldRegistration>();
        for (int index = 0; index < Config.Schema.MaxCustomFieldsPerType; index++)
            capFields.Add(new CustomFieldRegistration("custom" + index, SchemaValueKind.Float));
        Require(schema.RegisterAssetType(
            new AssetSchemaRegistration("AtCapData", "At Cap", Config.Types.AssetTypeEnemyData),
            capFields));
        Require(schema.GetTypeDefinition("AtCapData").Fields.Count ==
            baseFieldCount + Config.Schema.MaxCustomFieldsPerType);
        capFields.Add(new CustomFieldRegistration("overflow", SchemaValueKind.Float));
        Require(!schema.RegisterAssetType(
            new AssetSchemaRegistration("OverCapData", "Over Cap", Config.Types.AssetTypeEnemyData),
            capFields));
        SchemaDefinition overCap;
        Require(!schema.TryGetTypeDefinition("OverCapData", out overCap));
    }

    private static void TestCustomFieldsExportFlow()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            Require(schema.RegisterAssetType(
                new AssetSchemaRegistration("SparkleEnemyData", "Sparkle Enemy", Config.Types.AssetTypeEnemyData),
                new[]
                {
                    new CustomFieldRegistration("sparkleFactor", SchemaValueKind.Float),
                    new CustomFieldRegistration("comboCount", SchemaValueKind.Integer),
                    new CustomFieldRegistration("glowLabel", SchemaValueKind.Text)
                }));
            var toolbar = new ToolbarAPI(schema);
            var engine = new ExportEngine(new ProjectValidator(schema));

            // Plain single-facing export: the custom values ride the
            // .efyvlaby properties object as ordinary keys.
            EFYVProject project = toolbar.CreateNewProject("Sparkle Enemy" + Config.Entity.SuffixDown);
            project.UnityProjectPath = root;
            project.AssetProperties[SharedConfig.EntityNameField] = "Sparkler";
            var idle = new AnimationState("Idle", 4);
            idle.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, 0));
            project.Animations.Add(idle);
            Require(toolbar.TrySetProperty(project, "sparkleFactor", 2.5f).Succeeded);
            Require(toolbar.TrySetProperty(project, "comboCount", 7).Succeeded);
            Require(toolbar.TrySetProperty(project, "glowLabel", "violet").Succeeded);

            ExportResult result = engine.Export(project, CancellationToken.None);
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(result.MetadataPath)))
            {
                JsonElement properties = document.RootElement.GetProperty(
                    BackendConfig.Exporter.FieldProperties);
                Require(properties.GetProperty("sparkleFactor").GetSingle() == 2.5f);
                Require(properties.GetProperty("comboCount").GetInt32() == 7);
                Require(properties.GetProperty("glowLabel").GetString() == "violet");
                Require(properties.GetProperty(BackendConfig.Exporter.FieldEntityName).GetString() ==
                    "Sparkler");
            }

            // Linked directional export of a custom type: every facing's
            // .efyvlaby carries the shared custom values.
            EFYVProject linked = toolbar.CreateNewLinkedDirectionalProject("SparkleEnemyData");
            Require(linked != null);
            linked.UnityProjectPath = root;
            linked.AssetProperties[SharedConfig.EntityNameField] = "SparkleLinked";
            foreach (string facing in Config.Schema.FacingChoices)
            {
                var animation = new AnimationState("Idle" + facing, 4);
                animation.Frames.Add(new Frame(linked.CanvasWidth, linked.CanvasHeight, 0));
                if (facing == linked.Directional.ActiveFacing)
                    linked.Animations.Add(animation);
                else
                    linked.Directional.SetInactiveSet(facing, new List<AnimationState> { animation });
            }
            Require(toolbar.TrySetProperty(linked, "sparkleFactor", 1.5f).Succeeded);

            IReadOnlyList<ExportResult> facings = engine.ExportAllFacings(linked, CancellationToken.None);
            Require(facings.Count == Config.Entity.DirectionalVariantCount);
            foreach (ExportResult facingResult in facings)
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(facingResult.MetadataPath)))
                {
                    JsonElement properties = document.RootElement.GetProperty(
                        BackendConfig.Exporter.FieldProperties);
                    Require(properties.GetProperty("sparkleFactor").GetSingle() == 1.5f);
                    // The routed base type still travels for importer fallback.
                    Require(document.RootElement.GetProperty(BackendConfig.Exporter.FieldBaseAssetType)
                        .GetString() == Config.Types.AssetTypeEnemyData);
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
}
