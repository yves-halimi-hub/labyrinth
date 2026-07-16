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

                // Tooltips
                public const string TooltipDoorTargetMapId = "The ID of the map this door connects to.";
                public const string TooltipSarcophageMapIds = "List of possible random maps to teleport to.";
                public const string TooltipSarcophageTrapPrefab = "Enemy prefab to spawn for trap events.";
                public const string HeaderBackendBlur = "Backend Blur Integration";
                public const int InvalidBounds = -9999;
            }

            public static class EnvironmentData
            {
                public const float DefaultAnimationSpeed = 0.1f;
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
                public const float ImmediateDespawnDelay = 0f;
                public const bool Active = true;
                public const bool Dormant = false;
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
                public const string LogError = "Failed to parse " + EFYVLabyrinthConfig.Shared.EfyvExtension + " JSON file.";
                public const string LogSuccess = "[EFYV Importer] Successfully bridged {0} into Unity OOP system via FastImporter!";

                public const string DefaultEntityName = "UnknownEntity";
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

                public static readonly FieldDefinition[] GameAssetFields =
                {
                    new FieldDefinition(
                        EFYVLabyrinthConfig.Shared.AssetNameField,
                        Types.StringUpper,
                        EditorMetadata.Text("Asset Name", Types.DefaultString, true))
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
                public const int InitialFrameCount = Common.EmptyCount;
                public const int InitialFrameIndex = Common.FirstIndex;
                public const int AtlasDestinationY = Canvas.MinCoordinate;
                public const float SubElementScale = Common.UnitScale;
                public const int MaxAtlasDimension = 16384;
                public const int MaxAtlasPixelCount = 67108864;
            }

            public static class Persistence
            {
                public const int ProjectFormatVersion = EFYVLabyrinthConfig.Shared.UnitStep;
                public const int SubElementFormatVersion = ProjectFormatVersion;
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

                public static class Moving
                {
                    public const int ModeToonWalk = 0;
                    public const int ModeElementJitter = 1;
                    public const int DefaultWalkSplitY = 32;
                    public const float DefaultWalkBounceAmp = 2f;
                    public const float DefaultWalkStrideAmp = 4f;
                    public const int DefaultWalkFrameCount = 8;
                    public const int JitterOctantCount = EFYVLabyrinthConfig.Shared.DirectionOctantCount;
                    public const float DefaultJitterAmp = 1.5f;
                    public const float DefaultJitterFreq = Common.UnitScale;
                    public const int DefaultJitterFrameCount = 8;
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
            }
        }

        public static class Backend
        {
            public static class Exporter
            {
                public const string FieldEntityName = EFYVLabyrinthConfig.Shared.EntityNameField;
                public const string FieldAssetName = EFYVLabyrinthConfig.Shared.AssetNameField;
                public const string FieldAssetType = EFYVLabyrinthConfig.Shared.AssetTypeField;
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
