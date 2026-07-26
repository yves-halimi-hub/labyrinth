using System;
using UnityEngine;
using EFYV.Core.Compute;
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
                Enemy nearest = RuntimeGameplayCompute.FindNearestEnemy(origin);
                if (nearest != null)
                {
                    targetPosition = nearest.entityTransform.position;
                    return true;
                }
            }

            targetPosition = origin;
            return false;
        }

        // Faction-aware planar radius damage. Player-owned weapons hand one
        // point query to the native Runtime Kernel, then mutate the returned
        // Unity enemies in the former descending packed-list order.
        protected void DamageTargetsInRadius(Vector3 center, float radius, float damage)
        {
            float effectiveRadius = AbsoluteRadius(radius);
            if (OwnerFaction == Faction.Enemy)
            {
                PlayerController player = PlayerController.Instance;
                if (player == null || player.IsDead) return;
                float squaredRadius = effectiveRadius * effectiveRadius;
                if (player.entityTransform.position.FastSqrDistance(center) <= squaredRadius)
                {
                    player.TakeDamage(damage);
                }
            }
            else
            {
                RuntimeGameplayCompute.QueryEnemyRadius(center, effectiveRadius);
                DamageEnemyQuery(GameConfig.Runtime.FirstIndex, damage);
            }
        }

        // Drop, splash, and orbital weapons submit every center in one native
        // query batch. Domain mutations stay here in C# after native returns.
        protected void DamageTargetsInRadiusBatch(
            ReadOnlySpan<Vector3> centers,
            float radius,
            float damage)
        {
            if (centers.Length == GameConfig.Runtime.EmptyCollectionCount)
            {
                return;
            }

            float effectiveRadius = AbsoluteRadius(radius);
            if (OwnerFaction == Faction.Enemy)
            {
                PlayerController player = PlayerController.Instance;
                if (player == null || player.IsDead) return;

                float squaredRadius = effectiveRadius * effectiveRadius;
                for (int queryIndex = GameConfig.Runtime.FirstIndex;
                    queryIndex < centers.Length && !player.IsDead;
                    queryIndex++)
                {
                    if (player.entityTransform.position.FastSqrDistance(centers[queryIndex]) <=
                        squaredRadius)
                    {
                        player.TakeDamage(damage);
                    }
                }
                return;
            }

            RuntimeGameplayCompute.QueryEnemyRadii(centers, effectiveRadius);
            for (int queryIndex = GameConfig.Runtime.FirstIndex;
                queryIndex < centers.Length;
                queryIndex++)
            {
                DamageEnemyQuery(queryIndex, damage);
            }
        }

        private static void DamageEnemyQuery(int queryIndex, float damage)
        {
            int start = RuntimeGameplayCompute.QueryHitStart(queryIndex);
            for (int hitIndex = RuntimeGameplayCompute.QueryHitEnd(queryIndex) - 1;
                hitIndex >= start;
                hitIndex--)
            {
                Enemy enemy = RuntimeGameplayCompute.EnemyAtHit(hitIndex);
                // A prior center in the same native snapshot may already have
                // killed and swap-removed this enemy.
                if (enemy.IsSpawned)
                {
                    enemy.TakeDamage(damage);
                }
            }
        }

        private static float AbsoluteRadius(float radius)
        {
            return radius < GameConfig.Runtime.UnitIntervalMin ? -radius : radius;
        }
    }
}
