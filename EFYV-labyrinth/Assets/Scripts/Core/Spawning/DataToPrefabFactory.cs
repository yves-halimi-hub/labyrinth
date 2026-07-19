using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;
using EFYV.Core.Managers;
using SpawnConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game.SpawnPalette;

namespace EFYV.Core.Spawning
{
    // Item #4: supplies the archetype template prefab the factory clones. The
    // editor window backs this with AssetDatabase-loaded prefabs; the runtime
    // tests back it with hand-built template instances. The factory itself never
    // touches the asset database, so it stays runtime-testable in the harness.
    public interface ISpawnTemplateProvider
    {
        GameEntity GetTemplate(SpawnArchetype archetype);
    }

    // Item #4: turns an imported SchemaBackedAssetData into a live, pooled game
    // object. It resolves the asset's generic archetype, rents the matching
    // template prefab through PoolManager (respecting the per-prefab pool keys
    // and pooled-instantiation guards), and binds the asset onto the spawned
    // instance through the existing LivingEntity.LoadData / PropEntity.LoadData
    // paths - so the spawned clone drives the item #13 flipbook animation and
    // the item #14 hurtbox collider exactly like a scene-authored entity.
    public static class DataToPrefabFactory
    {
        // Resolves the generic archetype for an imported asset by walking its
        // data type. BossData is checked before EnemyData (which it extends);
        // any other living-entity data is an Enemy; any GameAssetData (plain
        // props, tilesets, and custom prop types) is a Prop. A plain EntityData
        // or an unrecognized SchemaBackedAssetData has no archetype and is
        // rejected. Custom registered types (EvilEyeData, TutData, PyramidsData,
        // ...) resolve through their base archetype automatically because they
        // subclass one of these bases.
        public static bool TryResolveArchetype(SchemaBackedAssetData data, out SpawnArchetype archetype)
        {
            switch (data)
            {
                case BossData _:
                    archetype = SpawnArchetype.Boss;
                    return true;
                case LivingEntityData _:
                    archetype = SpawnArchetype.Enemy;
                    return true;
                case GameAssetData _:
                    archetype = SpawnArchetype.Prop;
                    return true;
                default:
                    archetype = default;
                    return false;
            }
        }

        // Spawns the asset through the pool and binds it. Returns the live
        // instance, or null with a per-cause console error when the asset has no
        // archetype, the archetype template is missing, or the pool is exhausted.
        public static GameEntity Spawn(
            SchemaBackedAssetData data,
            ISpawnTemplateProvider templates,
            PoolManager pool,
            Vector3 position,
            Quaternion rotation)
        {
            if (data == null || templates == null || pool == null) return null;

            string displayName = ResolveDisplayName(data);
            if (!TryResolveArchetype(data, out SpawnArchetype archetype))
            {
                Debug.LogError(string.Format(
                    SpawnConfig.LogErrorUnknownArchetype, displayName, data.GetType().Name));
                return null;
            }

            GameEntity template = templates.GetTemplate(archetype);
            if (template == null)
            {
                Debug.LogError(string.Format(
                    SpawnConfig.LogErrorNoTemplate, displayName, ArchetypeName(archetype)));
                return null;
            }

            // Bind BEFORE OnSpawn so a prop registers for animation (OnSpawn
            // only lists props that already carry frames) and an enemy scales its
            // spawn stats from the authored data - the order the scene bootstrap
            // uses.
            GameEntity spawned = pool.Spawn(template, position, rotation, instance => BindData(instance, data));
            if (spawned == null)
            {
                Debug.LogError(string.Format(
                    SpawnConfig.LogErrorPoolEmpty, displayName, ArchetypeName(archetype)));
                return null;
            }

            Debug.Log(string.Format(
                SpawnConfig.LogSpawned, displayName, ArchetypeName(archetype), position.x, position.y));
            return spawned;
        }

        // Binds the imported asset onto a freshly spawned instance through the
        // same LoadData seam scene-authored entities use. Type-guarded so a
        // template whose concrete component does not match the archetype's data
        // is a safe no-op rather than a cast crash.
        public static void BindData(GameEntity instance, SchemaBackedAssetData data)
        {
            if (instance is LivingEntity living && data is LivingEntityData livingData)
            {
                living.LoadData(livingData);
            }
            else if (instance is PropEntity prop && data is GameAssetData gameAssetData)
            {
                prop.LoadData(gameAssetData);
            }
        }

        // The human-facing name of an imported asset: the authored
        // entityName/assetName when present, else the ScriptableObject name.
        public static string ResolveDisplayName(SchemaBackedAssetData data)
        {
            if (data == null) return SpawnConfig.UnnamedAsset;
            switch (data)
            {
                case EntityData entityData when !string.IsNullOrEmpty(entityData.entityName):
                    return entityData.entityName;
                case GameAssetData gameAssetData when !string.IsNullOrEmpty(gameAssetData.assetName):
                    return gameAssetData.assetName;
            }
            return string.IsNullOrEmpty(data.name) ? SpawnConfig.UnnamedAsset : data.name;
        }

        public static string ArchetypeName(SpawnArchetype archetype)
        {
            switch (archetype)
            {
                case SpawnArchetype.Enemy: return SpawnConfig.ArchetypeNameEnemy;
                case SpawnArchetype.Boss: return SpawnConfig.ArchetypeNameBoss;
                default: return SpawnConfig.ArchetypeNameProp;
            }
        }
    }
}
