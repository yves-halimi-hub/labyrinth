using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Models
{
    // Replaces single variable definitions in DropManager
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct DropManagerData
    {
        public FastSchemaBlock Block;
        public float DynamicDropMultiplier
        {
            get => Block.GetFloat((int)SystemSchema.DynamicDropMultiplier);
            set => Block.SetFloat((int)SystemSchema.DynamicDropMultiplier, value);
        }
    }

    // Replaces single variable definitions in WeaponController (Inventory)
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct InventoryData
    {
        public FastSchemaBlock Block;
        public int MaxWeapons
        {
            get => Block.GetInt((int)WeaponControllerSchema.MaxWeapons);
            set => Block.SetInt((int)WeaponControllerSchema.MaxWeapons, value);
        }
    }

    // Replaces single variable definitions in PlayerController
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct PlayerData
    {
        public FastSchemaBlock Block;
        public float Experience { get => Block.GetFloat((int)PlayerSchema.Experience); set => Block.SetFloat((int)PlayerSchema.Experience, value); }
        public int Level { get => Block.GetInt((int)PlayerSchema.Level); set => Block.SetInt((int)PlayerSchema.Level, value); }
        public string ActiveToonId 
        { 
            get => _activeToonId; 
            set 
            { 
                _activeToonId = value; 
                Block.SetInt((int)PlayerSchema.ActiveToonIdHash, FastMath.FastHash(value)); 
            } 
        }
        private string _activeToonId;
        public int SessionCoins { get => Block.GetInt((int)PlayerSchema.SessionCoins); set => Block.SetInt((int)PlayerSchema.SessionCoins, value); }
        public float IFrameTimer { get => Block.GetFloat((int)PlayerSchema.IFrameTimer); set => Block.SetFloat((int)PlayerSchema.IFrameTimer, value); }
        public float IFrameDuration { get => Block.GetFloat((int)PlayerSchema.IFrameDuration); set => Block.SetFloat((int)PlayerSchema.IFrameDuration, value); }
    }

    // Replaces single variable definitions in SpawnManager
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct SpawnManagerData
    {
        public FastSchemaBlock Block;
        public float SpawnRadius { get => Block.GetFloat((int)SpawnManagerSchema.SpawnRadius); set => Block.SetFloat((int)SpawnManagerSchema.SpawnRadius, value); }
        public float BaseSpawnRate { get => Block.GetFloat((int)SpawnManagerSchema.BaseSpawnRate); set => Block.SetFloat((int)SpawnManagerSchema.BaseSpawnRate, value); }
        public float DifficultyMultiplier { get => Block.GetFloat((int)SpawnManagerSchema.DifficultyMultiplier); set => Block.SetFloat((int)SpawnManagerSchema.DifficultyMultiplier, value); }
        public float GameTimer { get => Block.GetFloat((int)SpawnManagerSchema.GameTimer); set => Block.SetFloat((int)SpawnManagerSchema.GameTimer, value); }
        public float SpawnAccumulator { get => Block.GetFloat((int)SpawnManagerSchema.SpawnAccumulator); set => Block.SetFloat((int)SpawnManagerSchema.SpawnAccumulator, value); }
    }

    // Replaces single variable definitions in MapManager
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct MapManagerData
    {
        public FastSchemaBlock Block;
        public bool IsSwitchingMap 
        { 
            get => Block.GetInt((int)SystemSchema.IsSwitchingMap) == BackendConfig.Serialization.TrueValue; 
            set => Block.SetInt((int)SystemSchema.IsSwitchingMap, value ? BackendConfig.Serialization.TrueValue : BackendConfig.Serialization.FalseValue); 
        }
    }

    // Replaces single variable definitions in UpgradeManager
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct UpgradeManagerData
    {
        public FastSchemaBlock Block;
        public bool IsSpecialAttackPhase
        {
            get => Block.GetInt((int)SystemSchema.IsSpecialAttackPhase) == BackendConfig.Serialization.TrueValue;
            set => Block.SetInt((int)SystemSchema.IsSpecialAttackPhase, value ? BackendConfig.Serialization.TrueValue : BackendConfig.Serialization.FalseValue);
        }
        public int SpecialAttackInvokes 
        { 
            get => Block.GetInt((int)SystemSchema.SpecialAttackInvokes); 
            set => Block.SetInt((int)SystemSchema.SpecialAttackInvokes, value); 
        }
    }

    // Replaces single variable definitions in EFYVProject
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct EFYVProjectData
    {
        public FastSchemaBlock Block;
        
        public string TargetAssetType 
        { 
            get => _targetAssetType; 
            set 
            { 
                _targetAssetType = value; 
                Block.SetInt((int)ProjectSchema.TargetAssetTypeHash, FastMath.FastHash(value)); 
            } 
        }
        private string _targetAssetType;

        public string UnityProjectPath 
        { 
            get => _unityProjectPath; 
            set 
            { 
                _unityProjectPath = value; 
                Block.SetInt((int)ProjectSchema.UnityProjectPathHash, FastMath.FastHash(value)); 
            } 
        }
        private string _unityProjectPath;

        public int CanvasWidth 
        { 
            get => Block.GetInt((int)ProjectSchema.CanvasWidth); 
            set => Block.SetInt((int)ProjectSchema.CanvasWidth, value); 
        }

        public int CanvasHeight 
        { 
            get => Block.GetInt((int)ProjectSchema.CanvasHeight); 
            set => Block.SetInt((int)ProjectSchema.CanvasHeight, value); 
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct AnimationStateData
    {
        public FastSchemaBlock Block;
        
        public string StateName 
        { 
            get => _stateName; 
            set 
            { 
                _stateName = value; 
                Block.SetInt((int)AnimationStateSchema.StateNameHash, FastMath.FastHash(value)); 
            } 
        }
        private string _stateName;
        
        public int FPS
        {
            get => Block.GetInt((int)AnimationStateSchema.FPS);
            set => Block.SetInt((int)AnimationStateSchema.FPS, value);
        }

        // Item #10 playback tags. LoopEndFrame -1 means "the last frame".
        public int LoopStartFrame
        {
            get => Block.GetInt((int)AnimationStateSchema.LoopStartFrame);
            set => Block.SetInt((int)AnimationStateSchema.LoopStartFrame, value);
        }
        public int LoopEndFrame
        {
            get => Block.GetInt((int)AnimationStateSchema.LoopEndFrame);
            set => Block.SetInt((int)AnimationStateSchema.LoopEndFrame, value);
        }
        public bool PingPong
        {
            get => Block.GetInt((int)AnimationStateSchema.PingPong) == BackendConfig.Serialization.TrueValue;
            set => Block.SetInt((int)AnimationStateSchema.PingPong, value ? BackendConfig.Serialization.TrueValue : BackendConfig.Serialization.FalseValue);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct LayerData
    {
        public FastSchemaBlock Block;
        public bool IsVisible
        {
            get => Block.GetInt((int)LayerSchema.IsVisible) != BackendConfig.Serialization.FalseValue;
            set => Block.SetInt((int)LayerSchema.IsVisible, value ? BackendConfig.Serialization.TrueValue : BackendConfig.Serialization.FalseValue);
        }
        public float Opacity
        {
            get => Block.GetFloat((int)LayerSchema.Opacity);
            set => Block.SetFloat((int)LayerSchema.Opacity, value);
        }
        public int Width
        {
            get => Block.GetInt((int)LayerSchema.Width);
            set => Block.SetInt((int)LayerSchema.Width, value);
        }
        public int Height
        {
            get => Block.GetInt((int)LayerSchema.Height);
            set => Block.SetInt((int)LayerSchema.Height, value);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct FrameData
    {
        public FastSchemaBlock Block;
        public int FrameIndex
        {
            get => Block.GetInt((int)FrameSchema.FrameIndex);
            set => Block.SetInt((int)FrameSchema.FrameIndex, value);
        }

        // Item #10 per-frame duration override (milliseconds); 0 inherits FPS.
        public int DurationMs
        {
            get => Block.GetInt((int)FrameSchema.DurationMs);
            set => Block.SetInt((int)FrameSchema.DurationMs, value);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct SubElementData
    {
        public FastSchemaBlock Block;
        public string Name 
        { 
            get => _name; 
            set 
            { 
                _name = value; 
                Block.SetInt((int)SubElementSchema.NameHash, FastMath.FastHash(value)); 
            } 
        }
        private string _name;
        public int Width 
        { 
            get => Block.GetInt((int)SubElementSchema.Width); 
            set => Block.SetInt((int)SubElementSchema.Width, value); 
        }
        public int Height 
        { 
            get => Block.GetInt((int)SubElementSchema.Height); 
            set => Block.SetInt((int)SubElementSchema.Height, value); 
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct BrushToolData
    {
        public FastSchemaBlock Block;
        public int ActiveLayerIndex { get => Block.GetInt((int)BrushToolSchema.ActiveLayerIndex); set => Block.SetInt((int)BrushToolSchema.ActiveLayerIndex, value); }
        public int BrushSize { get => Block.GetInt((int)BrushToolSchema.BrushSize); set => Block.SetInt((int)BrushToolSchema.BrushSize, value); }
        public int TileSize { get => Block.GetInt((int)BrushToolSchema.TileSize); set => Block.SetInt((int)BrushToolSchema.TileSize, value); }
        public int CurrentColorRgba { get => Block.GetInt((int)BrushToolSchema.CurrentColorRgba); set => Block.SetInt((int)BrushToolSchema.CurrentColorRgba, value); }
        public int LastX { get => Block.GetInt((int)BrushToolSchema.LastX); set => Block.SetInt((int)BrushToolSchema.LastX, value); }
        public int LastY { get => Block.GetInt((int)BrushToolSchema.LastY); set => Block.SetInt((int)BrushToolSchema.LastY, value); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct HitboxToolData
    {
        public FastSchemaBlock Block;
        public string ActiveHitboxKey 
        { 
            get => _activeHitboxKey; 
            set 
            { 
                _activeHitboxKey = value; 
                Block.SetInt((int)HitboxToolSchema.ActiveHitboxKeyHash, FastMath.FastHash(value)); 
            } 
        }
        private string _activeHitboxKey;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct MapToolData
    {
        public FastSchemaBlock Block;
        public float ScatterRadius { get => Block.GetFloat((int)MapToolSchema.ScatterRadius); set => Block.SetFloat((int)MapToolSchema.ScatterRadius, value); }
        public int ScatterDensity { get => Block.GetInt((int)MapToolSchema.ScatterDensity); set => Block.SetInt((int)MapToolSchema.ScatterDensity, value); }
        public float MinScaleJitter { get => Block.GetFloat((int)MapToolSchema.MinScaleJitter); set => Block.SetFloat((int)MapToolSchema.MinScaleJitter, value); }
        public float MaxScaleJitter { get => Block.GetFloat((int)MapToolSchema.MaxScaleJitter); set => Block.SetFloat((int)MapToolSchema.MaxScaleJitter, value); }
        public string Mode { get => _mode; set { _mode = value; Block.SetInt((int)MapToolSchema.ModeHash, FastMath.FastHash(value)); } }
        private string _mode;
        public float FillProbability { get => Block.GetFloat((int)MapToolSchema.FillProbability); set => Block.SetFloat((int)MapToolSchema.FillProbability, value); }
        public short BaseTileId { get => (short)Block.GetInt((int)MapToolSchema.BaseTileId); set => Block.SetInt((int)MapToolSchema.BaseTileId, value); }
        public short TargetTileId { get => (short)Block.GetInt((int)MapToolSchema.TargetTileId); set => Block.SetInt((int)MapToolSchema.TargetTileId, value); }
        public string SelectedAsset { get => _selectedAsset; set { _selectedAsset = value; Block.SetInt((int)MapToolSchema.SelectedAssetHash, FastMath.FastHash(value)); } }
        private string _selectedAsset;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct MovingToolData
    {
        public FastSchemaBlock Block;
        public int ActiveMode { get => Block.GetInt((int)MovingToolSchema.ActiveMode); set => Block.SetInt((int)MovingToolSchema.ActiveMode, value); }
        public int WalkSplitY { get => Block.GetInt((int)MovingToolSchema.WalkSplitY); set => Block.SetInt((int)MovingToolSchema.WalkSplitY, value); }
        public float WalkBounceAmp { get => Block.GetFloat((int)MovingToolSchema.WalkBounceAmp); set => Block.SetFloat((int)MovingToolSchema.WalkBounceAmp, value); }
        public float WalkStrideAmp { get => Block.GetFloat((int)MovingToolSchema.WalkStrideAmp); set => Block.SetFloat((int)MovingToolSchema.WalkStrideAmp, value); }
        public int WalkFrameCount { get => Block.GetInt((int)MovingToolSchema.WalkFrameCount); set => Block.SetInt((int)MovingToolSchema.WalkFrameCount, value); }
        // The jitter accessors validate the octant index themselves: an out-of-range index
        // used to silently alias sibling slots (amplitude 8 overwrote frequency 0, index -1
        // clobbered JitterFrameCount) or reach past the fixed schema block entirely.
        public float GetJitterAmplitude(int index)
        {
            ValidateJitterOctantIndex(index);
            return Block.GetFloat((int)MovingToolSchema.JitterAmplitudesStart + index);
        }
        public void SetJitterAmplitude(int index, float val)
        {
            ValidateJitterOctantIndex(index);
            Block.SetFloat((int)MovingToolSchema.JitterAmplitudesStart + index, val);
        }
        public float GetJitterFrequency(int index)
        {
            ValidateJitterOctantIndex(index);
            return Block.GetFloat((int)MovingToolSchema.JitterFrequenciesStart + index);
        }
        public void SetJitterFrequency(int index, float val)
        {
            ValidateJitterOctantIndex(index);
            Block.SetFloat((int)MovingToolSchema.JitterFrequenciesStart + index, val);
        }
        public int JitterFrameCount { get => Block.GetInt((int)MovingToolSchema.JitterFrameCount); set => Block.SetInt((int)MovingToolSchema.JitterFrameCount, value); }

        // Item #10 preset gauges (bob/breathe + shake/hit-flash).
        public float BobAmplitude { get => Block.GetFloat((int)MovingToolSchema.BobAmplitude); set => Block.SetFloat((int)MovingToolSchema.BobAmplitude, value); }
        public float BreatheAmplitude { get => Block.GetFloat((int)MovingToolSchema.BreatheAmplitude); set => Block.SetFloat((int)MovingToolSchema.BreatheAmplitude, value); }
        public int BobFrameCount { get => Block.GetInt((int)MovingToolSchema.BobFrameCount); set => Block.SetInt((int)MovingToolSchema.BobFrameCount, value); }
        public float ShakeAmplitude { get => Block.GetFloat((int)MovingToolSchema.ShakeAmplitude); set => Block.SetFloat((int)MovingToolSchema.ShakeAmplitude, value); }
        public float FlashStrength { get => Block.GetFloat((int)MovingToolSchema.FlashStrength); set => Block.SetFloat((int)MovingToolSchema.FlashStrength, value); }
        public int ShakeFrameCount { get => Block.GetInt((int)MovingToolSchema.ShakeFrameCount); set => Block.SetInt((int)MovingToolSchema.ShakeFrameCount, value); }

        private static void ValidateJitterOctantIndex(int index)
        {
            if ((uint)index >= BackendConfig.Deformation.OctantCount)
                throw new System.ArgumentOutOfRangeException(nameof(index));
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct PurchasableData
    {
        public FastSchemaBlock Block;
        public string ItemName { get => _itemName; set { _itemName = value; Block.SetInt((int)PurchasableSchema.ItemNameHash, FastMath.FastHash(value)); } }
        private string _itemName;
        public int Cost { get => Block.GetInt((int)PurchasableSchema.Cost); set => Block.SetInt((int)PurchasableSchema.Cost, value); }
        public float HealAmount { get => Block.GetFloat((int)PurchasableSchema.HealAmount); set => Block.SetFloat((int)PurchasableSchema.HealAmount, value); }
        public string BuffId { get => _buffId; set { _buffId = value; Block.SetInt((int)PurchasableSchema.BuffIdHash, FastMath.FastHash(value)); } }
        private string _buffId;
        public float Duration { get => Block.GetFloat((int)PurchasableSchema.Duration); set => Block.SetFloat((int)PurchasableSchema.Duration, value); }
        public string WeaponId { get => _weaponId; set { _weaponId = value; Block.SetInt((int)PurchasableSchema.WeaponIdHash, FastMath.FastHash(value)); } }
        private string _weaponId;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct SystemData
    {
        public FastSchemaBlock Block;
        public int CurrentBlurRadius { get => Block.GetInt((int)SystemSchema.CurrentBlurRadius); set => Block.SetInt((int)SystemSchema.CurrentBlurRadius, value); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct ViewportData
    {
        public FastSchemaBlock Block;
        public float CellSize { get => Block.GetFloat((int)ViewportSchema.CellSize); set => Block.SetFloat((int)ViewportSchema.CellSize, value); }
        public float ZoomLevel { get => Block.GetFloat((int)ViewportSchema.ZoomLevel); set => Block.SetFloat((int)ViewportSchema.ZoomLevel, value); }
        public int OffsetX { get => Block.GetInt((int)ViewportSchema.OffsetX); set => Block.SetInt((int)ViewportSchema.OffsetX, value); }
        public int OffsetY { get => Block.GetInt((int)ViewportSchema.OffsetY); set => Block.SetInt((int)ViewportSchema.OffsetY, value); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct WeaponData
    {
        public FastSchemaBlock Block;
        public float CooldownTime { get => Block.GetFloat((int)WeaponSchema.CooldownTime); set => Block.SetFloat((int)WeaponSchema.CooldownTime, value); }
        public float BaseDamage { get => Block.GetFloat((int)WeaponSchema.BaseDamage); set => Block.SetFloat((int)WeaponSchema.BaseDamage, value); }
        public int Level { get => Block.GetInt((int)WeaponSchema.Level); set => Block.SetInt((int)WeaponSchema.Level, value); }
        public float CurrentCooldown { get => Block.GetFloat((int)WeaponSchema.CurrentCooldown); set => Block.SetFloat((int)WeaponSchema.CurrentCooldown, value); }
        public float ProjectileSpeed { get => Block.GetFloat((int)WeaponSchema.ProjectileSpeed); set => Block.SetFloat((int)WeaponSchema.ProjectileSpeed, value); }
        public int PierceCount { get => Block.GetInt((int)WeaponSchema.PierceCount); set => Block.SetInt((int)WeaponSchema.PierceCount, value); }
        public float AuraRadius { get => Block.GetFloat((int)WeaponSchema.AuraRadius); set => Block.SetFloat((int)WeaponSchema.AuraRadius, value); }
        public float DamageRadius { get => Block.GetFloat((int)WeaponSchema.DamageRadius); set => Block.SetFloat((int)WeaponSchema.DamageRadius, value); }
        public int DropCount { get => Block.GetInt((int)WeaponSchema.DropCount); set => Block.SetInt((int)WeaponSchema.DropCount, value); }
        public float AttackRange { get => Block.GetFloat((int)WeaponSchema.AttackRange); set => Block.SetFloat((int)WeaponSchema.AttackRange, value); }
        public float KnockbackForce { get => Block.GetFloat((int)WeaponSchema.KnockbackForce); set => Block.SetFloat((int)WeaponSchema.KnockbackForce, value); }
        public float OrbitRadius { get => Block.GetFloat((int)WeaponSchema.OrbitRadius); set => Block.SetFloat((int)WeaponSchema.OrbitRadius, value); }
        public float RotationSpeed { get => Block.GetFloat((int)WeaponSchema.RotationSpeed); set => Block.SetFloat((int)WeaponSchema.RotationSpeed, value); }
        public int ProjectileCount { get => Block.GetInt((int)WeaponSchema.ProjectileCount); set => Block.SetInt((int)WeaponSchema.ProjectileCount, value); }
        public float CurrentAngle { get => Block.GetFloat((int)WeaponSchema.CurrentAngle); set => Block.SetFloat((int)WeaponSchema.CurrentAngle, value); }
        public int ProjectilePrefabKey { get => Block.GetInt((int)WeaponSchema.ProjectilePrefabKey); set => Block.SetInt((int)WeaponSchema.ProjectilePrefabKey, value); }
        public float SplashRadius { get => Block.GetFloat((int)WeaponSchema.SplashRadius); set => Block.SetFloat((int)WeaponSchema.SplashRadius, value); }
        public int SplashCount { get => Block.GetInt((int)WeaponSchema.SplashCount); set => Block.SetInt((int)WeaponSchema.SplashCount, value); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct EntityData
    {
        public FastSchemaBlock Block;
        public int PrefabPoolKey { get => Block.GetInt((int)EntitySchema.PrefabPoolKey); set => Block.SetInt((int)EntitySchema.PrefabPoolKey, value); }
        public float MaxHealth { get => Block.GetFloat((int)EntitySchema.MaxHealth); set => Block.SetFloat((int)EntitySchema.MaxHealth, value); }
        public float CurrentHealth { get => Block.GetFloat((int)EntitySchema.CurrentHealth); set => Block.SetFloat((int)EntitySchema.CurrentHealth, value); }
        public float BaseSpeed { get => Block.GetFloat((int)EntitySchema.BaseSpeed); set => Block.SetFloat((int)EntitySchema.BaseSpeed, value); }
        public float ExperienceValue { get => Block.GetFloat((int)EntitySchema.ExperienceValue); set => Block.SetFloat((int)EntitySchema.ExperienceValue, value); }
        public float DamageToPlayer { get => Block.GetFloat((int)EntitySchema.DamageToPlayer); set => Block.SetFloat((int)EntitySchema.DamageToPlayer, value); }
        public int ActiveListIndex { get => Block.GetInt((int)EntitySchema.ActiveListIndex); set => Block.SetInt((int)EntitySchema.ActiveListIndex, value); }
        public float Phase2HealthThreshold { get => Block.GetFloat((int)EntitySchema.Phase2HealthThreshold); set => Block.SetFloat((int)EntitySchema.Phase2HealthThreshold, value); }
        public int CurrentPhase { get => Block.GetInt((int)EntitySchema.CurrentPhase); set => Block.SetInt((int)EntitySchema.CurrentPhase, value); }
        public bool IsEnraged { get => Block.GetInt((int)EntitySchema.IsEnraged) == BackendConfig.Serialization.TrueValue; set => Block.SetInt((int)EntitySchema.IsEnraged, value ? BackendConfig.Serialization.TrueValue : BackendConfig.Serialization.FalseValue); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct ProjectileData
    {
        public FastSchemaBlock Block;
        public float Damage { get => Block.GetFloat((int)ProjectileSchema.Damage); set => Block.SetFloat((int)ProjectileSchema.Damage, value); }
        public int PiercingCount { get => Block.GetInt((int)ProjectileSchema.PiercingCount); set => Block.SetInt((int)ProjectileSchema.PiercingCount, value); }
        public float Speed { get => Block.GetFloat((int)ProjectileSchema.Speed); set => Block.SetFloat((int)ProjectileSchema.Speed, value); }
        public int ActiveListIndex { get => Block.GetInt((int)ProjectileSchema.ActiveListIndex); set => Block.SetInt((int)ProjectileSchema.ActiveListIndex, value); }
        public float DirectionX { get => Block.GetFloat((int)ProjectileSchema.DirectionX); set => Block.SetFloat((int)ProjectileSchema.DirectionX, value); }
        public float DirectionY { get => Block.GetFloat((int)ProjectileSchema.DirectionY); set => Block.SetFloat((int)ProjectileSchema.DirectionY, value); }
        public int CurrentPierces { get => Block.GetInt((int)ProjectileSchema.CurrentPierces); set => Block.SetInt((int)ProjectileSchema.CurrentPierces, value); }
        public float RemainingLifetime { get => Block.GetFloat((int)ProjectileSchema.RemainingLifetime); set => Block.SetFloat((int)ProjectileSchema.RemainingLifetime, value); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct PowerUpData
    {
        public FastSchemaBlock Block;
        public int PowerUpIdHash { get => Block.GetInt((int)PowerUpSchema.PowerUpIdHash); set => Block.SetInt((int)PowerUpSchema.PowerUpIdHash, value); }
        public int Level { get => Block.GetInt((int)PowerUpSchema.Level); set => Block.SetInt((int)PowerUpSchema.Level, value); }
        public int Grade { get => Block.GetInt((int)PowerUpSchema.Grade); set => Block.SetInt((int)PowerUpSchema.Grade, value); }
        public int UsesRemaining { get => Block.GetInt((int)PowerUpSchema.UsesRemaining); set => Block.SetInt((int)PowerUpSchema.UsesRemaining, value); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct LegacyAchievementDefinitionData
    {
        public FastSchemaBlock Block;
        public int Id { get => Block.GetInt((int)LegacyAchievementDefinitionSchema.Id); set => Block.SetInt((int)LegacyAchievementDefinitionSchema.Id, value); }
        public int TitleHash { get => Block.GetInt((int)LegacyAchievementDefinitionSchema.TitleHash); set => Block.SetInt((int)LegacyAchievementDefinitionSchema.TitleHash, value); }
        public int DescriptionHash { get => Block.GetInt((int)LegacyAchievementDefinitionSchema.DescriptionHash); set => Block.SetInt((int)LegacyAchievementDefinitionSchema.DescriptionHash, value); }
    }
}
