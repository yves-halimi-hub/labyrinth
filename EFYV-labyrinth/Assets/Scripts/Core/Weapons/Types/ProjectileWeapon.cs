using UnityEngine;
using EFYV.Core.Weapons.Types;
using EFYV.Core.Entities;
using EFYVBackend.Core.Collections;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Implementations
{
    // The base linear projectile type
    public abstract class ProjectileWeapon : Weapon
    {
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
        }

        public override void Fire()
        {
            // Simple logic: shoot towards the closest enemy
            var activeEnemies = Enemy.ActiveEnemies;
            if (activeEnemies.Count == GameConfig.Runtime.EmptyCollectionCount) return;

            // The packed list supplies a deterministic fallback target.
            var target = activeEnemies[GameConfig.Runtime.FirstIndex];

            Vector3 myPos = transform.position;
            Vector3 direction = (target.entityTransform.position - myPos).normalized;

            // Pull a projectile from the fast C-optimized Object Pool
            var projObj = Managers.PoolManager.Instance.SpawnByKey(projectilePrefabKey, myPos, Quaternion.identity);
            if (projObj != null)
            {
                var proj = projObj as Projectile;
                if (proj != null)
                {
                    proj.Initialize(direction, BaseDamage, projectileSpeed, basePierceCount);
                }
            }
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
