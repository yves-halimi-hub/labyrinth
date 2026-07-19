// Item #4: the game-side debug spawn palette + data-to-prefab factory.
//
// - DataToPrefabFactory.TryResolveArchetype maps every imported
//   SchemaBackedAssetData onto one of the Enemy/Boss/Prop generic archetypes
//   (custom registered types fall back to their base archetype), and rejects
//   the data shapes the importer never produces.
// - DataToPrefabFactory.Spawn rents the matching archetype template through
//   PoolManager and binds the asset via LivingEntity/PropEntity LoadData BEFORE
//   OnSpawn, so the pooled clone drives the item #13 flipbook animation and the
//   item #14 hurtbox collider (living entities) or the animation loop (props).
// - SpawnPaletteModel is the window's list/refresh/selection state machine, and
//   EFYVLiveDebugBridge records the most recently imported asset the palette
//   auto-offers.
using System;
using EFYV.Core.Data;
using EFYV.Core.Data.Entities;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;
using EFYV.Core.Entities.Environment.Implementations;
using EFYV.Core.Managers;
using EFYV.Core.Spawning;
using EFYV.Editor;
using EFYVBackend.Core.Data;
using UnityEditor;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;
using FacingDirection = EFYVBackend.Core.Math.FastMath.FacingDirection;

internal static partial class Program
{
    private sealed class FakeTemplateProvider : ISpawnTemplateProvider
    {
        public GameEntity Enemy;
        public GameEntity Boss;
        public GameEntity Prop;

        public GameEntity GetTemplate(SpawnArchetype archetype)
        {
            switch (archetype)
            {
                case SpawnArchetype.Enemy: return Enemy;
                case SpawnArchetype.Boss: return Boss;
                default: return Prop;
            }
        }
    }

    // A prefab-shaped template: the presentation siblings are added BEFORE the
    // entity component so the pooled clone (which mirrors Unity's whole-object
    // Instantiate) caches them in Initialize, exactly like a real prefab.
    private static T BuildSpawnTemplate<T>(bool addCollider) where T : Component
    {
        var gameObject = new GameObject(typeof(T).Name + "Template");
        gameObject.AddComponent<SpriteRenderer>();
        if (addCollider) gameObject.AddComponent<BoxCollider2D>();
        return (T)gameObject.AddComponent(typeof(T), false);
    }

    private static T LivingDataWithFacing<T>(EntityHitboxRecord[] hitboxes) where T : LivingEntityData
    {
        var data = ScriptableObject.CreateInstance<T>();
        FastSchemaBlock block = default;
        block.SetFloat((int)AssetSchema.MaxHealth, 50f);
        block.SetFloat((int)AssetSchema.BaseSpeed, 3f);
        data.SetSchemaBlock(block);
        var animations = new[] { FlipbookAnim("idle", 10, 0, 2, 0, 1) };
        var atlas = new EntityAtlasMetadata
        {
            FrameWidth = 16,
            FrameHeight = 16,
            AtlasWidth = 32,
            AtlasHeight = 16,
            Animations = animations
        };
        data.SetImportedFacing(FacingDirection.Down, atlas, NamedFrames("f0", "f1"), hitboxes);
        return data;
    }

    private static EnemyData NamedEnemyData(string name)
    {
        var data = ScriptableObject.CreateInstance<EnemyData>();
        data.entityName = name;
        return data;
    }

    private static GameAssetData NamedPropData(string name)
    {
        var data = ScriptableObject.CreateInstance<GameAssetData>();
        data.assetName = name;
        return data;
    }

    private static void AssertArchetype<T>(SpawnArchetype expected) where T : SchemaBackedAssetData
    {
        var data = ScriptableObject.CreateInstance<T>();
        Check(DataToPrefabFactory.TryResolveArchetype(data, out SpawnArchetype archetype),
            typeof(T).Name + " must resolve an archetype.");
        Equal(expected, archetype, typeof(T).Name + " archetype");
    }

    private static void TestSpawnFactoryArchetypeResolution()
    {
        // Base archetypes.
        AssertArchetype<EnemyData>(SpawnArchetype.Enemy);
        AssertArchetype<BossData>(SpawnArchetype.Boss);
        AssertArchetype<LivingEntityData>(SpawnArchetype.Enemy);
        AssertArchetype<GameAssetData>(SpawnArchetype.Prop);
        AssertArchetype<TilesetAssetData>(SpawnArchetype.Prop);

        // Custom registered types fall back to their base archetype (they
        // subclass one of the bases, so no per-type table is needed).
        AssertArchetype<EvilEyeData>(SpawnArchetype.Enemy);
        AssertArchetype<EyeBearerData>(SpawnArchetype.Enemy);
        AssertArchetype<TutData>(SpawnArchetype.Boss);
        AssertArchetype<NefertitiData>(SpawnArchetype.Boss);
        AssertArchetype<PyramidsData>(SpawnArchetype.Prop);
        AssertArchetype<PyramidDoorData>(SpawnArchetype.Prop);

        // Boss is checked before Enemy (BossData : EnemyData): a boss never
        // resolves as the enemy archetype.
        var boss = ScriptableObject.CreateInstance<BossData>();
        DataToPrefabFactory.TryResolveArchetype(boss, out SpawnArchetype bossArchetype);
        Equal(SpawnArchetype.Boss, bossArchetype);

        // A plain EntityData (never produced by the importer) and a null asset
        // have no archetype and are rejected cleanly.
        var plain = ScriptableObject.CreateInstance<EntityData>();
        Check(!DataToPrefabFactory.TryResolveArchetype(plain, out _), "Plain EntityData has no archetype.");
        Check(!DataToPrefabFactory.TryResolveArchetype(null, out _), "Null asset has no archetype.");

        // Display-name resolution: authored identity wins, else the object name.
        Equal("Alpha", DataToPrefabFactory.ResolveDisplayName(NamedEnemyData("Alpha")));
        Equal("Crate", DataToPrefabFactory.ResolveDisplayName(NamedPropData("Crate")));
        var unnamed = ScriptableObject.CreateInstance<EnemyData>();
        Equal(Config.Game.SpawnPalette.UnnamedAsset, DataToPrefabFactory.ResolveDisplayName(unnamed));
    }

    private static void TestSpawnFactorySpawnsAndBinds()
    {
        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var provider = new FakeTemplateProvider
        {
            Enemy = BuildSpawnTemplate<Monster>(addCollider: true),
            Boss = BuildSpawnTemplate<Boss>(addCollider: true),
            Prop = BuildSpawnTemplate<GenericProp>(addCollider: false)
        };

        // ---- Enemy: pooled, LoadData-bound, flipbook + hurtbox active ----
        var hurt = new Rect(4f, 4f, 8f, 6f);
        var hitboxes = new[]
        {
            new EntityHitboxRecord
            {
                FrameIndex = 0,
                HitboxType = Config.Game.Hitbox.HurtboxType,
                Bounds = hurt
            }
        };
        EnemyData enemyData = LivingDataWithFacing<EnemyData>(hitboxes);
        GameEntity enemySpawned = DataToPrefabFactory.Spawn(
            enemyData, provider, pool, new Vector3(3f, 5f, 0f), Quaternion.identity);

        Check(enemySpawned is Monster, "Enemy archetype spawns the Monster template.");
        NotSame(provider.Enemy, enemySpawned);
        var enemy = (Monster)enemySpawned;
        Check(enemy.IsSpawned, "Spawned enemy is live.");
        Check(enemy.prefabPoolKey != Config.Game.Entity.EmptyPrefabPoolKey, "Spawned enemy is pooled.");
        Same(enemyData, enemy.SourceData, "LoadData bound the imported asset onto the clone.");
        Check(enemy.HasRuntimeFlipbook, "The bound clone plays the imported flipbook.");
        Equal("idle", enemy.CurrentAnimationName);
        Equal(1, Enemy.ActiveEnemies.Count, "The spawned enemy joined the central tick list.");
        Same(enemy, Enemy.ActiveEnemies[0]);

        // Item #14: the frame-0 Hurtbox drove the clone's own BoxCollider2D
        // (the presentation siblings survived pooling), using the shared
        // pixel-to-local-units math.
        var collider = enemy.GetComponent<BoxCollider2D>();
        Check(collider != null, "The enemy template's BoxCollider2D survived pooling.");
        enemyData.TryGetImportedFacing(FacingDirection.Down, out EntityFacingImportData facing);
        EntityAtlasMetadata atlas = facing.AtlasMetadata;
        Check(EntityHitboxGeometry.TryGetLocalBounds(atlas, hurt, out Vector3 center, out Vector3 size));
        Near(center.x, collider.offset.x, 0f);
        Near(center.y, collider.offset.y, 0f);
        Near(size.x, collider.size.x, 0f);
        Near(size.y, collider.size.y, 0f);

        // ---- Boss: routed to the Boss template, bound, flipbook active ----
        BossData bossData = LivingDataWithFacing<BossData>(null);
        GameEntity bossSpawned = DataToPrefabFactory.Spawn(
            bossData, provider, pool, Vector3.zero, Quaternion.identity);
        Check(bossSpawned is Boss, "Boss archetype spawns the Boss template.");
        NotSame(provider.Boss, bossSpawned);
        Same(bossData, ((Boss)bossSpawned).SourceData);
        Check(((Boss)bossSpawned).HasRuntimeFlipbook, "The bound boss plays the imported flipbook.");
        Equal(2, Enemy.ActiveEnemies.Count, "The boss also joined the central tick list.");

        // ---- Prop: routed to the GenericProp template, animation active ----
        GameAssetData propData = NamedPropData("DebugCrate");
        propData.SetImportedFrames(NamedFrames("c0", "c1", "c2"));
        GameEntity propSpawned = DataToPrefabFactory.Spawn(
            propData, provider, pool, new Vector3(1f, 1f, 0f), Quaternion.identity);
        Check(propSpawned is GenericProp, "Prop archetype spawns the GenericProp template.");
        var prop = (GenericProp)propSpawned;
        Same(propData, prop.SourceData, "LoadData bound the imported prop asset.");
        Check(prop.IsSpawned, "Spawned prop is live.");
        Check(prop.animationFrames != null && prop.animationFrames.Length == 3,
            "Imported frames became the prop animation set.");
        Equal(1, PropEntity.ActiveAnimatedProps.Count,
            "Binding before OnSpawn registered the animated prop in the central loop.");
        Check(prop.spriteRenderer.sprite != null, "The prop shows an imported frame.");

        // ---- custom registered enemy type routes to the Enemy template ----
        EvilEyeData evilEye = LivingDataWithFacing<EvilEyeData>(null);
        GameEntity customSpawned = DataToPrefabFactory.Spawn(
            evilEye, provider, pool, Vector3.zero, Quaternion.identity);
        Check(customSpawned is Monster, "A custom EnemyData subtype routes to the Enemy template.");
        Same(evilEye, ((Monster)customSpawned).SourceData);

        // ---- rejections add nothing ----
        int enemiesBefore = Enemy.ActiveEnemies.Count;
        int propsBefore = PropEntity.ActiveAnimatedProps.Count;
        var plain = ScriptableObject.CreateInstance<EntityData>();
        Check(DataToPrefabFactory.Spawn(plain, provider, pool, Vector3.zero, Quaternion.identity) == null,
            "An asset with no archetype is rejected cleanly.");
        Equal(enemiesBefore, Enemy.ActiveEnemies.Count, "A rejected spawn adds no enemy.");
        Equal(propsBefore, PropEntity.ActiveAnimatedProps.Count, "A rejected spawn adds no prop.");

        var emptyProvider = new FakeTemplateProvider();
        Check(DataToPrefabFactory.Spawn(
                LivingDataWithFacing<EnemyData>(null), emptyProvider, pool, Vector3.zero, Quaternion.identity) == null,
            "A missing archetype template is rejected cleanly.");

        // ---- null-argument guards ----
        Check(DataToPrefabFactory.Spawn(null, provider, pool, Vector3.zero, Quaternion.identity) == null);
        Check(DataToPrefabFactory.Spawn(propData, null, pool, Vector3.zero, Quaternion.identity) == null);
        Check(DataToPrefabFactory.Spawn(propData, provider, null, Vector3.zero, Quaternion.identity) == null);
    }

    private static void TestSpawnPaletteModelStateMachine()
    {
        var model = new SpawnPaletteModel();
        Equal(0, model.Count);
        Equal(-1, model.SelectedIndex);
        Check(model.SelectedAsset == null);
        Check(!model.HasSelection);

        EnemyData a = NamedEnemyData("Alpha");
        GameAssetData b = NamedPropData("Beta");
        var c = ScriptableObject.CreateInstance<EntityData>();
        c.name = "Gamma";

        // Duplicates collapse (by reference) and nulls are skipped.
        model.SetAssets(new SchemaBackedAssetData[] { a, b, c, a, null });
        Equal(3, model.Count, "Duplicates and nulls are filtered.");
        Equal(0, model.SelectedIndex, "Selection defaults to the first entry.");
        Same(a, model.SelectedAsset);

        // Entry metadata: display name, spawnable flag, archetype.
        Equal("Alpha", model.Entries[0].DisplayName);
        Check(model.Entries[0].CanSpawn);
        Equal(SpawnArchetype.Enemy, model.Entries[0].Archetype);
        Equal("Beta", model.Entries[1].DisplayName);
        Equal(SpawnArchetype.Prop, model.Entries[1].Archetype);
        Check(!model.Entries[2].CanSpawn, "A no-archetype asset is listed but not spawnable.");

        // Explicit selection with range guard.
        Check(model.Select(1));
        Same(b, model.SelectedAsset);
        Check(model.TryGetSelectedEntry(out SpawnPaletteModel.Entry selectedEntry));
        Same(b, selectedEntry.Asset);
        Check(!model.Select(5), "Out-of-range selection is rejected.");
        Same(b, model.SelectedAsset);
        Check(!model.Select(-1));

        // Selection survives a rediscovery that still contains it.
        model.SetAssets(new SchemaBackedAssetData[] { c, a, b });
        Same(b, model.SelectedAsset, "Selection is preserved by reference across rediscovery.");

        // A vanished selection falls back to the first entry.
        model.SetAssets(new SchemaBackedAssetData[] { a, c });
        Same(a, model.SelectedAsset, "A vanished selection falls back to the first entry.");

        // Emptying clears selection.
        model.SetAssets(Array.Empty<SchemaBackedAssetData>());
        Equal(-1, model.SelectedIndex);
        Check(model.SelectedAsset == null);
        Check(!model.TryGetSelectedEntry(out _));

        // Auto-offer: refreshing a listed asset selects it immediately.
        model.SetAssets(new SchemaBackedAssetData[] { a, b });
        model.NotifyRefreshed(b);
        Same(b, model.SelectedAsset, "A refresh of a listed asset auto-selects it.");

        // Auto-offer for a not-yet-listed asset selects it on the next discovery.
        EnemyData d = NamedEnemyData("Delta");
        model.NotifyRefreshed(d);
        Same(b, model.SelectedAsset, "An unlisted refresh leaves the current selection until it appears.");
        model.SetAssets(new SchemaBackedAssetData[] { a, b, d });
        Same(d, model.SelectedAsset, "The pending auto-offer is honored when the asset appears.");

        // The auto-offer is one-shot: a later rediscovery just preserves it.
        model.SetAssets(new SchemaBackedAssetData[] { d, a, b });
        Same(d, model.SelectedAsset, "Auto-offer does not re-fire; the selection is now merely preserved.");
        model.Select(1);
        Same(a, model.SelectedAsset);
        model.SetAssets(new SchemaBackedAssetData[] { d, a, b });
        Same(a, model.SelectedAsset, "With no pending auto-offer, a manual selection is preserved.");

        // A null refresh is ignored.
        model.NotifyRefreshed(null);
        Same(a, model.SelectedAsset);
    }

    private static void TestSpawnPaletteBridgeAutoOffer()
    {
        Check(EFYVLiveDebugBridge.LastRefreshedAsset == null, "Reset clears the last-refreshed asset.");
        int baseVersion = EFYVLiveDebugBridge.RefreshVersion;

        EnemyData a = NamedEnemyData("Alpha");
        EnemyData b = NamedEnemyData("Bravo");

        // The palette's auto-offer seam records the newest import in edit mode
        // too (a spawn candidate can be imported before Play Mode starts), while
        // the scene-entity refresh path stays Play-Mode-only.
        EditorApplication.isPlaying = false;
        EFYVLiveDebugBridge.QueueRefresh(a);
        Same(a, EFYVLiveDebugBridge.LastRefreshedAsset);
        Equal(baseVersion + 1, EFYVLiveDebugBridge.RefreshVersion);

        // A null refresh records nothing and does not bump the version.
        EFYVLiveDebugBridge.QueueRefresh(null);
        Same(a, EFYVLiveDebugBridge.LastRefreshedAsset);
        Equal(baseVersion + 1, EFYVLiveDebugBridge.RefreshVersion);

        EFYVLiveDebugBridge.QueueRefresh(b);
        Same(b, EFYVLiveDebugBridge.LastRefreshedAsset);
        Equal(baseVersion + 2, EFYVLiveDebugBridge.RefreshVersion);

        // The window's poll loop feeds the newest refresh into the model.
        var model = new SpawnPaletteModel();
        model.SetAssets(new SchemaBackedAssetData[] { a, b });
        model.NotifyRefreshed(EFYVLiveDebugBridge.LastRefreshedAsset);
        Same(b, model.SelectedAsset, "The palette auto-offers the bridge's latest refresh.");
    }
}
