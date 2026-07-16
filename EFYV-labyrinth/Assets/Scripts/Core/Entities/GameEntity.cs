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
        
        // Tracks which pool this object belongs to
        public int prefabPoolKey 
        { 
            get => Data.PrefabPoolKey; 
            set => Data.PrefabPoolKey = value; 
        }

        protected virtual void Awake()
        {
            Initialize();
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
