// batch3.4 agent (item #7): effects authoring - the immutable EffectDescriptor
// model, undoable session effect CRUD, the .efyvmake effects section (round
// trip + legacy + malformed corpus), the .efyvlaby export flow, and the
// destructive layer filters (blur/outline/glow/color-shift) through the sparse
// FrameEditCommand undo path with selection-region masking.
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
    private static EffectDescriptor EffectsFlash(string trigger = SharedConfig.EffectTriggerOnDamaged)
    {
        return new EffectDescriptor(
            Config.Effect.TypeFlash, "HurtFlash", trigger, 0xFF0000FFu, 150, 0.8f);
    }

    // ------------------------------------------------------------------
    // EffectDescriptor model: validation gate, clone/snapshot sharing.
    // ------------------------------------------------------------------
    private static void TestEffectDescriptorModelAndCloneSharing()
    {
        // The constructor is the single validation gate.
        EffectDescriptor flash = EffectsFlash();
        Require(flash.EffectType == BackendConfig.Exporter.EffectTypeFlash);
        Require(flash.Trigger == SharedConfig.EffectTriggerOnDamaged);
        Require(flash.ColorRgba == 0xFF0000FFu);
        Require(flash.DurationMs == 150);
        Require(flash.Strength == 0.8f);

        // Name is optional for flash/tint (null normalizes to empty)...
        EffectDescriptor unnamed = new EffectDescriptor(
            Config.Effect.TypeTint, null, SharedConfig.EffectTriggerOnSpawn, 0u, 0, 1f);
        Require(unnamed.Name == Config.Common.EmptyString);
        // ...but REQUIRED for particleHook (it names the particle system).
        RequireThrows<ArgumentException>(() => new EffectDescriptor(
            Config.Effect.TypeParticleHook, " ", "OnLand", 0u, 0, 1f));
        EffectDescriptor hook = new EffectDescriptor(
            Config.Effect.TypeParticleHook, "DustPuff", "OnLand", 0u, 0, 0f);
        Require(hook.Name == "DustPuff");

        // Rejection matrix.
        RequireThrows<ArgumentException>(() => new EffectDescriptor(
            "sparkle", "x", "OnSpawn", 0u, 0, 1f));
        RequireThrows<ArgumentException>(() => new EffectDescriptor(
            "Flash", "x", "OnSpawn", 0u, 0, 1f)); // case-sensitive
        RequireThrows<ArgumentException>(() => new EffectDescriptor(
            null, "x", "OnSpawn", 0u, 0, 1f));
        RequireThrows<ArgumentException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, "x", null, 0u, 0, 1f));
        RequireThrows<ArgumentException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, "x", "   ", 0u, 0, 1f));
        RequireThrows<ArgumentException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, "x", new string('t', Config.Effect.MaxTriggerLength + 1), 0u, 0, 1f));
        RequireThrows<ArgumentException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, new string('n', Config.Effect.MaxNameLength + 1), "OnSpawn", 0u, 0, 1f));
        RequireThrows<ArgumentOutOfRangeException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, "x", "OnSpawn", 0u, Config.Effect.MinDurationMs - 1, 1f));
        RequireThrows<ArgumentOutOfRangeException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, "x", "OnSpawn", 0u, Config.Effect.MaxDurationMs + 1, 1f));
        RequireThrows<ArgumentOutOfRangeException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, "x", "OnSpawn", 0u, 0, float.NaN));
        RequireThrows<ArgumentOutOfRangeException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, "x", "OnSpawn", 0u, 0, Config.Effect.MaxStrength + 0.01f));
        RequireThrows<ArgumentOutOfRangeException>(() => new EffectDescriptor(
            Config.Effect.TypeFlash, "x", "OnSpawn", 0u, 0, Config.Effect.MinStrength - 0.01f));

        // Boundary values are legal.
        _ = new EffectDescriptor(
            Config.Effect.TypeFlash,
            new string('n', Config.Effect.MaxNameLength),
            new string('t', Config.Effect.MaxTriggerLength),
            uint.MaxValue,
            Config.Effect.MaxDurationMs,
            Config.Effect.MaxStrength);
        _ = new EffectDescriptor(
            Config.Effect.TypeTint, null, "T", 0u,
            Config.Effect.MinDurationMs, Config.Effect.MinStrength);

        // IsKnownEffectType mirrors the wire strings.
        Require(EffectDescriptor.IsKnownEffectType(Config.Effect.TypeFlash));
        Require(EffectDescriptor.IsKnownEffectType(Config.Effect.TypeTint));
        Require(EffectDescriptor.IsKnownEffectType(Config.Effect.TypeParticleHook));
        Require(!EffectDescriptor.IsKnownEffectType(null));
        Require(!EffectDescriptor.IsKnownEffectType("FLASH"));

        // AnimationState: default empty, clone shares the immutable instances
        // but the LISTS are isolated.
        var animation = new AnimationState("FxProbe", 8);
        Require(animation.Effects.Count == 0);
        animation.Effects.Add(flash);
        animation.Effects.Add(hook);
        AnimationState clone = animation.Clone();
        Require(clone.Effects.Count == 2);
        Require(ReferenceEquals(clone.Effects[0], flash));
        clone.Effects.RemoveAt(0);
        Require(animation.Effects.Count == 2); // isolated list

        // Snapshot capture copies the list (immutable entries shared).
        var project = new EFYVProject(Config.Types.AssetTypeEnemyData);
        animation.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, 0));
        project.Animations.Add(animation);
        ProjectSnapshot snapshot = ProjectSnapshot.Capture(project);
        Require(snapshot.Animations[0].Effects.Count == 2);
        Require(ReferenceEquals(snapshot.Animations[0].Effects[0], flash));
        animation.Effects.Clear();
        Require(snapshot.Animations[0].Effects.Count == 2); // snapshot unaffected

        // Validator: null entries and the count cap are the only model-level
        // failure modes (instances are always valid).
        var validator = new ProjectValidator(new AssetSchemaService());
        animation.Effects.Add(null);
        ProjectValidationResult result = validator.Validate(project, ProjectValidationScope.Structural);
        bool sawInvalid = false;
        foreach (ProjectIssue issue in result.Issues)
            if (issue.Code == ProjectIssueCode.InvalidEffectDescriptor) sawInvalid = true;
        Require(sawInvalid);
    }

    // ------------------------------------------------------------------
    // Session effect CRUD: undoable, capped, index-validated.
    // ------------------------------------------------------------------
    private static void TestSessionEffectCrudUndoRedo()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("EffectCrud", project, root))
            {
                session.AutosaveEnabled = false;
                List<EffectDescriptor> effects = project.Animations[0].Effects;

                EffectDescriptor flash = EffectsFlash();
                session.AddAnimationEffect(0, flash);
                Require(effects.Count == 1 && ReferenceEquals(effects[0], flash));
                Require(session.Current.IsDirty);
                Require(session.History.Current.UndoCount == 1);
                Require(session.Undo());
                Require(effects.Count == 0);
                Require(session.Redo());
                Require(effects.Count == 1);

                EffectDescriptor tint = new EffectDescriptor(
                    Config.Effect.TypeTint, "SpawnTint", SharedConfig.EffectTriggerOnSpawn,
                    0xFF00FF00u, 0, 0.25f);
                session.AddAnimationEffect(0, tint);
                Require(effects.Count == 2 && ReferenceEquals(effects[1], tint));

                // Replace is one undoable step; replacing with the SAME
                // reference is a complete no-op.
                int historyBefore = session.History.Current.UndoCount;
                session.ReplaceAnimationEffect(0, 1, tint);
                Require(session.History.Current.UndoCount == historyBefore);
                EffectDescriptor strongerTint = new EffectDescriptor(
                    Config.Effect.TypeTint, "SpawnTint", SharedConfig.EffectTriggerOnSpawn,
                    0xFF00FF00u, 0, 0.75f);
                session.ReplaceAnimationEffect(0, 1, strongerTint);
                Require(ReferenceEquals(effects[1], strongerTint));
                Require(session.Undo());
                Require(ReferenceEquals(effects[1], tint));
                Require(session.Redo());
                Require(ReferenceEquals(effects[1], strongerTint));

                // Remove restores at the same index on undo.
                session.RemoveAnimationEffect(0, 0);
                Require(effects.Count == 1 && ReferenceEquals(effects[0], strongerTint));
                Require(session.Undo());
                Require(effects.Count == 2 && ReferenceEquals(effects[0], flash));

                // Guards.
                RequireThrows<ArgumentNullException>(() => session.AddAnimationEffect(0, null));
                RequireThrows<ArgumentOutOfRangeException>(() => session.AddAnimationEffect(1, flash));
                RequireThrows<ArgumentOutOfRangeException>(() => session.AddAnimationEffect(-1, flash));
                RequireThrows<ArgumentOutOfRangeException>(() => session.RemoveAnimationEffect(0, 2));
                RequireThrows<ArgumentOutOfRangeException>(() => session.RemoveAnimationEffect(0, -1));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ReplaceAnimationEffect(0, 5, flash));
                RequireThrows<ArgumentNullException>(
                    () => session.ReplaceAnimationEffect(0, 0, null));

                // The per-animation cap is enforced at the session gate.
                while (effects.Count < Config.Effect.MaxEffectsPerAnimation)
                    session.AddAnimationEffect(0, EffectsFlash());
                RequireThrows<InvalidOperationException>(
                    () => session.AddAnimationEffect(0, EffectsFlash()));
                Require(effects.Count == Config.Effect.MaxEffectsPerAnimation);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // .efyvmake effects section: round trip, legacy, malformed corpus.
    // ------------------------------------------------------------------
    private static void TestEffectsPersistenceRoundTripAndLegacy()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root);
            EFYVProject project = CreateValidProject(root, 1);
            project.Animations[0].Effects.Add(EffectsFlash());
            project.Animations[0].Effects.Add(new EffectDescriptor(
                Config.Effect.TypeParticleHook, "DustPuff", "OnLand", 0xAABBCCDDu, 40, 0.5f));
            persistence.SaveProject("fx", project, CancellationToken.None);

            EFYVProject loaded = persistence.LoadProject("fx");
            List<EffectDescriptor> effects = loaded.Animations[0].Effects;
            Require(effects.Count == 2);
            Require(effects[0].EffectType == Config.Effect.TypeFlash);
            Require(effects[0].Name == "HurtFlash");
            Require(effects[0].Trigger == SharedConfig.EffectTriggerOnDamaged);
            Require(effects[0].ColorRgba == 0xFF0000FFu);
            Require(effects[0].DurationMs == 150);
            Require(effects[0].Strength == 0.8f);
            Require(effects[1].EffectType == Config.Effect.TypeParticleHook);
            Require(effects[1].Name == "DustPuff");
            Require(effects[1].ColorRgba == 0xAABBCCDDu);

            // Legacy document: stripping the effects members entirely restores
            // to the empty effect list (null section is LEGAL).
            string path = persistence.GetProjectPath("fx");
            JsonNode legacyDocument = JsonNode.Parse(File.ReadAllText(path));
            foreach (JsonNode animation in legacyDocument["animations"].AsArray())
                animation.AsObject().Remove("effects");
            File.WriteAllText(persistence.GetProjectPath("legacyfx"), legacyDocument.ToJsonString());
            EFYVProject legacy = persistence.LoadProject("legacyfx");
            Require(legacy.Animations[0].Effects.Count == 0);

            // Malformed corpus: every mutation must reject the whole load.
            (string Field, JsonNode Value)[] corpus =
            {
                ("effectType", "sparkle"),
                ("effectType", null),
                ("trigger", "  "),
                ("durationMs", -1),
                ("durationMs", Config.Effect.MaxDurationMs + 1),
                ("strength", 1.5),
                ("strength", -0.5),
                ("name", new string('n', Config.Effect.MaxNameLength + 1))
            };
            for (int index = 0; index < corpus.Length; index++)
            {
                JsonNode broken = JsonNode.Parse(File.ReadAllText(path));
                broken["animations"][0]["effects"][0][corpus[index].Field] = corpus[index].Value;
                string brokenName = "brokenfx" + index;
                File.WriteAllText(persistence.GetProjectPath(brokenName), broken.ToJsonString());
                RequireThrows<InvalidDataException>(() => persistence.LoadProject(brokenName));
            }

            // particleHook without a name rejects.
            JsonNode namelessHook = JsonNode.Parse(File.ReadAllText(path));
            namelessHook["animations"][0]["effects"][1]["name"] = "";
            File.WriteAllText(persistence.GetProjectPath("namelesshook"), namelessHook.ToJsonString());
            RequireThrows<InvalidDataException>(() => persistence.LoadProject("namelesshook"));

            // Over the per-animation cap rejects.
            JsonNode overCap = JsonNode.Parse(File.ReadAllText(path));
            JsonArray effectArray = overCap["animations"][0]["effects"].AsArray();
            string template = effectArray[0].ToJsonString();
            while (effectArray.Count <= Config.Effect.MaxEffectsPerAnimation)
                effectArray.Add(JsonNode.Parse(template));
            File.WriteAllText(persistence.GetProjectPath("overcapfx"), overCap.ToJsonString());
            RequireThrows<InvalidDataException>(() => persistence.LoadProject("overcapfx"));

            // The SAVE gate rejects a model holding a null effect entry.
            project.Animations[0].Effects.Add(null);
            RequireThrows<InvalidDataException>(
                () => persistence.SaveProject("nullfx", project, CancellationToken.None));
            Require(!File.Exists(persistence.GetProjectPath("nullfx")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Export flow: designer effects land in the .efyvlaby atlas block.
    // ------------------------------------------------------------------
    private static void TestEffectsExportAtlasFlow()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var engine = new ExportEngine(new ProjectValidator(new AssetSchemaService()));

            // Effect-free project: no effects member on any wire animation.
            EFYVProject plain = CreateValidProject(root, 2);
            ExportResult plainResult = engine.Export(plain, CancellationToken.None);
            EFYVJsonFormat plainDocument = FastImporter.ParseEfyvFile(plainResult.MetadataPath);
            Require(plainDocument.atlas.Value.animations[0].effects == null);
            Require(plainDocument.EffectiveDocumentVersion ==
                BackendConfig.Exporter.CurrentDocumentVersion);

            // Authored effects flow through with every field populated.
            EFYVProject authored = CreateValidProject(root, 2);
            authored.AssetProperties[SharedConfig.EntityNameField] = "FxEnemy";
            authored.Animations[0].Effects.Add(EffectsFlash());
            authored.Animations[0].Effects.Add(new EffectDescriptor(
                Config.Effect.TypeTint, "SpawnTint", SharedConfig.EffectTriggerOnSpawn,
                0xFF00FF00u, 0, 0.25f));
            authored.Animations[0].Effects.Add(new EffectDescriptor(
                Config.Effect.TypeParticleHook, "DustPuff", "OnLand", 0u, 0, 1f));
            ExportResult authoredResult = engine.Export(authored, CancellationToken.None);
            EFYVJsonFormat document = FastImporter.ParseEfyvFile(authoredResult.MetadataPath);
            List<EffectDescriptorJson> wire = document.atlas.Value.animations[0].effects;
            Require(wire.Count == 3);
            Require(wire[0].name == "HurtFlash");
            Require(wire[0].effectType == BackendConfig.Exporter.EffectTypeFlash);
            Require(wire[0].trigger == SharedConfig.EffectTriggerOnDamaged);
            Require(wire[0].colorRgba.Value == 0xFF0000FFu);
            Require(wire[0].durationMs.Value == 150);
            Require(wire[0].strength.Value == 0.8f);
            Require(wire[1].effectType == BackendConfig.Exporter.EffectTypeTint);
            Require(wire[2].effectType == BackendConfig.Exporter.EffectTypeParticleHook);
            Require(wire[2].name == "DustPuff");
            // What the engine writes always passes the shared wire validator.
            Require(EFYVBackend.Core.Export.FastExporter.TryValidateAtlasMetadata(
                document.atlas.Value, out EFYVBackend.Core.Export.AtlasMetadataError error));
            Require(error == EFYVBackend.Core.Export.AtlasMetadataError.None);

            // A null effect entry fails EXPORT validation up front.
            EFYVProject broken = CreateValidProject(root, 1);
            broken.AssetProperties[SharedConfig.EntityNameField] = "BrokenFx";
            broken.Animations[0].Effects.Add(null);
            RequireThrows<ProjectValidationException>(
                () => engine.Export(broken, CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Destructive layer filters: sparse undo, one history entry, guards.
    // ------------------------------------------------------------------
    private static void TestSessionLayerFiltersSparseUndo()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 1);
            using (DesignerSession session = DesignerSession.Create("FilterTest", project, root))
            {
                session.AutosaveEnabled = false;
                Layer layer = session.CurrentFrame.Layers[0];
                uint red = 0xFF0000FFu;
                uint blue = 0xFFFF0000u;

                // Outline: one opaque pixel grows a full 8-neighborhood rim;
                // ONE history entry; undo restores the exact bytes.
                layer.SetPixel(3, 2, new PixelColor { Rgba = red });
                uint[] beforeOutline = SnapshotPixels(layer);
                Require(session.ApplyOutlineFilter(0, blue));
                Require(session.History.Current.UndoCount == 1);
                Require(layer.GetPixel(3, 2).Rgba == red);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        Require(layer.GetPixel(3 + dx, 2 + dy).Rgba == blue);
                    }
                }
                Require(layer.GetPixel(1, 2).Rgba == 0u); // beyond 1px untouched
                Require(session.Undo());
                uint[] afterUndo = SnapshotPixels(layer);
                for (int index = 0; index < beforeOutline.Length; index++)
                    Require(beforeOutline[index] == afterUndo[index]);
                Require(session.Redo());
                Require(layer.GetPixel(2, 1).Rgba == blue);
                Require(session.Undo());

                // Blur: spreads alpha off the lone pixel; undo-exact.
                Require(session.ApplyBlurFilter(0, 1));
                Require(layer.GetPixel(2, 2).Rgba != 0u);
                Require((byte)(layer.GetPixel(3, 2).Rgba >> SharedConfig.RgbaAlphaShift) > 0);
                Require(session.Undo());

                // Glow radius 0: hard rim BEHIND the sprite - the opaque
                // source pixel survives verbatim.
                Require(session.ApplyGlowFilter(0, blue, 0));
                Require(layer.GetPixel(3, 2).Rgba == red);
                Require(layer.GetPixel(4, 2).Rgba == blue);
                Require(session.Undo());

                // Color shift: red +120 degrees = green, alpha preserved.
                Require(session.ApplyColorShiftFilter(0, 120f, 0f, 0f));
                Require(layer.GetPixel(3, 2).Rgba == 0xFF00FF00u);
                Require(session.Undo());
                Require(layer.GetPixel(3, 2).Rgba == red);

                // A filter that changes nothing records NO history entry and
                // returns false (blur cannot move a fully clean layer... use a
                // fresh layer: color shift on empty pixels).
                int historyBefore = session.History.Current.UndoCount;
                session.AddLayer("Empty");
                int emptyIndex = session.CurrentFrame.Layers.Count - 1;
                Require(!session.ApplyBlurFilter(emptyIndex, 3));
                Require(!session.ApplyColorShiftFilter(emptyIndex, 42f, 0.5f, -0.5f));
                Require(!session.ApplyOutlineFilter(emptyIndex, blue));
                Require(!session.ApplyGlowFilter(emptyIndex, blue, 2));
                Require(session.History.Current.UndoCount == historyBefore + 1); // only AddLayer

                // Parameter guards (session-level bounds).
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ApplyBlurFilter(0, Config.Filter.MinBlurRadius - 1));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ApplyBlurFilter(0, Config.Filter.MaxBlurRadius + 1));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ApplyGlowFilter(0, blue, Config.Filter.MinGlowRadius - 1));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ApplyGlowFilter(0, blue, Config.Filter.MaxGlowRadius + 1));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ApplyColorShiftFilter(0, float.NaN, 0f, 0f));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ApplyColorShiftFilter(0, 0f, Config.Filter.MaxColorComponentDelta + 0.1f, 0f));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ApplyColorShiftFilter(0, 0f, 0f, Config.Filter.MinColorComponentDelta - 0.1f));
                RequireThrows<ArgumentOutOfRangeException>(() => session.ApplyOutlineFilter(9, blue));
                RequireThrows<ArgumentOutOfRangeException>(() => session.ApplyBlurFilter(-1, 1));

                // Filters refuse to run inside a pointer gesture.
                var pencil = new PencilTool { CurrentColor = new PixelColor { Rgba = red } };
                session.ActiveTool = pencil;
                Require(session.PointerDown(0, 0));
                RequireThrows<InvalidOperationException>(() => session.ApplyBlurFilter(0, 1));
                session.CancelGesture();
            }

            // No current frame (empty project) throws.
            EFYVProject frameless = CreatePixelToolsProject(root, 8, 6, 0);
            using (DesignerSession session = DesignerSession.Create("FilterNoFrame", frameless, root))
            {
                RequireThrows<InvalidOperationException>(() => session.ApplyBlurFilter(0, 1));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Filters + selection region mask + floating cancel + filter catalog.
    // ------------------------------------------------------------------
    private static void TestSessionFilterSelectionMaskAndCatalog()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 1);
            using (DesignerSession session = DesignerSession.Create("FilterMask", project, root))
            {
                session.AutosaveEnabled = false;
                Layer layer = session.CurrentFrame.Layers[0];
                uint red = 0xFF0000FFu;
                for (int y = 0; y < layer.Height; y++)
                    for (int x = 0; x < layer.Width; x++)
                        layer.SetPixel(x, y, new PixelColor { Rgba = red });

                // Rect-select (1,1)..(3,2), then color-shift: ONLY the masked
                // pixels turn green; the region survives the filter.
                var rectSelect = new RectSelectTool();
                session.ActiveTool = rectSelect;
                Require(session.PointerDown(1, 1));
                Require(session.PointerDrag(3, 2));
                session.PointerUp(3, 2);
                Require(session.Selection != null);
                SelectionRegion regionBefore = session.Selection;

                Require(session.ApplyColorShiftFilter(0, 120f, 0f, 0f));
                Require(ReferenceEquals(session.Selection, regionBefore));
                for (int y = 0; y < layer.Height; y++)
                {
                    for (int x = 0; x < layer.Width; x++)
                    {
                        bool inside = x >= 1 && x <= 3 && y >= 1 && y <= 2;
                        Require(layer.GetPixel(x, y).Rgba == (inside ? 0xFF00FF00u : red));
                    }
                }

                // The masked filter is ONE sparse history entry (6 pixels).
                Require(session.Undo());
                for (int y = 0; y < layer.Height; y++)
                    for (int x = 0; x < layer.Width; x++)
                        Require(layer.GetPixel(x, y).Rgba == red);

                // Floating buffer: a filter CANCELS the un-anchored lift (the
                // pixels return) and then filters the true layer bytes.
                session.ActiveTool = rectSelect;
                Require(session.PointerDown(0, 0));
                Require(session.PointerDrag(2, 2));
                session.PointerUp(2, 2);
                Require(session.LiftSelection(0, true));
                Require(session.Floating != null);
                Require(layer.GetPixel(0, 0).Rgba == 0u); // lifted = hole
                Require(session.ApplyColorShiftFilter(0, 120f, 0f, 0f));
                Require(session.Floating == null);
                // The hole was restored BEFORE filtering, so every pixel
                // (including the previously lifted ones) is green now.
                for (int y = 0; y < layer.Height; y++)
                    for (int x = 0; x < layer.Width; x++)
                        Require(layer.GetPixel(x, y).Rgba == 0xFF00FF00u);

                // Blur masked by a selection leaves everything outside the
                // region byte-identical even though sampling crossed it.
                session.ActiveTool = rectSelect;
                Require(session.PointerDown(4, 3));
                Require(session.PointerDrag(6, 4));
                session.PointerUp(6, 4);
                layer.SetPixel(7, 5, new PixelColor { Rgba = 0u }); // contrast next to the region
                uint[] beforeBlur = SnapshotPixels(layer);
                Require(session.ApplyBlurFilter(0, 2));
                for (int y = 0; y < layer.Height; y++)
                {
                    for (int x = 0; x < layer.Width; x++)
                    {
                        bool inside = x >= 4 && x <= 6 && y >= 3 && y <= 4;
                        if (!inside)
                            Require(layer.GetPixel(x, y).Rgba == beforeBlur[y * layer.Width + x]);
                    }
                }
                Require(layer.GetPixel(6, 4).Rgba != beforeBlur[4 * layer.Width + 6]); // inside changed
            }

            // ToolbarAPI filter catalog mirrors the config bounds.
            var toolbar = new ToolbarAPI(new AssetSchemaService());
            List<LayerFilterDefinition> filters = toolbar.GetLayerFilters();
            Require(filters.Count == 4);
            Require(filters[0].Kind == LayerFilterKind.Blur);
            Require(filters[0].DisplayLabel == Config.Filter.LabelBlur);
            Require(filters[0].UsesRadius && !filters[0].UsesColor && !filters[0].UsesHsvDeltas);
            Require(filters[0].MinRadius == Config.Filter.MinBlurRadius);
            Require(filters[0].MaxRadius == Config.Filter.MaxBlurRadius);
            Require(filters[1].Kind == LayerFilterKind.Outline);
            Require(!filters[1].UsesRadius && filters[1].UsesColor && !filters[1].UsesHsvDeltas);
            Require(filters[2].Kind == LayerFilterKind.Glow);
            Require(filters[2].UsesRadius && filters[2].UsesColor);
            Require(filters[2].MinRadius == Config.Filter.MinGlowRadius);
            Require(filters[2].MaxRadius == Config.Filter.MaxGlowRadius);
            Require(filters[3].Kind == LayerFilterKind.ColorShift);
            Require(!filters[3].UsesRadius && !filters[3].UsesColor && filters[3].UsesHsvDeltas);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
}
