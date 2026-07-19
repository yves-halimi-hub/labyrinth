using System.Collections.Generic;
using UnityEngine;
using EFYV.Core.Interfaces;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public abstract class GameEntity : MonoBehaviour, IPoolable
    {
        protected EFYVBackend.Core.Models.EntityData Data = new EFYVBackend.Core.Models.EntityData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public Transform entityTransform { get; private set; }
        public SpriteRenderer spriteRenderer { get; private set; }
        public bool IsSpawned { get; private set; }

        // ------------------------------------------------------------------
        // Scene-placed entity registration (#25).
        //
        // Entities dropped directly into a scene (never rented from a pool) used to
        // be invisible to the centralized update loops: enemies never Ticked, props
        // never animated, and map switches left them behind. Awake now registers
        // such entities as "pending scene-placed"; SpawnManager.Update (and the map
        // switch routine) promote them by invoking OnSpawn, which routes them into
        // the existing per-type FastSwapLists (Enemy.ActiveEnemies,
        // PropEntity.ActiveAnimatedProps, ...) so they Tick, are targetable, and
        // are cleaned up on map switch exactly like pooled entities.
        //
        // The pooled path is respected: PoolManager brackets its factory
        // Instantiate calls with Begin/EndPooledInstantiation so pool clones are
        // never registered here. Prefab assets are excluded because they live in
        // an invalid scene (gameObject.scene.IsValid() is false in Unity for
        // assets; test harnesses mark prefab stand-ins the same way).
        //
        // These are plain lists (not FastSwapList) on purpose: each subclass's
        // single ActiveListIndex slot is already owned by its per-type swap list,
        // and this registry is only consumed at promotion/map-switch time - never
        // per frame - so O(1) swap removal buys nothing here.
        // ------------------------------------------------------------------
        private static readonly List<GameEntity> pendingSceneEntities = new List<GameEntity>();
        private static readonly List<GameEntity> trackedSceneEntities = new List<GameEntity>();
        private static int pooledInstantiationDepth;

        public static int PendingSceneEntityCount => pendingSceneEntities.Count;
        public static int TrackedSceneEntityCount => trackedSceneEntities.Count;

        // The player is scene-placed but must never be auto-despawned by the map
        // switch cleanup (it is repositioned instead); PlayerController opts out.
        protected virtual bool TracksAsScenePlaced => true;

        public static void BeginPooledInstantiation() => pooledInstantiationDepth++;

        public static void EndPooledInstantiation()
        {
            if (pooledInstantiationDepth > GameConfig.Runtime.EmptyCollectionCount) pooledInstantiationDepth--;
        }

        // Promotes every pending scene-placed entity into the live world: not-yet-
        // spawned entities get OnSpawn (joining their type's swap list), and all of
        // them move into the tracked list consumed by the map-switch cleanup.
        // Allocation-free when nothing is pending.
        public static void ActivatePendingSceneEntities()
        {
            int pendingCount = pendingSceneEntities.Count;
            if (pendingCount == GameConfig.Runtime.EmptyCollectionCount) return;

            for (int i = 0; i < pendingCount; i++)
            {
                GameEntity entity = pendingSceneEntities[i];
                if (entity == null || entity.gameObject == null) continue;
                if (!entity.gameObject.activeInHierarchy) continue; // Destroyed, despawned, or deactivated before promotion.

                if (!entity.IsSpawned) entity.OnSpawn();
                trackedSceneEntities.Add(entity);
            }
            pendingSceneEntities.Clear();
        }

        // Map-switch cleanup (#25): deactivates every tracked scene-placed entity
        // through the normal despawn path and forgets it (the old map's scene
        // content must not leak into the new map). Safe against entities that were
        // already despawned by the per-type loops or destroyed outright.
        public static void DespawnTrackedSceneEntities()
        {
            int trackedCount = trackedSceneEntities.Count;
            for (int i = 0; i < trackedCount; i++)
            {
                GameEntity entity = trackedSceneEntities[i];
                if (entity == null || entity.gameObject == null) continue;
                entity.ReleaseToPool();
            }
            trackedSceneEntities.Clear();
        }

        // Full registry reset for world teardown (PoolManager.OnDestroy) and test
        // isolation. Does not touch the entities themselves.
        public static void ClearSceneRegistration()
        {
            pendingSceneEntities.Clear();
            trackedSceneEntities.Clear();
            pooledInstantiationDepth = GameConfig.Runtime.EmptyCollectionCount;
        }

        // Tracks which pool this object belongs to
        public int prefabPoolKey
        {
            get => Data.PrefabPoolKey;
            set => Data.PrefabPoolKey = value;
        }

        protected virtual void Awake()
        {
            Initialize();
            RegisterScenePlacedIfEligible();
        }

        private void RegisterScenePlacedIfEligible()
        {
            if (pooledInstantiationDepth > GameConfig.Runtime.EmptyCollectionCount) return; // Pool factory clone.
            if (!TracksAsScenePlaced) return; // The player manages its own lifecycle.
            if (gameObject == null || !gameObject.scene.IsValid()) return; // Prefab asset, not a scene object.

            pendingSceneEntities.Add(this);
        }

        public virtual void Initialize()
        {
            entityTransform = transform;
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public virtual void OnSpawn()
        {
            IsSpawned = true;
            gameObject.SetActive(GameConfig.Pool.Active);
        }

        public virtual void OnDespawn()
        {
            IsSpawned = false;
            gameObject.SetActive(GameConfig.Pool.Dormant);
        }

        public void ReleaseToPool()
        {
            if (prefabPoolKey == GameConfig.Entity.EmptyPrefabPoolKey)
            {
                if (gameObject.activeSelf) OnDespawn();
                return;
            }

            if (!IsSpawned) return;
            if (Managers.PoolManager.TryGetInstance(out Managers.PoolManager poolManager))
            {
                poolManager.Despawn(this);
            }
            else
            {
                OnDespawn();
            }
        }
    }
}
