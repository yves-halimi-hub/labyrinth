using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;
using EFYV.Core.Interfaces;
using EFYV.Core.Weapons;
using EFYV.Core.Weapons.Types;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Data;
using UnityEditor;
using UnityEngine;

internal static partial class Program
{
    private static long assertions;

    private static void Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("schema block isolation and asset names", TestSchemaBlockIsolationAndAssetNames),
            ("designable types and registration contracts", TestDesignableTypesAndRegistrationContracts),
            ("atlas and directional import data", TestAtlasAndDirectionalImportData),
            ("importer validation and sprite slicing", TestImporterValidationAndSpriteSlicing),
            ("importer conversion and outside-in import", TestImporterConversionAndOutsideInImport),
            ("hitbox calculations and live refresh", TestHitboxCalculationsAndLiveRefresh),
            ("vector, transform, and viewport calculations", TestVectorTransformAndViewportCalculations),
            ("entity health, sprites, and lifecycle", TestEntityHealthSpritesAndLifecycle),
            ("enemy, boss, and packed-list mutation", TestEnemyBossAndPackedListMutation),
            ("projectile lifecycle and central iteration", TestProjectileLifecycleAndCentralIteration),
            ("power-ups, weapons, and inventory", TestPowerUpsWeaponsAndInventory),
            ("entity and GameObject pooling", TestEntityAndGameObjectPooling),
            ("spawning, AI scaling, and fallback maps", TestSpawningAiAndFallbackMaps),
            ("save progression and saturating arithmetic", TestSaveProgressionAndSaturatingArithmetic),
            ("props, purchases, and achievements", TestPropsPurchasesAndAchievements),
            ("camera and manager state machines", TestCameraAndManagerStateMachines),
            ("player i-frames, input, and progression", TestEntitiesPlayerIFramesInputAndProgression),
            ("enemy scaling, chase, and swap-list fuzz", TestEntitiesEnemyScalingChaseAndSwapListFuzz),
            ("boss weapon slots and eye bearer spawns", TestEntitiesBossWeaponSlotsAndEyeBearer),
            ("projectile piercing, reuse, and trigger filter", TestEntitiesProjectilePiercingReuseAndTriggerFilter),
            ("prop animation, doors, chests, and coins", TestEntitiesPropAnimationDoorsChestsAndCoins),
            ("merchant, sarcophage, and purchasables", TestEntitiesMerchantSarcophageAndPurchasables),
            ("weapon cooldown state machine", TestWeaponsCooldownStateMachine),
            ("magic wand targeting and upgrades", TestWeaponsMagicWandTargetingAndUpgrades),
            ("aura and melee boundary combat", TestWeaponsAuraMeleeBoundaries),
            ("orbital rotation and stacked damage", TestWeaponsOrbitalRotationAndDamage),
            ("drop and splash seeded placement", TestWeaponsDropAndSplashSeededPlacement),
            ("projectile weapon pool contracts", TestWeaponsProjectileWeaponPoolContracts),
            ("controller evolution matrix", TestWeaponsControllerEvolutionMatrix),
            ("powerup lifecycle reference model", TestWeaponsPowerUpLifecycleModel),
            ("manager PRNG and drop-loot reference model", TestManagersPrngAndDropLootModel),
            ("achievement bitmask reference model", TestManagersAchievementBitmaskModel),
            ("save binary layout and dirty-save debounce", TestManagersSaveBinaryLayoutAndDebounce),
            ("stat upgrade and combine rules", TestManagersStatUpgradeAndCombineRules),
            ("spawn accumulator reference simulation", TestManagersSpawnAccumulatorSimulation),
            ("seeded spawn placement model", TestManagersSeededSpawnPlacement),
            ("pool scheduling and singleton ownership", TestManagersPoolSchedulingAndSingletons),
            ("viewport ring buffer reference model", TestManagersViewportRingBufferModel),
            ("map switch state machine", TestManagersMapSwitchStateMachine),
            ("upgrade phase penalties and special attacks", TestManagersUpgradePhasePenalties),
            ("deep schema block bit preservation", TestDataEditorSchemaBlockBitPreservation),
            ("deep asset name hash reference model", TestDataEditorAssetNameHashReferenceModel),
            ("deep legacy achievement sync and hashes", TestDataEditorLegacyAchievementSyncAndHashes),
            ("deep facing import retention model", TestDataEditorFacingImportRetentionModel),
            ("deep hitbox gizmo drawing and bounds model", TestDataEditorHitboxGizmoDrawingAndBoundsModel),
            ("deep live debug bridge scheduling", TestDataEditorLiveDebugBridgeScheduling),
            ("deep texture preprocess and reimport rules", TestDataEditorTexturePreprocessAndReimportRules),
            ("deep import pipeline adversarial paths", TestDataEditorImportPipelineAdversarialPaths),
            ("save assets coalescing per postprocess group", TestDataEditorSaveAssetsCoalescing),
            ("deep schema isolation and interface contracts", TestDataEditorSchemaIsolationAndInterfaceContracts),
            // batch1/game-combat agent: weapon faction + game-over and upgrade-loop groups
            ("weapon faction ownership and game over", TestWeaponsFactionOwnershipAndGameOver),
            ("upgrade loop runtime wiring", TestUpgradeLoopRuntimeWiring),
            // b2-pipeline-contract agent: schema manifest (#15), prop schema reads,
            // baseAssetType/documentVersion contract (#16), RawArt watcher (#12)
            ("schema manifest import end to end", TestSchemaManifestImportEndToEnd),
            ("prop walkable and trap damage from schema block", TestPropWalkableTrapDamageFromSchemaBlock),
            ("importer base-type fallback and document version", TestImporterBaseTypeFallbackAndDocumentVersion),
            ("raw art watcher debounce and polling", TestRawArtWatcherDebounceAndPolling),
            // batch2/game-managers agent: #24 remainder, #25, #32, #34 groups
            ("singleton negative cache and invalidation", TestRuntimeSingletonNegativeCache),
            ("game over manager reactions", TestManagersGameOverReactions),
            ("scene-placed entity lifecycle and map cleanup", TestManagersScenePlacedLifecycle),
            ("pool prewarm wiring and xp gem drops", TestManagersPrewarmAndXpGemDrops),
            ("meta progression, buffs, and achievement triggers", TestManagersMetaProgressionAndAchievementTriggers),
            // batch2/unity-project agent: scene bootstrap groups
            ("bootstrap placeholder art and palette wiring", TestBootstrapPlaceholderArtAndPaletteWiring),
            ("bootstrap enemy data and pooled spawn stats", TestBootstrapEnemyDataAndPooledSpawnStats),
            // batch3.3 agent: item #10 atlas timing/playback metadata import
            ("importer animation timing metadata", TestImporterAnimationTimingMetadata),
            // batch3.4 agent (EffectsRuntimeTests.cs): item #7 runtime effect
            // descriptors - importer conversion + end-to-end import, and the
            // LivingEntity flash/tint interpretation on OnSpawn/OnDamaged
            ("importer effect descriptors end to end", TestImporterEffectDescriptorsEndToEnd),
            ("living entity flash and tint runtime", TestLivingEntityFlashTintRuntime),
            // batch3.5 agent (AttachmentRuntimeTests.cs): item #6 sub-element
            // attachment records - importer parse + storage on the asset and
            // the minimal LivingEntity consumer (rendering deferred).
            ("importer attachment records end to end", TestImporterAttachmentRecordsEndToEnd),
            ("living entity stores authored attachments", TestLivingEntityStoresAttachments),
            // batch3.6 agent (MapRuntimeTests.cs): item #5 maps + tilesets -
            // .efyvmap -> MapAssetData ingestion, the documentVersion-5
            // tileset manifest -> TilesetAssetData, and the imported-map
            // runtime path in MapViewportController.LoadMapData (procedural
            // noise demoted to the explicit fallback).
            ("map importer end to end", TestMapImporterEndToEnd),
            ("tileset import end to end", TestTilesetImportEndToEnd),
            ("map viewport imported maps and switching", TestMapViewportImportedMapsAndSwitching),
            // batch3.8 agent (CustomFieldsRuntimeTests.cs): item #33b runtime-
            // extensible asset fields - the string-keyed custom-property store
            // on SchemaBackedAssetData and the importer path that parks (and
            // still logs) unknown .efyvlaby properties keys there, clearing
            // stale entries on reimport.
            ("custom property store accessors", TestCustomPropertyStoreAccessors),
            ("importer custom properties end to end", TestImporterCustomPropertiesEndToEnd),
            // items #13 + #14 (FlipbookRuntimeTests.cs): the central-loop runtime
            // flipbook (fps + per-frame durations + loop range + ping-pong,
            // facing-change progress, Enemy walk/idle selection) and the designer
            // Hurtbox -> BoxCollider2D sync plus the always-on play-mode overlay.
            ("living entity flipbook playback", TestLivingEntityFlipbookPlayback),
            ("designer hitbox collider sync and overlay", TestDesignerHitboxColliderSyncAndOverlay),
            // item #4 (SpawnPaletteRuntimeTests.cs): the debug spawn palette +
            // data-to-prefab factory - archetype resolution (custom types fall
            // back to their base, unknown shapes rejected), the pooled spawn +
            // LoadData bind that lights up the flipbook/hurtbox (living entities)
            // and the animation loop (props), the window's list/refresh/
            // selection state machine, and the bridge's most-recent-import seam.
            ("spawn factory archetype resolution", TestSpawnFactoryArchetypeResolution),
            ("spawn factory pooled spawn and bind", TestSpawnFactorySpawnsAndBinds),
            ("spawn palette model state machine", TestSpawnPaletteModelStateMachine),
            ("spawn palette bridge auto offer", TestSpawnPaletteBridgeAutoOffer)
        };

        int failures = 0;
        for (int i = 0; i < tests.Length; i++)
        {
            try
            {
                ResetState();
                tests[i].Body();
                Console.WriteLine("PASS " + tests[i].Name);
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + tests[i].Name + ": " + exception);
            }
            finally
            {
                ResetState();
            }
        }

        Console.WriteLine(assertions.ToString("N0") + " assertions across " + tests.Length + " game/editor groups.");
        if (failures != 0) Environment.Exit(1);
    }

    private static void ResetState()
    {
        TestRuntime.Reset();
        FastPoolRegistry<GameEntity>.Clear();
        FastPoolRegistry<GameObject>.Clear();
        Enemy.ActiveEnemies.Clear();
        Projectile.ActiveProjectiles.Clear();
        PropEntity.ActiveAnimatedProps.Clear();
        EditorUtility.DirtyObjects.Clear();
        Handles.Labels.Clear();
        RenderTexture.active = null;
        Application.persistentDataPath = Path.GetTempPath();

        Type bridge = typeof(EFYV.Editor.EFYVPixelArtImporter).Assembly.GetType("EFYV.Editor.EFYVLiveDebugBridge", true);
        object pending = GetField(bridge, "PendingAssets", null);
        pending.GetType().GetMethod("Clear").Invoke(pending, null);
        SetField(bridge, "applyScheduled", null, false);
        // Item #4: the spawn-palette last-refreshed seam (auto-property backing
        // fields) must not leak across test groups.
        SetField(bridge, "<LastRefreshedAsset>k__BackingField", null, null);
        SetField(bridge, "<RefreshVersion>k__BackingField", null, 0);

        // batch2/game-managers agent: scene-placed entity registry (#25) and the
        // singleton negative caches (#24) must not leak across test groups -
        // TestRuntime.Reset destroys components without running scene loads, so
        // the caches are refreshed here explicitly (SingletonSearchCache rule 2).
        GameEntity.ClearSceneRegistration();
        EFYV.Core.Utils.SingletonSearchCache.Invalidate();
    }

    private static void Check(bool condition, string message = null)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message ?? "Assertion failed.");
    }

    private static void Equal<T>(T expected, T actual, string message = null)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message ??
                "Expected <" + expected + "> but got <" + actual + ">.");
        }
    }

    private static void Same(object expected, object actual, string message = null)
    {
        assertions++;
        if (!ReferenceEquals(expected, actual))
            throw new InvalidOperationException(message ?? "References were not identical.");
    }

    private static void NotSame(object left, object right, string message = null)
    {
        assertions++;
        if (ReferenceEquals(left, right))
            throw new InvalidOperationException(message ?? "References unexpectedly aliased.");
    }

    private static void Near(float expected, float actual, float tolerance = 0.0001f, string message = null)
    {
        assertions++;
        if (float.IsNaN(actual) || MathF.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(message ??
                "Expected approximately <" + expected + "> but got <" + actual + ">.");
        }
    }

    private static TException Throws<TException>(Action action) where TException : Exception
    {
        assertions++;
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Expected " + typeof(TException).Name + " but got " + exception.GetType().Name + ".", exception);
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + " but no exception was thrown.");
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static object GetField(Type type, string name, object target)
    {
        return FindField(type, name).GetValue(target);
    }

    private static T GetField<T>(object target, string name)
    {
        return (T)GetField(target.GetType(), name, target);
    }

    private static void SetField(Type type, string name, object target, object value)
    {
        FindField(type, name).SetValue(target, value);
    }

    private static void SetField(object target, string name, object value)
    {
        SetField(target.GetType(), name, target, value);
    }

    private static MethodInfo FindMethod(Type type, string name, bool isStatic, int parameterCount = -1)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        for (Type current = type; current != null; current = current.BaseType)
        {
            foreach (MethodInfo method in current.GetMethods(flags | BindingFlags.DeclaredOnly))
            {
                if (method.Name == name && (parameterCount < 0 || method.GetParameters().Length == parameterCount))
                    return method;
            }
        }
        throw new MissingMethodException(type.FullName, name);
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        return InvokeMethod(FindMethod(target.GetType(), name, false, arguments.Length), target, arguments);
    }

    private static object InvokeStatic(Type type, string name, params object[] arguments)
    {
        return InvokeMethod(FindMethod(type, name, true, arguments.Length), null, arguments);
    }

    private static object InvokeMethod(MethodInfo method, object target, object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static JsonElement JsonValue(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static T CreateComponent<T>(bool addRenderer = false, bool invokeAwake = false)
        where T : Component
    {
        var gameObject = new GameObject(typeof(T).Name);
        if (addRenderer) gameObject.AddComponent<SpriteRenderer>();
        return (T)gameObject.AddComponent(typeof(T), invokeAwake);
    }

    private sealed class ProbeEntity : GameEntity
    {
        public int SpawnCalls;
        public int DespawnCalls;
        public override void OnSpawn() { SpawnCalls++; base.OnSpawn(); }
        public override void OnDespawn() { DespawnCalls++; base.OnDespawn(); }
    }

    private class ProbeLiving : LivingEntity
    {
        public int MoveCalls;
        public int RefreshCalls;
        public void Apply(float maxHealth, float speed, bool preserve = false)
        {
            FastSchemaBlock block = default;
            block.SetFloat((int)AssetSchema.MaxHealth, maxHealth);
            block.SetFloat((int)AssetSchema.BaseSpeed, speed);
            ApplySchemaBlock(block, preserve);
        }
        public void SetHealth(float value) { CurrentHealth = value; }
        protected override void Move(float deltaTime) { MoveCalls++; }
        public override void RefreshDataFromAsset() { RefreshCalls++; base.RefreshDataFromAsset(); }
    }

    private class ProbeEnemy : Enemy
    {
        public int TickMoves;
        public int DeathCalls;
        public void SetTarget(Transform value) { TargetPlayer = value; }
        public void Apply(float maxHealth, float speed, float damage, float experience)
        {
            FastSchemaBlock block = default;
            block.SetFloat((int)AssetSchema.MaxHealth, maxHealth);
            block.SetFloat((int)AssetSchema.BaseSpeed, speed);
            block.SetFloat((int)AssetSchema.DamageToPlayer, damage);
            block.SetFloat((int)AssetSchema.ExperienceValue, experience);
            ApplySchemaBlock(block, false);
        }
        public void SetHealth(float value) { CurrentHealth = value; }
        protected override void Move(float deltaTime) { TickMoves++; }
        public override void Die() { DeathCalls++; base.Die(); }
    }

    private sealed class ProbeBoss : BossEnemy
    {
        public void Apply(float maxHealth, float speed, float threshold)
        {
            FastSchemaBlock block = default;
            block.SetFloat((int)AssetSchema.MaxHealth, maxHealth);
            block.SetFloat((int)AssetSchema.BaseSpeed, speed);
            block.SetFloat((int)AssetSchema.Phase2HealthThreshold, threshold);
            ApplySchemaBlock(block, false);
        }
    }

    private sealed class ProbeProjectile : Projectile
    {
        public int DespawnCalls;
        public override void OnDespawn() { DespawnCalls++; base.OnDespawn(); }
    }

    private class ProbeWeapon : Weapon
    {
        public int FireCalls;
        public void Configure(float cooldown, float damage, int level = 1)
        {
            CooldownTime = cooldown;
            BaseDamage = damage;
            Level = level;
            currentCooldown = 0f;
        }
        public float RemainingCooldown => currentCooldown;
        public override void Fire() { FireCalls++; }
    }

    private sealed class ProbeAuraWeapon : AuraWeapon
    {
        public void SetDamage(float value) { BaseDamage = value; }
    }

    private sealed class ProbeMeleeWeapon : MeleeWeapon
    {
        public void SetDamage(float value) { BaseDamage = value; }
    }

    private sealed class ProbeOrbitalWeapon : OrbitalWeapon
    {
        public void SetDamage(float value) { BaseDamage = value; }
    }

    private sealed class ProbeProp : PropEntity
    {
        public int Frame => currentFrame;
        public float Timer => animTimer;
        public int RefreshCalls;
        public new void RefreshDataFromAsset() { RefreshCalls++; base.RefreshDataFromAsset(); }
    }

    private sealed class ProbeDamageable : IDamageable
    {
        public float TotalDamage;
        public int DieCalls;
        public void TakeDamage(float amount) { TotalDamage += amount; }
        public void Die() { DieCalls++; }
    }
}
