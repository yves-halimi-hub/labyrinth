using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons
{
    public abstract class Weapon : MonoBehaviour
    {
        protected EFYVBackend.Core.Models.WeaponData Data = new EFYVBackend.Core.Models.WeaponData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public float CooldownTime
        {
            get => Data.CooldownTime;
            protected set => Data.CooldownTime = value;
        }
        public float BaseDamage
        {
            get => Data.BaseDamage;
            protected set => Data.BaseDamage = value;
        }
        public int Level
        {
            get => Data.Level;
            protected set => Data.Level = value;
        }

        // Faction of whoever holds this weapon. WeaponController stamps it when the
        // weapon is equipped; free-standing weapons default to the player's side
        // (Faction.Player is the enum zero value).
        public Faction OwnerFaction { get; set; }

        // The deltaTime of the tick that triggered the current Fire call. Time-scaled
        // effects (orbital contact damage, melee knockback) must use this instead of
        // the global Time.deltaTime so custom-dt drivers stay in sync.
        protected float TickDeltaTime { get; set; }

        public System.Collections.Generic.List<WeaponEvolution> AvailableEvolutions = new System.Collections.Generic.List<WeaponEvolution>();

        protected float currentCooldown
        {
            get => Data.CurrentCooldown;
            set => Data.CurrentCooldown = value;
        }

        protected virtual void Awake()
        {
            Level = GameConfig.Weapons.Inventory.InitialLevel;
        }

        public virtual void Tick(float deltaTime)
        {
            TickDeltaTime = deltaTime;
            currentCooldown -= deltaTime;
            if (currentCooldown <= GameConfig.Weapons.CooldownReadyThreshold)
            {
                Fire();
                currentCooldown = CooldownTime;
            }
        }

        public abstract void Fire();

        public virtual void Upgrade()
        {
            Level += GameConfig.Weapons.Inventory.LevelIncrement;
            // Specific weapon subclasses will implement what level up means
            // (e.g., more damage, lower cooldown, more projectiles)
        }

        // Faction-aware target resolution for aimed weapons: player-owned weapons aim
        // at the nearest packed-list enemy, enemy-owned weapons aim at the player.
        // Returns false when no living opposing target exists.
        protected bool TryGetTargetPosition(Vector3 origin, out Vector3 targetPosition)
        {
            if (OwnerFaction == Faction.Enemy)
            {
                PlayerController player = PlayerController.Instance;
                if (player != null && !player.IsDead)
                {
                    targetPosition = player.entityTransform.position;
                    return true;
                }
            }
            else
            {
                Enemy nearest = FindNearestEnemy(origin);
                if (nearest != null)
                {
                    targetPosition = nearest.entityTransform.position;
                    return true;
                }
            }

            targetPosition = origin;
            return false;
        }

        // Faction-aware planar radius damage: player-owned weapons sweep the packed
        // enemy list, enemy-owned weapons test only the player singleton. No
        // allocations, no LINQ - safe for per-tick hot paths.
        protected void DamageTargetsInRadius(Vector3 center, float squaredRadius, float damage)
        {
            if (OwnerFaction == Faction.Enemy)
            {
                PlayerController player = PlayerController.Instance;
                if (player == null || player.IsDead) return;
                if (player.entityTransform.position.FastSqrDistance(center) <= squaredRadius)
                {
                    player.TakeDamage(damage);
                }
            }
            else
            {
                Enemy.ApplyDamageInRadius(center, squaredRadius, damage);
            }
        }

        protected static Enemy FindNearestEnemy(Vector3 origin)
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
    }
}
