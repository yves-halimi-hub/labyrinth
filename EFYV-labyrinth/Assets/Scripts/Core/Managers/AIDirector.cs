using UnityEngine;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class AIDirector : EFYV.Core.Utils.Singleton<AIDirector>
    {

        public SpawnManager spawnManager;

        // Game over (#25): latched by PlayerController.OnPlayerDied. From then on
        // every multiplier reports the neutral 1x - difficulty scaling stops with
        // the run. A clean restart/reset path is out of scope for this batch.
        private bool isGameOver;

        protected override void Awake()
        {
            base.Awake();
            if (IsSingletonInstance)
            {
                EFYV.Core.Entities.PlayerController.OnPlayerDied -= HandlePlayerDied;
                EFYV.Core.Entities.PlayerController.OnPlayerDied += HandlePlayerDied;
            }
        }

        protected override void OnDestroy()
        {
            EFYV.Core.Entities.PlayerController.OnPlayerDied -= HandlePlayerDied;
            base.OnDestroy();
        }

        private void HandlePlayerDied()
        {
            isGameOver = true;
        }

        // Called by SpawnManager to aggressively scale up spawn numbers
        public float GetIntensityMultiplier()
        {
            if (isGameOver || spawnManager == null) return GameConfig.AI.DefaultMultiplier;

            // Intensity jumps by 1.5x every minute survived
            float minutesSurvived = spawnManager.GameTimer / GameConfig.AI.IntensityMinuteDivider;
            return GameConfig.AI.IntensityBaseMultiplier + (minutesSurvived * GameConfig.AI.IntensityScalingFactor);
        }

        // Global stat modifiers injected into enemies upon spawning
        public float GetEnemyHealthMultiplier()
        {
            if (isGameOver || spawnManager == null) return GameConfig.AI.DefaultMultiplier;
            return GameConfig.AI.HealthBaseMultiplier + (spawnManager.GameTimer / GameConfig.AI.HealthMinuteDivider); // Health doubles every 2 minutes
        }

        public float GetEnemySpeedMultiplier()
        {
            if (isGameOver || spawnManager == null) return GameConfig.AI.DefaultMultiplier;
            return GameConfig.AI.SpeedBaseMultiplier + (spawnManager.GameTimer / GameConfig.AI.SpeedMinuteDivider); // Speed slowly increases over 5 mins
        }
    }
}
