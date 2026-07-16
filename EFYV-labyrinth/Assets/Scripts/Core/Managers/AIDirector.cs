using UnityEngine;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class AIDirector : EFYV.Core.Utils.Singleton<AIDirector>
    {

        public SpawnManager spawnManager;

        // Called by SpawnManager to aggressively scale up spawn numbers
        public float GetIntensityMultiplier()
        {
            if (spawnManager == null) return GameConfig.AI.DefaultMultiplier;

            // Intensity jumps by 1.5x every minute survived
            float minutesSurvived = spawnManager.GameTimer / GameConfig.AI.IntensityMinuteDivider;
            return GameConfig.AI.IntensityBaseMultiplier + (minutesSurvived * GameConfig.AI.IntensityScalingFactor); 
        }

        // Global stat modifiers injected into enemies upon spawning
        public float GetEnemyHealthMultiplier()
        {
            if (spawnManager == null) return GameConfig.AI.DefaultMultiplier;
            return GameConfig.AI.HealthBaseMultiplier + (spawnManager.GameTimer / GameConfig.AI.HealthMinuteDivider); // Health doubles every 2 minutes
        }

        public float GetEnemySpeedMultiplier()
        {
            if (spawnManager == null) return GameConfig.AI.DefaultMultiplier;
            return GameConfig.AI.SpeedBaseMultiplier + (spawnManager.GameTimer / GameConfig.AI.SpeedMinuteDivider); // Speed slowly increases over 5 mins
        }
    }
}
