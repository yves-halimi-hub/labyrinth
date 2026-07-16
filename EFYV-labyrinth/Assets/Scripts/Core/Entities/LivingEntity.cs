using UnityEngine;
using EFYV.Core.Interfaces;
using EFYV.Core.Data;
using System;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public abstract class LivingEntity : GameEntity, IDamageable
    {
        public float MaxHealth { get => Data.MaxHealth; protected set => Data.MaxHealth = value; }
        public float CurrentHealth { get => Data.CurrentHealth; protected set => Data.CurrentHealth = value; }
        public float BaseSpeed { get => Data.BaseSpeed; protected set => Data.BaseSpeed = value; }
        
        public EFYV.Core.Controllers.WeaponController WeaponSystem { get; protected set; }

        public event Action<float> OnHealthChanged;
        
        [SerializeField] protected LivingEntityData entityData;
        [SerializeField] private int hitboxPreviewFrame = GameConfig.Runtime.FirstIndex;
        [SerializeField] private EFYVBackend.Core.Math.FastMath.FacingDirection hitboxPreviewFacing = GameConfig.Entity.InitialFacing;
        public LivingEntityData SourceData => entityData;
        public int HitboxPreviewFrame => hitboxPreviewFrame;
        public EFYVBackend.Core.Math.FastMath.FacingDirection HitboxPreviewFacing => hitboxPreviewFacing;
        protected EFYVBackend.Core.Math.FastMath.FacingDirection currentFacing = GameConfig.Entity.InitialFacing;
        protected bool HasAuthoredStats { get; private set; }
        protected float AuthoredMaxHealth { get; private set; }
        protected float AuthoredBaseSpeed { get; private set; }

        public virtual void LoadData(LivingEntityData data)
        {
            entityData = data;
            ApplySourceData(preserveHealthRatio: false);
        }

        public virtual void RefreshDataFromAsset()
        {
            ApplySourceData(preserveHealthRatio: true);
        }

        public override void Initialize()
        {
            base.Initialize();
            CacheLivingEntityComponents();
            ApplySourceData(preserveHealthRatio: false);
        }

        private void CacheLivingEntityComponents()
        {
            WeaponSystem = GetComponent<EFYV.Core.Controllers.WeaponController>();
        }

        private void ApplySourceData(bool preserveHealthRatio)
        {
            if (entityData == null) return;

            EFYVBackend.Core.Data.FastSchemaBlock block = entityData.GetSchemaBlock();
            ApplySchemaBlock(block, preserveHealthRatio);
            ApplyDirectionalSprite(currentFacing);
        }

        protected void ApplySchemaBlock(EFYVBackend.Core.Data.FastSchemaBlock block, bool preserveHealthRatio)
        {
            float previousMaxHealth = MaxHealth;
            float healthRatio = preserveHealthRatio && previousMaxHealth > GameConfig.Entity.DeathHealthThreshold
                ? Mathf.Clamp01(CurrentHealth / previousMaxHealth)
                : GameConfig.Runtime.UnitIntervalMax;

            AuthoredMaxHealth = block.GetFloat((int)EFYVBackend.Core.Data.AssetSchema.MaxHealth);
            AuthoredBaseSpeed = block.GetFloat((int)EFYVBackend.Core.Data.AssetSchema.BaseSpeed);
            HasAuthoredStats = true;
            MaxHealth = AuthoredMaxHealth;
            BaseSpeed = AuthoredBaseSpeed;
            ApplyAdditionalSchemaData(block);
            CurrentHealth = MaxHealth * healthRatio;
            NotifyHealthChanged();
        }

        protected virtual void ApplyAdditionalSchemaData(EFYVBackend.Core.Data.FastSchemaBlock block)
        {
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            CurrentHealth = MaxHealth;
        }

        public virtual void TakeDamage(float amount)
        {
            CurrentHealth -= amount;
            NotifyHealthChanged();
            if (CurrentHealth <= GameConfig.Entity.DeathHealthThreshold)
            {
                Die();
            }
        }

        public virtual void Heal(float amount)
        {
            if (amount <= GameConfig.Entity.PositiveAmountThreshold) return;
            CurrentHealth = Mathf.Min(CurrentHealth + amount, MaxHealth);
            NotifyHealthChanged();
        }

        protected void NotifyHealthChanged()
        {
            OnHealthChanged?.Invoke(CurrentHealth);
        }

        public virtual void Die()
        {
            ReleaseToPool();
        }

        protected abstract void Move(float deltaTime);

        // PERFORMANCE: C-Optimized Sprite Swapper
        // Uses the FastMath backend to resolve the movement vector and swaps the ScriptableObject reference instantly.
        public void UpdateDirectionalSprite(float moveX, float moveY)
        {
            if (moveX == GameConfig.Entity.StationaryAxisValue && moveY == GameConfig.Entity.StationaryAxisValue) return;
            if (entityData == null || spriteRenderer == null) return;

            var newFacing = EFYVBackend.Core.Math.FastMath.Get4WayDirection(moveX, moveY);
            
            if (newFacing != currentFacing)
            {
                currentFacing = newFacing;
                hitboxPreviewFacing = newFacing;
                ApplyDirectionalSprite(currentFacing);
            }
        }

        private void ApplyDirectionalSprite(EFYVBackend.Core.Math.FastMath.FacingDirection facing)
        {
            if (entityData == null || spriteRenderer == null) return;

            Sprite directionalSprite = null;
            switch (facing)
            {
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Up:
                    directionalSprite = entityData.spriteSheetUp;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Down:
                    directionalSprite = entityData.spriteSheetDown;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Left:
                    directionalSprite = entityData.spriteSheetLeft;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Right:
                    directionalSprite = entityData.spriteSheetRight;
                    break;
            }

            spriteRenderer.sprite = directionalSprite != null ? directionalSprite : entityData.spriteSheet;
        }
    }
}
