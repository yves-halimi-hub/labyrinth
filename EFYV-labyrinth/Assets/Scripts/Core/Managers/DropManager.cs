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

        protected override void Awake()
        {
            base.Awake();
            Data.DynamicDropMultiplier = GameConfig.System.DefaultDynamicDropMultiplier;
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

        public void DropLoot(Enemy enemy)
        {
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
