using UnityEngine;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYV.Core.Entities.Environment
{
    // Base class for all world objects that aren't characters or projectiles
    public abstract class PropEntity : GameEntity, EFYVBackend.Core.Collections.IFastListTrackable
    {
        [SerializeField] protected GameAssetData assetData;
        public GameAssetData SourceData => assetData;

        [Header(GameConfig.DataConfig.HeaderPropSettings)]
        [SerializeField] private bool serializedIsBlocking = GameConfig.EnvironmentData.NonBlocking;
        public bool IsBlocking
        {
            get => Data.Block.GetInt((int)EFYVBackend.Core.Data.EntitySchema.IsBlocking) == BackendConfig.Serialization.TrueValue;
            set
            {
                serializedIsBlocking = value;
                Data.Block.SetInt((int)EFYVBackend.Core.Data.EntitySchema.IsBlocking, value ? BackendConfig.Serialization.TrueValue : BackendConfig.Serialization.FalseValue);
            }
        }
        
        [Header(GameConfig.DataConfig.HeaderAnimationSettings)]
        public Sprite[] animationFrames;
        [SerializeField] private float serializedAnimationSpeed = GameConfig.EnvironmentData.DefaultAnimationSpeed;
        public float animationSpeed
        {
            get => Data.Block.GetFloat((int)EFYVBackend.Core.Data.EntitySchema.AnimationSpeed);
            set
            {
                serializedAnimationSpeed = value;
                Data.Block.SetFloat((int)EFYVBackend.Core.Data.EntitySchema.AnimationSpeed, value);
            }
        }
        
        protected float animTimer 
        { 
            get => Data.Block.GetFloat((int)EFYVBackend.Core.Data.EntitySchema.AnimTimer); 
            set => Data.Block.SetFloat((int)EFYVBackend.Core.Data.EntitySchema.AnimTimer, value); 
        }
        protected int currentFrame 
        { 
            get => Data.Block.GetInt((int)EFYVBackend.Core.Data.EntitySchema.CurrentFrame); 
            set => Data.Block.SetInt((int)EFYVBackend.Core.Data.EntitySchema.CurrentFrame, value); 
        }

        // IFastListTrackable implementation
        public int ActiveListIndex { get => Data.Block.GetInt((int)EFYVBackend.Core.Data.EntitySchema.ActiveListIndex); set => Data.Block.SetInt((int)EFYVBackend.Core.Data.EntitySchema.ActiveListIndex, value); }

        public override void Initialize()
        {
            base.Initialize();
            SyncSerializedSettings();
            ApplySourceData();
            animTimer = GameConfig.EnvironmentData.InitialAnimTimer;
            currentFrame = GameConfig.EnvironmentData.InitialCurrentFrame;
            ActiveListIndex = GameConfig.EnvironmentData.UnregisteredListIndex;
        }

        protected virtual void OnValidate()
        {
            SyncSerializedSettings();
        }

        protected virtual void Reset()
        {
            serializedIsBlocking = GameConfig.EnvironmentData.NonBlocking;
            serializedAnimationSpeed = GameConfig.EnvironmentData.DefaultAnimationSpeed;
            SyncSerializedSettings();
        }

        protected virtual void SyncSerializedSettings()
        {
            Data.Block.SetInt(
                (int)EFYVBackend.Core.Data.EntitySchema.IsBlocking,
                serializedIsBlocking ? BackendConfig.Serialization.TrueValue : BackendConfig.Serialization.FalseValue);
            Data.Block.SetFloat((int)EFYVBackend.Core.Data.EntitySchema.AnimationSpeed, serializedAnimationSpeed);
        }

        public void LoadData(GameAssetData data)
        {
            assetData = data;
            ApplySourceData();
        }

        public void RefreshDataFromAsset()
        {
            ApplySourceData();
        }

        private void ApplySourceData()
        {
            if (assetData != null && spriteRenderer != null) spriteRenderer.sprite = assetData.sprite;
        }

        // Global strictly-packed memory array for blazing fast animation updates
        public static readonly EFYVBackend.Core.Collections.FastSwapList<PropEntity> ActiveAnimatedProps = new EFYVBackend.Core.Collections.FastSwapList<PropEntity>();

        public override void OnSpawn()
        {
            base.OnSpawn();
            
            // Randomize starting frame so thousands of trees don't wave in creepy unison
            if (animationFrames != null && animationFrames.Length > GameConfig.Runtime.EmptyCollectionCount)
            {
                currentFrame = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Runtime.FirstIndex, animationFrames.Length);
                animTimer = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.EnvironmentData.InitialAnimTimer, animationSpeed);
                spriteRenderer.sprite = animationFrames[currentFrame];

                ActiveAnimatedProps.Add(this);
            }
        }

        public override void OnDespawn()
        {
            if (animationFrames != null && animationFrames.Length > GameConfig.Runtime.EmptyCollectionCount)
            {
                // PERFORMANCE: Delegating O(1) Swap-and-Pop to C-optimized backend
                ActiveAnimatedProps.Remove(this);
            }
            base.OnDespawn();
        }

        // PERFORMANCE: Bypassing Unity's Native C++ Update() Bridge.
        // Handled entirely by a central C# Loop Manager (O(N) contiguous memory).
        public virtual void TickAnimation(float deltaTime)
        {
            animTimer += deltaTime;
            if (animTimer >= animationSpeed)
            {
                animTimer -= animationSpeed; // Avoid heavy modulo division
                
                currentFrame++;
                if (currentFrame >= animationFrames.Length) currentFrame = GameConfig.EnvironmentData.AnimationLoopStartFrame;
                
                spriteRenderer.sprite = animationFrames[currentFrame];
            }
        }
    }
}
