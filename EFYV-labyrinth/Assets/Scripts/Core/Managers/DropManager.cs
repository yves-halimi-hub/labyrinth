using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment.Implementations;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class DropManager : Singleton<DropManager>
    {
        private EFYVBackend.Core.Models.DropManagerData Data = new EFYVBackend.Core.Models.DropManagerData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        // Simple mock references for prefabs - would be populated in Editor
        public CoinProp coinPrefab;
        public ChestProp chestPrefab;
        public XPGem xpGemPrefab;

        // #24: XP gems are wired into the drop table. Regular enemies roll this
        // base chance (scaled by the survival-time multiplier); bosses and
        // mini-bosses always drop one. The gem's xpValue comes from the prefab.
        // Values live in the shared config; these aliases keep the public API.
        public const float BaseXpGemChance = GameConfig.Drops.BaseXpGemChance;

        // #32: pool prewarm targets for the drop prefabs, applied in Awake.
        public const int CoinPoolPrewarmCount = GameConfig.Pool.CoinPrewarmCount;
        public const int ChestPoolPrewarmCount = GameConfig.Pool.ChestPrewarmCount;
        public const int XpGemPoolPrewarmCount = GameConfig.Pool.XpGemPrewarmCount;

        protected override void Awake()
        {
            base.Awake();
            Data.DynamicDropMultiplier = GameConfig.System.DefaultDynamicDropMultiplier;

            // #32: fill the drop pools up-front so loot bursts never hitch on
            // mid-run Instantiate calls. Null prefabs are skipped by Prewarm.
            if (IsSingletonInstance && PoolManager.TryGetInstance(out PoolManager poolManager))
            {
                poolManager.Prewarm(coinPrefab, CoinPoolPrewarmCount);
                poolManager.Prewarm(chestPrefab, ChestPoolPrewarmCount);
                poolManager.Prewarm(xpGemPrefab, XpGemPoolPrewarmCount);
            }
        }

        public void Tick(float deltaTime, float survivalTimeInSeconds)
        {
            // Increase drop chances slightly every minute survived
            Data.DynamicDropMultiplier = GameConfig.System.DefaultDynamicDropMultiplier + (survivalTimeInSeconds / GameConfig.System.SurvivalTimeMinuteSeconds) * GameConfig.System.DropChanceIncreasePerMinute; 
        }

        public void ResetTimers()
        {
            Data.DynamicDropMultiplier = GameConfig.System.DefaultDynamicDropMultiplier;
        }

        // DropLoot is invoked by Enemy.Die, so it doubles as the central
        // kill-notification seam (#34): the achievement kill counters advance here
        // without touching the enemy class or consuming any PRNG state.
        public void DropLoot(Enemy enemy)
        {
            if (AchievementManager.TryGetInstance(out AchievementManager achievements))
            {
                achievements.NotifyEnemyKilled();
            }

            // PERFORMANCE: Generate random values via the optimized backend
            float rand = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Runtime.UnitIntervalMin, GameConfig.Runtime.UnitIntervalMax);

            bool isBoss = enemy is BossEnemy;
            bool isMiniBoss = enemy is MiniBoss;

            // Roll for Chest
            float chestChance = isBoss ? GameConfig.Drops.GuaranteedDropChance : (isMiniBoss ? GameConfig.Drops.GuaranteedDropChance : GameConfig.Drops.BaseChestChance * Data.DynamicDropMultiplier);

            if (rand <= chestChance && chestPrefab != null)
            {
                SpawnChest(enemy, isBoss, isMiniBoss);
            }

            // Roll for Coin
            // Monsters drop coins frequently, Bosses always drop them
            float coinChance = isBoss ? GameConfig.Drops.GuaranteedDropChance : GameConfig.Drops.BaseCoinChance * Data.DynamicDropMultiplier;
            rand = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Runtime.UnitIntervalMin, GameConfig.Runtime.UnitIntervalMax);

            if (rand <= coinChance && coinPrefab != null)
            {
                SpawnCoin(enemy, isBoss, isMiniBoss);
            }

            // Roll for XP Gem (#24). The draw is always consumed (mirrors the coin
            // path) so the PRNG stream is independent of prefab presence.
            float gemChance = (isBoss || isMiniBoss)
                ? GameConfig.Drops.GuaranteedDropChance
                : BaseXpGemChance * Data.DynamicDropMultiplier;
            rand = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Runtime.UnitIntervalMin, GameConfig.Runtime.UnitIntervalMax);

            if (rand <= gemChance && xpGemPrefab != null)
            {
                SpawnXpGem(enemy);
            }
        }

        private void SpawnXpGem(Enemy enemy)
        {
            PoolManager.Instance.Spawn(
                xpGemPrefab,
                enemy.entityTransform.position,
                Quaternion.identity);
        }

        private void SpawnChest(Enemy enemy, bool isBoss, bool isMiniBoss)
        {
            // A boss can drop 1 to 3 chests
            int chestCount = isBoss ? EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Drops.BossMinChestCount, GameConfig.Drops.BossMaxChestCountExclusive) : GameConfig.Drops.StandardChestCount;
            
            for (int i = 0; i < chestCount; i++)
            {
                int grade = GameConfig.Drops.EnemyChestGrade;
                
                if (isBoss) 
                {
                    grade = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Drops.BossMinChestGrade, GameConfig.Drops.BossMaxChestGrade + GameConfig.Runtime.ExclusiveUpperBoundOffset);
                }
                else if (isMiniBoss)
                {
                    grade = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Drops.MiniBossMinChestGrade, GameConfig.Drops.MiniBossMaxChestGrade + GameConfig.Runtime.ExclusiveUpperBoundOffset);
                }

                ChestProp chest = PoolManager.Instance.Spawn(
                    chestPrefab,
                    enemy.entityTransform.position,
                    Quaternion.identity) as ChestProp;
                if (chest != null) chest.InitializeGrade(grade);
            }
        }

        private void SpawnCoin(Enemy enemy, bool isBoss, bool isMiniBoss)
        {
            int grade = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Drops.MinCoinGrade, GameConfig.Drops.EnemyMaxCoinGrade + GameConfig.Runtime.ExclusiveUpperBoundOffset);
            
            if (isBoss) 
            {
                grade = GameConfig.Drops.BossCoinGrade;
            }
            else if (isMiniBoss)
            {
                grade = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Drops.MiniBossMinCoinGrade, GameConfig.Drops.MiniBossMaxCoinGrade + GameConfig.Runtime.ExclusiveUpperBoundOffset);
            }

            CoinProp coin = PoolManager.Instance.Spawn(
                coinPrefab,
                enemy.entityTransform.position,
                Quaternion.identity) as CoinProp;
            if (coin != null) coin.InitializeGrade(grade);
        }
    }
}
