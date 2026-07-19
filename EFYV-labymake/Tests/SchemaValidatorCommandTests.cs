using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    private static void TestSchemaAndToolbarAdversarialMatrix()
    {
        RequireThrows<ArgumentNullException>(() => new ToolbarAPI(null));

        var schema = new AssetSchemaService();
        IReadOnlyList<SchemaDefinition> definitions = schema.GetAvailableTypes();
        int builtInCount = Config.Schema.AssetDefinitions.Length +
            Config.Schema.BuiltInAssetRegistrations.Length;
        Require(definitions.Count == builtInCount);
        Require(ReferenceEquals(definitions, schema.GetAvailableTypes()));
        RequireThrows<NotSupportedException>(() =>
            ((IList<SchemaDefinition>)definitions).Add(null));

        var assetTypes = new HashSet<string>(StringComparer.Ordinal);
        var displayNames = new HashSet<string>(StringComparer.Ordinal);
        int expectedCategoryCount = 0;
        foreach (SchemaDefinition definition in definitions)
        {
            Require(!string.IsNullOrWhiteSpace(definition.AssetType));
            Require(!string.IsNullOrWhiteSpace(definition.DisplayName));
            Require(assetTypes.Add(definition.AssetType));
            Require(displayNames.Add(definition.DisplayName));
            Require(definition.IdentityFieldName == SharedConfig.EntityNameField ||
                definition.IdentityFieldName == SharedConfig.AssetNameField);
            Require(definition.Fields.Count > 0);
            expectedCategoryCount += definition.IsDirectional
                ? Config.Entity.DirectionalVariantCount
                : Config.Common.UnitCount;

            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (SchemaField field in definition.Fields)
            {
                Require(!string.IsNullOrWhiteSpace(field.FieldName));
                Require(!string.IsNullOrWhiteSpace(field.DisplayLabel));
                Require(field.ValueKind != SchemaValueKind.Unknown);
                Require(fieldNames.Add(field.FieldName));
                Require(field.Step >= 0d);
                if (field.HasRange)
                {
                    Require(field.Minimum <= field.Maximum);
                    Require(field.Step > 0d);
                    object normalized;
                    Require(ToolbarAPI.TryNormalizeValue(
                        field.ValueKind,
                        field.DefaultValue,
                        out normalized));
                    double numeric = Convert.ToDouble(normalized);
                    Require(numeric >= field.Minimum && numeric <= field.Maximum);
                }

                if (field.Choices.Count > 0)
                {
                    Require(field.ValueKind == SchemaValueKind.Text);
                    Require(ToolbarAPI.ContainsChoice(
                        field.Choices,
                        field.DefaultValue as string));
                    RequireThrows<NotSupportedException>(() =>
                        ((IList<string>)field.Choices).Add("Injected"));
                }
            }

            if (definition.BaseAssetType != definition.AssetType)
            {
                SchemaDefinition baseDefinition = schema.GetTypeDefinition(definition.BaseAssetType);
                Require(baseDefinition != null);
                Require(definition.IsDirectional == baseDefinition.IsDirectional);
                Require(definition.IdentityFieldName == baseDefinition.IdentityFieldName);
                RequireSchemaFieldsEqual(definition.Fields, baseDefinition.Fields);
            }
        }

        Require(schema.GetTypeDefinition(null) == null);
        Require(schema.GetTypeDefinition(string.Empty) == null);
        Require(schema.GetTypeDefinition("missing") == null);
        Require(schema.GetTypeDefinition(Config.Types.AssetTypeEnemyData.ToLowerInvariant()) == null);
        SchemaDefinition ignoredDefinition;
        Require(!schema.TryGetTypeDefinition(null, out ignoredDefinition));
        Require(!schema.TryGetTypeDefinition("missing", out ignoredDefinition));

        var toolbar = new ToolbarAPI(schema);
        List<DesignableCategory> categories = toolbar.GetDesignableCategoryDefinitions();
        Require(categories.Count == expectedCategoryCount);
        Require(toolbar.GetDesignableCategories().Count == categories.Count);
        var categoryLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (DesignableCategory category in categories)
        {
            Require(categoryLabels.Add(category.Label));
            SchemaDefinition definition = schema.GetTypeDefinition(category.AssetType);
            Require(definition != null);
            Require(category.DisplayName == definition.DisplayName);
            Require(definition.IsDirectional == !string.IsNullOrEmpty(category.Facing));
            if (definition.IsDirectional)
            {
                Require(category.Facing == Config.Entity.FacingUp ||
                    category.Facing == Config.Entity.FacingDown ||
                    category.Facing == Config.Entity.FacingLeft ||
                    category.Facing == Config.Entity.FacingRight);
            }
            else
            {
                Require(category.Facing == Config.Entity.FacingNone);
            }

            EFYVProject project = toolbar.CreateNewProject(category.Label);
            Require(project != null && project.TargetAssetType == category.AssetType);
            Require(project.AssetProperties.Count == definition.Fields.Count);
            foreach (SchemaField field in definition.Fields)
            {
                Require(project.AssetProperties.ContainsKey(field.FieldName));
                object expectedValue = definition.IsDirectional &&
                    field.FieldName == Config.Entity.KeyFacing
                    ? category.Facing
                    : field.DefaultValue;
                Require(Equals(project.AssetProperties[field.FieldName], expectedValue));
            }
            if (definition.IsDirectional)
                Require(Equals(project.AssetProperties[Config.Entity.KeyFacing], category.Facing));
            else
                Require(!project.AssetProperties.ContainsKey(Config.Entity.KeyFacing));

            List<DesignerProperty> properties = toolbar.GetEditableProperties(project);
            Require(properties.Count == definition.Fields.Count);
            for (int index = 0; index < properties.Count; index++)
            {
                Require(properties[index].FieldName == definition.Fields[index].FieldName);
                Require(properties[index].ValueKind == definition.Fields[index].ValueKind);
                object expectedValue = definition.IsDirectional &&
                    definition.Fields[index].FieldName == Config.Entity.KeyFacing
                    ? category.Facing
                    : definition.Fields[index].DefaultValue;
                Require(Equals(properties[index].Value, expectedValue));
            }
        }

        Require(toolbar.CreateNewProject(null) == null);
        Require(toolbar.CreateNewProject(string.Empty) == null);
        Require(toolbar.CreateNewProject("not a category") == null);
        Require(toolbar.GetEditableProperties(null).Count == 0);
        Require(toolbar.GetEditableProperties(new EFYVProject("unknown")).Count == 0);

        EFYVProject enemy = toolbar.CreateNewProject(
            SharedConfig.EnemyDisplayName + Config.Entity.SuffixDown);
        Require(enemy != null);
        enemy.AssetProperties.Remove(SharedConfig.BaseSpeedField);
        DesignerProperty missingProperty = FindDesignerProperty(
            toolbar.GetEditableProperties(enemy),
            SharedConfig.BaseSpeedField);
        Require(Equals(missingProperty.Value, missingProperty.DefaultValue));
        Require(!enemy.AssetProperties.ContainsKey(SharedConfig.BaseSpeedField));

        Require(toolbar.TrySetProperty(null, "x", 1).Status ==
            PropertyEditStatus.UnknownAssetType);
        Require(toolbar.TrySetProperty(new EFYVProject("unknown"), "x", 1).Status ==
            PropertyEditStatus.UnknownAssetType);
        Require(toolbar.TrySetProperty(enemy, null, 1).Status == PropertyEditStatus.UnknownField);
        Require(toolbar.TrySetProperty(enemy, "unknown", 1).Status == PropertyEditStatus.UnknownField);

        enemy.AssetProperties[SharedConfig.BaseSpeedField] = 7f;
        Require(toolbar.TrySetProperty(enemy, SharedConfig.BaseSpeedField, 0d).Succeeded);
        Require(enemy.AssetProperties[SharedConfig.BaseSpeedField] is float);
        Require((float)enemy.AssetProperties[SharedConfig.BaseSpeedField] == 0f);
        Require(toolbar.TrySetProperty(enemy, SharedConfig.BaseSpeedField, float.MaxValue).Succeeded);
        Require(toolbar.TrySetProperty(enemy, SharedConfig.BaseSpeedField, -float.Epsilon).Status ==
            PropertyEditStatus.OutOfRange);
        Require((float)enemy.AssetProperties[SharedConfig.BaseSpeedField] == float.MaxValue);
        Require(toolbar.TrySetProperty(enemy, SharedConfig.BaseSpeedField, double.MaxValue).Status ==
            PropertyEditStatus.InvalidValue);
        Require(toolbar.TrySetProperty(enemy, SharedConfig.BaseSpeedField, float.NaN).Status ==
            PropertyEditStatus.InvalidValue);
        Require(toolbar.TrySetProperty(enemy, SharedConfig.BaseSpeedField, "1").Status ==
            PropertyEditStatus.InvalidValue);

        foreach (string facing in Config.Schema.FacingChoices)
        {
            Require(toolbar.TrySetProperty(enemy, Config.Entity.KeyFacing, facing).Succeeded);
            Require(Equals(enemy.AssetProperties[Config.Entity.KeyFacing], facing));
        }
        object previousFacing = enemy.AssetProperties[Config.Entity.KeyFacing];
        Require(toolbar.TrySetProperty(enemy, Config.Entity.KeyFacing, "down").Status ==
            PropertyEditStatus.InvalidChoice);
        Require(Equals(enemy.AssetProperties[Config.Entity.KeyFacing], previousFacing));
        Require(toolbar.TrySetProperty(enemy, Config.Entity.KeyFacing, null).Status ==
            PropertyEditStatus.InvalidValue);

        RequireNormalization(SchemaValueKind.Float, (byte)1, true, 1f);
        RequireNormalization(SchemaValueKind.Float, -17, true, -17f);
        RequireNormalization(SchemaValueKind.Float, 1.25d, true, 1.25f);
        RequireNormalization(SchemaValueKind.Float, decimal.MaxValue, true, (float)decimal.MaxValue);
        RequireNormalization(SchemaValueKind.Float, null, false, null);
        RequireNormalization(SchemaValueKind.Float, "1", false, null);
        RequireNormalization(SchemaValueKind.Float, true, false, null);
        RequireNormalization(SchemaValueKind.Float, '1', false, null);
        RequireNormalization(SchemaValueKind.Float, float.NaN, false, null);
        RequireNormalization(SchemaValueKind.Float, float.NegativeInfinity, false, null);
        RequireNormalization(SchemaValueKind.Float, double.MaxValue, false, null);

        RequireNormalization(SchemaValueKind.Integer, byte.MaxValue, true, 255);
        RequireNormalization(SchemaValueKind.Integer, short.MinValue, true, (int)short.MinValue);
        RequireNormalization(SchemaValueKind.Integer, int.MaxValue, true, int.MaxValue);
        RequireNormalization(SchemaValueKind.Integer, (uint)int.MaxValue, true, int.MaxValue);
        RequireNormalization(SchemaValueKind.Integer, (long)int.MinValue, true, int.MinValue);
        RequireNormalization(SchemaValueKind.Integer, uint.MaxValue, false, null);
        RequireNormalization(SchemaValueKind.Integer, long.MaxValue, false, null);
        RequireNormalization(SchemaValueKind.Integer, ulong.MaxValue, false, null);
        RequireNormalization(SchemaValueKind.Integer, 1f, false, null);
        RequireNormalization(SchemaValueKind.Integer, 1m, false, null);
        RequireNormalization(SchemaValueKind.Integer, "1", false, null);
        RequireNormalization(SchemaValueKind.Integer, null, false, null);

        RequireNormalization(SchemaValueKind.Text, string.Empty, true, string.Empty);
        RequireNormalization(SchemaValueKind.Text, " value ", true, " value ");
        RequireNormalization(SchemaValueKind.Text, 'x', false, null);
        RequireNormalization(SchemaValueKind.Text, 1, false, null);
        RequireNormalization(SchemaValueKind.Text, null, false, null);
        RequireNormalization(SchemaValueKind.Unknown, "x", false, null);

        Require(!schema.RegisterAssetType(default));
        Require(!schema.RegisterAssetType(new AssetSchemaRegistration(
            " ", "Display", Config.Types.AssetTypeEnemyData)));
        Require(!schema.RegisterAssetType(new AssetSchemaRegistration(
            "Custom", " ", Config.Types.AssetTypeEnemyData)));
        Require(!schema.RegisterAssetType(new AssetSchemaRegistration(
            "Custom", "Display", "missing")));
        Require(!schema.RegisterAssetType(new AssetSchemaRegistration(
            Config.Types.AssetTypeEnemyData, "Duplicate", Config.Types.AssetTypeEnemyData)));

        var customRegistration = new AssetSchemaRegistration(
            "MatrixEnemyData",
            "Matrix Enemy",
            Config.Types.AssetTypeEnemyData);
        Require(schema.RegisterAssetType(customRegistration));
        Require(!schema.RegisterAssetType(customRegistration));
        Require(definitions.Count == builtInCount + 1);
        SchemaDefinition custom = schema.GetTypeDefinition(customRegistration.AssetType);
        Require(custom != null && custom.BaseAssetType == Config.Types.AssetTypeEnemyData);
        Require(custom.DisplayName == customRegistration.DisplayName && custom.IsDirectional);
        RequireSchemaFieldsEqual(
            custom.Fields,
            schema.GetTypeDefinition(Config.Types.AssetTypeEnemyData).Fields);

        RequireThrows<ArgumentNullException>(() => schema.RegisterManifest(null));
        int manifestCount = schema.RegisterManifest(new[]
        {
            new AssetSchemaRegistration("MatrixPropData", "Matrix Prop", Config.Types.AssetTypeGameAssetData),
            new AssetSchemaRegistration("MatrixBossData", "Matrix Boss", Config.Types.AssetTypeBossData),
            new AssetSchemaRegistration("BrokenData", "Broken", "missing"),
            customRegistration
        });
        Require(manifestCount == 2);
        Require(definitions.Count == builtInCount + 3);
    }

    private static void TestValidatorIssueMatrix()
    {
        var schema = new AssetSchemaService();
        var validator = new ProjectValidator(schema);
        var observed = new HashSet<ProjectIssueCode>();

        ProjectValidationResult result = validator.Validate(null);
        ObserveIssues(observed, result);
        RequireSingleIssue(result, ProjectIssueCode.MissingProject);

        EFYVProject project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.TargetAssetType = null;
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.MissingTargetAssetType);
        Require(!ContainsIssue(result, ProjectIssueCode.UnknownTargetAssetType));

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.TargetAssetType = "UnknownData";
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        ProjectIssue unknownType = RequireMatrixIssue(result, ProjectIssueCode.UnknownTargetAssetType);
        Require(unknownType.Subject == "UnknownData");

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.AssetProperties.Remove(SharedConfig.BaseSpeedField);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        ProjectIssue missing = RequireMatrixIssue(result, ProjectIssueCode.MissingProperty);
        Require(missing.Subject == SharedConfig.BaseSpeedField);
        Require(missing.ExpectedKind == SchemaValueKind.Float);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.AssetProperties.Remove(SharedConfig.EntityNameField);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.MissingProperty);
        RequireMatrixIssue(result, ProjectIssueCode.EmptyIdentityName);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.AssetProperties["matrixUnknown"] = 1;
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireSingleIssue(result, ProjectIssueCode.UnknownProperty);
        Require(result.IsValid);
        Require(result.Issues[0].Severity == ProjectIssueSeverity.Warning);
        Require(result.Issues[0].Subject == "matrixUnknown");
        RequireThrows<NotSupportedException>(() =>
            ((IList<ProjectIssue>)result.Issues).Add(default));

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.AssetProperties[SharedConfig.BaseSpeedField] = "fast";
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        ProjectIssue invalidType = RequireMatrixIssue(result, ProjectIssueCode.InvalidPropertyType);
        Require(invalidType.Subject == SharedConfig.BaseSpeedField);
        Require(invalidType.ExpectedKind == SchemaValueKind.Float);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.AssetProperties[SharedConfig.BaseSpeedField] = -1f;
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.PropertyOutOfRange);

        project = CreateValidatorProject(Config.Types.AssetTypeBossData, 8, 8);
        project.AssetProperties[SharedConfig.MaxHealthField] = 10f;
        project.AssetProperties[SharedConfig.Phase2HealthThresholdField] = 11f;
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        ProjectIssue phase = RequireMatrixIssue(
            result,
            ProjectIssueCode.BossPhaseThresholdExceedsMaxHealth);
        Require(phase.Subject == SharedConfig.Phase2HealthThresholdField);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.AssetProperties[Config.Entity.KeyFacing] = "Diagonal";
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.InvalidPropertyChoice);
        RequireMatrixIssue(result, ProjectIssueCode.InvalidFacing);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.AssetProperties[SharedConfig.EntityNameField] = "  ";
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.EmptyIdentityName);

        string[] unsafeNames = { "..", "CON", "folder/name", "trailing.", "trailing " };
        foreach (string unsafeName in unsafeNames)
        {
            project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            project.AssetProperties[SharedConfig.EntityNameField] = unsafeName;
            result = validator.Validate(project);
            ObserveIssues(observed, result);
            ProjectIssue unsafeIdentity = RequireMatrixIssue(
                result,
                ProjectIssueCode.InvalidIdentityName);
            Require(unsafeIdentity.Subject == SharedConfig.EntityNameField);
        }

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.CanvasWidth = 0;
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.InvalidCanvasDimensions);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.CanvasHeight = Config.Persistence.MaxCanvasDimension + 1;
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.CanvasLimitExceeded);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 1, 1);
        AnimationState oneFrameAnimation = project.Animations[0];
        for (int index = project.Animations.Count;
            index <= Config.Persistence.MaxAnimations;
            index++)
            project.Animations.Add(oneFrameAnimation);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.AnimationLimitExceeded);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 1, 1);
        Frame onePixelFrame = project.Animations[0].Frames[0];
        for (int index = project.Animations[0].Frames.Count;
            index <= Config.Persistence.MaxFramesPerAnimation;
            index++)
            project.Animations[0].Frames.Add(onePixelFrame);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.FrameLimitExceeded);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 1, 1);
        Layer onePixelLayer = project.Animations[0].Frames[0].Layers[0];
        for (int index = project.Animations[0].Frames[0].Layers.Count;
            index <= Config.Persistence.MaxLayersPerFrame;
            index++)
            project.Animations[0].Frames[0].Layers.Add(onePixelLayer);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.LayerLimitExceeded);

        // Grid-atlas budget (batch-1 layout): 17 frames of a 4096-wide canvas
        // need 5 grid columns -> 20480px atlas width, over MaxAtlasDimension.
        // (The retired single-row model flagged this at just 5 frames.)
        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 4096, 1);
        onePixelFrame = project.Animations[0].Frames[0];
        for (int index = 1; index < 17; index++)
            project.Animations[0].Frames.Add(onePixelFrame);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.AtlasLimitExceeded);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations.Clear();
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.MissingAnimations);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].Frames.Clear();
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        ProjectIssue missingFrames = RequireMatrixIssue(result, ProjectIssueCode.MissingFrames);
        Require(missingFrames.AnimationIndex == 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations.Add(null);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.MissingFrames, 1);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].FPS = 0;
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.InvalidFrameRate);

        // batch3.4 agent (item #7): authored effect descriptor issues.
        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        for (int overflow = 0; overflow <= Config.Effect.MaxEffectsPerAnimation; overflow++)
        {
            project.Animations[0].Effects.Add(new EffectDescriptor(
                Config.Effect.TypeFlash,
                "MatrixFlash",
                Config.Effect.TriggerOnDamaged,
                0xFF0000FFu,
                100,
                0.5f));
        }
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.EffectLimitExceeded);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].Effects.Add(null);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.InvalidEffectDescriptor);

        // batch3.5 agent (item #6): sub-element attachment issues.
        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        for (int overflow = 0; overflow <= Config.Attachment.MaxPerFrame; overflow++)
        {
            project.Animations[0].Frames[0].Attachments.Add(
                new SubElementAttachment("MatrixTorch", overflow, 0, 0, false, false));
        }
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.AttachmentLimitExceeded, 0, 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].Frames[0].Attachments.Add(null);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.InvalidAttachment, 0, 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].Frames[0] = null;
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.FrameDimensionMismatch, 0, 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].Frames[0] = new Frame(7, 8);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.FrameDimensionMismatch, 0, 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].Frames[0].Layers.Clear();
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.MissingLayers, 0, 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].Frames[0].Layers.Add(new Layer("wrong", 7, 8));
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.LayerDimensionMismatch, 0, 0, 1);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        Layer malformedLayer = project.Animations[0].Frames[0].Layers[0];
        typeof(Layer).GetProperty(nameof(Layer.Pixels),
            BindingFlags.Instance | BindingFlags.Public).SetValue(
                malformedLayer,
                new PixelColor[1]);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.LayerPixelCountMismatch, 0, 0, 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        malformedLayer = project.Animations[0].Frames[0].Layers[0];
        SetMalformedLayerOpacity(malformedLayer, float.NaN);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.InvalidLayerOpacity, 0, 0, 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.Animations[0].Frames[0].Hitboxes[" \t"] = MatrixHitbox(0, 0, 0, 0);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.EmptyHitboxKey, 0, 0);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        Frame hitboxFrame = project.Animations[0].Frames[0];
        hitboxFrame.Hitboxes["NegativeX"] = MatrixHitbox(-0.01f, 0, 0, 0);
        hitboxFrame.Hitboxes["NegativeY"] = MatrixHitbox(0, -0.01f, 0, 0);
        hitboxFrame.Hitboxes["NegativeWidth"] = MatrixHitbox(0, 0, -0.01f, 0);
        hitboxFrame.Hitboxes["NonFinite"] = MatrixHitbox(float.NaN, 0, 0, 0);
        hitboxFrame.Hitboxes["OriginOutside"] = MatrixHitbox(1f, 0, 0, 0);
        hitboxFrame.Hitboxes["WidthOutside"] = MatrixHitbox(0.4f, 0, 0.2f, 0);
        hitboxFrame.Hitboxes["HeightOutside"] = MatrixHitbox(0, 0.4f, 0, 0.2f);
        result = validator.Validate(project);
        ObserveIssues(observed, result);
        foreach (string key in new[]
        {
            "NegativeX", "NegativeY", "NegativeWidth", "NonFinite",
            "OriginOutside", "WidthOutside", "HeightOutside"
        })
            RequireMatrixIssueWithSubject(result, ProjectIssueCode.InvalidHitboxBounds, key);

        project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
        project.UnityProjectPath = null;
        result = validator.Validate(project, ProjectValidationScope.Designer);
        Require(!ContainsIssue(result, ProjectIssueCode.MissingUnityProjectPath));
        result = validator.Validate(project, ProjectValidationScope.Persistence);
        Require(!ContainsIssue(result, ProjectIssueCode.MissingUnityProjectPath));
        result = validator.Validate(project, ProjectValidationScope.Export);
        ObserveIssues(observed, result);
        RequireMatrixIssue(result, ProjectIssueCode.MissingUnityProjectPath);

        string root = NewTemporaryDirectory();
        try
        {
            project = CreateValidatorProject(Config.Types.AssetTypeEnemyData, 8, 8);
            project.UnityProjectPath = root;
            result = validator.Validate(project, ProjectValidationScope.Export);
            ObserveIssues(observed, result);
            ProjectIssue missingAssets = RequireMatrixIssue(
                result,
                ProjectIssueCode.MissingUnityAssetsDirectory);
            Require(missingAssets.Subject == Path.Combine(root, Config.Export.DirAssets));

            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            result = validator.Validate(
                project,
                ProjectValidationScope.Designer | ProjectValidationScope.Export);
            Require(result.IsValid && result.Issues.Count == 0);

            // Item #33: a linked directional project with empty parked facings
            // reports DirectionalFacingIncomplete (Subject = facing name) at
            // EXPORT scope only.
            project.Directional = new DirectionalState(Config.Entity.FacingDown);
            Require(validator.Validate(project, ProjectValidationScope.Designer).IsValid);
            result = validator.Validate(project, ProjectValidationScope.Export);
            ObserveIssues(observed, result);
            RequireMatrixIssueWithSubject(
                result,
                ProjectIssueCode.DirectionalFacingIncomplete,
                Config.Entity.FacingUp);
        }
        finally
        {
            DeleteDirectory(root);
        }

        foreach (ProjectIssueCode code in Enum.GetValues(typeof(ProjectIssueCode)))
            Require(observed.Contains(code));
    }

    private static void TestCommandManagerBoundedRandomizedModel()
    {
        RequireThrows<ArgumentOutOfRangeException>(() => new CommandManager(0, 1));
        RequireThrows<ArgumentOutOfRangeException>(() => new CommandManager(-1, 1));
        RequireThrows<ArgumentOutOfRangeException>(() => new CommandManager(1, 0));
        RequireThrows<ArgumentOutOfRangeException>(() => new CommandManager(1, -1));

        var nullManager = new CommandManager(1, 1);
        RequireThrows<ArgumentNullException>(() => nullManager.ExecuteCommand(null));
        RequireThrows<ArgumentNullException>(() => nullManager.RecordExecutedCommand(null));
        Require(!nullManager.Undo());
        Require(!nullManager.Redo());

        const int capacity = 11;
        const long byteCapacity = 1536;
        var state = new MatrixCommandState();
        var manager = new CommandManager(capacity, byteCapacity);
        var modelUndo = new List<MatrixCommandModelEntry>();
        var modelRedo = new List<MatrixCommandModelEntry>();
        long modelUndoBytes = 0;
        long modelRedoBytes = 0;
        int eventCount = 0;
        CommandHistorySnapshot lastEvent = default;
        manager.HistoryChanged += snapshot =>
        {
            eventCount++;
            lastEvent = snapshot;
        };

        var random = new Random(0x5C4E4A);
        long expectedValue = 0;
        int expectedEvents = 0;
        long[] sizes = { 1, 127, 255, 256, 257, 511, 768, 1024, 1536, 1537, 4096 };
        for (int iteration = 0; iteration < 20000; iteration++)
        {
            int operation = random.Next(100);
            if (operation < 52)
            {
                int delta = random.Next(-1000, 1001);
                long requestedSize = sizes[random.Next(sizes.Length)];
                bool unsized = random.Next(5) == 0;
                ICommand command = unsized
                    ? (ICommand)new MatrixUnsizedCommand(state, delta)
                    : new MatrixSizedCommand(state, delta, requestedSize);
                long effectiveSize = unsized
                    ? Config.Command.EstimatedCommandOverheadBytes
                    : requestedSize;
                var entry = new MatrixCommandModelEntry(command, delta, effectiveSize);

                if (operation < 42)
                {
                    manager.ExecuteCommand(command);
                }
                else
                {
                    command.Execute();
                    manager.RecordExecutedCommand(command);
                }
                expectedValue += delta;
                MatrixRecord(
                    entry,
                    modelUndo,
                    modelRedo,
                    ref modelUndoBytes,
                    ref modelRedoBytes,
                    capacity,
                    byteCapacity);
                expectedEvents++;
            }
            else if (operation < 72)
            {
                bool expected = modelUndo.Count > 0;
                Require(manager.Undo() == expected);
                if (expected)
                {
                    MatrixCommandModelEntry entry = modelUndo[modelUndo.Count - 1];
                    modelUndo.RemoveAt(modelUndo.Count - 1);
                    modelUndoBytes -= entry.Bytes;
                    expectedValue -= entry.Delta;
                    modelRedo.Add(entry);
                    modelRedoBytes += entry.Bytes;
                    expectedEvents++;
                }
            }
            else if (operation < 90)
            {
                bool expected = modelRedo.Count > 0;
                Require(manager.Redo() == expected);
                if (expected)
                {
                    MatrixCommandModelEntry entry = modelRedo[modelRedo.Count - 1];
                    modelRedo.RemoveAt(modelRedo.Count - 1);
                    modelRedoBytes -= entry.Bytes;
                    expectedValue += entry.Delta;
                    modelUndo.Add(entry);
                    modelUndoBytes += entry.Bytes;
                    expectedEvents++;
                }
            }
            else
            {
                bool hadHistory = modelUndo.Count > 0 || modelRedo.Count > 0;
                manager.Clear();
                modelUndo.Clear();
                modelRedo.Clear();
                modelUndoBytes = 0;
                modelRedoBytes = 0;
                if (hadHistory) expectedEvents++;
            }

            Require(state.Value == expectedValue);
            Require(manager.Current.UndoCount == modelUndo.Count);
            Require(manager.Current.RedoCount == modelRedo.Count);
            Require(manager.Current.UndoBytes == modelUndoBytes);
            Require(manager.Current.RedoBytes == modelRedoBytes);
            Require(manager.CanUndo == (modelUndo.Count > 0));
            Require(manager.CanRedo == (modelRedo.Count > 0));
            Require(manager.Current.CanUndo == manager.CanUndo);
            Require(manager.Current.CanRedo == manager.CanRedo);
            Require(manager.Current.UndoCount <= capacity);
            Require(manager.Current.UndoBytes >= 0 && manager.Current.UndoBytes <= byteCapacity);
            Require(manager.Current.RedoBytes >= 0 && manager.Current.RedoBytes <= byteCapacity);
            Require(eventCount == expectedEvents);
            if (eventCount > 0)
            {
                Require(lastEvent.UndoCount == manager.Current.UndoCount);
                Require(lastEvent.RedoCount == manager.Current.RedoCount);
                Require(lastEvent.UndoBytes == manager.Current.UndoBytes);
                Require(lastEvent.RedoBytes == manager.Current.RedoBytes);
            }
        }

        while (modelUndo.Count > 0)
        {
            MatrixCommandModelEntry entry = modelUndo[modelUndo.Count - 1];
            modelUndo.RemoveAt(modelUndo.Count - 1);
            modelUndoBytes -= entry.Bytes;
            expectedValue -= entry.Delta;
            modelRedo.Add(entry);
            modelRedoBytes += entry.Bytes;
            Require(manager.Undo());
            Require(state.Value == expectedValue);
        }
        Require(!manager.Undo());
        while (modelRedo.Count > 0)
        {
            MatrixCommandModelEntry entry = modelRedo[modelRedo.Count - 1];
            modelRedo.RemoveAt(modelRedo.Count - 1);
            modelRedoBytes -= entry.Bytes;
            expectedValue += entry.Delta;
            modelUndo.Add(entry);
            modelUndoBytes += entry.Bytes;
            Require(manager.Redo());
            Require(state.Value == expectedValue);
        }
        Require(!manager.Redo());

        var adversarialState = new MatrixCommandState();
        var adversarial = new CommandManager(4, 1024);
        adversarial.ExecuteCommand(new MatrixSizedCommand(adversarialState, 1, -1));
        Require(adversarial.Current.UndoBytes >= 0);
        Require(adversarial.Current.UndoBytes <= 1024);

        adversarial.Clear();
        adversarial.ExecuteCommand(new MatrixSizedCommand(adversarialState, 1, 0));
        Require(adversarial.Current.UndoBytes > 0);
        Require(adversarial.Current.UndoBytes <= 1024);

        var overflowState = new MatrixCommandState();
        var overflow = new CommandManager(4, long.MaxValue);
        overflow.ExecuteCommand(new MatrixSizedCommand(overflowState, 1, long.MaxValue));
        overflow.ExecuteCommand(new MatrixSizedCommand(overflowState, 1, long.MaxValue));
        Require(overflow.Current.UndoCount == 1);
        Require(overflow.Current.UndoBytes == long.MaxValue);
    }

    // Structural validation scope (#30): ProjectValidationScope.Structural runs only
    // the canvas/animation/frame/layer/hitbox integrity checks so previews can play
    // while schema-level fields are still incomplete; the full scopes keep everything.
    private static void TestValidatorStructuralScope()
    {
        var schema = new AssetSchemaService();
        var validator = new ProjectValidator(schema);
        string enemy = Config.Types.AssetTypeEnemyData;

        // Schema-level breakage is invisible to the structural scope...
        EFYVProject project = CreateValidatorProject(enemy, 8, 8);
        project.AssetProperties[SharedConfig.EntityNameField] = "  ";
        project.AssetProperties[SharedConfig.BaseSpeedField] = -1f;
        project.AssetProperties["structuralUnknown"] = 1;
        project.AssetProperties[Config.Entity.KeyFacing] = "Diagonal";
        ProjectValidationResult result = validator.Validate(
            project,
            ProjectValidationScope.Structural);
        Require(result.IsValid && result.Issues.Count == 0);

        // ...while the default designer scope still reports all of it.
        result = validator.Validate(project);
        Require(!result.IsValid);
        Require(ContainsIssue(result, ProjectIssueCode.EmptyIdentityName));
        Require(ContainsIssue(result, ProjectIssueCode.PropertyOutOfRange));
        Require(ContainsIssue(result, ProjectIssueCode.UnknownProperty));
        Require(ContainsIssue(result, ProjectIssueCode.InvalidFacing));

        // The structural scope alone also skips export-path checks.
        project.UnityProjectPath = null;
        result = validator.Validate(project, ProjectValidationScope.Structural);
        Require(!ContainsIssue(result, ProjectIssueCode.MissingUnityProjectPath));

        // Structural integrity failures stay fatal in the structural scope.
        Require(ContainsIssue(
            validator.Validate(null, ProjectValidationScope.Structural),
            ProjectIssueCode.MissingProject));

        EFYVProject badCanvas = CreateValidatorProject(enemy, 8, 8);
        badCanvas.CanvasWidth = 0;
        Require(ContainsIssue(
            validator.Validate(badCanvas, ProjectValidationScope.Structural),
            ProjectIssueCode.InvalidCanvasDimensions));

        EFYVProject noAnimations = CreateValidatorProject(enemy, 8, 8);
        noAnimations.Animations.Clear();
        Require(ContainsIssue(
            validator.Validate(noAnimations, ProjectValidationScope.Structural),
            ProjectIssueCode.MissingAnimations));

        EFYVProject noFrames = CreateValidatorProject(enemy, 8, 8);
        noFrames.Animations[0].Frames.Clear();
        Require(ContainsIssue(
            validator.Validate(noFrames, ProjectValidationScope.Structural),
            ProjectIssueCode.MissingFrames));

        EFYVProject badFrame = CreateValidatorProject(enemy, 8, 8);
        badFrame.Animations[0].Frames[0] = new Frame(7, 8);
        Require(ContainsIssue(
            validator.Validate(badFrame, ProjectValidationScope.Structural),
            ProjectIssueCode.FrameDimensionMismatch));

        EFYVProject badLayer = CreateValidatorProject(enemy, 8, 8);
        badLayer.Animations[0].Frames[0].Layers.Add(new Layer("structuralWrong", 7, 8));
        Require(ContainsIssue(
            validator.Validate(badLayer, ProjectValidationScope.Structural),
            ProjectIssueCode.LayerDimensionMismatch));

        EFYVProject badFps = CreateValidatorProject(enemy, 8, 8);
        badFps.Animations[0].FPS = 0;
        Require(ContainsIssue(
            validator.Validate(badFps, ProjectValidationScope.Structural),
            ProjectIssueCode.InvalidFrameRate));

        EFYVProject badHitbox = CreateValidatorProject(enemy, 8, 8);
        badHitbox.Animations[0].Frames[0].Hitboxes["StructuralBad"] =
            MatrixHitbox(-0.5f, 0f, 0f, 0f);
        Require(ContainsIssue(
            validator.Validate(badHitbox, ProjectValidationScope.Structural),
            ProjectIssueCode.InvalidHitboxBounds));

        // Combining Structural with a full scope keeps the full checks.
        EFYVProject combined = CreateValidatorProject(enemy, 8, 8);
        combined.AssetProperties[SharedConfig.EntityNameField] = " ";
        result = validator.Validate(
            combined,
            ProjectValidationScope.Structural | ProjectValidationScope.Designer);
        Require(ContainsIssue(result, ProjectIssueCode.EmptyIdentityName));
    }

    private static void RequireSchemaFieldsEqual(
        IReadOnlyList<SchemaField> actual,
        IReadOnlyList<SchemaField> expected)
    {
        Require(actual.Count == expected.Count);
        for (int index = 0; index < actual.Count; index++)
        {
            Require(actual[index].FieldName == expected[index].FieldName);
            Require(actual[index].FieldType == expected[index].FieldType);
            Require(actual[index].ValueKind == expected[index].ValueKind);
            Require(actual[index].DisplayLabel == expected[index].DisplayLabel);
            Require(Equals(actual[index].DefaultValue, expected[index].DefaultValue));
            Require(actual[index].HasRange == expected[index].HasRange);
            Require(actual[index].Minimum == expected[index].Minimum);
            Require(actual[index].Maximum == expected[index].Maximum);
            Require(actual[index].Step == expected[index].Step);
            Require(actual[index].IsRequired == expected[index].IsRequired);
            Require(actual[index].IsReadOnly == expected[index].IsReadOnly);
            Require(actual[index].Choices.Count == expected[index].Choices.Count);
            for (int choice = 0; choice < actual[index].Choices.Count; choice++)
                Require(actual[index].Choices[choice] == expected[index].Choices[choice]);
        }
    }

    private static DesignerProperty FindDesignerProperty(
        IReadOnlyList<DesignerProperty> properties,
        string fieldName)
    {
        foreach (DesignerProperty property in properties)
        {
            if (property.FieldName == fieldName) return property;
        }
        throw new InvalidOperationException();
    }

    private static void RequireNormalization(
        SchemaValueKind kind,
        object value,
        bool expectedSuccess,
        object expectedValue)
    {
        object normalized;
        bool succeeded = ToolbarAPI.TryNormalizeValue(kind, value, out normalized);
        Require(succeeded == expectedSuccess);
        Require(expectedSuccess ? Equals(normalized, expectedValue) : normalized == null);
    }

    private static EFYVProject CreateValidatorProject(
        string assetType,
        int width,
        int height)
    {
        var schema = new AssetSchemaService();
        SchemaDefinition definition = schema.GetTypeDefinition(assetType);
        Require(definition != null);
        var project = new EFYVProject(assetType)
        {
            CanvasWidth = width,
            CanvasHeight = height
        };
        foreach (SchemaField field in definition.Fields)
            project.AssetProperties[field.FieldName] = field.DefaultValue;
        project.AssetProperties[definition.IdentityFieldName] = "MatrixAsset";
        if (definition.IsDirectional)
            project.AssetProperties[Config.Entity.KeyFacing] = Config.Entity.FacingDown;
        var animation = new AnimationState("Idle", 12);
        var frame = new Frame(width, height);
        frame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = MatrixHitbox(0, 0, 0, 0);
        animation.Frames.Add(frame);
        project.Animations.Add(animation);
        return project;
    }

    private static void ObserveIssues(
        ISet<ProjectIssueCode> observed,
        ProjectValidationResult result)
    {
        foreach (ProjectIssue issue in result.Issues) observed.Add(issue.Code);
    }

    private static void RequireSingleIssue(ProjectValidationResult result, ProjectIssueCode code)
    {
        Require(result.Issues.Count == 1);
        Require(result.Issues[0].Code == code);
        Require(result.Issues[0].Severity ==
            (code == ProjectIssueCode.UnknownProperty
                ? ProjectIssueSeverity.Warning
                : ProjectIssueSeverity.Error));
    }

    private static ProjectIssue RequireMatrixIssue(
        ProjectValidationResult result,
        ProjectIssueCode code,
        int animationIndex = Config.Common.NotFoundIndex,
        int frameIndex = Config.Common.NotFoundIndex,
        int layerIndex = Config.Common.NotFoundIndex)
    {
        foreach (ProjectIssue issue in result.Issues)
        {
            if (issue.Code == code &&
                (animationIndex == Config.Common.NotFoundIndex || issue.AnimationIndex == animationIndex) &&
                (frameIndex == Config.Common.NotFoundIndex || issue.FrameIndex == frameIndex) &&
                (layerIndex == Config.Common.NotFoundIndex || issue.LayerIndex == layerIndex))
            {
                Require(issue.Severity == ProjectIssueSeverity.Error ||
                    code == ProjectIssueCode.UnknownProperty);
                return issue;
            }
        }
        throw new InvalidOperationException("Missing validator issue: " + code);
    }

    private static void RequireMatrixIssueWithSubject(
        ProjectValidationResult result,
        ProjectIssueCode code,
        string subject)
    {
        foreach (ProjectIssue issue in result.Issues)
        {
            if (issue.Code == code && issue.Subject == subject)
            {
                Require(issue.Severity == ProjectIssueSeverity.Error);
                return;
            }
        }
        throw new InvalidOperationException("Missing validator issue subject: " + subject);
    }

    private static HitboxData MatrixHitbox(
        float x,
        float y,
        float width,
        float height)
    {
        var value = new HitboxData();
        value.X = x;
        value.Y = y;
        value.Width = width;
        value.Height = height;
        return value;
    }

    private static void SetMalformedLayerOpacity(Layer layer, float opacity)
    {
        FieldInfo dataField = typeof(Layer).GetField(
            "Data",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Require(dataField != null);
        var data = (LayerData)dataField.GetValue(layer);
        data.Opacity = opacity;
        dataField.SetValue(layer, data);
    }

    private static void MatrixRecord(
        MatrixCommandModelEntry entry,
        IList<MatrixCommandModelEntry> undo,
        IList<MatrixCommandModelEntry> redo,
        ref long undoBytes,
        ref long redoBytes,
        int capacity,
        long byteCapacity)
    {
        redo.Clear();
        redoBytes = 0;
        if (entry.Bytes > byteCapacity)
        {
            undo.Clear();
            undoBytes = 0;
            return;
        }

        undo.Add(entry);
        undoBytes += entry.Bytes;
        while (undo.Count > capacity || undoBytes > byteCapacity)
        {
            undoBytes -= undo[0].Bytes;
            undo.RemoveAt(0);
        }
    }

    private sealed class MatrixCommandState
    {
        public long Value;
    }

    private readonly struct MatrixCommandModelEntry
    {
        public ICommand Command { get; }
        public int Delta { get; }
        public long Bytes { get; }

        public MatrixCommandModelEntry(ICommand command, int delta, long bytes)
        {
            Command = command;
            Delta = delta;
            Bytes = bytes;
        }
    }

    private sealed class MatrixSizedCommand : ISizedCommand
    {
        private readonly MatrixCommandState state;
        private readonly int delta;

        public long EstimatedBytes { get; }

        public MatrixSizedCommand(MatrixCommandState state, int delta, long estimatedBytes)
        {
            this.state = state;
            this.delta = delta;
            EstimatedBytes = estimatedBytes;
        }

        public void Execute() => state.Value += delta;
        public void Undo() => state.Value -= delta;
    }

    private sealed class MatrixUnsizedCommand : ICommand
    {
        private readonly MatrixCommandState state;
        private readonly int delta;

        public MatrixUnsizedCommand(MatrixCommandState state, int delta)
        {
            this.state = state;
            this.delta = delta;
        }

        public void Execute() => state.Value += delta;
        public void Undo() => state.Value -= delta;
    }
}
