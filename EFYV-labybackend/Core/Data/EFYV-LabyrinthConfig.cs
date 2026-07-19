namespace EFYVBackend.Core.Data
{
    public static class EFYVLabyrinthConfig
    {
        public static class Shared
        {
            public const int FirstIndex = 0;
            public const int EmptyCount = FirstIndex;
            public const int NotFoundIndex = -1;
            public const int UnitStep = 1;
            public const int SequentialStructPack = UnitStep;
            public const float NormalizedMin = 0f;
            public const float NormalizedMax = 1f;
            public const int RgbaGreenShift = 8;
            public const int RgbaBlueShift = 16;
            public const int RgbaAlphaShift = 24;
            public const int TransparentAlpha = EmptyCount;
            public const uint TransparentRgba = EmptyCount;
            public const int DirectionOctantCount = 8;
            public const float PixelsPerUnit = 16f;
            public const string EmptyString = "";
            public const string EntityNameField = "entityName";
            public const string AssetNameField = "assetName";
            public const string AssetTypeField = "assetType";
            public const string MaxHealthField = "maxHealth";
            public const string BaseSpeedField = "baseSpeed";
            public const string DamageToPlayerField = "damageToPlayer";
            public const string ExperienceValueField = "experienceValue";
            public const string Phase2HealthThresholdField = "phase2HealthThreshold";
            public const string BaseDamageField = "baseDamage";
            public const string CooldownTimerField = "cooldownTimer";
            public const string IsWalkableField = "isWalkable";
            public const string TrapDamageField = "trapDamage";
            public const string FacingField = "facing";
            public const string EfyvExtension = ".efyvlaby";
            public const string PngExtension = ".png";
            public const string LivingEntityAssetType = "LivingEntityData";
            public const string GameAssetAssetType = "GameAssetData";
            public const string EnemyAssetType = "EnemyData";
            public const string BossAssetType = "BossData";
            public const string LivingEntityDisplayName = "Living Entity";
            public const string GameAssetDisplayName = "Game Asset";
            public const string EnemyDisplayName = "Enemy";
            public const string BossDisplayName = "Boss";
            public const string FacingUp = "Up";
            public const string FacingDown = "Down";
            public const string FacingLeft = "Left";
            public const string FacingRight = "Right";
            public const string FacingFileSuffixUp = "_Up";
            public const string FacingFileSuffixDown = "_Down";
            public const string FacingFileSuffixLeft = "_Left";
            public const string FacingFileSuffixRight = "_Right";

            // Item #7 runtime-effect trigger tags. Shared because both sides
            // speak them: the designer offers them when authoring effect
            // descriptors and the game runtime fires them from the matching
            // LivingEntity seams (OnSpawn / TakeDamage).
            public const string EffectTriggerOnSpawn = "OnSpawn";
            public const string EffectTriggerOnDamaged = "OnDamaged";

            // ----------------------------------------------------------------
            // Shared schema-field manifest (#15): the single wire-format table
            // mapping .efyvlaby property names to AssetSchema block slots.
            // The LabyMake designer schema (field definitions below) and the
            // Unity importer (EFYVPixelArtImporter.ApplySchemaProperties) both
            // consume this table; neither side may hardcode a key list again.
            // ----------------------------------------------------------------
            public enum SchemaFieldKind
            {
                Float = 0,
                // Stored in the block as Serialization.TrueValue/FalseValue;
                // accepted on the wire as JSON true/false or 0/1 numbers.
                Boolean = 1
            }

            public readonly struct SchemaFieldMapping
            {
                public string FieldName { get; }
                public int Slot { get; }
                public SchemaFieldKind Kind { get; }

                public SchemaFieldMapping(string fieldName, int slot, SchemaFieldKind kind)
                {
                    FieldName = fieldName;
                    Slot = slot;
                    Kind = kind;
                }
            }

            public static readonly SchemaFieldMapping[] AssetSchemaFieldManifest =
            {
                new SchemaFieldMapping(MaxHealthField, (int)AssetSchema.MaxHealth, SchemaFieldKind.Float),
                new SchemaFieldMapping(BaseSpeedField, (int)AssetSchema.BaseSpeed, SchemaFieldKind.Float),
                new SchemaFieldMapping(DamageToPlayerField, (int)AssetSchema.DamageToPlayer, SchemaFieldKind.Float),
                new SchemaFieldMapping(ExperienceValueField, (int)AssetSchema.ExperienceValue, SchemaFieldKind.Float),
                new SchemaFieldMapping(Phase2HealthThresholdField, (int)AssetSchema.Phase2HealthThreshold, SchemaFieldKind.Float),
                new SchemaFieldMapping(BaseDamageField, (int)AssetSchema.BaseDamage, SchemaFieldKind.Float),
                new SchemaFieldMapping(CooldownTimerField, (int)AssetSchema.CooldownTimer, SchemaFieldKind.Float),
                new SchemaFieldMapping(IsWalkableField, (int)AssetSchema.IsWalkable, SchemaFieldKind.Boolean),
                new SchemaFieldMapping(TrapDamageField, (int)AssetSchema.TrapDamage, SchemaFieldKind.Float)
            };

            // Identity/routing keys that legitimately appear next to schema
            // fields in the properties object without occupying a block slot.
            public static readonly string[] NonSchemaPropertyFields =
            {
                EntityNameField,
                AssetNameField,
                FacingField
            };
        }

        public static class Game
        {
            public static class Runtime
            {
                public const int FirstIndex = EFYVLabyrinthConfig.Shared.FirstIndex;
                public const int EmptyCollectionCount = EFYVLabyrinthConfig.Shared.EmptyCount;
                public const int NotFoundIndex = EFYVLabyrinthConfig.Shared.NotFoundIndex;
                public const int ExclusiveUpperBoundOffset = EFYVLabyrinthConfig.Shared.UnitStep;
                public const int SequentialStructPack = EFYVLabyrinthConfig.Shared.SequentialStructPack;
                public const float UnitIntervalMin = EFYVLabyrinthConfig.Shared.NormalizedMin;
                public const float UnitIntervalMax = EFYVLabyrinthConfig.Shared.NormalizedMax;
            }

            public static class Player
            {
                public const float DefaultMaxHealth = 100f;
                public const float DefaultBaseSpeed = 5f;
                public const int DefaultLevel = 1;
                public const float InitialIFrameTimer = 0f;
                public const float InvincibilityFramesDuration = 0.5f;
                public const float IFrameZeroThreshold = 0f;
                public const string InputHorizontal = "Horizontal";
                public const string InputVertical = "Vertical";
                public const float InitialExperience = 0f;
                public const float ExpNeededPerLevelMultiplier = 100f;
                public const int InitialSessionCoins = 0;
                public const int ZeroAmount = 0;
                public const int LevelIncrement = 1;
                // Timed-buff effect for the merchant haste potion (#34): applied
                // by PlayerController.ApplyTimedBuff (refresh-never-stack,
                // reverted on expiry).
                public const float MoveSpeedBuffMultiplier = 1.5f;
            }

            public static class Weapons
            {
                // Weapon Configs
                public const float DefaultZOffset = -1.0f;

                // Logs
                public const string LogSpecialAttackPhase = "Entered Special Attack Phase! Normal upgrades exhausted.";
                public const string LogOfferingNormalUpgrades = "Offering {0} random normal upgrades.";
                public const string LogOfferingSpecialAttacks = "Offering {0} special attacks. Penalty Multiplier: {1}";
                public const string LogScreenWipe = "Screen Wipe Activated!";
                public const string LogMobHealthHalved = "All Mob Health Halved!";
                public const string LogGrantedNewWeapon = "Upgrade granted a new weapon: {0}";
                public const string LogUpgradedWeapon = "Upgrade raised {0} to level {1}.";

                // Tooltips
                public const string TooltipNormalWeaponPool = "Weapon prefabs a normal upgrade may grant into a free player slot.";

                public const string SpecialAttackScreenWipeName = "ScreenWipe";
                public const string SpecialAttackHalfMobHealthName = "HalfMobHealth";
                public const float SpecialAttackScreenWipeDamage = 999999f;
                public const float SpecialAttackHalfMobHealthMultiplier = 0.5f;

                public static class MagicWand
                {
                    public const float DefaultCooldown = 1.0f;
                    public const float DefaultDamage = 10f;
                    public const float DefaultSpeed = 10f;
                    public const int DefaultPierce = 1;
                    public const int DefaultLevel = 1;
                    public const float UpgradeDamage = 5f;
                    public const float UpgradeCooldownReduction = 0.05f;
                    public const float MinCooldown = 0.1f;
                    public const int PierceUpgradeInterval = 3;
                    public const int PierceUpgradeIncrement = 1;
                }

                public static class Aura
                {
                    public const float DefaultRadius = 3f;
                    public static class Garlic { public const float Damage = 2f; public const float Radius = 3f; public const float Cooldown = 0.5f; }
                    public static class Swords { public const float Damage = 15f; public const float Radius = 5f; public const float Cooldown = 1.0f; }
                }

                public static class Drop
                {
                    public const float DefaultDamageRadius = 3f;
                    public const int DefaultCount = 1;
                    public const float VfxLifetime = 1.0f;
                    public static class Bomb { public const float Damage = 50f; public const float DamageRadius = 4f; public const int Count = 1; public const float Cooldown = 3.0f; }
                    public static class Meteor { public const float Damage = 100f; public const float DamageRadius = 6f; public const int Count = 3; public const float Cooldown = 10.0f; }
                }

                public static class Splash
                {
                    public const float DefaultSplashRadius = 5f;
                    public const float DefaultDamageRadius = 2f;
                    public const int DefaultCount = 3;
                    public const float VfxLifetime = 0.5f;
                    public static class Lightning { public const float Damage = 20f; public const float SplashRadius = 6f; public const float DamageRadius = 2.5f; public const int Count = 3; public const float Cooldown = 2.0f; }
                    public static class HolyWater { public const float Damage = 5f; public const float SplashRadius = 4f; public const float DamageRadius = 1.5f; public const int Count = 5; public const float Cooldown = 1.5f; }
                }

                public static class Melee
                {
                    public const float DefaultAttackRange = 2f;
                    public const float DefaultKnockback = 5f;
                    public static class Bat { public const float Damage = 30f; public const float AttackRange = 1.5f; public const float Knockback = 15f; public const float Cooldown = 0.8f; }
                    public static class Sword { public const float Damage = 40f; public const float AttackRange = 3.0f; public const float Knockback = 5f; public const float Cooldown = 1.2f; }
                }

                public static class Orbital
                {
                    public const float DefaultOrbitRadius = 3f;
                    public const float DefaultRotationSpeed = 180f;
                    public const int DefaultProjectileCount = 2;
                    public const float DefaultDamageRadius = 0.5f;
                    public const float InitialAngle = 0f;
                    public const float FullCircleDegrees = 360f;
                    public static class Axe { public const float Damage = 25f; public const float OrbitRadius = 4f; public const float RotationSpeed = 90f; public const int Count = 2; public const float DamageRadius = 1.0f; }
                    public static class Beyblade { public const float Damage = 10f; public const float OrbitRadius = 2f; public const float RotationSpeed = 360f; public const int Count = 4; public const float DamageRadius = 0.5f; }
                }

                public static class Projectile
                {
                    public const float DefaultSpeed = 10f;
                    public const int DefaultPierce = 1;
                    public const int PierceHitIncrement = 1;
                    public const float DefaultLifetime = 10f;
                    public const float LifetimeExpiredThreshold = 0f;
                    public static class Gun { public const float Damage = 15f; public const float Speed = 25f; public const int Pierce = 1; public const float Cooldown = 0.5f; }
                    public static class Bow { public const float Damage = 25f; public const float Speed = 15f; public const int Pierce = 3; public const float Cooldown = 1.2f; }
                }

                public const float CooldownReadyThreshold = 0f;
                public const int PierceUpgradeRemainder = 0;
                public const int LoopStartIndex = 0;
                public const int InitialPierces = 0;
                public const int MissingWeaponIndex = Runtime.NotFoundIndex;
                public const int MissingPowerUpIndex = Runtime.NotFoundIndex;
                public const int RandomChoiceMinIndex = Runtime.FirstIndex;

                public static class Inventory
                {
                    public const int InitialLevel = 1;
                    public const int MaxLevel = 10;
                    public const int PlayerMaxWeapons = 6;
                    public const int MonsterMaxWeapons = 1;
                    public const int MiniBossMaxWeapons = 2;
                    public const int BossMaxWeapons = 6;
                    public const int PowerUpUses = 3;
                    public const int UpgradeChoicesSpecialPhase = 2;
                    public const int UpgradeChoicesNormalPhase = 3;
                    public const float PenaltyMultiplierBase = 1.0f;
                    public const float PenaltyMultiplierIncrement = 0.1f;
                    public const int LevelIncrement = 1;
                    public const int UseCost = 1;
                    public const int ExhaustedUses = 0;
                    public const int GradeDecrement = 1;
                    public const int InitialSpecialAttackPhaseFlag = 0;
                    public const int InitialSpecialAttackInvokes = 0;
                    public const int SpecialAttackInvokeIncrement = 1;
                    public const bool SpecialAttackPhase = true;
                }
            }

            public static class Entity
            {
                public const float DeathHealthThreshold = 0f;
                public const float PositiveAmountThreshold = 0f;
                public const float StationaryAxisValue = 0f;
                public const int EmptyPrefabPoolKey = 0;
                public const EFYVBackend.Core.Math.FastMath.FacingDirection InitialFacing = EFYVBackend.Core.Math.FastMath.FacingDirection.Down;
            }

            // Item #7 runtime interpretation of authored effect descriptors:
            // flash/tint recolor the SpriteRenderer on their trigger seams and
            // the flash countdown is ticked by the central update loops.
            // particleHook descriptors are stored on the imported asset but
            // NOT interpreted yet (deferred until a particle pipeline exists).
            public static class EntityEffects
            {
                public const string TypeFlash = EFYVLabyrinthConfig.Backend.Exporter.EffectTypeFlash;
                public const string TypeTint = EFYVLabyrinthConfig.Backend.Exporter.EffectTypeTint;
                public const string TypeParticleHook =
                    EFYVLabyrinthConfig.Backend.Exporter.EffectTypeParticleHook;
                public const string TriggerOnSpawn = EFYVLabyrinthConfig.Shared.EffectTriggerOnSpawn;
                public const string TriggerOnDamaged = EFYVLabyrinthConfig.Shared.EffectTriggerOnDamaged;
                public const float MillisecondsPerSecond = 1000f;
                public const float ExpiredFlashSeconds = 0f;
            }

            // Item #13 runtime flipbook: LivingEntity plays the CURRENT facing's
            // imported animation over EntityFacingImportData.Frames, ticked by
            // the central update loops (Enemy.Tick / PlayerController.Update).
            // Frame timing honors the imported per-frame durations (0 = inherit
            // the animation FPS) and the loop-range / ping-pong playback tags.
            public static class Animation
            {
                public const float MillisecondsPerSecond = EntityEffects.MillisecondsPerSecond;
                // FPS guard when an animation's metadata carries a non-positive
                // FramesPerSecond (single-source with the designer default).
                public const int DefaultFramesPerSecond = EFYVLabyrinthConfig.LabyMake.Animation.DefaultFPS;
                public const int InheritFrameDurationMs = Runtime.EmptyCollectionCount;
                public const int InitialLocalFrame = Runtime.FirstIndex;
                public const float InitialFrameTimer = 0f;
                public const bool InitialReverse = false;
                public const float NonPositiveDeltaThreshold = 0f;
                // Runaway guard: cap frame advances in one tick so pathological
                // (near-zero) frame durations cannot spin the advance loop.
                public const int MaxAdvancesPerTick = 1024;
                // Movement-driven animation-state seams. A state takes effect
                // only when an authored animation carries the matching Name;
                // otherwise the FIRST animation plays (single-clip atlases just
                // play their one clip). Attack is an available PlayAnimation
                // seam (no auto-revert - combat owns its cadence).
                public const string StateIdle = "idle";
                public const string StateWalk = "walk";
                public const string StateAttack = "attack";
                public const string DefaultState = StateIdle;
            }

            // Item #14 designer hitboxes to gameplay: LivingEntity syncs the
            // current facing + flipbook frame's Hurtbox record onto a
            // BoxCollider2D, reusing the pixel-to-local-units math hoisted into
            // EntityHitboxGeometry (the same math EFYVHitboxGizmo draws).
            public static class Hitbox
            {
                public const string HurtboxType = EFYVLabyrinthConfig.LabyMake.Hitbox.DefaultKeyHurtbox;
            }

            public static class Enemy
            {
                public const float BaseSpeedFallback = 4f;
                public const float DefaultExperienceValue = 10f;
            }

            public static class Boss
            {
                public const float BaseSpeedFallback = 3.0f;
                public const float DefaultPhase2HealthThreshold = 50f;
                public const bool InitialEnraged = false;
                public const bool Enraged = true;
                public const int PhaseOne = 1;
                public const int PhaseTwo = 2;
            }

            public static class EyeBearer
            {
                public const int SpawnCount = 2;
                public const float SpawnOffsetRadius = 0.5f;
                public const string TooltipEvilEyePrefab = "The prefab to spawn when this enemy dies. (Should be EvilEye)";
            }

            public static class Camera
            {
                public const float DefaultZoomLevel = 8f; // Orthographic size (higher is more zoomed out)
                public const float FOVWidthMultiplier = 1.2f; // Extend the FOV culling horizontally
                public const float FOVHeightMultiplier = 1.2f; // Extend the FOV culling vertically
                public const float CameraZOffset = -10f;
                public const float OrthographicExtentMultiplier = 2f;
            }

            public static class Map
            {
                public const int DefaultMapWidth = 1000;
                public const int DefaultMapHeight = 1000;

                // Map Switching Constants
                public const float MapTransitionDuration = 1.0f; // Seconds
                public const int MapTransitionMaxBlurRadius = 15;
                public const string DefaultMapId = "Default";

                // Sarcophage Probabilities (Cumulative)
                public const float SarcophageTeleportChance = 0.3f;      // 0.0 -> 0.3
                public const float SarcophageSpawnEnemyChance = 0.7f;    // 0.3 -> 0.7
                public const float SarcophageTrapChance = 0.9f;          // 0.7 -> 0.9
                public const int SarcophageAmbushCount = 5;
                public const float SarcophageAmbushRadius = 2f;
                public const float SarcophageTrapDamage = 50f;
                public const int SarcophageCurseCoinLoss = 200;

                // Map Logs
                public const string LogTargetMapIdEmpty = "[DoorProp] TargetMapId is empty! Cannot switch maps.";
                public const string LogSwitchingToMap = "[DoorProp] Interacting with door. Switching to map: {0}";
                public const string LogSarcophageTeleport = "[SarcophageProp] Triggered random teleport! Switching to map: {0}";
                public const string LogSarcophageAmbush = "[SarcophageProp] It was an ambush! Spawning Mummies!";
                public const string LogSarcophageTrap = "[SarcophageProp] It's a trap! Player takes massive damage!";
                public const string LogSarcophageCurse = "[SarcophageProp] A terrible curse steals session coins!";
                public const string LogMapManagerSwitchSuccess = "[MapManager] Seamlessly switched to Map: {0}";
                public const float DefaultCellSize = 1f;
                public const float TileZOffset = 0f;
                public const int PaddingCellsBackend = 2;
                public const int InclusiveBoundsCellCount = 1;
                public const int MinimumTileId = 0;
                public const int RandomTileMinIndex = Runtime.FirstIndex;

                public const bool InitialIsSwitching = false;
                public const bool Switching = true;
                public const bool NotSwitching = false;
                public const bool BlurEnabled = true;
                public const bool BlurDisabled = false;
                public const float InitialTransitionElapsed = 0f;
                public const float HalfTransitionMultiplier = 0.5f;
                public const int MinimumBlurRadius = 0;
                public const bool TextureMipmapsEnabled = false;
                public const int TexturePixelOrigin = 0;

                // Item #5 imported maps: MapViewportController.LoadMapData
                // loads a matching MapAssetData (produced by EFYVMapImporter
                // from a published .efyvmap) and only falls back to the
                // procedural noise map when no imported map exists.
                public const string LogImportedMapLoaded =
                    "[MapViewportController] Loaded imported map '{0}' ({1}x{2}).";
                public const string LogNoImportedMapFallback =
                    "[MapViewportController] No imported map for id '{0}'; using procedural fallback.";

                // Tooltips
                public const string TooltipImportedMaps =
                    "Imported map assets (.efyvmap via EFYVMapImporter) selectable by map id.";
                public const string TooltipDoorTargetMapId = "The ID of the map this door connects to.";
                public const string TooltipSarcophageMapIds = "List of possible random maps to teleport to.";
                public const string TooltipSarcophageTrapPrefab = "Enemy prefab to spawn for trap events.";
                public const string HeaderBackendBlur = "Backend Blur Integration";
                public const int InvalidBounds = -9999;
            }

            public static class EnvironmentData
            {
                public const float DefaultAnimationSpeed = 0.1f;
                // Floor for the per-frame animation interval: zero, negative, or NaN
                // speeds would thrash the frame every tick and invert the OnSpawn
                // timer randomization range.
                public const float MinimumAnimationSpeed = 0.01f;
                public const float DefaultXPGemValue = 10f;
                public const string LogChestOpened = "Player opened a treasure chest!";
                public const bool Blocking = true;
                public const bool NonBlocking = false;
                public const float PlanarZOffset = 0f;
                public const int GradeToSpriteIndexOffset = 1;

                public const float InitialAnimTimer = 0f;
                public const int InitialCurrentFrame = 0;
                public const int AnimationLoopStartFrame = 0;
                public const int UnregisteredListIndex = -1;
            }

            public static class Drops
            {
                public const int MaxCoinGrade = 5;
                public const int MaxChestGrade = 3;

                public const int EnemyMaxCoinGrade = 3;
                public const int MiniBossMinCoinGrade = 4;
                public const int MiniBossMaxCoinGrade = 5;
                public const int BossCoinGrade = 5;

                public const int EnemyChestGrade = 1;
                public const int MiniBossMinChestGrade = 1;
                public const int MiniBossMaxChestGrade = 2;
                public const int BossMinChestGrade = 2;
                public const int BossMaxChestGrade = 3;

                public const int ChestGrade1Rewards = 1;
                public const int ChestGrade2Rewards = 3;
                public const int ChestGrade3Rewards = 5;

                public const int Grade2 = 2;
                public const int Grade3 = 3;

                public const string LogCoinPickedUp = "Picked up a Grade {0} Coin! Value: {1}";
                public const string LogChestOpened = "Opened a Grade {0} Chest! You get {1} rewards!";

                public const int BaseCoinValue = 10;

                public const float BaseChestChance = 0.05f;
                public const float BaseCoinChance = 0.3f;
                // #24: XP gems in the drop table. Regular enemies roll this base
                // chance (scaled by the survival-time multiplier); bosses and
                // mini-bosses always drop one.
                public const float BaseXpGemChance = 0.35f;
                public const float GuaranteedDropChance = 1f;
                public const int StandardChestCount = 1;
                public const int BossMinChestCount = 1;
                public const int BossMaxChestCountExclusive = 4;
                public const int MinCoinGrade = 1;
                public const int MinChestGrade = 1;
            }

            public static class Spawner
            {
                public const float DefaultSpawnRadius = 15f;
                public const float DefaultBaseSpawnRate = 2f;
                public const float DefaultDifficultyMultiplier = 0.1f;
                public const float AccumulatorThreshold = 1f;
                public const int MaxSpawnsPerFrame = 64;
                public const float MaxAccumulatedSpawns = 256f;

                public const float InitialGameTimer = 0f;
                public const float InitialSpawnAccumulator = 0f;
                public const int RandomMinIndex = 0;
                public const float MinRadians = 0f;

                public const string HeaderReferences = "References";
                public const string TooltipEnemyPrefabs = "List of enemy prefabs available for spawning. (In a full game, this would be driven by a WaveScriptableObject)";
                public const string HeaderSettings = "Spawn Settings";
                public const string TooltipSpawnRadius = "Radius around the player where enemies spawn. Should be just outside the camera view.";
                public const string TooltipBaseSpawnRate = "How many enemies spawn per second at the very start of the run.";
                public const string TooltipDifficultyMultiplier = "How much the spawn rate increases per second of survival.";
            }

            public static class UI
            {
                public const string GameOverMessage = "Game Over!";
            }

            public static class System
            {
                public const float DefaultDynamicDropMultiplier = 1.0f;
                public const float SurvivalTimeMinuteSeconds = 60f;
                public const float DropChanceIncreasePerMinute = 0.05f;
            }

            public static class Save
            {
                public const string SaveFileName = "save.bin";
                public const float DirtySaveDebounceSeconds = 1f;

                // Logs
                public const string LogSaveSuccess = "Game saved successfully to {0}";
                public const string LogLoadSuccess = "Game loaded successfully.";
                public const string LogLoadFailNewProfile = "Save file not found, using default.";
                public const string LogMaxToonsReached = "[SaveManager] Max Toons ({0}) reached. Cannot add {1}.";
                public const string LogNoUnspentPoints = "[SaveManager] Toon '{0}' has no unspent stat points.";
                public const string LogNotEnoughLegacyCoins = "[SaveManager] Not enough global coins to buy legacy stat.";
                public const string LogBoughtLegacyUpgrade = "[SaveManager] Bought Legacy Upgrade for {0}. Remaining Coins: {1}";
            }

            public static class AI
            {
                public const float DefaultMultiplier = 1f;

                public const float IntensityMinuteDivider = 60f;
                public const float IntensityBaseMultiplier = 1f;
                public const float IntensityScalingFactor = 0.5f;

                public const float HealthMinuteDivider = 120f;
                public const float HealthBaseMultiplier = 1f;

                public const float SpeedMinuteDivider = 300f;
                public const float SpeedBaseMultiplier = 1f;
            }

            public static class Pool
            {
                public const int DefaultPoolCapacity = 500;
                // First key handed out by PoolManager.GetPoolKey. Keys are
                // manager-assigned per prefab reference (never engine instance
                // ids - Unity 6.6 EntityId is not int-convertible); 0 stays
                // reserved as Entity.EmptyPrefabPoolKey.
                public const int FirstPrefabPoolKey = 1;
                public const float ImmediateDespawnDelay = 0f;
                public const bool Active = true;
                public const bool Dormant = false;

                // ----------------------------------------------------------------
                // #32 prewarm targets: pools are populated up to these counts at
                // startup seams (FastPool.Prewarm is populate-up-to-target, so
                // repeated calls are idempotent) instead of paying Instantiate on
                // the first rent mid-combat.
                // ----------------------------------------------------------------
                // SpawnManager.Start, per enemy prefab.
                public const int EnemyPrewarmCount = 32;
                // DropManager.Awake, per drop prefab.
                public const int CoinPrewarmCount = 24;
                public const int ChestPrewarmCount = 8;
                public const int XpGemPrewarmCount = 24;
                // Weapon Awake paths through the static PoolManager.TryPrewarm /
                // TryPrewarmGameObject hooks (typed projectile prefabs and
                // splash/drop VFX prefabs).
                public const int ProjectilePrewarmCount = 16;
                public const int WeaponVfxPrewarmCount = 8;
            }

            public static class Importer
            {
                public const string ExtensionEFYV = EFYVLabyrinthConfig.Shared.EfyvExtension;
                public const string ExtensionAsset = "_Data.asset";
                public const string ExtensionPNG = EFYVLabyrinthConfig.Shared.PngExtension;
                public const string PathSeparator = "/";
                public const string SpriteSliceNameSeparator = "_";
                public const string SpriteSliceIndexFormat = "D8";
                public const float SpritePivotNormalized = 0.5f;

                public const string LogDetected = "[EFYV Importer] Detected new or modified art data: {0}";
                public const string LogSuccess = "[EFYV Importer] Successfully bridged {0} into Unity OOP system via FastImporter!";

                // Per-cause import failure messages (#16d). Every rejection names
                // its actual cause instead of the old single generic parse error.
                public const string LogErrorMalformed = "[EFYV Importer] Malformed " + EFYVLabyrinthConfig.Shared.EfyvExtension + " JSON in {0}.";
                public const string LogErrorMissingFile = "[EFYV Importer] Metadata file vanished before import: {0}.";
                public const string LogErrorUnsupportedDocumentVersion = "[EFYV Importer] Unsupported documentVersion {0} in {1} (supported: {2}).";
                public const string LogErrorMissingProperties = "[EFYV Importer] No properties object in {0}.";
                public const string LogErrorMissingIdentity = "[EFYV Importer] Rejected {0}: no entityName/assetName identity property.";
                public const string LogErrorUnsafeIdentity = "[EFYV Importer] Rejected {0}: identity '{1}' is not a safe file stem.";
                public const string LogErrorInvalidAtlas = "[EFYV Importer] Rejected {0}: invalid atlas metadata ({1}).";
                public const string LogErrorInvalidAttachments = "[EFYV Importer] Rejected {0}: invalid attachment record at index {1}.";
                public const string LogErrorInvalidTileset = "[EFYV Importer] Rejected {0}: invalid tileset manifest ({1}).";
                public const string LogErrorUnknownAssetType = "[EFYV Importer] Rejected {0}: unknown assetType '{1}' (no baseAssetType fallback).";
                public const string LogErrorExistingAssetTypeMismatch = "[EFYV Importer] Rejected {0}: existing asset is a {1}, expected {2}.";
                public const string LogWarningUnknownSchemaKeys = "[EFYV Importer] Unknown schema keys in {0} (kept in file, not mapped): {1}.";

                public const string KeyEntityName = EFYVLabyrinthConfig.Shared.EntityNameField;
                public const string KeyMaxHealth = EFYVLabyrinthConfig.Shared.MaxHealthField;
                public const string KeyBaseSpeed = EFYVLabyrinthConfig.Shared.BaseSpeedField;
                public const string KeyDamageToPlayer = EFYVLabyrinthConfig.Shared.DamageToPlayerField;
                public const string KeyExperienceValue = EFYVLabyrinthConfig.Shared.ExperienceValueField;
                public const string KeyPhase2HealthThreshold = EFYVLabyrinthConfig.Shared.Phase2HealthThresholdField;
                public const string KeyFacing = EFYVLabyrinthConfig.Shared.FacingField;
                public const string AssetTypeLivingEntityData = EFYVLabyrinthConfig.Shared.LivingEntityAssetType;
                public const string AssetTypeEnemyData = EFYVLabyrinthConfig.Shared.EnemyAssetType;
                public const string AssetTypeBossData = EFYVLabyrinthConfig.Shared.BossAssetType;

                public const string FacingUp = EFYVLabyrinthConfig.Shared.FacingUp;
                public const string FacingDown = EFYVLabyrinthConfig.Shared.FacingDown;
                public const string FacingLeft = EFYVLabyrinthConfig.Shared.FacingLeft;
                public const string FacingRight = EFYVLabyrinthConfig.Shared.FacingRight;
                public const string FacingNone = EFYVLabyrinthConfig.Shared.EmptyString;
                public const bool InitialIsNewAsset = false;
                public const bool IsNewAsset = true;
                public const int MaxTextureSize = EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasDimension;
                public const int MaxAtlasPixelCount = EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasPixelCount;
            }

            // Item #5: .efyvmap ingestion (EFYVMapImporter) into MapAssetData
            // assets and the imported-map runtime path in
            // MapViewportController.LoadMapData.
            public static class MapImporter
            {
                public const string ExtensionMap = EFYVLabyrinthConfig.Backend.MapFile.Extension;
                public const string ExtensionAsset = "_Map.asset";
                public const string LogImported = "[EFYV Map Importer] Imported map '{0}' ({1}x{2}, {3} props) from {4}.";
                public const string LogErrorMissingFile = "[EFYV Map Importer] Map file vanished before import: {0}.";
                public const string LogErrorMalformed = "[EFYV Map Importer] Malformed map file {0}.";
                public const string LogErrorUnsafeStem = "[EFYV Map Importer] Rejected {0}: stem '{1}' is not a safe map id.";
                public const string LogErrorExistingAssetTypeMismatch = "[EFYV Map Importer] Rejected {0}: existing asset at {1} is not a MapAssetData.";
                public const string LogWarningMissingTilesetSprites = "[EFYV Map Importer] Map '{0}' references tileset '{1}' but no sliced sprites were found next to the map file.";
            }

            // Live-transport RawArt watcher (#12): the editor-side poller that
            // notices published exports without waiting for Unity to regain focus.
            public static class RawArtWatcher
            {
                public const double PollIntervalSeconds = 0.25d;
                // A publish writes the PNG and the .efyvlaby back to back (plus
                // Unity's own .meta churn); the quiet window coalesces them into
                // one import batch.
                public const double DebounceSeconds = 0.3d;
                public const string LogImported = "[EFYV RawArt Watcher] Imported {0} changed file(s) from {1}.";
            }

            // Item #4: the game-side debug spawn palette + data-to-prefab
            // factory. Generic per-archetype template prefabs live under
            // TemplateDirectory; the factory picks the matching template for an
            // imported SchemaBackedAssetData, spawns it through PoolManager, and
            // binds it via LivingEntity/PropEntity LoadData (so the spawned
            // instance drives the item #13 flipbook + item #14 hurtbox). The
            // Play-Mode editor window lists the imported assets under
            // Assets/RawArt and one-clicks the selected one into the running
            // game, auto-offering the most recently imported/refreshed asset.
            public static class SpawnPalette
            {
                // Archetype template prefab asset paths (editor-loaded via
                // AssetDatabase). Scene-independent; one shared pool per prefab.
                public const string TemplateDirectory = "Assets/Prefabs/DebugTemplates";
                public const string TemplatePathEnemy = TemplateDirectory + "/Enemy.prefab";
                public const string TemplatePathBoss = TemplateDirectory + "/Boss.prefab";
                public const string TemplatePathProp = TemplateDirectory + "/Prop.prefab";

                // Where imported assets are discovered: the RawArt export root
                // the pixel-art importer writes <name>_Data.asset files into.
                public const string DiscoveryRoot =
                    EFYVLabyrinthConfig.LabyMake.Export.DirAssets + "/" +
                    EFYVLabyrinthConfig.LabyMake.Export.DirRawArt;
                public const string AssetSearchFilter = "t:SchemaBackedAssetData";

                // Player-relative spawn placement (the simpler of the two
                // options - no scene-view raycast): the selected asset spawns at
                // the player's position plus this offset, or at world origin
                // when no player exists in the scene yet.
                public const float DefaultSpawnOffsetX = 2f;
                public const float DefaultSpawnOffsetY = 0f;

                // Editor window chrome.
                public const string WindowTitle = "EFYV Spawn Palette";
                public const string MenuPath = "EFYV/Debug Spawn Palette";
                public const string SpawnButtonLabel = "Spawn Selected";
                public const string RefreshButtonLabel = "Refresh List";
                public const string HeaderImportedAssets = "Imported assets (Assets/RawArt)";
                public const string HelpEnterPlayMode = "Enter Play Mode to spawn imported assets into the running game.";
                public const string HelpNoAssets = "No imported assets found under Assets/RawArt. Push an export from the editor shell.";
                public const string HelpNoPool = "No PoolManager in the scene - spawning needs the running game.";
                public const string OffsetFieldLabel = "Spawn offset (from player)";
                public const string SelectedPrefix = "Selected: ";
                public const string ArchetypeSuffixFormat = "  [{0}]";
                public const string UnspawnableSuffix = "  (no archetype)";

                // Factory outcome log messages (per-cause, mirroring the
                // importer's per-cause rejection style).
                public const string LogSpawned = "[EFYV Spawn Palette] Spawned '{0}' as {1} archetype at ({2}, {3}).";
                public const string LogErrorUnknownArchetype = "[EFYV Spawn Palette] Cannot spawn '{0}': no archetype template matches asset type {1}.";
                public const string LogErrorNoTemplate = "[EFYV Spawn Palette] Cannot spawn '{0}': the {1} archetype template prefab is missing.";
                public const string LogErrorPoolEmpty = "[EFYV Spawn Palette] Could not spawn '{0}': the {1} pool is exhausted.";
                public const string LogErrorNoPool = "[EFYV Spawn Palette] Cannot spawn '{0}': no PoolManager in the scene.";

                // Archetype display names (window labels + log messages).
                public const string ArchetypeNameEnemy = "Enemy";
                public const string ArchetypeNameBoss = "Boss";
                public const string ArchetypeNameProp = "Prop";

                public const string UnnamedAsset = "(unnamed)";
            }

            public static class DataConfig
            {
                public const string AssetMenuFileName = "NewEntityData";
                public const string AssetMenuName = "EFYV/Entity Data";
                public const string HeaderGeneral = "General info";
                public const string HeaderArt = "Art & Collision";

                // Extracted from AssetDataHierarchy
                public const string HeaderMapSettings = "Map Settings";
                public const string HeaderPropSettings = "Prop Settings";
                public const string HeaderAnimationSettings = "Animation Settings";
                public const string HeaderGemSettings = "Gem Settings";
                public const string HeaderDirectionalSprites = "Directional Sprites";

                public static class SpecificEntities
                {
                    public const string MonsterEvilEye = "Evil Eye";
                    public const string MonsterEyeBearer = "Eye Bearer";
                    public const string MonsterSphinxKitten = "Sphinx Kitten";
                    public const string MonsterSphinxCat = "Sphinx Cat";
                    public const string MonsterBabyMummies = "Baby Mummies";
                    public const string MonsterFemaleMummy = "Female Mummy";
                    public const string MonsterMaleMummy = "Male Mummy";

                    public const string MiniBossTut = "Tut";
                    public const string MiniBossAnkhesenpaaten = "Ankhesenpaaten";
                    public const string MiniBossEyeOfProvidenceFake = "Eye of Providence (Fake relic)";

                    public const string BossEyeOfProvidenceReal = "Eye of Providence (Real relic)";
                    public const string BossPharaohAkhenaten = "Pharaoh Akhenaten";
                    public const string BossNefertiti = "Nefertiti";

                    public const string ObjectPyramids = "Pyramids";
                    public const string ObjectCactus = "Cactus";

                    public const string InteractablePyramidDoor = "Pyramid Door";
                    public const string InteractableClosedSarcophage = "Closed Sarcophage";
                }

                public const string MenuLegacyAchievementDb = "EFYV/Legacy Achievement Database";
                public const string FileNameLegacyAchievementDb = "LegacyAchievementDatabase";
            }

            public static class Progression
            {
                public const int CoinsPerToonLevel = 1000;
                public const int StatPointsPerLevel = 5;

                // Logs
                public const string LogToonLevelUp = "Toon '{0}' leveled up to {1}! Gained {2} Stat Points.";
                public const string LogToonStatUpgraded = "Toon '{0}' upgraded {1}. Remaining points: {2}.";

                public const int EmptyToonHash = 0;
                public const int InitialToonLevel = 1;
                public const int InitialToonCoins = 0;
                public const int InitialUnspentStatPoints = 0;
                public const float StatUpgradeAdditiveTenPercent = 0.1f;
                public const float StatUpgradeAdditiveFivePercent = 0.05f;
                public const float StatUpgradeMultiplicativeFivePercentReduction = 0.95f;
                public const float StatUpgradeFlatOne = 1.0f;
                public const int PositiveCoinThreshold = 0;
                public const int EmptyUnspentStatPoints = 0;
                public const int ToonStatPointCost = 1;
                public const int LegacyStatsOffset = 0;
                public const int LevelIncrement = 1;
                public const float DefaultMultiplier = 1.0f;
            }

            public static class Merchant
            {
                public const int BaseItemChoices = 4;
                public const float RollThresholdChicken = 0.33f;
                public const float RollThresholdPotion = 0.66f;

                // Hardcoded Items Default Data
                public const string ChickenName = "Floor Chicken";
                public const int ChickenCost = 50;
                public const float ChickenHeal = 30f;

                public const string PotionName = "Haste Potion";
                public const int PotionCost = 100;
                public const string PotionBuffId = "MoveSpeed";
                public const float PotionDuration = 10f;

                public const string MysteryWeaponName = "Mystery Weapon";
                public const int MysteryWeaponCost = 200;
                public const string MysteryWeaponId = "RandomWeapon";

                // Logs
                public const string LogMerchantEncountered = "Merchant Encountered! Generating {0} purchasable items.";
                public const string LogNotEnoughCoins = "Not enough session coins to purchase {0}. Costs: {1}, Have: {2}";
                public const string LogItemPurchased = "Purchased {0} for {1} coins.";
                public const string LogBuffApplied = "Applied temporary buff: {0} for {1} seconds.";
                public const string LogHealingApplied = "Healed player for {0} HP.";
                public const string LogMerchantInteract = "[BaseMerchantProp] Interacted with Merchant. {0} items available.";
                public const string LogWeaponUpgradeApplied = "[WeaponUpgradePurchase] Applied weapon/upgrade: {0}";
            }

            public static class Achievements
            {
                public const int MaxAchievements = 256;
                public const int BitsPerInt = 32;
                public const int MinimumId = 0;
                public const int LockedBitValue = 0;
                public const int BitMaskSeed = 1;
                public const int MissingDefinitionIndex = Runtime.NotFoundIndex;

                public const string LogUnlockedSuccess = "ACHIEVEMENT UNLOCKED: {0} - {1}";
                public const string LogUnlockedNoVisual = "ACHIEVEMENT UNLOCKED (ID: {0}), but no visual database was linked to print text!";

                public const string ContextMenuPopulateBasis = "Populate 30 Basis Achievements";
                public const string TooltipAchievementId = "The index (0-255) corresponding to the bit in FastSchemaBlock";
                public const string TooltipAchievementDatabase = "The database containing all achievement visual and text definitions.";

                // ----------------------------------------------------------------
                // Event-driven gameplay triggers (#34), consumed by
                // AchievementManager with O(1) threshold checks. The id/threshold
                // tables mirror the shipped basis database (BasisData below):
                // ids 0-4 are the kill ladder ("First Blood".."Labyrinth
                // Cleaner"), ids 18/19 are the survival pair ("Unstoppable"
                // 10 min, "Immortal" 30 min). Kill counts are per-session
                // (PlayerMetaSchema has no lifetime kill slot yet).
                // ----------------------------------------------------------------
                public static class Triggers
                {
                    public static readonly int[] KillThresholds = { 1, 100, 1000, 10000, 100000 };
                    public static readonly int[] KillAchievementIds = { 0, 1, 2, 3, 4 };
                    public static readonly float[] SurvivalThresholdSeconds = { 600f, 1800f };
                    public static readonly int[] SurvivalAchievementIds = { 18, 19 };
                }

                public static class BasisData
                {
                    public static readonly string[] Titles = new string[]
                    {
                        "First Blood", "Slayer", "Executioner", "Monster Hunter", "Labyrinth Cleaner",
                        "First Steps", "Explorer", "Adventurer", "Cartographer", "Labyrinth Walker",
                        "Pocket Change", "Wealthy", "Millionaire", "Hoarder", "Treasure Hunter",
                        "First Death", "Try Again", "Persistent", "Unstoppable", "Immortal",
                        "Novice Caster", "Apprentice", "Adept", "Master", "Archmage",
                        "Close Call", "Untouchable", "Survivor", "Flawless Victory", "Godlike"
                    };

                    public static readonly string[] Descriptions = new string[]
                    {
                        "Kill your first monster.", "Kill 100 monsters.", "Kill 1,000 monsters.", "Kill 10,000 monsters.", "Kill 100,000 monsters.",
                        "Complete your first room.", "Clear 10 rooms.", "Clear 50 rooms.", "Clear 100 rooms.", "Clear 500 rooms.",
                        "Collect 100 coins.", "Collect 1,000 coins.", "Collect 10,000 coins.", "Collect 100,000 coins.", "Open 50 chests.",
                        "Die for the first time.", "Die 10 times.", "Die 50 times.", "Survive for 10 minutes.", "Survive for 30 minutes.",
                        "Use a spell.", "Evolve your first weapon.", "Evolve 3 weapons in one run.", "Evolve 6 weapons in one run.", "Deal 1,000,000 total magic damage.",
                        "Survive with less than 5% health.", "Complete a room without taking damage.", "Reach level 10.", "Defeat a boss without taking damage.", "Reach level 100."
                    };
                }
            }
        }

        public static class LabyMake
        {
            public static class Common
            {
                public const int FirstIndex = EFYVLabyrinthConfig.Shared.FirstIndex;
                public const int EmptyCount = EFYVLabyrinthConfig.Shared.EmptyCount;
                public const int UnitCount = EFYVLabyrinthConfig.Shared.UnitStep;
                public const int NotFoundIndex = EFYVLabyrinthConfig.Shared.NotFoundIndex;
                public const float ZeroFloat = EFYVLabyrinthConfig.Shared.NormalizedMin;
                public const float UnitScale = EFYVLabyrinthConfig.Shared.NormalizedMax;
                public const string EmptyString = EFYVLabyrinthConfig.Shared.EmptyString;
            }

            public static class Layout
            {
                public const int StructPack = EFYVLabyrinthConfig.Shared.SequentialStructPack;
            }

            public static class Canvas
            {
                public const int DefaultWidth = 64;
                public const int DefaultHeight = 64;
                public const int MinCoordinate = Common.FirstIndex;
                public const int AnchorCenterDivisor = 2;
            }

            public static class Color
            {
                public const int BitsPerByte = 8;
                public const int RgbaChannelCount = 4;
                public const int RedByteOffset = Common.FirstIndex;
                public const int GreenByteOffset = GreenShift / BitsPerByte;
                public const int BlueByteOffset = BlueShift / BitsPerByte;
                public const int AlphaByteOffset = AlphaShift / BitsPerByte;
                public const uint ChannelMask = 0xFFu;
                public const uint ClearRedMask = 0xFFFFFF00u;
                public const uint ClearGreenMask = 0xFFFF00FFu;
                public const uint ClearBlueMask = 0xFF00FFFFu;
                public const uint ClearAlphaMask = 0x00FFFFFFu;
                public const int GreenShift = EFYVLabyrinthConfig.Shared.RgbaGreenShift;
                public const int BlueShift = EFYVLabyrinthConfig.Shared.RgbaBlueShift;
                public const int AlphaShift = EFYVLabyrinthConfig.Shared.RgbaAlphaShift;
                public const uint TransparentPixelRgba = EFYVLabyrinthConfig.Shared.TransparentRgba;
                public const uint DefaultBrushRgba = 0xFF000000u;
            }

            public static class Schema
            {
                public readonly struct EditorMetadata
                {
                    public string Label { get; }
                    public object DefaultValue { get; }
                    public bool HasRange { get; }
                    public double Minimum { get; }
                    public double Maximum { get; }
                    public double Step { get; }
                    public bool IsRequired { get; }
                    public bool IsReadOnly { get; }
                    public string[] Choices { get; }

                    private EditorMetadata(
                        string label,
                        object defaultValue,
                        bool hasRange,
                        double minimum,
                        double maximum,
                        double step,
                        bool isRequired,
                        bool isReadOnly,
                        string[] choices)
                    {
                        Label = label;
                        DefaultValue = defaultValue;
                        HasRange = hasRange;
                        Minimum = minimum;
                        Maximum = maximum;
                        Step = step;
                        IsRequired = isRequired;
                        IsReadOnly = isReadOnly;
                        Choices = choices;
                    }

                    public static EditorMetadata Text(string label, string defaultValue, bool isRequired)
                    {
                        return new EditorMetadata(label, defaultValue, false, 0d, 0d, 0d, isRequired, false, null);
                    }

                    public static EditorMetadata Number(
                        string label,
                        object defaultValue,
                        double minimum,
                        double maximum,
                        double step,
                        bool isRequired)
                    {
                        return new EditorMetadata(label, defaultValue, true, minimum, maximum, step, isRequired, false, null);
                    }

                    public static EditorMetadata Choice(
                        string label,
                        string defaultValue,
                        bool isRequired,
                        string[] choices)
                    {
                        return new EditorMetadata(label, defaultValue, false, 0d, 0d, 0d, isRequired, false, choices);
                    }
                }

                public readonly struct FieldDefinition
                {
                    public string Name { get; }
                    public string FieldType { get; }
                    public EditorMetadata Editor { get; }

                    public FieldDefinition(string name, string fieldType, EditorMetadata editor)
                    {
                        Name = name;
                        FieldType = fieldType;
                        Editor = editor;
                    }
                }

                public readonly struct AssetDefinition
                {
                    public string AssetType { get; }
                    public string DisplayName { get; }
                    public bool IsDirectional { get; }
                    public bool UsesLivingEntityFields { get; }
                    public bool IncludesEnemyFields { get; }
                    public bool IncludesBossFields { get; }

                    public AssetDefinition(
                        string assetType,
                        string displayName,
                        bool isDirectional,
                        bool usesLivingEntityFields,
                        bool includesEnemyFields,
                        bool includesBossFields)
                    {
                        AssetType = assetType;
                        DisplayName = displayName;
                        IsDirectional = isDirectional;
                        UsesLivingEntityFields = usesLivingEntityFields;
                        IncludesEnemyFields = includesEnemyFields;
                        IncludesBossFields = includesBossFields;
                    }
                }

                public readonly struct AssetRegistration
                {
                    public string AssetType { get; }
                    public string DisplayName { get; }
                    public string BaseAssetType { get; }

                    public AssetRegistration(string assetType, string displayName, string baseAssetType)
                    {
                        AssetType = assetType;
                        DisplayName = displayName;
                        BaseAssetType = baseAssetType;
                    }
                }

                public const int AdditionalEnemyFieldCount = 2;
                public const int AdditionalBossFieldCount = EFYVLabyrinthConfig.Shared.UnitStep;

                // Item #33 runtime-extensible asset fields: caps for custom
                // fields registered through AssetSchemaService.RegisterAssetType
                // (name + slot kind, no backend config edit). The values ride
                // the .efyvlaby properties object as ordinary keys, so the name
                // cap keeps them log- and JSON-friendly.
                public const int MaxCustomFieldNameLength = 64;
                public const int MaxCustomFieldsPerType = 32;

                public static readonly string[] FacingChoices =
                {
                    EFYVLabyrinthConfig.Shared.FacingUp,
                    EFYVLabyrinthConfig.Shared.FacingDown,
                    EFYVLabyrinthConfig.Shared.FacingLeft,
                    EFYVLabyrinthConfig.Shared.FacingRight
                };

                public static readonly FieldDefinition[] LivingEntityFields =
                {
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.EntityNameField,
                        Types.StringUpper,
                        EditorMetadata.Text("Entity Name", Types.DefaultString, true)),
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.MaxHealthField,
                        Types.FloatSingle,
                        EditorMetadata.Number("Max Health", EFYVLabyrinthConfig.Game.Player.DefaultMaxHealth, Common.ZeroFloat, float.MaxValue, Common.UnitScale, true)),
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.BaseSpeedField,
                        Types.FloatSingle,
                        EditorMetadata.Number("Base Speed", EFYVLabyrinthConfig.Game.Player.DefaultBaseSpeed, Common.ZeroFloat, float.MaxValue, 0.1d, true)),
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.FacingField,
                        Types.StringUpper,
                        EditorMetadata.Choice("Facing", EFYVLabyrinthConfig.Shared.FacingDown, true, FacingChoices))
                };

                // Prop/weapon gameplay numbers (#15): optional designer fields on the
                // GameAssetData family, wired end to end through the shared
                // Shared.AssetSchemaFieldManifest into AssetSchema block slots.
                public static readonly FieldDefinition BaseDamageFieldDefinition =
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.BaseDamageField,
                        Types.FloatSingle,
                        EditorMetadata.Number("Base Damage", Types.DefaultFloat, Common.ZeroFloat, float.MaxValue, Common.UnitScale, false));

                public static readonly FieldDefinition CooldownTimerFieldDefinition =
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.CooldownTimerField,
                        Types.FloatSingle,
                        EditorMetadata.Number("Cooldown Timer", Types.DefaultFloat, Common.ZeroFloat, float.MaxValue, 0.1d, false));

                public static readonly FieldDefinition IsWalkableFieldDefinition =
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.IsWalkableField,
                        Types.Int32,
                        EditorMetadata.Number("Is Walkable (0/1)", DefaultIsWalkable, Common.ZeroFloat, Common.UnitScale, Common.UnitScale, false));

                public static readonly FieldDefinition TrapDamageFieldDefinition =
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.TrapDamageField,
                        Types.FloatSingle,
                        EditorMetadata.Number("Trap Damage", Types.DefaultFloat, Common.ZeroFloat, float.MaxValue, Common.UnitScale, false));

                // Newly authored props default to walkable, matching the runtime
                // default (props are non-blocking unless a designer opts in).
                public const int DefaultIsWalkable = EFYVLabyrinthConfig.Shared.UnitStep;

                public static readonly FieldDefinition[] GameAssetFields =
                {
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.AssetNameField,
                        Types.StringUpper,
                        EditorMetadata.Text("Asset Name", Types.DefaultString, true)),
                    BaseDamageFieldDefinition,
                    CooldownTimerFieldDefinition,
                    IsWalkableFieldDefinition,
                    TrapDamageFieldDefinition
                };

                public static readonly FieldDefinition DamageToPlayerField =
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.DamageToPlayerField,
                        Types.FloatSingle,
                        EditorMetadata.Number("Damage To Player", Types.DefaultFloat, Common.ZeroFloat, float.MaxValue, Common.UnitScale, true));

                public static readonly FieldDefinition ExperienceValueField =
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.ExperienceValueField,
                        Types.FloatSingle,
                        EditorMetadata.Number("Experience Value", EFYVLabyrinthConfig.Game.Enemy.DefaultExperienceValue, Common.ZeroFloat, float.MaxValue, Common.UnitScale, true));

                public static readonly FieldDefinition Phase2HealthThresholdField =
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.Phase2HealthThresholdField,
                        Types.FloatSingle,
                        EditorMetadata.Number("Phase 2 Health Threshold", EFYVLabyrinthConfig.Game.Boss.DefaultPhase2HealthThreshold, Common.ZeroFloat, float.MaxValue, Common.UnitScale, true));

                public static readonly FieldDefinition[] EnemyFields =
                {
                    DamageToPlayerField,
                    ExperienceValueField
                };

                public static readonly FieldDefinition[] BossFields =
                {
                    Phase2HealthThresholdField
                };

                public static readonly AssetDefinition[] AssetDefinitions =
                {
                    new AssetDefinition(Types.AssetTypeGameAssetData, EFYVLabyrinthConfig.Shared.GameAssetDisplayName, false, false, false, false),
                    new AssetDefinition(Types.AssetTypeLivingEntityData, EFYVLabyrinthConfig.Shared.LivingEntityDisplayName, true, true, false, false),
                    new AssetDefinition(Types.AssetTypeEnemyData, EFYVLabyrinthConfig.Shared.EnemyDisplayName, true, true, true, false),
                    new AssetDefinition(Types.AssetTypeBossData, EFYVLabyrinthConfig.Shared.BossDisplayName, true, true, true, true)
                };

                public static readonly AssetRegistration[] BuiltInAssetRegistrations =
                {
                    new AssetRegistration("EvilEyeData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MonsterEvilEye, Types.AssetTypeEnemyData),
                    new AssetRegistration("EyeBearerData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MonsterEyeBearer, Types.AssetTypeEnemyData),
                    new AssetRegistration("SphinxKittenData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MonsterSphinxKitten, Types.AssetTypeEnemyData),
                    new AssetRegistration("SphinxCatData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MonsterSphinxCat, Types.AssetTypeEnemyData),
                    new AssetRegistration("BabyMummiesData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MonsterBabyMummies, Types.AssetTypeEnemyData),
                    new AssetRegistration("FemaleMummyData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MonsterFemaleMummy, Types.AssetTypeEnemyData),
                    new AssetRegistration("MaleMummyData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MonsterMaleMummy, Types.AssetTypeEnemyData),
                    new AssetRegistration("TutData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MiniBossTut, Types.AssetTypeBossData),
                    new AssetRegistration("AnkhesenpaatenData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MiniBossAnkhesenpaaten, Types.AssetTypeBossData),
                    new AssetRegistration("EyeOfProvidenceFakeData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.MiniBossEyeOfProvidenceFake, Types.AssetTypeBossData),
                    new AssetRegistration("EyeOfProvidenceRealData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.BossEyeOfProvidenceReal, Types.AssetTypeBossData),
                    new AssetRegistration("PharaohAkhenatenData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.BossPharaohAkhenaten, Types.AssetTypeBossData),
                    new AssetRegistration("NefertitiData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.BossNefertiti, Types.AssetTypeBossData),
                    new AssetRegistration("PyramidsData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.ObjectPyramids, Types.AssetTypeGameAssetData),
                    new AssetRegistration("CactusData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.ObjectCactus, Types.AssetTypeGameAssetData),
                    new AssetRegistration("PyramidDoorData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.InteractablePyramidDoor, Types.AssetTypeGameAssetData),
                    new AssetRegistration("ClosedSarcophageData", EFYVLabyrinthConfig.Game.DataConfig.SpecificEntities.InteractableClosedSarcophage, Types.AssetTypeGameAssetData)
                };
            }

            public static class Viewport
            {
                public const float MinZoom = 0.5f;
                public const float MaxZoom = 20f;
                public const float ZoomStep = 0.2f;
                public const float DefaultZoomLevel = Common.UnitScale;
                public const int DefaultOffsetX = Canvas.MinCoordinate;
                public const int DefaultOffsetY = Canvas.MinCoordinate;
                public const float NeutralScrollDelta = Common.ZeroFloat;
            }

            // Item #31 viewport designer overlays: host-agnostic defaults for
            // the optional overlay passes of ViewportController's overlay
            // RenderToScreenBuffer overloads. Colors are straight RGBA with
            // red in the low byte (the PixelColor layout).
            public static class Overlay
            {
                // Checkerboard transparency background (screen-anchored cells
                // of edge 1 << CellShift, composited UNDER the canvas content
                // inside the canvas area only). Moved into core from the
                // Avalonia shell so every UI host renders the same backdrop.
                public const uint CheckerLightRgba = 0xFF808080u;
                public const uint CheckerDarkRgba = 0xFF5A5A5Au;
                public const int DefaultCheckerCellShift = 3;
                public const int MinCheckerCellShift = Common.EmptyCount;
                public const int MaxCheckerCellShift = 15;
                // Pixel grid: 1-screen-pixel lines on canvas pixel boundaries,
                // drawn only at zoom >= the threshold (fine grids are noise at
                // low zoom). White at ~31% alpha reads on light and dark art.
                public const float DefaultPixelGridMinZoom = 4f;
                public const uint PixelGridLineRgba = 0x50FFFFFFu;
                // Tile grid: TileSize-cell boundaries when a tileset/map
                // context is active (TileGridOverlayConfig.TileSize > 0).
                // Amber at ~63% alpha, drawn over the pixel grid.
                public const uint TileGridLineRgba = 0xA000D8FFu;
                public const int InactiveTileSize = Common.EmptyCount;
                // Attachment outlines (placed sub-element bounds) + pivot
                // markers (sub-element pivot anchors and the optional
                // host-supplied explicit pivot).
                public const uint AttachmentOutlineRgba = 0xFFFF00FFu;
                public const uint PivotMarkerRgba = 0xFF00A8FFu;
                public const int DefaultPivotMarkerRadius = 3;
                public const int MaxPivotMarkerRadius = 64;
            }

            public static class Types
            {
                public const string AssetTypeGameAssetData = EFYVLabyrinthConfig.Shared.GameAssetAssetType;
                public const string AssetTypeEnemyData = EFYVLabyrinthConfig.Shared.EnemyAssetType;
                public const string AssetTypeBossData = EFYVLabyrinthConfig.Shared.BossAssetType;
                public const string AssetTypeLivingEntityData = EFYVLabyrinthConfig.Shared.LivingEntityAssetType;
                public const string FloatSingle = "Single";
                public const string FloatLower = "float";
                public const string Int32 = "Int32";
                public const string IntLower = "int";
                public const string StringUpper = "String";
                public const string StringLower = "string";
                public const float DefaultFloat = Common.ZeroFloat;
                public const int DefaultInt = Common.EmptyCount;
                public const string DefaultString = Common.EmptyString;
            }

            public static class Export
            {
                public const string DirAssets = "Assets";
                public const string DirRawArt = "RawArt";
                public const string ExtensionEfyvSub = ".efyvsub";
                public const string WildcardEfyvSub = "*.efyvsub";
                // Item #6: per-project sub-element bank directory used by the
                // editor shell (created next to the project files).
                public const string AssetBankDirectoryName = "AssetBank";
                public const int InitialFrameCount = Common.EmptyCount;
                public const int InitialFrameIndex = Common.FirstIndex;
                public const float SubElementScale = Common.UnitScale;
                public const int MaxAtlasDimension = 16384;
                public const int MaxAtlasPixelCount = 67108864;
                // Item #5 tile-sheet export: the single atlas animation the
                // tileset .efyvlaby declares so Unity slices exactly tileCount
                // sprites (fps is meaningless for a tile sheet but must be
                // positive per the shared atlas contract).
                public const string TilesetAnimationName = "Tiles";
                public const int TilesetAnimationFps = 1;
            }

            // Item #5 designer-side tileset section: a grid of N tiles at
            // TileSize, each authored as a mini-frame of raw RGBA pixels. The
            // tile at list index i maps to FastGridMap short tile id i.
            public static class Tileset
            {
                public const string DefaultName = "Tileset";
                public const int MinTileSize = Tool.TileMaker.MinTileSize;
                public const int MaxTileSize = Tool.TileMaker.MaxTileSize;
                public const int DefaultTileSize = Tool.TileMaker.DefaultTileSize;
                public const int MaxTiles = EFYVLabyrinthConfig.Backend.Exporter.MaxTilesPerTileset;
                public const int MaxTileNameLength = EFYVLabyrinthConfig.Backend.Exporter.MaxTileNameLength;
            }

            // Item #5 designer-side map section: a short[] tile grid + prop
            // placements + tileset reference, persisted in .efyvmake and
            // exported as a versioned .efyvmap binary. The caps alias the
            // Backend.MapFile wire limits so the designer model, .efyvmake
            // persistence, and the binary export enforce one contract.
            public static class MapDocument
            {
                public const string DefaultMapId = EFYVLabyrinthConfig.Game.Map.DefaultMapId;
                public const int MaxDimension = EFYVLabyrinthConfig.Backend.MapFile.MaxMapDimension;
                public const int MaxProps = EFYVLabyrinthConfig.Backend.MapFile.MaxMapProps;
                public const short BlankTileId = EFYVLabyrinthConfig.Backend.MapFile.BlankTileId;
            }

            public static class Persistence
            {
                public const int ProjectFormatVersion = EFYVLabyrinthConfig.Shared.UnitStep;
                // Deliberately its own constant (#16a): a project-format bump must
                // not silently invalidate every persisted .efyvsub bank.
                // Item #6 made the split a REAL version scheme: version 2 appended
                // the pivot + default-transform header fields, and readers accept
                // the whole range [MinSupportedSubElementFormatVersion ..
                // SubElementFormatVersion] instead of pinning one value - a
                // version-1 bank file loads with the default pivot/transform.
                public const int SubElementFormatVersion = 2;
                public const int MinSupportedSubElementFormatVersion = 1;
                // .efyvsub v2 flip flag bits (any other bit set is corrupt data).
                public const byte SubElementFlagFlipX = 1;
                public const byte SubElementFlagFlipY = 2;
                public const string ProjectExtension = ".efyvmake";
                public const string AutosaveSuffix = ".autosave";
                public const int DefaultAutosaveDebounceMilliseconds = 1000;
                public const bool DefaultAutosaveEnabled = true;
                public const int MaxCanvasDimension = 4096;
                public const int MaxAnimations = 256;
                public const int MaxFramesPerAnimation = 4096;
                public const int MaxLayersPerFrame = 256;
                public const long MaxProjectFileBytes = 268435456L;
                public const int MaxSubElementDimension = MaxCanvasDimension;
                public const int MaxSubElementPixelCount = 16777216;
            }

            public static class LiveDebug
            {
                public const int DefaultDebounceMilliseconds = 250;
            }

            public static class Animation
            {
                public const int DefaultFPS = 12;
                public const int WalkDefaultFPS = 12;
                public const int JitterDefaultFPS = 15;
                public const string WalkAnimName = "Walk_Procedural";
                public const string JitterAnimName = "Jitter_Procedural";
                public const bool DefaultPreviewLoop = true;

                // Item #10 animation workflow depth.
                // Per-frame durations: 0 is the "inherit the animation FPS"
                // sentinel everywhere (model, .efyvmake, .efyvlaby atlas); a
                // positive value overrides that one frame's display time in
                // milliseconds, bounded by the shared wire cap below.
                public const int InheritFrameDurationMs =
                    EFYVLabyrinthConfig.Backend.Exporter.InheritFrameDurationMs;
                public const int MinFrameDurationMs = EFYVLabyrinthConfig.Shared.UnitStep;
                public const int MaxFrameDurationMs =
                    EFYVLabyrinthConfig.Backend.Exporter.MaxFrameDurationMs;
                // Loop-range/ping-pong playback tags: LoopEnd of -1 means "the
                // last frame" so appended frames extend the loop automatically.
                public const int DefaultLoopStartFrame = Common.FirstIndex;
                public const int FullRangeLoopEnd = Common.NotFoundIndex;
                public const bool DefaultPingPong = false;
                // Layer-preserving generators write onto (and regenerate only)
                // the layer with this name, leaving manual touch-up layers alone.
                public const string GeneratedLayerName = "Generated";
                public const string BobAnimName = "Bob_Procedural";
                public const string ShakeAnimName = "HitFlash_Procedural";
                public const int BobDefaultFPS = 10;
                public const int ShakeDefaultFPS = 20;
            }

            // Item #10 onion skinning: host-agnostic ghost-frame compositing
            // defaults (ViewportController.ComposeOnionSkin).
            public static class OnionSkin
            {
                public const int MaxNeighborFrames = 8;
                public const int DefaultPreviousFrames = 1;
                public const int DefaultNextFrames = 1;
                public const float DefaultPreviousAlpha = 0.35f;
                public const float DefaultNextAlpha = 0.2f;
                public const float DefaultAlphaFalloff = 0.6f;
            }

            public static class Frame
            {
                public const int DefaultIndex = Common.FirstIndex;
            }

            public static class Hitbox
            {
                public const string DefaultKeyHurtbox = "Hurtbox";
                public const float PixelsPerUnit = EFYVLabyrinthConfig.Shared.PixelsPerUnit;
            }

            public static class Layer
            {
                public const string DefaultName = "Layer 1";
                public const string CopySuffix = "_Copy";
                public const bool DefaultVisibility = true;
                public const float DefaultOpacity = Common.UnitScale;
                public const int TransparentAlpha = EFYVLabyrinthConfig.Shared.TransparentAlpha;
            }

            // Item #8 palette-and-color workflow: named palettes with ordered
            // swatches plus a most-recent-first ring of recently used colors,
            // both persisted in .efyvmake (optional document section - legacy
            // documents without it restore to empty palette state).
            public static class Palette
            {
                public const int MaxPalettes = 64;
                public const int MaxSwatchesPerPalette = 256;
                public const int MaxNameLength = 64;
                public const int RecentColorCapacity = 16;
                public const int DefaultActivePaletteIndex = Common.FirstIndex;
            }

            // Item #6 sub-element attachments: per-frame records placing a
            // bank sub-element (by name) on the canvas. The wire caps alias
            // the Backend.Exporter constants so the designer model, .efyvmake
            // persistence, and the .efyvlaby exporter enforce one contract.
            public static class Attachment
            {
                public const int MaxPerFrame = EFYVLabyrinthConfig.Backend.Exporter.MaxAttachmentsPerFrame;
                public const int MinZOrder = EFYVLabyrinthConfig.Backend.Exporter.MinAttachmentZOrder;
                public const int MaxZOrder = EFYVLabyrinthConfig.Backend.Exporter.MaxAttachmentZOrder;
                public const int DefaultZOrder = Common.EmptyCount;
                // Stamp tool grab tolerance (canvas pixels, per axis) when
                // picking up an existing attachment to reposition it.
                public const int GrabRadius = 4;
            }

            public static class Tool
            {
                public const int DefaultLayerIndex = Common.FirstIndex;
                public const int InvalidCoordinate = Common.NotFoundIndex;
                public const int MaxBrushSize = 256;

                public static class Pencil
                {
                    public const int DefaultBrushSize = 1;
                    public const int MinThickBrushSize = 1;
                    public const global::EFYVBackend.Core.Math.Algorithms.BrushShape DefaultBrushShape = global::EFYVBackend.Core.Math.Algorithms.BrushShape.Circle;
                }

                public static class Stamp
                {
                    public const int CenterDivisorPower = 1;
                }

                public static class Eraser
                {
                    public const int DefaultBrushSize = Pencil.DefaultBrushSize;
                    public const int MinThickBrushSize = Pencil.MinThickBrushSize;
                    public const uint EraseRgba = EFYVLabyrinthConfig.Shared.TransparentRgba;
                }

                public static class Shape
                {
                    public const int DefaultThickness = Pencil.DefaultBrushSize;
                    public const int MaxThickness = MaxBrushSize;
                    public const bool DefaultFilled = false;
                }

                public static class Selection
                {
                    public const int MaxLassoPoints = 4096;
                    public const int MinPolygonPoints = 3;
                }

                public static class Symmetry
                {
                    public const int VariantCount = 4;
                    public const int FlipXBit = 1;
                    public const int FlipYBit = 2;
                }

                public static class Moving
                {
                    public const int ModeToonWalk = 0;
                    public const int ModeElementJitter = 1;
                    // Item #10 movement presets beyond ToonWalk/ElementJitter.
                    public const int ModeBobBreathe = 2;
                    public const int ModeShakeHitFlash = 3;
                    public const int DefaultWalkSplitY = 32;
                    public const float DefaultWalkBounceAmp = 2f;
                    public const float DefaultWalkStrideAmp = 4f;
                    public const int DefaultWalkFrameCount = 8;
                    public const int JitterOctantCount = EFYVLabyrinthConfig.Shared.DirectionOctantCount;
                    public const float DefaultJitterAmp = 1.5f;
                    public const float DefaultJitterFreq = Common.UnitScale;
                    public const int DefaultJitterFrameCount = 8;
                    public const float DefaultBobAmp = 1.5f;
                    public const float DefaultBreatheAmp = 0.05f;
                    public const int DefaultBobFrameCount = 8;
                    public const float DefaultShakeAmp = 2f;
                    public const float DefaultFlashStrength = 0.6f;
                    public const int DefaultShakeFrameCount = 6;
                }

                public static class TileMaker
                {
                    public const int DefaultTileSize = 32;
                    public const int MinTileSize = 8;
                    public const int MaxTileSize = 128;
                }

                public static class Map
                {
                    public const uint DefaultSeed = EFYVLabyrinthConfig.Backend.Random.DefaultSeed;
                    public const float DefaultScatterRadius = 5f;
                    public const int DefaultScatterDensity = 3;
                    public const int MaxScatterDensity = 4096;
                    public const float MinScaleJitter = 0.8f;
                    public const float MaxScaleJitter = 1.2f;
                    public const float DefaultFillProbability = 0.45f;
                    public const short DefaultBaseTileId = 0;
                    public const short DefaultTargetTileId = 1;
                    public const float DefaultObjectScale = Common.UnitScale;
                    public const string ModeScatter = "Scatter";
                    public const string ModeTile = "Tile";
                    public const string ModeNoiseFill = "NoiseFill";
                    public const string ModeAutomataSmooth = "AutomataSmooth";
                }
            }

            public static class Command
            {
                public const int EmptyStackCount = Common.EmptyCount;
                public const int DefaultHistoryCapacity = 100;
                public const long DefaultHistoryByteCapacity = 67108864L;
                public const int EstimatedCommandOverheadBytes = 256;
            }

            // Item #7 destructive layer filters: session-level parameter caps
            // and the host-facing filter catalog labels (ToolbarAPI).
            public static class Filter
            {
                public const int MinBlurRadius = EFYVLabyrinthConfig.Shared.UnitStep;
                public const int MaxBlurRadius = 64;
                // Glow radius 0 is legal: it leaves a hard 1px rim behind the
                // sprite instead of a soft halo.
                public const int MinGlowRadius = Common.EmptyCount;
                public const int MaxGlowRadius = MaxBlurRadius;
                // Saturation/value deltas live on the normalized channel scale.
                public const float MinColorComponentDelta = -Common.UnitScale;
                public const float MaxColorComponentDelta = Common.UnitScale;
                public const string LabelBlur = "Blur";
                public const string LabelOutline = "Outline";
                public const string LabelGlow = "Glow";
                public const string LabelColorShift = "Color Shift";
            }

            // Item #7 runtime-effect descriptor authoring (designer model
            // caps; the wire-format value bounds live in Backend.Exporter).
            public static class Effect
            {
                public const int MaxEffectsPerAnimation =
                    EFYVLabyrinthConfig.Backend.Exporter.MaxEffectsPerAnimation;
                public const int MinDurationMs =
                    EFYVLabyrinthConfig.Backend.Exporter.MinEffectDurationMs;
                public const int MaxDurationMs =
                    EFYVLabyrinthConfig.Backend.Exporter.MaxEffectDurationMs;
                public const float MinStrength =
                    EFYVLabyrinthConfig.Backend.Exporter.MinEffectStrength;
                public const float MaxStrength =
                    EFYVLabyrinthConfig.Backend.Exporter.MaxEffectStrength;
                public const int MaxNameLength = 64;
                public const int MaxTriggerLength = 64;
                public const string TypeFlash = EFYVLabyrinthConfig.Backend.Exporter.EffectTypeFlash;
                public const string TypeTint = EFYVLabyrinthConfig.Backend.Exporter.EffectTypeTint;
                public const string TypeParticleHook =
                    EFYVLabyrinthConfig.Backend.Exporter.EffectTypeParticleHook;
                public const string TriggerOnSpawn = EFYVLabyrinthConfig.Shared.EffectTriggerOnSpawn;
                public const string TriggerOnDamaged = EFYVLabyrinthConfig.Shared.EffectTriggerOnDamaged;
            }

            public static class Entity
            {
                public const int DirectionalVariantCount = 4;
                public const string KeyFacing = EFYVLabyrinthConfig.Shared.FacingField;
                public const string SuffixUp = " (Up)";
                public const string SuffixDown = " (Down)";
                public const string SuffixLeft = " (Left)";
                public const string SuffixRight = " (Right)";
                public const string FacingUp = EFYVLabyrinthConfig.Shared.FacingUp;
                public const string FacingDown = EFYVLabyrinthConfig.Shared.FacingDown;
                public const string FacingLeft = EFYVLabyrinthConfig.Shared.FacingLeft;
                public const string FacingRight = EFYVLabyrinthConfig.Shared.FacingRight;
                public const string FacingNone = Common.EmptyString;
                public const string FileSuffixUp = EFYVLabyrinthConfig.Shared.FacingFileSuffixUp;
                public const string FileSuffixDown = EFYVLabyrinthConfig.Shared.FacingFileSuffixDown;
                public const string FileSuffixLeft = EFYVLabyrinthConfig.Shared.FacingFileSuffixLeft;
                public const string FileSuffixRight = EFYVLabyrinthConfig.Shared.FacingFileSuffixRight;
                // Item #33 linked directional authoring: the facing a linked
                // 4-direction project starts on (matches Game.Entity.InitialFacing).
                public const string DefaultActiveFacing = FacingDown;
            }
        }

        public static class Backend
        {
            public static class Exporter
            {
                public const string FieldEntityName = EFYVLabyrinthConfig.Shared.EntityNameField;
                public const string FieldAssetName = EFYVLabyrinthConfig.Shared.AssetNameField;
                public const string FieldAssetType = EFYVLabyrinthConfig.Shared.AssetTypeField;
                public const string FieldDocumentVersion = "documentVersion";
                public const string FieldBaseAssetType = "baseAssetType";
                public const string FieldProperties = "properties";
                public const string FieldHitboxes = "hitboxes";
                public const string FieldFrameIndex = "frameIndex";
                public const string FieldHitboxType = "hitboxType";
                public const string FieldX = "x";
                public const string FieldY = "y";
                public const string FieldWidth = "width";
                public const string FieldHeight = "height";
                public const string FieldAtlas = "atlas";
                public const string FieldFormatVersion = "formatVersion";
                public const string FieldFrameWidth = "frameWidth";
                public const string FieldFrameHeight = "frameHeight";
                public const string FieldAtlasWidth = "atlasWidth";
                public const string FieldAtlasHeight = "atlasHeight";
                public const string FieldAnimations = "animations";
                public const string FieldName = "name";
                public const string FieldFps = "fps";
                public const string FieldStartFrame = "startFrame";
                public const string FieldFrameCount = "frameCount";
                // Item #10 optional atlas-animation timing/playback fields. All
                // four are OMITTED when they hold their defaults, so documents
                // that do not use the features stay byte-identical and readers
                // fall back to fps / the full frame range.
                public const string FieldFrameDurationsMs = "frameDurationsMs";
                public const string FieldLoopStart = "loopStart";
                public const string FieldLoopEnd = "loopEnd";
                public const string FieldPingPong = "pingPong";
                // Wire-format bounds for frameDurationsMs entries: 0 is the
                // "inherit fps" sentinel; positive values are milliseconds.
                public const int InheritFrameDurationMs = EFYVLabyrinthConfig.Shared.EmptyCount;
                public const int MaxFrameDurationMs = 60000;
                // Item #7 OPTIONAL per-animation runtime-effect descriptors
                // (documentVersion 3). Each entry inside an atlas animation's
                // "effects" array is {name, effectType, trigger} plus optional
                // {colorRgba, durationMs, strength}; absent optionals resolve
                // to the Default* values below. effectType must be one of the
                // EffectType* strings; particleHook additionally requires a
                // non-empty name (it identifies the particle system to spawn).
                public const string FieldEffects = "effects";
                public const string FieldEffectType = "effectType";
                public const string FieldTrigger = "trigger";
                public const string FieldColorRgba = "colorRgba";
                public const string FieldDurationMs = "durationMs";
                public const string FieldStrength = "strength";
                public const string EffectTypeFlash = "flash";
                public const string EffectTypeTint = "tint";
                public const string EffectTypeParticleHook = "particleHook";
                public const int MaxEffectsPerAnimation = 32;
                // Item #6 OPTIONAL top-level sub-element attachment records
                // (documentVersion 4). The array sits NEXT TO "hitboxes" (same
                // frame-indexed shape) and is OMITTED entirely when no frame
                // carries an attachment, so attachment-free documents stay
                // byte-identical to version-3 output. Each entry is
                // {frameIndex, subElement, x, y, zOrder} plus optional
                // {flipX, flipY} (absent means false). x/y is the canvas-space
                // position of the sub-element's PIVOT; the attachment pixels
                // are ALSO flattened into the atlas at export time, so these
                // records exist for future dynamic consumers (the runtime
                // stores them; rendering from them is deferred).
                public const string FieldAttachments = "attachments";
                public const string FieldSubElement = "subElement";
                public const string FieldZOrder = "zOrder";
                public const string FieldFlipX = "flipX";
                public const string FieldFlipY = "flipY";
                // Item #5 OPTIONAL top-level tileset manifest block
                // (documentVersion 5). Present only on tile-sheet exports; the
                // member is OMITTED entirely on every other document, so
                // non-tileset output stays byte-identical to version-4 output.
                // The block is {tileSize, tiles:[name,...]} where the tile at
                // list index i carries FastGridMap short tile id i - the
                // tile-ID manifest mapping designed tiles to runtime ids. When
                // an atlas block rides alongside, its frameWidth/frameHeight
                // must equal tileSize and the tile count must fit the sheet.
                public const string FieldTileset = "tileset";
                public const string FieldTileSize = "tileSize";
                public const string FieldTiles = "tiles";
                public const int MaxTilesPerTileset = 256;
                public const int MaxTileNameLength = 64;
                public const int MaxAttachmentsPerFrame = 64;
                public const int MinAttachmentZOrder = -1024;
                public const int MaxAttachmentZOrder = 1024;
                public const int MinEffectDurationMs = EFYVLabyrinthConfig.Shared.EmptyCount;
                public const int MaxEffectDurationMs = MaxFrameDurationMs;
                public const float MinEffectStrength = EFYVLabyrinthConfig.Shared.NormalizedMin;
                public const float MaxEffectStrength = EFYVLabyrinthConfig.Shared.NormalizedMax;
                public const uint DefaultEffectColorRgba = 0xFFFFFFFFu;
                public const int DefaultEffectDurationMs = MinEffectDurationMs;
                public const float DefaultEffectStrength = MaxEffectStrength;
                public const string ExportSuffix = "_Export";
                public const string EfyvExtension = EFYVLabyrinthConfig.Shared.EfyvExtension;
                public const string PngExtension = EFYVLabyrinthConfig.Shared.PngExtension;
                public const string ReservedCon = "CON";
                public const string ReservedPrn = "PRN";
                public const string ReservedAux = "AUX";
                public const string ReservedNul = "NUL";
                public const string ReservedComPrefix = "COM";
                public const string ReservedLptPrefix = "LPT";
                public const int ReservedDeviceSuffixLength = EFYVLabyrinthConfig.Shared.UnitStep;
                public const char ReservedDeviceMinSuffix = '1';
                public const char ReservedDeviceMaxSuffix = '9';
                public const string TemporaryNamePrefix = ".";
                public const string CurrentDirectoryName = TemporaryNamePrefix;
                public const string ParentDirectoryName = TemporaryNamePrefix + TemporaryNamePrefix;
                public const string TrailingSpace = " ";
                public const string TemporaryExtension = ".tmp";
                public const string CompactGuidFormat = "N";
                public const int CurrentFormatVersion = 1;
                // Top-level .efyvlaby document version (#16a). Files written before
                // the field existed are read as LegacyDocumentVersion. Version 2
                // (item #10) added the OPTIONAL atlas-animation timing/playback
                // fields above; version 3 (item #7) added the OPTIONAL per-
                // animation runtime-effect descriptors; version 4 (item #6)
                // added the OPTIONAL top-level sub-element attachment records;
                // version 5 (item #5) added the OPTIONAL top-level tileset
                // manifest block. Importers accept the whole supported range
                // [MinSupportedDocumentVersion .. CurrentDocumentVersion]
                // instead of pinning one value, because every addition so far
                // is backward-compatible (old fields keep their meaning, new
                // fields are optional with defaults).
                public const int CurrentDocumentVersion = 5;
                public const int MinSupportedDocumentVersion = 1;
                public const int LegacyDocumentVersion = 1;

                public static class Png
                {
                    public const int IhdrLength = 13;
                    public const int ChunkTypeLength = 4;
                    public const int StoredBlockMaxLength = ushort.MaxValue;
                    public const int StoredBlockHeaderLength = 5;
                    public const int ZlibHeaderLength = 2;
                    public const int AdlerLength = 4;
                    public const int ScanlineFilterLength = 1;
                    public const int RgbaChannelCount = 4;
                    public const byte RgbaBitDepth = 8;
                    public const byte RgbaColorType = 6;
                    public const byte CompressionMethod = 0;
                    public const byte FilterMethod = 0;
                    public const byte InterlaceMethod = 0;
                    public const byte ScanlineFilterNone = 0;
                    public const byte ZlibCompressionMethodAndInfo = 0x78;
                    public const byte ZlibNoCompressionFlags = 0x01;
                    public const byte StoredBlockFinal = 1;
                    public const byte StoredBlockContinues = 0;
                    public const uint AdlerModulus = 65521u;
                    public const int AdlerModuloBlockLength = 5552;
                    public const uint AdlerInitialA = 1u;
                    public const uint AdlerInitialB = 0u;
                    public const uint InitialCrc = 0xFFFFFFFFu;
                    public const uint FinalCrcMask = 0xFFFFFFFFu;
                    public const uint CrcPolynomial = 0xEDB88320u;
                    public const int CrcTableSize = 256;
                    public const int CrcBitsPerByte = 8;
                    public const uint CrcIndexMask = 0xFFu;
                    public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                    public static readonly byte[] IhdrChunkType = { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
                    public static readonly byte[] IdatChunkType = { (byte)'I', (byte)'D', (byte)'A', (byte)'T' };
                    public static readonly byte[] IendChunkType = { (byte)'I', (byte)'E', (byte)'N', (byte)'D' };
                }
            }

            public static class IO
            {
                public const int DefaultFileStreamBufferSize = 4096;
                public const int InitialReadOffset = 0;
                public const int EndOfStreamReadCount = InitialReadOffset;
                // Longest stem accepted anywhere in the system. Exporters decorate
                // stems with facing suffixes, extensions, and dotted temporary names
                // (prefix + 32-char GUID + ".tmp"), so the cap leaves ample headroom
                // under the 255-character filename component limit shared by Windows
                // and common Unix filesystems.
                public const int MaxFileStemLength = 128;

                // Bounded retry (#12) around File.Replace/Move publishes: Unity (or
                // an antivirus scanner) briefly holding the destination makes the
                // swap sporadically fail with a sharing violation.
                public const int PublishRetryAttempts = 3;
                public const int PublishRetryFirstDelayMilliseconds = 20;
                public const int PublishRetryMaxDelayMilliseconds = 50;
            }

            public static class Save
            {
                // FastSaveEngine on-disk envelope (#19): {magic, version, CRC32}
                // little-endian header followed by the raw PlayerMetaSchema bytes.
                public const uint MagicNumber = 0x56594645u; // "EFYV" as little-endian bytes
                public const int FormatVersion = 1;
                public const int MagicOffset = 0;
                public const int VersionOffset = 4;
                public const int ChecksumOffset = 8;
                public const int HeaderSizeBytes = 12;
            }

            // Item #5 versioned binary map container (.efyvmap): the same
            // {magic, version, CRC32} little-endian envelope the save engine
            // uses, followed by the map payload (dimensions, tileset
            // reference, row-major int16 tile ids, prop placements). Written
            // by FastMapExporter through the atomic-publish machinery and read
            // back by FastMapImporter.TryParse (tri-state, like FastImporter).
            public static class MapFile
            {
                public const string Extension = ".efyvmap";
                public const uint MagicNumber = 0x4D594645u; // "EFYM" as little-endian bytes
                public const int FormatVersion = 1;
                public const int MagicOffset = 0;
                public const int VersionOffset = 4;
                public const int ChecksumOffset = 8;
                public const int HeaderSizeBytes = 12;
                public const int MaxMapDimension = 4096;
                public const int MaxMapProps = 4096;
                public const int BytesPerTile = 2;
                // The designer's "no tile here" cell id. Distinct from
                // Collections.EmptyTileId (0), which is a REAL first-palette
                // tile at runtime: the game blanks any id below
                // Game.Map.MinimumTileId, so blank designer cells render as
                // empty space instead of aliasing palette entry 0.
                public const short BlankTileId = -1;
            }

            public static class Collections
            {
                public const int DefaultSwapListCapacity = 100;
                public const int MapPropsCapacity = 256;
                public const int EmptyPoolCount = Serialization.FalseValue;
                public const short EmptyTileId = Serialization.FalseValue;
                public const int UnregisteredListIndex = -1;
                public const int UninitializedViewportCoordinate = int.MaxValue;
                public const float ViewportHalfExtent = 0.5f;
                public const float ReciprocalNumerator = Math.NormalizedMax;
            }

            public static class Serialization
            {
                public const int FalseValue = EFYVLabyrinthConfig.Shared.EmptyCount;
                public const int TrueValue = EFYVLabyrinthConfig.Shared.UnitStep;
                public const int SequentialPack = EFYVLabyrinthConfig.Shared.SequentialStructPack;
                public const int NullHash = FalseValue;
            }

            public static class Schema
            {
                public const int BlockSlotCount = 64;
                public const int BytesPerSlot = 4;
                public const int BlockSizeBytes = BlockSlotCount * BytesPerSlot;
                public const int MaxToons = 64;
                public const float DefaultStatMultiplier = Math.NormalizedMax;
                public const float DefaultAdditiveStat = Math.NormalizedMin;
                public const int InitialTotalCoins = Serialization.FalseValue;
            }

            public static class Models
            {
                public const float HitboxDefaultPosition = Math.NormalizedMin;
                public const float HitboxDefaultSize = Math.NormalizedMax;
            }

            public static class Memory
            {
                public const byte ClearedByte = Serialization.FalseValue;
            }

            public static class Procedural
            {
                public const int DefaultSmoothThreshold = 4;
                public const int FloodStackHeightShift = 2;
                public const int StackGrowthMultiplier = 2;
            }

            public static class Deformation
            {
                public const float BounceFrequencyMultiplier = 2f;
                public const float NormalizedCycle = Math.NormalizedMax;
                public const int OctantCount = EFYVLabyrinthConfig.Shared.DirectionOctantCount;
                // Item #10 presets. Shake runs this many full oscillations over
                // one normalized cycle; breathe amplitude is capped so the
                // vertical scale factor 1 + wave * amplitude can never reach 0.
                public const float ShakeOscillations = 3f;
                public const float MaxBreatheAmplitude = 0.9f;
                public const int PackedOctantLookup = 0xB374C8;
                public const int BitsPerOctant = 3;
                public const int OctantMask = 7;
                public const int OctantYSignShift = 2;
                public const int OctantXSignShift = Math.SingleBitShift;
                public const uint TransparentPixel = EFYVLabyrinthConfig.Shared.TransparentRgba;
            }

            public static class Random
            {
                public const uint DefaultSeed = 13371337u;
                public const int XorShiftLeftA = 13;
                public const int XorShiftRight = 17;
                public const int XorShiftLeftB = 5;
                public const uint InvalidSeed = Serialization.FalseValue;
                public const uint FallbackSeed = Serialization.TrueValue;
                public const float UIntToUnitFloat = 2.3283064365386963e-10f;
            }

            // Item #7 destructive filter primitives (FastEffects outline, glow,
            // and HSV color shift).
            public static class Effects
            {
                // Outline expands the opaque silhouette by exactly one pixel in
                // the 8-neighborhood (diagonals included, so diagonal edges get
                // an unbroken rim).
                public const int OutlineExpandRadius = EFYVLabyrinthConfig.Shared.UnitStep;
                // Classic hexcone HSV: hue in degrees over six 60-degree
                // sectors; the parity modulus folds a sector position onto the
                // descending half of the chroma ramp.
                public const float HueFullCircleDegrees = 360f;
                public const float HueSectorDegrees = 60f;
                public const float HueSectorParityModulus = 2f;
            }

            public static class Pixel
            {
                public const int GreenShift = EFYVLabyrinthConfig.Shared.RgbaGreenShift;
                public const int BlueShift = EFYVLabyrinthConfig.Shared.RgbaBlueShift;
                public const int AlphaShift = EFYVLabyrinthConfig.Shared.RgbaAlphaShift;
                public const int FixedPointShift = BlueShift;
                public const uint FixedPointRounding = 1u << (FixedPointShift - 1);
                public const int KernelRadiusMultiplier = 2;
                public const int KernelCenterPixel = Serialization.TrueValue;
                public const byte TransparentAlpha = EFYVLabyrinthConfig.Shared.TransparentAlpha;
                public const byte OpaqueAlpha = Math.ColorMaxByte;
                public const uint RgbMask = 0x00FFFFFFu;
            }

            public static class Math
            {
                public const int StepPositive = EFYVLabyrinthConfig.Shared.UnitStep;
                public const int StepNegative = EFYVLabyrinthConfig.Shared.NotFoundIndex;
                public const int SingleBitShift = StepPositive;
                public const float Deg2Rad = 0.0174532925f;
                public const float TwoPI = 6.28318531f;
                public const float PI = 3.14159265f;
                public const float PI_HALF = 1.57079632f;
                public const uint FnvOffsetBasis = 2166136261u;
                public const uint FnvPrime = 16777619u;
                public const int QuakeMagicNumber = 0x5f3759df;
                public const float InvSqrtInputHalf = 0.5f;
                public const float InvSqrtThreeHalves = 1.5f;
                public const float TaylorSinA = 1.27323954f;
                public const float TaylorSinB = 0.405284735f;
                public const byte ColorMaxByte = byte.MaxValue;
                public const float ColorHalf = 0.5f;
                public const int IntSignBitShift = 31;
                public const int FloatSignMask = 0x7FFFFFFF;
                public const float NormalizedMin = EFYVLabyrinthConfig.Shared.NormalizedMin;
                public const float NormalizedMax = EFYVLabyrinthConfig.Shared.NormalizedMax;
                public const uint DirectionVerticalFlag = Serialization.TrueValue;
                public const uint DirectionHorizontalFlag = Serialization.FalseValue;
                public const uint DirectionHorizontalBias = 3u;
            }
        }
    }
}
