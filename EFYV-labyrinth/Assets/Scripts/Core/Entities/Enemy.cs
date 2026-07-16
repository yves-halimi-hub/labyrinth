using UnityEngine;
using EFYV.Core.Interfaces;
using EFYV.Core.Data;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public abstract class Enemy : LivingEntity, EFYVBackend.Core.Collections.IFastListTrackable
    {
        public float DamageToPlayer { get => Data.DamageToPlayer; protected set => Data.DamageToPlayer = value; }
        public float ExperienceValue { get => Data.ExperienceValue; protected set => Data.ExperienceValue = value; }
        public Transform TargetPlayer { get; protected set; }
        protected virtual int MaxWeapons => GameConfig.Runtime.EmptyCollectionCount;
        protected float SpawnHealthMultiplier { get; private set; } = GameConfig.AI.DefaultMultiplier;
        protected float SpawnSpeedMultiplier { get; private set; } = GameConfig.AI.DefaultMultiplier;

        private float fallbackMaxHealth;
        private float fallbackBaseSpeed;

        public static readonly EFYVBackend.Core.Collections.FastSwapList<Enemy> ActiveEnemies = new EFYVBackend.Core.Collections.FastSwapList<Enemy>();
        
        // IFastListTrackable implementation
        public int ActiveListIndex { get => Data.ActiveListIndex; set => Data.ActiveListIndex = value; }

        public override void Initialize()
        {
            base.Initialize();
            fallbackMaxHealth = MaxHealth;
            fallbackBaseSpeed = BaseSpeed > GameConfig.Entity.PositiveAmountThreshold
                ? BaseSpeed
                : GameConfig.Enemy.BaseSpeedFallback;
            ActiveListIndex = GameConfig.EnvironmentData.UnregisteredListIndex;
            // MIGRATION: Eliminated expensive FindObjectOfType. Target is instantly cached via Singleton.
            if (PlayerController.Instance != null)
            {
                TargetPlayer = PlayerController.Instance.entityTransform;
            }
        }

        protected override void ApplyAdditionalSchemaData(EFYVBackend.Core.Data.FastSchemaBlock block)
        {
            DamageToPlayer = block.GetFloat((int)EFYVBackend.Core.Data.AssetSchema.DamageToPlayer);
            ExperienceValue = block.GetFloat((int)EFYVBackend.Core.Data.AssetSchema.ExperienceValue);
        }

        public override void RefreshDataFromAsset()
        {
            base.RefreshDataFromAsset();
            if (!IsSpawned || !HasAuthoredStats) return;

            MaxHealth *= SpawnHealthMultiplier;
            CurrentHealth *= SpawnHealthMultiplier;
            BaseSpeed *= SpawnSpeedMultiplier;
            NotifyHealthChanged();
        }

        public static void ApplyDamageInRadius(Vector3 center, float squaredRadius, float damage)
        {
            // Damage can despawn an enemy and mutate the packed list. Descending iteration
            // keeps swap-removal safe because the swapped tail item was already visited.
            for (int i = ActiveEnemies.Count - 1; i >= 0; i--)
            {
                Enemy enemy = ActiveEnemies[i];
                if (enemy.entityTransform.position.FastSqrDistance(center) <= squaredRadius)
                {
                    enemy.TakeDamage(damage);
                }
            }
        }

        public override void OnSpawn()
        {
            base.OnSpawn();

            float healthMultiplier = GameConfig.AI.DefaultMultiplier;
            float speedMultiplier = GameConfig.AI.DefaultMultiplier;
            if (Managers.AIDirector.TryGetInstance(out Managers.AIDirector director))
            {
                healthMultiplier = director.GetEnemyHealthMultiplier();
                speedMultiplier = director.GetEnemySpeedMultiplier();
            }
            SpawnHealthMultiplier = healthMultiplier;
            SpawnSpeedMultiplier = speedMultiplier;

            float sourceMaxHealth = HasAuthoredStats ? AuthoredMaxHealth : fallbackMaxHealth;
            float sourceBaseSpeed = HasAuthoredStats ? AuthoredBaseSpeed : fallbackBaseSpeed;
            CalculateSpawnStats(
                sourceMaxHealth,
                sourceBaseSpeed,
                healthMultiplier,
                speedMultiplier,
                out float scaledMaxHealth,
                out float scaledBaseSpeed);
            MaxHealth = scaledMaxHealth;
            CurrentHealth = scaledMaxHealth;
            BaseSpeed = scaledBaseSpeed;

            if (TargetPlayer == null && PlayerController.Instance != null)
            {
                TargetPlayer = PlayerController.Instance.entityTransform;
            }

            // MIGRATION: Delegating fast insertion to backend FastSwapList
            ActiveEnemies.Add(this);

            if (WeaponSystem != null)
            {
                WeaponSystem.Initialize(MaxWeapons);
            }
        }

        internal static void CalculateSpawnStats(
            float sourceMaxHealth,
            float sourceBaseSpeed,
            float healthMultiplier,
            float speedMultiplier,
            out float scaledMaxHealth,
            out float scaledBaseSpeed)
        {
            scaledMaxHealth = sourceMaxHealth * healthMultiplier;
            scaledBaseSpeed = sourceBaseSpeed * speedMultiplier;
        }

        public override void OnDespawn()
        {
            // PERFORMANCE: Delegating O(1) Swap-and-Pop to C-optimized backend
            ActiveEnemies.Remove(this);

            base.OnDespawn();
        }

        // PERFORMANCE: Bypassing Unity's Native C++ Update Reflection
        // We delete the magic 'Update' method so Unity ignores this object.
        // Instead, a central C# Manager calls this Tick method, saving thousands of Native-to-Managed overhead calls.
        public virtual void Tick(float deltaTime)
        {
            if (TargetPlayer != null)
            {
                Move(deltaTime);
            }
            if (WeaponSystem != null)
            {
                WeaponSystem.TickWeapons(deltaTime);
            }
        }

        protected override void Move(float deltaTime)
        {
            ChaseTarget(deltaTime);
        }

        protected virtual void ChaseTarget(float deltaTime)
        {
            // MIGRATION: Bypassed Unity's standard floating-point normalization
            Vector3 diff = TargetPlayer.position - entityTransform.position;
            Vector3 normDiff = diff.FastNormalized();
            
            // MIGRATION: Bypassed Unity's Transform.Translate wrapper
            entityTransform.ApplyFastTranslation(normDiff.x, normDiff.y, BaseSpeed, deltaTime);
        }

        public virtual void Attack(IDamageable target)
        {
            target.TakeDamage(DamageToPlayer);
        }

        // Handle collision with player to deal damage
        protected virtual void OnTriggerStay2D(Collider2D collision)
        {
            // PERFORMANCE: Double GetComponent Removed.
            // GetComponent forces a heavy C++ interop call which is devastating inside a Physics Stay loop.
            // Instead, we just do a lightning-fast memory reference check against the Player Singleton.
            if (PlayerController.Instance != null && collision.gameObject == PlayerController.Instance.gameObject)
            {
                // In a full implementation, you'd have an attack cooldown or rely on Player's i-frames
                Attack(PlayerController.Instance);
            }
        }
        
        public override void Die()
        {
            if (PlayerController.Instance != null)
                PlayerController.Instance.GainExperience(ExperienceValue);
                
            if (Managers.DropManager.Instance != null)
                Managers.DropManager.Instance.DropLoot(this);
                
            base.Die();
        }
    }
}
