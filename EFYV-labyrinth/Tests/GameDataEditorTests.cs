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
        int baseFactoryCount = Config.LabyMake.Schema.AssetDefinitions.Length + 1;
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
            new EntityAtlasMetadata { FrameWidth = 1 }, null);
        Check(metadataPresent.HasImportedData);
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
}
