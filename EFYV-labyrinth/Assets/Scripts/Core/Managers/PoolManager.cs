using System.Collections.Generic;
using UnityEngine;
using EFYV.Core.Entities;
using EFYVBackend.Core.Collections;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class PoolManager : EFYV.Core.Utils.Singleton<PoolManager>
    {
        // Pool keys are assigned per prefab REFERENCE, not from engine instance
        // ids: Unity 6.6 makes EntityId non-convertible to int, and pool identity
        // only ever needs to be stable within the session. Static so keys survive
        // manager re-creation (FastPoolRegistry state is process-wide too); a
        // domain reload clears both together.
        private static readonly Dictionary<UnityEngine.Object, int> prefabPoolKeys =
            new Dictionary<UnityEngine.Object, int>();
        private static int nextPrefabPoolKey = GameConfig.Pool.FirstPrefabPoolKey;

        public static int GetPoolKey(UnityEngine.Object prefab)
        {
            if (prefab == null) return GameConfig.Entity.EmptyPrefabPoolKey;
            if (!prefabPoolKeys.TryGetValue(prefab, out int key))
            {
                key = nextPrefabPoolKey++;
                prefabPoolKeys[prefab] = key;
            }
            return key;
        }

        private readonly HashSet<int> registeredEntityPoolKeys = new HashSet<int>();
        private readonly HashSet<int> registeredGameObjectPoolKeys = new HashSet<int>();
        private readonly List<ScheduledDespawn> scheduledDespawns =
            new List<ScheduledDespawn>(GameConfig.Pool.DefaultPoolCapacity);

        // #24: tracks every GameObject currently rented from a pool, mapped to the
        // key it was rented under. This makes DespawnGameObject idempotent (a
        // second despawn of the same object is a no-op, mirroring the entity-path
        // IsSpawned guard) and lets the pool key be validated BEFORE the object is
        // deactivated (a wrong key used to deactivate the object and then fail to
        // pool it, leaking it inactive).
        private readonly Dictionary<GameObject, int> spawnedGameObjectKeys =
            new Dictionary<GameObject, int>();

        private struct ScheduledDespawn
        {
            public GameObject GameObject;
            public int PoolKey;
            public float RemainingDelay;
        }

        public GameEntity Spawn(GameEntity prefab, Vector3 position, Quaternion rotation)
        {
            return Spawn(prefab, position, rotation, null);
        }

        // Item #4: spawn with a hook that runs on the rented instance AFTER its
        // transform is placed but BEFORE OnSpawn. The data-to-prefab factory
        // binds the imported asset here so OnSpawn sees the loaded data - props
        // register for animation only when they already carry frames, and enemy
        // spawn-stat scaling reads the authored stats (the same order the scene
        // bootstrap uses: LoadData, then activate).
        public GameEntity Spawn(
            GameEntity prefab,
            Vector3 position,
            Quaternion rotation,
            System.Action<GameEntity> configureBeforeSpawn)
        {
            int poolKey = GetPoolKey(prefab.gameObject);
            EnsureEntityPoolRegistered(prefab, poolKey);

            return SpawnByKey(poolKey, position, rotation, configureBeforeSpawn);
        }

        public void Prewarm(GameEntity prefab, int count = GameConfig.Pool.DefaultPoolCapacity)
        {
            if (prefab == null) return;

            int poolKey = GetPoolKey(prefab.gameObject);
            EnsureEntityPoolRegistered(prefab, poolKey);
            FastPoolRegistry<GameEntity>.Prewarm(poolKey, count);
        }

        // #32: static prewarm hooks for callers (weapon Awake paths et al.) that
        // must not care whether the manager exists yet. Returns false (no-op)
        // without an instance.
        public static bool TryPrewarm(GameEntity prefab, int count = GameConfig.Pool.DefaultPoolCapacity)
        {
            if (!TryGetInstance(out PoolManager manager)) return false;
            manager.Prewarm(prefab, count);
            return true;
        }

        public static bool TryPrewarmGameObject(GameObject prefab, int count = GameConfig.Pool.DefaultPoolCapacity)
        {
            if (!TryGetInstance(out PoolManager manager)) return false;
            manager.PrewarmGameObject(prefab, count);
            return true;
        }

        private void EnsureEntityPoolRegistered(GameEntity prefab, int poolKey)
        {
            if (registeredEntityPoolKeys.Add(poolKey)) RegisterEntityPool(prefab, poolKey);
        }

        private void RegisterEntityPool(GameEntity prefab, int poolKey)
        {
            FastPoolRegistry<GameEntity>.RegisterPool(poolKey, GameConfig.Pool.DefaultPoolCapacity, () =>
            {
                // Pool clones must never register as scene-placed entities (#25).
                GameEntity.BeginPooledInstantiation();
                try
                {
                    GameEntity newEntity = Instantiate(prefab);
                    newEntity.prefabPoolKey = poolKey; // Mark the entity so it knows which queue to return to
                    newEntity.transform.SetParent(transform); // Keep the hierarchy clean
                    newEntity.gameObject.SetActive(GameConfig.Pool.Dormant); // Dormant initially
                    return newEntity;
                }
                finally
                {
                    GameEntity.EndPooledInstantiation();
                }
            });
        }

        public GameEntity SpawnByKey(int poolKey, Vector3 position, Quaternion rotation)
        {
            return SpawnByKey(poolKey, position, rotation, null);
        }

        public GameEntity SpawnByKey(
            int poolKey,
            Vector3 position,
            Quaternion rotation,
            System.Action<GameEntity> configureBeforeSpawn)
        {
            // Rent from the allocation-free FastPoolRegistry
            GameEntity spawnedEntity = FastPoolRegistry<GameEntity>.Rent(poolKey);

            if (spawnedEntity == null)
            {
                return null; // Strict memory bound hit. Pool is empty. Do not spawn.
            }

            // Set transform data
            Transform spawnedTransform = spawnedEntity.transform;
            spawnedTransform.position = position;
            spawnedTransform.rotation = rotation;

            // Item #4: bind imported data before activation (see Spawn overload).
            configureBeforeSpawn?.Invoke(spawnedEntity);

            // Trigger the IPoolable interface method to wake the object up
            spawnedEntity.OnSpawn();

            return spawnedEntity;
        }

        public void Despawn(GameEntity entity)
        {
            if (entity == null || !entity.IsSpawned) return;
            entity.OnDespawn();

            // Return it to the static FastPool array instantly
            FastPoolRegistry<GameEntity>.Return(entity.prefabPoolKey, entity);
        }

        public GameObject SpawnGameObject(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            int poolKey = GetPoolKey(prefab);
            EnsureGameObjectPoolRegistered(prefab, poolKey);

            GameObject spawnedObj = FastPoolRegistry<GameObject>.Rent(poolKey);
            if (spawnedObj == null) return null;

            CancelScheduledDespawn(spawnedObj);
            spawnedGameObjectKeys[spawnedObj] = poolKey;
            spawnedObj.transform.position = position;
            spawnedObj.transform.rotation = rotation;
            spawnedObj.SetActive(GameConfig.Pool.Active);

            return spawnedObj;
        }

        public void PrewarmGameObject(GameObject prefab, int count = GameConfig.Pool.DefaultPoolCapacity)
        {
            if (prefab == null) return;

            int poolKey = GetPoolKey(prefab);
            EnsureGameObjectPoolRegistered(prefab, poolKey);
            FastPoolRegistry<GameObject>.Prewarm(poolKey, count);
        }

        private void EnsureGameObjectPoolRegistered(GameObject prefab, int poolKey)
        {
            if (registeredGameObjectPoolKeys.Add(poolKey)) RegisterGameObjectPool(prefab, poolKey);
        }

        private void RegisterGameObjectPool(GameObject prefab, int poolKey)
        {
            FastPoolRegistry<GameObject>.RegisterPool(poolKey, GameConfig.Pool.DefaultPoolCapacity, () =>
            {
                // A pooled prefab may carry GameEntity components; keep them out of
                // the scene-placed registry (#25).
                GameEntity.BeginPooledInstantiation();
                try
                {
                    GameObject newObj = Instantiate(prefab);
                    newObj.transform.SetParent(transform);
                    newObj.SetActive(GameConfig.Pool.Dormant);
                    return newObj;
                }
                finally
                {
                    GameEntity.EndPooledInstantiation();
                }
            });
        }

        public void DespawnGameObject(GameObject obj, int poolKey, float delay = GameConfig.Pool.ImmediateDespawnDelay)
        {
            if (obj == null) return;

            // #24: idempotent despawn - an object that is not currently rented
            // (double despawn, or never spawned through this manager) is ignored.
            if (!spawnedGameObjectKeys.TryGetValue(obj, out int rentedKey)) return;

            // #24: the key is validated BEFORE any deactivation. A mismatched key
            // now throws with the object still active and rented instead of
            // deactivating it without pooling it.
            if (rentedKey != poolKey)
            {
                throw new System.ArgumentException(null, nameof(poolKey));
            }

            CancelScheduledDespawn(obj);

            if (delay > GameConfig.Pool.ImmediateDespawnDelay)
            {
                scheduledDespawns.Add(new ScheduledDespawn
                {
                    GameObject = obj,
                    PoolKey = poolKey,
                    RemainingDelay = delay
                });
            }
            else
            {
                ReturnGameObjectToPool(obj, poolKey);
            }
        }

        private void ReturnGameObjectToPool(GameObject obj, int poolKey)
        {
            spawnedGameObjectKeys.Remove(obj);
            obj.SetActive(GameConfig.Pool.Dormant);
            FastPoolRegistry<GameObject>.Return(poolKey, obj);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int i = scheduledDespawns.Count - 1; i >= 0; i--)
            {
                ScheduledDespawn scheduled = scheduledDespawns[i];
                scheduled.RemainingDelay -= deltaTime;
                if (scheduled.RemainingDelay > GameConfig.Pool.ImmediateDespawnDelay)
                {
                    scheduledDespawns[i] = scheduled;
                    continue;
                }

                if (scheduled.GameObject != null)
                {
                    ReturnGameObjectToPool(scheduled.GameObject, scheduled.PoolKey);
                }

                int lastIndex = scheduledDespawns.Count - 1;
                scheduledDespawns[i] = scheduledDespawns[lastIndex];
                scheduledDespawns.RemoveAt(lastIndex);
            }
        }

        private void CancelScheduledDespawn(GameObject obj)
        {
            for (int i = scheduledDespawns.Count - 1; i >= 0; i--)
            {
                if (scheduledDespawns[i].GameObject != obj) continue;

                int lastIndex = scheduledDespawns.Count - 1;
                scheduledDespawns[i] = scheduledDespawns[lastIndex];
                scheduledDespawns.RemoveAt(lastIndex);
            }
        }

        protected override void OnDestroy()
        {
            bool ownsRegistries = IsSingletonInstance;
            base.OnDestroy();
            if (!ownsRegistries) return;

            scheduledDespawns.Clear();
            registeredEntityPoolKeys.Clear();
            registeredGameObjectPoolKeys.Clear();
            spawnedGameObjectKeys.Clear();
            FastPoolRegistry<GameEntity>.Clear();
            FastPoolRegistry<GameObject>.Clear();
            Enemy.ActiveEnemies.Clear();
            Projectile.ActiveProjectiles.Clear();
            EFYV.Core.Entities.Environment.PropEntity.ActiveAnimatedProps.Clear();
            GameEntity.ClearSceneRegistration();
        }
    }
}
