using UnityEngine;
using EFYV.Core.Entities;
using EFYV.Core.Utils;
using EFYVBackend.Core.Collections;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Implementations
{
    // The base linear projectile type
    public abstract class ProjectileWeapon : Weapon
    {
        // Preferred wiring (MagicWandWeapon pattern): a typed prefab reference that
        // registers its own pool through PoolManager.Spawn on first use.
        public Projectile projectilePrefab;

        public float projectileSpeed
        {
            get => Data.ProjectileSpeed;
            set => Data.ProjectileSpeed = value;
        }

        public int basePierceCount
        {
            get => Data.PierceCount;
            set => Data.PierceCount = value;
        }

        // Legacy schema wiring: an already-registered pool key. Only consulted when
        // no typed prefab is assigned.
        public int projectilePrefabKey
        {
            get => Data.ProjectilePrefabKey;
            set => Data.ProjectilePrefabKey = value;
        }

        protected override void Awake()
        {
            base.Awake();
            projectileSpeed = GameConfig.Weapons.Projectile.DefaultSpeed;
            basePierceCount = GameConfig.Weapons.Projectile.DefaultPierce;

            // #32: fill the projectile pool up-front so the first volley never
            // hitches on Instantiate. No-op when the prefab is unassigned (the
            // legacy key path's pool is owned by whoever registered the key) or
            // when no PoolManager exists yet; populate-up-to-target keeps
            // repeated weapon grants idempotent.
            Managers.PoolManager.TryPrewarm(projectilePrefab, GameConfig.Pool.ProjectilePrewarmCount);
        }

        public override void Fire()
        {
            // Faction-aware targeting: player-owned weapons aim at the nearest
            // packed-list enemy, enemy-owned weapons aim at the player.
            Vector3 origin = transform.position;
            if (!TryGetTargetPosition(origin, out Vector3 targetPosition)) return;

            Projectile proj;
            if (projectilePrefab != null)
            {
                Managers.PoolManager pool = Managers.PoolManager.Instance;
                if (pool == null) return;

                // Typed prefab path: the pool key derives from the prefab itself, so
                // the rented entity is a Projectile by construction.
                proj = pool.Spawn(projectilePrefab, origin, Quaternion.identity) as Projectile;
            }
            else
            {
                // Key path: type-check the pooled entry BEFORE activating it so a
                // mis-keyed pool entry is returned unharmed instead of being spawned
                // uninitialized and leaked.
                GameEntity rented = FastPoolRegistry<GameEntity>.Rent(projectilePrefabKey);
                if (rented == null) return;

                proj = rented as Projectile;
                if (proj == null)
                {
                    FastPoolRegistry<GameEntity>.Return(projectilePrefabKey, rented);
                    return;
                }

                Transform projectileTransform = proj.transform;
                projectileTransform.position = origin;
                projectileTransform.rotation = Quaternion.identity;
                proj.OnSpawn();
            }

            if (proj == null) return;

            // MIGRATION: Bypassed Unity's standard floating-point normalization
            Vector2 diff = targetPosition - origin;
            Vector2 direction = diff.FastNormalized();
            proj.Initialize(direction, BaseDamage, projectileSpeed, basePierceCount, OwnerFaction);
        }
    }

    public class GunWeapon : ProjectileWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Projectile.Gun.Damage;
            projectileSpeed = GameConfig.Weapons.Projectile.Gun.Speed;
            basePierceCount = GameConfig.Weapons.Projectile.Gun.Pierce;
            CooldownTime = GameConfig.Weapons.Projectile.Gun.Cooldown;
        }
    }

    public class BowWeapon : ProjectileWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Projectile.Bow.Damage;
            projectileSpeed = GameConfig.Weapons.Projectile.Bow.Speed;
            basePierceCount = GameConfig.Weapons.Projectile.Bow.Pierce;
            CooldownTime = GameConfig.Weapons.Projectile.Bow.Cooldown;
        }
    }
}
