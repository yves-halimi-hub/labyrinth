using System.Runtime.InteropServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Data
{
    // The master block of memory for any data schema; size is defined by BackendConfig.Schema.
    // Utterly fast, branchless, zero-allocation flat array memory wrapper.
    [StructLayout(LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public unsafe struct FastSchemaBlock
    {
        public const int MaxSize = BackendConfig.Schema.BlockSlotCount;
        public fixed int Raw[MaxSize];

        public int GetInt(int offset) { return Raw[offset]; }
        public void SetInt(int offset, int val) { Raw[offset] = val; }
        
        public float GetFloat(int offset) 
        { 
            fixed (int* ptr = Raw) return ((float*)ptr)[offset]; 
        }
        public void SetFloat(int offset, float val) 
        { 
            fixed (int* ptr = Raw) ((float*)ptr)[offset] = val; 
        }

        public void ApplyAdditiveFloat(int offset, float val)
        {
            fixed (int* ptr = Raw) ((float*)ptr)[offset] += val;
        }

        public void ApplyMultiplicativeFloat(int offset, float val)
        {
            fixed (int* ptr = Raw) ((float*)ptr)[offset] *= val;
        }

        // Specific helper for Stats since they are used everywhere
        public static FastSchemaBlock DefaultStats()
        {
            FastSchemaBlock block = new FastSchemaBlock();
            for (int i = 0; i < (int)StatSchema.MAX_STATS; i++)
            {
                block.SetFloat(i, BackendConfig.Schema.DefaultStatMultiplier);
            }
            block.SetFloat((int)StatSchema.Recovery, BackendConfig.Schema.DefaultAdditiveStat);
            block.SetFloat((int)StatSchema.Armor, BackendConfig.Schema.DefaultAdditiveStat);
            block.SetFloat((int)StatSchema.Revival, BackendConfig.Schema.DefaultAdditiveStat);
            block.SetFloat((int)StatSchema.Amount, BackendConfig.Schema.DefaultAdditiveStat);
            return block;
        }
    }

    // ----------------------------------------------------
    // SCHEMA DEFINITIONS (Integer Memory Offsets)
    // ----------------------------------------------------

    public enum StatSchema : int
    {
        MaxHealth = 0,
        Recovery = 1,
        Armor = 2,
        MoveSpeed = 3,
        Revival = 4,
        Might = 5,
        Area = 6,
        WeaponSpeed = 7,
        Duration = 8,
        Cooldown = 9,
        Amount = 10,
        Magnet = 11,
        Luck = 12,
        Growth = 13,
        Greed = 14,
        Curse = 15,
        MAX_STATS = 16
    }

    public enum ToonSchema : int
    {
        ToonIdHash = 0,
        Level = 1,
        TotalCoinsCollected = 2,
        UnspentStatPoints = 3,
        
        // The stats block starts at offset 4 and takes 16 slots.
        StatsStart = 4
    }

    // Unifies AssetDataHierarchy.cs and EntityData.cs into a 64-byte ultra-fast block
    public enum AssetSchema : int
    {
        AssetIdHash = 0,
        
        // Base Stats (Living Entity)
        MaxHealth = 1,
        BaseSpeed = 2,
        
        // Enemy specific
        DamageToPlayer = 3,
        ExperienceValue = 4,
        
        // Boss specific
        Phase2HealthThreshold = 5,
        
        // Weapon specific
        BaseDamage = 6,
        CooldownTimer = 7,
        
        // Prop/Tile specific
        IsWalkable = 8,
        TrapDamage = 9,
        
        // Reserve up to 16 integer slots for polymorhpic asset data
        SIZE = 16
    }
    
    public enum PowerUpGrade : int
    {
        Normal = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3
    }

    public enum PowerUpSchema : int
    {
        PowerUpIdHash = 0,
        Level = 1,
        Grade = 2, // Casts to PowerUpGrade
        UsesRemaining = 3,
        SIZE = 4
    }

    public enum WeaponSchema : int
    {
        WeaponIdHash = 0,
        Level = 1,
        BaseDamage = 2,
        CooldownTime = 3,
        OrbitRadius = 4,
        RotationSpeed = 5,
        ProjectileCount = 6,
        DamageRadius = 7,
        AuraRadius = 8,
        DropCount = 9,
        AttackRange = 10,
        KnockbackForce = 11,
        ProjectileSpeed = 12,
        PierceCount = 13,
        ProjectilePrefabKey = 14,
        SplashRadius = 15,
        SplashCount = 16,
        CurrentCooldown = 17,
        CurrentAngle = 18,
        SIZE = 19
    }

    public enum MovingToolSchema : int
    {
        ActiveMode = 0,
        WalkSplitY = 1,
        WalkBounceAmp = 2,
        WalkStrideAmp = 3,
        WalkFrameCount = 4,
        JitterFrameCount = 5,
        JitterAmplitudesStart = 6, // 8 slots
        JitterFrequenciesStart = 14, // 8 slots
        // Item #10 preset gauges (bob/breathe + shake/hit-flash).
        BobAmplitude = 22,
        BreatheAmplitude = 23,
        BobFrameCount = 24,
        ShakeAmplitude = 25,
        FlashStrength = 26,
        ShakeFrameCount = 27,
        SIZE = 28
    }

    public enum BrushToolSchema : int
    {
        ActiveLayerIndex = 0,
        BrushSize = 1,
        TileSize = 2,
        CurrentColorRgba = 3,
        LastX = 4,
        LastY = 5,
        SIZE = 6
    }

    public enum HitboxSchema : int
    {
        FrameIndex = 0,
        HitboxTypeHash = 1,
        X = 2,
        Y = 3,
        Width = 4,
        Height = 5,
        SIZE = 6
    }
    
    public enum LayerSchema : int
    {
        IsVisible = 0,
        Opacity = 1,
        Width = 2,
        Height = 3,
        NameHash = 4,
        SIZE = 5
    }

    public enum EntitySchema : int
    {
        MaxHealth = 0,
        CurrentHealth = 1,
        BaseSpeed = 2,
        DamageToPlayer = 3,
        ExperienceValue = 4,
        ActiveListIndex = 5,
        PrefabPoolKey = 6,
        AnimationSpeed = 7,
        IsBlocking = 8,
        AnimTimer = 9,
        CurrentFrame = 10,
        CurrentPhase = 11,
        IsEnraged = 12,
        Phase2HealthThreshold = 13,
        SIZE = 14
    }

    public enum PlayerSchema : int
    {
        Experience = 0,
        Level = 1,
        ActiveToonIdHash = 2,
        SessionCoins = 3,
        IFrameDuration = 4,
        IFrameTimer = 5,
        SIZE = 6
    }

    public enum ProjectileSchema : int
    {
        Damage = 0,
        PiercingCount = 1,
        Speed = 2,
        ActiveListIndex = 3,
        DirectionX = 4,
        DirectionY = 5,
        CurrentPierces = 6,
        RemainingLifetime = 7,
        SIZE = 8
    }

    public enum SpawnManagerSchema : int
    {
        SpawnRadius = 0,
        BaseSpawnRate = 1,
        DifficultyMultiplier = 2,
        GameTimer = 3,
        SpawnAccumulator = 4,
        SIZE = 5
    }

    public enum MapToolSchema : int
    {
        ScatterRadius = 0,
        ScatterDensity = 1,
        MinScaleJitter = 2,
        MaxScaleJitter = 3,
        ModeHash = 4,
        FillProbability = 5,
        SelectedAssetHash = 6,
        BaseTileId = 7,
        TargetTileId = 8,
        SIZE = 9
    }

    public enum PurchasableSchema : int
    {
        ItemNameHash = 0,
        Cost = 1,
        HealAmount = 2,
        BuffIdHash = 3,
        Duration = 4,
        WeaponIdHash = 5,
        SIZE = 6
    }

    public enum PropSchema : int
    {
        Value = 0,
        Grade = 1,
        XpValue = 2,
        SIZE = 3
    }

    public enum HitboxToolSchema : int
    {
        ActiveHitboxKeyHash = 0,
        SIZE = 1
    }

    public enum ViewportSchema : int
    {
        ZoomLevel = 0,
        OffsetX = 1,
        OffsetY = 2,
        CellSize = 3,
        SIZE = 4
    }

    public enum SystemSchema : int
    {
        DynamicDropMultiplier = 0,
        IntensityMultiplier = 1,
        EnemyHealthMultiplier = 2,
        EnemySpeedMultiplier = 3,
        CurrentBlurRadius = 4,
        IsSpecialAttackPhase = 5,
        IsSwitchingMap = 6,
        SpecialAttackInvokes = 7,
        SIZE = 8
    }
    
    public enum ProjectSchema : int
    {
        TargetAssetTypeHash = 0,
        UnityProjectPathHash = 1,
        CanvasWidth = 2,
        CanvasHeight = 3,
        SIZE = 4
    }

    public enum AnimationStateSchema : int
    {
        StateNameHash = 0,
        FPS = 1,
        // Item #10 playback tags: loop range (LoopEndFrame -1 = last frame)
        // and ping-pong flag. See EFYVLabyrinthConfig.LabyMake.Animation.
        LoopStartFrame = 2,
        LoopEndFrame = 3,
        PingPong = 4,
        SIZE = 5
    }

    public enum FrameSchema : int
    {
        FrameIndex = 0,
        // Item #10 per-frame duration override in milliseconds; 0 inherits the
        // owning animation's FPS.
        DurationMs = 1,
        SIZE = 2
    }

    public enum SubElementSchema : int
    {
        NameHash = 0,
        Width = 1,
        Height = 2,
        SIZE = 3
    }

    public enum WeaponControllerSchema : int
    {
        MaxWeapons = 0,
        SIZE = 1
    }

    public enum DoorPropSchema : int
    {
        TargetMapIdHash = 0,
        SIZE = 1
    }
    
    public enum LegacyAchievementDefinitionSchema : int
    {
        Id = 0,
        TitleHash = 1,
        DescriptionHash = 2,
        SIZE = 3
    }

    // The master Save Data file schema. Contains everything required to dump the player profile.
    [StructLayout(LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public unsafe struct PlayerMetaSchema
    {
        public int TotalCoinsCollected;

        // Account Legacy Stats
        public FastSchemaBlock LegacyStats;

        // Account Achievements (256-bit mask)
        public FastSchemaBlock LegacyAchievements;

        public const int MaxToons = BackendConfig.Schema.MaxToons;

        // The exact byte size this struct must marshal to: the save envelope and
        // the ToonBlocks stride math below both depend on it.
        public const int ExpectedSizeBytes =
            sizeof(int) + (2 + MaxToons) * BackendConfig.Schema.BlockSizeBytes;

        // Static size assert: the fixed ToonBlocks buffer strides by
        // BackendConfig.Schema.BlockSizeBytes, so a FastSchemaBlock (or profile)
        // whose real size drifts from the config constant would silently corrupt
        // every toon block read/write. Fail loudly at type initialization instead.
        static PlayerMetaSchema()
        {
            if (sizeof(FastSchemaBlock) != BackendConfig.Schema.BlockSizeBytes ||
                sizeof(PlayerMetaSchema) != ExpectedSizeBytes)
            {
                throw new System.InvalidOperationException(
                    "PlayerMetaSchema layout drifted from BackendConfig.Schema constants.");
            }
        }

        // Contiguous blocks for each toon; capacity and stride come from BackendConfig.Schema.
        public fixed byte ToonBlocks[MaxToons * BackendConfig.Schema.BlockSizeBytes];

        public static PlayerMetaSchema Default()
        {
            return new PlayerMetaSchema {
                TotalCoinsCollected = BackendConfig.Schema.InitialTotalCoins,
                LegacyStats = FastSchemaBlock.DefaultStats(),
                LegacyAchievements = new FastSchemaBlock() // All zeros (locked)
            };
        }
        
        public bool TryGetToonBlock(int index, out FastSchemaBlock block)
        {
            if (index < 0 || index >= MaxToons)
            {
                block = default;
                return false;
            }

            fixed (byte* ptr = ToonBlocks)
            {
                block = *(((FastSchemaBlock*)ptr) + index);
            }
            return true;
        }

        public bool TrySetToonBlock(int index, in FastSchemaBlock block)
        {
            if (index < 0 || index >= MaxToons) return false;

            fixed (byte* ptr = ToonBlocks)
            {
                *(((FastSchemaBlock*)ptr) + index) = block;
            }
            return true;
        }
    }
}
