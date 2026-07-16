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
        private readonly HashSet<int> registeredEntityPoolKeys = new HashSet<int>();
        private readonly HashSet<int> registeredGameObjectPoolKeys = new HashSet<int>();
        private readonly List<ScheduledDespawn> scheduledDespawns =
            new List<ScheduledDespawn>(GameConfig.Pool.DefaultPoolCapacity);

        private struct ScheduledDespawn
        {
            public GameObject GameObject;
            public int PoolKey;
            public float RemainingDelay;
        }

        public GameEntity Spawn(GameEntity prefab, Vector3 position, Quaternion rotation)
        {
            int poolKey = prefab.gameObject.GetInstanceID();
            EnsureEntityPoolRegistered(prefab, poolKey);

            return SpawnByKey(poolKey, position, rotation);
        }

        public void Prewarm(GameEntity prefab, int count = GameConfig.Pool.DefaultPoolCapacity)
        {
            if (prefab == null) return;

            int poolKey = prefab.gameObject.GetInstanceID();
            EnsureEntityPoolRegistered(prefab, poolKey);
            FastPoolRegistry<GameEntity>.Prewarm(poolKey, count);
        }

        private void EnsureEntityPoolRegistered(GameEntity prefab, int poolKey)
        {
            if (registeredEntityPoolKeys.Add(poolKey)) RegisterEntityPool(prefab, poolKey);
        }

        private void RegisterEntityPool(GameEntity prefab, int poolKey)
        {
            FastPoolRegistry<GameEntity>.RegisterPool(poolKey, GameConfig.Pool.DefaultPoolCapacity, () =>
            {
                GameEntity newEntity = Instantiate(prefab);
                newEntity.prefabPoolKey = poolKey; // Mark the entity so it knows which queue to return to
                newEntity.transform.SetParent(transform); // Keep the hierarchy clean
                newEntity.gameObject.SetActive(GameConfig.Pool.Dormant); // Dormant initially
                return newEntity;
            });
        }

        public GameEntity SpawnByKey(int poolKey, Vector3 position, Quaternion rotation)
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
            int poolKey = prefab.GetInstanceID();
            EnsureGameObjectPoolRegistered(prefab, poolKey);

            GameObject spawnedObj = FastPoolRegistry<GameObject>.Rent(poolKey);
            if (spawnedObj == null) return null;

            CancelScheduledDespawn(spawnedObj);
            spawnedObj.transform.position = position;
            spawnedObj.transform.rotation = rotation;
            spawnedObj.SetActive(GameConfig.Pool.Active);

            return spawnedObj;
        }

        public void PrewarmGameObject(GameObject prefab, int count = GameConfig.Pool.DefaultPoolCapacity)
        {
            if (prefab == null) return;

            int poolKey = prefab.GetInstanceID();
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
                GameObject newObj = Instantiate(prefab);
                newObj.transform.SetParent(transform);
                newObj.SetActive(GameConfig.Pool.Dormant);
                return newObj;
            });
        }

        public void DespawnGameObject(GameObject obj, int poolKey, float delay = GameConfig.Pool.ImmediateDespawnDelay)
        {
            if (obj == null) return;
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
                obj.SetActive(GameConfig.Pool.Dormant);
                FastPoolRegistry<GameObject>.Return(poolKey, obj);
            }
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
                    scheduled.GameObject.SetActive(GameConfig.Pool.Dormant);
                    FastPoolRegistry<GameObject>.Return(scheduled.PoolKey, scheduled.GameObject);
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
            FastPoolRegistry<GameEntity>.Clear();
            FastPoolRegistry<GameObject>.Clear();
            Enemy.ActiveEnemies.Clear();
            Projectile.ActiveProjectiles.Clear();
            EFYV.Core.Entities.Environment.PropEntity.ActiveAnimatedProps.Clear();
        }
    }
}
