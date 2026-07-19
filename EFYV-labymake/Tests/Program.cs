using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    private static int passed;
    private static long assertions;

    private static async Task Main()
    {
        Run(TestSchemaAndDesignerMetadata);
        Run(TestOpacityCompositing);
        Run(TestAtlasLimitValidation);
        Run(TestHitboxAndGeneratedAnimation);
        Run(TestTileMakerBounds);
        Run(TestToolDefaultsAndBrushDiameters);
        Run(TestViewportOverwritesOffCanvasPixels);
        Run(TestDesignerSessionHistory);
        Run(TestPersistenceRoundTripAndPathSafety);
        Run(TestAtomicExportAndAtlasMetadata);
        Run(TestPreviewPlayback);
        Run(TestModelContractsAndCloneIsolation);
        Run(TestPixelPackingAndRandomizedCompositing);
        Run(TestDrawingToolsExactnessAndCanaries);
        Run(TestFillAndStampAdversarialCases);
        Run(TestTileAndHitboxReferenceModels);
        Run(TestAnimationGenerationBoundaries);
        Run(TestSnapshotDeepImmutability);
        Run(TestViewportTransformAndRenderBoundaries);
        Run(TestSchemaAndToolbarAdversarialMatrix);
        Run(TestValidatorIssueMatrix);
        Run(TestCommandManagerBoundedRandomizedModel);
        Run(TestDesignerSessionCrudAndGestureRollback);
        Run(TestPreviewStateMachineExtremeTiming);
        Run(TestMapToolDeterminismAndModes);
        Run(TestPathPolicyAttackCorpus);
        Run(TestAssetBankRoundTripAndCorruption);
        Run(TestPersistenceMalformedDocumentCorpus);
        Run(TestPersistenceSnapshotsAndAtomicCancellation);
        Run(TestExportAtlasPixelsAndAdversarialPublication);
        await RunAsync(TestLiveDebugDebounceAndStop);
        await RunAsync(TestAutosaveDeferredCapture);
        await RunAsync(TestLiveDebugAdversarialStateMachine);
        await RunAsync(TestAutosaveAdversarialStateMachine);
        await RunAsync(TestDesignerSessionSaveReloadLifecycle);
        // ToolsDeepTests.cs
        Run(TestToolsPencilThickLineReferenceModel);
        Run(TestToolsFillFloodReferenceModel);
        Run(TestToolsStampBlitReferenceModel);
        Run(TestToolsHitboxGestureStateMachine);
        Run(TestToolsTileMakerWrapModelAndGuards);
        Run(TestToolsMovingToolDefaultsAndGeneratorEquivalence);
        Run(TestToolsMapToolRngReferenceAndSequencing);
        // SessionLogicDeepTests.cs
        Run(TestSessionLogicSessionConstructionAndSelection);
        Run(TestSessionLogicSessionNoOpAndIndexTracking);
        Run(TestSessionLogicHitboxGestureAndGenerateReplace);
        Run(TestSessionLogicGestureRandomizedDiffModel);
        Run(TestSessionLogicCommandInternalsAndStructuralEdges);
        Run(TestSessionLogicCommandManagerThrowingCommands);
        Run(TestSessionLogicValidatorBoundariesAndHitboxModel);
        Run(TestSessionLogicSchemaChainsAndValueKinds);
        Run(TestSessionLogicAssetBankBytesAndCorpus);
        // LiveLogicDeepTests.cs
        Run(TestLiveLogicPreviewCumulativeReferenceModel);
        Run(TestLiveLogicPreviewResidualAndEmptyStateMachine);
        Run(TestLiveLogicViewportRenderReferenceModel);
        Run(TestLiveLogicViewportTransformContracts);
        Run(TestLiveLogicToolbarCategoryOrderAndNormalization);
        Run(TestLiveLogicWalkAnimationReferenceModel);
        Run(TestLiveLogicJitterAnimationReferenceModel);
        await RunAsync(TestLiveLogicTaskDebounceSchedulerContract);
        await RunAsync(TestLiveLogicLiveDebugEventContextPath);
        await RunAsync(TestLiveLogicLiveDebugTimeAndFailurePaths);
        // batch4/item-27: live loop scope accumulation + metadata-only fast path
        await RunAsync(TestLiveLogicScopeMetadataOnlyFastPath);
        // ModelsPersistenceDeepTests.cs
        Run(TestModelsPersistenceModelContractsAndLayerReference);
        Run(TestModelsPersistenceSnapshotCompositionAndPropertyCapture);
        Run(TestModelsPersistenceDocumentBytesAndPropertyRoundTrip);
        Run(TestModelsPersistenceSaveGuardsAndFailureAtomicity);
        Run(TestModelsPersistenceExportAtlasMetadataAndPublication);
        await RunAsync(TestModelsPersistenceAutosaveContextDispatch);
        // b1-backend-png agent additions (StorageAndExportTests.cs)
        Run(TestExportGridAtlasLayoutRoundTrip);
        // batch1/labymake-session: session history robustness (#20) + structural preview scope (#30)
        Run(TestSessionLogicUndoRedoSelectionClampPolicy);
        Run(TestSessionLogicGestureRollbackContract);
        Run(TestValidatorStructuralScope);
        Run(TestSessionPreviewStructuralValidationScope);
        // batch4/item-27: validation deferred off the per-command hot path but
        // current on demand (SessionMapPreviewTests.cs).
        Run(TestSessionValidationDeferredComputeOnDemand);
        // b2-pipeline-contract agent (StorageAndExportTests.cs): .efyvlaby
        // contract (#16), snapshot atlas caps + identity reject (#16b/#36),
        // and the bounded publish retry (#12)
        Run(TestExportDocumentVersionAndBaseAssetType);
        Run(TestExportSnapshotCapsAndIdentityReject);
        Run(TestExportPublishRetryRollback);
        // batch4/item-27 (live fast path): per-artifact content-hash suppression
        // and the metadata-only publish path (StorageAndExportTests.cs).
        Run(TestExportContentHashSuppression);
        Run(TestExportMetadataOnlyFastPath);
        // batch3/pixel-tools agent (PixelToolsSelectionResizeTests.cs): item #9
        // eraser, symmetry, shape tools, selection/floating buffer, ResizeCanvas
        Run(TestPixelToolsEraserTrueTransparency);
        Run(TestPixelToolsSymmetryMirrorModes);
        Run(TestPixelToolsShapeGesturePreviewAndCommit);
        Run(TestPixelToolsSelectionRegionGeometry);
        Run(TestPixelToolsSelectionLiftMoveAnchorHistory);
        Run(TestPixelToolsClipboardAndTransientDrops);
        Run(TestPixelToolsResizeCanvasAnchorModel);
        Run(TestPixelToolsResizeCanvasGuardsAndHistory);
        // batch3/palette agent (PaletteColorWorkflowTests.cs): item #8 palette
        // model + recent ring, .efyvmake palette section, session palette CRUD,
        // composited eyedropper, global color swap, palette-constraint snap
        Run(TestPaletteModelRecentRingAndNearestMetric);
        Run(TestPalettePersistenceRoundTripAndLegacyDocuments);
        Run(TestSessionPaletteCrudUndoRedoAndRecents);
        Run(TestEyedropperCompositedPickContract);
        Run(TestColorSwapScopesSparseDiffsAndFuzz);
        Run(TestPaletteConstraintSnapOnDrawTools);
        // batch3.3 agent (AnimationWorkflowTests.cs): item #10 - per-frame
        // durations + loop/ping-pong tags (model/persistence/preview/export),
        // cross-frame layer batches, onion skinning, layer-preserving
        // generators + bob/breathe and shake/hit-flash presets
        Run(TestAnimationTimingModelAndPersistence);
        Run(TestPreviewDurationsLoopRangeAndPingPong);
        Run(TestSessionFrameDurationAndPlaybackTagCommands);
        Run(TestSessionBatchLayerOpsAcrossFrames);
        Run(TestGeneratorPresetsAndLayerPreservingMerge);
        Run(TestExportAtlasTimingMetadataFlow);
        Run(TestViewportOnionSkinComposite);
        // batch3.4 agent (EffectsAuthoringFilterTests.cs): item #7 - the
        // immutable EffectDescriptor model, undoable session effect CRUD,
        // .efyvmake effects section + malformed corpus, .efyvlaby export flow,
        // and the destructive layer filters (blur/outline/glow/color-shift)
        // through the sparse FrameEditCommand path with selection masking.
        Run(TestEffectDescriptorModelAndCloneSharing);
        Run(TestSessionEffectCrudUndoRedo);
        Run(TestEffectsPersistenceRoundTripAndLegacy);
        Run(TestEffectsExportAtlasFlow);
        Run(TestSessionLayerFiltersSparseUndo);
        Run(TestSessionFilterSelectionMaskAndCatalog);
        // batch3.5 agent (SubElementPipelineTests.cs): item #6 - SubElement
        // pivot/default transform + .efyvsub v2 (with v1 legacy reads),
        // per-frame attachments (model/session CRUD/stamp gestures),
        // .efyvmake persistence, and the flatten+metadata export flow.
        Run(TestSubElementPivotTransformAndAttachmentModel);
        Run(TestSubElementBankV2RoundTripAndLegacy);
        Run(TestSessionAttachmentCrudUndoRedo);
        Run(TestStampToolAttachmentGesturesAndBake);
        Run(TestAttachmentPersistenceRoundTripAndResize);
        Run(TestExportAttachmentFlattenAndMetadata);
        // batch3.6 agent (MapTilesetPipelineTests.cs): item #5 - tileset and
        // map sections (models + MapRenderer), undoable session tileset CRUD
        // and map editing (cells/bulk/paint strokes/MapTool gestures),
        // .efyvmake sections + malformed corpus, tile-sheet .efyvlaby export
        // with the tile-ID manifest, and .efyvmap publication round trips.
        Run(TestMapTilesetModelsAndRenderer);
        Run(TestSessionTilesetCrudUndoRedo);
        Run(TestSessionMapEditingUndoRedo);
        Run(TestSessionMapToolGestureHistory);
        Run(TestMapTilesetPersistenceRoundTrip);
        Run(TestTilesetExportEndToEnd);
        Run(TestMapExportEndToEnd);
        // batch3.7 agent (ViewportOverlayZoomTests.cs): item #31 - the public
        // SetZoom/SetPan/ResetView viewport API (batch-2 carried gap) and the
        // designer overlay passes (core checkerboard backdrop, pixel/tile
        // grid boundary lines, per-key hitbox rectangles, attachment
        // outlines pinned to the export flatten bounds, pivot markers) with
        // pixel-exact reference models and a zero-alloc steady-state guard.
        Run(TestViewportZoomPanResetApiContract);
        Run(TestOverlayCheckerboardCoreComposite);
        Run(TestOverlayGridBoundaryReference);
        Run(TestOverlayHitboxRectsAndKeyColors);
        Run(TestOverlayAttachmentOutlinesAndPivotMarkers);
        Run(TestOverlayComposeStateAndSteadyState);
        // batch3.8 agent (DirectionalAuthoringTests.cs): item #33 - linked
        // 4-direction authoring (DirectionalState model + project routing,
        // linked project creation, undoable enable/switch/mirror session
        // commands, export-scope all-facings validation, the .efyvmake
        // directional section + malformed corpus, one-export-all-facings +
        // live debug) and runtime-extensible asset fields (custom-field
        // registration matrix + the .efyvlaby export flow).
        Run(TestDirectionalStateModelAndFacingRouting);
        Run(TestToolbarLinkedDirectionalCreation);
        Run(TestSessionDirectionalEnableAndFacingSwitch);
        Run(TestSessionMirrorGeneratedFacing);
        Run(TestDirectionalValidationExportScope);
        Run(TestDirectionalPersistenceRoundTripAndCorpus);
        Run(TestDirectionalExportAllFacings);
        await RunAsync(TestDirectionalLiveDebugPublishesAllFacings);
        Run(TestSchemaCustomFieldRegistrationMatrix);
        Run(TestCustomFieldsExportFlow);
        // item #3 (editor panels): App view-state helpers + ListProjects
        // (EditorAppStateTests.cs).
        Run(TestAppStateScreenPixelConverterAndBlit);
        Run(TestAppStatePropertyFieldEditor);
        Run(TestAppStateProblemFormatter);
        Run(TestAppStateLiveDebugFormatter);
        Run(TestAppStatePreviewStatusFormatter);
        Run(TestPersistenceListProjectsAndAdversarialDirs);
        Console.WriteLine($"LabyMake verification passed: {assertions:N0} assertions in {passed} groups.");
    }

    private static void TestSchemaAndDesignerMetadata()
    {
        var schema = new AssetSchemaService();
        Require(schema.GetAvailableTypes().Count ==
            Config.Schema.AssetDefinitions.Length + Config.Schema.BuiltInAssetRegistrations.Length);

        SchemaDefinition evilEye = schema.GetTypeDefinition("EvilEyeData");
        Require(evilEye != null && evilEye.BaseAssetType == Config.Types.AssetTypeEnemyData);
        Require(FindField(evilEye, SharedConfig.DamageToPlayerField).ValueKind == SchemaValueKind.Float);
        Require((float)FindField(evilEye, SharedConfig.ExperienceValueField).DefaultValue ==
            EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game.Enemy.DefaultExperienceValue);
        Require((float)FindField(schema.GetTypeDefinition(Config.Types.AssetTypeBossData),
            SharedConfig.Phase2HealthThresholdField).DefaultValue ==
            EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game.Boss.DefaultPhase2HealthThreshold);

        SchemaDefinition pyramid = schema.GetTypeDefinition("PyramidsData");
        Require(pyramid != null && !pyramid.IsDirectional);
        SchemaField assetName = FindField(pyramid, SharedConfig.AssetNameField);
        Require(assetName.IsRequired && assetName.ValueKind == SchemaValueKind.Text);

        SchemaField facing = FindField(
            schema.GetTypeDefinition(Config.Types.AssetTypeEnemyData),
            SharedConfig.FacingField);
        Require(facing.Choices.Count == Config.Entity.DirectionalVariantCount);

        var toolbar = new ToolbarAPI(schema);
        EFYVProject project = toolbar.CreateNewProject(
            SharedConfig.EnemyDisplayName + Config.Entity.SuffixDown);
        Require(project != null);
        Require((float)project.AssetProperties[SharedConfig.MaxHealthField] ==
            EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game.Player.DefaultMaxHealth);
        Require(toolbar.TrySetProperty(project, SharedConfig.BaseSpeedField, -1f).Status ==
            PropertyEditStatus.OutOfRange);
        Require(toolbar.TrySetProperty(project, SharedConfig.BaseSpeedField, 7.5f).Succeeded);
        Require(toolbar.TrySetProperty(project, SharedConfig.BaseSpeedField, float.NaN).Status ==
            PropertyEditStatus.InvalidValue);

        EFYVProject boss = toolbar.CreateNewProject(
            SharedConfig.BossDisplayName + Config.Entity.SuffixDown);
        boss.AssetProperties[SharedConfig.MaxHealthField] = 40f;
        boss.AssetProperties[SharedConfig.Phase2HealthThresholdField] = 50f;
        ProjectValidationResult bossValidation = new ProjectValidator(schema).Validate(boss);
        Require(ContainsIssue(
            bossValidation,
            ProjectIssueCode.BossPhaseThresholdExceedsMaxHealth));

        project.AssetProperties[SharedConfig.BaseSpeedField] = float.PositiveInfinity;
        Require(!new ProjectValidator(schema).Validate(project).IsValid);
    }

    private static void TestOpacityCompositing()
    {
        var frame = new Frame(2, 1);
        Layer layer = frame.Layers[0];
        layer.Pixels[0].Rgba = Pack(255, 0, 0, 255);
        layer.Opacity = 0.5f;

        var destination = new PixelColor[2];
        frame.FlattenLayers(destination);
        Require(destination[0].R == 255);
        Require(destination[0].A >= 127 && destination[0].A <= 128);

        layer.IsVisible = false;
        frame.FlattenLayers(destination);
        Require(destination[0].Rgba == 0u);
        RequireThrows<ArgumentOutOfRangeException>(() => layer.Opacity = float.NaN);
        RequireThrows<ArgumentOutOfRangeException>(() => layer.Opacity = float.PositiveInfinity);
    }

    private static void TestAtlasLimitValidation()
    {
        // b2-pipeline-contract agent: the atlas budget now follows the batch-1
        // near-square grid layout. 257 default-canvas frames used to overflow
        // the retired single-row model but fit comfortably as a 17x16 grid.
        int frameCount = (Config.Export.MaxAtlasDimension / Config.Canvas.DefaultWidth) +
            Config.Common.UnitCount;
        EFYVProject project = CreateValidProject(Path.GetTempPath(), frameCount);
        var validator = new ProjectValidator(new AssetSchemaService());
        Require(!ContainsIssue(validator.Validate(project), ProjectIssueCode.AtlasLimitExceeded));

        // A 4096x1 canvas: 16 frames form a 4x4 grid at exactly the 16384px
        // width cap (legal); the 17th frame forces a 5th column (illegal).
        EFYVProject grid = CreateValidProject(Path.GetTempPath(), 1);
        grid.CanvasWidth = Config.Persistence.MaxCanvasDimension;
        grid.CanvasHeight = 1;
        grid.Animations.Clear();
        var animation = new AnimationState("GridBudget", Config.Animation.DefaultFPS);
        var wideFrame = new Frame(grid.CanvasWidth, grid.CanvasHeight);
        for (int index = 0; index < 16; index++) animation.Frames.Add(wideFrame);
        grid.Animations.Add(animation);
        Require(!ContainsIssue(validator.Validate(grid), ProjectIssueCode.AtlasLimitExceeded));
        animation.Frames.Add(wideFrame);
        Require(ContainsIssue(validator.Validate(grid), ProjectIssueCode.AtlasLimitExceeded));
    }

    private static void TestHitboxAndGeneratedAnimation()
    {
        var project = new EFYVProject(Config.Types.AssetTypeEnemyData);
        var frame = new Frame(16, 32);
        var hitboxTool = new HitboxTool();
        hitboxTool.OnPointerDown(project, frame, 0, 0);
        hitboxTool.OnPointerUp(project, frame, 16, 32);
        EFYVBackend.Core.Models.HitboxData hurtbox = frame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
        Require(hurtbox.Width == 1f && hurtbox.Height == 2f);

        hitboxTool.ActiveHitboxKey = "AttackBox";
        hitboxTool.OnPointerDown(project, frame, 4, 4);
        hitboxTool.OnPointerUp(project, frame, 8, 8);
        var animation = new AnimationGeneratorAPI().GenerateWalkAnimation(
            "Walk",
            frame,
            2,
            16,
            1f,
            1f);
        Require(animation.Frames.Count == 2);
        Require(animation.Frames[0].FrameIndex == 0 && animation.Frames[1].FrameIndex == 1);
        Require(animation.Frames[0].Hitboxes.ContainsKey("AttackBox"));
        Require(animation.Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox].Height == 2f);

        var schema = new AssetSchemaService();
        var toolbar = new ToolbarAPI(schema);
        EFYVProject invalidProject = toolbar.CreateNewProject(
            SharedConfig.EnemyDisplayName + Config.Entity.SuffixDown);
        invalidProject.AssetProperties[SharedConfig.EntityNameField] = "InvalidHitbox";
        var invalidAnimation = new AnimationState("Idle");
        var invalidFrame = new Frame(16, 16);
        EFYVBackend.Core.Models.HitboxData invalid = invalidFrame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
        invalid.X = 2f;
        invalid.Width = 2f;
        invalidFrame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = invalid;
        invalidAnimation.Frames.Add(invalidFrame);
        invalidProject.Animations.Add(invalidAnimation);
        ProjectValidationResult validation = new ProjectValidator(schema).Validate(invalidProject);
        Require(!validation.IsValid);
    }

    private static void TestTileMakerBounds()
    {
        var frame = new Frame(64, 64);
        var tool = new TileMakerTool
        {
            TileSize = 32,
            BrushSize = 5,
            CurrentColor = Color(0, 255, 0, 255)
        };
        tool.Execute(frame, 31, 31);
        Require(CountOpaque(frame.Layers[0]) > 0);

        int before = CountOpaque(frame.Layers[0]);
        tool.ActiveLayerIndex = -1;
        tool.Execute(frame, 31, 31);
        Require(CountOpaque(frame.Layers[0]) == before);

        var mapTool = new MapTool { MinScaleJitter = 2f };
        Require(mapTool.MaxScaleJitter == 2f);
        mapTool.MaxScaleJitter = 0.5f;
        Require(mapTool.MinScaleJitter == 0.5f);
        RequireThrows<ArgumentOutOfRangeException>(() => mapTool.ScatterRadius = float.NaN);
        mapTool.ScatterDensity = int.MaxValue;
        Require(mapTool.ScatterDensity == Config.Tool.Map.MaxScatterDensity);
    }

    private static void TestToolDefaultsAndBrushDiameters()
    {
        var pencil = new PencilTool();
        var fill = new FillTool();
        var tile = new TileMakerTool { TileSize = 8 };
        Require(pencil.CurrentColor.Rgba == Config.Color.DefaultBrushRgba);
        Require(fill.CurrentColor.Rgba == Config.Color.DefaultBrushRgba);
        Require(tile.CurrentColor.Rgba == Config.Color.DefaultBrushRgba);
        Require(pencil.CurrentColor.A == 255);
        pencil.BrushSize = int.MaxValue;
        tile.BrushSize = int.MaxValue;
        Require(pencil.BrushSize == Config.Tool.MaxBrushSize);
        Require(tile.BrushSize == Config.Tool.MaxBrushSize);
        Require(new Layer("Bounds", 1, 1).GetPixel(-1, 0).Rgba ==
            Config.Color.TransparentPixelRgba);

        Frame evenPencil = DrawPencilTap(2, EFYVBackend.Core.Math.Algorithms.BrushShape.Square);
        Frame oddPencil = DrawPencilTap(3, EFYVBackend.Core.Math.Algorithms.BrushShape.Square);
        Require(CountOpaque(evenPencil.Layers[0]) == 4);
        Require(CountOpaque(oddPencil.Layers[0]) == 9);

        Frame evenCircle = DrawPencilTap(2, EFYVBackend.Core.Math.Algorithms.BrushShape.Circle);
        Frame oddCircle = DrawPencilTap(3, EFYVBackend.Core.Math.Algorithms.BrushShape.Circle);
        Require(CountOpaque(evenCircle.Layers[0]) == 4);
        Require(CountOpaque(oddCircle.Layers[0]) == 5);

        var evenTileFrame = new Frame(9, 9);
        tile.BrushSize = 2;
        tile.Execute(evenTileFrame, 4, 4);
        Require(CountOpaque(evenTileFrame.Layers[0]) == 4);
        var oddTileFrame = new Frame(9, 9);
        tile.BrushSize = 3;
        tile.Execute(oddTileFrame, 4, 4);
        Require(CountOpaque(oddTileFrame.Layers[0]) == 9);

        var moving = new MovingTool();
        moving.SetJitterAmplitude(Config.Common.FirstIndex, 2f);
        moving.SetJitterFrequency(Config.Tool.Moving.JitterOctantCount - Config.Common.UnitCount, 3f);
        Require(moving.GetJitterAmplitude(Config.Common.FirstIndex) == 2f);
        Require(moving.GetJitterFrequency(
            Config.Tool.Moving.JitterOctantCount - Config.Common.UnitCount) == 3f);
        RequireThrows<ArgumentOutOfRangeException>(() => moving.GetJitterAmplitude(-1));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            moving.SetJitterFrequency(Config.Tool.Moving.JitterOctantCount, 1f));
    }

    private static void TestViewportOverwritesOffCanvasPixels()
    {
        var frame = new Frame(1, 1);
        frame.Layers[0].SetPixel(0, 0, Color(12, 34, 56, 255));
        uint sentinel = Pack(1, 2, 3, 4);
        uint[] screen = { sentinel, sentinel, sentinel, sentinel };
        new ViewportController().RenderToScreenBuffer(frame, screen, 2, 2);
        Require(screen[0] == Pack(12, 34, 56, 255));
        Require(screen[1] == Config.Color.TransparentPixelRgba);
        Require(screen[2] == Config.Color.TransparentPixelRgba);
        Require(screen[3] == Config.Color.TransparentPixelRgba);
    }

    private static void TestDesignerSessionHistory()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("History", project, root))
            {
                session.AutosaveEnabled = false;
                var pencil = new PencilTool { CurrentColor = Color(0, 0, 255, 255) };
                session.ActiveTool = pencil;
                Require(session.PointerDown(1, 1));
                session.ActiveTool = new FillTool();
                Require(session.PointerUp(1, 1));
                Require(project.Animations[0].Frames[0].Layers[0].GetPixel(1, 1).B == 255);
                Require(session.Current.IsDirty && session.History.Current.UndoCount == 1);

                Require(session.Undo());
                Require(project.Animations[0].Frames[0].Layers[0].GetPixel(1, 1).Rgba == 0u);
                Require(session.Redo());
                Require(project.Animations[0].Frames[0].Layers[0].GetPixel(1, 1).B == 255);
                Require(session.History.Current.UndoBytes <= Config.Command.DefaultHistoryByteCapacity);

                session.DuplicateLayer(0);
                Require(project.Animations[0].Frames[0].Layers.Count == 2);
                Require(session.Undo());
                Require(project.Animations[0].Frames[0].Layers.Count == 1);
                Require(session.Redo());
                Require(project.Animations[0].Frames[0].Layers.Count == 2);
                session.RemoveLayer(1);
                Require(project.Animations[0].Frames[0].Layers.Count == 1);
                Require(session.Undo());
                Require(project.Animations[0].Frames[0].Layers.Count == 2);
                Require(session.Redo());
                Require(project.Animations[0].Frames[0].Layers.Count == 1);
                RequireThrows<InvalidOperationException>(() => session.RemoveLayer(0));

                int animationCount = project.Animations.Count;
                var moving = new MovingTool { WalkFrameCount = 2 };
                session.SelectFrame(0, 0);
                session.GenerateAnimation(moving);
                Require(project.Animations.Count == animationCount + 1);
                Require(session.Undo());
                Require(project.Animations.Count == animationCount);
                Require(session.Redo());
                Require(project.Animations.Count == animationCount + 1);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestPersistenceRoundTripAndPathSafety()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var schema = new AssetSchemaService();
            var persistence = new ProjectPersistenceService(root, schema);
            EFYVProject project = CreateValidProject(root, 2);
            project.DesignerSeed = 42u;
            project.Animations[0].Frames[0].Layers[0].SetPixel(2, 3, Color(12, 34, 56, 255));

            string path = persistence.SaveProject("RoundTrip", project, CancellationToken.None);
            Require(File.Exists(path));
            EFYVProject restored = persistence.LoadProject("RoundTrip");
            Require(restored.DesignerSeed == 42u);
            Require((string)restored.AssetProperties[SharedConfig.EntityNameField] == "VerificationEnemy");
            Require(restored.Animations[0].Frames[0].Layers[0].GetPixel(2, 3).G == 34);

            persistence.SaveAutosave("RoundTrip", restored, CancellationToken.None);
            Require(persistence.AutosaveExists("RoundTrip"));
            persistence.DeleteAutosave("RoundTrip");
            Require(!persistence.AutosaveExists("RoundTrip"));

            RequireThrows<ArgumentException>(() => persistence.GetProjectPath("../escape"));
            RequireThrows<ArgumentException>(() => persistence.GetProjectPath("CON"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestAtomicExportAndAtlasMetadata()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            EFYVProject project = CreateValidProject(root, 2);
            var schema = new AssetSchemaService();
            var engine = new ExportEngine(new ProjectValidator(schema));
            ExportResult result = engine.Export(project, CancellationToken.None);
            Require(File.Exists(result.MetadataPath) && File.Exists(result.ImagePath));
            Require(new FileInfo(result.ImagePath).Length > 8);

            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(result.MetadataPath)))
            {
                JsonElement atlas = json.RootElement.GetProperty(
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Exporter.FieldAtlas);
                Require(atlas.GetProperty(
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Exporter.FieldFrameWidth).GetInt32() ==
                    project.CanvasWidth);
                Require(atlas.GetProperty(
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Exporter.FieldAnimations)[0]
                    .GetProperty(EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Exporter.FieldFrameCount)
                    .GetInt32() == 2);
            }

            ExportResult replaced = engine.Export(project, CancellationToken.None);
            Require(replaced.MetadataPath == result.MetadataPath);
            project.AssetProperties[SharedConfig.FacingField] = SharedConfig.FacingUp;
            ExportResult up = engine.Export(project, CancellationToken.None);
            Require(up.MetadataPath != result.MetadataPath);
            Require(File.Exists(up.MetadataPath) && File.Exists(result.MetadataPath));

            string rollbackDirectory = Path.Combine(root, "rollback");
            string publishDirectory = Path.Combine(rollbackDirectory, "Assets", "RawArt");
            string stagingDirectory = Path.Combine(rollbackDirectory, "staging");
            Directory.CreateDirectory(publishDirectory);
            Directory.CreateDirectory(stagingDirectory);
            string imagePath = Path.Combine(publishDirectory, "image.png");
            string stagedImage = Path.Combine(stagingDirectory, "staged.png");
            string metadataPath = Path.Combine(publishDirectory, "metadata.efyvlaby");
            string stagedMetadata = Path.Combine(stagingDirectory, "staged.efyvlaby");
            File.WriteAllText(imagePath, "old-image");
            File.WriteAllText(stagedImage, "new-image");
            File.WriteAllText(stagedMetadata, "new-metadata");
            Directory.CreateDirectory(metadataPath);
            RequireThrows<IOException>(() => ExportEngine.PublishPair(
                stagedImage,
                imagePath,
                stagedMetadata,
                metadataPath));
            Require(File.ReadAllText(imagePath) == "old-image");
            Require(Directory.GetFiles(publishDirectory).Length == 1);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void TestPreviewPlayback()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 3);
            var preview = new PreviewController { IsLooping = false };
            preview.Load(ProjectSnapshot.Capture(project), 0);
            preview.Play();
            Require(preview.Tick(TimeSpan.FromSeconds(1d / project.Animations[0].FPS)));
            Require(preview.Current.FrameIndex == 1);
            preview.Tick(TimeSpan.FromSeconds(10));
            Require(preview.Current.FrameIndex == 2);
            Require(preview.Current.State == PreviewPlaybackState.Paused);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task TestLiveDebugDebounceAndStop()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            EFYVProject project = CreateValidProject(root, 1);
            var schema = new AssetSchemaService();
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
                long first = live.NotifyProjectChanged(project);
                long second = live.NotifyProjectChanged(project);
                Require(second > first);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount >= 2, 2000));
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Succeeded);

                live.NotifyProjectChanged(project);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount > 0, 2000));
                live.StopWatching();
                scheduler.ReleaseAll();
                await live.FlushAsync();
                Require(live.Current.State == LiveDebugState.Stopped);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task TestAutosaveDeferredCapture()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
            var scheduler = new ManualScheduler();
            EFYVProject project = CreateValidProject(root, 1);
            int captures = 0;
            using (var autosave = new AutosaveController(
                persistence,
                scheduler,
                TimeSpan.FromSeconds(1),
                null))
            {
                autosave.Schedule("Deferred", () =>
                {
                    captures++;
                    return ProjectPersistenceSnapshot.Capture(project);
                });
                autosave.Schedule("Deferred", () =>
                {
                    captures++;
                    return ProjectPersistenceSnapshot.Capture(project);
                });
                Require(captures == 0);
                Require(SpinWait.SpinUntil(() => scheduler.PendingCount >= 2, 2000));
                scheduler.ReleaseAll();
                await autosave.FlushAsync();
                Require(autosave.Current.State == AutosaveState.Succeeded);
                Require(captures == 1);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static EFYVProject CreateValidProject(string unityRoot, int frameCount)
    {
        var schema = new AssetSchemaService();
        var toolbar = new ToolbarAPI(schema);
        EFYVProject project = toolbar.CreateNewProject(
            SharedConfig.EnemyDisplayName + Config.Entity.SuffixDown);
        project.UnityProjectPath = unityRoot;
        project.AssetProperties[SharedConfig.EntityNameField] = "VerificationEnemy";
        var animation = new AnimationState("Idle", 4);
        for (int index = 0; index < frameCount; index++)
            animation.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, index));
        project.Animations.Add(animation);
        return project;
    }

    private static SchemaField FindField(SchemaDefinition definition, string fieldName)
    {
        foreach (var field in definition.Fields)
        {
            if (field.FieldName == fieldName) return field;
        }
        throw new InvalidOperationException();
    }

    private static bool ContainsIssue(ProjectValidationResult result, ProjectIssueCode code)
    {
        foreach (var issue in result.Issues)
        {
            if (issue.Code == code) return true;
        }
        return false;
    }

    private static Frame DrawPencilTap(
        int brushSize,
        EFYVBackend.Core.Math.Algorithms.BrushShape brushShape)
    {
        var frame = new Frame(9, 9);
        var pencil = new PencilTool
        {
            BrushSize = brushSize,
            BrushShape = brushShape
        };
        pencil.OnPointerDown(null, frame, 4, 4);
        pencil.OnPointerUp(null, frame, 4, 4);
        return frame;
    }

    private static PixelColor Color(byte red, byte green, byte blue, byte alpha)
    {
        return new PixelColor { Rgba = Pack(red, green, blue, alpha) };
    }

    private static uint Pack(byte red, byte green, byte blue, byte alpha)
    {
        return red |
            ((uint)green << Config.Color.GreenShift) |
            ((uint)blue << Config.Color.BlueShift) |
            ((uint)alpha << Config.Color.AlphaShift);
    }

    private static int CountOpaque(Layer layer)
    {
        int count = 0;
        foreach (var pixel in layer.Pixels)
        {
            if (!pixel.IsTransparent) count++;
        }
        return count;
    }

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private static void Run(Action test)
    {
        test();
        passed++;
    }

    private static async Task RunAsync(Func<Task> test)
    {
        await test();
        passed++;
    }

    private static void Require(bool condition)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException();
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        assertions++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException();
    }

    private sealed class ManualScheduler : IDebounceScheduler
    {
        private readonly object gate = new object();
        private readonly List<TaskCompletionSource<bool>> pending =
            new List<TaskCompletionSource<bool>>();

        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
        public int PendingCount { get { lock (gate) return pending.Count; } }

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay <= TimeSpan.Zero) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled());
            lock (gate) pending.Add(completion);
            return completion.Task;
        }

        public void ReleaseAll()
        {
            TaskCompletionSource<bool>[] requests;
            lock (gate)
            {
                requests = pending.ToArray();
                pending.Clear();
                UtcNow = UtcNow.AddSeconds(1);
            }
            foreach (var request in requests) request.TrySetResult(true);
        }
    }
}
