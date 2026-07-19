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

        // Which side fired this projectile. Player-owned projectiles damage enemies;
        // enemy-owned projectiles damage the player. Set by Initialize; defaults to
        // the player's side for legacy callers.
        public Faction OwnerFaction { get; private set; }

        // Single-entry memo for the trigger component lookup: repeated triggers from
        // the same collider skip the costly GetComponent interface scan entirely.
        private Collider2D cachedTriggerCollider;
        private IDamageable cachedTriggerDamageable;

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
            Initialize(direction, damage, speed, pierceCount, Faction.Player);
        }

        public virtual void Initialize(Vector2 direction, float damage, float speed, int pierceCount, Faction ownerFaction)
        {
            Damage = damage;
            Speed = speed;
            PiercingCount = pierceCount;
            Direction = direction.FastNormalized();
            currentPierces = GameConfig.Weapons.InitialPierces;
            OwnerFaction = ownerFaction;
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            remainingLifetime = GameConfig.Weapons.Projectile.DefaultLifetime;
            // Pool reuse safety: a rented projectile must never inherit the spent
            // pierce counter (or a stale trigger memo) from its previous flight.
            currentPierces = GameConfig.Weapons.InitialPierces;
            cachedTriggerCollider = null;
            cachedTriggerDamageable = null;
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
            // PERFORMANCE: single-entry memo per projectile. Piercing projectiles that
            // re-trigger the same collider reuse the cached lookup instead of paying
            // the heavy C++ GetComponent interface scan on every trigger.
            IDamageable damageable;
            if (ReferenceEquals(collision, cachedTriggerCollider))
            {
                damageable = cachedTriggerDamageable;
            }
            else
            {
                damageable = collision.GetComponent<IDamageable>();
                cachedTriggerCollider = collision;
                cachedTriggerDamageable = damageable;
            }

            // Faction gate: a projectile only harms the opposing side. Player-owned
            // projectiles hit enemies; enemy-owned projectiles hit the player.
            bool hostileHit = OwnerFaction == Faction.Enemy
                ? damageable is PlayerController
                : damageable is Enemy;
            if (hostileHit)
            {
                OnHit(damageable);
            }
        }
    }
}
