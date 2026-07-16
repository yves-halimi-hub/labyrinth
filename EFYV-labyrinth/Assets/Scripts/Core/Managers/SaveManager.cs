using UnityEngine;
using System.IO;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Data;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class SaveManager : EFYV.Core.Utils.Singleton<SaveManager>
    {
        public PlayerMetaSchema currentSaveData;
        private string saveFilePath;
        private bool hasUnsavedChanges;
        private float dirtySaveDeadline;

        protected override void Awake()
        {
            base.Awake();
            if (Instance == this)
            {
                // MIGRATION: Replaced the slow, allocating JSON string file with a raw C-level .bin file
                saveFilePath = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, GameConfig.Save.SaveFileName);
                LoadGame();
            }
        }

        public void SaveGame()
        {
            // MIGRATION: Instant pointer-based struct serialization
            FastSaveEngine.SaveGame(saveFilePath, ref currentSaveData);
            hasUnsavedChanges = false;
            Debug.Log(string.Format(GameConfig.Save.LogSaveSuccess, saveFilePath));
        }

        public void LoadGame()
        {
            if (FastSaveEngine.LoadGame(saveFilePath, out currentSaveData))
            {
                Debug.Log(GameConfig.Save.LogLoadSuccess);
            }
            else
            {
                Debug.Log(GameConfig.Save.LogLoadFailNewProfile);
            }

            hasUnsavedChanges = false;
        }

        private void Update()
        {
            if (hasUnsavedChanges && Time.unscaledTime >= dirtySaveDeadline) SaveGame();
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused) FlushUnsavedChanges();
        }

        private void OnApplicationQuit()
        {
            FlushUnsavedChanges();
        }

        /// <summary>
        /// Instantly computes the combined stats of Account Legacy + Toon Specific Progression
        /// entirely within the unmanaged memory struct without allocations.
        /// </summary>
        public FastSchemaBlock GetCombinedStatsForToon(string toonId)
        {
            if (string.IsNullOrWhiteSpace(toonId)) return currentSaveData.LegacyStats;
            int toonHash = EFYVBackend.Core.Math.FastMath.FastHash(toonId);
            
            // Loop through the fixed buffer to find the toon
            for (int i = 0; i < PlayerMetaSchema.MaxToons; i++)
            {
                if (!currentSaveData.TryGetToonBlock(i, out FastSchemaBlock toonBlock)) break;
                int storedToonHash = toonBlock.GetInt((int)ToonSchema.ToonIdHash);
                if (storedToonHash == toonHash)
                {
                    // Found! Combine Account Legacy Stats + Toon Stats
                    return CombineStats(currentSaveData.LegacyStats, toonBlock);
                }
                if (storedToonHash == GameConfig.Progression.EmptyToonHash)
                {
                    break; // Reached the end of initialized toons
                }
            }

            // Fallback: Just return Account Legacy Stats
            return currentSaveData.LegacyStats;
        }

        /// <summary>
        /// Instantly adds coins to a Toon and calculates level ups using integer division.
        /// Zero allocations.
        /// </summary>
        public void AddCoinsToToon(string toonId, int coinAmount)
        {
            if (string.IsNullOrWhiteSpace(toonId) || coinAmount <= GameConfig.Progression.PositiveCoinThreshold) return;

            int toonHash = EFYVBackend.Core.Math.FastMath.FastHash(toonId);
            if (toonHash == GameConfig.Progression.EmptyToonHash) return;
            
            // Loop through the fixed buffer to find or initialize the toon
            for (int i = 0; i < PlayerMetaSchema.MaxToons; i++)
            {
                if (!currentSaveData.TryGetToonBlock(i, out FastSchemaBlock toonBlock)) break;
                int storedToonHash = toonBlock.GetInt((int)ToonSchema.ToonIdHash);
                 
                // If we found an empty slot, initialize it
                if (storedToonHash == GameConfig.Progression.EmptyToonHash)
                {
                    toonBlock.SetInt((int)ToonSchema.ToonIdHash, toonHash);
                    toonBlock.SetInt((int)ToonSchema.Level, GameConfig.Progression.InitialToonLevel);
                    toonBlock.SetInt((int)ToonSchema.TotalCoinsCollected, GameConfig.Progression.InitialToonCoins);
                    toonBlock.SetInt((int)ToonSchema.UnspentStatPoints, GameConfig.Progression.InitialUnspentStatPoints);
                    
                    // Initialize the embedded stats block inside the Toon Schema
                    FastSchemaBlock defStats = FastSchemaBlock.DefaultStats();
                    for(int s = 0; s < (int)StatSchema.MAX_STATS; s++)
                    {
                        toonBlock.SetFloat((int)ToonSchema.StatsStart + s, defStats.GetFloat(s));
                    }
                    storedToonHash = toonHash;
                }
                 
                if (storedToonHash == toonHash)
                {
                    int totalCoins = SaturatingAdd(toonBlock.GetInt((int)ToonSchema.TotalCoinsCollected), coinAmount);
                    toonBlock.SetInt((int)ToonSchema.TotalCoinsCollected, totalCoins);
                    
                    // Calculate expected level: 1 + (TotalCoins / CoinsPerLevel)
                    int expectedLevel = GameConfig.Progression.InitialToonLevel + (totalCoins / GameConfig.Progression.CoinsPerToonLevel);
                    
                    int currentLevel = toonBlock.GetInt((int)ToonSchema.Level);
                    int unspent = toonBlock.GetInt((int)ToonSchema.UnspentStatPoints);
                    if (currentLevel < expectedLevel)
                    {
                        int levelsGained = expectedLevel - currentLevel;
                        currentLevel = expectedLevel;
                        int statPointsGained = SaturatingMultiply(levelsGained, GameConfig.Progression.StatPointsPerLevel);
                        unspent = SaturatingAdd(unspent, statPointsGained);
                        Debug.LogFormat(GameConfig.Progression.LogToonLevelUp, toonId, currentLevel, statPointsGained);
                    }
                     
                    toonBlock.SetInt((int)ToonSchema.Level, currentLevel);
                    toonBlock.SetInt((int)ToonSchema.UnspentStatPoints, unspent);

                    if (!currentSaveData.TrySetToonBlock(i, toonBlock)) return;
                    currentSaveData.TotalCoinsCollected = SaturatingAdd(currentSaveData.TotalCoinsCollected, coinAmount);
                    MarkSaveDirty();
                     
                    return;
                }
            }
            
            Debug.LogWarningFormat(GameConfig.Save.LogMaxToonsReached, PlayerMetaSchema.MaxToons, toonId);
        }

        /// <summary>
        /// Spends a stat point for a specific Toon to upgrade a target stat.
        /// </summary>
        public bool SpendToonStatPoint(string toonId, StatSchema statType)
        {
            if (string.IsNullOrWhiteSpace(toonId) || !IsValidStat(statType)) return false;
            int toonHash = EFYVBackend.Core.Math.FastMath.FastHash(toonId);
            for (int i = 0; i < PlayerMetaSchema.MaxToons; i++)
            {
                if (!currentSaveData.TryGetToonBlock(i, out FastSchemaBlock toonBlock)) break;
                int storedToonHash = toonBlock.GetInt((int)ToonSchema.ToonIdHash);
                if (storedToonHash == GameConfig.Progression.EmptyToonHash) break;
                 
                if (storedToonHash == toonHash)
                {
                    int unspent = toonBlock.GetInt((int)ToonSchema.UnspentStatPoints);
                    if (unspent <= GameConfig.Progression.EmptyUnspentStatPoints)
                    {
                        Debug.LogWarningFormat(GameConfig.Save.LogNoUnspentPoints, toonId);
                        return false;
                    }

                    // Apply the upgrade using the Toon offset
                    ApplyStatUpgradeLogic(ref toonBlock, statType, (int)ToonSchema.StatsStart);

                    toonBlock.SetInt((int)ToonSchema.UnspentStatPoints, unspent - GameConfig.Progression.ToonStatPointCost);
                    if (!currentSaveData.TrySetToonBlock(i, toonBlock)) return false;
                    Debug.LogFormat(GameConfig.Progression.LogToonStatUpgraded, toonId, statType.ToString(), unspent - GameConfig.Progression.ToonStatPointCost);
                    SaveGame();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Spends global coins to upgrade Account Legacy Stats.
        /// </summary>
        public bool SpendCoinsOnLegacyStat(StatSchema statType, int coinCost)
        {
            if (!IsValidStat(statType) || coinCost <= GameConfig.Progression.PositiveCoinThreshold) return false;
            if (currentSaveData.TotalCoinsCollected < coinCost)
            {
                Debug.LogWarning(GameConfig.Save.LogNotEnoughLegacyCoins);
                return false;
            }

            currentSaveData.TotalCoinsCollected -= coinCost;
            ApplyStatUpgradeLogic(ref currentSaveData.LegacyStats, statType, GameConfig.Progression.LegacyStatsOffset);
            
            Debug.LogFormat(GameConfig.Save.LogBoughtLegacyUpgrade, statType.ToString(), currentSaveData.TotalCoinsCollected);
            SaveGame();
            return true;
        }

        // Shared upgrade logic map
        private void ApplyStatUpgradeLogic(ref FastSchemaBlock block, StatSchema statType, int offsetBase)
        {
            int offset = offsetBase + (int)statType;
            switch (statType)
            {
                // Additive 10%
                case StatSchema.MaxHealth:
                case StatSchema.Recovery:
                case StatSchema.Magnet:
                case StatSchema.Luck:
                case StatSchema.Greed:
                case StatSchema.Curse:
                    block.ApplyAdditiveFloat(offset, GameConfig.Progression.StatUpgradeAdditiveTenPercent);
                    break;
                // Additive 5%
                case StatSchema.Might:
                case StatSchema.Area:
                case StatSchema.WeaponSpeed:
                case StatSchema.Duration:
                case StatSchema.Growth:
                case StatSchema.MoveSpeed:
                    block.ApplyAdditiveFloat(offset, GameConfig.Progression.StatUpgradeAdditiveFivePercent);
                    break;
                // Multiplicative 5% reduction
                case StatSchema.Cooldown:
                    block.ApplyMultiplicativeFloat(offset, GameConfig.Progression.StatUpgradeMultiplicativeFivePercentReduction);
                    break;
                // Flat Integers
                case StatSchema.Armor:
                case StatSchema.Revival:
                case StatSchema.Amount:
                    block.ApplyAdditiveFloat(offset, GameConfig.Progression.StatUpgradeFlatOne);
                    break;
            }
        }

        // Backend struct-math combine
        private FastSchemaBlock CombineStats(FastSchemaBlock accountStats, FastSchemaBlock toonBlock)
        {
            FastSchemaBlock combined = FastSchemaBlock.DefaultStats();
            
            for (int i = 0; i < (int)StatSchema.MAX_STATS; i++)
            {
                StatSchema t = (StatSchema)i;
                float aVal = accountStats.GetFloat(i);
                float bVal = toonBlock.GetFloat((int)ToonSchema.StatsStart + i);

                if (t == StatSchema.Cooldown)
                {
                    combined.SetFloat(i, aVal * bVal);
                }
                else if (t == StatSchema.Recovery || t == StatSchema.Armor || 
                         t == StatSchema.Revival || t == StatSchema.Amount)
                {
                    combined.SetFloat(i, aVal + bVal);
                }
                else
                {
                    // Multipliers default to 1.0, so additive math removes the base 1.0 overlap
                    combined.SetFloat(i, aVal + (bVal - GameConfig.Progression.DefaultMultiplier));
                }
            }

            return combined;
        }

        private void MarkSaveDirty()
        {
            if (hasUnsavedChanges) return;

            hasUnsavedChanges = true;
            dirtySaveDeadline = Time.unscaledTime + GameConfig.Save.DirtySaveDebounceSeconds;
        }

        private void FlushUnsavedChanges()
        {
            if (hasUnsavedChanges) SaveGame();
        }

        private static bool IsValidStat(StatSchema statType)
        {
            int statIndex = (int)statType;
            return statIndex >= GameConfig.Runtime.FirstIndex && statIndex < (int)StatSchema.MAX_STATS;
        }

        private static int SaturatingAdd(int value, int increment)
        {
            long result = (long)value + increment;
            return result > int.MaxValue ? int.MaxValue : (int)result;
        }

        private static int SaturatingMultiply(int value, int multiplier)
        {
            long result = (long)value * multiplier;
            return result > int.MaxValue ? int.MaxValue : (int)result;
        }
    }
}
