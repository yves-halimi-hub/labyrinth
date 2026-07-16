using UnityEngine;
using EFYV.Core.Interfaces;
using EFYV.Core.Data;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public class Projectile : GameEntity, EFYVBackend.Core.Collections.IFastListTrackable
    {
        private EFYVBackend.Core.Models.ProjectileData projectileData =
            new EFYVBackend.Core.Models.ProjectileData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public float Damage { get => projectileData.Damage; private set => projectileData.Damage = value; }
        public int PiercingCount { get => projectileData.PiercingCount; protected set => projectileData.PiercingCount = value; }
        public float Speed { get => projectileData.Speed; protected set => projectileData.Speed = value; }
        public Vector2 Direction 
        { 
            get => new Vector2(projectileData.DirectionX, projectileData.DirectionY); 
            protected set 
            { 
                projectileData.DirectionX = value.x;
                projectileData.DirectionY = value.y;
            } 
        }
        
        public static readonly EFYVBackend.Core.Collections.FastSwapList<Projectile> ActiveProjectiles = new EFYVBackend.Core.Collections.FastSwapList<Projectile>();
        
        // IFastListTrackable implementation
        public int ActiveListIndex { get => projectileData.ActiveListIndex; set => projectileData.ActiveListIndex = value; }

        public override void Initialize()
        {
            base.Initialize();
            ActiveListIndex = GameConfig.EnvironmentData.UnregisteredListIndex;
        }
        
        private int currentPierces 
        {
            get => projectileData.CurrentPierces;
            set => projectileData.CurrentPierces = value;
        }

        private float remainingLifetime
        {
            get => projectileData.RemainingLifetime;
            set => projectileData.RemainingLifetime = value;
        }

        public virtual void Initialize(Vector2 direction, float damage, float speed, int pierceCount)
        {
            Damage = damage;
            Speed = speed;
            PiercingCount = pierceCount;
            Direction = direction.FastNormalized();
            currentPierces = GameConfig.Weapons.InitialPierces;
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            remainingLifetime = GameConfig.Weapons.Projectile.DefaultLifetime;
            // MIGRATION: Delegating fast insertion to backend FastSwapList
            ActiveProjectiles.Add(this);
        }

        public override void OnDespawn()
        {
            // PERFORMANCE: Delegating O(1) Swap-and-Pop to C-optimized backend
            ActiveProjectiles.Remove(this);
            base.OnDespawn();
        }

        // PERFORMANCE: Bypassing Unity's Native C++ Update Reflection
        public virtual void Tick(float deltaTime)
        {
            if (!IsSpawned) return;
            remainingLifetime -= deltaTime;
            if (remainingLifetime <= GameConfig.Weapons.Projectile.LifetimeExpiredThreshold)
            {
                ReleaseToPool();
                return;
            }

            // MIGRATION: Bypassed Unity's Transform.Translate wrapper
            Vector2 direction = Direction;
            entityTransform.ApplyFastTranslation(direction.x, direction.y, Speed, deltaTime);
        }

        public virtual void OnHit(IDamageable target)
        {
            if (!IsSpawned || target == null) return;
            target.TakeDamage(Damage);
            currentPierces += GameConfig.Weapons.Projectile.PierceHitIncrement;

            if (currentPierces >= PiercingCount)
            {
                ReleaseToPool();
            }
        }

        protected virtual void OnTriggerEnter2D(Collider2D collision)
        {
            var damageable = collision.GetComponent<IDamageable>();
            
            // PERFORMANCE: O(1) Type Casting instead of O(N) Component Searching
            // Bypasses the heavy C++ Unity GetComponent string-matching lookup completely
            if (damageable is Enemy)
            {
                OnHit(damageable);
            }
        }
    }
}
