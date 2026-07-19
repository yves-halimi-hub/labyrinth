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
using EFYV.Core.Interfaces;
using EFYV.Editor;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Models;
using UnityEditor;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;
using FacingDirection = EFYVBackend.Core.Math.FastMath.FacingDirection;
using UnityEntityData = EFYV.Core.Data.EntityData;

internal static partial class Program
{
    // Independent FNV-1a reference implementation (mirrors the documented contract of
    // FastMath.FastHash) used to verify every hash the data/editor layer persists.
    private static int DataEditorReferenceFnvHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return Config.Backend.Serialization.NullHash;
        unchecked
        {
            uint hash = Config.Backend.Math.FnvOffsetBasis;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= Config.Backend.Math.FnvPrime;
            }
            return (int)hash;
        }
    }

    private static Sprite DataEditorSheetFor(LivingEntityData data, FacingDirection facing)
    {
        return facing == FacingDirection.Up ? data.spriteSheetUp :
            facing == FacingDirection.Down ? data.spriteSheetDown :
            facing == FacingDirection.Left ? data.spriteSheetLeft : data.spriteSheetRight;
    }

    private static void DataEditorResetTextureSentinel(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Default;
        importer.spriteImportMode = SpriteImportMode.None;
        importer.spritesheet = null;
        importer.filterMode = FilterMode.Trilinear;
    }

    private static string DataEditorWriteEfyvFile(string directory, string stem, EFYVJsonFormat format)
    {
        string path = Path.Combine(directory, stem + Config.Game.Importer.ExtensionEFYV);
        File.WriteAllText(path, JsonSerializer.Serialize(format));
        return path;
    }

    private static void TestDataEditorSchemaBlockBitPreservation()
    {
        FastRandom.SetSeed(0xD1CE0001u);
        var asset = ScriptableObject.CreateInstance<GameAssetData>();

        // The unsafe MemoryCopy in SchemaBackedAssetData copies exactly MaxSize * sizeof(int)
        // bytes, so the marshalled struct size must match or the copy would over/under-run.
        Equal(FastSchemaBlock.MaxSize * sizeof(int),
            System.Runtime.InteropServices.Marshal.SizeOf<FastSchemaBlock>());

        float[] adversarialFloats =
        {
            0f, -0f, 1f, -1f, float.MaxValue, float.MinValue, float.Epsilon, -float.Epsilon,
            float.PositiveInfinity, float.NegativeInfinity, float.NaN,
            BitConverter.Int32BitsToSingle(0x7FC00001),
            BitConverter.Int32BitsToSingle(unchecked((int)0xFFC12345)),
            BitConverter.Int32BitsToSingle(0x7F800001),
            BitConverter.Int32BitsToSingle(1),
            BitConverter.Int32BitsToSingle(unchecked((int)0x80000001)),
            MathF.PI
        };
        for (int i = 0; i < adversarialFloats.Length; i++)
        {
            int slot = (i * 5) % FastSchemaBlock.MaxSize;
            int expectedBits = BitConverter.SingleToInt32Bits(adversarialFloats[i]);
            FastSchemaBlock block = default;
            block.SetFloat(slot, adversarialFloats[i]);
            asset.SetSchemaBlock(block);
            int[] serialized = GetField<int[]>(asset, "schemaBlockData");
            Equal(expectedBits, serialized[slot], "Unity bridge altered float bits during serialization.");
            FastSchemaBlock loaded = asset.GetSchemaBlock();
            Equal(expectedBits, BitConverter.SingleToInt32Bits(loaded.GetFloat(slot)),
                "Float bits changed on read-back through the Unity bridge.");
            Equal(expectedBits, loaded.GetInt(slot));
        }

        // Reference model: a plain int[64] mirrors every write; the serialized Unity field and
        // both typed readers must agree with it bit-for-bit across randomized int/float mixes.
        var random = new Random(0x0B17F00D);
        int[] model = new int[FastSchemaBlock.MaxSize];
        for (int pass = 0; pass < 96; pass++)
        {
            FastSchemaBlock block = default;
            for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
            {
                if (random.Next(2) == 0)
                {
                    int value = random.Next(int.MinValue, int.MaxValue);
                    block.SetInt(slot, value);
                    model[slot] = value;
                }
                else
                {
                    float value = (float)((random.NextDouble() * 2e6) - 1e6);
                    block.SetFloat(slot, value);
                    model[slot] = BitConverter.SingleToInt32Bits(value);
                }
            }

            asset.SetSchemaBlock(block);
            int[] serialized = GetField<int[]>(asset, "schemaBlockData");
            Equal(FastSchemaBlock.MaxSize, serialized.Length);
            FastSchemaBlock loaded = asset.GetSchemaBlock();
            for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
            {
                Equal(model[slot], serialized[slot]);
                Equal(model[slot], loaded.GetInt(slot));
                Equal(model[slot], BitConverter.SingleToInt32Bits(loaded.GetFloat(slot)));
            }
        }

        // An oversized backing array is malformed and must read as all-zero, and the next
        // write must reallocate it back to the exact schema size (guard against overruns).
        var oversized = new int[FastSchemaBlock.MaxSize + 1];
        for (int i = 0; i < oversized.Length; i++) oversized[i] = 0x1BADB002 + i;
        SetField(asset, "schemaBlockData", oversized);
        FastSchemaBlock zeros = asset.GetSchemaBlock();
        for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
            Equal(0, zeros.GetInt(slot), "Oversized backing array must be treated as absent.");
        FastSchemaBlock replacement = default;
        replacement.SetInt(0, 111);
        replacement.SetInt(FastSchemaBlock.MaxSize - 1, -222);
        asset.SetSchemaBlock(replacement);
        int[] reallocated = GetField<int[]>(asset, "schemaBlockData");
        NotSame(oversized, reallocated);
        Equal(FastSchemaBlock.MaxSize, reallocated.Length);
        Equal(111, reallocated[0]);
        Equal(-222, reallocated[FastSchemaBlock.MaxSize - 1]);

        // Instances never share backing storage.
        var second = ScriptableObject.CreateInstance<UnityEntityData>();
        NotSame(GetField<int[]>(asset, "schemaBlockData"), GetField<int[]>(second, "schemaBlockData"));
        FastSchemaBlock fresh = second.GetSchemaBlock();
        for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++) Equal(0, fresh.GetInt(slot));
    }

    private static void TestDataEditorAssetNameHashReferenceModel()
    {
        FastRandom.SetSeed(0xD1CE0002u);

        string[] corpus =
        {
            null, string.Empty, " ", "a", "A", "hero", "Hero", "hero ", " hero",
            "\0", "a\0b", "￿￿￿", "🎮", "שלום",
            new string('x', 2048), "Assets/Enemies/Boss #1"
        };
        foreach (string candidate in corpus)
            Equal(DataEditorReferenceFnvHash(candidate), FastMath.FastHash(candidate),
                "FastHash diverged from the FNV-1a reference.");

        var random = new Random(0xDA7A01);
        for (int i = 0; i < 200; i++)
        {
            int length = random.Next(0, 64);
            var chars = new char[length];
            for (int j = 0; j < length; j++) chars[j] = (char)random.Next(1, 0x10000);
            string candidate = new string(chars);
            Equal(DataEditorReferenceFnvHash(candidate), FastMath.FastHash(candidate));
        }

        Check(FastMath.FastHash("Hero") != FastMath.FastHash("hero"), "FNV-1a must be case-sensitive.");
        Equal(Config.Backend.Serialization.NullHash, FastMath.FastHash(null));
        Equal(Config.Backend.Serialization.NullHash, FastMath.FastHash(string.Empty));

        // Setting the display name must only rewrite the AssetIdHash slot; all other schema
        // slots hold live design data and must survive a rename byte-for-byte.
        var asset = ScriptableObject.CreateInstance<GameAssetData>();
        var slotRandom = new Random(0x5EED5);
        FastSchemaBlock seeded = default;
        int[] expectedSlots = new int[FastSchemaBlock.MaxSize];
        for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
        {
            expectedSlots[slot] = slotRandom.Next(int.MinValue, int.MaxValue);
            seeded.SetInt(slot, expectedSlots[slot]);
        }
        asset.SetSchemaBlock(seeded);
        asset.assetName = "isolation-check";
        FastSchemaBlock renamed = asset.GetSchemaBlock();
        Equal(DataEditorReferenceFnvHash("isolation-check"), renamed.GetInt((int)AssetSchema.AssetIdHash));
        for (int slot = 1; slot < FastSchemaBlock.MaxSize; slot++)
            Equal(expectedSlots[slot], renamed.GetInt(slot), "assetName setter clobbered slot " + slot);

        var entity = ScriptableObject.CreateInstance<UnityEntityData>();
        entity.SetSchemaBlock(seeded);
        entity.entityName = "isolation-check";
        FastSchemaBlock renamedEntity = entity.GetSchemaBlock();
        Equal(DataEditorReferenceFnvHash("isolation-check"), renamedEntity.GetInt((int)AssetSchema.AssetIdHash));
        for (int slot = 1; slot < FastSchemaBlock.MaxSize; slot++)
            Equal(expectedSlots[slot], renamedEntity.GetInt(slot), "entityName setter clobbered slot " + slot);

        // EntityData.OnValidate must resync the hash from the serialized backing field the same
        // way the GameAssetData path (already covered elsewhere) does.
        SetField(entity, "_entityName", "validated-entity");
        Invoke(entity, "OnValidate");
        Equal("validated-entity", entity.entityName);
        Equal(DataEditorReferenceFnvHash("validated-entity"),
            entity.GetSchemaBlock().GetInt((int)AssetSchema.AssetIdHash));
        for (int slot = 1; slot < FastSchemaBlock.MaxSize; slot++)
            Equal(expectedSlots[slot], entity.GetSchemaBlock().GetInt(slot));

        // Renaming to null resets the hash to the null sentinel and still keeps other slots.
        entity.entityName = null;
        Equal(Config.Backend.Serialization.NullHash,
            entity.GetSchemaBlock().GetInt((int)AssetSchema.AssetIdHash));
        Equal(expectedSlots[1], entity.GetSchemaBlock().GetInt(1));
    }

    private static void TestDataEditorLegacyAchievementSyncAndHashes()
    {
        FastRandom.SetSeed(0xD1CE0003u);

        var definition = new LegacyAchievementDefinition { id = 12, title = "Alpha", description = "Beta" };
        Equal(12, definition.id);
        Equal(12, definition.Data.Id);
        Equal(12, GetField<int>(definition, "_id"));
        Equal("Alpha", definition.title);
        Equal("Beta", definition.description);
        Equal(DataEditorReferenceFnvHash("Alpha"), definition.Data.TitleHash);
        Equal(DataEditorReferenceFnvHash("Beta"), definition.Data.DescriptionHash);

        // Definitions are value types: mutating a copy must never leak back into the original.
        LegacyAchievementDefinition copy = definition;
        copy.title = "Gamma";
        copy.id = 99;
        Equal(DataEditorReferenceFnvHash("Alpha"), definition.Data.TitleHash);
        Equal(12, definition.Data.Id);
        Equal(DataEditorReferenceFnvHash("Gamma"), copy.Data.TitleHash);
        Equal(99, copy.Data.Id);

        var iconSprite = new Sprite { name = "icon" };
        copy.icon = iconSprite;
        copy.SyncData();
        Same(iconSprite, copy.icon);

        // Simulate Unity deserialization: only the private serialized fields are populated, so
        // the packed Data block is stale until SyncData runs.
        object boxed = new LegacyAchievementDefinition();
        SetField(boxed, "_id", 9);
        SetField(boxed, "_title", "Stale");
        var stale = (LegacyAchievementDefinition)boxed;
        // #24 (flipped pin, batch2/game-managers agent): the id getter now falls
        // back to the serialized _id while the packed block is stale (Data.Id == 0),
        // so player builds - where OnValidate never runs - read the right id.
        Equal(9, stale.id);
        Equal(0, stale.Data.Id, "The packed block itself stays stale until SyncData.");
        Equal("Stale", stale.title);
        Equal(Config.Backend.Serialization.NullHash, stale.Data.TitleHash);
        stale.SyncData();
        Equal(9, stale.id);
        Equal(9, stale.Data.Id);
        Equal(DataEditorReferenceFnvHash("Stale"), stale.Data.TitleHash);
        Equal(Config.Backend.Serialization.NullHash, stale.Data.DescriptionHash);

        // Database OnValidate must sync every element and write the struct back into the list.
        var database = ScriptableObject.CreateInstance<LegacyAchievementDatabase>();
        for (int i = 0; i < 5; i++)
        {
            object entry = new LegacyAchievementDefinition();
            SetField(entry, "_id", 100 + i);
            SetField(entry, "_title", "Title-" + i);
            SetField(entry, "_description", i % 2 == 0 ? "Desc-" + i : null);
            database.achievements.Add((LegacyAchievementDefinition)entry);
        }
        Invoke(database, "OnValidate");
        for (int i = 0; i < database.achievements.Count; i++)
        {
            LegacyAchievementDefinition synced = database.achievements[i];
            Equal(100 + i, synced.Data.Id);
            Equal(DataEditorReferenceFnvHash("Title-" + i), synced.Data.TitleHash);
            Equal(DataEditorReferenceFnvHash(i % 2 == 0 ? "Desc-" + i : null), synced.Data.DescriptionHash);
        }

        // PopulateBasis clears before filling, so a second run cannot duplicate entries, and
        // every generated definition must carry backend hashes matching the FNV reference.
        Invoke(database, "PopulateBasis");
        Invoke(database, "PopulateBasis");
        string[] titles = Config.Game.Achievements.BasisData.Titles;
        string[] descriptions = Config.Game.Achievements.BasisData.Descriptions;
        Equal(titles.Length, database.achievements.Count);
        Equal(descriptions.Length, database.achievements.Count);
        for (int i = 0; i < database.achievements.Count; i++)
        {
            LegacyAchievementDefinition basis = database.achievements[i];
            Equal(i, basis.id);
            Equal(i, basis.Data.Id);
            Equal(titles[i], basis.title);
            Equal(descriptions[i], basis.description);
            Equal(DataEditorReferenceFnvHash(titles[i]), basis.Data.TitleHash);
            Equal(DataEditorReferenceFnvHash(descriptions[i]), basis.Data.DescriptionHash);
        }
        Equal(titles.Length, database.achievements.Select(item => item.Data.TitleHash).Distinct().Count(),
            "Shipped basis titles must not collide under FNV-1a.");
        Equal(descriptions.Length, database.achievements.Select(item => item.Data.DescriptionHash).Distinct().Count(),
            "Shipped basis descriptions must not collide under FNV-1a.");
    }

    private static void TestDataEditorFacingImportRetentionModel()
    {
        FastRandom.SetSeed(0xD1CE0004u);

        // HasImportedData truth table. Metadata only counts when it describes a
        // real frame: BOTH dimensions must be positive (#36 fixed the old
        // width-only check that accepted torn width-only atlases).
        Check(new EntityFacingImportData(new[] { new Sprite() }, default, null).HasImportedData);
        Check(!new EntityFacingImportData(Array.Empty<Sprite>(), default, Array.Empty<EntityHitboxRecord>()).HasImportedData);
        Check(new EntityFacingImportData(null, new EntityAtlasMetadata { FrameWidth = 1, FrameHeight = 1 }, null).HasImportedData);
        Check(new EntityFacingImportData(null, default, new EntityHitboxRecord[1]).HasImportedData);
        // Torn atlases (only one positive dimension) are NOT imported data.
        Check(!new EntityFacingImportData(
            null,
            new EntityAtlasMetadata { FormatVersion = 1, FrameHeight = 9, AtlasWidth = 4, AtlasHeight = 4 },
            null).HasImportedData);
        Check(!new EntityFacingImportData(
            null,
            new EntityAtlasMetadata { FormatVersion = 1, FrameWidth = 9, AtlasWidth = 4, AtlasHeight = 4 },
            null).HasImportedData);

        // Plain EntityData atlas import edges: empty/null frame sets never disturb sprites.
        var plain = ScriptableObject.CreateInstance<UnityEntityData>();
        plain.SetImportedAtlas(new EntityAtlasMetadata { FrameWidth = 5 }, null);
        Same(null, plain.SpriteFrames);
        Same(null, plain.spriteSheet);
        Equal(5, plain.AtlasMetadata.FrameWidth);
        plain.SetImportedAtlas(new EntityAtlasMetadata { FrameWidth = 6 }, Array.Empty<Sprite>());
        Same(null, plain.SpriteFrames);
        Equal(6, plain.AtlasMetadata.FrameWidth);
        var onlyFrame = new Sprite();
        plain.SetImportedAtlas(default, new[] { onlyFrame });
        Same(onlyFrame, plain.spriteSheet);
        Equal(0, plain.AtlasMetadata.FrameWidth);
        plain.SetImportedHitboxes(Array.Empty<EntityHitboxRecord>());
        Equal(0, plain.Hitboxes.Length);
        plain.SetImportedHitboxes(null);
        Same(null, plain.Hitboxes);

        // Randomized state machine: replay every SetImportedFacing against a naive reference
        // model of the documented retention semantics and compare all four facings each step.
        var random = new Random(0xFAC1);
        var living = ScriptableObject.CreateInstance<LivingEntityData>();
        FacingDirection[] facings =
        {
            FacingDirection.Up, FacingDirection.Down, FacingDirection.Left, FacingDirection.Right
        };
        var modelFrames = new Sprite[facings.Length][];
        var modelMetadata = new EntityAtlasMetadata[facings.Length];
        var modelHitboxes = new EntityHitboxRecord[facings.Length][];
        var modelSheets = new Sprite[facings.Length];

        for (int op = 0; op < 240; op++)
        {
            int target = random.Next(facings.Length);
            int frameChoice = random.Next(3);
            Sprite[] frames = frameChoice == 0 ? null :
                frameChoice == 1 ? Array.Empty<Sprite>() : new Sprite[random.Next(1, 4)];
            if (frames != null)
                for (int j = 0; j < frames.Length; j++) frames[j] = new Sprite { name = "op" + op + "-" + j };
            var metadata = new EntityAtlasMetadata
            {
                FormatVersion = op,
                FrameWidth = random.Next(0, 3),
                FrameHeight = random.Next(0, 3),
                AtlasWidth = random.Next(0, 64),
                AtlasHeight = random.Next(0, 64)
            };
            int hitboxChoice = random.Next(3);
            EntityHitboxRecord[] hitboxes = hitboxChoice == 0 ? null :
                hitboxChoice == 1 ? Array.Empty<EntityHitboxRecord>() :
                new[] { new EntityHitboxRecord { FrameIndex = op, HitboxType = "h" + op, Bounds = new Rect(op, 0, 1, 1) } };

            living.SetImportedFacing(facings[target], metadata, frames, hitboxes);

            Sprite[] retained = frames != null && frames.Length > 0 ? frames : modelFrames[target];
            modelFrames[target] = retained;
            modelMetadata[target] = metadata;
            modelHitboxes[target] = hitboxes;
            if (retained != null && retained.Length > 0) modelSheets[target] = retained[0];

            for (int g = 0; g < facings.Length; g++)
            {
                bool expectedHasData = (modelFrames[g] != null && modelFrames[g].Length > 0) ||
                    (modelMetadata[g].FrameWidth > 0 && modelMetadata[g].FrameHeight > 0) ||
                    (modelHitboxes[g] != null && modelHitboxes[g].Length > 0);
                bool found = living.TryGetImportedFacing(facings[g], out EntityFacingImportData imported);
                Equal(expectedHasData, found, "Facing " + facings[g] + " availability diverged at op " + op);
                Same(modelFrames[g], imported.Frames);
                Same(modelHitboxes[g], imported.Hitboxes);
                Equal(modelMetadata[g].FormatVersion, imported.AtlasMetadata.FormatVersion);
                Equal(modelMetadata[g].FrameWidth, imported.AtlasMetadata.FrameWidth);
                Equal(modelMetadata[g].AtlasWidth, imported.AtlasMetadata.AtlasWidth);
                Equal(modelMetadata[g].AtlasHeight, imported.AtlasMetadata.AtlasHeight);
                Same(modelSheets[g], DataEditorSheetFor(living, facings[g]));
            }
        }

        // Out-of-range facing values must be complete no-ops on both read and write.
        living.SetImportedFacing((FacingDirection)99,
            new EntityAtlasMetadata { FrameWidth = 77 }, new[] { new Sprite() }, new EntityHitboxRecord[2]);
        for (int g = 0; g < facings.Length; g++)
        {
            living.TryGetImportedFacing(facings[g], out EntityFacingImportData imported);
            Same(modelFrames[g], imported.Frames);
            Same(modelHitboxes[g], imported.Hitboxes);
            Equal(modelMetadata[g].FormatVersion, imported.AtlasMetadata.FormatVersion);
            Same(modelSheets[g], DataEditorSheetFor(living, facings[g]));
        }
        Check(!living.TryGetImportedFacing((FacingDirection)99, out EntityFacingImportData ghost));
        Check(!ghost.HasImportedData);
        Same(null, ghost.Frames);
        Same(null, ghost.Hitboxes);
        Equal(0, ghost.AtlasMetadata.FrameWidth);
    }

    private static void TestDataEditorHitboxGizmoDrawingAndBoundsModel()
    {
        FastRandom.SetSeed(0xD1CE0005u);

        // Randomized reference model for TryGetLocalBounds: reimplements the validity rules
        // and the pivot math naively and demands bit-identical float results.
        var random = new Random(0x0B07B0B);
        float[] specials = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        float pivot = Config.Game.Importer.SpritePivotNormalized;
        for (int i = 0; i < 3000; i++)
        {
            var atlas = new EntityAtlasMetadata
            {
                FrameWidth = random.Next(-2, 40),
                FrameHeight = random.Next(-2, 40)
            };
            float x = (float)((random.NextDouble() * 16.0) - 8.0);
            float y = (float)((random.NextDouble() * 16.0) - 8.0);
            float width = (float)((random.NextDouble() * 8.0) - 2.0);
            float height = (float)((random.NextDouble() * 8.0) - 2.0);
            if (random.Next(10) == 0)
            {
                float special = specials[random.Next(specials.Length)];
                switch (random.Next(4))
                {
                    case 0: x = special; break;
                    case 1: y = special; break;
                    case 2: width = special; break;
                    default: height = special; break;
                }
            }

            bool expectedValid =
                atlas.FrameWidth > 0 && atlas.FrameHeight > 0 &&
                width > 0f && height > 0f &&
                !float.IsNaN(x) && !float.IsInfinity(x) &&
                !float.IsNaN(y) && !float.IsInfinity(y) &&
                !float.IsNaN(width) && !float.IsInfinity(width) &&
                !float.IsNaN(height) && !float.IsInfinity(height);
            bool actualValid = EFYVHitboxGizmo.TryGetLocalBounds(
                atlas, new Rect(x, y, width, height), out Vector3 center, out Vector3 size);
            Equal(expectedValid, actualValid);
            if (expectedValid)
            {
                float frameWidth = atlas.FrameWidth / Config.Shared.PixelsPerUnit;
                float frameHeight = atlas.FrameHeight / Config.Shared.PixelsPerUnit;
                Equal(x + (width * pivot) - (frameWidth * pivot), center.x);
                Equal((frameHeight * pivot) - y - (height * pivot), center.y);
                Equal(0f, center.z);
                Equal(width, size.x);
                Equal(height, size.y);
                Equal(0f, size.z);
            }
            else
            {
                Equal(0f, center.x);
                Equal(0f, center.y);
                Equal(0f, center.z);
                Equal(0f, size.x);
                Equal(0f, size.y);
                Equal(0f, size.z);
            }
        }

        // Drive the actual gizmo draw callback through reflection and observe the recorded
        // wire cubes and labels.
        Type gizmoType = typeof(EFYVHitboxGizmo);
        var sentinel = new Color(0.125f, 0.25f, 0.375f, 0.5f);

        var entity = CreateComponent<ProbeLiving>();
        entity.transform.position = new Vector3(2f, 3f, 4f);
        Gizmos.Cubes.Clear();
        Handles.Labels.Clear();
        Gizmos.color = sentinel;
        InvokeStatic(gizmoType, "DrawImportedHitboxes", entity, GizmoType.Selected);
        Equal(0, Gizmos.Cubes.Count, "An entity without SourceData must draw nothing.");
        Equal(0, Handles.Labels.Count);
        Near(sentinel.r, Gizmos.color.r, 0f);

        var baseAtlas = new EntityAtlasMetadata { FrameWidth = 32, FrameHeight = 32 };
        var baseHitboxes = new[]
        {
            new EntityHitboxRecord { FrameIndex = 0, HitboxType = "hurt", Bounds = new Rect(0.5f, 0.25f, 1f, 1.5f) },
            new EntityHitboxRecord { FrameIndex = 1, HitboxType = "hit", Bounds = new Rect(-0.5f, 0.75f, 2f, 0.5f) },
            new EntityHitboxRecord { FrameIndex = 1, HitboxType = "broken", Bounds = new Rect(0f, 0f, 0f, 1f) },
            new EntityHitboxRecord { FrameIndex = 1, HitboxType = null, Bounds = new Rect(0.25f, 0.5f, 0.75f, 1.25f) },
            new EntityHitboxRecord { FrameIndex = 2, HitboxType = "hurt", Bounds = new Rect(1f, 1f, 1f, 1f) }
        };
        var source = ScriptableObject.CreateInstance<LivingEntityData>();
        source.SetImportedAtlas(baseAtlas, null);
        source.SetImportedHitboxes(baseHitboxes);
        entity.LoadData(source);
        SetField(entity, "hitboxPreviewFrame", 1);

        // No facing data imported for the default preview facing -> falls back to the base
        // hitbox set; only frame-1 records with valid bounds may draw.
        Gizmos.Cubes.Clear();
        Handles.Labels.Clear();
        Gizmos.color = sentinel;
        InvokeStatic(gizmoType, "DrawImportedHitboxes", entity, GizmoType.Selected);
        Equal(2, Gizmos.Cubes.Count, "Exactly the two valid frame-1 hitboxes must draw.");
        Equal(2, Handles.Labels.Count);
        Check(EFYVHitboxGizmo.TryGetLocalBounds(baseAtlas, baseHitboxes[1].Bounds,
            out Vector3 expectedCenterHit, out Vector3 expectedSizeHit));
        Near(expectedCenterHit.x, Gizmos.Cubes[0].Center.x, 0f);
        Near(expectedCenterHit.y, Gizmos.Cubes[0].Center.y, 0f);
        Near(expectedSizeHit.x, Gizmos.Cubes[0].Size.x, 0f);
        Near(expectedSizeHit.y, Gizmos.Cubes[0].Size.y, 0f);
        Equal("hit", Handles.Labels[0].Text);
        Near(entity.transform.position.x + expectedCenterHit.x, Handles.Labels[0].Position.x, 0f);
        Near(entity.transform.position.y + expectedCenterHit.y, Handles.Labels[0].Position.y, 0f);
        Check(EFYVHitboxGizmo.TryGetLocalBounds(baseAtlas, baseHitboxes[3].Bounds,
            out Vector3 expectedCenterNull, out Vector3 expectedSizeNull));
        Near(expectedCenterNull.x, Gizmos.Cubes[1].Center.x, 0f);
        Near(expectedCenterNull.y, Gizmos.Cubes[1].Center.y, 0f);
        Near(expectedSizeNull.y, Gizmos.Cubes[1].Size.y, 0f);
        Equal(null, Handles.Labels[1].Text, "A null hitbox type must still label (with null text).");
        Near(sentinel.r, Gizmos.color.r, 0f);
        Near(sentinel.g, Gizmos.color.g, 0f);
        Near(sentinel.b, Gizmos.color.b, 0f);
        Near(sentinel.a, Gizmos.color.a, 0f);

        // Imported facing data for the preview facing takes precedence over the base set.
        var facingAtlas = new EntityAtlasMetadata { FrameWidth = 16, FrameHeight = 16 };
        var facingHitboxes = new[]
        {
            new EntityHitboxRecord { FrameIndex = 1, HitboxType = "aura", Bounds = new Rect(0.1f, 0.2f, 0.6f, 0.8f) }
        };
        source.SetImportedFacing(FacingDirection.Down, facingAtlas, new[] { new Sprite() }, facingHitboxes);
        Gizmos.Cubes.Clear();
        Handles.Labels.Clear();
        InvokeStatic(gizmoType, "DrawImportedHitboxes", entity, GizmoType.Selected);
        Equal(1, Gizmos.Cubes.Count);
        Check(EFYVHitboxGizmo.TryGetLocalBounds(facingAtlas, facingHitboxes[0].Bounds,
            out Vector3 expectedFacingCenter, out Vector3 expectedFacingSize));
        Near(expectedFacingCenter.x, Gizmos.Cubes[0].Center.x, 0f);
        Near(expectedFacingCenter.y, Gizmos.Cubes[0].Center.y, 0f);
        Near(expectedFacingSize.x, Gizmos.Cubes[0].Size.x, 0f);
        Equal("aura", Handles.Labels[0].Text);

        // Documents current behavior: a metadata-only facing import (no hitboxes) satisfies
        // TryGetImportedFacing, so its null hitbox array suppresses the base-set fallback.
        source.SetImportedFacing(FacingDirection.Up,
            new EntityAtlasMetadata { FrameWidth = 8, FrameHeight = 8 }, null, null);
        SetField(entity, "hitboxPreviewFacing", FacingDirection.Up);
        Gizmos.Cubes.Clear();
        Handles.Labels.Clear();
        InvokeStatic(gizmoType, "DrawImportedHitboxes", entity, GizmoType.Selected);
        Equal(0, Gizmos.Cubes.Count);

        // An entity whose data has no hitboxes anywhere draws nothing.
        var bareEntity = CreateComponent<ProbeLiving>();
        bareEntity.LoadData(ScriptableObject.CreateInstance<LivingEntityData>());
        Gizmos.Cubes.Clear();
        InvokeStatic(gizmoType, "DrawImportedHitboxes", bareEntity, GizmoType.Selected);
        Equal(0, Gizmos.Cubes.Count);
    }

    private static void TestDataEditorLiveDebugBridgeScheduling()
    {
        FastRandom.SetSeed(0xD1CE0006u);
        Type bridgeType = typeof(EFYVLiveDebugBridge);
        var pending = (ICollection<SchemaBackedAssetData>)GetField(bridgeType, "PendingAssets", null);

        var dataA = ScriptableObject.CreateInstance<LivingEntityData>();
        var dataB = ScriptableObject.CreateInstance<LivingEntityData>();
        var dataC = ScriptableObject.CreateInstance<LivingEntityData>();
        var entityA = CreateComponent<ProbeLiving>();
        entityA.LoadData(dataA);
        var entityB = CreateComponent<ProbeLiving>();
        entityB.LoadData(dataB);
        var entityC = CreateComponent<ProbeLiving>();
        entityC.LoadData(dataC);
        var entityNoData = CreateComponent<ProbeLiving>();
        var entityUnloaded = CreateComponent<ProbeLiving>();
        entityUnloaded.LoadData(dataA);
        entityUnloaded.gameObject.scene = new UnityEngine.SceneManagement.Scene(true, false);
        var entityInvalid = CreateComponent<ProbeLiving>();
        entityInvalid.LoadData(dataA);
        entityInvalid.gameObject.scene = new UnityEngine.SceneManagement.Scene(false, false);
        var entityDoomed = CreateComponent<ProbeLiving>();
        entityDoomed.LoadData(dataA);
        UnityEngine.Object.Destroy(entityDoomed.gameObject);

        var propData = ScriptableObject.CreateInstance<GameAssetData>();
        var spriteOld = new Sprite();
        var spriteNew = new Sprite();
        propData.sprite = spriteOld;
        var prop = CreateComponent<ProbeProp>(addRenderer: true);
        prop.Initialize();
        prop.LoadData(propData);
        Same(spriteOld, prop.spriteRenderer.sprite);
        propData.sprite = spriteNew;

        // Outside play mode nothing may queue or schedule.
        EditorApplication.isPlaying = false;
        EFYVLiveDebugBridge.QueueRefresh(dataA);
        Equal(0, pending.Count);
        Check(!(bool)GetField(bridgeType, "applyScheduled", null));

        // Null assets are ignored even in play mode.
        EditorApplication.isPlaying = true;
        EFYVLiveDebugBridge.QueueRefresh(null);
        Equal(0, pending.Count);
        Check(!(bool)GetField(bridgeType, "applyScheduled", null));

        // Queueing is set-based and schedules exactly one delay call.
        EFYVLiveDebugBridge.QueueRefresh(dataA);
        Equal(1, pending.Count);
        Check((bool)GetField(bridgeType, "applyScheduled", null));
        EFYVLiveDebugBridge.QueueRefresh(dataA);
        Equal(1, pending.Count, "Re-queueing the same asset must not grow the pending set.");
        EFYVLiveDebugBridge.QueueRefresh(dataB);
        EFYVLiveDebugBridge.QueueRefresh(propData);
        Equal(3, pending.Count);

        EditorApplication.InvokeDelayCalls();
        Equal(1, entityA.RefreshCalls);
        Equal(1, entityB.RefreshCalls);
        Equal(0, entityC.RefreshCalls, "An asset that was never queued must not refresh.");
        Equal(0, entityNoData.RefreshCalls, "Entities without SourceData must be skipped.");
        Equal(0, entityUnloaded.RefreshCalls, "A valid-but-unloaded scene must be skipped.");
        Equal(0, entityInvalid.RefreshCalls, "An invalid scene must be skipped.");
        Equal(0, entityDoomed.RefreshCalls, "Destroyed entities must be skipped.");
        Same(spriteNew, prop.spriteRenderer.sprite);
        Equal(0, pending.Count);
        Check(!(bool)GetField(bridgeType, "applyScheduled", null));

        // The bridge must be reusable: a second round schedules a fresh delay call.
        EFYVLiveDebugBridge.QueueRefresh(dataA);
        Check((bool)GetField(bridgeType, "applyScheduled", null));
        EditorApplication.InvokeDelayCalls();
        Equal(2, entityA.RefreshCalls);
        EditorApplication.InvokeDelayCalls();
        Equal(2, entityA.RefreshCalls, "A drained delay-call queue must be a no-op.");

        // Pending work is dropped wholesale when play mode ends before the delay call lands.
        EFYVLiveDebugBridge.QueueRefresh(dataB);
        Equal(1, pending.Count);
        EditorApplication.isPlaying = false;
        EditorApplication.InvokeDelayCalls();
        Equal(1, entityB.RefreshCalls);
        Equal(0, pending.Count);
        Check(!(bool)GetField(bridgeType, "applyScheduled", null));
    }

    private static void TestDataEditorTexturePreprocessAndReimportRules()
    {
        FastRandom.SetSeed(0xD1CE0007u);
        Type importerType = typeof(EFYVPixelArtImporter);
        string tempRoot = Path.Combine(Path.GetTempPath(),
            "efyv-dataeditor-texture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string pngPath = Path.Combine(tempRoot, "Probe" + Config.Game.Importer.ExtensionPNG);
            string metadataPath = Path.ChangeExtension(pngPath, Config.Game.Importer.ExtensionEFYV);
            var post = new EFYVPixelArtImporter();
            var textureImporter = new TextureImporter { assetPath = pngPath };
            post.assetImporter = textureImporter;

            // Missing sibling metadata leaves the importer untouched.
            DataEditorResetTextureSentinel(textureImporter);
            post.assetPath = pngPath;
            Invoke(post, "OnPreprocessTexture");
            Equal(TextureImporterType.Default, textureImporter.textureType);
            Equal(SpriteImportMode.None, textureImporter.spriteImportMode);

            // Even with metadata on disk, non-PNG asset paths exit before configuring.
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new EFYVJsonFormat { atlas = ValidAtlas() }));
            post.assetPath = Path.Combine(tempRoot, "Probe.txt");
            Invoke(post, "OnPreprocessTexture");
            Equal(TextureImporterType.Default, textureImporter.textureType);

            // A valid sibling atlas configures slicing end to end.
            post.assetPath = pngPath;
            Invoke(post, "OnPreprocessTexture");
            Equal(TextureImporterType.Sprite, textureImporter.textureType);
            Equal(FilterMode.Point, textureImporter.filterMode);
            Equal(SpriteImportMode.Multiple, textureImporter.spriteImportMode);
            Equal(5, textureImporter.spritesheet.Length);
            Equal("Probe" + Config.Game.Importer.SpriteSliceNameSeparator +
                0.ToString(Config.Game.Importer.SpriteSliceIndexFormat), textureImporter.spritesheet[0].name);

            // An invalid atlas must abort before touching the importer at all.
            AtlasMetadataJson invalidAtlas = ValidAtlas();
            invalidAtlas.formatVersion++;
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new EFYVJsonFormat { atlas = invalidAtlas }));
            DataEditorResetTextureSentinel(textureImporter);
            Invoke(post, "OnPreprocessTexture");
            Equal(TextureImporterType.Default, textureImporter.textureType);
            Equal(SpriteImportMode.None, textureImporter.spriteImportMode);
            Same(null, textureImporter.spritesheet);

            // Metadata without an atlas still standardizes the texture, in Single mode.
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new EFYVJsonFormat { assetType = nameof(GameAssetData) }));
            Invoke(post, "OnPreprocessTexture");
            Equal(TextureImporterType.Sprite, textureImporter.textureType);
            Equal(SpriteImportMode.Single, textureImporter.spriteImportMode);

            // Exceptions inside the callback are contained and logged, never thrown.
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new EFYVJsonFormat { atlas = ValidAtlas() }));
            post.assetImporter = new AssetImporter();
            int logsBefore = Debug.Messages.Count;
            Invoke(post, "OnPreprocessTexture");
            Check(Debug.Messages.Count > logsBefore, "The preprocess cast failure must be logged.");

            // RequiresTextureReimport: a fully conforming importer is stable; every relevant
            // drifted property must trigger a reimport.
            AtlasMetadataJson atlas = ValidAtlas();
            var conforming = new TextureImporter { assetPath = pngPath };
            EFYVPixelArtImporter.ConfigureTextureImporter(conforming, atlas);
            Check(!(bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));

            conforming.textureType = TextureImporterType.Default;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.textureType = TextureImporterType.Sprite;
            conforming.mipmapEnabled = !Config.Game.Map.TextureMipmapsEnabled;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.mipmapEnabled = Config.Game.Map.TextureMipmapsEnabled;
            conforming.filterMode = FilterMode.Bilinear;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.filterMode = FilterMode.Point;
            conforming.textureCompression = TextureImporterCompression.Compressed;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.textureCompression = TextureImporterCompression.Uncompressed;
            conforming.alphaIsTransparency = false;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.alphaIsTransparency = true;
            conforming.spritePixelsPerUnit += 1f;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.spritePixelsPerUnit = Config.Shared.PixelsPerUnit;
            conforming.maxTextureSize += 1;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.maxTextureSize = Config.Game.Importer.MaxTextureSize;
            conforming.npotScale = TextureImporterNPOTScale.ToNearest;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.npotScale = TextureImporterNPOTScale.None;
            conforming.spriteImportMode = SpriteImportMode.Single;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.spriteImportMode = SpriteImportMode.Multiple;

            SpriteMetaData[] savedSlices = conforming.spritesheet;
            conforming.spritesheet = null;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.spritesheet = new SpriteMetaData[savedSlices.Length - 1];
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.spritesheet = savedSlices;
            conforming.spritesheet[0].rect.width += 1f;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));
            conforming.spritesheet[0].rect.width -= 1f;
            Check(!(bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, atlas));

            // Without atlas metadata only the import mode matters.
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, null));
            conforming.spriteImportMode = SpriteImportMode.Single;
            Check(!(bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, null));

            // Single-frame atlases ignore the (stale) spritesheet entirely.
            AtlasMetadataJson oneFrame = atlas;
            oneFrame.atlasWidth = 16;
            oneFrame.atlasHeight = 16;
            oneFrame.animations = new List<AnimationMetadataJson>
            {
                new AnimationMetadataJson { name = "one", fps = 1, startFrame = 0, frameCount = 1 }
            };
            Check(!(bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, oneFrame));
            conforming.spriteImportMode = SpriteImportMode.Multiple;
            Check((bool)InvokeStatic(importerType, "RequiresTextureReimport", conforming, oneFrame));

            // EnsureTextureImportIsCurrent only forces a reimport for a drifted, registered
            // importer whose texture actually exists on disk.
            string realPng = Path.Combine(tempRoot, "Ensure" + Config.Game.Importer.ExtensionPNG);
            int importsBefore = AssetDatabase.Imports.Count;
            InvokeStatic(importerType, "EnsureTextureImportIsCurrent", realPng, atlas);
            Equal(importsBefore, AssetDatabase.Imports.Count, "Missing PNG must not trigger a reimport.");
            File.WriteAllBytes(realPng, new byte[] { 1, 2, 3 });
            InvokeStatic(importerType, "EnsureTextureImportIsCurrent", realPng, atlas);
            Equal(importsBefore, AssetDatabase.Imports.Count, "Unregistered importer must not trigger a reimport.");
            var registered = new TextureImporter { assetPath = realPng };
            EFYVPixelArtImporter.ConfigureTextureImporter(registered, atlas);
            AssetImporter.Register(realPng, registered);
            InvokeStatic(importerType, "EnsureTextureImportIsCurrent", realPng, atlas);
            Equal(importsBefore, AssetDatabase.Imports.Count, "A conforming importer must not reimport.");
            registered.filterMode = FilterMode.Bilinear;
            InvokeStatic(importerType, "EnsureTextureImportIsCurrent", realPng, atlas);
            Equal(importsBefore + 1, AssetDatabase.Imports.Count);
            Equal(realPng, AssetDatabase.Imports[^1].Path);
            Equal(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport,
                AssetDatabase.Imports[^1].Options);

            // LoadSprites filters non-sprites and sorts by ordinal name.
            string spriteSourcePath = Path.Combine(tempRoot, "Sprites" + Config.Game.Importer.ExtensionPNG);
            var slice02 = new Sprite { name = "Atlas_02" };
            var slice10 = new Sprite { name = "Atlas_10" };
            var slice2 = new Sprite { name = "Atlas_2" };
            var nonSprite = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            AssetDatabase.SetAllAssetsAtPath(spriteSourcePath, slice2, nonSprite, slice02, slice10);
            var sprites = (Sprite[])InvokeStatic(importerType, "LoadSprites", spriteSourcePath);
            Equal(3, sprites.Length);
            Same(slice02, sprites[0]);
            Same(slice10, sprites[1]);
            Same(slice2, sprites[2]);
            Equal(0, ((Sprite[])InvokeStatic(importerType, "LoadSprites", (object)null)).Length);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void TestDataEditorImportPipelineAdversarialPaths()
    {
        FastRandom.SetSeed(0xD1CE0008u);
        Type importerType = typeof(EFYVPixelArtImporter);
        string tempRoot = Path.Combine(Path.GetTempPath(),
            "efyv-dataeditor-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            // Adversarial entity names: traversal, reserved devices, separators, invalid
            // characters, and trailing dot/space must all be rejected with the shared error.
            string[] unsafeNames =
            {
                "..", ".", "a/b", "a\\b", "nul", "NUL.dat", "con", "com3", "lpt9",
                "bad.", "bad ", "a:b", "a?b", "   "
            };
            int fileIndex = 0;
            foreach (string unsafeName in unsafeNames)
            {
                var format = new EFYVJsonFormat
                {
                    assetType = nameof(EnemyData),
                    properties = new Dictionary<string, JsonElement>
                    {
                        [Config.Game.Importer.KeyEntityName] = JsonValue(JsonSerializer.Serialize(unsafeName))
                    }
                };
                string path = DataEditorWriteEfyvFile(tempRoot, "Adversarial" + fileIndex++, format);
                int dirtyBefore = EditorUtility.DirtyObjects.Count;
                InvokeStatic(importerType, "ImportEFYVAsset", path);
                Equal(dirtyBefore, EditorUtility.DirtyObjects.Count, "Unsafe name was imported: " + unsafeName);
                Equal(string.Format(Config.Game.Importer.LogErrorUnsafeIdentity, path, unsafeName),
                    Debug.Messages[^1]);
            }

            // A JSON-null entity name is rejected the same way (per-cause message).
            var nullNameFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("null")
                }
            };
            string nullNamePath = DataEditorWriteEfyvFile(tempRoot, "Adversarial" + fileIndex++, nullNameFormat);
            int dirtyBeforeNull = EditorUtility.DirtyObjects.Count;
            InvokeStatic(importerType, "ImportEFYVAsset", nullNamePath);
            Equal(dirtyBeforeNull, EditorUtility.DirtyObjects.Count);
            Equal(string.Format(Config.Game.Importer.LogErrorUnsafeIdentity, nullNamePath, (string)null),
                Debug.Messages[^1]);

            // A non-string entity name is a hard exception surfaced to the caller (the batch
            // postprocessor is the layer that contains it).
            var numericNameFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("5")
                }
            };
            string numericNamePath = DataEditorWriteEfyvFile(tempRoot, "Adversarial" + fileIndex++, numericNameFormat);
            Throws<InvalidOperationException>(() => InvokeStatic(importerType, "ImportEFYVAsset", numericNamePath));

            // Names that merely look reserved must pass.
            foreach (string safeName in new[] { "com10", "nul2", "x.y.z" })
            {
                var format = new EFYVJsonFormat
                {
                    assetType = nameof(EnemyData),
                    properties = new Dictionary<string, JsonElement>
                    {
                        [Config.Game.Importer.KeyEntityName] = JsonValue(JsonSerializer.Serialize(safeName))
                    }
                };
                string path = DataEditorWriteEfyvFile(tempRoot, "Safe" + fileIndex++, format);
                InvokeStatic(importerType, "ImportEFYVAsset", path);
                string assetPath = Path.GetDirectoryName(path) + Config.Game.Importer.PathSeparator +
                    safeName + Config.Game.Importer.ExtensionAsset;
                EnemyData created = AssetDatabase.LoadAssetAtPath<EnemyData>(assetPath);
                Check(created != null, "Safe name was rejected: " + safeName);
                Equal(safeName, created.entityName);
                Check(EditorUtility.DirtyObjects.Contains(created));
            }

            // entityName wins over the assetName fallback when both are present.
            var priorityFormat = new EFYVJsonFormat
            {
                assetType = nameof(GameAssetData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"PrimaryName\""),
                    [Config.Shared.AssetNameField] = JsonValue("\"SecondaryName\"")
                }
            };
            string priorityPath = DataEditorWriteEfyvFile(tempRoot, "Priority", priorityFormat);
            InvokeStatic(importerType, "ImportEFYVAsset", priorityPath);
            string primaryPath = Path.GetDirectoryName(priorityPath) + Config.Game.Importer.PathSeparator +
                "PrimaryName" + Config.Game.Importer.ExtensionAsset;
            string secondaryPath = Path.GetDirectoryName(priorityPath) + Config.Game.Importer.PathSeparator +
                "SecondaryName" + Config.Game.Importer.ExtensionAsset;
            GameAssetData primary = AssetDatabase.LoadAssetAtPath<GameAssetData>(primaryPath);
            Check(primary != null);
            Equal("PrimaryName", primary.assetName);
            Equal(null, AssetDatabase.LoadAssetAtPath<GameAssetData>(secondaryPath));

            // The assetName key alone also names the asset, and schema values flow into it.
            var fallbackFormat = new EFYVJsonFormat
            {
                assetType = nameof(GameAssetData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Shared.AssetNameField] = JsonValue("\"FallbackAsset\""),
                    [Config.Game.Importer.KeyMaxHealth] = JsonValue("7.5")
                }
            };
            string fallbackPath = DataEditorWriteEfyvFile(tempRoot, "Fallback", fallbackFormat);
            InvokeStatic(importerType, "ImportEFYVAsset", fallbackPath);
            GameAssetData fallback = AssetDatabase.LoadAssetAtPath<GameAssetData>(
                Path.GetDirectoryName(fallbackPath) + Config.Game.Importer.PathSeparator +
                "FallbackAsset" + Config.Game.Importer.ExtensionAsset);
            Check(fallback != null);
            Equal("FallbackAsset", fallback.assetName);
            Near(7.5f, fallback.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));
            Equal(DataEditorReferenceFnvHash("FallbackAsset"),
                fallback.GetSchemaBlock().GetInt((int)AssetSchema.AssetIdHash));

            // Without any name key the import is REJECTED (#36): the old
            // "UnknownEntity" collapse silently merged unrelated exports into
            // one shared asset. The per-cause message names the missing identity.
            string anonymousPath = Path.Combine(tempRoot, "Anonymous" + Config.Game.Importer.ExtensionEFYV);
            var anonymousFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyMaxHealth] = JsonValue("3")
                }
            };
            File.WriteAllText(anonymousPath, JsonSerializer.Serialize(anonymousFormat));
            int dirtyBeforeAnonymous = EditorUtility.DirtyObjects.Count;
            InvokeStatic(importerType, "ImportEFYVAsset", anonymousPath);
            Equal(dirtyBeforeAnonymous, EditorUtility.DirtyObjects.Count);
            Equal(string.Format(Config.Game.Importer.LogErrorMissingIdentity, anonymousPath),
                Debug.Messages[^1]);
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(anonymousPath) + Config.Game.Importer.PathSeparator +
                "UnknownEntity" + Config.Game.Importer.ExtensionAsset));

            // A facing property on a plain (non-living) EntityData type routes the
            // sprites into the base atlas import instead. Plain EntityData is no
            // longer a built-in registration (the dead "EntityData" factory was
            // dropped in #16e), so register a scoped custom type for the probe.
            const string plainEntityKey = "PlainEntityProbeData";
            EFYVPixelArtImporter.RegisterAssetFactory<UnityEntityData>(plainEntityKey);
            try
            {
                string plainPath = Path.Combine(tempRoot, "PlainRouted" + Config.Game.Importer.ExtensionEFYV);
                var anonSprite0 = new Sprite { name = "anon_00" };
                var anonSprite1 = new Sprite { name = "anon_01" };
                AssetDatabase.SetAllAssetsAtPath(
                    Path.ChangeExtension(plainPath, Config.Game.Importer.ExtensionPNG), anonSprite1, anonSprite0);
                var plainFormat = new EFYVJsonFormat
                {
                    assetType = plainEntityKey,
                    properties = new Dictionary<string, JsonElement>
                    {
                        [Config.Game.Importer.KeyEntityName] = JsonValue("\"PlainRouted\""),
                        [Config.Game.Importer.KeyMaxHealth] = JsonValue("3"),
                        [Config.Game.Importer.KeyFacing] = JsonValue(JsonSerializer.Serialize(Config.Game.Importer.FacingDown))
                    }
                };
                File.WriteAllText(plainPath, JsonSerializer.Serialize(plainFormat));
                InvokeStatic(importerType, "ImportEFYVAsset", plainPath);
                UnityEntityData plainRouted = AssetDatabase.LoadAssetAtPath<UnityEntityData>(
                    Path.GetDirectoryName(plainPath) + Config.Game.Importer.PathSeparator +
                    "PlainRouted" + Config.Game.Importer.ExtensionAsset);
                Check(plainRouted != null, "Plain EntityData import must publish the ScriptableObject.");
                Check(!(plainRouted is LivingEntityData));
                Equal("PlainRouted", plainRouted.entityName);
                Equal(2, plainRouted.SpriteFrames.Length);
                Same(anonSprite0, plainRouted.SpriteFrames[0]);
                Same(anonSprite0, plainRouted.spriteSheet);
                Equal(0, plainRouted.Hitboxes.Length);
            }
            finally
            {
                var factories = (IDictionary)GetField(importerType, "AssetFactories", null);
                var assetTypes = (IDictionary)GetField(importerType, "AssetTypes", null);
                factories.Remove(plainEntityKey);
                assetTypes.Remove(plainEntityKey);
            }

            // Re-importing updates the existing ScriptableObject in place.
            var updateFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"Updatee\""),
                    [Config.Game.Importer.KeyMaxHealth] = JsonValue("10")
                }
            };
            string updatePath = DataEditorWriteEfyvFile(tempRoot, "Updatee", updateFormat);
            InvokeStatic(importerType, "ImportEFYVAsset", updatePath);
            string updateAssetPath = Path.GetDirectoryName(updatePath) + Config.Game.Importer.PathSeparator +
                "Updatee" + Config.Game.Importer.ExtensionAsset;
            EnemyData firstPass = AssetDatabase.LoadAssetAtPath<EnemyData>(updateAssetPath);
            Check(firstPass != null);
            Near(10f, firstPass.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));
            updateFormat.properties[Config.Game.Importer.KeyMaxHealth] = JsonValue("20");
            updateFormat.properties[Config.Game.Importer.KeyBaseSpeed] = JsonValue("4");
            File.WriteAllText(updatePath, JsonSerializer.Serialize(updateFormat));
            InvokeStatic(importerType, "ImportEFYVAsset", updatePath);
            Same(firstPass, AssetDatabase.LoadAssetAtPath<EnemyData>(updateAssetPath));
            Near(20f, firstPass.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));
            Near(4f, firstPass.GetSchemaBlock().GetFloat((int)AssetSchema.BaseSpeed));

            // An existing asset of a more derived type is accepted and updated in place.
            var bossOccupant = ScriptableObject.CreateInstance<BossData>();
            string subMetadataPath = Path.Combine(tempRoot, "Sub" + Config.Game.Importer.ExtensionEFYV);
            string subAssetPath = Path.GetDirectoryName(subMetadataPath) + Config.Game.Importer.PathSeparator +
                "Sub" + Config.Game.Importer.ExtensionAsset;
            AssetDatabase.CreateAsset(bossOccupant, subAssetPath);
            var subFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"Sub\""),
                    [Config.Game.Importer.KeyMaxHealth] = JsonValue("33")
                }
            };
            File.WriteAllText(subMetadataPath, JsonSerializer.Serialize(subFormat));
            InvokeStatic(importerType, "ImportEFYVAsset", subMetadataPath);
            Same(bossOccupant, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(subAssetPath));
            Near(33f, bossOccupant.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));
            Equal("Sub", bossOccupant.entityName);

            // An existing asset of an unrelated type blocks the import and stays untouched.
            var occupant = ScriptableObject.CreateInstance<GameAssetData>();
            string occupiedMetadataPath = Path.Combine(tempRoot, "Occupied" + Config.Game.Importer.ExtensionEFYV);
            string occupiedAssetPath = Path.GetDirectoryName(occupiedMetadataPath) + Config.Game.Importer.PathSeparator +
                "Occupied" + Config.Game.Importer.ExtensionAsset;
            AssetDatabase.CreateAsset(occupant, occupiedAssetPath);
            var occupiedFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"Occupied\""),
                    [Config.Game.Importer.KeyMaxHealth] = JsonValue("44")
                }
            };
            File.WriteAllText(occupiedMetadataPath, JsonSerializer.Serialize(occupiedFormat));
            InvokeStatic(importerType, "ImportEFYVAsset", occupiedMetadataPath);
            Same(occupant, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(occupiedAssetPath));
            Near(0f, occupant.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));
            Equal(null, occupant.assetName);
            Equal(string.Format(
                    Config.Game.Importer.LogErrorExistingAssetTypeMismatch,
                    occupiedMetadataPath,
                    nameof(GameAssetData),
                    nameof(EnemyData)),
                Debug.Messages[^1]);

            // GameAssetData links the first (ordinal-sorted) sibling sprite; an authored facing
            // property is ignored for non-entity assets.
            string linkPath = Path.Combine(tempRoot, "SpriteLink" + Config.Game.Importer.ExtensionEFYV);
            var linkSpriteA = new Sprite { name = "link_00" };
            var linkSpriteZ = new Sprite { name = "link_01" };
            AssetDatabase.SetAllAssetsAtPath(
                Path.ChangeExtension(linkPath, Config.Game.Importer.ExtensionPNG), linkSpriteZ, linkSpriteA);
            var linkFormat = new EFYVJsonFormat
            {
                assetType = nameof(GameAssetData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"SpriteLink\""),
                    [Config.Game.Importer.KeyFacing] = JsonValue(JsonSerializer.Serialize(Config.Game.Importer.FacingUp))
                }
            };
            File.WriteAllText(linkPath, JsonSerializer.Serialize(linkFormat));
            InvokeStatic(importerType, "ImportEFYVAsset", linkPath);
            GameAssetData linked = AssetDatabase.LoadAssetAtPath<GameAssetData>(
                Path.GetDirectoryName(linkPath) + Config.Game.Importer.PathSeparator +
                "SpriteLink" + Config.Game.Importer.ExtensionAsset);
            Check(linked != null);
            Same(linkSpriteA, linked.sprite);
            Equal("SpriteLink", linked.assetName);

            // A living entity with a facing property imports frames and hitboxes into that
            // facing only, leaving the base atlas/hitbox set untouched.
            string facingPath = Path.Combine(tempRoot, "FacingRight" + Config.Game.Importer.ExtensionEFYV);
            var facingSprite0 = new Sprite { name = "F_00" };
            var facingSprite1 = new Sprite { name = "F_01" };
            AssetDatabase.SetAllAssetsAtPath(
                Path.ChangeExtension(facingPath, Config.Game.Importer.ExtensionPNG), facingSprite1, facingSprite0);
            var facingFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"FacingRight\""),
                    [Config.Game.Importer.KeyFacing] = JsonValue(JsonSerializer.Serialize(Config.Game.Importer.FacingRight)),
                    [Config.Game.Importer.KeyMaxHealth] = JsonValue("55")
                },
                hitboxes = new List<HitboxJson>
                {
                    new HitboxJson { frameIndex = 2, hitboxType = "hurt", x = 1, y = 2, width = 3, height = 4 }
                }
            };
            File.WriteAllText(facingPath, JsonSerializer.Serialize(facingFormat));
            InvokeStatic(importerType, "ImportEFYVAsset", facingPath);
            EnemyData facingEnemy = AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(facingPath) + Config.Game.Importer.PathSeparator +
                "FacingRight" + Config.Game.Importer.ExtensionAsset);
            Check(facingEnemy != null);
            Check(facingEnemy.TryGetImportedFacing(FacingDirection.Right, out EntityFacingImportData importedRight));
            Equal(2, importedRight.Frames.Length);
            Equal("F_00", importedRight.Frames[0].name);
            Same(importedRight.Frames[0], facingEnemy.spriteSheetRight);
            Equal(1, importedRight.Hitboxes.Length);
            Equal(2, importedRight.Hitboxes[0].FrameIndex);
            Equal("hurt", importedRight.Hitboxes[0].HitboxType);
            Near(3f, importedRight.Hitboxes[0].Bounds.width);
            Same(null, facingEnemy.Hitboxes);
            Same(null, facingEnemy.SpriteFrames);
            Check(!facingEnemy.TryGetImportedFacing(FacingDirection.Left, out _));
            Near(55f, facingEnemy.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));

            // Missing properties and invalid atlas metadata abort with per-cause errors.
            string noPropsPath = DataEditorWriteEfyvFile(tempRoot, "NoProps",
                new EFYVJsonFormat { assetType = nameof(EnemyData) });
            int dirtyBeforeNoProps = EditorUtility.DirtyObjects.Count;
            InvokeStatic(importerType, "ImportEFYVAsset", noPropsPath);
            Equal(dirtyBeforeNoProps, EditorUtility.DirtyObjects.Count);
            Equal(string.Format(Config.Game.Importer.LogErrorMissingProperties, noPropsPath),
                Debug.Messages[^1]);

            AtlasMetadataJson badAtlas = ValidAtlas();
            badAtlas.formatVersion += 9;
            var badAtlasFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"BadAtlas\"")
                },
                atlas = badAtlas
            };
            string badAtlasPath = DataEditorWriteEfyvFile(tempRoot, "BadAtlas", badAtlasFormat);
            InvokeStatic(importerType, "ImportEFYVAsset", badAtlasPath);
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(badAtlasPath) + Config.Game.Importer.PathSeparator +
                "BadAtlas" + Config.Game.Importer.ExtensionAsset));
            Equal(string.Format(
                    Config.Game.Importer.LogErrorInvalidAtlas,
                    badAtlasPath,
                    EFYVBackend.Core.Export.AtlasMetadataError.FormatVersion),
                Debug.Messages[^1]);

            // The batch postprocessor dedupes case-insensitively across the metadata file and
            // its sibling PNG, and only the imported list may trigger work.
            var batchFormat = new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"Batch\"")
                }
            };
            string batchMetadataPath = DataEditorWriteEfyvFile(tempRoot, "Batch", batchFormat);
            string batchPngPath = Path.ChangeExtension(batchMetadataPath, Config.Game.Importer.ExtensionPNG);
            string detectedPrefix = Config.Game.Importer.LogDetected.Substring(
                0, Config.Game.Importer.LogDetected.IndexOf('{'));
            int detectedBefore = Debug.Messages.Count(
                message => message != null && message.StartsWith(detectedPrefix, StringComparison.Ordinal));
            InvokeStatic(importerType, "OnPostprocessAllAssets",
                new[] { batchMetadataPath.ToUpperInvariant(), batchMetadataPath, batchPngPath },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            int detectedAfter = Debug.Messages.Count(
                message => message != null && message.StartsWith(detectedPrefix, StringComparison.Ordinal));
            Equal(detectedBefore + 1, detectedAfter, "The batch postprocessor must import each asset once.");
            Check(AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(batchMetadataPath) + Config.Game.Importer.PathSeparator +
                "Batch" + Config.Game.Importer.ExtensionAsset) != null);

            InvokeStatic(importerType, "OnPostprocessAllAssets",
                Array.Empty<string>(), new[] { batchMetadataPath }, new[] { batchMetadataPath }, new[] { batchMetadataPath });
            Equal(detectedAfter, Debug.Messages.Count(
                message => message != null && message.StartsWith(detectedPrefix, StringComparison.Ordinal)),
                "Deleted/moved lists must never trigger an import.");

            InvokeStatic(importerType, "OnPostprocessAllAssets",
                new[] { Path.Combine(tempRoot, "Ghost" + Config.Game.Importer.ExtensionPNG) },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Equal(detectedAfter, Debug.Messages.Count(
                message => message != null && message.StartsWith(detectedPrefix, StringComparison.Ordinal)),
                "A PNG without sibling metadata must be ignored.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // Item #27: the batch postprocessor coalesces AssetDatabase.SaveAssets into
    // ONE call per postprocess group (not one per asset), and never flushes when
    // the group produced no created/dirtied asset.
    private static void TestDataEditorSaveAssetsCoalescing()
    {
        Type importerType = typeof(EFYVPixelArtImporter);
        string tempRoot = Path.Combine(Path.GetTempPath(),
            "efyv-dataeditor-saveassets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            EFYVJsonFormat Make(string name) => new EFYVJsonFormat
            {
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue(JsonSerializer.Serialize(name))
                }
            };

            string a = DataEditorWriteEfyvFile(tempRoot, "CoalesceA", Make("CoalesceA"));
            string b = DataEditorWriteEfyvFile(tempRoot, "CoalesceB", Make("CoalesceB"));
            string c = DataEditorWriteEfyvFile(tempRoot, "CoalesceC", Make("CoalesceC"));

            // Three assets in one group -> exactly ONE SaveAssets, not three.
            int before = AssetDatabase.SaveAssetsCount;
            InvokeStatic(importerType, "OnPostprocessAllAssets",
                new[] { a, b, c }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Equal(before + 1, AssetDatabase.SaveAssetsCount,
                "Three imported assets must flush with a single SaveAssets.");
            Check(AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(a) + Config.Game.Importer.PathSeparator +
                "CoalesceA" + Config.Game.Importer.ExtensionAsset) != null);
            Check(AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(c) + Config.Game.Importer.PathSeparator +
                "CoalesceC" + Config.Game.Importer.ExtensionAsset) != null);

            // A group that imports nothing must not flush at all.
            int beforeEmpty = AssetDatabase.SaveAssetsCount;
            InvokeStatic(importerType, "OnPostprocessAllAssets",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Equal(beforeEmpty, AssetDatabase.SaveAssetsCount,
                "An empty postprocess group must not call SaveAssets.");

            // A group whose only member fails to import must not flush.
            string malformed = Path.Combine(tempRoot, "Malformed" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(malformed, "{ not valid json ]");
            int beforeFail = AssetDatabase.SaveAssetsCount;
            InvokeStatic(importerType, "OnPostprocessAllAssets",
                new[] { malformed }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Equal(beforeFail, AssetDatabase.SaveAssetsCount,
                "A group whose imports all fail must not call SaveAssets.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void TestDataEditorSchemaIsolationAndInterfaceContracts()
    {
        FastRandom.SetSeed(0xD1CE0009u);
        Type importerType = typeof(EFYVPixelArtImporter);
        var assetTypes = (IDictionary)GetField(importerType, "AssetTypes", null);
        var keys = assetTypes.Keys.Cast<string>().OrderBy(key => key, StringComparer.Ordinal).ToArray();
        Check(keys.Length >= Config.LabyMake.Schema.BuiltInAssetRegistrations.Length,
            "Every built-in registration must have a factory.");

        // Every registered factory must produce independent instances with private, correctly
        // sized schema storage (a shared static block would corrupt all designed assets).
        foreach (string key in keys)
        {
            var first = (SchemaBackedAssetData)InvokeStatic(importerType, "CreateAssetData", key);
            var second = (SchemaBackedAssetData)InvokeStatic(importerType, "CreateAssetData", key);
            NotSame(first, second);
            Same((Type)assetTypes[key], first.GetType());
            int[] firstStorage = GetField<int[]>(first, "schemaBlockData");
            NotSame(firstStorage, GetField<int[]>(second, "schemaBlockData"));
            Equal(FastSchemaBlock.MaxSize, firstStorage.Length);
            FastSchemaBlock block = default;
            block.SetInt(3, 0x0C0FFEE);
            first.SetSchemaBlock(block);
            Equal(0x0C0FFEE, first.GetSchemaBlock().GetInt(3));
            Equal(0, second.GetSchemaBlock().GetInt(3), key + " instances share schema storage.");
        }

        // Interface contracts the pooling and combat systems compile against.
        Type damageable = typeof(IDamageable);
        Check(damageable.IsInterface);
        Equal(2, damageable.GetMethods().Length);
        MethodInfo takeDamage = damageable.GetMethod("TakeDamage");
        Equal(typeof(void), takeDamage.ReturnType);
        Equal(1, takeDamage.GetParameters().Length);
        Equal(typeof(float), takeDamage.GetParameters()[0].ParameterType);
        MethodInfo die = damageable.GetMethod("Die");
        Equal(typeof(void), die.ReturnType);
        Equal(0, die.GetParameters().Length);

        Type poolable = typeof(IPoolable);
        Check(poolable.IsInterface);
        Equal(2, poolable.GetMethods().Length);
        Equal(typeof(void), poolable.GetMethod("OnSpawn").ReturnType);
        Equal(0, poolable.GetMethod("OnSpawn").GetParameters().Length);
        Equal(typeof(void), poolable.GetMethod("OnDespawn").ReturnType);
        Equal(0, poolable.GetMethod("OnDespawn").GetParameters().Length);

        Check(poolable.IsAssignableFrom(typeof(GameEntity)));
        Check(damageable.IsAssignableFrom(typeof(LivingEntity)));
        Check(!damageable.IsAssignableFrom(typeof(PropEntity)));

        // Every damageable type shipped in the EFYV namespaces must also be poolable, because
        // damage flows despawn targets through the shared pooling contract.
        Type[] damageableTypes = typeof(GameEntity).Assembly.GetTypes()
            .Where(type => !type.IsInterface &&
                type.Namespace != null &&
                type.Namespace.StartsWith("EFYV", StringComparison.Ordinal) &&
                damageable.IsAssignableFrom(type))
            .ToArray();
        Check(damageableTypes.Length > 0);
        foreach (Type type in damageableTypes)
            Check(poolable.IsAssignableFrom(type), type.FullName + " is damageable but not poolable.");
    }
}
