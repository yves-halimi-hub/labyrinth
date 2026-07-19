using UnityEngine;
using EFYV.Core.Entities;
using EFYV.Core.Managers;
using EFYV.Core.Data;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons
{
    public class MagicWandWeapon : Weapon
    {
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

        protected override void Awake()
        {
            base.Awake();
            projectileSpeed = GameConfig.Weapons.MagicWand.DefaultSpeed;
            basePierceCount = GameConfig.Weapons.MagicWand.DefaultPierce;
            CooldownTime = GameConfig.Weapons.MagicWand.DefaultCooldown; // Fires once per second at start
            BaseDamage = GameConfig.Weapons.MagicWand.DefaultDamage;
            Level = GameConfig.Weapons.MagicWand.DefaultLevel;

            // #32: fill the projectile pool up-front so the first volley never
            // hitches on Instantiate. No-op without a prefab or PoolManager;
            // populate-up-to-target keeps repeated grants idempotent.
            PoolManager.TryPrewarm(projectilePrefab, GameConfig.Pool.ProjectilePrewarmCount);
        }

        public override void Fire()
        {
            // 1. Locate Target (faction-aware: player wands aim at the nearest packed-list
            // enemy, enemy-held wands aim at the player)
            Vector3 origin = transform.position;
            if (!TryGetTargetPosition(origin, out Vector3 targetPosition)) return;

            if (projectilePrefab != null)
            {
                PoolManager pool = PoolManager.Instance;
                if (pool == null) return;

                // MIGRATION: Bypassed Unity's standard floating-point normalization
                Vector2 diff = targetPosition - origin;
                Vector2 direction = diff.FastNormalized();

                // 3. Call Fast Object Pool instead of Instantiate
                GameEntity spawnedEntity = pool.Spawn(projectilePrefab, origin, Quaternion.identity);
                Projectile proj = spawnedEntity as Projectile;

                if (proj != null)
                {
                    // 4. Initialize bullet stats, carrying the owner's faction
                    proj.Initialize(direction, BaseDamage, projectileSpeed, basePierceCount, OwnerFaction);
                }
            }
        }

        public override void Upgrade()
        {
            base.Upgrade();
            if (Level % GameConfig.Weapons.MagicWand.PierceUpgradeInterval == GameConfig.Weapons.PierceUpgradeRemainder)
            {
                basePierceCount += GameConfig.Weapons.MagicWand.PierceUpgradeIncrement;
            }

            BaseDamage += GameConfig.Weapons.MagicWand.UpgradeDamage;
            
            // PERFORMANCE: FastMax wrapper handles the boundary check mathematically without C# branching
            CooldownTime = EFYVBackend.Core.Math.FastMath.FastMax(GameConfig.Weapons.MagicWand.MinCooldown, CooldownTime - GameConfig.Weapons.MagicWand.UpgradeCooldownReduction); // Shoot slightly faster, capped at 10 shots/sec
        }
    }
}
