// batch3.4 agent (item #7): runtime effect descriptors - the importer parses
// per-animation "effects" arrays from the .efyvlaby atlas block onto the
// imported asset (EntityEffectDescriptor), and LivingEntity minimally
// interprets flash/tint against the SpriteRenderer color on the OnSpawn /
// OnDamaged seams with the flash countdown ticked by the central loops.
// particleHook descriptors are STORED but not interpreted (deferred).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Editor;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Models;
using UnityEditor;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

internal static partial class Program
{
    private static EffectDescriptorJson RuntimeFlashJson()
    {
        return new EffectDescriptorJson
        {
            name = "HurtFlash",
            effectType = Config.Backend.Exporter.EffectTypeFlash,
            trigger = Config.Shared.EffectTriggerOnDamaged,
            colorRgba = 0xFF0000FFu, // opaque red
            durationMs = 150,
            strength = 0.8f
        };
    }

    // ------------------------------------------------------------------
    // Importer: effects arrays convert (with defaults) and import end to end.
    // ------------------------------------------------------------------
    private static void TestImporterEffectDescriptorsEndToEnd()
    {
        AtlasMetadataJson atlas = ValidAtlas();
        AnimationMetadataJson walk = atlas.animations[1];
        walk.effects = new List<EffectDescriptorJson>
        {
            RuntimeFlashJson(),
            // Optionals absent: conversion must resolve the shared defaults.
            new EffectDescriptorJson
            {
                name = "DustPuff",
                effectType = Config.Backend.Exporter.EffectTypeParticleHook,
                trigger = "OnLand"
            }
        };
        atlas.animations[1] = walk;
        Check(EFYVPixelArtImporter.IsValidAtlasMetadata(atlas), "Effect atlas must validate.");

        EntityAtlasMetadata converted = (EntityAtlasMetadata)InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertAtlasMetadata", (AtlasMetadataJson?)atlas);
        // "idle" carried no effects: stored as null (no empty-array spam).
        Equal(null, converted.Animations[0].Effects);
        // "walk" resolves every field; absent optionals become the defaults.
        Equal(2, converted.Animations[1].Effects.Length);
        EntityEffectDescriptor flash = converted.Animations[1].Effects[0];
        Equal("HurtFlash", flash.Name);
        Equal(Config.Backend.Exporter.EffectTypeFlash, flash.EffectType);
        Equal(Config.Shared.EffectTriggerOnDamaged, flash.Trigger);
        Equal(0xFF0000FFu, flash.ColorRgba);
        Equal(150, flash.DurationMs);
        Equal(0.8f, flash.Strength);
        EntityEffectDescriptor hook = converted.Animations[1].Effects[1];
        Equal(Config.Backend.Exporter.EffectTypeParticleHook, hook.EffectType);
        Equal(Config.Backend.Exporter.DefaultEffectColorRgba, hook.ColorRgba);
        Equal(Config.Backend.Exporter.DefaultEffectDurationMs, hook.DurationMs);
        Equal(Config.Backend.Exporter.DefaultEffectStrength, hook.Strength);
        // The converted array is a COPY of the wire list.
        walk.effects[0] = new EffectDescriptorJson
        {
            name = "Mutated",
            effectType = Config.Backend.Exporter.EffectTypeTint,
            trigger = "X"
        };
        Equal("HurtFlash", converted.Animations[1].Effects[0].Name);
        walk.effects[0] = RuntimeFlashJson(); // restore: the document below reuses the list

        // End to end: a documentVersion-3 .efyvlaby with effects imports and
        // lands the descriptors on the ScriptableObject.
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-effects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var document = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"EffectImport\"")
                },
                hitboxes = new List<HitboxJson>(),
                atlas = atlas
            };
            string path = Path.Combine(tempRoot, "EffectImport" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(path, JsonSerializer.Serialize(document));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", path);
            EnemyData imported = AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(path) + Config.Game.Importer.PathSeparator +
                "EffectImport" + Config.Game.Importer.ExtensionAsset);
            Check(imported != null, "Effect .efyvlaby must import.");
            EntityEffectDescriptor[] importedEffects = imported.AtlasMetadata.Animations[1].Effects;
            Equal(2, importedEffects.Length);
            Equal("HurtFlash", importedEffects[0].Name);
            Equal(0xFF0000FFu, importedEffects[0].ColorRgba);
            Equal("OnLand", importedEffects[1].Trigger);
            Equal(null, imported.AtlasMetadata.Animations[0].Effects);

            // A broken effect (unknown type) rejects the whole import with the
            // per-cause atlas error.
            AtlasMetadataJson broken = ValidAtlas();
            AnimationMetadataJson brokenWalk = broken.animations[1];
            brokenWalk.effects = new List<EffectDescriptorJson>
            {
                new EffectDescriptorJson { name = "x", effectType = "sparkle", trigger = "OnSpawn" }
            };
            broken.animations[1] = brokenWalk;
            var brokenDocument = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"BrokenEffect\"")
                },
                hitboxes = new List<HitboxJson>(),
                atlas = broken
            };
            string brokenPath = Path.Combine(tempRoot, "BrokenEffect" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(brokenPath, JsonSerializer.Serialize(brokenDocument));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", brokenPath);
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(brokenPath) + Config.Game.Importer.PathSeparator +
                "BrokenEffect" + Config.Game.Importer.ExtensionAsset));
            Equal(string.Format(
                    Config.Game.Importer.LogErrorInvalidAtlas,
                    brokenPath,
                    EFYVBackend.Core.Export.AtlasMetadataError.AnimationEffects),
                Debug.Messages[^1]);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // ------------------------------------------------------------------
    // LivingEntity: flash/tint interpretation on the trigger seams.
    // ------------------------------------------------------------------
    private static LivingEntityData RuntimeEffectData(params EntityEffectDescriptor[] effects)
    {
        var data = ScriptableObject.CreateInstance<LivingEntityData>();
        FastSchemaBlock block = default;
        block.SetFloat((int)AssetSchema.MaxHealth, 100f);
        block.SetFloat((int)AssetSchema.BaseSpeed, 2f);
        data.SetSchemaBlock(block);
        var metadata = new EntityAtlasMetadata
        {
            FormatVersion = Config.Backend.Exporter.CurrentFormatVersion,
            FrameWidth = 16,
            FrameHeight = 16,
            AtlasWidth = 16,
            AtlasHeight = 16,
            Animations = new[]
            {
                new EntityAnimationMetadata
                {
                    Name = "idle",
                    FramesPerSecond = 8,
                    StartFrame = 0,
                    FrameCount = 1,
                    Effects = effects
                }
            }
        };
        data.SetImportedFacing(
            EFYVBackend.Core.Math.FastMath.FacingDirection.Down, metadata, null, null);
        return data;
    }

    private static void TestLivingEntityFlashTintRuntime()
    {
        var spawnTint = new EntityEffectDescriptor
        {
            Name = "SpawnTint",
            EffectType = Config.Game.EntityEffects.TypeTint,
            Trigger = Config.Game.EntityEffects.TriggerOnSpawn,
            ColorRgba = 0xFF00FF00u, // opaque green
            DurationMs = 0,
            Strength = 0.25f
        };
        var hurtFlash = new EntityEffectDescriptor
        {
            Name = "HurtFlash",
            EffectType = Config.Game.EntityEffects.TypeFlash,
            Trigger = Config.Game.EntityEffects.TriggerOnDamaged,
            ColorRgba = 0xFF0000FFu, // opaque red
            DurationMs = 150,
            Strength = 0.8f
        };

        Color green = new Color(0f, 1f, 0f, 1f);
        Color red = new Color(1f, 0f, 0f, 1f);
        Color tintColor = Color.Lerp(Color.white, green, 0.25f);
        Color flashOverTint = Color.Lerp(tintColor, red, 0.8f);

        ProbeLiving live = CreateComponent<ProbeLiving>(addRenderer: true);
        live.Initialize();
        live.LoadData(RuntimeEffectData(spawnTint, hurtFlash));

        // OnSpawn applies the spawn tint persistently (facing defaults Down).
        live.OnSpawn();
        Equal(tintColor, live.spriteRenderer.color);
        Check(!live.HasActiveEffectFlash);

        // A real hit flashes toward red ON TOP of the tint...
        live.TakeDamage(5f);
        Check(live.HasActiveEffectFlash);
        Equal(flashOverTint, live.spriteRenderer.color);

        // ...the central-loop countdown keeps it while time remains...
        live.TickAuthoredEffects(0.1f);
        Check(live.HasActiveEffectFlash);
        Equal(flashOverTint, live.spriteRenderer.color);

        // ...and expiry restores the persistent tint, not plain white.
        live.TickAuthoredEffects(0.0501f);
        Check(!live.HasActiveEffectFlash);
        Equal(tintColor, live.spriteRenderer.color);
        // Further ticks are inert.
        live.TickAuthoredEffects(10f);
        Equal(tintColor, live.spriteRenderer.color);

        // Zero/negative damage is not a hit: no flash fires.
        live.TakeDamage(-3f);
        Check(!live.HasActiveEffectFlash);
        Equal(tintColor, live.spriteRenderer.color);

        // Pooled reuse starts color-clean and re-applies the spawn effects.
        live.spriteRenderer.color = new Color(0.1f, 0.2f, 0.3f, 0.4f);
        live.OnSpawn();
        Equal(tintColor, live.spriteRenderer.color);
        Check(!live.HasActiveEffectFlash);

        // Without a tint, flash restores to clean white; a spawn resets the
        // countdown state outright.
        ProbeLiving flashOnly = CreateComponent<ProbeLiving>(addRenderer: true);
        flashOnly.Initialize();
        flashOnly.LoadData(RuntimeEffectData(hurtFlash));
        flashOnly.OnSpawn();
        Equal(Color.white, flashOnly.spriteRenderer.color);
        flashOnly.TakeDamage(1f);
        Equal(Color.Lerp(Color.white, red, 0.8f), flashOnly.spriteRenderer.color);
        flashOnly.OnSpawn(); // respawn mid-flash
        Check(!flashOnly.HasActiveEffectFlash);
        Equal(Color.white, flashOnly.spriteRenderer.color);

        // particleHook descriptors are stored but change nothing at runtime
        // (interpretation deferred until a particle pipeline exists).
        ProbeLiving hooked = CreateComponent<ProbeLiving>(addRenderer: true);
        hooked.Initialize();
        hooked.LoadData(RuntimeEffectData(new EntityEffectDescriptor
        {
            Name = "DustPuff",
            EffectType = Config.Game.EntityEffects.TypeParticleHook,
            Trigger = Config.Game.EntityEffects.TriggerOnDamaged,
            ColorRgba = 0xFF0000FFu,
            DurationMs = 150,
            Strength = 1f
        }));
        hooked.OnSpawn();
        hooked.TakeDamage(2f);
        Check(!hooked.HasActiveEffectFlash);
        Equal(Color.white, hooked.spriteRenderer.color);

        // Entities without a renderer or without imported facing data are
        // safe no-ops on every seam.
        ProbeLiving bare = CreateComponent<ProbeLiving>();
        bare.Initialize();
        bare.LoadData(RuntimeEffectData(hurtFlash));
        bare.OnSpawn();
        bare.TakeDamage(1f);
        bare.TickAuthoredEffects(1f);
        var facingless = ScriptableObject.CreateInstance<LivingEntityData>();
        ProbeLiving noFacing = CreateComponent<ProbeLiving>(addRenderer: true);
        noFacing.Initialize();
        noFacing.LoadData(facingless);
        noFacing.OnSpawn();
        Equal(Color.white, noFacing.spriteRenderer.color);

        // Enemy.Tick drives the countdown from the central loop.
        ProbeEnemy enemy = CreateComponent<ProbeEnemy>(addRenderer: true);
        enemy.Initialize();
        enemy.LoadData(RuntimeEffectData(hurtFlash));
        enemy.OnSpawn();
        enemy.TakeDamage(3f);
        Check(enemy.HasActiveEffectFlash);
        enemy.Tick(0.1f);
        Check(enemy.HasActiveEffectFlash);
        enemy.Tick(0.1f);
        Check(!enemy.HasActiveEffectFlash);
        Equal(Color.white, enemy.spriteRenderer.color);
        enemy.OnDespawn();
    }
}
