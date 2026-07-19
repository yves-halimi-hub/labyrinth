using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EFYV.Core.Controllers;
using EFYV.Core.Data;
using EFYV.Core.Data.Entities;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;
using EFYV.Core.Entities.Environment.Implementations;
using EFYV.Core.Entities.Implementations;
using EFYV.Core.Entities.Items.Merchant;
using EFYV.Core.Managers;
using EFYV.Core.Utils;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

internal static partial class Program
{
    private static void TestEntitiesPlayerIFramesInputAndProgression()
    {
        // Full Awake path: singleton claim, i-frame config, weapon system wiring, defaults.
        var playerObject = new GameObject("entities-player");
        playerObject.AddComponent<SpriteRenderer>();
        var weapons = playerObject.AddComponent<WeaponController>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        Same(player, PlayerController.Instance);
        Same(weapons, player.WeaponSystem);
        Near(Config.Game.Player.DefaultMaxHealth, player.MaxHealth, 0f);
        Near(Config.Game.Player.DefaultMaxHealth, player.CurrentHealth, 0f);
        Near(Config.Game.Player.DefaultBaseSpeed, player.BaseSpeed, 0f);
        Near(Config.Game.Player.InvincibilityFramesDuration, player.iFrameDuration, 0f);
        Equal(Config.Game.Player.InitialSessionCoins, player.SessionCoins);
        var playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        Equal(Config.Game.Player.DefaultLevel, playerData.Level);
        Near(Config.Game.Player.InitialIFrameTimer, playerData.IFrameTimer, 0f);

        // Weapon inventory capacity is the player maximum, filled exactly to the brim.
        for (int i = 0; i < Config.Game.Weapons.Inventory.PlayerMaxWeapons; i++)
            Check(weapons.TryAddWeapon(CreateComponent<ProbeWeapon>(invokeAwake: true)));
        Check(!weapons.TryAddWeapon(CreateComponent<ProbeWeapon>(invokeAwake: true)));
        weapons.activeWeapons.Clear();

        // A duplicate player is destroyed and never steals the singleton.
        var duplicateObject = new GameObject("entities-player-duplicate");
        duplicateObject.AddComponent<SpriteRenderer>();
        duplicateObject.AddComponent(typeof(PlayerController), true);
        Same(player, PlayerController.Instance);
        Check(!duplicateObject.activeSelf, "Duplicate player object must be deactivated by Destroy.");

        // Active toon identity is hashed into the schema block.
        player.ActiveToonId = "entities-hero";
        Equal("entities-hero", player.ActiveToonId);
        playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        Equal(FastMath.FastHash("entities-hero"), playerData.Block.GetInt((int)PlayerSchema.ActiveToonIdHash));

        // I-frame contract: first hit applies and arms the timer; hits inside the window are ignored.
        player.TakeDamage(10f);
        Near(90f, player.CurrentHealth, 0f);
        playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        Near(player.iFrameDuration, playerData.IFrameTimer, 0f);
        player.TakeDamage(50f);
        Near(90f, player.CurrentHealth, 0f);

        // Update ticks the window down; only expiry re-enables damage.
        Time.deltaTime = 0.3f;
        Invoke(player, "Update");
        playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        Near(0.2f, playerData.IFrameTimer, 0.0001f);
        player.TakeDamage(50f);
        Near(90f, player.CurrentHealth, 0f);
        Invoke(player, "Update");
        player.TakeDamage(15f);
        Near(75f, player.CurrentHealth, 0f);

        // Zero i-frame duration means every hit lands (timer resets to zero, never blocks).
        player.iFrameDuration = 0f;
        Invoke(player, "Update");
        Invoke(player, "Update");
        player.TakeDamage(5f);
        Near(70f, player.CurrentHealth, 0f);
        player.TakeDamage(5f);
        Near(65f, player.CurrentHealth, 0f);
        player.iFrameDuration = Config.Game.Player.InvincibilityFramesDuration;
        player.Heal(1000f);
        Near(Config.Game.Player.DefaultMaxHealth, player.CurrentHealth, 0f);

        // Input-driven movement matches an exact reference model (normalize, scale, translate, keep z).
        player.entityTransform.position = new Vector3(10f, -4f, 7f);
        var random = new Random(0xEF11);
        float expectedX = 10f;
        float expectedY = -4f;
        for (int i = 0; i < 300; i++)
        {
            float axisX = (float)((random.NextDouble() * 2.0) - 1.0);
            float axisY = (float)((random.NextDouble() * 2.0) - 1.0);
            float dt = (float)(random.NextDouble() * 0.1);
            Input.SetAxisRaw(Config.Game.Player.InputHorizontal, axisX);
            Input.SetAxisRaw(Config.Game.Player.InputVertical, axisY);
            Time.deltaTime = dt;
            Invoke(player, "Update");
            Vector2 direction = new Vector2(axisX, axisY).FastNormalized();
            float scalar = player.BaseSpeed * dt;
            expectedX += direction.x * scalar;
            expectedY += direction.y * scalar;
            Near(expectedX, player.entityTransform.position.x, 0f);
            Near(expectedY, player.entityTransform.position.y, 0f);
            Near(7f, player.entityTransform.position.z, 0f);
        }
        Input.SetAxisRaw(Config.Game.Player.InputHorizontal, 0f);
        Input.SetAxisRaw(Config.Game.Player.InputVertical, 0f);

        // Experience thresholds: level * multiplier XP levels up and resets experience to zero.
        player.GainExperience(99.5f);
        playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        Equal(1, playerData.Level);
        Near(99.5f, playerData.Experience, 0f);
        player.GainExperience(0.5f);
        playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        Equal(2, playerData.Level);
        Near(Config.Game.Player.InitialExperience, playerData.Experience, 0f);
        player.GainExperience(150f);
        playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        Equal(2, playerData.Level);
        Near(150f, playerData.Experience, 0f);
        player.GainExperience(50f);
        playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        Equal(3, playerData.Level);
        Near(Config.Game.Player.InitialExperience, playerData.Experience, 0f);

        // Session coin addition saturates at int.MaxValue (mirrors the semantics of
        // SaveManager.SaturatingAdd) instead of wrapping negative.
        player.AddSessionCoins(int.MaxValue);
        Equal(int.MaxValue, player.SessionCoins);
        player.AddSessionCoins(int.MaxValue);
        Equal(int.MaxValue, player.SessionCoins, "AddSessionCoins must saturate, never wrap negative.");
        player.AddSessionCoins(1);
        Equal(int.MaxValue, player.SessionCoins);
        Check(player.SpendSessionCoins(1), "A saturated wallet still spends normally.");
        Equal(int.MaxValue - 1, player.SessionCoins);

        // Death is a real game-over: the dead flag latches, the object despawns, the
        // static OnPlayerDied event fires exactly once even for duplicate kills, and
        // Update stops driving input.
        int playerDeaths = 0;
        Action onPlayerDied = () => playerDeaths++;
        PlayerController.OnPlayerDied += onPlayerDied;
        try
        {
            Check(!player.IsDead);
            player.Die();
            Check(player.IsDead);
            Check(Debug.Messages.Contains(Config.Game.UI.GameOverMessage));
            Check(!player.gameObject.activeSelf);
            Equal(1, playerDeaths);
            player.Die();
            Equal(1, playerDeaths, "Duplicate Die calls must not re-raise game over.");

            // A dead player ignores input entirely: Update is a no-op.
            player.gameObject.SetActive(true);
            player.entityTransform.position = new Vector3(4f, 5f, 6f);
            Input.SetAxisRaw(Config.Game.Player.InputHorizontal, 1f);
            Time.deltaTime = 0.5f;
            Invoke(player, "Update");
            Near(4f, player.entityTransform.position.x, 0f);
            Near(5f, player.entityTransform.position.y, 0f);
            Input.SetAxisRaw(Config.Game.Player.InputHorizontal, 0f);

            // Respawning through the pool contract clears the dead state.
            player.OnSpawn();
            Check(!player.IsDead);
            Near(Config.Game.Player.DefaultMaxHealth, player.CurrentHealth, 0f);
        }
        finally
        {
            PlayerController.OnPlayerDied -= onPlayerDied;
        }
    }

    private static void TestEntitiesEnemyScalingChaseAndSwapListFuzz()
    {
        // Fallback stats: an enemy without authored data spawns with the config fallback speed.
        var bare = CreateComponent<Monster>(addRenderer: true);
        bare.Initialize();
        Near(0f, bare.MaxHealth, 0f);
        bare.OnSpawn();
        Near(Config.Game.Enemy.BaseSpeedFallback, bare.BaseSpeed, 0f);
        Near(0f, bare.MaxHealth, 0f);
        bare.OnDespawn();

        // Negative damage is clamped to zero: TakeDamage never heals (healing is the
        // exclusive job of Heal, which clamps against MaxHealth).
        var living = CreateComponent<ProbeLiving>(addRenderer: true);
        living.Initialize();
        living.Apply(100f, 1f);
        living.OnSpawn();
        living.TakeDamage(-50f);
        Near(100f, living.CurrentHealth, 0f);
        living.TakeDamage(float.MinValue);
        Near(100f, living.CurrentHealth, 0f);
        living.TakeDamage(25f);
        Near(75f, living.CurrentHealth, 0f);

        // Preserve-ratio refresh against a dead block (previous max 0) snaps to full health.
        var fresh = CreateComponent<ProbeLiving>(addRenderer: true);
        fresh.Initialize();
        Near(0f, fresh.MaxHealth, 0f);
        fresh.Apply(40f, 1f, preserve: true);
        Near(40f, fresh.MaxHealth, 0f);
        Near(40f, fresh.CurrentHealth, 0f);

        // AI director scaling: spawn stats multiply authored stats by the director multipliers.
        var spawner = CreateComponent<SpawnManager>();
        Invoke(spawner, "Awake");
        var spawnerData = GetField<EFYVBackend.Core.Models.SpawnManagerData>(spawner, "Data");
        spawnerData.GameTimer = 90f;
        SetField(spawner, "Data", spawnerData);
        var director = CreateComponent<AIDirector>(invokeAwake: true);
        director.spawnManager = spawner;
        float healthMultiplier = director.GetEnemyHealthMultiplier();
        float speedMultiplier = director.GetEnemySpeedMultiplier();
        Near(1.75f, healthMultiplier);
        Near(1.3f, speedMultiplier);

        var data = ScriptableObject.CreateInstance<EnemyData>();
        FastSchemaBlock block = default;
        block.SetFloat((int)AssetSchema.MaxHealth, 80f);
        block.SetFloat((int)AssetSchema.BaseSpeed, 2f);
        block.SetFloat((int)AssetSchema.DamageToPlayer, 12f);
        block.SetFloat((int)AssetSchema.ExperienceValue, 5f);
        data.SetSchemaBlock(block);

        var monster = CreateComponent<Monster>(addRenderer: true);
        monster.Initialize();
        monster.LoadData(data);
        monster.OnSpawn();
        Near(80f * healthMultiplier, monster.MaxHealth, 0f);
        Near(80f * healthMultiplier, monster.CurrentHealth, 0f);
        Near(2f * speedMultiplier, monster.BaseSpeed, 0f);
        Near(12f, monster.DamageToPlayer, 0f);
        Near(5f, monster.ExperienceValue, 0f);

        // Live-refresh on a spawned enemy re-applies authored stats, spawn multipliers, and ratio.
        monster.TakeDamage(monster.CurrentHealth * 0.5f);
        float ratio = Mathf.Clamp01(monster.CurrentHealth / monster.MaxHealth);
        block.SetFloat((int)AssetSchema.MaxHealth, 120f);
        block.SetFloat((int)AssetSchema.BaseSpeed, 3f);
        data.SetSchemaBlock(block);
        monster.RefreshDataFromAsset();
        Near(120f * healthMultiplier, monster.MaxHealth, 0f);
        Near((120f * ratio) * healthMultiplier, monster.CurrentHealth, 0f);
        Near(3f * speedMultiplier, monster.BaseSpeed, 0f);

        // Boss phase threshold is scaled by the health multiplier at spawn time, and dying
        // straight through the threshold never raises the phase-two event.
        var bossData = ScriptableObject.CreateInstance<BossData>();
        FastSchemaBlock bossBlock = default;
        bossBlock.SetFloat((int)AssetSchema.MaxHealth, 200f);
        bossBlock.SetFloat((int)AssetSchema.BaseSpeed, 1f);
        bossBlock.SetFloat((int)AssetSchema.Phase2HealthThreshold, 60f);
        bossData.SetSchemaBlock(bossBlock);
        var boss = CreateComponent<Boss>(addRenderer: true);
        boss.Initialize();
        boss.LoadData(bossData);
        int phaseEvents = 0;
        boss.PhaseTwoStarted += () => phaseEvents++;
        boss.OnSpawn();
        Near(60f * healthMultiplier, boss.Phase2HealthThreshold, 0f);
        Equal(Config.Game.Boss.PhaseOne, boss.CurrentPhase);
        boss.TakeDamage(float.MaxValue);
        Equal(0, phaseEvents);
        Equal(Config.Game.Boss.PhaseOne, boss.CurrentPhase);
        Check(!boss.IsSpawned);

        UnityEngine.Object.Destroy(director.gameObject);
        UnityEngine.Object.Destroy(spawner.gameObject);

        // Chase movement matches an exact reference model and auto-targets the player singleton.
        var playerObject = new GameObject("entities-chase-player");
        playerObject.AddComponent<SpriteRenderer>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        var chaserData = ScriptableObject.CreateInstance<EnemyData>();
        FastSchemaBlock chaserBlock = default;
        chaserBlock.SetFloat((int)AssetSchema.MaxHealth, 50f);
        chaserBlock.SetFloat((int)AssetSchema.BaseSpeed, 3.5f);
        chaserBlock.SetFloat((int)AssetSchema.DamageToPlayer, 6f);
        chaserBlock.SetFloat((int)AssetSchema.ExperienceValue, 2f);
        chaserData.SetSchemaBlock(chaserBlock);
        var chaser = CreateComponent<Monster>(addRenderer: true);
        chaser.Initialize();
        Same(player.entityTransform, chaser.TargetPlayer);
        chaser.LoadData(chaserData);
        chaser.OnSpawn();
        Near(3.5f, chaser.BaseSpeed, 0f);

        var chaseRandom = new Random(0xC4A5E);
        for (int i = 0; i < 400; i++)
        {
            float ex = (float)((chaseRandom.NextDouble() * 200.0) - 100.0);
            float ey = (float)((chaseRandom.NextDouble() * 200.0) - 100.0);
            float ez = (float)((chaseRandom.NextDouble() * 20.0) - 10.0);
            float px = (float)((chaseRandom.NextDouble() * 200.0) - 100.0);
            float py = (float)((chaseRandom.NextDouble() * 200.0) - 100.0);
            float pz = (float)((chaseRandom.NextDouble() * 20.0) - 10.0);
            float dt = (float)(chaseRandom.NextDouble() * 0.2);
            chaser.entityTransform.position = new Vector3(ex, ey, ez);
            player.entityTransform.position = new Vector3(px, py, pz);
            Vector3 norm = new Vector3(px - ex, py - ey, pz - ez).FastNormalized();
            float scalar = chaser.BaseSpeed * dt;
            chaser.Tick(dt);
            Near(ex + (norm.x * scalar), chaser.entityTransform.position.x, 0f);
            Near(ey + (norm.y * scalar), chaser.entityTransform.position.y, 0f);
            Near(ez, chaser.entityTransform.position.z, 0f);
        }

        // Physics-stay attack path: only the player's collider triggers the attack, i-frames gate it.
        player.entityTransform.position = Vector3.zero;
        var strangerCollider = new GameObject("entities-stranger").AddComponent<Collider2D>();
        Invoke(chaser, "OnTriggerStay2D", strangerCollider);
        Near(Config.Game.Player.DefaultMaxHealth, player.CurrentHealth, 0f);
        var playerCollider = playerObject.AddComponent<Collider2D>();
        Invoke(chaser, "OnTriggerStay2D", playerCollider);
        Near(Config.Game.Player.DefaultMaxHealth - 6f, player.CurrentHealth, 0f);
        Invoke(chaser, "OnTriggerStay2D", playerCollider);
        Near(Config.Game.Player.DefaultMaxHealth - 6f, player.CurrentHealth, 0f);

        // Radius damage boundary is inclusive at exactly squaredRadius.
        Enemy.ActiveEnemies.Clear();
        ProbeEnemy onEdge = SpawnEnemy(3f, 4f, 10f);
        ProbeEnemy pastEdge = SpawnEnemy(3f, 4.001f, 10f);
        Enemy.ApplyDamageInRadius(Vector3.zero, 25f, 4f);
        Near(6f, onEdge.CurrentHealth, 0f);
        Near(10f, pastEdge.CurrentHealth, 0f);

        // Randomized swap-list fuzz: kills and resurrections keep the packed list perfectly indexed.
        Enemy.ActiveEnemies.Clear();
        var fuzzRandom = new Random(0x5AFE);
        var live = new List<ProbeEnemy>();
        for (int i = 0; i < 32; i++) live.Add(SpawnEnemy(i * 2f, -i, 10f));
        var deaths = new Dictionary<ProbeEnemy, int>();
        ProbeEnemy lastVictim = null;
        for (int round = 0; round < 300 && live.Count > 0; round++)
        {
            int victimIndex = fuzzRandom.Next(live.Count);
            ProbeEnemy victim = live[victimIndex];
            live.RemoveAt(victimIndex);
            deaths.TryGetValue(victim, out int priorDeaths);
            int countBefore = Enemy.ActiveEnemies.Count;
            victim.TakeDamage(1000f);
            deaths[victim] = priorDeaths + 1;
            lastVictim = victim;
            Equal(priorDeaths + 1, victim.DeathCalls);
            Equal(Config.Game.EnvironmentData.UnregisteredListIndex, victim.ActiveListIndex);
            Check(!victim.IsSpawned);
            Equal(countBefore - 1, Enemy.ActiveEnemies.Count);
            Equal(live.Count, Enemy.ActiveEnemies.Count);
            for (int i = 0; i < Enemy.ActiveEnemies.Count; i++)
                Equal(i, Enemy.ActiveEnemies[i].ActiveListIndex, "Packed-list invariant violated after swap removal.");
            foreach (ProbeEnemy survivor in live)
            {
                int index = survivor.ActiveListIndex;
                Check(index >= 0 && index < Enemy.ActiveEnemies.Count);
                Same(survivor, Enemy.ActiveEnemies[index]);
            }
            if (deaths[victim] == 1 && fuzzRandom.Next(3) == 0)
            {
                victim.OnSpawn();
                Near(10f, victim.CurrentHealth, 0f);
                live.Add(victim);
            }
        }
        Equal(0, live.Count);
        Equal(0, Enemy.ActiveEnemies.Count);

        // Despawning an already-despawned enemy is a guarded no-op.
        lastVictim.OnDespawn();
        Equal(0, Enemy.ActiveEnemies.Count);
        Equal(Config.Game.EnvironmentData.UnregisteredListIndex, lastVictim.ActiveListIndex);
    }

    private static int EntitiesMeasureWeaponCapacity<TEnemy>() where TEnemy : Enemy
    {
        var gameObject = new GameObject("entities-capacity-" + typeof(TEnemy).Name);
        gameObject.AddComponent<SpriteRenderer>();
        gameObject.AddComponent<WeaponController>();
        var enemy = (TEnemy)gameObject.AddComponent(typeof(TEnemy), false);
        enemy.Initialize();
        enemy.OnSpawn();
        int added = 0;
        while (added < 100 && enemy.WeaponSystem.TryAddWeapon(CreateComponent<ProbeWeapon>(invokeAwake: true))) added++;
        enemy.OnDespawn();
        return added;
    }

    private static void TestEntitiesBossWeaponSlotsAndEyeBearer()
    {
        try
        {
            // Per-archetype weapon inventory capacity is wired through Enemy.OnSpawn.
            Equal(Config.Game.Weapons.Inventory.BossMaxWeapons, EntitiesMeasureWeaponCapacity<Boss>());
            Equal(Config.Game.Weapons.Inventory.MiniBossMaxWeapons, EntitiesMeasureWeaponCapacity<MiniBoss>());
            Equal(Config.Game.Weapons.Inventory.MonsterMaxWeapons, EntitiesMeasureWeaponCapacity<Monster>());
            Equal(Config.Game.Runtime.EmptyCollectionCount, EntitiesMeasureWeaponCapacity<EvilEye>());
            Check(typeof(BossEnemy).IsAssignableFrom(typeof(Boss)));
            Check(!typeof(BossEnemy).IsAssignableFrom(typeof(MiniBoss)), "MiniBoss is a plain Enemy without boss phases.");
            Check(typeof(Enemy).IsAssignableFrom(typeof(EvilEye)));

            // Egypt theme data classes: runtime bases, unique display names, isolated schema storage.
            Check(typeof(EnemyData).IsAssignableFrom(typeof(EvilEyeData)));
            Check(typeof(BossData).IsAssignableFrom(typeof(TutData)));
            Check(typeof(BossData).IsAssignableFrom(typeof(NefertitiData)));
            Check(typeof(GameAssetData).IsAssignableFrom(typeof(PyramidsData)));
            Check(!typeof(EnemyData).IsAssignableFrom(typeof(PyramidsData)));
            Equal(Config.Game.DataConfig.SpecificEntities.MonsterEvilEye,
                typeof(EvilEyeData).GetCustomAttribute<DesignableAssetAttribute>().DisplayName);
            Equal(Config.Game.DataConfig.SpecificEntities.BossNefertiti,
                typeof(NefertitiData).GetCustomAttribute<DesignableAssetAttribute>().DisplayName);
            Equal(Config.Game.DataConfig.SpecificEntities.InteractableClosedSarcophage,
                typeof(ClosedSarcophageData).GetCustomAttribute<DesignableAssetAttribute>().DisplayName);
            var eyeDataA = ScriptableObject.CreateInstance<EvilEyeData>();
            var eyeDataB = ScriptableObject.CreateInstance<EvilEyeData>();
            FastSchemaBlock eyeBlock = default;
            eyeBlock.SetFloat((int)AssetSchema.MaxHealth, 11f);
            eyeDataA.SetSchemaBlock(eyeBlock);
            Near(11f, eyeDataA.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth), 0f);
            Near(0f, eyeDataB.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth), 0f);

            // EyeBearer death spawns exactly SpawnCount evil eyes at deterministic pooled offsets.
            var pool = CreateComponent<PoolManager>(invokeAwake: true);
            var prefab = CreateComponent<EvilEye>(addRenderer: true);
            prefab.Initialize();
            var bearerObject = new GameObject("entities-eye-bearer");
            bearerObject.AddComponent<SpriteRenderer>();
            var bearer = (EyeBearer)bearerObject.AddComponent(typeof(EyeBearer), false);
            bearer.Initialize();
            bearer.evilEyePrefab = prefab;
            bearer.entityTransform.position = new Vector3(5f, -3f, 2f);
            bearer.OnSpawn();
            Equal(1, Enemy.ActiveEnemies.Count);

            FastRandom.SetSeed(0xE7E5u);
            var expectedPositions = new Vector3[Config.Game.EyeBearer.SpawnCount];
            for (int i = 0; i < expectedPositions.Length; i++)
            {
                expectedPositions[i] = new Vector3(5f, -3f, 2f) +
                    VectorExtensions.GetRandomOffset(Config.Game.EyeBearer.SpawnOffsetRadius);
            }
            FastRandom.SetSeed(0xE7E5u);
            bearer.Die();
            Check(!bearer.IsSpawned);
            Equal(Config.Game.EyeBearer.SpawnCount, Enemy.ActiveEnemies.Count);
            var unmatched = new List<Vector3>(expectedPositions);
            for (int i = 0; i < Enemy.ActiveEnemies.Count; i++)
            {
                Enemy spawned = Enemy.ActiveEnemies[i];
                Check(spawned is EvilEye, "EyeBearer must spawn EvilEye clones.");
                NotSame(prefab, spawned);
                Check(spawned.IsSpawned);
                Equal(PoolManager.GetPoolKey(prefab.gameObject), spawned.prefabPoolKey);
                Vector3 actual = spawned.entityTransform.position;
                float dx = actual.x - 5f;
                float dy = actual.y - (-3f);
                Check((dx * dx) + (dy * dy) <=
                    (Config.Game.EyeBearer.SpawnOffsetRadius * Config.Game.EyeBearer.SpawnOffsetRadius) + 0.0001f);
                Near(2f, actual.z, 0f);
                int match = unmatched.FindIndex(expected =>
                    expected.x == actual.x && expected.y == actual.y && expected.z == actual.z);
                Check(match >= 0, "Spawn position did not match the deterministic PRNG replay.");
                unmatched.RemoveAt(match);
            }
            Equal(0, unmatched.Count);

            // The spawned eyes are pool-owned: despawn returns them for identity-preserving reuse.
            Enemy firstEye = Enemy.ActiveEnemies[0];
            pool.Despawn(firstEye);
            Equal(Config.Game.EyeBearer.SpawnCount - 1, Enemy.ActiveEnemies.Count);
            GameEntity rentedBack = pool.SpawnByKey(PoolManager.GetPoolKey(prefab.gameObject), new Vector3(1f, 1f, 1f), Quaternion.identity);
            Same(firstEye, rentedBack);
            Equal(Config.Game.EyeBearer.SpawnCount, Enemy.ActiveEnemies.Count);

            // A bearer without a prefab still dies cleanly and spawns nothing.
            var lonelyObject = new GameObject("entities-lonely-bearer");
            lonelyObject.AddComponent<SpriteRenderer>();
            var lonely = (EyeBearer)lonelyObject.AddComponent(typeof(EyeBearer), false);
            lonely.Initialize();
            lonely.OnSpawn();
            Equal(Config.Game.EyeBearer.SpawnCount + 1, Enemy.ActiveEnemies.Count);
            lonely.Die();
            Check(!lonely.IsSpawned);
            Equal(Config.Game.EyeBearer.SpawnCount, Enemy.ActiveEnemies.Count);
        }
        finally
        {
            FastRandom.SetSeed(13371337u);
        }
    }

    private static void TestEntitiesProjectilePiercingReuseAndTriggerFilter()
    {
        // Pierce count 0: the very first hit consumes the projectile after applying damage once.
        var single = CreateComponent<ProbeProjectile>(addRenderer: true);
        single.Initialize();
        single.Initialize(new Vector2(1f, 0f), 5f, 2f, 0);
        single.OnSpawn();
        var target = new ProbeDamageable();
        single.OnHit(target);
        Near(5f, target.TotalDamage, 0f);
        Check(!single.IsSpawned);
        Equal(1, single.DespawnCalls);
        Equal(0, Projectile.ActiveProjectiles.Count);

        // Zero-direction projectile: never moves, but its lifetime still expires.
        var still = CreateComponent<ProbeProjectile>(addRenderer: true);
        still.Initialize();
        still.Initialize(Vector2.zero, 1f, 50f, 9);
        Near(0f, still.Direction.x, 0f);
        Near(0f, still.Direction.y, 0f);
        still.entityTransform.position = new Vector3(4f, 5f, 6f);
        still.OnSpawn();
        still.Tick(1f);
        Near(4f, still.entityTransform.position.x, 0f);
        Near(5f, still.entityTransform.position.y, 0f);
        Check(still.IsSpawned);
        still.Tick(Config.Game.Weapons.Projectile.DefaultLifetime);
        Check(!still.IsSpawned);

        // Ticking a despawned projectile is inert: no movement and no lifetime drain.
        float lifetimeAfter = GetField<EFYVBackend.Core.Models.ProjectileData>(still, "projectileData").RemainingLifetime;
        still.Tick(123f);
        Near(lifetimeAfter, GetField<EFYVBackend.Core.Models.ProjectileData>(still, "projectileData").RemainingLifetime, 0f);
        Near(4f, still.entityTransform.position.x, 0f);

        // Initialize normalizes any direction exactly like the shared fast-normalize path.
        var directionRandom = new Random(0xD1CE);
        for (int i = 0; i < 100; i++)
        {
            float dx = (float)((directionRandom.NextDouble() * 20.0) - 10.0);
            float dy = (float)((directionRandom.NextDouble() * 20.0) - 10.0);
            if (MathF.Abs(dx) + MathF.Abs(dy) < 0.01f) dx = 1f;
            still.Initialize(new Vector2(dx, dy), 1f, 1f, 1);
            Vector2 expected = new Vector2(dx, dy).FastNormalized();
            Near(expected.x, still.Direction.x, 0f);
            Near(expected.y, still.Direction.y, 0f);
        }

        // Multi-tick flight matches an exact reference model until lifetime expiry.
        var mover = CreateComponent<ProbeProjectile>(addRenderer: true);
        mover.Initialize();
        mover.Initialize(new Vector2(-2.5f, 1.75f), 1f, 7.5f, 99);
        mover.entityTransform.position = new Vector3(1f, 2f, 3f);
        mover.OnSpawn();
        Vector2 direction = mover.Direction;
        float expectedX = 1f;
        float expectedY = 2f;
        float remaining = Config.Game.Weapons.Projectile.DefaultLifetime;
        var flightRandom = new Random(0x9903);
        for (int i = 0; i < 200 && mover.IsSpawned; i++)
        {
            float dt = (float)(flightRandom.NextDouble() * 0.4);
            mover.Tick(dt);
            remaining -= dt;
            if (remaining <= Config.Game.Weapons.Projectile.LifetimeExpiredThreshold)
            {
                Check(!mover.IsSpawned);
                break;
            }
            float scalar = mover.Speed * dt;
            expectedX += direction.x * scalar;
            expectedY += direction.y * scalar;
            Near(expectedX, mover.entityTransform.position.x, 0f);
            Near(expectedY, mover.entityTransform.position.y, 0f);
            Near(3f, mover.entityTransform.position.z, 0f);
        }
        Check(!mover.IsSpawned, "Projectile must expire within the reference flight window.");

        // Pool round trip: identity-preserving reuse, and OnSpawn resets the spent
        // pierce counter (#24) so a reused projectile gets its full pierce budget back.
        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var prefab = CreateComponent<ProbeProjectile>(addRenderer: true);
        prefab.Initialize();
        var rented = (ProbeProjectile)pool.Spawn(prefab, Vector3.zero, Quaternion.identity);
        Check(rented != null && rented.IsSpawned);
        Equal(1, Projectile.ActiveProjectiles.Count);
        rented.Initialize(new Vector2(1f, 0f), 2f, 3f, 2);
        var pooledTarget = new ProbeDamageable();
        rented.OnHit(pooledTarget);
        Near(2f, pooledTarget.TotalDamage, 0f);
        Check(rented.IsSpawned, "The first hit of a two-pierce projectile must not despawn it.");
        rented.OnHit(pooledTarget);
        Near(4f, pooledTarget.TotalDamage, 0f);
        Check(!rented.IsSpawned);
        Equal(0, Projectile.ActiveProjectiles.Count);
        var reused = (ProbeProjectile)pool.SpawnByKey(PoolManager.GetPoolKey(prefab.gameObject), Vector3.zero, Quaternion.identity);
        Same(rented, reused);
        Check(reused.IsSpawned);
        Equal(1, Projectile.ActiveProjectiles.Count);
        reused.OnHit(pooledTarget);
        Near(6f, pooledTarget.TotalDamage, 0f);
        Check(reused.IsSpawned, "OnSpawn must reset the spent pierce counter on pool reuse.");
        reused.OnHit(pooledTarget);
        Near(8f, pooledTarget.TotalDamage, 0f);
        Check(!reused.IsSpawned);

        // Trigger filter: a player-owned projectile hits only Enemy damageables; the
        // player and plain objects are ignored.
        var playerObject = new GameObject("entities-projectile-player");
        playerObject.AddComponent<SpriteRenderer>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        var playerCollider = playerObject.AddComponent<Collider2D>();
        Enemy.ActiveEnemies.Clear();
        ProbeEnemy enemy = SpawnEnemy(0f, 0f, 30f);
        var enemyCollider = enemy.gameObject.AddComponent<Collider2D>();
        var neutralCollider = new GameObject("entities-neutral").AddComponent<Collider2D>();

        var hunter = CreateComponent<ProbeProjectile>(addRenderer: true);
        hunter.Initialize();
        hunter.Initialize(new Vector2(1f, 0f), 4f, 1f, 99);
        hunter.OnSpawn();
        Invoke(hunter, "OnTriggerEnter2D", playerCollider);
        Near(Config.Game.Player.DefaultMaxHealth, player.CurrentHealth, 0f);
        Check(hunter.IsSpawned);
        Invoke(hunter, "OnTriggerEnter2D", neutralCollider);
        Check(hunter.IsSpawned);
        Invoke(hunter, "OnTriggerEnter2D", enemyCollider);
        Near(26f, enemy.CurrentHealth, 0f);

        // Component-lookup memo (#24): re-triggering the same collider skips the
        // GetComponent scan entirely while still applying damage.
        long lookupsBefore = GameObject.GetComponentCalls;
        Invoke(hunter, "OnTriggerEnter2D", enemyCollider);
        Near(22f, enemy.CurrentHealth, 0f);
        Equal(lookupsBefore, GameObject.GetComponentCalls,
            "A repeated trigger from the memoized collider must not re-scan components.");

        // OnSpawn clears the memo: after a pool round trip the same collider is
        // freshly resolved (no stale cross-flight component reference).
        hunter.OnDespawn();
        hunter.OnSpawn();
        lookupsBefore = GameObject.GetComponentCalls;
        Invoke(hunter, "OnTriggerEnter2D", enemyCollider);
        Near(18f, enemy.CurrentHealth, 0f);
        Check(GameObject.GetComponentCalls > lookupsBefore, "OnSpawn must clear the trigger memo.");

        // Faction gate (#21): an enemy-owned projectile ignores enemies and damages
        // the player instead.
        var enemyShot = CreateComponent<ProbeProjectile>(addRenderer: true);
        enemyShot.Initialize();
        enemyShot.Initialize(new Vector2(1f, 0f), 6f, 1f, 99, Faction.Enemy);
        Equal(Faction.Enemy, enemyShot.OwnerFaction);
        enemyShot.OnSpawn();
        Invoke(enemyShot, "OnTriggerEnter2D", enemyCollider);
        Near(18f, enemy.CurrentHealth, 0f);
        Check(enemyShot.IsSpawned);
        Invoke(enemyShot, "OnTriggerEnter2D", neutralCollider);
        Near(Config.Game.Player.DefaultMaxHealth, player.CurrentHealth, 0f);
        Invoke(enemyShot, "OnTriggerEnter2D", playerCollider);
        Near(Config.Game.Player.DefaultMaxHealth - 6f, player.CurrentHealth, 0f);

        // Re-Initialize back to the player's side re-arms it against enemies only.
        enemyShot.Initialize(new Vector2(1f, 0f), 6f, 1f, 99);
        Equal(Faction.Player, enemyShot.OwnerFaction);
        Invoke(enemyShot, "OnTriggerEnter2D", enemyCollider);
        Near(12f, enemy.CurrentHealth, 0f);
        Near(Config.Game.Player.DefaultMaxHealth - 6f, player.CurrentHealth, 0f);
    }

    private static void TestEntitiesPropAnimationDoorsChestsAndCoins()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "entities-prop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Application.persistentDataPath = tempRoot;
        try
        {
            // Spawn randomization is a deterministic PRNG replay: frame index then timer offset.
            var prop = CreateComponent<ProbeProp>(addRenderer: true);
            prop.Initialize();
            var frames = new Sprite[5];
            for (int i = 0; i < frames.Length; i++) frames[i] = new Sprite { name = "entities-frame-" + i };
            prop.animationFrames = frames;
            prop.animationSpeed = 0.3f;
            FastRandom.SetSeed(0xA111u);
            int expectedFrame = FastRandom.Range(Config.Game.Runtime.FirstIndex, frames.Length);
            float expectedTimer = FastRandom.Range(Config.Game.EnvironmentData.InitialAnimTimer, prop.animationSpeed);
            FastRandom.SetSeed(0xA111u);
            prop.OnSpawn();
            Equal(expectedFrame, prop.Frame);
            Near(expectedTimer, prop.Timer, 0f);
            Same(frames[expectedFrame], prop.spriteRenderer.sprite);
            Equal(1, PropEntity.ActiveAnimatedProps.Count);
            Equal(0, prop.ActiveListIndex);

            // Animation state machine matches a naive reference model step for step.
            float referenceTimer = expectedTimer;
            int referenceFrame = expectedFrame;
            var animationRandom = new Random(0x7A6);
            for (int i = 0; i < 600; i++)
            {
                float dt = (float)(animationRandom.NextDouble() * 0.5);
                prop.TickAnimation(dt);
                referenceTimer += dt;
                if (referenceTimer >= 0.3f)
                {
                    referenceTimer -= 0.3f;
                    referenceFrame++;
                    if (referenceFrame >= frames.Length) referenceFrame = Config.Game.EnvironmentData.AnimationLoopStartFrame;
                }
                Equal(referenceFrame, prop.Frame);
                Near(referenceTimer, prop.Timer, 0f);
                Same(frames[referenceFrame], prop.spriteRenderer.sprite);
            }
            prop.OnDespawn();
            Equal(0, PropEntity.ActiveAnimatedProps.Count);
            Equal(Config.Game.EnvironmentData.UnregisteredListIndex, prop.ActiveListIndex);

            // A frameless prop never registers in the animated list and despawns safely.
            var frameless = CreateComponent<ProbeProp>(addRenderer: true);
            frameless.Initialize();
            frameless.OnSpawn();
            Equal(0, PropEntity.ActiveAnimatedProps.Count);
            frameless.OnDespawn();

            // animationSpeed sanitization (#24): zero, negative, and NaN speeds clamp
            // to the documented minimum instead of thrashing a frame per tick or
            // inverting the OnSpawn timer randomization range.
            var brokenProp = CreateComponent<ProbeProp>(addRenderer: true);
            brokenProp.Initialize();
            brokenProp.animationSpeed = 0f;
            Near(PropEntity.MinimumAnimationSpeed, brokenProp.animationSpeed, 0f);
            brokenProp.animationSpeed = -3.5f;
            Near(PropEntity.MinimumAnimationSpeed, brokenProp.animationSpeed, 0f);
            brokenProp.animationSpeed = float.NaN;
            Near(PropEntity.MinimumAnimationSpeed, brokenProp.animationSpeed, 0f);
            brokenProp.animationSpeed = 0.75f;
            Near(0.75f, brokenProp.animationSpeed, 0f);
            // The serialized-field path (OnValidate/Initialize sync) clamps too.
            SetField(brokenProp, "serializedAnimationSpeed", 0f);
            Invoke(brokenProp, "OnValidate");
            Near(PropEntity.MinimumAnimationSpeed, brokenProp.animationSpeed, 0f);
            Near(PropEntity.MinimumAnimationSpeed, GetField<float>(brokenProp, "serializedAnimationSpeed"), 0f);
            // A sanitized speed keeps the OnSpawn timer range ordered (timer < speed),
            // and a whole animation cycle still advances frames without thrash.
            brokenProp.animationFrames = frames;
            SetField(brokenProp, "serializedAnimationSpeed", -1f);
            Invoke(brokenProp, "OnValidate");
            FastRandom.SetSeed(0xA112u);
            brokenProp.OnSpawn();
            Check(brokenProp.Timer >= 0f && brokenProp.Timer <= PropEntity.MinimumAnimationSpeed,
                "The randomized start timer must stay within the sanitized speed range.");
            int frameBefore = brokenProp.Frame;
            brokenProp.TickAnimation(PropEntity.MinimumAnimationSpeed);
            Equal((frameBefore + 1) % frames.Length, brokenProp.Frame % frames.Length);
            brokenProp.OnDespawn();
            Equal(0, PropEntity.ActiveAnimatedProps.Count);

            // OnValidate/Reset round trip serialized inspector state through the schema block.
            var gem = CreateComponent<XPGem>(addRenderer: true);
            gem.Initialize();
            Check(!gem.IsBlocking);
            Near(Config.Game.EnvironmentData.DefaultXPGemValue, gem.xpValue, 0f);
            SetField(gem, "serializedXpValue", 55.5f);
            SetField(gem, "serializedIsBlocking", true);
            SetField(gem, "serializedAnimationSpeed", 0.75f);
            Invoke(gem, "OnValidate");
            Near(55.5f, gem.xpValue, 0f);
            Check(gem.IsBlocking);
            Near(0.75f, gem.animationSpeed, 0f);
            Invoke(gem, "Reset");
            Near(Config.Game.EnvironmentData.DefaultXPGemValue, gem.xpValue, 0f);
            Check(!gem.IsBlocking);
            Near(Config.Game.EnvironmentData.DefaultAnimationSpeed, gem.animationSpeed, 0f);

            // TreeProp forces itself non-blocking even against adversarial serialized state.
            var treeObject = new GameObject("entities-tree");
            treeObject.AddComponent<SpriteRenderer>();
            var tree = (TreeProp)treeObject.AddComponent(typeof(TreeProp), false);
            SetField(tree, "serializedIsBlocking", true);
            tree.Initialize();
            Check(!tree.IsBlocking, "TreeProp.Initialize must force non-blocking.");

            // XPGem interact grants exactly enough XP to level the player, then self-despawns.
            var playerObject = new GameObject("entities-prop-player");
            playerObject.AddComponent<SpriteRenderer>();
            playerObject.AddComponent<WeaponController>();
            var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
            gem.xpValue = 100f;
            gem.OnSpawn();
            gem.OnInteract(player);
            var playerData = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
            Equal(2, playerData.Level);
            Near(Config.Game.Player.InitialExperience, playerData.Experience, 0f);
            Check(!gem.IsSpawned);

            // DoorProp: target-map hash write-through on set, validate, and initialize.
            var doorObject = new GameObject("entities-door");
            doorObject.AddComponent<SpriteRenderer>();
            var door = (DoorProp)doorObject.AddComponent(typeof(DoorProp), false);
            door.Initialize();
            Equal(FastMath.FastHash(null),
                GetField<EFYVBackend.Core.Models.EntityData>(door, "Data").Block.GetInt((int)DoorPropSchema.TargetMapIdHash));
            door.TargetMapId = "entities-map";
            Equal("entities-map", door.TargetMapId);
            Equal(FastMath.FastHash("entities-map"),
                GetField<EFYVBackend.Core.Models.EntityData>(door, "Data").Block.GetInt((int)DoorPropSchema.TargetMapIdHash));
            SetField(door, "_targetMapId", "entities-map-2");
            Invoke(door, "OnValidate");
            Equal("entities-map-2", door.TargetMapId);
            Equal(FastMath.FastHash("entities-map-2"),
                GetField<EFYVBackend.Core.Models.EntityData>(door, "Data").Block.GetInt((int)DoorPropSchema.TargetMapIdHash));

            // Empty map id: warning only, no switch attempted.
            door.TargetMapId = "";
            door.OnInteract(player);
            Check(Debug.Messages.Contains(Config.Game.Map.LogTargetMapIdEmpty));

            // Valid map id routes through MapManager: enemies unloaded, player repositioned.
            CreateComponent<PoolManager>(invokeAwake: true);
            CreateComponent<MapManager>(invokeAwake: true);
            Enemy.ActiveEnemies.Clear();
            ProbeEnemy straggler = SpawnEnemy(1f, 1f, 50f);
            door.TargetMapId = "entities-map-3";
            player.entityTransform.position = new Vector3(9f, 9f, 0f);
            door.OnInteract(player);
            Equal(0, Enemy.ActiveEnemies.Count);
            Check(!straggler.IsSpawned);
            Near(0f, player.entityTransform.position.x, 0f);
            Near(0f, player.entityTransform.position.y, 0f);
            Check(Debug.Messages.Contains(string.Format(Config.Game.Map.LogSwitchingToMap, "entities-map-3")));
            Check(Debug.Messages.Contains(string.Format(Config.Game.Map.LogMapManagerSwitchSuccess, "entities-map-3")));

            // ChestProp reward table: grade drives the number of upgrade choices requested.
            var upgrades = CreateComponent<UpgradeManager>(invokeAwake: true);
            var weapon = CreateComponent<ProbeWeapon>(invokeAwake: true);
            weapon.Configure(1f, 1f, 1);
            Check(player.WeaponSystem.TryAddWeapon(weapon));
            var chestObject = new GameObject("entities-chest");
            chestObject.AddComponent<SpriteRenderer>();
            var chest = (ChestProp)chestObject.AddComponent(typeof(ChestProp), false);
            chest.Initialize();
            Check(chest.IsBlocking);
            Equal(Config.Game.Drops.EnemyChestGrade, chest.Grade);
            int requested = -1;
            upgrades.OnNormalUpgradesRequested += count => requested = count;
            foreach ((int grade, int rewards) in new[]
            {
                (1, Config.Game.Drops.ChestGrade1Rewards),
                (Config.Game.Drops.Grade2, Config.Game.Drops.ChestGrade2Rewards),
                (Config.Game.Drops.Grade3, Config.Game.Drops.ChestGrade3Rewards)
            })
            {
                chest.OnSpawn();
                chest.InitializeGrade(grade);
                Equal(grade, chest.Grade);
                requested = -1;
                chest.OnInteract(player);
                Equal(rewards, requested);
                Check(!chest.IsSpawned);
            }

            // CoinProp interact: session coins + persistent toon coins, sprite guarded by array length.
            var save = CreateComponent<SaveManager>(invokeAwake: true);
            save.currentSaveData = PlayerMetaSchema.Default();
            player.ActiveToonId = "entities-toon";
            var coinObject = new GameObject("entities-coin");
            coinObject.AddComponent<SpriteRenderer>();
            var coin = (CoinProp)coinObject.AddComponent(typeof(CoinProp), false);
            coin.Initialize();
            Check(!coin.IsBlocking);
            Equal(Config.Game.Drops.MinCoinGrade, coin.Grade);
            Equal(Config.Game.Drops.BaseCoinValue, coin.Value);
            coin.gradeSprites = new[] { new Sprite(), new Sprite() };
            Sprite spriteBefore = coin.spriteRenderer.sprite;
            coin.OnSpawn();
            coin.InitializeGrade(3);
            Equal(3, coin.Grade);
            Same(spriteBefore, coin.spriteRenderer.sprite);
            int expectedValue = Config.Game.Drops.BaseCoinValue * 3 * 3;
            Equal(expectedValue, coin.Value);
            int sessionBefore = player.SessionCoins;
            coin.OnInteract(player);
            Equal(sessionBefore + expectedValue, player.SessionCoins);
            Check(save.currentSaveData.TryGetToonBlock(0, out FastSchemaBlock toon));
            Equal(FastMath.FastHash("entities-toon"), toon.GetInt((int)ToonSchema.ToonIdHash));
            Equal(expectedValue, toon.GetInt((int)ToonSchema.TotalCoinsCollected));
            Equal(expectedValue, save.currentSaveData.TotalCoinsCollected);
            Check(!coin.IsSpawned);
        }
        finally
        {
            FastRandom.SetSeed(13371337u);
            TestRuntime.Reset();
            Directory.Delete(tempRoot, true);
        }
    }

    private static uint EntitiesFindSeedWhere(Func<float, bool> predicate)
    {
        for (uint seed = 1; seed <= 100000; seed++)
        {
            FastRandom.SetSeed(seed);
            if (predicate(FastRandom.Range(Config.Game.Runtime.UnitIntervalMin, Config.Game.Runtime.UnitIntervalMax)))
                return seed;
        }
        throw new InvalidOperationException("No seed found for the requested branch.");
    }

    private static void TestEntitiesMerchantSarcophageAndPurchasables()
    {
        try
        {
            // Purchasable schema write-through: names, ids, and amounts land in the hashed block slots.
            var healing = new HealingPurchase("entities-heal", 10, 25f);
            var healingData = GetField<EFYVBackend.Core.Models.PurchasableData>(healing, "Data");
            Equal(FastMath.FastHash("entities-heal"), healingData.Block.GetInt((int)PurchasableSchema.ItemNameHash));
            Equal(10, healingData.Block.GetInt((int)PurchasableSchema.Cost));
            Near(25f, healingData.Block.GetFloat((int)PurchasableSchema.HealAmount), 0f);
            var buff = new TemporaryBuffPurchase("entities-buff", 7, "entities-speed", 12.5f);
            var buffData = GetField<EFYVBackend.Core.Models.PurchasableData>(buff, "Data");
            Equal(FastMath.FastHash("entities-buff"), buffData.Block.GetInt((int)PurchasableSchema.ItemNameHash));
            Equal(FastMath.FastHash("entities-speed"), buffData.Block.GetInt((int)PurchasableSchema.BuffIdHash));
            Near(12.5f, buffData.Block.GetFloat((int)PurchasableSchema.Duration), 0f);
            var weaponPurchase = new WeaponUpgradePurchase("entities-weapon", 9, "entities-wand");
            var weaponData = GetField<EFYVBackend.Core.Models.PurchasableData>(weaponPurchase, "Data");
            Equal(FastMath.FastHash("entities-wand"), weaponData.Block.GetInt((int)PurchasableSchema.WeaponIdHash));

            // Weapon upgrade purchase fails without a weapon system, succeeds with one.
            var lonerObject = new GameObject("entities-loner");
            lonerObject.AddComponent<SpriteRenderer>();
            var loner = (PlayerController)lonerObject.AddComponent(typeof(PlayerController), false);
            loner.Initialize();
            Check(loner.WeaponSystem == null);
            Check(!weaponPurchase.Apply(loner));

            var playerObject = new GameObject("entities-merchant-player");
            playerObject.AddComponent<SpriteRenderer>();
            playerObject.AddComponent<WeaponController>();
            var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
            Same(player, PlayerController.Instance);
            var upgrades = CreateComponent<UpgradeManager>(invokeAwake: true);
            int specialRequests = 0;
            int normalRequests = 0;
            int normalChoices = -1;
            upgrades.OnSpecialAttacksRequested += (count, penalty) => specialRequests++;
            upgrades.OnNormalUpgradesRequested += count => { normalRequests++; normalChoices = count; };
            Check(weaponPurchase.Apply(player));
            // A weaponless player still has free weapon slots, so the purchase offers
            // NORMAL upgrades (#22) instead of flipping into the special-attack phase.
            Equal(0, specialRequests);
            Equal(1, normalRequests);
            Equal(Config.Game.Weapons.Inventory.UpgradeChoicesNormalPhase, normalChoices);
            Check(!upgrades.IsSpecialAttackPhase,
                "An empty weapon inventory must never trigger the special-attack phase.");

            // Merchant inventory generation matches the PRNG reference model across a seed sweep.
            int healingSeen = 0;
            int buffSeen = 0;
            int weaponSeen = 0;
            for (uint seed = 1; seed <= 24; seed++)
            {
                FastRandom.SetSeed(seed);
                var expectedTypes = new Type[Config.Game.Merchant.BaseItemChoices];
                for (int i = 0; i < expectedTypes.Length; i++)
                {
                    float roll = FastRandom.Range(Config.Game.Runtime.UnitIntervalMin, Config.Game.Runtime.UnitIntervalMax);
                    expectedTypes[i] = roll < Config.Game.Merchant.RollThresholdChicken ? typeof(HealingPurchase)
                        : roll < Config.Game.Merchant.RollThresholdPotion ? typeof(TemporaryBuffPurchase)
                        : typeof(WeaponUpgradePurchase);
                }
                var merchantObject = new GameObject("entities-merchant-" + seed);
                merchantObject.AddComponent<SpriteRenderer>();
                var merchant = (BaseMerchantProp)merchantObject.AddComponent(typeof(BaseMerchantProp), false);
                FastRandom.SetSeed(seed);
                merchant.Initialize();
                Check(merchant.IsBlocking, "Merchants must block movement.");
                var items = GetField<List<PurchasableItem>>(merchant, "_availableItems");
                Equal(Config.Game.Merchant.BaseItemChoices, items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    Equal(expectedTypes[i], items[i].GetType());
                    if (items[i] is HealingPurchase chicken)
                    {
                        healingSeen++;
                        Equal(Config.Game.Merchant.ChickenName, chicken.ItemName);
                        Equal(Config.Game.Merchant.ChickenCost, chicken.Cost);
                        Near(Config.Game.Merchant.ChickenHeal, chicken.HealAmount, 0f);
                    }
                    else if (items[i] is TemporaryBuffPurchase potion)
                    {
                        buffSeen++;
                        Equal(Config.Game.Merchant.PotionName, potion.ItemName);
                        Equal(Config.Game.Merchant.PotionCost, potion.Cost);
                        Equal(Config.Game.Merchant.PotionBuffId, potion.BuffId);
                        Near(Config.Game.Merchant.PotionDuration, potion.Duration, 0f);
                    }
                    else
                    {
                        weaponSeen++;
                        var mystery = (WeaponUpgradePurchase)items[i];
                        Equal(Config.Game.Merchant.MysteryWeaponName, mystery.ItemName);
                        Equal(Config.Game.Merchant.MysteryWeaponCost, mystery.Cost);
                        Equal(Config.Game.Merchant.MysteryWeaponId, mystery.WeaponId);
                    }
                }
            }
            Check(healingSeen > 0 && buffSeen > 0 && weaponSeen > 0,
                "Seed sweep must exercise all three merchant item archetypes.");

            // Merchant interact (#34, flipped from the prototype auto-buy): touching
            // a merchant only raises the interaction request event - the wallet and
            // the stock are untouched until an explicit AttemptPurchase.
            uint healSeed = 0;
            for (uint seed = 1; seed <= 200 && healSeed == 0; seed++)
            {
                FastRandom.SetSeed(seed);
                if (FastRandom.Range(Config.Game.Runtime.UnitIntervalMin, Config.Game.Runtime.UnitIntervalMax) <
                    Config.Game.Merchant.RollThresholdChicken)
                {
                    healSeed = seed;
                }
            }
            Check(healSeed != 0);
            BaseMerchantProp observedMerchant = null;
            List<PurchasableItem> observedItems = null;
            int interactionEvents = 0;
            Action<BaseMerchantProp, List<PurchasableItem>> listener = (merchant, list) =>
            {
                observedMerchant = merchant;
                observedItems = list;
                interactionEvents++;
            };
            BaseMerchantProp.OnMerchantInteracted += listener;
            try
            {
                var shopObject = new GameObject("entities-shop");
                shopObject.AddComponent<SpriteRenderer>();
                var shop = (BaseMerchantProp)shopObject.AddComponent(typeof(BaseMerchantProp), false);
                FastRandom.SetSeed(healSeed);
                shop.Initialize();
                var stock = GetField<List<PurchasableItem>>(shop, "_availableItems");
                Check(stock[0] is HealingPurchase);
                var chicken = (HealingPurchase)stock[0];

                player.AddSessionCoins(1000);
                player.TakeDamage(40f);
                float woundedHealth = player.CurrentHealth;
                shop.OnInteract(player);
                Same(shop, observedMerchant);
                Same(stock, observedItems);
                Equal(1, interactionEvents);
                Equal(1000, player.SessionCoins, "Contact must never auto-buy (#34).");
                Near(woundedHealth, player.CurrentHealth, 0f);
                Equal(Config.Game.Merchant.BaseItemChoices, stock.Count, "Stock is untouched by contact.");

                // Re-entering re-raises the request without any side effects.
                shop.OnInteract(player);
                Equal(2, interactionEvents);
                Equal(1000, player.SessionCoins);
                Equal(Config.Game.Merchant.BaseItemChoices, stock.Count);

                // The explicit purchase API buys the offered chicken: coins deducted,
                // heal applied, stock reduced.
                Check(shop.AttemptPurchase(player, chicken));
                Equal(1000 - chicken.Cost, player.SessionCoins);
                Near(woundedHealth + Config.Game.Merchant.ChickenHeal, player.CurrentHealth, 0f);
                Equal(Config.Game.Merchant.BaseItemChoices - 1, stock.Count);

                // Failed apply at full health refunds the deducted coins and keeps the item.
                player.Heal(1000f);
                int coinsBefore = player.SessionCoins;
                Check(!shop.AttemptPurchase(player, new HealingPurchase("entities-refund", 25, 10f)));
                Equal(coinsBefore, player.SessionCoins);

                // Unaffordable item: warning logged, wallet untouched.
                int logCount = Debug.Messages.Count;
                Check(!shop.AttemptPurchase(player, new HealingPurchase("entities-overpriced", int.MaxValue, 10f)));
                Equal(coinsBefore, player.SessionCoins);
                Check(Debug.Messages.Count > logCount);
            }
            finally
            {
                BaseMerchantProp.OnMerchantInteracted -= listener;
            }

            // Sarcophage branch coverage: seeds scanned per branch, then deterministically replayed.
            CreateComponent<PoolManager>(invokeAwake: true);
            CreateComponent<MapManager>(invokeAwake: true);
            var mummyPrefab = CreateComponent<Monster>(addRenderer: true);
            mummyPrefab.Initialize();
            var boxObject = new GameObject("entities-sarcophage");
            boxObject.AddComponent<SpriteRenderer>();
            var box = (SarcophageProp)boxObject.AddComponent(typeof(SarcophageProp), false);
            box.Initialize();
            box.MummyPrefab = mummyPrefab;
            box.PossibleRandomMapIds.Add("entities-crypt-a");
            box.PossibleRandomMapIds.Add("entities-crypt-b");
            box.entityTransform.position = new Vector3(-2f, 3f, 0f);

            // Teleport branch: replay the roll to predict the chosen map, then verify the switch.
            uint teleportSeed = EntitiesFindSeedWhere(roll => roll <= Config.Game.Map.SarcophageTeleportChance);
            FastRandom.SetSeed(teleportSeed);
            FastRandom.Range(Config.Game.Runtime.UnitIntervalMin, Config.Game.Runtime.UnitIntervalMax);
            string expectedMap = box.PossibleRandomMapIds[
                FastRandom.Range(Config.Game.Runtime.FirstIndex, box.PossibleRandomMapIds.Count)];
            Enemy.ActiveEnemies.Clear();
            box.OnSpawn();
            FastRandom.SetSeed(teleportSeed);
            box.OnInteract(player);
            Check(Debug.Messages.Contains(string.Format(Config.Game.Map.LogSarcophageTeleport, expectedMap)));
            Check(Debug.Messages.Contains(string.Format(Config.Game.Map.LogMapManagerSwitchSuccess, expectedMap)));
            Check(!box.IsSpawned);

            // Ambush branch: exact circle-distribution spawn positions via the shared Taylor trig.
            uint ambushSeed = EntitiesFindSeedWhere(roll =>
                roll > Config.Game.Map.SarcophageTeleportChance && roll <= Config.Game.Map.SarcophageSpawnEnemyChance);
            Enemy.ActiveEnemies.Clear();
            box.OnSpawn();
            FastRandom.SetSeed(ambushSeed);
            box.OnInteract(player);
            Equal(Config.Game.Map.SarcophageAmbushCount, Enemy.ActiveEnemies.Count);
            for (int i = 0; i < Enemy.ActiveEnemies.Count; i++)
            {
                float rad = FastMath.GetCircleDistributionAngleRad(i, Config.Game.Map.SarcophageAmbushCount);
                FastMath.FastSinCosTaylor(rad, out float sin, out float cos);
                Vector3 expectedPosition = new Vector3(-2f, 3f, 0f) + new Vector3(
                    cos * Config.Game.Map.SarcophageAmbushRadius,
                    sin * Config.Game.Map.SarcophageAmbushRadius,
                    Config.Game.EnvironmentData.PlanarZOffset);
                Check(Enemy.ActiveEnemies[i] is Monster);
                Near(expectedPosition.x, Enemy.ActiveEnemies[i].entityTransform.position.x, 0f);
                Near(expectedPosition.y, Enemy.ActiveEnemies[i].entityTransform.position.y, 0f);
                Near(expectedPosition.z, Enemy.ActiveEnemies[i].entityTransform.position.z, 0f);
            }
            Check(Debug.Messages.Contains(Config.Game.Map.LogSarcophageAmbush));
            Check(!box.IsSpawned);

            // Trap branch: flat config damage to the player once i-frames have expired.
            uint trapSeed = EntitiesFindSeedWhere(roll =>
                roll > Config.Game.Map.SarcophageSpawnEnemyChance && roll <= Config.Game.Map.SarcophageTrapChance);
            player.Heal(1000f);
            Time.deltaTime = 1f;
            Invoke(player, "Update");
            box.OnSpawn();
            FastRandom.SetSeed(trapSeed);
            box.OnInteract(player);
            Near(Config.Game.Player.DefaultMaxHealth - Config.Game.Map.SarcophageTrapDamage, player.CurrentHealth, 0f);
            Check(Debug.Messages.Contains(Config.Game.Map.LogSarcophageTrap));
            Check(!box.IsSpawned);

            // Curse branch: steals the configured session coins.
            uint curseSeed = EntitiesFindSeedWhere(roll => roll > Config.Game.Map.SarcophageTrapChance);
            int coinsBeforeCurse = player.SessionCoins;
            Check(coinsBeforeCurse >= Config.Game.Map.SarcophageCurseCoinLoss);
            box.OnSpawn();
            FastRandom.SetSeed(curseSeed);
            box.OnInteract(player);
            Equal(coinsBeforeCurse - Config.Game.Map.SarcophageCurseCoinLoss, player.SessionCoins);
            Check(Debug.Messages.Contains(Config.Game.Map.LogSarcophageCurse));
            Check(!box.IsSpawned);

            // Ambush without a mummy prefab spawns nothing and still consumes the sarcophage.
            box.MummyPrefab = null;
            Enemy.ActiveEnemies.Clear();
            box.OnSpawn();
            FastRandom.SetSeed(ambushSeed);
            box.OnInteract(player);
            Equal(0, Enemy.ActiveEnemies.Count);
            Check(!box.IsSpawned);
        }
        finally
        {
            FastRandom.SetSeed(13371337u);
        }
    }
}
