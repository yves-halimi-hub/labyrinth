using UnityEngine;
using EFYV.Core.Managers;
using System.Collections.Generic;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Environment.Implementations
{
    public class SarcophageProp : InteractableProp
    {
        [Tooltip(GameConfig.Map.TooltipSarcophageMapIds)]
        public List<string> PossibleRandomMapIds = new List<string>();

        [Tooltip(GameConfig.Map.TooltipSarcophageTrapPrefab)]
        public EFYV.Core.Entities.Enemy MummyPrefab;

        // Triggered when the player interacts with this object
        public override void OnInteract(PlayerController player)
        {
            float roll = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Runtime.UnitIntervalMin, GameConfig.Runtime.UnitIntervalMax);

            if (roll <= GameConfig.Map.SarcophageTeleportChance && PossibleRandomMapIds.Count > GameConfig.Runtime.EmptyCollectionCount)
            {
                // Select a random map index
                int randomIndex = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Runtime.FirstIndex, PossibleRandomMapIds.Count);
                string targetMap = PossibleRandomMapIds[randomIndex];
                Debug.LogFormat(GameConfig.Map.LogSarcophageTeleport, targetMap);
                MapManager.Instance.SwitchMap(targetMap);
            }
            else if (roll <= GameConfig.Map.SarcophageSpawnEnemyChance)
            {
                TriggerEnemyAmbush();
            }
            else if (roll <= GameConfig.Map.SarcophageTrapChance)
            {
                TriggerTrap();
            }
            else
            {
                TriggerCurse();
            }
            
            // Assuming Sarcophage can only be interacted with once
            ReleaseToPool();
        }

        private void TriggerEnemyAmbush()
        {
            if (MummyPrefab == null || !PoolManager.TryGetInstance(out PoolManager poolManager)) return;
            Debug.Log(GameConfig.Map.LogSarcophageAmbush);
            
            int count = GameConfig.Map.SarcophageAmbushCount;
            float radius = GameConfig.Map.SarcophageAmbushRadius;
            
            // Spawn mummies in a circle around the sarcophage
            for (int i = 0; i < count; i++)
            {
                float rad = EFYVBackend.Core.Math.FastMath.GetCircleDistributionAngleRad(i, count);
                
                EFYVBackend.Core.Math.FastMath.FastSinCosTaylor(rad, out float sin, out float cos);
                float x = cos * radius;
                float y = sin * radius;
                
                Vector3 spawnPos = transform.position + new Vector3(x, y, GameConfig.EnvironmentData.PlanarZOffset);
                poolManager.Spawn(MummyPrefab, spawnPos, Quaternion.identity);
            }
        }

        private void TriggerTrap()
        {
            Debug.Log(GameConfig.Map.LogSarcophageTrap);
            // Instant damage spike trap
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.TakeDamage(GameConfig.Map.SarcophageTrapDamage);
            }
        }

        private void TriggerCurse()
        {
            Debug.Log(GameConfig.Map.LogSarcophageCurse);
            // Steal session coins
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.SpendSessionCoins(GameConfig.Map.SarcophageCurseCoinLoss);
            }
        }
    }
}
