using UnityEngine;
using System.Collections.Generic;
using EFYVBackend.Core.Data;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class AchievementManager : EFYV.Core.Utils.Singleton<AchievementManager>
    {
        [Tooltip(GameConfig.Achievements.TooltipAchievementDatabase)]
        public LegacyAchievementDatabase achievementDatabase;

        public delegate void OnAchievementUnlockedHandler(LegacyAchievementDefinition achievement);
        public event OnAchievementUnlockedHandler OnAchievementUnlocked;

        // ------------------------------------------------------------------
        // Event-driven gameplay triggers (#34), ticked centrally with O(1)
        // threshold checks - no per-frame string operations. The id/threshold
        // tables mirror the shipped basis database (see
        // GameConfig.Achievements.BasisData): ids 0-4 are the kill ladder
        // ("First Blood".."Labyrinth Cleaner"), ids 18/19 are the survival pair
        // ("Unstoppable" 10 min, "Immortal" 30 min).
        // The tables live in the shared config (Achievements.Triggers); these
        // fields alias them. Kill counts are per-session (PlayerMetaSchema has
        // no lifetime kill slot yet) - deferred as a backend schema change.
        // ------------------------------------------------------------------
        private static readonly int[] KillThresholds = GameConfig.Achievements.Triggers.KillThresholds;
        private static readonly int[] KillAchievementIds = GameConfig.Achievements.Triggers.KillAchievementIds;
        private static readonly float[] SurvivalThresholdSeconds = GameConfig.Achievements.Triggers.SurvivalThresholdSeconds;
        private static readonly int[] SurvivalAchievementIds = GameConfig.Achievements.Triggers.SurvivalAchievementIds;

        private int sessionKillCount;
        private int nextKillThresholdIndex;
        private int nextSurvivalThresholdIndex;

        public int SessionKillCount => sessionKillCount;

        protected override void Awake()
        {
            base.Awake();
            // #24: OnValidate never runs in player builds, so the serialized
            // definition fields are re-synced into their packed Data blocks
            // (id, title/description hashes) on startup.
            if (IsSingletonInstance && achievementDatabase != null)
            {
                achievementDatabase.SyncAllDefinitions();
            }
        }

        /// <summary>
        /// Kill-count trigger (#34). Called once per enemy death from
        /// DropManager.DropLoot (the central death seam). Consumes no PRNG state.
        /// </summary>
        public void NotifyEnemyKilled()
        {
            if (sessionKillCount < int.MaxValue) sessionKillCount++;
            if (SaveManager.Instance == null) return; // Unlock storage unavailable.

            while (nextKillThresholdIndex < KillThresholds.Length &&
                sessionKillCount >= KillThresholds[nextKillThresholdIndex])
            {
                UnlockAchievement(KillAchievementIds[nextKillThresholdIndex]);
                nextKillThresholdIndex++;
            }
        }

        /// <summary>
        /// Survival-time trigger (#34). Called per frame by SpawnManager.Update
        /// with the central game timer; each call is a bounded float comparison.
        /// </summary>
        public void NotifySurvivalTime(float survivalSeconds)
        {
            if (SaveManager.Instance == null) return; // Unlock storage unavailable.

            while (nextSurvivalThresholdIndex < SurvivalThresholdSeconds.Length &&
                survivalSeconds >= SurvivalThresholdSeconds[nextSurvivalThresholdIndex])
            {
                UnlockAchievement(SurvivalAchievementIds[nextSurvivalThresholdIndex]);
                nextSurvivalThresholdIndex++;
            }
        }

        /// <summary>
        /// Instantly checks if an achievement is unlocked using the O(1) FastSchemaBlock memory space.
        /// </summary>
        public bool IsAchievementUnlocked(int achievementId)
        {
            if (achievementId < GameConfig.Achievements.MinimumId || achievementId >= GameConfig.Achievements.MaxAchievements) return false;

            int intIndex = achievementId / GameConfig.Achievements.BitsPerInt;
            int bitMask = GameConfig.Achievements.BitMaskSeed << (achievementId % GameConfig.Achievements.BitsPerInt);

            // Direct field access to the unmanaged memory struct
            return (SaveManager.Instance.currentSaveData.LegacyAchievements.GetInt(intIndex) & bitMask) != GameConfig.Achievements.LockedBitValue;
        }

        /// <summary>
        /// Unlocks the achievement by applying a bitwise OR operation to the fast memory block,
        /// then persists it and fires UI/audio events.
        /// </summary>
        public void UnlockAchievement(int achievementId)
        {
            if (achievementId < GameConfig.Achievements.MinimumId || achievementId >= GameConfig.Achievements.MaxAchievements) return;
            if (IsAchievementUnlocked(achievementId)) return; // Already unlocked

            int intIndex = achievementId / GameConfig.Achievements.BitsPerInt;
            int bitMask = GameConfig.Achievements.BitMaskSeed << (achievementId % GameConfig.Achievements.BitsPerInt);

            // Fetch the current save data as a reference to mutate it in place
            ref PlayerMetaSchema saveData = ref SaveManager.Instance.currentSaveData;
            
            // Apply the bitmask with bitwise OR
            int currentBlock = saveData.LegacyAchievements.GetInt(intIndex);
            saveData.LegacyAchievements.SetInt(intIndex, currentBlock | bitMask);
            
            // Instantly persist the struct to disk using the C-level binary dumper
            SaveManager.Instance.SaveGame();

            // Trigger UI or audio events
            if (achievementDatabase != null)
            {
                int index = achievementDatabase.achievements.FindIndex(a => a.id == achievementId);
                if (index != GameConfig.Achievements.MissingDefinitionIndex)
                {
                    var achievement = achievementDatabase.achievements[index];
                    Debug.Log(string.Format(GameConfig.Achievements.LogUnlockedSuccess, achievement.title, achievement.description));
                    OnAchievementUnlocked?.Invoke(achievement);
                }
            }
            else
            {
                Debug.Log(string.Format(GameConfig.Achievements.LogUnlockedNoVisual, achievementId));
            }
        }
        
        /// <summary>
        /// Helper to retrieve progress and build UI lists
        /// </summary>
        public List<LegacyAchievementDefinition> GetUnlockedAchievements()
        {
            List<LegacyAchievementDefinition> unlocked = new List<LegacyAchievementDefinition>();
            if (achievementDatabase == null) return unlocked;

            foreach (var achievement in achievementDatabase.achievements)
            {
                if (IsAchievementUnlocked(achievement.id))
                {
                    unlocked.Add(achievement);
                }
            }
            return unlocked;
        }
    }
}
