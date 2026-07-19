using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;
using EFYV.Editor;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Models;
using UnityEditor;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;
using UnityEntityData = EFYV.Core.Data.EntityData;

internal static partial class Program
{
    private static void TestSchemaBlockIsolationAndAssetNames()
    {
        var asset = ScriptableObject.CreateInstance<GameAssetData>();
        var random = new Random(0x51A7);

        for (int pass = 0; pass < 512; pass++)
        {
            FastSchemaBlock block = default;
            int[] expected = new int[FastSchemaBlock.MaxSize];
            for (int slot = 0; slot < expected.Length; slot++)
            {
                expected[slot] = random.Next(int.MinValue, int.MaxValue);
                block.SetInt(slot, expected[slot]);
            }

            asset.SetSchemaBlock(block);
            FastSchemaBlock loaded = asset.GetSchemaBlock();
            for (int slot = 0; slot < expected.Length; slot++)
                Equal(expected[slot], loaded.GetInt(slot), "Schema slot changed during Unity serialization bridge copy.");

            loaded.SetInt(pass % expected.Length, ~expected[pass % expected.Length]);
            Equal(expected[pass % expected.Length], asset.GetSchemaBlock().GetInt(pass % expected.Length),
                "GetSchemaBlock returned aliased storage.");
        }

        int[] serialized = GetField<int[]>(asset, "schemaBlockData");
        Equal(FastSchemaBlock.MaxSize, serialized.Length);
        int[] canaryCopy = (int[])serialized.Clone();
        FastSchemaBlock copyOnly = asset.GetSchemaBlock();
        copyOnly.SetInt(0, 1234567);
        Check(serialized.SequenceEqual(canaryCopy), "Reading a schema block mutated serialized storage.");

        SetField(asset, "schemaBlockData", null);
        FastSchemaBlock missing = asset.GetSchemaBlock();
        for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++) Equal(0, missing.GetInt(slot));

        SetField(asset, "schemaBlockData", new int[FastSchemaBlock.MaxSize - 1]);
        FastSchemaBlock malformed = asset.GetSchemaBlock();
        for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++) Equal(0, malformed.GetInt(slot));
        malformed.SetInt(FastSchemaBlock.MaxSize - 1, int.MaxValue);
        asset.SetSchemaBlock(malformed);
        Equal(FastSchemaBlock.MaxSize, GetField<int[]>(asset, "schemaBlockData").Length);
        Equal(int.MaxValue, asset.GetSchemaBlock().GetInt(FastSchemaBlock.MaxSize - 1));

        string[] names = { null, string.Empty, "hero", "Hero", "alpha/beta", new string('x', 4096) };
        foreach (string name in names)
        {
            asset.assetName = name;
            Equal(name, asset.assetName);
            Equal(FastMath.FastHash(name), asset.GetSchemaBlock().GetInt((int)AssetSchema.AssetIdHash));

            var entity = ScriptableObject.CreateInstance<UnityEntityData>();
            entity.entityName = name;
            Equal(name, entity.entityName);
            Equal(FastMath.FastHash(name), entity.GetSchemaBlock().GetInt((int)AssetSchema.AssetIdHash));
        }

        SetField(asset, "_assetName", "validated-name");
        Invoke(asset, "OnValidate");
        Equal("validated-name", asset.assetName);
        Equal(FastMath.FastHash("validated-name"), asset.GetSchemaBlock().GetInt((int)AssetSchema.AssetIdHash));
    }

    private static void TestDesignableTypesAndRegistrationContracts()
    {
        Assembly assembly = typeof(GameAssetData).Assembly;
        Type[] designableTypes = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<DesignableAssetAttribute>() != null)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        Config.LabyMake.Schema.AssetRegistration[] registrations =
            Config.LabyMake.Schema.BuiltInAssetRegistrations;

        Equal(registrations.Length, designableTypes.Length);
        Equal(designableTypes.Length, designableTypes.Select(type => type.Name).Distinct(StringComparer.Ordinal).Count());
        Equal(designableTypes.Length, designableTypes
            .Select(type => type.GetCustomAttribute<DesignableAssetAttribute>().DisplayName)
            .Distinct(StringComparer.Ordinal).Count());

        var expectedByName = registrations.ToDictionary(item => item.AssetType, StringComparer.Ordinal);
        foreach (Type type in designableTypes)
        {
            Check(typeof(SchemaBackedAssetData).IsAssignableFrom(type));
            Check(!type.IsAbstract);
            Check(expectedByName.TryGetValue(type.Name, out Config.LabyMake.Schema.AssetRegistration registration));
            Equal(registration.DisplayName, type.GetCustomAttribute<DesignableAssetAttribute>().DisplayName);
            Type expectedBase = registration.BaseAssetType == nameof(BossData) ? typeof(BossData) :
                registration.BaseAssetType == nameof(EnemyData) ? typeof(EnemyData) : typeof(GameAssetData);
            Check(expectedBase.IsAssignableFrom(type), type.Name + " has the wrong runtime base type.");
            Check(ScriptableObject.CreateInstance(type) is SchemaBackedAssetData);
        }

        Type importerType = typeof(EFYVPixelArtImporter);
        var factories = (IDictionary)GetField(importerType, "AssetFactories", null);
        var assetTypes = (IDictionary)GetField(importerType, "AssetTypes", null);
        // #16e: exactly the four base archetypes plus the generated registrations;
        // the dead plain-EntityData factory is gone.
        int baseFactoryCount = Config.LabyMake.Schema.AssetDefinitions.Length;
        Equal(registrations.Length + baseFactoryCount, factories.Count);
        Equal(factories.Count, assetTypes.Count);

        foreach (Config.LabyMake.Schema.AssetRegistration registration in registrations)
        {
            Check(factories.Contains(registration.AssetType));
            Check(assetTypes.Contains(registration.AssetType));
            var created = (SchemaBackedAssetData)InvokeStatic(importerType, "CreateAssetData", registration.AssetType);
            Equal(registration.AssetType, created.GetType().Name);
        }

        Equal(null, InvokeStatic(importerType, "CreateAssetData", (object)null));
        Equal(null, InvokeStatic(importerType, "CreateAssetData", "not-registered"));
        Throws<ArgumentException>(() => EFYVPixelArtImporter.RegisterAssetFactory<GameAssetData>(null));
        Throws<ArgumentException>(() => EFYVPixelArtImporter.RegisterAssetFactory<GameAssetData>(string.Empty));

        const string custom = "CustomTestAsset";
        EFYVPixelArtImporter.RegisterAssetFactory<GameAssetData>(custom);
        Check(InvokeStatic(importerType, "CreateAssetData", custom) is GameAssetData);
        EFYVPixelArtImporter.RegisterAssetFactory<UnityEntityData>(custom);
        Check(InvokeStatic(importerType, "CreateAssetData", custom) is UnityEntityData,
            "Factory replacement did not atomically update the registered type.");
        factories.Remove(custom);
        assetTypes.Remove(custom);
    }

    private static void TestAtlasAndDirectionalImportData()
    {
        var entity = ScriptableObject.CreateInstance<UnityEntityData>();
        var frame0 = new Sprite { name = "frame0" };
        var frame1 = new Sprite { name = "frame1" };
        var frames = new[] { frame0, frame1 };
        var animations = new[]
        {
            new EntityAnimationMetadata { Name = "idle", FramesPerSecond = 8, StartFrame = 0, FrameCount = 2 }
        };
        var atlas = new EntityAtlasMetadata
        {
            FormatVersion = 1,
            FrameWidth = 16,
            FrameHeight = 24,
            AtlasWidth = 32,
            AtlasHeight = 24,
            Animations = animations
        };
        var hitboxes = new[]
        {
            new EntityHitboxRecord { FrameIndex = 1, HitboxType = "hurt", Bounds = new Rect(1, 2, 3, 4) }
        };

        entity.SetImportedAtlas(atlas, frames);
        entity.SetImportedHitboxes(hitboxes);
        Same(frames, entity.SpriteFrames);
        Same(frame0, entity.spriteSheet);
        Same(animations, entity.AtlasMetadata.Animations);
        Same(hitboxes, entity.Hitboxes);

        var replacementAtlas = atlas;
        replacementAtlas.FrameWidth = 99;
        entity.SetImportedAtlas(replacementAtlas, Array.Empty<Sprite>());
        Same(frames, entity.SpriteFrames);
        Same(frame0, entity.spriteSheet);
        Equal(99, entity.AtlasMetadata.FrameWidth);

        var living = ScriptableObject.CreateInstance<LivingEntityData>();
        FastMath.FacingDirection[] facings =
        {
            FastMath.FacingDirection.Up,
            FastMath.FacingDirection.Down,
            FastMath.FacingDirection.Left,
            FastMath.FacingDirection.Right
        };
        for (int i = 0; i < facings.Length; i++)
        {
            var facingFrame = new Sprite { name = facings[i].ToString() };
            var facingFrames = new[] { facingFrame, new Sprite() };
            EntityAtlasMetadata facingAtlas = atlas;
            facingAtlas.FrameWidth = 16 + i;
            var facingHitboxes = new[]
            {
                new EntityHitboxRecord { FrameIndex = i, HitboxType = "type-" + i, Bounds = new Rect(i, i, 1, 1) }
            };
            living.SetImportedFacing(facings[i], facingAtlas, facingFrames, facingHitboxes);
            Check(living.TryGetImportedFacing(facings[i], out EntityFacingImportData imported));
            Same(facingFrames, imported.Frames);
            Same(facingHitboxes, imported.Hitboxes);
            Equal(16 + i, imported.AtlasMetadata.FrameWidth);

            Sprite sheet = facings[i] == FastMath.FacingDirection.Up ? living.spriteSheetUp :
                facings[i] == FastMath.FacingDirection.Down ? living.spriteSheetDown :
                facings[i] == FastMath.FacingDirection.Left ? living.spriteSheetLeft : living.spriteSheetRight;
            Same(facingFrame, sheet);
        }

        Check(!living.TryGetImportedFacing((FastMath.FacingDirection)int.MaxValue, out EntityFacingImportData invalid));
        Check(!invalid.HasImportedData);

        Check(living.TryGetImportedFacing(FastMath.FacingDirection.Left, out EntityFacingImportData before));
        EntityAtlasMetadata metadataOnly = before.AtlasMetadata;
        metadataOnly.FrameHeight++;
        living.SetImportedFacing(FastMath.FacingDirection.Left, metadataOnly, null, null);
        Check(living.TryGetImportedFacing(FastMath.FacingDirection.Left, out EntityFacingImportData after));
        Same(before.Frames, after.Frames);
        Equal(before.AtlasMetadata.FrameHeight + 1, after.AtlasMetadata.FrameHeight);
        Equal(null, after.Hitboxes);

        var empty = new EntityFacingImportData(null, default, null);
        Check(!empty.HasImportedData);
        var metadataPresent = new EntityFacingImportData(null,
            new EntityAtlasMetadata { FrameWidth = 1, FrameHeight = 1 }, null);
        Check(metadataPresent.HasImportedData);
        // #36: a width-only atlas no longer counts as imported data.
        var widthOnly = new EntityFacingImportData(null,
            new EntityAtlasMetadata { FrameWidth = 1 }, null);
        Check(!widthOnly.HasImportedData);
    }

    private static AtlasMetadataJson ValidAtlas()
    {
        return new AtlasMetadataJson
        {
            formatVersion = Config.Backend.Exporter.CurrentFormatVersion,
            frameWidth = 16,
            frameHeight = 16,
            atlasWidth = 64,
            atlasHeight = 32,
            animations = new List<AnimationMetadataJson>
            {
                new AnimationMetadataJson { name = "idle", fps = 8, startFrame = 0, frameCount = 3 },
                new AnimationMetadataJson { name = "walk", fps = 12, startFrame = 3, frameCount = 2 }
            }
        };
    }

    private static void TestImporterValidationAndSpriteSlicing()
    {
        AtlasMetadataJson valid = ValidAtlas();
        Check(EFYVPixelArtImporter.IsValidAtlasMetadata(valid));

        var importer = new TextureImporter { assetPath = "Assets/Hero.png" };
        EFYVPixelArtImporter.ConfigureTextureImporter(importer, valid);
        Equal(TextureImporterType.Sprite, importer.textureType);
        Equal(Config.Game.Map.TextureMipmapsEnabled, importer.mipmapEnabled);
        Equal(FilterMode.Point, importer.filterMode);
        Equal(TextureImporterCompression.Uncompressed, importer.textureCompression);
        Check(importer.alphaIsTransparency);
        Near(Config.Shared.PixelsPerUnit, importer.spritePixelsPerUnit);
        Equal(Config.Game.Importer.MaxTextureSize, importer.maxTextureSize);
        Equal(TextureImporterNPOTScale.None, importer.npotScale);
        Equal(SpriteImportMode.Multiple, importer.spriteImportMode);
        Equal(5, importer.spritesheet.Length);

        for (int i = 0; i < importer.spritesheet.Length; i++)
        {
            SpriteMetaData slice = importer.spritesheet[i];
            Equal("Hero" + Config.Game.Importer.SpriteSliceNameSeparator +
                i.ToString(Config.Game.Importer.SpriteSliceIndexFormat), slice.name);
            Near((i % 4) * 16f, slice.rect.x);
            Near(32f - (((i / 4) + 1) * 16f), slice.rect.y);
            Near(16f, slice.rect.width);
            Near(16f, slice.rect.height);
            Near(Config.Game.Importer.SpritePivotNormalized, slice.pivot.x);
            Near(Config.Game.Importer.SpritePivotNormalized, slice.pivot.y);
        }

        var single = new TextureImporter { assetPath = "one.png", spritesheet = new SpriteMetaData[7] };
        EFYVPixelArtImporter.ConfigureTextureImporter(single, null);
        Equal(SpriteImportMode.Single, single.spriteImportMode);

        AtlasMetadataJson oneFrame = valid;
        oneFrame.atlasWidth = 16;
        oneFrame.atlasHeight = 16;
        oneFrame.animations = new List<AnimationMetadataJson>
        {
            new AnimationMetadataJson { name = "one", fps = 1, startFrame = 0, frameCount = 1 }
        };
        Check(EFYVPixelArtImporter.IsValidAtlasMetadata(oneFrame));
        EFYVPixelArtImporter.ConfigureTextureImporter(single, oneFrame);
        Equal(SpriteImportMode.Single, single.spriteImportMode);

        AtlasMetadataJson fullCapacity = valid;
        fullCapacity.animations = new List<AnimationMetadataJson>();
        Check(EFYVPixelArtImporter.IsValidAtlasMetadata(fullCapacity));
        EFYVPixelArtImporter.ConfigureTextureImporter(single, fullCapacity);
        Equal(SpriteImportMode.Multiple, single.spriteImportMode);
        Equal(8, single.spritesheet.Length);

        var invalid = new List<AtlasMetadataJson>();
        AtlasMetadataJson item;
        item = valid; item.formatVersion++; invalid.Add(item);
        item = valid; item.frameWidth = 0; invalid.Add(item);
        item = valid; item.frameHeight = -1; invalid.Add(item);
        item = valid; item.atlasWidth = 0; invalid.Add(item);
        item = valid; item.atlasHeight = -1; invalid.Add(item);
        item = valid; item.atlasWidth = 65; invalid.Add(item);
        item = valid; item.atlasHeight = 31; invalid.Add(item);
        item = valid; item.atlasWidth = Config.Game.Importer.MaxTextureSize + 1; invalid.Add(item);
        item = valid; item.atlasHeight = Config.Game.Importer.MaxTextureSize + 1; invalid.Add(item);
        item = valid; item.animations = null; invalid.Add(item);

        AnimationMetadataJson good = valid.animations[0];
        AnimationMetadataJson bad;
        item = valid; bad = good; bad.name = null; item.animations = new List<AnimationMetadataJson> { bad }; invalid.Add(item);
        item = valid; bad = good; bad.name = "  "; item.animations = new List<AnimationMetadataJson> { bad }; invalid.Add(item);
        item = valid; bad = good; bad.fps = 0; item.animations = new List<AnimationMetadataJson> { bad }; invalid.Add(item);
        item = valid; bad = good; bad.startFrame = -1; item.animations = new List<AnimationMetadataJson> { bad }; invalid.Add(item);
        item = valid; bad = good; bad.frameCount = 0; item.animations = new List<AnimationMetadataJson> { bad }; invalid.Add(item);
        item = valid; bad = good; bad.startFrame = 7; bad.frameCount = 2; item.animations = new List<AnimationMetadataJson> { bad }; invalid.Add(item);

        foreach (AtlasMetadataJson metadata in invalid)
            Check(!EFYVPixelArtImporter.IsValidAtlasMetadata(metadata), "Invalid atlas metadata was accepted.");

        object authoredCount = InvokeStatic(typeof(EFYVPixelArtImporter), "GetAuthoredFrameCount", valid, 8);
        Equal(5, (int)authoredCount);
        AtlasMetadataJson pastCapacity = valid;
        pastCapacity.animations = new List<AnimationMetadataJson>
        {
            new AnimationMetadataJson { name = "x", fps = 1, startFrame = 7, frameCount = 100 }
        };
        Equal(8, (int)InvokeStatic(typeof(EFYVPixelArtImporter), "GetAuthoredFrameCount", pastCapacity, 8));
    }

    private static void TestImporterConversionAndOutsideInImport()
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [Config.Game.Importer.KeyEntityName] = JsonValue("\"OutsideEnemy\""),
            [Config.Game.Importer.KeyMaxHealth] = JsonValue("123.5"),
            [Config.Game.Importer.KeyBaseSpeed] = JsonValue("4.25"),
            [Config.Game.Importer.KeyDamageToPlayer] = JsonValue("8.75"),
            [Config.Game.Importer.KeyExperienceValue] = JsonValue("19.5"),
            [Config.Game.Importer.KeyPhase2HealthThreshold] = JsonValue("50")
        };
        FastSchemaBlock block = default;
        for (int i = 0; i < FastSchemaBlock.MaxSize; i++) block.SetInt(i, unchecked((int)0x5A5A0000) + i);
        int[] before = new int[FastSchemaBlock.MaxSize];
        for (int i = 0; i < before.Length; i++) before[i] = block.GetInt(i);
        EFYVPixelArtImporter.ApplySchemaProperties(properties, ref block);
        Near(123.5f, block.GetFloat((int)AssetSchema.MaxHealth));
        Near(4.25f, block.GetFloat((int)AssetSchema.BaseSpeed));
        Near(8.75f, block.GetFloat((int)AssetSchema.DamageToPlayer));
        Near(19.5f, block.GetFloat((int)AssetSchema.ExperienceValue));
        Near(50f, block.GetFloat((int)AssetSchema.Phase2HealthThreshold));
        var changed = new HashSet<int>
        {
            (int)AssetSchema.MaxHealth, (int)AssetSchema.BaseSpeed,
            (int)AssetSchema.DamageToPlayer, (int)AssetSchema.ExperienceValue,
            (int)AssetSchema.Phase2HealthThreshold
        };
        for (int i = 0; i < before.Length; i++)
            if (!changed.Contains(i)) Equal(before[i], block.GetInt(i), "Importer overwrote a neighboring schema slot.");

        var wrongKind = new Dictionary<string, JsonElement>
        {
            [Config.Game.Importer.KeyMaxHealth] = JsonValue("\"not-a-number\"")
        };
        Throws<InvalidOperationException>(() =>
        {
            FastSchemaBlock target = default;
            EFYVPixelArtImporter.ApplySchemaProperties(wrongKind, ref target);
        });

        string[] paths =
        {
            "Assets/a.efyv", "Assets/a.b.c.EFYV", "a", "C:\\folder with spaces\\boss.efyv"
        };
        foreach (string path in paths)
        {
            Equal(Path.ChangeExtension(path, Config.Game.Importer.ExtensionPNG),
                EFYVPixelArtImporter.GetSiblingTexturePath(path));
            Equal(Path.ChangeExtension(path, Config.Game.Importer.ExtensionEFYV),
                EFYVPixelArtImporter.GetSiblingMetadataPath(path));
        }
        Equal(null, EFYVPixelArtImporter.GetSiblingTexturePath(null));
        Equal(null, EFYVPixelArtImporter.GetSiblingMetadataPath(null));

        AtlasMetadataJson valid = ValidAtlas();
        EntityAtlasMetadata converted = (EntityAtlasMetadata)InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertAtlasMetadata", (AtlasMetadataJson?)valid);
        Equal(valid.frameWidth, converted.FrameWidth);
        Equal(valid.animations.Count, converted.Animations.Length);
        Equal(valid.animations[1].name, converted.Animations[1].Name);
        Equal(null, ((EntityAtlasMetadata)InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertAtlasMetadata", (AtlasMetadataJson?)null)).Animations);

        var hitboxes = new List<HitboxJson>
        {
            new HitboxJson { frameIndex = 3, hitboxType = "hurt", x = 1, y = 2, width = 3, height = 4 }
        };
        var convertedHitboxes = (EntityHitboxRecord[])InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertHitboxes", hitboxes);
        Equal(1, convertedHitboxes.Length);
        Equal(3, convertedHitboxes[0].FrameIndex);
        Equal("hurt", convertedHitboxes[0].HitboxType);
        Near(4f, convertedHitboxes[0].Bounds.height);
        Equal(0, ((EntityHitboxRecord[])InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertHitboxes", (object)null)).Length);

        foreach (string authored in new[] { "Up", "Down", "Left", "Right" })
        {
            var format = new EFYVJsonFormat
            {
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyFacing] = JsonValue(JsonSerializer.Serialize(authored))
                }
            };
            object[] arguments = { format, default(FastMath.FacingDirection) };
            bool found = (bool)InvokeMethod(FindMethod(typeof(EFYVPixelArtImporter), "TryGetFacing", true, 2), null, arguments);
            Check(found);
            Equal(Enum.Parse<FastMath.FacingDirection>(authored), (FastMath.FacingDirection)arguments[1]);
        }
        var badFacing = new EFYVJsonFormat
        {
            properties = new Dictionary<string, JsonElement>
            {
                [Config.Game.Importer.KeyFacing] = JsonValue("\"Diagonal\"")
            }
        };
        object[] badArguments = { badFacing, default(FastMath.FacingDirection) };
        Check(!(bool)InvokeMethod(FindMethod(typeof(EFYVPixelArtImporter), "TryGetFacing", true, 2), null, badArguments));

        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-game-import-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string metadataPath = Path.Combine(tempRoot, "OutsideEnemy" + Config.Game.Importer.ExtensionEFYV);
            var format = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = properties,
                hitboxes = hitboxes,
                atlas = null
            };
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(format));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", metadataPath);

            string expectedAssetPath = Path.GetDirectoryName(metadataPath) +
                Config.Game.Importer.PathSeparator + "OutsideEnemy" + Config.Game.Importer.ExtensionAsset;
            EnemyData imported = AssetDatabase.LoadAssetAtPath<EnemyData>(expectedAssetPath);
            Check(imported != null, "Outside-in importer did not publish the ScriptableObject.");
            Equal("OutsideEnemy", imported.entityName);
            Near(123.5f, imported.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));
            Check(EditorUtility.DirtyObjects.Contains(imported));

            string invalidPath = Path.Combine(tempRoot, "Unknown" + Config.Game.Importer.ExtensionEFYV);
            format.assetType = "System.IO.FileInfo";
            format.properties[Config.Game.Importer.KeyEntityName] = JsonValue("\"Unknown\"");
            File.WriteAllText(invalidPath, JsonSerializer.Serialize(format));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", invalidPath);
            string invalidAssetPath = Path.GetDirectoryName(invalidPath) +
                Config.Game.Importer.PathSeparator + "Unknown" + Config.Game.Importer.ExtensionAsset;
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(invalidAssetPath));

            string malformedPath = Path.Combine(tempRoot, "Malformed" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(malformedPath, "{this is not json");
            int logsBefore = Debug.Messages.Count;
            InvokeStatic(typeof(EFYVPixelArtImporter), "OnPostprocessAllAssets",
                new[] { malformedPath }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Check(Debug.Messages.Count > logsBefore, "Postprocessor did not contain and report malformed JSON.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // ------------------------------------------------------------------
    // b2-pipeline-contract agent additions: the shared schema-field manifest
    // (#15), PropEntity schema-block reads, the baseAssetType/documentVersion
    // import contract (#16a/#16e), and the RawArt watcher (#12).
    // ------------------------------------------------------------------

    private static void TestSchemaManifestImportEndToEnd()
    {
        // Every manifest slot maps; nothing else in the block moves.
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [Config.Shared.EntityNameField] = JsonValue("\"ManifestProbe\""),
            [Config.Shared.MaxHealthField] = JsonValue("11.5"),
            [Config.Shared.BaseSpeedField] = JsonValue("2.25"),
            [Config.Shared.DamageToPlayerField] = JsonValue("3.5"),
            [Config.Shared.ExperienceValueField] = JsonValue("4.5"),
            [Config.Shared.Phase2HealthThresholdField] = JsonValue("5.5"),
            [Config.Shared.BaseDamageField] = JsonValue("6.5"),
            [Config.Shared.CooldownTimerField] = JsonValue("7.5"),
            [Config.Shared.IsWalkableField] = JsonValue("1"),
            [Config.Shared.TrapDamageField] = JsonValue("8.5"),
            [Config.Shared.FacingField] = JsonValue("\"Down\"")
        };
        FastSchemaBlock block = default;
        for (int i = 0; i < FastSchemaBlock.MaxSize; i++) block.SetInt(i, unchecked((int)0xA5A50000) + i);
        int[] before = new int[FastSchemaBlock.MaxSize];
        for (int i = 0; i < before.Length; i++) before[i] = block.GetInt(i);
        var unknown = new List<string>();
        EFYVPixelArtImporter.ApplySchemaProperties(properties, ref block, unknown);
        Near(11.5f, block.GetFloat((int)AssetSchema.MaxHealth));
        Near(2.25f, block.GetFloat((int)AssetSchema.BaseSpeed));
        Near(3.5f, block.GetFloat((int)AssetSchema.DamageToPlayer));
        Near(4.5f, block.GetFloat((int)AssetSchema.ExperienceValue));
        Near(5.5f, block.GetFloat((int)AssetSchema.Phase2HealthThreshold));
        Near(6.5f, block.GetFloat((int)AssetSchema.BaseDamage));
        Near(7.5f, block.GetFloat((int)AssetSchema.CooldownTimer));
        Equal(1, block.GetInt((int)AssetSchema.IsWalkable));
        Near(8.5f, block.GetFloat((int)AssetSchema.TrapDamage));
        Equal(0, unknown.Count, "Identity/facing keys must not be reported as unknown.");
        var touched = new HashSet<int>();
        foreach (Config.Shared.SchemaFieldMapping mapping in Config.Shared.AssetSchemaFieldManifest)
            touched.Add(mapping.Slot);
        for (int i = 0; i < before.Length; i++)
            if (!touched.Contains(i)) Equal(before[i], block.GetInt(i), "Manifest import touched slot " + i);

        // Boolean wire forms: JSON true/false and 0/1 numbers all land as flags.
        foreach ((string Wire, int Expected) sample in new[] { ("true", 1), ("false", 0), ("0", 0), ("7", 1) })
        {
            var boolProperties = new Dictionary<string, JsonElement>
            {
                [Config.Shared.IsWalkableField] = JsonValue(sample.Wire)
            };
            FastSchemaBlock boolBlock = default;
            boolBlock.SetInt((int)AssetSchema.IsWalkable, -123);
            EFYVPixelArtImporter.ApplySchemaProperties(boolProperties, ref boolBlock);
            Equal(sample.Expected, boolBlock.GetInt((int)AssetSchema.IsWalkable), "wire form " + sample.Wire);
        }
        var stringWalkable = new Dictionary<string, JsonElement>
        {
            [Config.Shared.IsWalkableField] = JsonValue("\"yes\"")
        };
        Throws<InvalidOperationException>(() =>
        {
            FastSchemaBlock target = default;
            EFYVPixelArtImporter.ApplySchemaProperties(stringWalkable, ref target);
        });

        // Unknown keys are reported (and logged end to end), never silently dropped.
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var format = new EFYVJsonFormat
            {
                assetType = nameof(GameAssetData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Shared.AssetNameField] = JsonValue("\"TrapTile\""),
                    [Config.Shared.IsWalkableField] = JsonValue("0"),
                    [Config.Shared.TrapDamageField] = JsonValue("42.5"),
                    [Config.Shared.BaseDamageField] = JsonValue("9"),
                    [Config.Shared.CooldownTimerField] = JsonValue("0.75"),
                    ["sparkleFactor"] = JsonValue("3"),
                    ["zzUnmapped"] = JsonValue("true")
                }
            };
            string path = Path.Combine(tempRoot, "TrapTile" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(path, JsonSerializer.Serialize(format));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", path);
            GameAssetData imported = AssetDatabase.LoadAssetAtPath<GameAssetData>(
                Path.GetDirectoryName(path) + Config.Game.Importer.PathSeparator +
                "TrapTile" + Config.Game.Importer.ExtensionAsset);
            Check(imported != null, "Manifest end-to-end import failed.");
            FastSchemaBlock importedBlock = imported.GetSchemaBlock();
            Equal(0, importedBlock.GetInt((int)AssetSchema.IsWalkable));
            Near(42.5f, importedBlock.GetFloat((int)AssetSchema.TrapDamage));
            Near(9f, importedBlock.GetFloat((int)AssetSchema.BaseDamage));
            Near(0.75f, importedBlock.GetFloat((int)AssetSchema.CooldownTimer));
            string expectedWarning = string.Format(
                Config.Game.Importer.LogWarningUnknownSchemaKeys,
                path,
                "sparkleFactor, zzUnmapped");
            Check(Debug.Messages.Contains(expectedWarning),
                "Unknown schema keys must be logged: " + expectedWarning);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void TestPropWalkableTrapDamageFromSchemaBlock()
    {
        var walkableAsset = ScriptableObject.CreateInstance<GameAssetData>();
        FastSchemaBlock walkableBlock = default;
        walkableBlock.SetInt((int)AssetSchema.IsWalkable, 1);
        walkableBlock.SetFloat((int)AssetSchema.TrapDamage, 12.5f);
        walkableAsset.SetSchemaBlock(walkableBlock);

        var prop = CreateComponent<ProbeProp>(addRenderer: true);
        prop.Initialize();
        prop.LoadData(walkableAsset);
        Check(prop.IsWalkable, "Imported IsWalkable=1 must read walkable.");
        Check(!prop.IsBlocking, "A walkable designer prop must not block.");
        Near(12.5f, prop.TrapDamage);

        // The designer flips walkability off in LabyMake; a live refresh must
        // re-sync the runtime blocking flag from the asset schema block.
        FastSchemaBlock blockedBlock = walkableBlock;
        blockedBlock.SetInt((int)AssetSchema.IsWalkable, 0);
        blockedBlock.SetFloat((int)AssetSchema.TrapDamage, 50f);
        walkableAsset.SetSchemaBlock(blockedBlock);
        prop.RefreshDataFromAsset();
        Check(!prop.IsWalkable);
        Check(prop.IsBlocking, "IsWalkable=0 must block after RefreshDataFromAsset.");
        Near(50f, prop.TrapDamage);

        // Hand-placed props without designer data keep the runtime flag as the
        // source of truth and deal no schema trap damage.
        var bare = CreateComponent<ProbeProp>(addRenderer: true);
        bare.Initialize();
        Check(bare.IsWalkable, "Default props are non-blocking, hence walkable.");
        Near(0f, bare.TrapDamage);
        bare.IsBlocking = true;
        Check(!bare.IsWalkable);

        // Subclass hardcodes still win over asset data because they run after
        // base.Initialize() (merchants must always block).
        var merchantData = ScriptableObject.CreateInstance<GameAssetData>();
        FastSchemaBlock merchantBlock = default;
        merchantBlock.SetInt((int)AssetSchema.IsWalkable, 1);
        merchantData.SetSchemaBlock(merchantBlock);
        var merchant = CreateComponent<EFYV.Core.Entities.Environment.Implementations.BaseMerchantProp>(addRenderer: true);
        SetField(merchant, "assetData", merchantData);
        merchant.Initialize();
        Check(merchant.IsBlocking, "Merchant Initialize must force blocking over asset data.");
    }

    private static void TestImporterBaseTypeFallbackAndDocumentVersion()
    {
        Type importerType = typeof(EFYVPixelArtImporter);
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-basetype-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            // A custom assetType with a known baseAssetType imports as the base
            // archetype (#16e) - config-only registrations reach the game.
            var custom = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = "FutureCustomData",
                baseAssetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"FutureCustom\""),
                    [Config.Shared.MaxHealthField] = JsonValue("77")
                }
            };
            string customPath = Path.Combine(tempRoot, "FutureCustom" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(customPath, JsonSerializer.Serialize(custom));
            InvokeStatic(importerType, "ImportEFYVAsset", customPath);
            EnemyData fallbackImported = AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(customPath) + Config.Game.Importer.PathSeparator +
                "FutureCustom" + Config.Game.Importer.ExtensionAsset);
            Check(fallbackImported != null, "baseAssetType fallback must import as the base archetype.");
            Equal(typeof(EnemyData), fallbackImported.GetType());
            Near(77f, fallbackImported.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));

            // Unknown assetType with unknown (or missing) baseAssetType rejects
            // with the per-cause message.
            var unknownBoth = custom;
            unknownBoth.assetType = "NoSuchData";
            unknownBoth.baseAssetType = "AlsoNoSuchData";
            unknownBoth.properties = new Dictionary<string, JsonElement>
            {
                [Config.Game.Importer.KeyEntityName] = JsonValue("\"NoSuch\"")
            };
            string unknownPath = Path.Combine(tempRoot, "NoSuch" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(unknownPath, JsonSerializer.Serialize(unknownBoth));
            InvokeStatic(importerType, "ImportEFYVAsset", unknownPath);
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(unknownPath) + Config.Game.Importer.PathSeparator +
                "NoSuch" + Config.Game.Importer.ExtensionAsset));
            Equal(string.Format(Config.Game.Importer.LogErrorUnknownAssetType, unknownPath, "NoSuchData"),
                Debug.Messages[^1]);

            // Unsupported document versions are rejected before any other work.
            // The importer accepts the whole supported RANGE (item #10), so the
            // "future" probe sits one past CurrentDocumentVersion.
            int futureVersion = Config.Backend.Exporter.CurrentDocumentVersion + 1;
            string futurePath = Path.Combine(tempRoot, "FutureDoc" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(
                futurePath,
                "{\"documentVersion\":" + futureVersion + ",\"assetType\":\"EnemyData\"," +
                "\"properties\":{\"entityName\":\"FutureDoc\"},\"hitboxes\":[]}");
            InvokeStatic(importerType, "ImportEFYVAsset", futurePath);
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(futurePath) + Config.Game.Importer.PathSeparator +
                "FutureDoc" + Config.Game.Importer.ExtensionAsset));
            Equal(string.Format(
                    Config.Game.Importer.LogErrorUnsupportedDocumentVersion,
                    futureVersion,
                    futurePath,
                    Config.Backend.Exporter.CurrentDocumentVersion),
                Debug.Messages[^1]);

            // Below the supported floor rejects the same way.
            int ancientVersion = Config.Backend.Exporter.MinSupportedDocumentVersion - 1;
            string ancientPath = Path.Combine(tempRoot, "AncientDoc" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(
                ancientPath,
                "{\"documentVersion\":" + ancientVersion + ",\"assetType\":\"EnemyData\"," +
                "\"properties\":{\"entityName\":\"AncientDoc\"},\"hitboxes\":[]}");
            InvokeStatic(importerType, "ImportEFYVAsset", ancientPath);
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(ancientPath) + Config.Game.Importer.PathSeparator +
                "AncientDoc" + Config.Game.Importer.ExtensionAsset));

            // Every version inside [MinSupported .. Current] imports.
            for (int version = Config.Backend.Exporter.MinSupportedDocumentVersion;
                version <= Config.Backend.Exporter.CurrentDocumentVersion;
                version++)
            {
                string stem = "RangeDoc" + version;
                string rangePath = Path.Combine(tempRoot, stem + Config.Game.Importer.ExtensionEFYV);
                File.WriteAllText(
                    rangePath,
                    "{\"documentVersion\":" + version + ",\"assetType\":\"EnemyData\"," +
                    "\"properties\":{\"entityName\":\"" + stem + "\"},\"hitboxes\":[]}");
                InvokeStatic(importerType, "ImportEFYVAsset", rangePath);
                Check(AssetDatabase.LoadAssetAtPath<EnemyData>(
                    Path.GetDirectoryName(rangePath) + Config.Game.Importer.PathSeparator +
                    stem + Config.Game.Importer.ExtensionAsset) != null,
                    "documentVersion " + version + " must be inside the supported range.");
            }

            // Version-absent legacy documents read as version 1 and import.
            string legacyPath = Path.Combine(tempRoot, "LegacyDoc" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(
                legacyPath,
                "{\"assetType\":\"EnemyData\",\"properties\":{\"entityName\":\"LegacyDoc\"},\"hitboxes\":[]}");
            InvokeStatic(importerType, "ImportEFYVAsset", legacyPath);
            Check(AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(legacyPath) + Config.Game.Importer.PathSeparator +
                "LegacyDoc" + Config.Game.Importer.ExtensionAsset) != null);

            // Malformed and vanished files get their own causes (#16c/#16d).
            string malformedPath = Path.Combine(tempRoot, "Broken" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(malformedPath, "{not json");
            InvokeStatic(importerType, "ImportEFYVAsset", malformedPath);
            Equal(string.Format(Config.Game.Importer.LogErrorMalformed, malformedPath), Debug.Messages[^1]);
            string ghostPath = Path.Combine(tempRoot, "Ghost" + Config.Game.Importer.ExtensionEFYV);
            InvokeStatic(importerType, "ImportEFYVAsset", ghostPath);
            Equal(string.Format(Config.Game.Importer.LogErrorMissingFile, ghostPath), Debug.Messages[^1]);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void TestRawArtWatcherDebounceAndPolling()
    {
        // Pure tracker contract first: baseline, debounce, coalescing, deletion.
        var tracker = new RawArtChangeTracker(0.3d);
        var emitted = new List<string>();
        var files = new List<(string Path, long Stamp)>
        {
            ("Assets/RawArt/Hero.efyvlaby", 100L),
            ("Assets/RawArt/Hero.png", 100L)
        };
        Check(!tracker.Update(10.0d, files, emitted), "First scan is a baseline, never an import.");
        Equal(0, tracker.PendingCount);
        Check(!tracker.Update(10.5d, files, emitted), "Unchanged snapshots stay idle.");

        files[0] = (files[0].Path, 101L);
        Check(!tracker.Update(11.0d, files, emitted), "A fresh change must wait out the quiet window.");
        Equal(1, tracker.PendingCount);
        files[1] = (files[1].Path, 101L);
        Check(!tracker.Update(11.2d, files, emitted), "Churn keeps extending the window.");
        Equal(2, tracker.PendingCount);
        Check(!tracker.Update(11.49d, files, emitted), "Still inside the quiet window.");
        Check(tracker.Update(11.5d, files, emitted), "Quiet for debounceSeconds -> emit.");
        Equal(2, emitted.Count);
        Equal("Assets/RawArt/Hero.efyvlaby", emitted[0]);
        Equal("Assets/RawArt/Hero.png", emitted[1]);
        Equal(0, tracker.PendingCount);
        emitted.Clear();
        Check(!tracker.Update(12.0d, files, emitted), "Emission drains the pending set.");

        // A new file appears and is then deleted before the window closes: no import.
        files.Add(("Assets/RawArt/Ghost.png", 5L));
        Check(!tracker.Update(13.0d, files, emitted));
        Equal(1, tracker.PendingCount);
        files.RemoveAt(2);
        Check(!tracker.Update(13.1d, files, emitted));
        Equal(0, tracker.PendingCount);
        Check(!tracker.Update(14.0d, files, emitted), "Nothing pending, nothing to emit.");

        // Deleted-then-recreated file re-imports with a fresh stamp.
        files.Add(("Assets/RawArt/Ghost.png", 6L));
        tracker.Update(15.0d, files, emitted);
        Check(tracker.Update(15.3d, files, emitted));
        Equal(1, emitted.Count);
        Equal("Assets/RawArt/Ghost.png", emitted[0]);
        emitted.Clear();

        Throws<ArgumentOutOfRangeException>(() => new RawArtChangeTracker(-0.1d));
        Throws<ArgumentOutOfRangeException>(() => new RawArtChangeTracker(double.NaN));
        Throws<ArgumentNullException>(() => tracker.Update(1d, null, emitted));
        Throws<ArgumentNullException>(() => tracker.Update(1d, files, null));

        // End to end through the [InitializeOnLoad] poller with real files and
        // the stubbed AssetDatabase, including the poll-interval gate.
        string watchRoot = Path.Combine(Path.GetTempPath(), "efyv-rawart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(watchRoot);
        string originalRoot = EFYVRawArtWatcher.WatchRoot;
        try
        {
            EFYVRawArtWatcher.WatchRoot = watchRoot;
            SetField(typeof(EFYVRawArtWatcher), "nextPollTime", null, 0d);
            double interval = Config.Game.RawArtWatcher.PollIntervalSeconds;
            double debounce = Config.Game.RawArtWatcher.DebounceSeconds;
            double now = 1000d;

            EditorApplication.timeSinceStartup = now;
            EFYVRawArtWatcher.Poll(); // baseline scan of the empty directory
            int importsBefore = AssetDatabase.Imports.Count;

            string metadataPath = Path.Combine(watchRoot, "Hero" + Config.Game.Importer.ExtensionEFYV);
            string texturePath = Path.Combine(watchRoot, "Hero" + Config.Game.Importer.ExtensionPNG);
            string ignoredPath = Path.Combine(watchRoot, "notes.txt");
            File.WriteAllText(metadataPath, "{}");
            File.WriteAllText(texturePath, "png");
            File.WriteAllText(ignoredPath, "ignored");

            // Inside the poll interval nothing happens (main-thread budget guard).
            EditorApplication.timeSinceStartup = now + (interval / 2d);
            EFYVRawArtWatcher.Poll();
            Equal(importsBefore, AssetDatabase.Imports.Count);

            now += interval;
            EditorApplication.timeSinceStartup = now;
            EFYVRawArtWatcher.Poll(); // change detected, debounce window opens
            Equal(importsBefore, AssetDatabase.Imports.Count);

            now += interval + debounce;
            EditorApplication.timeSinceStartup = now;
            EFYVRawArtWatcher.Poll(); // quiet window elapsed -> batched import
            Equal(importsBefore + 2, AssetDatabase.Imports.Count);
            Equal(metadataPath, AssetDatabase.Imports[^2].Path);
            Equal(texturePath, AssetDatabase.Imports[^1].Path);
            Check(Debug.Messages.Contains(string.Format(
                Config.Game.RawArtWatcher.LogImported, 2, watchRoot)));

            // A republish of one file (the live loop's steady state) imports once
            // more; the .txt file never does.
            File.SetLastWriteTimeUtc(texturePath, File.GetLastWriteTimeUtc(texturePath).AddSeconds(3));
            now += interval;
            EditorApplication.timeSinceStartup = now;
            EFYVRawArtWatcher.Poll();
            now += interval + debounce;
            EditorApplication.timeSinceStartup = now;
            EFYVRawArtWatcher.Poll();
            Equal(importsBefore + 3, AssetDatabase.Imports.Count);
            Equal(texturePath, AssetDatabase.Imports[^1].Path);
            foreach ((string Path, ImportAssetOptions Options) import in AssetDatabase.Imports)
                Check(!string.Equals(import.Path, ignoredPath, StringComparison.OrdinalIgnoreCase));

            // Steady state with no further changes stays quiet.
            now += interval;
            EditorApplication.timeSinceStartup = now;
            EFYVRawArtWatcher.Poll();
            Equal(importsBefore + 3, AssetDatabase.Imports.Count);
        }
        finally
        {
            EFYVRawArtWatcher.WatchRoot = originalRoot;
            Directory.Delete(watchRoot, true);
        }
    }

    private static void TestHitboxCalculationsAndLiveRefresh()
    {
        var atlas = new EntityAtlasMetadata { FrameWidth = 32, FrameHeight = 48 };
        Check(EFYVHitboxGizmo.TryGetLocalBounds(
            atlas, new Rect(0.25f, 0.5f, 0.75f, 1.25f), out Vector3 center, out Vector3 size));
        float pivot = Config.Game.Importer.SpritePivotNormalized;
        Near(0.25f + (0.75f * pivot) - ((32f / Config.Shared.PixelsPerUnit) * pivot), center.x);
        Near(((48f / Config.Shared.PixelsPerUnit) * pivot) - 0.5f - (1.25f * pivot), center.y);
        Near(0f, center.z);
        Near(0.75f, size.x);
        Near(1.25f, size.y);

        float[] badFloats = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        foreach (float bad in badFloats)
        {
            Check(!EFYVHitboxGizmo.TryGetLocalBounds(atlas, new Rect(bad, 0, 1, 1), out _, out _));
            Check(!EFYVHitboxGizmo.TryGetLocalBounds(atlas, new Rect(0, bad, 1, 1), out _, out _));
            Check(!EFYVHitboxGizmo.TryGetLocalBounds(atlas, new Rect(0, 0, bad, 1), out _, out _));
            Check(!EFYVHitboxGizmo.TryGetLocalBounds(atlas, new Rect(0, 0, 1, bad), out _, out _));
        }
        Check(!EFYVHitboxGizmo.TryGetLocalBounds(default, new Rect(0, 0, 1, 1), out _, out _));
        Check(!EFYVHitboxGizmo.TryGetLocalBounds(atlas, new Rect(0, 0, 0, 1), out _, out _));
        Check(!EFYVHitboxGizmo.TryGetLocalBounds(atlas, new Rect(0, 0, 1, -1), out _, out _));

        var data = ScriptableObject.CreateInstance<LivingEntityData>();
        FastSchemaBlock block = default;
        block.SetFloat((int)AssetSchema.MaxHealth, 100f);
        block.SetFloat((int)AssetSchema.BaseSpeed, 2f);
        data.SetSchemaBlock(block);
        var live = CreateComponent<ProbeLiving>(addRenderer: true);
        live.Initialize();
        live.LoadData(data);

        EFYVLiveDebugBridge.QueueRefresh(data);
        EditorApplication.InvokeDelayCalls();
        Equal(0, live.RefreshCalls);

        EditorApplication.isPlaying = true;
        block.SetFloat((int)AssetSchema.MaxHealth, 250f);
        data.SetSchemaBlock(block);
        EFYVLiveDebugBridge.QueueRefresh(data);
        EFYVLiveDebugBridge.QueueRefresh(data);
        EFYVLiveDebugBridge.QueueRefresh(data);
        EditorApplication.InvokeDelayCalls();
        Equal(1, live.RefreshCalls);
        Near(250f, live.MaxHealth);

        live.gameObject.scene = new UnityEngine.SceneManagement.Scene(false, false);
        block.SetFloat((int)AssetSchema.MaxHealth, 300f);
        data.SetSchemaBlock(block);
        EFYVLiveDebugBridge.QueueRefresh(data);
        EditorApplication.InvokeDelayCalls();
        Equal(1, live.RefreshCalls);

        live.gameObject.scene = new UnityEngine.SceneManagement.Scene(true, true);
        EFYVLiveDebugBridge.QueueRefresh(data);
        EditorApplication.isPlaying = false;
        EditorApplication.InvokeDelayCalls();
        Equal(1, live.RefreshCalls);

        var propData = ScriptableObject.CreateInstance<GameAssetData>();
        var first = new Sprite();
        var second = new Sprite();
        propData.sprite = first;
        var prop = CreateComponent<ProbeProp>(addRenderer: true);
        prop.Initialize();
        prop.LoadData(propData);
        Same(first, prop.spriteRenderer.sprite);
        propData.sprite = second;
        EditorApplication.isPlaying = true;
        EFYVLiveDebugBridge.QueueRefresh(propData);
        EditorApplication.InvokeDelayCalls();
        Same(second, prop.spriteRenderer.sprite);
    }

    // batch3.3 agent (item #10): the importer reads the OPTIONAL atlas-animation
    // timing/playback fields (frameDurationsMs with 0 = inherit-fps sentinel,
    // loopStart/loopEnd, pingPong) and resolves the effective defaults for
    // runtime consumers; absent fields fall back to fps and the full range.
    private static void TestImporterAnimationTimingMetadata()
    {
        // Conversion resolves defaults per animation.
        AtlasMetadataJson atlas = ValidAtlas();
        AnimationMetadataJson walk = atlas.animations[1];
        walk.frameDurationsMs = new List<int> { 90, 0 };
        walk.loopStart = 1;
        walk.loopEnd = 1;
        walk.pingPong = true;
        atlas.animations[1] = walk;
        Check(EFYVPixelArtImporter.IsValidAtlasMetadata(atlas), "Timed atlas must validate.");

        EntityAtlasMetadata converted = (EntityAtlasMetadata)InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertAtlasMetadata", (AtlasMetadataJson?)atlas);
        // "idle" carried no optional fields: fps-only timing, full loop range.
        Equal(null, converted.Animations[0].FrameDurationsMs);
        Equal(0, converted.Animations[0].LoopStartFrame);
        Equal(atlas.animations[0].frameCount - 1, converted.Animations[0].LoopEndFrame);
        Check(!converted.Animations[0].PingPong);
        // "walk" resolves every populated value; the durations array is a COPY.
        Check(converted.Animations[1].FrameDurationsMs != null);
        Equal(2, converted.Animations[1].FrameDurationsMs.Length);
        Equal(90, converted.Animations[1].FrameDurationsMs[0]);
        Equal(0, converted.Animations[1].FrameDurationsMs[1]);
        Equal(1, converted.Animations[1].LoopStartFrame);
        Equal(1, converted.Animations[1].LoopEndFrame);
        Check(converted.Animations[1].PingPong);
        walk.frameDurationsMs[0] = 555;
        Equal(90, converted.Animations[1].FrameDurationsMs[0]);
        walk.frameDurationsMs[0] = 90; // restore: the atlas below reuses this list

        // End to end: a documentVersion-2 .efyvlaby with a timed atlas block
        // imports and lands the resolved metadata on the ScriptableObject.
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-animtiming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var document = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"TimedImport\"")
                },
                hitboxes = new List<HitboxJson>(),
                atlas = atlas
            };
            string path = Path.Combine(tempRoot, "TimedImport" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(path, JsonSerializer.Serialize(document));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", path);
            EnemyData imported = AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(path) + Config.Game.Importer.PathSeparator +
                "TimedImport" + Config.Game.Importer.ExtensionAsset);
            Check(imported != null, "Timed .efyvlaby must import.");
            EntityAnimationMetadata importedWalk = imported.AtlasMetadata.Animations[1];
            Equal("walk", importedWalk.Name);
            Equal(12, importedWalk.FramesPerSecond);
            Equal(90, importedWalk.FrameDurationsMs[0]);
            Equal(0, importedWalk.FrameDurationsMs[1]);
            Equal(1, importedWalk.LoopStartFrame);
            Equal(1, importedWalk.LoopEndFrame);
            Check(importedWalk.PingPong);
            EntityAnimationMetadata importedIdle = imported.AtlasMetadata.Animations[0];
            Equal(null, importedIdle.FrameDurationsMs);
            Equal(0, importedIdle.LoopStartFrame);
            Equal(2, importedIdle.LoopEndFrame);
            Check(!importedIdle.PingPong);

            // A broken timing block (duration count mismatch) rejects the
            // whole import with the per-cause atlas error.
            AtlasMetadataJson broken = ValidAtlas();
            AnimationMetadataJson brokenWalk = broken.animations[1];
            brokenWalk.frameDurationsMs = new List<int> { 10 };
            broken.animations[1] = brokenWalk;
            var brokenDocument = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"BrokenTiming\"")
                },
                hitboxes = new List<HitboxJson>(),
                atlas = broken
            };
            string brokenPath = Path.Combine(tempRoot, "BrokenTiming" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(brokenPath, JsonSerializer.Serialize(brokenDocument));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", brokenPath);
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(brokenPath) + Config.Game.Importer.PathSeparator +
                "BrokenTiming" + Config.Game.Importer.ExtensionAsset));
            Equal(string.Format(
                    Config.Game.Importer.LogErrorInvalidAtlas,
                    brokenPath,
                    EFYVBackend.Core.Export.AtlasMetadataError.AnimationFrameDurations),
                Debug.Messages[^1]);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
