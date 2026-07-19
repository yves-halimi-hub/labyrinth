using System;
using System.Collections.Generic;
using System.IO;
using EFYV.Core.Controllers;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;
using EFYV.Core.Entities.Environment.Implementations;
using EFYV.Core.Entities.Items.Merchant;
using EFYV.Core.Managers;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

internal static partial class Program
{
    private static void TestSpawningAiAndFallbackMaps()
    {
        var spawner = CreateComponent<SpawnManager>();
        spawner.spawnRadius = -1f;
        Near(Config.Game.Spawner.DefaultSpawnRadius, spawner.spawnRadius);
        spawner.spawnRadius = float.NaN;
        Near(Config.Game.Spawner.DefaultSpawnRadius, spawner.spawnRadius);
        spawner.spawnRadius = float.PositiveInfinity;
        Near(Config.Game.Spawner.DefaultSpawnRadius, spawner.spawnRadius);
        spawner.baseSpawnRate = -100f;
        Near(Config.Game.Spawner.DefaultBaseSpawnRate, spawner.baseSpawnRate);
        spawner.difficultyMultiplier = float.NegativeInfinity;
        Near(Config.Game.Spawner.DefaultDifficultyMultiplier, spawner.difficultyMultiplier);

        spawner.spawnRadius = 12.5f;
        spawner.baseSpawnRate = 2.25f;
        spawner.difficultyMultiplier = 0.125f;
        Near(12.5f, spawner.spawnRadius);
        Near(2.25f, spawner.baseSpawnRate);
        Near(0.125f, spawner.difficultyMultiplier);
        Invoke(spawner, "Awake");
        Near(Config.Game.Spawner.InitialGameTimer, spawner.GameTimer);

        var playerTarget = new GameObject("spawn-target").transform;
        playerTarget.position = new Vector3(100, 200, 7);
        spawner.playerTransform = playerTarget;
        spawner.enemyPrefabs = Array.Empty<Enemy>();
        Time.deltaTime = 0.5f;
        Invoke(spawner, "Update");
        Near(0.5f, spawner.GameTimer);

        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var enemyPrefab = CreateComponent<ProbeEnemy>(addRenderer: true);
        enemyPrefab.Initialize();
        enemyPrefab.Apply(10f, 1f, 1f, 1f);
        spawner.enemyPrefabs = new Enemy[] { enemyPrefab };
        spawner.baseSpawnRate = float.MaxValue;
        spawner.difficultyMultiplier = 0f;
        Time.deltaTime = 1f;
        Invoke(spawner, "Update");
        Equal(Config.Game.Spawner.MaxSpawnsPerFrame, Enemy.ActiveEnemies.Count);
        for (int i = 0; i < Enemy.ActiveEnemies.Count; i++)
        {
            Enemy enemy = Enemy.ActiveEnemies[i];
            float dx = enemy.entityTransform.position.x - playerTarget.position.x;
            float dy = enemy.entityTransform.position.y - playerTarget.position.y;
            float distance = MathF.Sqrt((dx * dx) + (dy * dy));
            Check(distance >= spawner.spawnRadius * 0.9f && distance <= spawner.spawnRadius * 1.1f,
                "Taylor trig spawn offset exceeded its approximation envelope.");
            Near(playerTarget.position.z, enemy.entityTransform.position.z, 0f);
            Equal(i, enemy.ActiveListIndex);
        }

        var director = CreateComponent<AIDirector>(invokeAwake: true);
        director.spawnManager = spawner;
        Near(Config.Game.AI.IntensityBaseMultiplier +
            (spawner.GameTimer / Config.Game.AI.IntensityMinuteDivider) * Config.Game.AI.IntensityScalingFactor,
            director.GetIntensityMultiplier());
        Near(Config.Game.AI.HealthBaseMultiplier +
            (spawner.GameTimer / Config.Game.AI.HealthMinuteDivider), director.GetEnemyHealthMultiplier());
        Near(Config.Game.AI.SpeedBaseMultiplier +
            (spawner.GameTimer / Config.Game.AI.SpeedMinuteDivider), director.GetEnemySpeedMultiplier());
        director.spawnManager = null;
        Near(Config.Game.AI.DefaultMultiplier, director.GetIntensityMultiplier());
        Near(Config.Game.AI.DefaultMultiplier, director.GetEnemyHealthMultiplier());
        Near(Config.Game.AI.DefaultMultiplier, director.GetEnemySpeedMultiplier());

        var map = new FastGridMap(127, 89);
        Array.Fill(map.RawData, (short)-123);
        MapViewportController.PopulateFallbackMap(map, 0);
        foreach (short tile in map.RawData) Equal((short)0, tile);

        for (int types = 1; types <= 17; types++)
        {
            MapViewportController.PopulateFallbackMap(map, types);
            foreach (short tile in map.RawData)
                Check(tile >= 0 && tile < types, "Fallback map generated an invalid palette index.");
        }
        Throws<ArgumentNullException>(() => MapViewportController.PopulateFallbackMap(null, 1));

        var viewport = CreateComponent<MapViewportController>();
        viewport.cellSize = 0f;
        Near(Config.Game.Map.DefaultCellSize, viewport.cellSize);
        viewport.cellSize = float.NaN;
        Near(Config.Game.Map.DefaultCellSize, viewport.cellSize);
        viewport.cellSize = 2.5f;
        Near(2.5f, viewport.cellSize);

        UnityEngine.Object.Destroy(pool.gameObject);
    }

    private static void TestSaveProgressionAndSaturatingArithmetic()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-game-save-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Application.persistentDataPath = tempRoot;
        try
        {
            var save = CreateComponent<SaveManager>(invokeAwake: true);
            save.currentSaveData = PlayerMetaSchema.Default();
            Same(save, SaveManager.Instance);

            int initialTotal = save.currentSaveData.TotalCoinsCollected;
            save.AddCoinsToToon(null, 100);
            save.AddCoinsToToon(string.Empty, 100);
            save.AddCoinsToToon("hero", 0);
            save.AddCoinsToToon("hero", -1);
            Equal(initialTotal, save.currentSaveData.TotalCoinsCollected);

            int award = Config.Game.Progression.CoinsPerToonLevel * 3 + 7;
            save.AddCoinsToToon("hero", award);
            Check(save.currentSaveData.TryGetToonBlock(0, out FastSchemaBlock toon));
            Equal(FastMath.FastHash("hero"), toon.GetInt((int)ToonSchema.ToonIdHash));
            Equal(award, toon.GetInt((int)ToonSchema.TotalCoinsCollected));
            Equal(Config.Game.Progression.InitialToonLevel +
                (award / Config.Game.Progression.CoinsPerToonLevel), toon.GetInt((int)ToonSchema.Level));
            int expectedPoints = (award / Config.Game.Progression.CoinsPerToonLevel) *
                Config.Game.Progression.StatPointsPerLevel;
            Equal(expectedPoints, toon.GetInt((int)ToonSchema.UnspentStatPoints));

            float beforeHealth = toon.GetFloat((int)ToonSchema.StatsStart + (int)StatSchema.MaxHealth);
            Check(save.SpendToonStatPoint("hero", StatSchema.MaxHealth));
            Check(save.currentSaveData.TryGetToonBlock(0, out toon));
            Near(beforeHealth + Config.Game.Progression.StatUpgradeAdditiveTenPercent,
                toon.GetFloat((int)ToonSchema.StatsStart + (int)StatSchema.MaxHealth));
            Equal(expectedPoints - Config.Game.Progression.ToonStatPointCost,
                toon.GetInt((int)ToonSchema.UnspentStatPoints));
            Check(!save.SpendToonStatPoint(null, StatSchema.MaxHealth));
            Check(!save.SpendToonStatPoint("hero", (StatSchema)(-1)));
            Check(!save.SpendToonStatPoint("hero", StatSchema.MAX_STATS));

            save.currentSaveData.TotalCoinsCollected = 1000;
            save.currentSaveData.LegacyStats = FastSchemaBlock.DefaultStats();
            float legacyMight = save.currentSaveData.LegacyStats.GetFloat((int)StatSchema.Might);
            Check(save.SpendCoinsOnLegacyStat(StatSchema.Might, 100));
            Equal(900, save.currentSaveData.TotalCoinsCollected);
            Near(legacyMight + Config.Game.Progression.StatUpgradeAdditiveFivePercent,
                save.currentSaveData.LegacyStats.GetFloat((int)StatSchema.Might));
            Check(!save.SpendCoinsOnLegacyStat(StatSchema.Might, 0));
            Check(!save.SpendCoinsOnLegacyStat((StatSchema)int.MaxValue, 1));
            Check(!save.SpendCoinsOnLegacyStat(StatSchema.Might, 901));

            FastSchemaBlock combined = save.GetCombinedStatsForToon("hero");
            float toonMight = toon.GetFloat((int)ToonSchema.StatsStart + (int)StatSchema.Might);
            Near(save.currentSaveData.LegacyStats.GetFloat((int)StatSchema.Might) +
                (toonMight - Config.Game.Progression.DefaultMultiplier),
                combined.GetFloat((int)StatSchema.Might));
            FastSchemaBlock noToon = save.GetCombinedStatsForToon("missing");
            Near(save.currentSaveData.LegacyStats.GetFloat((int)StatSchema.Might),
                noToon.GetFloat((int)StatSchema.Might));

            Equal(int.MaxValue, (int)InvokeStatic(typeof(SaveManager), "SaturatingAdd", int.MaxValue, 1));
            Equal(int.MaxValue, (int)InvokeStatic(typeof(SaveManager), "SaturatingAdd", int.MaxValue - 10, 100));
            Equal(30, (int)InvokeStatic(typeof(SaveManager), "SaturatingAdd", 10, 20));
            Equal(int.MaxValue, (int)InvokeStatic(typeof(SaveManager), "SaturatingMultiply", int.MaxValue, 2));
            Equal(42, (int)InvokeStatic(typeof(SaveManager), "SaturatingMultiply", 6, 7));

            save.currentSaveData.TotalCoinsCollected = int.MaxValue - 5;
            save.AddCoinsToToon("hero", int.MaxValue);
            Equal(int.MaxValue, save.currentSaveData.TotalCoinsCollected);
            Check(save.currentSaveData.TryGetToonBlock(0, out toon));
            Equal(int.MaxValue, toon.GetInt((int)ToonSchema.TotalCoinsCollected));

            save.SaveGame();
            string savePath = Path.Combine(tempRoot, Config.Game.Save.SaveFileName);
            Check(File.Exists(savePath));
            int persistedTotal = save.currentSaveData.TotalCoinsCollected;
            save.currentSaveData = default;
            save.LoadGame();
            Equal(persistedTotal, save.currentSaveData.TotalCoinsCollected);

            save.currentSaveData = PlayerMetaSchema.Default();
            for (int i = 0; i < PlayerMetaSchema.MaxToons; i++)
                save.AddCoinsToToon("toon-" + i, 1);
            int fullTotal = save.currentSaveData.TotalCoinsCollected;
            save.AddCoinsToToon("one-too-many", 1);
            Equal(fullTotal, save.currentSaveData.TotalCoinsCollected);
            for (int i = 0; i < PlayerMetaSchema.MaxToons; i++)
            {
                Check(save.currentSaveData.TryGetToonBlock(i, out FastSchemaBlock populated));
                Check(populated.GetInt((int)ToonSchema.ToonIdHash) != Config.Game.Progression.EmptyToonHash);
            }
        }
        finally
        {
            TestRuntime.Reset();
            Directory.Delete(tempRoot, true);
        }
    }

    private static PlayerController CreateTestPlayer()
    {
        var gameObject = new GameObject("TestPlayer");
        gameObject.AddComponent<SpriteRenderer>();
        gameObject.AddComponent<WeaponController>();
        var player = (PlayerController)gameObject.AddComponent(typeof(PlayerController), false);
        player.Initialize();
        return player;
    }

    private static void TestPropsPurchasesAndAchievements()
    {
        var propData = ScriptableObject.CreateInstance<GameAssetData>();
        var sourceSprite = new Sprite();
        propData.sprite = sourceSprite;
        var prop = CreateComponent<ProbeProp>(addRenderer: true);
        prop.Initialize();
        prop.LoadData(propData);
        Same(sourceSprite, prop.spriteRenderer.sprite);
        prop.IsBlocking = true;
        Check(prop.IsBlocking);
        prop.animationSpeed = 0.5f;
        prop.animationFrames = new[] { new Sprite(), new Sprite(), new Sprite() };
        prop.OnSpawn();
        Equal(1, PropEntity.ActiveAnimatedProps.Count);
        int frameBefore = prop.Frame;
        prop.TickAnimation(1f);
        Equal((frameBefore + 1) % prop.animationFrames.Length, prop.Frame);
        Same(prop.animationFrames[prop.Frame], prop.spriteRenderer.sprite);
        prop.OnDespawn();
        Equal(0, PropEntity.ActiveAnimatedProps.Count);
        Equal(-1, prop.ActiveListIndex);

        var coin = CreateComponent<CoinProp>(addRenderer: true);
        coin.Initialize();
        coin.gradeSprites = new Sprite[Config.Game.Drops.MaxCoinGrade];
        for (int i = 0; i < coin.gradeSprites.Length; i++) coin.gradeSprites[i] = new Sprite();
        coin.InitializeGrade(int.MinValue);
        Equal(Config.Game.Drops.MinCoinGrade, coin.Grade);
        Equal(Config.Game.Drops.BaseCoinValue * coin.Grade * coin.Grade, coin.Value);
        coin.InitializeGrade(int.MaxValue);
        Equal(Config.Game.Drops.MaxCoinGrade, coin.Grade);
        Same(coin.gradeSprites[coin.Grade - Config.Game.EnvironmentData.GradeToSpriteIndexOffset],
            coin.spriteRenderer.sprite);

        var chest = CreateComponent<ChestProp>(addRenderer: true);
        chest.Initialize();
        chest.gradeSprites = new Sprite[Config.Game.Drops.MaxChestGrade];
        for (int i = 0; i < chest.gradeSprites.Length; i++) chest.gradeSprites[i] = new Sprite();
        chest.InitializeGrade(int.MinValue);
        Equal(Config.Game.Drops.MinChestGrade, chest.Grade);
        chest.InitializeGrade(int.MaxValue);
        Equal(Config.Game.Drops.MaxChestGrade, chest.Grade);
        Check(chest.IsBlocking);

        var gem = CreateComponent<XPGem>(addRenderer: true);
        gem.Initialize();
        gem.xpValue = 123.5f;
        Near(123.5f, gem.xpValue);
        Check(!gem.IsBlocking);

        PlayerController player = CreateTestPlayer();
        var healing = new HealingPurchase("heal", 10, 25f);
        Equal("heal", healing.ItemName);
        Equal(10, healing.Cost);
        Near(25f, healing.HealAmount);
        Check(!healing.Apply(player));
        player.TakeDamage(50f);
        Check(healing.Apply(player));
        Near(75f, player.CurrentHealth);

        // #34: buffs are real now - a recognized id registers a ticking buff on
        // the player; unknown ids are rejected so purchases refund.
        var buff = new TemporaryBuffPurchase("buff", 7, Config.Game.Merchant.PotionBuffId, 12.5f);
        Equal(Config.Game.Merchant.PotionBuffId, buff.BuffId);
        Near(12.5f, buff.Duration);
        float speedBeforeBuff = player.BaseSpeed;
        Check(buff.Apply(player));
        Equal(1, player.ActiveBuffCount);
        Near(speedBeforeBuff * PlayerController.MoveSpeedBuffMultiplier, player.BaseSpeed);
        var unknownBuff = new TemporaryBuffPurchase("buff2", 7, "speed", 12.5f);
        Check(!unknownBuff.Apply(player), "Unknown buff ids must be rejected.");
        Equal(1, player.ActiveBuffCount);
        var weaponPurchase = new WeaponUpgradePurchase("weapon", 9, "wand");
        Equal("wand", weaponPurchase.WeaponId);

        var merchant = CreateComponent<BaseMerchantProp>(addRenderer: true);
        merchant.Initialize();
        player.AddSessionCoins(100);
        int coinsBefore = player.SessionCoins;
        player.Heal(1000f);
        merchant.AttemptPurchase(player, healing);
        Equal(coinsBefore, player.SessionCoins);
        player.TakeDamage(50f);
        merchant.AttemptPurchase(player, healing);
        Equal(coinsBefore - healing.Cost, player.SessionCoins);
        player.AddSessionCoins(-1);
        Equal(coinsBefore - healing.Cost, player.SessionCoins);
        Check(!player.SpendSessionCoins(0));
        Check(!player.SpendSessionCoins(-1));
        Check(!player.SpendSessionCoins(int.MaxValue));

        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-achievement-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Application.persistentDataPath = tempRoot;
        try
        {
            var save = CreateComponent<SaveManager>(invokeAwake: true);
            save.currentSaveData = PlayerMetaSchema.Default();
            var database = ScriptableObject.CreateInstance<LegacyAchievementDatabase>();
            Invoke(database, "PopulateBasis");
            Equal(Config.Game.Achievements.BasisData.Titles.Length, database.achievements.Count);
            for (int i = 0; i < database.achievements.Count; i++)
            {
                LegacyAchievementDefinition definition = database.achievements[i];
                Equal(i, definition.id);
                Equal(Config.Game.Achievements.BasisData.Titles[i], definition.title);
                Equal(Config.Game.Achievements.BasisData.Descriptions[i], definition.description);
            }

            var achievements = CreateComponent<AchievementManager>(invokeAwake: true);
            achievements.achievementDatabase = database;
            int events = 0;
            achievements.OnAchievementUnlocked += _ => events++;
            foreach (int id in new[] { 0, 1, 29, 31, 32, Config.Game.Achievements.MaxAchievements - 1 })
            {
                Check(!achievements.IsAchievementUnlocked(id));
                achievements.UnlockAchievement(id);
                Check(achievements.IsAchievementUnlocked(id));
                achievements.UnlockAchievement(id);
                Check(achievements.IsAchievementUnlocked(id));
            }
            Equal(3, events);
            Check(!achievements.IsAchievementUnlocked(-1));
            Check(!achievements.IsAchievementUnlocked(Config.Game.Achievements.MaxAchievements));
            achievements.UnlockAchievement(-1);
            achievements.UnlockAchievement(int.MaxValue);
            List<LegacyAchievementDefinition> unlocked = achievements.GetUnlockedAchievements();
            Equal(3, unlocked.Count);
            Equal(0, unlocked[0].id);
            Equal(1, unlocked[1].id);
            Equal(29, unlocked[2].id);
        }
        finally
        {
            TestRuntime.Reset();
            Directory.Delete(tempRoot, true);
        }
    }

    private static void TestCameraAndManagerStateMachines()
    {
        var effect = CreateComponent<MapTransitionCameraEffect>(invokeAwake: true);
        Equal(Config.Game.Map.MinimumBlurRadius, effect.CurrentBlurRadius);
        SetField(effect, "enableCpuBlurFallback", false);
        var source = new RenderTexture { width = 64, height = 32 };
        var destination = new RenderTexture { width = 64, height = 32 };
        effect.CurrentBlurRadius = 99;
        Invoke(effect, "OnRenderImage", source, destination);
        Equal(1, Graphics.BlitCount);
        Equal(null, RenderTexture.active);
        effect.CurrentBlurRadius = Config.Game.Map.MinimumBlurRadius;
        SetField(effect, "enableCpuBlurFallback", true);
        Invoke(effect, "OnRenderImage", source, destination);
        Equal(2, Graphics.BlitCount);

        var playerObject = new GameObject("manager-player");
        playerObject.AddComponent<SpriteRenderer>();
        var weaponSystem = playerObject.AddComponent<WeaponController>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        Same(player, PlayerController.Instance);
        Same(weaponSystem, player.WeaponSystem);
        // A free weapon slot now always allows normal offers (#22), so cap the
        // inventory at ONE slot to exercise the special-attack flip below.
        weaponSystem.Initialize(1);
        var normalWeapon = CreateComponent<ProbeWeapon>(invokeAwake: true);
        normalWeapon.Configure(1f, 1f, 1);
        Check(weaponSystem.TryAddWeapon(normalWeapon));

        var upgrades = CreateComponent<UpgradeManager>(invokeAwake: true);
        int normalChoices = -1;
        int specialChoices = -1;
        float specialPenalty = -1f;
        upgrades.OnNormalUpgradesRequested += count => normalChoices = count;
        upgrades.OnSpecialAttacksRequested += (count, penalty) =>
        {
            specialChoices = count;
            specialPenalty = penalty;
        };
        upgrades.OnPlayerLevelUp();
        Equal(Config.Game.Weapons.Inventory.UpgradeChoicesNormalPhase, normalChoices);
        Check(!upgrades.IsSpecialAttackPhase);

        normalWeapon.Configure(1f, 1f, Config.Game.Weapons.Inventory.MaxLevel);
        upgrades.OnPlayerLevelUp();
        Check(upgrades.IsSpecialAttackPhase);
        Equal(Config.Game.Weapons.Inventory.UpgradeChoicesNormalPhase, specialChoices);
        Near(Config.Game.Weapons.Inventory.PenaltyMultiplierBase, specialPenalty);
        upgrades.OpenChest(7);
        Equal(7, specialChoices);
        Near(Config.Game.Weapons.Inventory.PenaltyMultiplierBase +
            Config.Game.Weapons.Inventory.PenaltyMultiplierIncrement, specialPenalty);

        Enemy.ActiveEnemies.Clear();
        var enemies = new List<ProbeEnemy>();
        for (int i = 0; i < 32; i++) enemies.Add(SpawnEnemy(i, 0, 10f));
        upgrades.InvokeSpecialAttack(Config.Game.Weapons.SpecialAttackScreenWipeName);
        Equal(0, Enemy.ActiveEnemies.Count);
        foreach (ProbeEnemy enemy in enemies) Equal(1, enemy.DeathCalls);

        for (int i = 0; i < 16; i++) enemies.Add(SpawnEnemy(i, 0, 100f));
        upgrades.InvokeSpecialAttack(Config.Game.Weapons.SpecialAttackHalfMobHealthName);
        Equal(16, Enemy.ActiveEnemies.Count);
        for (int i = 0; i < Enemy.ActiveEnemies.Count; i++) Near(50f, Enemy.ActiveEnemies[i].CurrentHealth);
        upgrades.InvokeSpecialAttack("unknown-attack");
        for (int i = 0; i < Enemy.ActiveEnemies.Count; i++) Near(50f, Enemy.ActiveEnemies[i].CurrentHealth);

        var drop = CreateComponent<DropManager>(invokeAwake: true);
        drop.Tick(1f, 600f);
        drop.ResetTimers();
        drop.DropLoot(Enemy.ActiveEnemies[0]);

        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        foreach (Enemy enemy in Enemy.ActiveEnemies.Items)
        {
            // These enemies are not pool-owned; map switching should still deactivate them safely.
            enemy.prefabPoolKey = Config.Game.Entity.EmptyPrefabPoolKey;
        }
        var mapManager = CreateComponent<MapManager>(invokeAwake: true);
        mapManager.SwitchMap("adversarial-map-id/../ignored-by-placeholder");
        Equal(0, Enemy.ActiveEnemies.Count);
        Check(Debug.Messages.Count > 0);
        mapManager.SwitchMap(null);

        UnityEngine.Object.Destroy(effect.gameObject);
        Equal(null, GetField<Texture2D>(effect, "_screenTex"));
        Equal(null, GetField<Texture2D>(effect, "_blurTex"));
        Equal(null, GetField<Texture2D>(effect, "_scratchTex"));
        UnityEngine.Object.Destroy(pool.gameObject);
    }

    // batch1/game-combat agent: runtime upgrade-loop wiring (#22).
    private static void TestUpgradeLoopRuntimeWiring()
    {
        var playerObject = new GameObject("upgrade-loop-player");
        playerObject.AddComponent<SpriteRenderer>();
        var weaponSystem = playerObject.AddComponent<WeaponController>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        Same(player, PlayerController.Instance);
        weaponSystem.Initialize(2);
        Check(weaponSystem.HasFreeWeaponSlot);

        var upgrades = CreateComponent<UpgradeManager>(invokeAwake: true);
        var poolPrefab = CreateComponent<ProbeWeapon>(invokeAwake: true);
        poolPrefab.Configure(1f, 3f);
        upgrades.normalWeaponPool = new EFYV.Core.Weapons.Weapon[] { poolPrefab };

        int specialRequests = 0;
        upgrades.OnSpecialAttacksRequested += (count, penalty) => specialRequests++;

        // An EMPTY weapon list with free capacity offers normal upgrades: the run
        // must not flip into the special-attack phase on first level-up.
        FastRandom.SetSeed(0x22AAu);
        player.LevelUp();
        Check(!upgrades.IsSpecialAttackPhase,
            "An empty inventory must never flip the special-attack phase.");
        Equal(0, specialRequests);

        // No UI subscriber: the manager applied the level-up choices directly -
        // both free slots are filled from the weapon pool, then the lowest-level
        // weapon is raised. New weapons carry the player's faction.
        Equal(2, weaponSystem.activeWeapons.Count);
        Check(!weaponSystem.HasFreeWeaponSlot);
        Check(weaponSystem.activeWeapons[0] is ProbeWeapon);
        Check(weaponSystem.activeWeapons[1] is ProbeWeapon);
        NotSame(poolPrefab, weaponSystem.activeWeapons[0]);
        NotSame(poolPrefab, weaponSystem.activeWeapons[1]);
        NotSame(weaponSystem.activeWeapons[0], weaponSystem.activeWeapons[1]);
        Equal(Faction.Player, weaponSystem.activeWeapons[0].OwnerFaction);
        Equal(Faction.Player, weaponSystem.activeWeapons[1].OwnerFaction);
        // Two grants land at the initial level; every remaining choice raised the
        // lowest weapon by one, so the level sum equals the offered choice count.
        Equal(Config.Game.Weapons.Inventory.UpgradeChoicesNormalPhase,
            weaponSystem.activeWeapons[0].Level + weaponSystem.activeWeapons[1].Level);

        // Chest path: OpenChest routes through the same application path and raises
        // the lowest-level weapon.
        upgrades.OpenChest(1);
        Equal(Config.Game.Weapons.Inventory.UpgradeChoicesNormalPhase + 1,
            weaponSystem.activeWeapons[0].Level + weaponSystem.activeWeapons[1].Level);

        // A UI subscriber takes over: choices are forwarded and nothing auto-applies.
        int normalRequests = 0;
        int offeredChoices = -1;
        upgrades.OnNormalUpgradesRequested += count => { normalRequests++; offeredChoices = count; };
        upgrades.OpenChest(5);
        Equal(1, normalRequests);
        Equal(5, offeredChoices);
        Equal(Config.Game.Weapons.Inventory.UpgradeChoicesNormalPhase + 1,
            weaponSystem.activeWeapons[0].Level + weaponSystem.activeWeapons[1].Level);

        // Direct application API: reports how many upgrades landed and stops at max.
        int maxLevel = Config.Game.Weapons.Inventory.MaxLevel;
        Equal(2 * (maxLevel - 2), upgrades.ApplyNormalUpgrades(1000));
        Equal(maxLevel, weaponSystem.activeWeapons[0].Level);
        Equal(maxLevel, weaponSystem.activeWeapons[1].Level);
        Equal(0, upgrades.ApplyNormalUpgrades(3));

        // Everything maxed with no free slot: the next selection flips the run into
        // the special-attack phase.
        upgrades.OnPlayerLevelUp();
        Check(upgrades.IsSpecialAttackPhase);
        Equal(1, specialRequests);

        FastRandom.SetSeed(Config.Backend.Random.DefaultSeed);
    }

    // batch2/game-managers agent: #34 - meta-progression stat fold, timed buffs,
    // and event-driven achievement triggers, plus the #24 achievement-id fixes.
    private static void TestManagersMetaProgressionAndAchievementTriggers()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-meta-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Application.persistentDataPath = tempRoot;
        try
        {
            var save = CreateComponent<SaveManager>(invokeAwake: true);
            save.currentSaveData = PlayerMetaSchema.Default();

            // Buy legacy upgrades: MaxHealth twice (+0.1 each), MoveSpeed once (+0.05).
            save.currentSaveData.TotalCoinsCollected = 1000;
            Check(save.SpendCoinsOnLegacyStat(StatSchema.MaxHealth, 1));
            Check(save.SpendCoinsOnLegacyStat(StatSchema.MaxHealth, 1));
            Check(save.SpendCoinsOnLegacyStat(StatSchema.MoveSpeed, 1));
            // And toon points: one MaxHealth (+0.1), one MoveSpeed (+0.05).
            save.AddCoinsToToon("meta-toon", 2000);
            Check(save.SpendToonStatPoint("meta-toon", StatSchema.MaxHealth));
            Check(save.SpendToonStatPoint("meta-toon", StatSchema.MoveSpeed));

            // #34: player initialization folds the account-legacy multipliers into
            // MaxHealth/BaseSpeed (no toon selected yet).
            PlayerController player = CreateTestPlayer();
            float legacyHealthMultiplier = 1f + (2f * Config.Game.Progression.StatUpgradeAdditiveTenPercent);
            float legacySpeedMultiplier = 1f + Config.Game.Progression.StatUpgradeAdditiveFivePercent;
            Near(Config.Game.Player.DefaultMaxHealth * legacyHealthMultiplier, player.MaxHealth);
            Near(player.MaxHealth, player.CurrentHealth, 0f);
            Near(Config.Game.Player.DefaultBaseSpeed * legacySpeedMultiplier, player.BaseSpeed);

            // Toon selection folds toon stats on top: combined = legacy + (toon - 1).
            player.ReinitializeForToon("meta-toon");
            float combinedHealthMultiplier =
                legacyHealthMultiplier + Config.Game.Progression.StatUpgradeAdditiveTenPercent;
            float combinedSpeedMultiplier =
                legacySpeedMultiplier + Config.Game.Progression.StatUpgradeAdditiveFivePercent;
            Near(Config.Game.Player.DefaultMaxHealth * combinedHealthMultiplier, player.MaxHealth);
            Near(Config.Game.Player.DefaultBaseSpeed * combinedSpeedMultiplier, player.BaseSpeed);
            // Re-initialization recomputes from the base stats - never compounds.
            player.ReinitializeForToon("meta-toon");
            Near(Config.Game.Player.DefaultMaxHealth * combinedHealthMultiplier, player.MaxHealth);
            Near(Config.Game.Player.DefaultBaseSpeed * combinedSpeedMultiplier, player.BaseSpeed);

            // Corrupt multipliers (NaN, negative) fall back to the neutral 1x.
            save.currentSaveData.LegacyStats.SetFloat((int)StatSchema.MaxHealth, float.NaN);
            save.currentSaveData.LegacyStats.SetFloat((int)StatSchema.MoveSpeed, -3f);
            player.ReinitializeForToon(null);
            Near(Config.Game.Player.DefaultMaxHealth, player.MaxHealth);
            Near(Config.Game.Player.DefaultBaseSpeed, player.BaseSpeed);
            save.currentSaveData.LegacyStats = FastSchemaBlock.DefaultStats();
            player.ReinitializeForToon(null);
            Near(Config.Game.Player.DefaultMaxHealth, player.MaxHealth);

            // #34 timed buffs: the haste potion multiplies BaseSpeed, ticks down
            // centrally in the player's Update, and reverts on expiry.
            float baseSpeed = player.BaseSpeed;
            var potion = new TemporaryBuffPurchase(Config.Game.Merchant.PotionName,
                Config.Game.Merchant.PotionCost, Config.Game.Merchant.PotionBuffId, 2f);
            Check(potion.Apply(player));
            Equal(1, player.ActiveBuffCount);
            Near(baseSpeed * PlayerController.MoveSpeedBuffMultiplier, player.BaseSpeed);
            // Re-applying keeps the longer timer and never stacks the multiplier.
            Check(player.ApplyTimedBuff(Config.Game.Merchant.PotionBuffId, 1f));
            Equal(1, player.ActiveBuffCount);
            Near(baseSpeed * PlayerController.MoveSpeedBuffMultiplier, player.BaseSpeed);
            Time.deltaTime = 1.5f;
            Invoke(player, "Update"); // 2.0s - 1.5s: still active.
            Equal(1, player.ActiveBuffCount);
            Near(baseSpeed * PlayerController.MoveSpeedBuffMultiplier, player.BaseSpeed);
            Invoke(player, "Update"); // Expired: reverted exactly to the base.
            Equal(0, player.ActiveBuffCount);
            Near(baseSpeed, player.BaseSpeed);
            // Rejections: unknown ids and non-positive durations never register.
            Check(!player.ApplyTimedBuff("NotABuff", 5f));
            Check(!player.ApplyTimedBuff(Config.Game.Merchant.PotionBuffId, 0f));
            Check(!player.ApplyTimedBuff(Config.Game.Merchant.PotionBuffId, float.NaN));
            Check(!player.ApplyTimedBuff(null, 1f));
            Equal(0, player.ActiveBuffCount);

            // #34 purchase API: success deducts, unknown-buff failure refunds.
            var merchant = CreateComponent<BaseMerchantProp>(addRenderer: true);
            merchant.Initialize();
            player.AddSessionCoins(1000);
            int coins = player.SessionCoins;
            var affordable = new TemporaryBuffPurchase("meta-potion", 100, Config.Game.Merchant.PotionBuffId, 3f);
            Check(merchant.AttemptPurchase(player, affordable));
            Equal(coins - 100, player.SessionCoins);
            Equal(1, player.ActiveBuffCount);
            var unknown = new TemporaryBuffPurchase("meta-unknown", 50, "NoSuchBuff", 3f);
            Check(!merchant.AttemptPurchase(player, unknown), "An unknown buff must refund.");
            Equal(coins - 100, player.SessionCoins);
            Check(!merchant.AttemptPurchase(null, affordable));
            Check(!merchant.AttemptPurchase(player, null));

            // #34 achievement triggers: the kill ladder fires through the
            // AchievementManager, event-driven and idempotent.
            var achievements = CreateComponent<AchievementManager>();
            var database = ScriptableObject.CreateInstance<LegacyAchievementDatabase>();
            Invoke(database, "PopulateBasis");
            achievements.achievementDatabase = database;
            Invoke(achievements, "Awake");
            int unlockEvents = 0;
            var unlockedIds = new List<int>();
            achievements.OnAchievementUnlocked += definition => { unlockEvents++; unlockedIds.Add(definition.id); };

            Equal(0, achievements.SessionKillCount);
            achievements.NotifyEnemyKilled(); // Kill #1: "First Blood" (id 0).
            Equal(1, achievements.SessionKillCount);
            Check(achievements.IsAchievementUnlocked(0));
            Check(!achievements.IsAchievementUnlocked(1));
            for (int kill = 1; kill < 99; kill++) achievements.NotifyEnemyKilled();
            Equal(99, achievements.SessionKillCount);
            Check(!achievements.IsAchievementUnlocked(1));
            achievements.NotifyEnemyKilled(); // Kill #100: "Slayer" (id 1).
            Check(achievements.IsAchievementUnlocked(1));
            Equal(2, unlockEvents);
            Equal(0, unlockedIds[0]);
            Equal(1, unlockedIds[1]);

            // Kills flow in from DropManager.DropLoot - the Enemy.Die seam (#34).
            var drop = CreateComponent<DropManager>(invokeAwake: true);
            var victim = CreateComponent<ProbeEnemy>(addRenderer: true);
            victim.Initialize();
            drop.DropLoot(victim);
            Equal(101, achievements.SessionKillCount);

            // Survival thresholds unlock exactly once, in order, on crossing.
            achievements.NotifySurvivalTime(599.99f);
            Check(!achievements.IsAchievementUnlocked(18));
            achievements.NotifySurvivalTime(600f);
            Check(achievements.IsAchievementUnlocked(18));
            Check(!achievements.IsAchievementUnlocked(19));
            achievements.NotifySurvivalTime(600f); // Re-notification: no double fire.
            achievements.NotifySurvivalTime(5400f); // A huge jump unlocks the rest.
            Check(achievements.IsAchievementUnlocked(19));
            Equal(4, unlockEvents);
            Equal(18, unlockedIds[2]);
            Equal(19, unlockedIds[3]);

            // SpawnManager feeds the survival trigger from the central game timer.
            save.currentSaveData = PlayerMetaSchema.Default();
            SetField(achievements, "nextSurvivalThresholdIndex", 0);
            var spawner = CreateComponent<SpawnManager>();
            Invoke(spawner, "Awake");
            spawner.playerTransform = player.entityTransform;
            spawner.enemyPrefabs = Array.Empty<Enemy>();
            Time.deltaTime = 600f;
            Invoke(spawner, "Update");
            Near(600f, spawner.GameTimer, 0f);
            Check(achievements.IsAchievementUnlocked(18), "SpawnManager must feed the survival trigger.");

            // #24: the id getter falls back to the serialized _id, and
            // AchievementManager.Awake re-syncs databases for player builds.
            object boxed = new LegacyAchievementDefinition();
            SetField(boxed, "_id", 7);
            SetField(boxed, "_title", "Stale Title");
            var staleDefinition = (LegacyAchievementDefinition)boxed;
            Equal(7, staleDefinition.id, "id must fall back to the serialized _id before syncing.");
            var playerBuildDb = ScriptableObject.CreateInstance<LegacyAchievementDatabase>();
            playerBuildDb.achievements.Add(staleDefinition);
            achievements.achievementDatabase = playerBuildDb;
            Invoke(achievements, "Awake");
            Equal(7, playerBuildDb.achievements[0].Data.Id, "Awake must re-sync serialized definitions.");
            Equal(FastMath.FastHash("Stale Title"), playerBuildDb.achievements[0].Data.TitleHash);
        }
        finally
        {
            TestRuntime.Reset();
            Directory.Delete(tempRoot, true);
            FastRandom.SetSeed(Config.Backend.Random.DefaultSeed);
        }
    }
}
