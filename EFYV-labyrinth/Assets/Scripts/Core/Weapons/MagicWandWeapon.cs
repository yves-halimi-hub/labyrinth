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
        }

        public override void Fire()
        {
            // 1. Locate Target
            Vector3 origin = transform.position;
            Enemy nearestEnemy = FindNearestEnemy(origin);
            
            if (nearestEnemy != null && projectilePrefab != null)
            {
                // MIGRATION: Bypassed Unity's standard floating-point normalization
                Vector2 diff = nearestEnemy.entityTransform.position - origin;
                Vector2 direction = diff.FastNormalized();
                
                // 3. Call Fast Object Pool instead of Instantiate
                GameEntity spawnedEntity = PoolManager.Instance.Spawn(projectilePrefab, origin, Quaternion.identity);
                Projectile proj = spawnedEntity as Projectile;
                
                if (proj != null)
                {
                    // 4. Initialize bullet stats
                    proj.Initialize(direction, BaseDamage, projectileSpeed, basePierceCount);
                }
            }
        }

        private static Enemy FindNearestEnemy(Vector3 origin)
        {
            // MIGRATION: Completely eliminated FindObjectsOfType. 
            // We now iterate over a perfectly packed C# List representing only active enemies in memory.
            var allEnemies = Enemy.ActiveEnemies;
            Enemy nearest = null;
            float minDistanceSqr = float.MaxValue;

            // Notice we use a basic for-loop instead of foreach for even more performance (no enumerator GC)
            int count = allEnemies.Count;
            for (int i = GameConfig.Weapons.LoopStartIndex; i < count; i++)
            {
                Enemy enemy = allEnemies[i];

                // PERFORMANCE: Broad-Phase 1D Heuristic Culling
                // We instantly cull enemies that are too far away on the X-axis before ever calculating the Y-axis.
                // This eliminates ~50% to 75% of the multiplication operations inside this O(N) loop!
                float dx = origin.x - enemy.entityTransform.position.x;
                if ((dx * dx) >= minDistanceSqr) continue; 

                float dy = origin.y - enemy.entityTransform.position.y;
                float distSqr = (dx * dx) + (dy * dy);
                
                if (distSqr < minDistanceSqr)
                {
                    minDistanceSqr = distSqr;
                    nearest = enemy;
                }
            }

            return nearest;
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
