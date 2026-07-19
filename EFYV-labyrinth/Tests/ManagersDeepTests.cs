using System;
using System.Collections.Generic;
using System.IO;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;
using EFYV.Core.Entities.Environment.Implementations;
using EFYV.Core.Managers;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Models;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

internal static partial class Program
{
    // ------------------------------------------------------------------
    // Reference model of the backend XOR-shift PRNG (13/17/5 taps) so the
    // manager tests can predict every random decision byte-for-byte.
    // ------------------------------------------------------------------
    private sealed class ManagersRefRandom
    {
        private uint state;

        public ManagersRefRandom(uint seed)
        {
            state = seed == 0u ? 1u : seed;
        }

        public uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public int RangeInt(int min, int max)
        {
            if (max <= min) return min;
            return min + (int)(Next() % (max - min));
        }

        public float RangeFloat(float min, float max)
        {
            float normalized = Next() * 2.3283064365386963e-10f;
            return min + (max - min) * normalized;
        }
    }

    private static float ManagersRefWrapRadians(float x)
    {
        x %= 6.28318531f;
        if (x > 3.14159265f) return x - 6.28318531f;
        if (x < -3.14159265f) return x + 6.28318531f;
        return x;
    }

    private static float ManagersRefSinApprox(float x)
    {
        return x * (1.27323954f - (0.405284735f * MathF.Abs(x)));
    }

    private static void ManagersRefSinCos(float x, out float sin, out float cos)
    {
        x = ManagersRefWrapRadians(x);
        sin = ManagersRefSinApprox(x);
        float cosineInput = x + 1.57079632f;
        if (cosineInput > 3.14159265f) cosineInput -= 6.28318531f;
        cos = ManagersRefSinApprox(cosineInput);
    }

    private static int ManagersClamp(int value, int min, int max)
    {
        return value < min ? min : (value > max ? max : value);
    }

    private static void ManagersRestoreDefaultSeed()
    {
        FastRandom.SetSeed(Config.Backend.Random.DefaultSeed);
    }

    // ------------------------------------------------------------------
    // 1. DropManager: PRNG equivalence + full seeded loot reference model.
    // ------------------------------------------------------------------
    private static void TestManagersPrngAndDropLootModel()
    {
        // PRNG stream equivalence, including the zero-seed fallback.
        foreach (uint seed in new[] { 1u, 0u, 0xDEADBEEFu, 12345u, uint.MaxValue })
        {
            FastRandom.SetSeed(seed);
            var model = new ManagersRefRandom(seed);
            for (int i = 0; i < 25; i++) Equal(model.Next(), FastRandom.Next());
        }

        // Degenerate int ranges return min WITHOUT consuming state; float ranges always consume.
        FastRandom.SetSeed(777u);
        var aligned = new ManagersRefRandom(777u);
        Equal(5, FastRandom.Range(5, 5));
        Equal(-3, FastRandom.Range(-3, -7));
        Equal(aligned.RangeInt(2, 9), FastRandom.Range(2, 9));
        Equal(aligned.RangeInt(-5, 3), FastRandom.Range(-5, 3));
        Near(aligned.RangeFloat(2f, 2f), FastRandom.Range(2f, 2f), 0f);
        Equal(aligned.Next(), FastRandom.Next());

        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var drop = CreateComponent<DropManager>(invokeAwake: true);
        var dropData = GetField<DropManagerData>(drop, "Data");
        Near(Config.Game.System.DefaultDynamicDropMultiplier, dropData.DynamicDropMultiplier, 0f);

        drop.Tick(0.123f, 600f);
        float expectedMultiplier = Config.Game.System.DefaultDynamicDropMultiplier +
            (600f / Config.Game.System.SurvivalTimeMinuteSeconds) * Config.Game.System.DropChanceIncreasePerMinute;
        Near(expectedMultiplier, GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier, 0f);
        drop.ResetTimers();
        Near(Config.Game.System.DefaultDynamicDropMultiplier,
            GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier, 0f);

        var chestPrefab = CreateComponent<ChestProp>(addRenderer: true);
        chestPrefab.Initialize();
        var coinPrefab = CreateComponent<CoinProp>(addRenderer: true);
        coinPrefab.Initialize();
        var gemPrefab = CreateComponent<XPGem>(addRenderer: true);
        gemPrefab.Initialize();
        gemPrefab.xpValue = 17.5f;
        drop.chestPrefab = chestPrefab;
        drop.coinPrefab = coinPrefab;
        drop.xpGemPrefab = gemPrefab;

        var monster = CreateComponent<ProbeEnemy>(addRenderer: true);
        monster.Initialize();
        monster.entityTransform.position = new Vector3(3f, 4f, 5f);
        var miniBoss = CreateComponent<MiniBoss>(addRenderer: true);
        miniBoss.Initialize();
        miniBoss.entityTransform.position = new Vector3(-2f, 7f, 0f);
        var boss = CreateComponent<ProbeBoss>(addRenderer: true);
        boss.Initialize();
        boss.entityTransform.position = new Vector3(10f, -3f, 1f);

        var enemies = new Enemy[] { monster, miniBoss, boss };
        var bossFlags = new[] { false, false, true };
        var miniFlags = new[] { false, true, false };

        foreach (float survivalSeconds in new[] { 0f, 600f })
        {
            drop.Tick(0f, survivalSeconds);
            float multiplier = GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier;
            for (int type = 0; type < enemies.Length; type++)
            {
                for (int trial = 0; trial < 40; trial++)
                {
                    uint seed = 0xA100u + (uint)(trial * 3 + type) + (survivalSeconds > 0f ? 5000u : 0u);
                    ManagersRunDropTrial(pool, drop, enemies[type], bossFlags[type], miniFlags[type],
                        multiplier, seed, chestPresent: true, gemPresent: true);
                }
            }
        }

        // Chest prefab missing: the chest roll still burns a draw, but no chest RNG follows.
        drop.ResetTimers();
        drop.chestPrefab = null;
        for (int trial = 0; trial < 15; trial++)
        {
            ManagersRunDropTrial(pool, drop, boss, isBoss: true, isMini: false,
                multiplier: GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier,
                seed: 0xB200u + (uint)trial, chestPresent: false, gemPresent: true);
        }
        drop.chestPrefab = chestPrefab;

        // Gem prefab missing (#24): the gem roll still burns its draw, but nothing
        // spawns - the PRNG stream is independent of prefab presence.
        drop.xpGemPrefab = null;
        for (int trial = 0; trial < 15; trial++)
        {
            ManagersRunDropTrial(pool, drop, monster, isBoss: false, isMini: false,
                multiplier: GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier,
                seed: 0xC300u + (uint)trial, chestPresent: true, gemPresent: false);
        }
        drop.xpGemPrefab = gemPrefab;

        ManagersRestoreDefaultSeed();
    }

    private static void ManagersRunDropTrial(PoolManager pool, DropManager drop, Enemy enemy,
        bool isBoss, bool isMini, float multiplier, uint seed, bool chestPresent, bool gemPresent)
    {
        // Naive reimplementation of DropManager.DropLoot's decision tree.
        var model = new ManagersRefRandom(seed);
        var expectedChests = new List<int>();
        var expectedCoins = new List<int>();
        int expectedGems = 0;

        float roll = model.RangeFloat(0f, 1f);
        float chestChance = (isBoss || isMini) ? 1f : 0.05f * multiplier;
        if (roll <= chestChance && chestPresent)
        {
            int chestCount = isBoss ? model.RangeInt(1, 4) : 1;
            for (int c = 0; c < chestCount; c++)
            {
                int grade = 1;
                if (isBoss) grade = model.RangeInt(2, 4);
                else if (isMini) grade = model.RangeInt(1, 3);
                expectedChests.Add(ManagersClamp(grade, 1, 3));
            }
        }

        roll = model.RangeFloat(0f, 1f);
        float coinChance = isBoss ? 1f : 0.3f * multiplier;
        if (roll <= coinChance)
        {
            int grade = model.RangeInt(1, 4);
            if (isBoss) grade = 5;
            else if (isMini) grade = model.RangeInt(4, 6);
            expectedCoins.Add(ManagersClamp(grade, 1, 5));
        }

        // XP gem roll (#24): drawn after the coin block; bosses and mini-bosses
        // always drop one, regular enemies use the documented base chance.
        roll = model.RangeFloat(0f, 1f);
        float gemChance = (isBoss || isMini) ? 1f : DropManager.BaseXpGemChance * multiplier;
        if (roll <= gemChance && gemPresent) expectedGems = 1;

        FastRandom.SetSeed(seed);
        drop.DropLoot(enemy);

        var actualChests = new List<ChestProp>();
        foreach (ChestProp chest in Resources.FindObjectsOfTypeAll<ChestProp>())
        {
            if (chest.IsSpawned) actualChests.Add(chest);
        }
        var actualCoins = new List<CoinProp>();
        foreach (CoinProp coin in Resources.FindObjectsOfTypeAll<CoinProp>())
        {
            if (coin.IsSpawned) actualCoins.Add(coin);
        }
        var actualGems = new List<XPGem>();
        foreach (XPGem gem in Resources.FindObjectsOfTypeAll<XPGem>())
        {
            if (gem.IsSpawned) actualGems.Add(gem);
        }

        Equal(expectedChests.Count, actualChests.Count, "Chest count diverged for seed " + seed + ".");
        Equal(expectedCoins.Count, actualCoins.Count, "Coin count diverged for seed " + seed + ".");
        Equal(expectedGems, actualGems.Count, "XP gem count diverged for seed " + seed + ".");

        var actualChestGrades = new List<int>();
        foreach (ChestProp chest in actualChests)
        {
            actualChestGrades.Add(chest.Grade);
            Near(enemy.entityTransform.position.x, chest.entityTransform.position.x, 0f);
            Near(enemy.entityTransform.position.y, chest.entityTransform.position.y, 0f);
            Near(enemy.entityTransform.position.z, chest.entityTransform.position.z, 0f);
        }
        expectedChests.Sort();
        actualChestGrades.Sort();
        for (int i = 0; i < expectedChests.Count; i++) Equal(expectedChests[i], actualChestGrades[i]);

        for (int i = 0; i < actualCoins.Count; i++)
        {
            CoinProp coin = actualCoins[i];
            Equal(expectedCoins[i], coin.Grade);
            Equal(Config.Game.Drops.BaseCoinValue * coin.Grade * coin.Grade, coin.Value);
            Near(enemy.entityTransform.position.x, coin.entityTransform.position.x, 0f);
            Near(enemy.entityTransform.position.y, coin.entityTransform.position.y, 0f);
        }

        foreach (XPGem gem in actualGems)
        {
            Near(17.5f, gem.xpValue, 0f); // The prefab's configured value survives the clone.
            Near(enemy.entityTransform.position.x, gem.entityTransform.position.x, 0f);
            Near(enemy.entityTransform.position.y, gem.entityTransform.position.y, 0f);
        }

        foreach (ChestProp chest in actualChests) pool.Despawn(chest);
        foreach (CoinProp coin in actualCoins) pool.Despawn(coin);
        foreach (XPGem gem in actualGems) pool.Despawn(gem);
    }

    // ------------------------------------------------------------------
    // 2. AchievementManager: exhaustive 256-bit reference model, raw word
    //    layout, persistence round-trip, and event/log accounting.
    // ------------------------------------------------------------------
    private static void TestManagersAchievementBitmaskModel()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-managers-ach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Application.persistentDataPath = tempRoot;
        try
        {
            var save = CreateComponent<SaveManager>(invokeAwake: true);
            save.currentSaveData = PlayerMetaSchema.Default();
            var achievements = CreateComponent<AchievementManager>(invokeAwake: true);

            int eventCount = 0;
            achievements.OnAchievementUnlocked += _ => eventCount++;

            var expected = new HashSet<int>();
            int expectedEvents = 0;
            int expectedAnnouncements = 0;

            // Phase 1: no database attached (every fresh unlock logs the no-visual line).
            foreach (int id in new[] { 0, 31, 32, 63, 64, 95, 128, 255 })
            {
                if (expected.Add(id)) expectedAnnouncements++;
                achievements.UnlockAchievement(id);
                Check(achievements.IsAchievementUnlocked(id));
            }
            Equal(0, eventCount);

            // Phase 2: database attached; only ids with definitions log/fire events.
            var database = ScriptableObject.CreateInstance<LegacyAchievementDatabase>();
            Invoke(database, "PopulateBasis");
            achievements.achievementDatabase = database;
            int definitionCount = database.achievements.Count;

            var rng = new Random(0xACE5);
            for (int i = 0; i < 120; i++)
            {
                int id = rng.Next(-8, 264);
                bool valid = id >= 0 && id < Config.Game.Achievements.MaxAchievements;
                bool wasNew = valid && expected.Add(id);
                achievements.UnlockAchievement(id);
                if (wasNew && id < definitionCount)
                {
                    expectedEvents++;
                    expectedAnnouncements++;
                }
                Equal(valid, achievements.IsAchievementUnlocked(id));
            }

            // Adversarial ids must not disturb any state.
            achievements.UnlockAchievement(int.MinValue);
            achievements.UnlockAchievement(int.MaxValue);
            achievements.UnlockAchievement(-1);
            achievements.UnlockAchievement(Config.Game.Achievements.MaxAchievements);
            Check(!achievements.IsAchievementUnlocked(int.MinValue));
            Check(!achievements.IsAchievementUnlocked(int.MaxValue));

            // Exhaustive comparison across (and beyond) the full id space.
            for (int id = -2; id < Config.Game.Achievements.MaxAchievements + 2; id++)
            {
                Equal(expected.Contains(id), achievements.IsAchievementUnlocked(id));
            }

            // Raw 32-bit word layout: 256 achievements live in exactly the first 8 ints.
            for (int word = 0; word < FastSchemaBlock.MaxSize; word++)
            {
                int expectedWord = 0;
                foreach (int id in expected)
                {
                    if (id / 32 == word) expectedWord |= 1 << (id % 32);
                }
                Equal(expectedWord, save.currentSaveData.LegacyAchievements.GetInt(word));
            }

            Equal(expectedEvents, eventCount);
            int announcements = 0;
            foreach (string message in Debug.Messages)
            {
                if (message != null && message.StartsWith("ACHIEVEMENT UNLOCKED", StringComparison.Ordinal))
                    announcements++;
            }
            Equal(expectedAnnouncements, announcements);

            // Every successful unlock persisted synchronously; disk must equal memory.
            save.currentSaveData = default;
            save.LoadGame();
            for (int id = 0; id < Config.Game.Achievements.MaxAchievements; id++)
            {
                Equal(expected.Contains(id), achievements.IsAchievementUnlocked(id));
            }

            List<LegacyAchievementDefinition> unlocked = achievements.GetUnlockedAchievements();
            var expectedListed = new List<int>();
            for (int id = 0; id < definitionCount; id++)
            {
                if (expected.Contains(id)) expectedListed.Add(id);
            }
            Equal(expectedListed.Count, unlocked.Count);
            for (int i = 0; i < unlocked.Count; i++)
            {
                Equal(expectedListed[i], unlocked[i].id);
                Equal(Config.Game.Achievements.BasisData.Titles[expectedListed[i]], unlocked[i].title);
            }
        }
        finally
        {
            TestRuntime.Reset();
            Directory.Delete(tempRoot, true);
        }
    }

    // ------------------------------------------------------------------
    // 3. SaveManager: byte-level binary layout, round-trip, corrupted
    //    files, and the dirty-save debounce state machine.
    // ------------------------------------------------------------------
    private static unsafe int ManagersSizeOfSaveSchema()
    {
        return sizeof(PlayerMetaSchema);
    }

    // b2-pipeline-contract agent (#19): independent CRC-32 reference for the
    // versioned save envelope (same bitwise model the PNG tests use).
    private static uint ManagersReferenceCrc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFFu;
        for (int i = 0; i < bytes.Length; i++)
        {
            crc ^= bytes[i];
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1u) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static void TestManagersSaveBinaryLayoutAndDebounce()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-managers-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Application.persistentDataPath = tempRoot;
        try
        {
            // b2-pipeline-contract agent (#19): the save file is now a versioned
            // envelope - {magic, version, CRC32} little-endian header + payload.
            const int BlockBytes = 256;
            const int HeaderBytes = 12;
            int payloadSize = sizeof(int) + (2 * BlockBytes) + (PlayerMetaSchema.MaxToons * BlockBytes);
            Equal(16900, payloadSize);
            Equal(payloadSize, ManagersSizeOfSaveSchema());
            int expectedSize = HeaderBytes + payloadSize;

            // Int/float aliasing invariant of the schema block (raw bit reinterpretation).
            FastSchemaBlock alias = default;
            alias.SetFloat(0, 123.5f);
            Equal(BitConverter.SingleToInt32Bits(123.5f), alias.GetInt(0));
            alias.SetInt(1, 0x42F6E979);
            Near(BitConverter.Int32BitsToSingle(0x42F6E979), alias.GetFloat(1), 0f);

            var save = CreateComponent<SaveManager>(invokeAwake: true);
            string savePath = Path.Combine(tempRoot, Config.Game.Save.SaveFileName);
            Check(!File.Exists(savePath));

            // Deterministic pattern across every slot of the struct.
            PlayerMetaSchema data = PlayerMetaSchema.Default();
            data.TotalCoinsCollected = unchecked((int)0x1B2C3D4E);
            for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
            {
                data.LegacyStats.SetFloat(slot, slot * 1.5f - 3f);
                data.LegacyAchievements.SetInt(slot, unchecked(slot * 0x01010101 + 7));
            }
            for (int toon = 0; toon < PlayerMetaSchema.MaxToons; toon++)
            {
                FastSchemaBlock block = default;
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                {
                    block.SetInt(slot, toon * 1000 + slot * 7 + 13);
                }
                Check(data.TrySetToonBlock(toon, block));
            }

            save.currentSaveData = data;
            save.SaveGame();
            byte[] actualBytes = File.ReadAllBytes(savePath);
            Equal(expectedSize, actualBytes.Length);

            // Independent byte-level model of the Pack=1 sequential layout,
            // prefixed by the {magic, version, CRC32-of-payload} envelope.
            byte[] expectedPayload = new byte[payloadSize];
            BitConverter.GetBytes(unchecked((int)0x1B2C3D4E)).CopyTo(expectedPayload, 0);
            for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
            {
                BitConverter.GetBytes(slot * 1.5f - 3f).CopyTo(expectedPayload, 4 + (slot * 4));
                BitConverter.GetBytes(unchecked(slot * 0x01010101 + 7)).CopyTo(expectedPayload, 4 + BlockBytes + (slot * 4));
            }
            for (int toon = 0; toon < PlayerMetaSchema.MaxToons; toon++)
            {
                int baseOffset = 4 + (2 * BlockBytes) + (toon * BlockBytes);
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                {
                    BitConverter.GetBytes(toon * 1000 + slot * 7 + 13).CopyTo(expectedPayload, baseOffset + (slot * 4));
                }
            }
            byte[] expectedBytes = new byte[expectedSize];
            BitConverter.GetBytes(0x56594645u).CopyTo(expectedBytes, 0);           // "EFYV" magic
            BitConverter.GetBytes(1).CopyTo(expectedBytes, 4);                     // format version
            BitConverter.GetBytes(ManagersReferenceCrc32(expectedPayload)).CopyTo(expectedBytes, 8);
            expectedPayload.CopyTo(expectedBytes, HeaderBytes);
            int firstMismatch = -1;
            for (int i = 0; i < expectedSize; i++)
            {
                if (expectedBytes[i] != actualBytes[i]) { firstMismatch = i; break; }
            }
            Equal(-1, firstMismatch, "Save file bytes diverged from the layout model at offset " + firstMismatch + ".");

            // Round-trip: every slot must survive the disk trip bit-exactly.
            save.currentSaveData = default;
            save.LoadGame();
            Equal(unchecked((int)0x1B2C3D4E), save.currentSaveData.TotalCoinsCollected);
            for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
            {
                Equal(BitConverter.SingleToInt32Bits(slot * 1.5f - 3f), save.currentSaveData.LegacyStats.GetInt(slot));
                Equal(unchecked(slot * 0x01010101 + 7), save.currentSaveData.LegacyAchievements.GetInt(slot));
            }
            for (int toon = 0; toon < PlayerMetaSchema.MaxToons; toon++)
            {
                Check(save.currentSaveData.TryGetToonBlock(toon, out FastSchemaBlock block));
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                {
                    Equal(toon * 1000 + slot * 7 + 13, block.GetInt(slot));
                }
            }
            Check(!save.currentSaveData.TryGetToonBlock(-1, out _));
            Check(!save.currentSaveData.TryGetToonBlock(PlayerMetaSchema.MaxToons, out _));

            // Oversized files are REJECTED by the exact-size envelope check (#19
            // flipped the old permissive trailing-bytes read): the profile
            // resets to defaults instead of trusting a corrupt file.
            using (var appendStream = new FileStream(savePath, FileMode.Append, FileAccess.Write))
            {
                appendStream.Write(new byte[37], 0, 37);
            }
            save.currentSaveData = default;
            save.LoadGame();
            Equal(0, save.currentSaveData.TotalCoinsCollected);
            Near(1f, save.currentSaveData.LegacyStats.GetFloat((int)StatSchema.Might), 0f);

            // Restore the intact file: a fresh save must round-trip again.
            File.WriteAllBytes(savePath, actualBytes);
            save.currentSaveData = default;
            save.LoadGame();
            Equal(unchecked((int)0x1B2C3D4E), save.currentSaveData.TotalCoinsCollected);

            // Truncated files fall back to the pristine default profile.
            byte[] truncated = new byte[1234];
            Array.Copy(actualBytes, truncated, truncated.Length);
            File.WriteAllBytes(savePath, truncated);
            save.currentSaveData = default;
            save.LoadGame();
            Equal(0, save.currentSaveData.TotalCoinsCollected);
            Near(1f, save.currentSaveData.LegacyStats.GetFloat((int)StatSchema.Might), 0f);
            Near(0f, save.currentSaveData.LegacyStats.GetFloat((int)StatSchema.Recovery), 0f);
            Check(save.currentSaveData.TryGetToonBlock(0, out FastSchemaBlock emptyToon));
            Equal(Config.Game.Progression.EmptyToonHash, emptyToon.GetInt((int)ToonSchema.ToonIdHash));

            // Zero-byte file behaves like a missing file.
            File.WriteAllBytes(savePath, Array.Empty<byte>());
            save.currentSaveData = default;
            save.LoadGame();
            Equal(0, save.currentSaveData.TotalCoinsCollected);

            // Dirty-save debounce state machine.
            File.Delete(savePath);
            save.currentSaveData = PlayerMetaSchema.Default();
            Time.unscaledTime = 50f;
            save.AddCoinsToToon("debounce", 3);
            Check(GetField<bool>(save, "hasUnsavedChanges"));
            Near(50f + Config.Game.Save.DirtySaveDebounceSeconds, GetField<float>(save, "dirtySaveDeadline"), 0f);
            Check(!File.Exists(savePath), "AddCoinsToToon must debounce, not save synchronously.");

            Time.unscaledTime = 50.6f;
            save.AddCoinsToToon("debounce", 2);
            Near(50f + Config.Game.Save.DirtySaveDebounceSeconds, GetField<float>(save, "dirtySaveDeadline"),
                0f, "A second dirty mark must not extend the existing deadline.");
            Invoke(save, "Update");
            Check(!File.Exists(savePath));

            Time.unscaledTime = 50.999f;
            Invoke(save, "Update");
            Check(!File.Exists(savePath));

            Time.unscaledTime = 51f;
            Invoke(save, "Update");
            Check(File.Exists(savePath), "Deadline reached: Update must flush the dirty save.");
            Check(!GetField<bool>(save, "hasUnsavedChanges"));

            File.Delete(savePath);
            Invoke(save, "Update");
            Check(!File.Exists(savePath), "A clean manager must not rewrite the save file.");

            save.AddCoinsToToon("debounce", 1);
            Invoke(save, "OnApplicationPause", false);
            Check(!File.Exists(savePath), "Unpausing must not flush.");
            Invoke(save, "OnApplicationPause", true);
            Check(File.Exists(savePath), "Pausing must flush unsaved changes.");

            File.Delete(savePath);
            save.AddCoinsToToon("debounce", 1);
            Invoke(save, "OnApplicationQuit");
            Check(File.Exists(savePath), "Quitting must flush unsaved changes.");
        }
        finally
        {
            TestRuntime.Reset();
            Directory.Delete(tempRoot, true);
        }
    }

    // ------------------------------------------------------------------
    // 4. SaveManager: full 16-stat upgrade rule table (legacy + toon) and
    //    the combined-stats math against a naive rule model.
    // ------------------------------------------------------------------
    private static float ManagersExpectedUpgradedStat(StatSchema stat, float before)
    {
        switch (stat)
        {
            case StatSchema.MaxHealth:
            case StatSchema.Recovery:
            case StatSchema.Magnet:
            case StatSchema.Luck:
            case StatSchema.Greed:
            case StatSchema.Curse:
                return before + 0.1f;
            case StatSchema.Might:
            case StatSchema.Area:
            case StatSchema.WeaponSpeed:
            case StatSchema.Duration:
            case StatSchema.Growth:
            case StatSchema.MoveSpeed:
                return before + 0.05f;
            case StatSchema.Cooldown:
                return before * 0.95f;
            default:
                return before + 1f; // Armor, Revival, Amount: flat +1.
        }
    }

    private static int ManagersFindToonIndex(ref PlayerMetaSchema data, string toonId)
    {
        int hash = FastMath.FastHash(toonId);
        for (int i = 0; i < PlayerMetaSchema.MaxToons; i++)
        {
            if (!data.TryGetToonBlock(i, out FastSchemaBlock block)) break;
            if (block.GetInt((int)ToonSchema.ToonIdHash) == hash) return i;
        }
        return -1;
    }

    private static void TestManagersStatUpgradeAndCombineRules()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "efyv-managers-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Application.persistentDataPath = tempRoot;
        try
        {
            var save = CreateComponent<SaveManager>(invokeAwake: true);
            save.currentSaveData = PlayerMetaSchema.Default();

            // Legacy sweep across every stat with cumulative coin accounting.
            save.currentSaveData.TotalCoinsCollected = 100000;
            int coins = 100000;
            for (int stat = 0; stat < (int)StatSchema.MAX_STATS; stat++)
            {
                float before = save.currentSaveData.LegacyStats.GetFloat(stat);
                Check(save.SpendCoinsOnLegacyStat((StatSchema)stat, stat + 1));
                coins -= stat + 1;
                Equal(coins, save.currentSaveData.TotalCoinsCollected);
                Near(ManagersExpectedUpgradedStat((StatSchema)stat, before),
                    save.currentSaveData.LegacyStats.GetFloat(stat), 0f);
            }

            // Exact-cost boundary: spending down to zero succeeds, one more coin fails.
            save.currentSaveData.TotalCoinsCollected = 25;
            Check(save.SpendCoinsOnLegacyStat(StatSchema.Luck, 25));
            Equal(0, save.currentSaveData.TotalCoinsCollected);
            Check(!save.SpendCoinsOnLegacyStat(StatSchema.Luck, 1));

            // Toon sweep: 16000 coins = 16 level-ups = 80 stat points.
            save.currentSaveData = PlayerMetaSchema.Default();
            save.AddCoinsToToon("sweeper", 16000);
            int toonIndex = ManagersFindToonIndex(ref save.currentSaveData, "sweeper");
            Check(toonIndex >= 0);
            Check(save.currentSaveData.TryGetToonBlock(toonIndex, out FastSchemaBlock sweeper));
            Equal(17, sweeper.GetInt((int)ToonSchema.Level));
            Equal(80, sweeper.GetInt((int)ToonSchema.UnspentStatPoints));
            Equal(16000, sweeper.GetInt((int)ToonSchema.TotalCoinsCollected));
            Equal(16000, save.currentSaveData.TotalCoinsCollected);

            for (int stat = 0; stat < (int)StatSchema.MAX_STATS; stat++)
            {
                Check(save.currentSaveData.TryGetToonBlock(toonIndex, out FastSchemaBlock before));
                float statBefore = before.GetFloat((int)ToonSchema.StatsStart + stat);
                Check(save.SpendToonStatPoint("sweeper", (StatSchema)stat));
                Check(save.currentSaveData.TryGetToonBlock(toonIndex, out FastSchemaBlock after));
                Near(ManagersExpectedUpgradedStat((StatSchema)stat, statBefore),
                    after.GetFloat((int)ToonSchema.StatsStart + stat), 0f);
                Equal(80 - (stat + 1), after.GetInt((int)ToonSchema.UnspentStatPoints));
            }

            Check(!save.SpendToonStatPoint("nobody", StatSchema.Might));
            Check(!save.SpendToonStatPoint("   ", StatSchema.Might));
            save.AddCoinsToToon("pauper", 999);
            Check(!save.SpendToonStatPoint("pauper", StatSchema.Might), "999 coins must not grant a stat point.");

            // Combined stats: naive rule model over adversarial slot values.
            for (int stat = 0; stat < (int)StatSchema.MAX_STATS; stat++)
            {
                save.currentSaveData.LegacyStats.SetFloat(stat, 2f + stat * 0.125f);
            }
            Check(save.currentSaveData.TryGetToonBlock(toonIndex, out FastSchemaBlock custom));
            for (int stat = 0; stat < (int)StatSchema.MAX_STATS; stat++)
            {
                custom.SetFloat((int)ToonSchema.StatsStart + stat, 0.5f + stat * 0.25f);
            }
            Check(save.currentSaveData.TrySetToonBlock(toonIndex, custom));

            FastSchemaBlock combined = save.GetCombinedStatsForToon("sweeper");
            for (int stat = 0; stat < (int)StatSchema.MAX_STATS; stat++)
            {
                float account = 2f + stat * 0.125f;
                float toon = 0.5f + stat * 0.25f;
                float expected;
                var schema = (StatSchema)stat;
                if (schema == StatSchema.Cooldown) expected = account * toon;
                else if (schema == StatSchema.Recovery || schema == StatSchema.Armor ||
                         schema == StatSchema.Revival || schema == StatSchema.Amount)
                    expected = account + toon;
                else expected = account + (toon - 1f);
                Near(expected, combined.GetFloat(stat), 0f);
            }

            // Missing or whitespace toons fall back to the raw legacy stats.
            FastSchemaBlock fallback = save.GetCombinedStatsForToon("ghost");
            FastSchemaBlock whitespace = save.GetCombinedStatsForToon("   ");
            for (int stat = 0; stat < (int)StatSchema.MAX_STATS; stat++)
            {
                Near(2f + stat * 0.125f, fallback.GetFloat(stat), 0f);
                Near(2f + stat * 0.125f, whitespace.GetFloat(stat), 0f);
            }

            // Level-up boundary math: 999 -> no level, +1 -> level 2, +3001 -> level 5.
            save.AddCoinsToToon("edge", 999);
            int edgeIndex = ManagersFindToonIndex(ref save.currentSaveData, "edge");
            Check(save.currentSaveData.TryGetToonBlock(edgeIndex, out FastSchemaBlock edge));
            Equal(1, edge.GetInt((int)ToonSchema.Level));
            Equal(0, edge.GetInt((int)ToonSchema.UnspentStatPoints));
            save.AddCoinsToToon("edge", 1);
            Check(save.currentSaveData.TryGetToonBlock(edgeIndex, out edge));
            Equal(2, edge.GetInt((int)ToonSchema.Level));
            Equal(5, edge.GetInt((int)ToonSchema.UnspentStatPoints));
            save.AddCoinsToToon("edge", 3001);
            Check(save.currentSaveData.TryGetToonBlock(edgeIndex, out edge));
            Equal(4001, edge.GetInt((int)ToonSchema.TotalCoinsCollected));
            Equal(5, edge.GetInt((int)ToonSchema.Level));
            Equal(20, edge.GetInt((int)ToonSchema.UnspentStatPoints));
            Equal(16000 + 999 + 999 + 1 + 3001, save.currentSaveData.TotalCoinsCollected);
        }
        finally
        {
            TestRuntime.Reset();
            Directory.Delete(tempRoot, true);
        }
    }

    // ------------------------------------------------------------------
    // 5. SpawnManager: multi-frame accumulator/timer simulation against a
    //    naive model, including AI intensity coupling, the 256 cap, the
    //    64-per-frame drain, and central prop/enemy ticking.
    // ------------------------------------------------------------------
    private static void TestManagersSpawnAccumulatorSimulation()
    {
        FastRandom.SetSeed(0x51A7Eu);

        var spawner = CreateComponent<SpawnManager>();
        Invoke(spawner, "Awake");
        var playerAnchor = new GameObject("accumulator-player").transform;
        spawner.playerTransform = playerAnchor;
        spawner.enemyPrefabs = Array.Empty<Enemy>();
        spawner.baseSpawnRate = 3.3f;
        spawner.difficultyMultiplier = 0.45f;
        Near(3.3f, spawner.baseSpawnRate, 0f);
        Near(0.45f, spawner.difficultyMultiplier, 0f);
        Near(0f, spawner.GameTimer, 0f);

        var director = CreateComponent<AIDirector>(invokeAwake: true);
        director.spawnManager = spawner;
        var drop = CreateComponent<DropManager>(invokeAwake: true);

        // Central tick integration probes.
        var prop = CreateComponent<ProbeProp>(addRenderer: true);
        prop.Initialize();
        prop.animationFrames = new[] { new Sprite(), new Sprite(), new Sprite() };
        prop.animationSpeed = 99999f;
        prop.OnSpawn();
        float expectedPropTimer = prop.Timer;
        ProbeEnemy tickProbe = SpawnEnemy(5f, 5f, 10f);
        tickProbe.SetTarget(playerAnchor);
        int expectedMoves = tickProbe.TickMoves;

        float baseRate = 3.3f;
        float difficulty = 0.45f;
        float timer = 0f;
        float accumulator = 0f;

        void ManagersSimulateFrame(float deltaTime)
        {
            Time.deltaTime = deltaTime;
            Invoke(spawner, "Update");

            timer += deltaTime;
            float rate = baseRate + (timer * difficulty);
            rate *= 1f + ((timer / 60f) * 0.5f); // AI director intensity.
            float increment = rate * deltaTime;
            if (float.IsNaN(increment) || increment <= 0f) increment = 0f;
            else if (float.IsInfinity(increment)) increment = 256f;
            accumulator = MathF.Min(accumulator + increment, 256f);
            int spawnsThisFrame = 0;
            while (accumulator >= 1f && spawnsThisFrame < 64)
            {
                accumulator -= 1f;
                spawnsThisFrame++;
            }
            expectedPropTimer += deltaTime;
            expectedMoves++;

            Near(timer, spawner.GameTimer, 1e-4f);
            Near(accumulator, GetField<SpawnManagerData>(spawner, "Data").SpawnAccumulator, 2e-3f);
            Near(1f + ((timer / 60f) * 0.05f),
                GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier, 1e-5f);
            Near(expectedPropTimer, prop.Timer, 1e-3f);
            Equal(expectedMoves, tickProbe.TickMoves);
        }

        foreach (float deltaTime in new[] { 0.016f, 0.33f, 1.25f, 0.05f, 2.5f, 0.075f, 0.6f, 3.1f, 0.02f, 1.9f })
        {
            ManagersSimulateFrame(deltaTime);
        }

        // Infinity handling: a finite-but-huge rate overflows to +inf and clamps to the cap.
        spawner.baseSpawnRate = float.MaxValue;
        baseRate = float.MaxValue;
        ManagersSimulateFrame(0.5f);
        ManagersSimulateFrame(0.5f);
        Near(256f - 64f, GetField<SpawnManagerData>(spawner, "Data").SpawnAccumulator, 0f);

        // Without a player the whole update is skipped (timer, ticks, accumulator frozen).
        spawner.playerTransform = null;
        Time.deltaTime = 5f;
        Invoke(spawner, "Update");
        Near(timer, spawner.GameTimer, 1e-4f);
        Near(expectedPropTimer, prop.Timer, 1e-3f);
        Equal(expectedMoves, tickProbe.TickMoves);

        // An empty prefab array never spawned anything while the accumulator drained.
        Equal(1, Enemy.ActiveEnemies.Count);
        Same(tickProbe, Enemy.ActiveEnemies[0]);

        ManagersRestoreDefaultSeed();
    }

    // ------------------------------------------------------------------
    // 6. SpawnManager: seeded end-to-end placement model. Predicts every
    //    prefab choice and exact spawn coordinate from the PRNG stream.
    // ------------------------------------------------------------------
    private static void TestManagersSeededSpawnPlacement()
    {
        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var spawner = CreateComponent<SpawnManager>();
        Invoke(spawner, "Awake");
        var target = new GameObject("seeded-player").transform;
        target.position = new Vector3(100f, -50f, 3f);
        spawner.playerTransform = target;
        spawner.spawnRadius = 12.5f;
        spawner.baseSpawnRate = 5f;
        spawner.difficultyMultiplier = 0f;

        var prefabA = CreateComponent<ProbeEnemy>(addRenderer: true);
        prefabA.Initialize();
        prefabA.Apply(50f, 2f, 3f, 4f);
        var prefabB = CreateComponent<ProbeEnemy>(addRenderer: true);
        prefabB.Initialize();
        prefabB.Apply(70f, 1f, 2f, 6f);
        spawner.enemyPrefabs = new Enemy[] { prefabA, prefabB };
        int keyA = PoolManager.GetPoolKey(prefabA.gameObject);
        int keyB = PoolManager.GetPoolKey(prefabB.gameObject);

        // No AI director / drop manager: the RNG stream belongs to spawning alone.
        Check(!AIDirector.TryGetInstance(out _));
        Check(!DropManager.TryGetInstance(out _));

        // Direct pipeline check: FastRandom.Range + FastSinCosTaylor must match the
        // naive reimplementation bit-for-bit across a seeded sweep of angles.
        FastRandom.SetSeed(0x7A97u);
        var trig = new ManagersRefRandom(0x7A97u);
        for (int i = 0; i < 2000; i++)
        {
            float actualAngle = FastRandom.Range(-10f, 10f);
            float expectedAngle = trig.RangeFloat(-10f, 10f);
            Near(expectedAngle, actualAngle, 0f);
            ManagersRefSinCos(expectedAngle, out float expectedSin, out float expectedCos);
            FastMath.FastSinCosTaylor(actualAngle, out float actualSin, out float actualCos);
            Near(expectedSin, actualSin, 0f);
            Near(expectedCos, actualCos, 0f);
        }

        const uint Seed = 0x5EED1234u;
        FastRandom.SetSeed(Seed);
        var model = new ManagersRefRandom(Seed);

        float timer = 0f;
        float accumulator = 0f;
        float baseRate = 5f;
        var expected = new List<int>();
        // The test-runtime Instantiate gives every clone its own GameObject/Transform
        // (CopyFields skips the <gameObject> backing field), so the model verifies the
        // exact position of EVERY spawn after every frame, plus the complete
        // prefab-choice sequence.
        var expectedPositions = new List<Vector2>();

        void ManagersModelFrame(float deltaTime)
        {
            timer += deltaTime;
            float rate = baseRate + (timer * 0f);
            float increment = rate * deltaTime;
            if (float.IsNaN(increment) || increment <= 0f) increment = 0f;
            else if (float.IsInfinity(increment)) increment = 256f;
            accumulator = MathF.Min(accumulator + increment, 256f);
            int spawnsThisFrame = 0;
            while (accumulator >= 1f && spawnsThisFrame < 64)
            {
                int prefabIndex = model.RangeInt(0, 2);
                float radians = model.RangeFloat(0f, 6.28318531f) - 3.14159265f;
                ManagersRefSinCos(radians, out float sin, out float cos);
                float positionX = 100f + (cos * 12.5f);
                float positionY = -50f + (sin * 12.5f);
                expected.Add(prefabIndex == 0 ? keyA : keyB);
                expectedPositions.Add(new Vector2(positionX, positionY));
                accumulator -= 1f;
                spawnsThisFrame++;
            }
        }

        void ManagersCheckSpawnPositions()
        {
            Equal(expectedPositions.Count, Enemy.ActiveEnemies.Count);
            for (int i = 0; i < expectedPositions.Count; i++)
            {
                Near(expectedPositions[i].x, Enemy.ActiveEnemies[i].entityTransform.position.x, 0f);
                Near(expectedPositions[i].y, Enemy.ActiveEnemies[i].entityTransform.position.y, 0f);
                Near(3f, Enemy.ActiveEnemies[i].entityTransform.position.z, 0f);
            }
        }

        foreach (float deltaTime in new[] { 1f, 0.5f, 0.8f })
        {
            Time.deltaTime = deltaTime;
            Invoke(spawner, "Update");
            ManagersModelFrame(deltaTime);
            ManagersCheckSpawnPositions();
        }
        Equal(11, expected.Count, "Scenario sanity: 5 + 2 + 4 spawns expected.");

        // MaxSpawnsPerFrame clamp with live spawns.
        spawner.baseSpawnRate = 300f;
        baseRate = 300f;
        Time.deltaTime = 1f;
        Invoke(spawner, "Update");
        ManagersModelFrame(1f);
        ManagersCheckSpawnPositions();
        Equal(11 + 64, expected.Count);
        Near(accumulator, GetField<SpawnManagerData>(spawner, "Data").SpawnAccumulator, 0f);

        Equal(expected.Count, Enemy.ActiveEnemies.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Enemy enemy = Enemy.ActiveEnemies[i];
            Equal(expected[i], enemy.prefabPoolKey, "Prefab choice diverged at spawn " + i + ".");
            Equal(i, enemy.ActiveListIndex);
        }

        UnityEngine.Object.Destroy(pool.gameObject);
        ManagersRestoreDefaultSeed();
    }

    // ------------------------------------------------------------------
    // 7. PoolManager: LIFO reuse contract, scheduled-despawn simulation,
    //    re-scheduling, wrong-key guards, and singleton ownership rules.
    // ------------------------------------------------------------------
    private static void TestManagersPoolSchedulingAndSingletons()
    {
        var manager = CreateComponent<PoolManager>(invokeAwake: true);

        // LIFO rent/return: spawning after returns yields the reverse return order.
        var prefab = CreateComponent<ProbeEntity>(addRenderer: true);
        prefab.Initialize();
        manager.Prewarm(prefab, 3);
        int entityKey = PoolManager.GetPoolKey(prefab.gameObject);
        GameEntity first = manager.SpawnByKey(entityKey, Vector3.zero, Quaternion.identity);
        GameEntity second = manager.SpawnByKey(entityKey, Vector3.zero, Quaternion.identity);
        GameEntity third = manager.SpawnByKey(entityKey, Vector3.zero, Quaternion.identity);
        Check(first != null && second != null && third != null);
        NotSame(first, second);
        NotSame(second, third);
        manager.Despawn(first);
        manager.Despawn(third);
        manager.Despawn(second);
        Same(second, manager.SpawnByKey(entityKey, Vector3.zero, Quaternion.identity));
        Same(third, manager.SpawnByKey(entityKey, Vector3.zero, Quaternion.identity));
        Same(first, manager.SpawnByKey(entityKey, Vector3.zero, Quaternion.identity));
        manager.Despawn(first);
        manager.Despawn(second);
        manager.Despawn(third);

        // Scheduled-despawn reference simulation across mixed delays.
        var objPrefab = new GameObject("scheduled-prefab");
        manager.PrewarmGameObject(objPrefab, 6);
        int objectKey = PoolManager.GetPoolKey(objPrefab);
        var objects = new GameObject[6];
        for (int i = 0; i < objects.Length; i++)
        {
            objects[i] = manager.SpawnGameObject(objPrefab, new Vector3(i, 0f, 0f), Quaternion.identity);
            Check(objects[i] != null && objects[i].activeSelf);
        }
        float[] remaining = { 0.30f, 0.55f, 0.80f, 1.05f, 1.30f, 0.05f };
        for (int i = 0; i < objects.Length; i++)
        {
            manager.DespawnGameObject(objects[i], objectKey, remaining[i]);
            Check(objects[i].activeSelf, "Delayed despawn must keep the object active until expiry.");
        }
        var scheduleList = (System.Collections.ICollection)GetField(typeof(PoolManager), "scheduledDespawns", manager);
        Equal(6, scheduleList.Count);
        for (int frame = 0; frame < 6; frame++)
        {
            Time.deltaTime = 0.28f;
            Invoke(manager, "Update");
            int expectedScheduled = 0;
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] > 0f) remaining[i] -= 0.28f;
                if (remaining[i] > 0f) expectedScheduled++;
                Equal(remaining[i] > 0f, objects[i].activeSelf,
                    "Scheduled despawn state diverged for object " + i + " on frame " + frame + ".");
            }
            Equal(expectedScheduled, scheduleList.Count);
        }
        for (int i = 0; i < objects.Length; i++)
        {
            Check(manager.SpawnGameObject(objPrefab, Vector3.zero, Quaternion.identity) != null,
                "All expired objects must be rentable again.");
        }
        foreach (GameObject obj in objects) manager.DespawnGameObject(obj, objectKey, 0f);

        // Re-scheduling replaces the previous timer instead of duplicating it.
        GameObject rescheduled = manager.SpawnGameObject(objPrefab, Vector3.zero, Quaternion.identity);
        manager.DespawnGameObject(rescheduled, objectKey, 10f);
        manager.DespawnGameObject(rescheduled, objectKey, 0.3f);
        Equal(1, scheduleList.Count);
        Time.deltaTime = 0.31f;
        Invoke(manager, "Update");
        Check(!rescheduled.activeSelf);
        Equal(0, scheduleList.Count);
        Invoke(manager, "Update");
        Invoke(manager, "Update");
        Same(rescheduled, manager.SpawnGameObject(objPrefab, Vector3.zero, Quaternion.identity));

        // Guard region: despawning through a foreign pool key throws BEFORE any
        // deactivation (#24) - the object stays active and rented instead of being
        // deactivated without pooling (the old wrong-key leak).
        var otherPrefab = new GameObject("foreign-prefab");
        manager.PrewarmGameObject(otherPrefab, 1);
        int foreignKey = PoolManager.GetPoolKey(otherPrefab);
        Throws<ArgumentException>(() => manager.DespawnGameObject(rescheduled, foreignKey, 0f));
        Check(rescheduled.activeSelf, "A rejected wrong-key despawn must leave the object active.");
        Throws<ArgumentException>(() => manager.DespawnGameObject(rescheduled, foreignKey, 5f));
        Check(rescheduled.activeSelf, "A rejected wrong-key DELAYED despawn must not schedule anything.");
        Equal(0, scheduleList.Count);
        // The object is still rented under its own key: a correct-key despawn works
        // and (LIFO) hands the same object back on the next spawn.
        manager.DespawnGameObject(rescheduled, objectKey, 0f);
        Check(!rescheduled.activeSelf);
        Same(rescheduled, manager.SpawnGameObject(objPrefab, Vector3.zero, Quaternion.identity));
        manager.DespawnGameObject(rescheduled, objectKey, 0f);

        // #24 (flipped pin): an immediate double-despawn of the same pooled
        // GameObject is now idempotent - the second call is a no-op (mirrors the
        // entity path's IsSpawned guard) instead of throwing from FastPool.
        GameObject doubled = manager.SpawnGameObject(objPrefab, Vector3.zero, Quaternion.identity);
        manager.DespawnGameObject(doubled, objectKey, 0f);
        manager.DespawnGameObject(doubled, objectKey, 0f);
        Check(!doubled.activeSelf);
        // Exactly one pooled copy: the double despawn must not have returned it twice.
        Same(doubled, manager.SpawnGameObject(objPrefab, Vector3.zero, Quaternion.identity));
        // Objects never rented through the manager are ignored too (with any key).
        var strayObject = new GameObject("never-spawned");
        manager.DespawnGameObject(strayObject, objectKey, 0f);
        Check(strayObject.activeSelf, "A never-spawned object must not be deactivated.");
        manager.DespawnGameObject(doubled, objectKey, 0f);

        // A duplicate PoolManager self-destructs without clearing the live registries.
        var duplicate = CreateComponent<PoolManager>(invokeAwake: true);
        Same(manager, PoolManager.Instance);
        Check(!duplicate.gameObject.activeSelf, "The duplicate singleton must destroy itself.");
        GameEntity survivor = manager.SpawnByKey(entityKey, Vector3.zero, Quaternion.identity);
        Check(survivor != null, "Registries must survive a duplicate manager's destruction.");
        manager.Despawn(survivor);

        // Destroying the owning manager clears registries and unregisters packed lists.
        ProbeEnemy resident = SpawnEnemy(0f, 0f, 10f);
        Equal(1, Enemy.ActiveEnemies.Count);
        UnityEngine.Object.Destroy(manager.gameObject);
        Equal(null, PoolManager.Instance);
        Equal(0, Enemy.ActiveEnemies.Count);
        Equal(Config.Game.EnvironmentData.UnregisteredListIndex, resident.ActiveListIndex);
        Equal(null, FastPoolRegistry<GameEntity>.Rent(entityKey));
        Equal(null, FastPoolRegistry<GameObject>.Rent(objectKey));
    }

    // ------------------------------------------------------------------
    // 8. MapViewportController: LateUpdate ring-buffer mapping verified
    //    cell-by-cell against a naive bounds/wrap model.
    // ------------------------------------------------------------------
    private static void ManagersRefVisibleBounds(FastGridMap map, float cameraX, float cameraY,
        float fovWidth, float fovHeight, float cellSize, int padding,
        out int minX, out int maxX, out int minY, out int maxY)
    {
        float halfWidth = fovWidth * 0.5f;
        float halfHeight = fovHeight * 0.5f;
        float inverseCell = 1f / cellSize;
        minX = (int)((cameraX - halfWidth) * inverseCell) - padding;
        maxX = (int)((cameraX + halfWidth) * inverseCell) + padding;
        minY = (int)((cameraY - halfHeight) * inverseCell) - padding;
        maxY = (int)((cameraY + halfHeight) * inverseCell) + padding;
        if (minX < 0) minX = 0;
        if (maxX >= map.Width) maxX = map.Width - 1;
        if (minY < 0) minY = 0;
        if (maxY >= map.Height) maxY = map.Height - 1;
    }

    private static void ManagersVerifyViewportGrid(MapViewportController viewport)
    {
        var map = GetField<FastGridMap>(viewport, "backendMap");
        var grid = GetField<SpriteRenderer[,]>(viewport, "visualGrid");
        Camera camera = viewport.mainCamera;
        float fovHeight = camera.orthographicSize * Config.Game.Camera.OrthographicExtentMultiplier *
            Config.Game.Camera.FOVHeightMultiplier;
        float fovWidth = camera.orthographicSize * Config.Game.Camera.OrthographicExtentMultiplier *
            camera.aspect * Config.Game.Camera.FOVWidthMultiplier;
        Vector3 cameraPosition = camera.transform.position;
        ManagersRefVisibleBounds(map, cameraPosition.x, cameraPosition.y, fovWidth, fovHeight,
            viewport.cellSize, Config.Game.Map.PaddingCellsBackend,
            out int minX, out int maxX, out int minY, out int maxY);

        int cols = grid.GetLength(0);
        int rows = grid.GetLength(1);
        for (int worldX = minX; worldX <= maxX; worldX++)
        {
            for (int worldY = minY; worldY <= maxY; worldY++)
            {
                int ringX = ((worldX % cols) + cols) % cols;
                int ringY = ((worldY % rows) + rows) % rows;
                SpriteRenderer renderer = grid[ringX, ringY];
                short tile = map.GetTile(worldX, worldY);
                if (tile >= 0 && tile < viewport.tilePalette.Length)
                    Same(viewport.tilePalette[tile], renderer.sprite,
                        "Sprite mismatch at world (" + worldX + "," + worldY + ") ring (" + ringX + "," + ringY + ").");
                else
                    Same(null, renderer.sprite,
                        "Out-of-palette tile at world (" + worldX + "," + worldY + ") must blank its sprite.");
            }
        }

        // NOTE: the test-runtime Instantiate aliases every tile clone's transform to the
        // tile prefab's transform, so per-cell positions collapse to the LAST cell the
        // controller wrote: (maxX, maxY) given its X-outer/Y-inner iteration order.
        SpriteRenderer lastWritten = grid[((maxX % cols) + cols) % cols, ((maxY % rows) + rows) % rows];
        Near(maxX * viewport.cellSize, lastWritten.transform.position.x, 0f);
        Near(maxY * viewport.cellSize, lastWritten.transform.position.y, 0f);
        Near(Config.Game.Map.TileZOffset, lastWritten.transform.position.z, 0f);
    }

    private static void TestManagersViewportRingBufferModel()
    {
        FastRandom.SetSeed(0xFEEDFACEu);

        var camera = CreateComponent<Camera>();
        camera.aspect = 2f;
        var viewport = CreateComponent<MapViewportController>(invokeAwake: true);
        viewport.mainCamera = camera;
        var target = new GameObject("viewport-target").transform;
        viewport.targetToFollow = target;
        viewport.tilePalette = new[]
        {
            new Sprite { name = "tile-0" },
            new Sprite { name = "tile-1" },
            new Sprite { name = "tile-2" }
        };
        var tilePrefab = new GameObject("tile-prefab");
        tilePrefab.AddComponent<SpriteRenderer>();
        viewport.tilePrefab = tilePrefab;
        viewport.cellSize = 4f;

        Invoke(viewport, "Start");
        Near(Config.Game.Camera.DefaultZoomLevel, camera.orthographicSize, 0f);
        var grid = GetField<SpriteRenderer[,]>(viewport, "visualGrid");
        var ringBuffer = GetField<FastRingBufferViewport>(viewport, "ringBuffer");
        var map = GetField<FastGridMap>(viewport, "backendMap");
        Equal(15, grid.GetLength(0)); // ceil(38.4 / 4) + 2*2 + 1
        Equal(10, grid.GetLength(1)); // ceil(19.2 / 4) + 2*2 + 1
        Equal(15, ringBuffer.ViewportCols);
        Equal(10, ringBuffer.ViewportRows);
        Equal(Config.Game.Map.DefaultMapWidth, map.Width);
        Equal(Config.Game.Map.DefaultMapHeight, map.Height);

        // Frame 1: full mapping model.
        target.position = new Vector3(50.3f, 20.7f, 9f);
        Invoke(viewport, "LateUpdate");
        Near(50.3f, camera.transform.position.x, 0f);
        Near(20.7f, camera.transform.position.y, 0f);
        Near(Config.Game.Camera.CameraZOffset, camera.transform.position.z, 0f);
        ManagersVerifyViewportGrid(viewport);

        // Fast exit: identical bounds leave the grid untouched.
        ManagersRefVisibleBounds(map, 50.3f, 20.7f, 38.4f, 19.2f, 4f, 2,
            out int previousMinX, out _, out int previousMinY, out _);
        var marker = new Sprite { name = "marker" };
        int markerRingX = ((previousMinX % 15) + 15) % 15;
        int markerRingY = ((previousMinY % 10) + 10) % 10;
        grid[markerRingX, markerRingY].sprite = marker;
        Invoke(viewport, "LateUpdate");
        Same(marker, grid[markerRingX, markerRingY].sprite);
        target.position = new Vector3(50.5f, 20.7f, 9f); // Sub-cell move, same bounds.
        ManagersRefVisibleBounds(map, 50.5f, 20.7f, 38.4f, 19.2f, 4f, 2,
            out int nudgedMinX, out _, out int nudgedMinY, out _);
        Equal(previousMinX, nudgedMinX);
        Equal(previousMinY, nudgedMinY);
        Invoke(viewport, "LateUpdate");
        Same(marker, grid[markerRingX, markerRingY].sprite);

        // A one-cell shift rewrites the whole window through the wrap mapping.
        target.position = new Vector3(54.4f, 20.7f, 9f);
        ManagersRefVisibleBounds(map, 54.4f, 20.7f, 38.4f, 19.2f, 4f, 2,
            out int shiftedMinX, out _, out _, out _);
        Check(shiftedMinX != previousMinX, "Scenario sanity: the shift must change the bounds.");
        Invoke(viewport, "LateUpdate");
        ManagersVerifyViewportGrid(viewport);

        // Out-of-palette and negative tile ids blank their cells.
        map.SetTile(10, 5, 99);
        map.SetTile(11, 5, -5);
        ringBuffer.UpdatePreviousBounds(Config.Game.Map.InvalidBounds, Config.Game.Map.InvalidBounds);
        Invoke(viewport, "LateUpdate");
        Same(null, grid[10 % 15, 5 % 10].sprite);
        Same(null, grid[11 % 15, 5 % 10].sprite);
        ManagersVerifyViewportGrid(viewport);

        // LoadMapData blanks the grid and invalidates the cached bounds.
        viewport.LoadMapData("regenerated-map");
        for (int x = 0; x < 15; x++)
        {
            for (int y = 0; y < 10; y++) Same(null, grid[x, y].sprite);
        }
        Invoke(viewport, "LateUpdate"); // No movement, yet the grid repopulates.
        ManagersVerifyViewportGrid(viewport);

        // Losing the follow target freezes the camera and the grid.
        viewport.targetToFollow = null;
        camera.transform.position = new Vector3(-777f, -777f, -777f);
        Invoke(viewport, "LateUpdate");
        Near(-777f, camera.transform.position.x, 0f);

        // Clamping at the map origin only rewrites the clamped window.
        viewport.targetToFollow = target;
        target.position = new Vector3(1f, 1.5f, 0f);
        Invoke(viewport, "LateUpdate");
        ManagersVerifyViewportGrid(viewport);

        ManagersRestoreDefaultSeed();
    }

    // ------------------------------------------------------------------
    // 9. MapManager: switching state machine — re-entrancy guard, entity
    //    unloading through the pool, player reset, blur wind-down.
    // ------------------------------------------------------------------
    private static void TestManagersMapSwitchStateMachine()
    {
        FastRandom.SetSeed(0xD00Du);

        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var drop = CreateComponent<DropManager>(invokeAwake: true);
        drop.Tick(0f, 600f);
        var blur = CreateComponent<MapTransitionCameraEffect>(invokeAwake: true);
        var mapManager = CreateComponent<MapManager>(invokeAwake: true);
        Same(mapManager, MapManager.Instance);
        var duplicate = CreateComponent<MapManager>(invokeAwake: true);
        Same(mapManager, MapManager.Instance);
        Check(!duplicate.gameObject.activeSelf, "A duplicate MapManager must destroy itself.");
        mapManager.blurCameraEffect = blur;

        // The player must be Awake-registered so PlayerController.Instance is set and
        // the map switch can find it to reposition it.
        var playerObject = new GameObject("switch-player");
        playerObject.AddComponent<SpriteRenderer>();
        playerObject.AddComponent<EFYV.Core.Controllers.WeaponController>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        Same(player, PlayerController.Instance);
        player.entityTransform.position = new Vector3(5f, 6f, 7f);

        var enemyPrefab = CreateComponent<ProbeEnemy>(addRenderer: true);
        enemyPrefab.Initialize();
        enemyPrefab.Apply(10f, 1f, 1f, 1f);
        int enemyKey = PoolManager.GetPoolKey(enemyPrefab.gameObject);
        var pooledEnemies = new List<GameEntity>();
        for (int i = 0; i < 3; i++)
        {
            pooledEnemies.Add(pool.Spawn(enemyPrefab, new Vector3(i, 0f, 0f), Quaternion.identity));
        }
        Equal(3, Enemy.ActiveEnemies.Count);

        var projectile = CreateComponent<ProbeProjectile>(addRenderer: true);
        projectile.Initialize();
        projectile.Initialize(new Vector2(1f, 0f), 1f, 1f, 1);
        projectile.OnSpawn();
        Equal(1, Projectile.ActiveProjectiles.Count);

        var prop = CreateComponent<ProbeProp>(addRenderer: true);
        prop.Initialize();
        prop.animationFrames = new[] { new Sprite(), new Sprite() };
        prop.animationSpeed = 1f;
        prop.OnSpawn();
        Equal(1, PropEntity.ActiveAnimatedProps.Count);

        // Re-entrancy guard: while the switching flag is up, requests are ignored.
        var mapData = GetField<MapManagerData>(mapManager, "Data");
        mapData.IsSwitchingMap = true;
        SetField(mapManager, "Data", mapData);
        int messagesBefore = Debug.Messages.Count;
        mapManager.SwitchMap("ignored-map");
        Equal(messagesBefore, Debug.Messages.Count);
        Equal(3, Enemy.ActiveEnemies.Count);
        mapData.IsSwitchingMap = false;
        SetField(mapManager, "Data", mapData);

        // Full transition (the stub coroutine pump runs it synchronously).
        Time.deltaTime = 0.25f;
        blur.CurrentBlurRadius = Config.Game.Map.MinimumBlurRadius;
        blur.enabled = false;
        mapManager.SwitchMap("target-map");

        Equal(0, Enemy.ActiveEnemies.Count);
        Equal(0, Projectile.ActiveProjectiles.Count);
        Equal(0, PropEntity.ActiveAnimatedProps.Count);
        foreach (GameEntity entity in pooledEnemies)
        {
            Check(!entity.IsSpawned);
            Equal(Config.Game.EnvironmentData.UnregisteredListIndex, ((Enemy)entity).ActiveListIndex);
        }
        Check(!projectile.IsSpawned);
        Check(!prop.IsSpawned);

        // Pool-owned enemies were returned, not orphaned.
        GameEntity reused = pool.SpawnByKey(enemyKey, Vector3.zero, Quaternion.identity);
        Check(reused != null && pooledEnemies.Contains(reused),
            "Despawned enemies must return to their pool during a map switch.");
        pool.Despawn(reused);

        Near(0f, player.entityTransform.position.x, 0f);
        Near(0f, player.entityTransform.position.y, 0f);
        Near(0f, player.entityTransform.position.z, 0f);

        Equal(Config.Game.Map.MinimumBlurRadius, blur.CurrentBlurRadius);
        Check(!blur.enabled, "The blur effect must be disabled after the transition.");
        mapData = GetField<MapManagerData>(mapManager, "Data");
        Check(!mapData.IsSwitchingMap);
        Near(Config.Game.System.DefaultDynamicDropMultiplier,
            GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier, 0f);
        Check(Debug.Messages.Contains(string.Format(Config.Game.Map.LogMapManagerSwitchSuccess, "target-map")));

        // The machine is reusable: a second switch still works.
        mapManager.SwitchMap("second-map");
        Check(Debug.Messages.Contains(string.Format(Config.Game.Map.LogMapManagerSwitchSuccess, "second-map")));
        mapData = GetField<MapManagerData>(mapManager, "Data");
        Check(!mapData.IsSwitchingMap);

        UnityEngine.Object.Destroy(pool.gameObject);
        ManagersRestoreDefaultSeed();
    }

    // ------------------------------------------------------------------
    // 10. UpgradeManager: phase transition without a player, penalty
    //     arithmetic progression, and special-attack damage contracts.
    // ------------------------------------------------------------------
    private static void TestManagersUpgradePhasePenalties()
    {
        Check(PlayerController.Instance == null, "Scenario sanity: no player must exist.");
        var upgrades = CreateComponent<UpgradeManager>(invokeAwake: true);
        int normalCount = 0;
        int specialCount = 0;
        int lastSpecialChoices = -1;
        float lastPenalty = -1f;
        upgrades.OnNormalUpgradesRequested += _ => normalCount++;
        upgrades.OnSpecialAttacksRequested += (choices, penalty) =>
        {
            specialCount++;
            lastSpecialChoices = choices;
            lastPenalty = penalty;
        };

        // With no player at all, the first level-up flips straight into the special
        // phase, but the choice count was computed BEFORE the flip (normal-phase 3).
        Check(!upgrades.IsSpecialAttackPhase);
        upgrades.OnPlayerLevelUp();
        Check(upgrades.IsSpecialAttackPhase);
        Equal(0, normalCount);
        Equal(1, specialCount);
        Equal(Config.Game.Weapons.Inventory.UpgradeChoicesNormalPhase, lastSpecialChoices);
        Near(Config.Game.Weapons.Inventory.PenaltyMultiplierBase, lastPenalty, 0f);

        // Subsequent level-ups use the special-phase choice count.
        upgrades.OnPlayerLevelUp();
        Equal(2, specialCount);
        Equal(Config.Game.Weapons.Inventory.UpgradeChoicesSpecialPhase, lastSpecialChoices);
        Near(1.1f, lastPenalty, 1e-6f);

        upgrades.OpenChest(9);
        Equal(3, specialCount);
        Equal(9, lastSpecialChoices);
        Near(1.2f, lastPenalty, 1e-6f);

        // Penalty grows as an arithmetic progression of 0.1 per invocation.
        for (int invocation = 3; invocation < 10; invocation++)
        {
            upgrades.OpenChest(1);
            Near(1f + (invocation * 0.1f), lastPenalty, 1e-5f);
        }
        Equal(0, normalCount);

        // Screen wipe deals a fixed 999999 damage: enormous enemies survive it.
        ProbeEnemy tough = SpawnEnemy(0f, 0f, 2000000f);
        ProbeEnemy weak = SpawnEnemy(1f, 0f, 500f);
        ProbeEnemy fractional = SpawnEnemy(2f, 0f, 0.5f);
        Equal(3, Enemy.ActiveEnemies.Count);
        upgrades.InvokeSpecialAttack(Config.Game.Weapons.SpecialAttackScreenWipeName);
        Equal(1, Enemy.ActiveEnemies.Count);
        Same(tough, Enemy.ActiveEnemies[0]);
        float toughRemaining = 2000000f - Config.Game.Weapons.SpecialAttackScreenWipeDamage;
        Near(toughRemaining, tough.CurrentHealth, 0f);
        Equal(1, weak.DeathCalls);
        Equal(1, fractional.DeathCalls);

        // Half-mob-health is exact float halving via damage = hp * 0.5.
        upgrades.InvokeSpecialAttack(Config.Game.Weapons.SpecialAttackHalfMobHealthName);
        float expectedHalf = toughRemaining - (toughRemaining * 0.5f);
        Near(expectedHalf, tough.CurrentHealth, 0f);

        // Unknown attack names are ignored.
        upgrades.InvokeSpecialAttack("not-a-real-attack");
        Near(expectedHalf, tough.CurrentHealth, 0f);
        Equal(1, Enemy.ActiveEnemies.Count);
    }

    // ------------------------------------------------------------------
    // batch2/game-managers agent: 11. Game over (#25a) - SpawnManager stops
    // spawning, AIDirector stops scaling, MapManager halts transitions, but the
    // central entity ticking keeps the world alive around the corpse.
    // ------------------------------------------------------------------
    private static void TestManagersGameOverReactions()
    {
        FastRandom.SetSeed(0xB2D1u);

        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var playerObject = new GameObject("gameover-player");
        playerObject.AddComponent<SpriteRenderer>();
        playerObject.AddComponent<EFYV.Core.Controllers.WeaponController>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        Same(player, PlayerController.Instance);

        var spawner = CreateComponent<SpawnManager>();
        Invoke(spawner, "Awake");
        spawner.playerTransform = player.entityTransform;
        var enemyPrefab = CreateComponent<ProbeEnemy>(addRenderer: true);
        enemyPrefab.Initialize();
        enemyPrefab.Apply(10f, 1f, 1f, 1f);
        spawner.enemyPrefabs = new Enemy[] { enemyPrefab };
        spawner.baseSpawnRate = 5f;
        spawner.difficultyMultiplier = 0f;

        var director = CreateComponent<AIDirector>(invokeAwake: true);
        director.spawnManager = spawner;
        var drop = CreateComponent<DropManager>(invokeAwake: true);
        var mapManager = CreateComponent<MapManager>(invokeAwake: true);

        // Pre-death: spawning runs and the director scales past 1x.
        Time.deltaTime = 1f;
        Invoke(spawner, "Update");
        Near(1f, spawner.GameTimer, 0f);
        Check(Enemy.ActiveEnemies.Count > 0, "Scenario sanity: spawning must be live before death.");
        Check(director.GetIntensityMultiplier() > Config.Game.AI.DefaultMultiplier);
        float dropMultiplierAtDeath = GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier;

        // Death latches every subscribed manager.
        player.TakeDamage(float.MaxValue);
        Check(player.IsDead);

        // SpawnManager: the timer, the accumulator, and spawning all freeze even
        // against an absurd spawn rate.
        spawner.baseSpawnRate = float.MaxValue;
        float accumulatorAtDeath = GetField<SpawnManagerData>(spawner, "Data").SpawnAccumulator;
        int enemiesAtDeath = Enemy.ActiveEnemies.Count;
        Time.deltaTime = 10f;
        Invoke(spawner, "Update");
        Near(1f, spawner.GameTimer, 0f);
        Near(accumulatorAtDeath, GetField<SpawnManagerData>(spawner, "Data").SpawnAccumulator, 0f);
        Equal(enemiesAtDeath, Enemy.ActiveEnemies.Count);
        Near(dropMultiplierAtDeath, GetField<DropManagerData>(drop, "Data").DynamicDropMultiplier, 0f,
            "The drop multiplier must freeze with the survival timer.");

        // ...but the central ticking still runs: a rallied enemy keeps moving.
        var rally = new GameObject("gameover-rally").transform;
        rally.position = new Vector3(100f, 0f, 0f);
        var walker = (ProbeEnemy)Enemy.ActiveEnemies[0];
        walker.SetTarget(rally);
        int movesBefore = walker.TickMoves;
        Invoke(spawner, "Update");
        Equal(movesBefore + 1, walker.TickMoves, "Central enemy ticking must continue after game over.");

        // AIDirector: every multiplier reports the neutral 1x after game over.
        Near(Config.Game.AI.DefaultMultiplier, director.GetIntensityMultiplier(), 0f);
        Near(Config.Game.AI.DefaultMultiplier, director.GetEnemyHealthMultiplier(), 0f);
        Near(Config.Game.AI.DefaultMultiplier, director.GetEnemySpeedMultiplier(), 0f);

        // MapManager: transition requests are ignored - no log, no unloads.
        int messagesBefore = Debug.Messages.Count;
        int enemiesBefore = Enemy.ActiveEnemies.Count;
        mapManager.SwitchMap("gameover-map");
        Equal(messagesBefore, Debug.Messages.Count);
        Equal(enemiesBefore, Enemy.ActiveEnemies.Count);
        Check(!GetField<MapManagerData>(mapManager, "Data").IsSwitchingMap);

        // Unsubscription: destroyed managers must not observe (or throw on) a
        // later death broadcast.
        UnityEngine.Object.Destroy(spawner.gameObject);
        UnityEngine.Object.Destroy(director.gameObject);
        UnityEngine.Object.Destroy(mapManager.gameObject);
        player.OnSpawn();
        Check(!player.IsDead);
        Time.deltaTime = 1f;
        Invoke(player, "Update"); // Burn off the i-frames from the lethal hit.
        player.TakeDamage(float.MaxValue);
        Check(player.IsDead, "A second death after manager teardown must not throw.");

        UnityEngine.Object.Destroy(pool.gameObject);
        ManagersRestoreDefaultSeed();
    }

    // ------------------------------------------------------------------
    // batch2/game-managers agent: 12. Scene-placed entity lifecycle (#25b) -
    // Awake registration (pool clones and prefab assets excluded), promotion
    // into the centralized loops, and map-switch cleanup.
    // ------------------------------------------------------------------
    private static void TestManagersScenePlacedLifecycle()
    {
        FastRandom.SetSeed(0xC2E5u);

        Equal(0, GameEntity.PendingSceneEntityCount);
        var pool = CreateComponent<PoolManager>(invokeAwake: true);

        var playerObject = new GameObject("scene-player");
        playerObject.AddComponent<SpriteRenderer>();
        playerObject.AddComponent<EFYV.Core.Controllers.WeaponController>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        // The player opts out of scene-placed tracking: it is repositioned, never
        // despawned, on map switches.
        Equal(0, GameEntity.PendingSceneEntityCount);

        // A scene-dropped enemy (Awake runs, never pool-spawned) registers as
        // pending but joins nothing until promotion.
        var sceneEnemyObject = new GameObject("scene-enemy");
        sceneEnemyObject.AddComponent<SpriteRenderer>();
        var sceneEnemy = (ProbeEnemy)sceneEnemyObject.AddComponent(typeof(ProbeEnemy), true);
        sceneEnemy.Apply(30f, 2f, 3f, 4f);
        Equal(1, GameEntity.PendingSceneEntityCount);
        Check(!sceneEnemy.IsSpawned);
        Equal(0, Enemy.ActiveEnemies.Count);

        // A scene-dropped animated prop registers too.
        var scenePropObject = new GameObject("scene-prop");
        scenePropObject.AddComponent<SpriteRenderer>();
        var sceneProp = (ProbeProp)scenePropObject.AddComponent(typeof(ProbeProp), true);
        sceneProp.animationFrames = new[] { new Sprite(), new Sprite() };
        sceneProp.animationSpeed = 0.25f;
        Equal(2, GameEntity.PendingSceneEntityCount);

        // A prefab stand-in (invalid scene, mirroring Unity prefab assets) does NOT.
        var prefabObject = new GameObject("scene-prefab-asset");
        prefabObject.scene = new UnityEngine.SceneManagement.Scene(false, false);
        prefabObject.AddComponent<SpriteRenderer>();
        var prefabStandIn = (ProbeEnemy)prefabObject.AddComponent(typeof(ProbeEnemy), true);
        prefabStandIn.Apply(20f, 1f, 1f, 1f);
        Equal(2, GameEntity.PendingSceneEntityCount);

        // Pool factory clones never register either (the pooled path is respected).
        GameEntity pooled = pool.Spawn(prefabStandIn, new Vector3(9f, 9f, 0f), Quaternion.identity);
        Check(pooled != null);
        Equal(2, GameEntity.PendingSceneEntityCount);
        Equal(1, Enemy.ActiveEnemies.Count);

        // SpawnManager.Update promotes pending entities: they enter the per-type
        // swap lists, Tick with everyone else, and are targetable.
        var spawner = CreateComponent<SpawnManager>();
        Invoke(spawner, "Awake");
        spawner.playerTransform = player.entityTransform;
        spawner.enemyPrefabs = Array.Empty<Enemy>();
        int movesBefore = sceneEnemy.TickMoves;
        Time.deltaTime = 0.5f;
        Invoke(spawner, "Update");
        Equal(0, GameEntity.PendingSceneEntityCount);
        Equal(2, GameEntity.TrackedSceneEntityCount);
        Check(sceneEnemy.IsSpawned);
        Check(sceneProp.IsSpawned);
        Equal(2, Enemy.ActiveEnemies.Count);
        Equal(1, PropEntity.ActiveAnimatedProps.Count);
        Equal(movesBefore + 1, sceneEnemy.TickMoves,
            "A promoted scene enemy must Tick in the same frame.");
        Check(sceneEnemy.ActiveListIndex >= 0, "Promoted enemies join the targetable swap list.");

        // Map switch (#25): scene-placed entities are deactivated and forgotten;
        // pooled entities return to their pool; the player is only repositioned.
        var mapManager = CreateComponent<MapManager>(invokeAwake: true);
        player.entityTransform.position = new Vector3(4f, 5f, 6f);
        mapManager.SwitchMap("scene-map");
        Check(!sceneEnemy.IsSpawned);
        Check(!sceneEnemy.gameObject.activeSelf, "Scene-placed enemies must be deactivated by a map switch.");
        Check(!sceneProp.IsSpawned);
        Check(!sceneProp.gameObject.activeSelf, "Scene-placed props must be deactivated by a map switch.");
        Check(!pooled.IsSpawned);
        Equal(0, Enemy.ActiveEnemies.Count);
        Equal(0, PropEntity.ActiveAnimatedProps.Count);
        Equal(0, GameEntity.TrackedSceneEntityCount);
        Equal(0, GameEntity.PendingSceneEntityCount);
        Check(player.gameObject.activeSelf, "The player survives the map switch.");
        Near(0f, player.entityTransform.position.x, 0f);
        Near(0f, player.entityTransform.position.y, 0f);

        // Entities dropped right before a switch (still pending, no SpawnManager
        // frame in between) are promoted inside the switch and cleaned too.
        var lateObject = new GameObject("scene-late");
        lateObject.AddComponent<SpriteRenderer>();
        var late = (ProbeEnemy)lateObject.AddComponent(typeof(ProbeEnemy), true);
        Equal(1, GameEntity.PendingSceneEntityCount);
        mapManager.SwitchMap("scene-map-2");
        Equal(0, GameEntity.PendingSceneEntityCount);
        Equal(0, GameEntity.TrackedSceneEntityCount);
        Check(!late.gameObject.activeSelf);
        Equal(0, Enemy.ActiveEnemies.Count);

        // A pending entity deactivated before promotion is dropped, not spawned.
        var dormantObject = new GameObject("scene-dormant");
        dormantObject.AddComponent<SpriteRenderer>();
        var dormant = (ProbeEnemy)dormantObject.AddComponent(typeof(ProbeEnemy), true);
        dormantObject.SetActive(false);
        Equal(1, GameEntity.PendingSceneEntityCount);
        GameEntity.ActivatePendingSceneEntities();
        Equal(0, GameEntity.PendingSceneEntityCount);
        Equal(0, GameEntity.TrackedSceneEntityCount);
        Check(!dormant.IsSpawned);

        // Already-spawned pending entities are tracked without a second OnSpawn.
        ProbeEnemy manual = SpawnEnemy(1f, 1f, 10f); // No Awake: not pending.
        Equal(0, GameEntity.PendingSceneEntityCount);
        var manualSceneObject = new GameObject("scene-manual");
        manualSceneObject.AddComponent<SpriteRenderer>();
        var manualScene = (ProbeEnemy)manualSceneObject.AddComponent(typeof(ProbeEnemy), true);
        manualScene.OnSpawn();
        int spawnedEnemies = Enemy.ActiveEnemies.Count;
        GameEntity.ActivatePendingSceneEntities();
        Equal(spawnedEnemies, Enemy.ActiveEnemies.Count, "Promotion must not double-OnSpawn.");
        Equal(1, GameEntity.TrackedSceneEntityCount);
        manual.OnDespawn();
        manualScene.OnDespawn();

        // World teardown clears the registry.
        var strayObject = new GameObject("scene-stray");
        strayObject.AddComponent<SpriteRenderer>();
        strayObject.AddComponent(typeof(ProbeEnemy), true);
        Equal(1, GameEntity.PendingSceneEntityCount);
        UnityEngine.Object.Destroy(pool.gameObject);
        Equal(0, GameEntity.PendingSceneEntityCount);
        Equal(0, GameEntity.TrackedSceneEntityCount);

        ManagersRestoreDefaultSeed();
    }

    // ------------------------------------------------------------------
    // batch2/game-managers agent: 13. Pool prewarming (#32) and the XP gem
    // drop-table wiring (#24) end to end.
    // ------------------------------------------------------------------
    private static void TestManagersPrewarmAndXpGemDrops()
    {
        FastRandom.SetSeed(0xD32Au);

        // Without a manager the static hooks are safe no-ops.
        var hookPrefab = CreateComponent<ProbeEntity>(addRenderer: true);
        hookPrefab.Initialize();
        Check(!PoolManager.TryPrewarm(hookPrefab, 4));
        Check(!PoolManager.TryPrewarmGameObject(hookPrefab.gameObject, 4));
        Equal(1, Resources.FindObjectsOfTypeAll<ProbeEntity>().Length);

        var pool = CreateComponent<PoolManager>(invokeAwake: true);

        // #32 static hooks: prewarm through the singleton once it exists.
        Check(PoolManager.TryPrewarm(hookPrefab, 4));
        Equal(1 + 4, Resources.FindObjectsOfTypeAll<ProbeEntity>().Length,
            "TryPrewarm must create exactly the requested pool population.");
        var vfxPrefab = new GameObject("prewarm-vfx");
        Check(PoolManager.TryPrewarmGameObject(vfxPrefab, 3));
        GameObject rented = pool.SpawnGameObject(vfxPrefab, Vector3.zero, Quaternion.identity);
        Check(rented != null);
        pool.DespawnGameObject(rented, PoolManager.GetPoolKey(vfxPrefab), 0f);

        // DropManager.Awake prewarms the coin/chest/gem pools (#32).
        var drop = CreateComponent<DropManager>();
        var coinPrefab = CreateComponent<CoinProp>(addRenderer: true);
        coinPrefab.Initialize();
        var chestPrefab = CreateComponent<ChestProp>(addRenderer: true);
        chestPrefab.Initialize();
        var gemPrefab = CreateComponent<XPGem>(addRenderer: true);
        gemPrefab.Initialize();
        gemPrefab.xpValue = 40f;
        drop.coinPrefab = coinPrefab;
        drop.chestPrefab = chestPrefab;
        drop.xpGemPrefab = gemPrefab;
        int coinsBefore = Resources.FindObjectsOfTypeAll<CoinProp>().Length;
        int chestsBefore = Resources.FindObjectsOfTypeAll<ChestProp>().Length;
        int gemsBefore = Resources.FindObjectsOfTypeAll<XPGem>().Length;
        Invoke(drop, "Awake");
        Equal(coinsBefore + DropManager.CoinPoolPrewarmCount, Resources.FindObjectsOfTypeAll<CoinProp>().Length);
        Equal(chestsBefore + DropManager.ChestPoolPrewarmCount, Resources.FindObjectsOfTypeAll<ChestProp>().Length);
        Equal(gemsBefore + DropManager.XpGemPoolPrewarmCount, Resources.FindObjectsOfTypeAll<XPGem>().Length);

        // Renting from a prewarmed pool creates nothing new.
        int coinObjects = Resources.FindObjectsOfTypeAll<CoinProp>().Length;
        GameEntity rentedCoin = pool.Spawn(coinPrefab, Vector3.zero, Quaternion.identity);
        Check(rentedCoin != null);
        Equal(coinObjects, Resources.FindObjectsOfTypeAll<CoinProp>().Length);
        pool.Despawn(rentedCoin);

        // Prewarmed clones are dormant: nothing joined the live lists and the
        // pooled path never touched the scene-placed registry (#25).
        Equal(0, Enemy.ActiveEnemies.Count);
        Equal(0, PropEntity.ActiveAnimatedProps.Count);
        Equal(0, GameEntity.PendingSceneEntityCount);

        // SpawnManager.Start prewarms every enemy prefab pool (#32).
        var spawner = CreateComponent<SpawnManager>();
        Invoke(spawner, "Awake");
        var prefabA = CreateComponent<ProbeEnemy>(addRenderer: true);
        prefabA.Initialize();
        prefabA.Apply(10f, 1f, 1f, 1f);
        var prefabB = CreateComponent<ProbeEnemy>(addRenderer: true);
        prefabB.Initialize();
        prefabB.Apply(20f, 2f, 2f, 2f);
        spawner.enemyPrefabs = new Enemy[] { prefabA, prefabB };
        int enemiesBefore = Resources.FindObjectsOfTypeAll<ProbeEnemy>().Length;
        Invoke(spawner, "Start");
        Equal(enemiesBefore + (2 * SpawnManager.EnemyPoolPrewarmCount),
            Resources.FindObjectsOfTypeAll<ProbeEnemy>().Length);
        Equal(0, Enemy.ActiveEnemies.Count, "Prewarmed enemies must stay dormant.");

        // End to end (#24): a boss kill always drops an XP gem; touching it grants
        // the prefab-configured experience and pools the gem again.
        var playerObject = new GameObject("prewarm-player");
        playerObject.AddComponent<SpriteRenderer>();
        playerObject.AddComponent<EFYV.Core.Controllers.WeaponController>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        var boss = CreateComponent<ProbeBoss>(addRenderer: true);
        boss.Initialize();
        boss.Apply(100f, 1f, 40f);
        boss.entityTransform.position = new Vector3(2f, 3f, 0f);
        FastRandom.SetSeed(0xFEEDu);
        drop.DropLoot(boss);
        XPGem droppedGem = null;
        foreach (XPGem gem in Resources.FindObjectsOfTypeAll<XPGem>())
        {
            if (gem.IsSpawned) droppedGem = gem;
        }
        Check(droppedGem != null, "A boss kill must always drop an XP gem.");
        Near(40f, droppedGem.xpValue, 0f);
        Near(2f, droppedGem.entityTransform.position.x, 0f);
        Near(3f, droppedGem.entityTransform.position.y, 0f);
        droppedGem.OnInteract(player);
        Near(40f, GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData").Experience, 0f);
        Check(!droppedGem.IsSpawned, "An interacted gem must return to its pool.");

        foreach (ChestProp chest in Resources.FindObjectsOfTypeAll<ChestProp>())
        {
            if (chest.IsSpawned) pool.Despawn(chest);
        }
        foreach (CoinProp coin in Resources.FindObjectsOfTypeAll<CoinProp>())
        {
            if (coin.IsSpawned) pool.Despawn(coin);
        }

        UnityEngine.Object.Destroy(pool.gameObject);
        ManagersRestoreDefaultSeed();
    }
}
