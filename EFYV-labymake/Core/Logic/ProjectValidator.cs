using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using EFYVLabyMake.Core.IO;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    [Flags]
    public enum ProjectValidationScope
    {
        Designer = 1,
        Export = 2,
        Persistence = 4,
        Structural = 8
    }

    public enum ProjectIssueSeverity
    {
        Warning,
        Error
    }

    public enum ProjectIssueCode
    {
        MissingProject,
        MissingTargetAssetType,
        UnknownTargetAssetType,
        MissingProperty,
        UnknownProperty,
        InvalidPropertyType,
        PropertyOutOfRange,
        BossPhaseThresholdExceedsMaxHealth,
        InvalidPropertyChoice,
        EmptyIdentityName,
        InvalidIdentityName,
        InvalidFacing,
        InvalidCanvasDimensions,
        CanvasLimitExceeded,
        AtlasLimitExceeded,
        AnimationLimitExceeded,
        FrameLimitExceeded,
        LayerLimitExceeded,
        MissingAnimations,
        MissingFrames,
        InvalidFrameRate,
        FrameDimensionMismatch,
        MissingLayers,
        LayerDimensionMismatch,
        LayerPixelCountMismatch,
        InvalidLayerOpacity,
        EmptyHitboxKey,
        InvalidHitboxBounds,
        MissingUnityProjectPath,
        MissingUnityAssetsDirectory,
        // Item #7 authored effect descriptors (appended). Instances are
        // validated at construction, so only null entries and the per-
        // animation count can go wrong at the model level.
        EffectLimitExceeded,
        InvalidEffectDescriptor,
        // Item #6 sub-element attachments (appended). Instances are validated
        // at construction, so only null entries and the per-frame count can
        // go wrong at the model level.
        AttachmentLimitExceeded,
        InvalidAttachment,
        // Item #33 linked directional authoring (appended): an INACTIVE facing
        // of a directional project has no animations. Reported at EXPORT scope
        // only (Subject = the facing name) - sketching one facing at a time
        // must not flag the others while designing. The ACTIVE facing's empty
        // set keeps reporting plain MissingAnimations at every full scope.
        DirectionalFacingIncomplete
    }

    public readonly struct ProjectIssue
    {
        public ProjectIssueSeverity Severity { get; }
        public ProjectIssueCode Code { get; }
        public string Subject { get; }
        public SchemaValueKind ExpectedKind { get; }
        public int AnimationIndex { get; }
        public int FrameIndex { get; }
        public int LayerIndex { get; }

        public ProjectIssue(
            ProjectIssueSeverity severity,
            ProjectIssueCode code,
            string subject = null,
            SchemaValueKind expectedKind = SchemaValueKind.Unknown,
            int animationIndex = Config.Common.NotFoundIndex,
            int frameIndex = Config.Common.NotFoundIndex,
            int layerIndex = Config.Common.NotFoundIndex)
        {
            Severity = severity;
            Code = code;
            Subject = subject;
            ExpectedKind = expectedKind;
            AnimationIndex = animationIndex;
            FrameIndex = frameIndex;
            LayerIndex = layerIndex;
        }
    }

    public sealed class ProjectValidationResult
    {
        private readonly ReadOnlyCollection<ProjectIssue> issues;

        public IReadOnlyList<ProjectIssue> Issues => issues;
        public bool IsValid { get; }

        internal ProjectValidationResult(IList<ProjectIssue> source)
        {
            issues = new ReadOnlyCollection<ProjectIssue>(new List<ProjectIssue>(source));
            bool valid = true;
            foreach (var issue in issues)
            {
                if (issue.Severity == ProjectIssueSeverity.Error)
                {
                    valid = false;
                    break;
                }
            }
            IsValid = valid;
        }
    }

    public sealed class ProjectValidator
    {
        // Scopes whose validation covers the full schema/property/identity surface.
        // ProjectValidationScope.Structural alone limits validation to the structural
        // checks (canvas, animation, frame, layer, hitbox integrity) so previews can
        // play while schema-level fields (for example the identity name) are still
        // incomplete; export and persistence keep the full checks.
        private const ProjectValidationScope FullScopes =
            ProjectValidationScope.Designer |
            ProjectValidationScope.Export |
            ProjectValidationScope.Persistence;

        private readonly AssetSchemaService schemaService;

        // The export engine resolves baseAssetType through the same schema the
        // validator was built with, keeping both on a single registration source.
        internal AssetSchemaService SchemaService => schemaService;

        public ProjectValidator(AssetSchemaService schemaService)
        {
            if (schemaService == null) throw new ArgumentNullException(nameof(schemaService));
            this.schemaService = schemaService;
        }

        public ProjectValidationResult Validate(
            EFYVProject project,
            ProjectValidationScope scope = ProjectValidationScope.Designer)
        {
            var issues = new List<ProjectIssue>();
            if (project == null)
            {
                issues.Add(Error(ProjectIssueCode.MissingProject));
                return new ProjectValidationResult(issues);
            }

            if ((scope & FullScopes) != 0)
                ValidateSchemaAndProperties(project, issues);
            ValidateCanvas(project, issues);
            ValidateAnimationSet(project, project.Animations, issues, true);

            if ((scope & ProjectValidationScope.Export) != 0)
            {
                ValidateExportPath(project, issues);
                // Item #33: all-facings completeness is an EXPORT-scope
                // concern only - one export publishes every facing, so every
                // facing must be structurally sound and non-empty there,
                // while designer/persistence scopes keep validating just the
                // active set a designer is working on.
                if (project.Directional != null)
                    ValidateInactiveFacings(project, issues);
            }

            return new ProjectValidationResult(issues);
        }

        private static void ValidateInactiveFacings(EFYVProject project, ICollection<ProjectIssue> issues)
        {
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (string.Equals(facing, project.Directional.ActiveFacing, StringComparison.Ordinal))
                    continue;
                IReadOnlyList<AnimationState> animations =
                    project.Directional.GetInactiveFacingAnimations(facing);
                if (animations.Count == Config.Common.EmptyCount)
                {
                    issues.Add(Error(ProjectIssueCode.DirectionalFacingIncomplete, facing));
                    continue;
                }
                ValidateAnimationSet(project, animations, issues, false);
            }
        }

        private SchemaDefinition ValidateSchemaAndProperties(EFYVProject project, ICollection<ProjectIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(project.TargetAssetType))
            {
                issues.Add(Error(ProjectIssueCode.MissingTargetAssetType));
                return null;
            }

            SchemaDefinition definition;
            if (!schemaService.TryGetTypeDefinition(project.TargetAssetType, out definition))
            {
                issues.Add(Error(ProjectIssueCode.UnknownTargetAssetType, project.TargetAssetType));
                return null;
            }

            var knownFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in definition.Fields)
            {
                knownFields.Add(field.FieldName);
                object value;
                if (!project.AssetProperties.TryGetValue(field.FieldName, out value))
                {
                    if (field.IsRequired)
                        issues.Add(Error(ProjectIssueCode.MissingProperty, field.FieldName, field.ValueKind));
                    continue;
                }

                object normalized;
                if (!ToolbarAPI.TryNormalizeValue(field.ValueKind, value, out normalized))
                {
                    issues.Add(Error(ProjectIssueCode.InvalidPropertyType, field.FieldName, field.ValueKind));
                    continue;
                }

                if (field.HasRange)
                {
                    double numeric = Convert.ToDouble(normalized, System.Globalization.CultureInfo.InvariantCulture);
                    if (numeric < field.Minimum || numeric > field.Maximum)
                        issues.Add(Error(ProjectIssueCode.PropertyOutOfRange, field.FieldName, field.ValueKind));
                }

                if (field.Choices.Count > Config.Common.EmptyCount &&
                    !ToolbarAPI.ContainsChoice(field.Choices, normalized as string))
                    issues.Add(Error(ProjectIssueCode.InvalidPropertyChoice, field.FieldName, field.ValueKind));
            }

            foreach (var property in project.AssetProperties)
            {
                if (!knownFields.Contains(property.Key))
                    issues.Add(Warning(ProjectIssueCode.UnknownProperty, property.Key));
            }

            ValidateCrossFieldRules(project, definition, issues);

            object identityNameValue;
            if (definition.IdentityFieldName == null ||
                !project.AssetProperties.TryGetValue(definition.IdentityFieldName, out identityNameValue) ||
                string.IsNullOrWhiteSpace(identityNameValue as string))
            {
                issues.Add(Error(
                    ProjectIssueCode.EmptyIdentityName,
                    definition.IdentityFieldName,
                    SchemaValueKind.Text));
            }
            else if (!DesignerPathPolicy.IsSafeFileStem((string)identityNameValue))
            {
                issues.Add(Error(
                    ProjectIssueCode.InvalidIdentityName,
                    definition.IdentityFieldName,
                    SchemaValueKind.Text));
            }

            object facingValue;
            if (project.AssetProperties.TryGetValue(Config.Entity.KeyFacing, out facingValue) &&
                !IsValidFacing(facingValue as string))
            {
                issues.Add(Error(ProjectIssueCode.InvalidFacing, Config.Entity.KeyFacing, SchemaValueKind.Text));
            }

            return definition;
        }

        private static void ValidateCrossFieldRules(
            EFYVProject project,
            SchemaDefinition definition,
            ICollection<ProjectIssue> issues)
        {
            if (definition.AssetType != Config.Types.AssetTypeBossData &&
                definition.BaseAssetType != Config.Types.AssetTypeBossData)
                return;

            object maxHealthValue;
            object phaseThresholdValue;
            object normalizedMaxHealth;
            object normalizedPhaseThreshold;
            if (!project.AssetProperties.TryGetValue(
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.MaxHealthField,
                    out maxHealthValue) ||
                !project.AssetProperties.TryGetValue(
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.Phase2HealthThresholdField,
                    out phaseThresholdValue) ||
                !ToolbarAPI.TryNormalizeValue(SchemaValueKind.Float, maxHealthValue, out normalizedMaxHealth) ||
                !ToolbarAPI.TryNormalizeValue(SchemaValueKind.Float, phaseThresholdValue, out normalizedPhaseThreshold))
                return;

            if ((float)normalizedPhaseThreshold > (float)normalizedMaxHealth)
            {
                issues.Add(Error(
                    ProjectIssueCode.BossPhaseThresholdExceedsMaxHealth,
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.Phase2HealthThresholdField,
                    SchemaValueKind.Float));
            }
        }

        private static void ValidateCanvas(EFYVProject project, ICollection<ProjectIssue> issues)
        {
            if (project.CanvasWidth <= Config.Canvas.MinCoordinate ||
                project.CanvasHeight <= Config.Canvas.MinCoordinate)
                issues.Add(Error(ProjectIssueCode.InvalidCanvasDimensions));
            else if (project.CanvasWidth > Config.Persistence.MaxCanvasDimension ||
                project.CanvasHeight > Config.Persistence.MaxCanvasDimension)
                issues.Add(Error(ProjectIssueCode.CanvasLimitExceeded));
        }

        // Validates ONE animation set (the live list, or - at export scope -
        // an inactive facing set of a directional project) against the same
        // structural rules; reportMissingAnimations is false for inactive
        // facing sets, whose emptiness reports as DirectionalFacingIncomplete.
        private static void ValidateAnimationSet(
            EFYVProject project,
            IReadOnlyList<AnimationState> animationSet,
            ICollection<ProjectIssue> issues,
            bool reportMissingAnimations)
        {
            if (animationSet.Count == Config.Common.EmptyCount)
            {
                if (reportMissingAnimations)
                    issues.Add(Error(ProjectIssueCode.MissingAnimations));
                return;
            }

            if (animationSet.Count > Config.Persistence.MaxAnimations)
                issues.Add(Error(ProjectIssueCode.AnimationLimitExceeded));

            long totalFrames = Config.Common.EmptyCount;

            for (int animationIndex = Config.Common.FirstIndex;
                animationIndex < animationSet.Count;
                animationIndex++)
            {
                var animation = animationSet[animationIndex];
                if (animation == null || animation.Frames.Count == Config.Common.EmptyCount)
                {
                    issues.Add(Error(
                        ProjectIssueCode.MissingFrames,
                        animation?.StateName,
                        animationIndex: animationIndex));
                    continue;
                }

                if (animation.Frames.Count > Config.Persistence.MaxFramesPerAnimation)
                    issues.Add(Error(
                        ProjectIssueCode.FrameLimitExceeded,
                        animation.StateName,
                        animationIndex: animationIndex));
                totalFrames += animation.Frames.Count;

                if (animation.FPS <= Config.Common.EmptyCount)
                    issues.Add(Error(
                        ProjectIssueCode.InvalidFrameRate,
                        animation.StateName,
                        animationIndex: animationIndex));

                if (animation.Effects.Count > Config.Effect.MaxEffectsPerAnimation)
                    issues.Add(Error(
                        ProjectIssueCode.EffectLimitExceeded,
                        animation.StateName,
                        animationIndex: animationIndex));
                foreach (EffectDescriptor effect in animation.Effects)
                {
                    if (effect == null)
                    {
                        issues.Add(Error(
                            ProjectIssueCode.InvalidEffectDescriptor,
                            animation.StateName,
                            animationIndex: animationIndex));
                        break;
                    }
                }

                for (int frameIndex = Config.Common.FirstIndex;
                    frameIndex < animation.Frames.Count;
                    frameIndex++)
                {
                    ValidateFrame(project, animation.Frames[frameIndex], animationIndex, frameIndex, issues);
                }
            }

            // Atlas budget matches the actual grid layout the exporter produces
            // (near-square via FastExporter.ComputeAtlasLayout), not the retired
            // single-row model that capped frames at MaxAtlasDimension / width.
            // Skip when the canvas itself is invalid/oversized (already reported
            // as its own issue) so the layout math below cannot overflow.
            if (totalFrames > Config.Common.EmptyCount &&
                project.CanvasWidth > Config.Canvas.MinCoordinate &&
                project.CanvasHeight > Config.Canvas.MinCoordinate &&
                project.CanvasWidth <= Config.Persistence.MaxCanvasDimension &&
                project.CanvasHeight <= Config.Persistence.MaxCanvasDimension)
            {
                // More frames than the pixel budget can never fit (every frame is
                // at least one pixel); guards the int cast below as well.
                if (totalFrames > Config.Export.MaxAtlasPixelCount)
                {
                    issues.Add(Error(ProjectIssueCode.AtlasLimitExceeded));
                }
                else
                {
                    EFYVBackend.Core.Export.FastExporter.ComputeAtlasLayout(
                        (int)totalFrames,
                        project.CanvasWidth,
                        project.CanvasHeight,
                        out int atlasColumns,
                        out int atlasRows);
                    long atlasWidth = (long)project.CanvasWidth * atlasColumns;
                    long atlasHeight = (long)project.CanvasHeight * atlasRows;
                    if (atlasWidth > Config.Export.MaxAtlasDimension ||
                        atlasHeight > Config.Export.MaxAtlasDimension ||
                        atlasWidth * atlasHeight > Config.Export.MaxAtlasPixelCount)
                        issues.Add(Error(ProjectIssueCode.AtlasLimitExceeded));
                }
            }
        }

        private static void ValidateFrame(
            EFYVProject project,
            Frame frame,
            int animationIndex,
            int frameIndex,
            ICollection<ProjectIssue> issues)
        {
            if (frame == null || frame.Width != project.CanvasWidth || frame.Height != project.CanvasHeight)
            {
                issues.Add(Error(
                    ProjectIssueCode.FrameDimensionMismatch,
                    animationIndex: animationIndex,
                    frameIndex: frameIndex));
                return;
            }

            if (frame.Layers.Count == Config.Common.EmptyCount)
                issues.Add(Error(
                    ProjectIssueCode.MissingLayers,
                    animationIndex: animationIndex,
                    frameIndex: frameIndex));
            else if (frame.Layers.Count > Config.Persistence.MaxLayersPerFrame)
                issues.Add(Error(
                    ProjectIssueCode.LayerLimitExceeded,
                    animationIndex: animationIndex,
                    frameIndex: frameIndex));

            int expectedPixelCount = checked(frame.Width * frame.Height);
            for (int layerIndex = Config.Common.FirstIndex; layerIndex < frame.Layers.Count; layerIndex++)
            {
                var layer = frame.Layers[layerIndex];
                if (layer == null || layer.Width != frame.Width || layer.Height != frame.Height)
                {
                    issues.Add(Error(
                        ProjectIssueCode.LayerDimensionMismatch,
                        layer?.Name,
                        animationIndex: animationIndex,
                        frameIndex: frameIndex,
                        layerIndex: layerIndex));
                    continue;
                }

                if (layer.Pixels == null || layer.Pixels.Length != expectedPixelCount)
                    issues.Add(Error(
                        ProjectIssueCode.LayerPixelCountMismatch,
                        layer.Name,
                        animationIndex: animationIndex,
                        frameIndex: frameIndex,
                        layerIndex: layerIndex));

                if (!IsFinite(layer.Opacity) ||
                    layer.Opacity < Config.Common.ZeroFloat ||
                    layer.Opacity > Config.Common.UnitScale)
                    issues.Add(Error(
                        ProjectIssueCode.InvalidLayerOpacity,
                        layer.Name,
                        animationIndex: animationIndex,
                        frameIndex: frameIndex,
                        layerIndex: layerIndex));
            }

            if (frame.Attachments.Count > Config.Attachment.MaxPerFrame)
                issues.Add(Error(
                    ProjectIssueCode.AttachmentLimitExceeded,
                    animationIndex: animationIndex,
                    frameIndex: frameIndex));
            foreach (SubElementAttachment attachment in frame.Attachments)
            {
                // The constructor is the validation gate; hosts injecting raw
                // list entries can only get nulls (or bypass nothing else)
                // past it, so null is the one per-entry failure here.
                if (attachment == null)
                {
                    issues.Add(Error(
                        ProjectIssueCode.InvalidAttachment,
                        animationIndex: animationIndex,
                        frameIndex: frameIndex));
                    break;
                }
            }

            foreach (var hitbox in frame.Hitboxes)
            {
                if (string.IsNullOrWhiteSpace(hitbox.Key))
                {
                    issues.Add(Error(
                        ProjectIssueCode.EmptyHitboxKey,
                        animationIndex: animationIndex,
                        frameIndex: frameIndex));
                    continue;
                }

                if (!IsFinite(hitbox.Value.X) || !IsFinite(hitbox.Value.Y) ||
                    !IsFinite(hitbox.Value.Width) || !IsFinite(hitbox.Value.Height) ||
                    hitbox.Value.X < Config.Common.ZeroFloat ||
                    hitbox.Value.Y < Config.Common.ZeroFloat ||
                    hitbox.Value.Width < Config.Common.ZeroFloat ||
                    hitbox.Value.Height < Config.Common.ZeroFloat ||
                    hitbox.Value.X > frame.Width / Config.Hitbox.PixelsPerUnit ||
                    hitbox.Value.Y > frame.Height / Config.Hitbox.PixelsPerUnit ||
                    hitbox.Value.Width > (frame.Width / Config.Hitbox.PixelsPerUnit) - hitbox.Value.X ||
                    hitbox.Value.Height > (frame.Height / Config.Hitbox.PixelsPerUnit) - hitbox.Value.Y)
                {
                    issues.Add(Error(
                        ProjectIssueCode.InvalidHitboxBounds,
                        hitbox.Key,
                        animationIndex: animationIndex,
                        frameIndex: frameIndex));
                }
            }
        }

        private static void ValidateExportPath(EFYVProject project, ICollection<ProjectIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(project.UnityProjectPath))
            {
                issues.Add(Error(ProjectIssueCode.MissingUnityProjectPath));
                return;
            }

            string assetsPath = Path.Combine(project.UnityProjectPath, Config.Export.DirAssets);
            if (!Directory.Exists(assetsPath))
                issues.Add(Error(ProjectIssueCode.MissingUnityAssetsDirectory, assetsPath));
        }

        private static bool IsValidFacing(string value)
        {
            return value == Config.Entity.FacingNone ||
                value == Config.Entity.FacingUp ||
                value == Config.Entity.FacingDown ||
                value == Config.Entity.FacingLeft ||
                value == Config.Entity.FacingRight;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static ProjectIssue Error(
            ProjectIssueCode code,
            string subject = null,
            SchemaValueKind kind = SchemaValueKind.Unknown,
            int animationIndex = Config.Common.NotFoundIndex,
            int frameIndex = Config.Common.NotFoundIndex,
            int layerIndex = Config.Common.NotFoundIndex)
        {
            return new ProjectIssue(
                ProjectIssueSeverity.Error,
                code,
                subject,
                kind,
                animationIndex,
                frameIndex,
                layerIndex);
        }

        private static ProjectIssue Warning(ProjectIssueCode code, string subject = null)
        {
            return new ProjectIssue(ProjectIssueSeverity.Warning, code, subject);
        }
    }
}
