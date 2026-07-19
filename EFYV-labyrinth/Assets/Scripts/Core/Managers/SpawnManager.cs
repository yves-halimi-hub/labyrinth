using UnityEngine;
using EFYV.Core.Entities;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class SpawnManager : MonoBehaviour
    {
        private EFYVBackend.Core.Models.SpawnManagerData Data = new EFYVBackend.Core.Models.SpawnManagerData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        [Header(GameConfig.Spawner.HeaderReferences)]
        public Transform playerTransform;
        
        [Tooltip(GameConfig.Spawner.TooltipEnemyPrefabs)]
        public Enemy[] enemyPrefabs; 

        [Header(GameConfig.Spawner.HeaderSettings)]
        [Tooltip(GameConfig.Spawner.TooltipSpawnRadius)]
        [SerializeField] private float serializedSpawnRadius = GameConfig.Spawner.DefaultSpawnRadius;
        public float spawnRadius
        {
            get => Data.SpawnRadius;
            set
            {
                serializedSpawnRadius = SanitizeNonNegative(value, GameConfig.Spawner.DefaultSpawnRadius);
                Data.SpawnRadius = serializedSpawnRadius;
            }
        }
        
        [Tooltip(GameConfig.Spawner.TooltipBaseSpawnRate)]
        [SerializeField] private float serializedBaseSpawnRate = GameConfig.Spawner.DefaultBaseSpawnRate;
        public float baseSpawnRate
        {
            get => Data.BaseSpawnRate;
            set
            {
                serializedBaseSpawnRate = SanitizeNonNegative(value, GameConfig.Spawner.DefaultBaseSpawnRate);
                Data.BaseSpawnRate = serializedBaseSpawnRate;
            }
        }
        
        [Tooltip(GameConfig.Spawner.TooltipDifficultyMultiplier)]
        [SerializeField] private float serializedDifficultyMultiplier = GameConfig.Spawner.DefaultDifficultyMultiplier;
        public float difficultyMultiplier
        {
            get => Data.DifficultyMultiplier;
            set
            {
                serializedDifficultyMultiplier = SanitizeNonNegative(value, GameConfig.Spawner.DefaultDifficultyMultiplier);
                Data.DifficultyMultiplier = serializedDifficultyMultiplier;
            }
        }

        // Tracks the total survival time
        public float GameTimer { get => Data.GameTimer; private set => Data.GameTimer = value; }

        // Tracks fractional spawns (e.g. if we need to spawn 2.5 enemies this frame, we spawn 2 and save 0.5)
        private float spawnAccumulator { get => Data.SpawnAccumulator; set => Data.SpawnAccumulator = value; }

        // Game over (#25): latched by PlayerController.OnPlayerDied. Spawning, the
        // survival timer, and difficulty coupling freeze; the central entity ticks
        // keep running so the world stays alive around the corpse. A clean
        // restart/reset path is deliberately out of scope for this batch.
        private bool isGameOver;

        // #32: enemy pool prewarm target per prefab; the value lives in the
        // shared config, this alias keeps the public API.
        public const int EnemyPoolPrewarmCount = GameConfig.Pool.EnemyPrewarmCount;

        private void Awake()
        {
            SyncSerializedSettings();
            Data.GameTimer = GameConfig.Spawner.InitialGameTimer;
            spawnAccumulator = GameConfig.Spawner.InitialSpawnAccumulator;

            // Static event: the pair below keeps a double Awake idempotent, and
            // OnDestroy unsubscribes (#25).
            PlayerController.OnPlayerDied -= HandlePlayerDied;
            PlayerController.OnPlayerDied += HandlePlayerDied;
        }

        private void OnDestroy()
        {
            PlayerController.OnPlayerDied -= HandlePlayerDied;
        }

        private void HandlePlayerDied()
        {
            isGameOver = true;
        }

        private void OnValidate()
        {
            SyncSerializedSettings();
        }

        private void Reset()
        {
            serializedSpawnRadius = GameConfig.Spawner.DefaultSpawnRadius;
            serializedBaseSpawnRate = GameConfig.Spawner.DefaultBaseSpawnRate;
            serializedDifficultyMultiplier = GameConfig.Spawner.DefaultDifficultyMultiplier;
            SyncSerializedSettings();
        }

        private void SyncSerializedSettings()
        {
            serializedSpawnRadius = SanitizeNonNegative(serializedSpawnRadius, GameConfig.Spawner.DefaultSpawnRadius);
            serializedBaseSpawnRate = SanitizeNonNegative(serializedBaseSpawnRate, GameConfig.Spawner.DefaultBaseSpawnRate);
            serializedDifficultyMultiplier = SanitizeNonNegative(serializedDifficultyMultiplier, GameConfig.Spawner.DefaultDifficultyMultiplier);
            Data.SpawnRadius = serializedSpawnRadius;
            Data.BaseSpawnRate = serializedBaseSpawnRate;
            Data.DifficultyMultiplier = serializedDifficultyMultiplier;
        }

        private static float SanitizeNonNegative(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < GameConfig.Runtime.UnitIntervalMin
                ? fallback
                : value;
        }

        private void Start()
        {
            // Auto-find the player if not linked in the inspector
            if (playerTransform == null)
            {
                var player = FindAnyObjectByType<PlayerController>();
                if (player != null) playerTransform = player.entityTransform;
            }

            // #32: fill the enemy pools up-front so early gameplay never hitches on
            // mid-run Instantiate bursts.
            if (enemyPrefabs != null && PoolManager.TryGetInstance(out PoolManager poolManager))
            {
                for (int i = 0; i < enemyPrefabs.Length; i++)
                {
                    poolManager.Prewarm(enemyPrefabs[i], EnemyPoolPrewarmCount);
                }
            }
        }

        private void Update()
        {
            if (playerTransform == null) return;
            float deltaTime = Time.deltaTime;

            // #25: promote scene-dropped entities into the centralized loops so
            // they Tick, are targetable, and are cleaned on map switch.
            GameEntity.ActivatePendingSceneEntities();

            if (!isGameOver)
            {
                // 1. Advance the central game timer
                GameTimer += deltaTime;

                // Survival-time achievement triggers (#34): O(1) threshold checks,
                // no per-frame string operations.
                if (AchievementManager.TryGetInstance(out AchievementManager achievements))
                {
                    achievements.NotifySurvivalTime(GameTimer);
                }

                // 2. Mathematical Curve for Spawning
                // Example: If base is 2, and multiplier is 0.1. At 60 seconds: 2 + (60 * 0.1) = 8 enemies per second.
                float currentSpawnRate = baseSpawnRate + (GameTimer * difficultyMultiplier);

                // Apply AI Director intensity to the spawn rate
                if (AIDirector.TryGetInstance(out AIDirector director))
                {
                    currentSpawnRate *= director.GetIntensityMultiplier();
                }

                // 3. Accumulate spawns for this frame
                float spawnIncrement = currentSpawnRate * deltaTime;
                if (float.IsNaN(spawnIncrement) || spawnIncrement <= GameConfig.Runtime.UnitIntervalMin)
                    spawnIncrement = GameConfig.Runtime.UnitIntervalMin;
                else if (float.IsInfinity(spawnIncrement))
                    spawnIncrement = GameConfig.Spawner.MaxAccumulatedSpawns;
                spawnAccumulator = Mathf.Min(
                    spawnAccumulator + spawnIncrement,
                    GameConfig.Spawner.MaxAccumulatedSpawns);
                // Update the DropManager's time-based probabilities
                if (DropManager.TryGetInstance(out DropManager dropManager))
                {
                    dropManager.Tick(deltaTime, GameTimer);
                }

                // 4. Resolve spawns. If our accumulator goes above threshold, we spawn an enemy.
                // (A while loop allows multiple spawns in a single frame during late-game chaos)
                int spawnsThisFrame = GameConfig.Runtime.EmptyCollectionCount;
                while (spawnAccumulator >= GameConfig.Spawner.AccumulatorThreshold &&
                    spawnsThisFrame < GameConfig.Spawner.MaxSpawnsPerFrame)
                {
                    SpawnRandomEnemy();
                    spawnAccumulator -= GameConfig.Spawner.AccumulatorThreshold;
                    spawnsThisFrame++;
                }
            }

            // PERFORMANCE: UPDATE MANAGER PATTERN
            // Instead of Unity's Native C++ engine invoking the magic Update() method 10,000 times,
            // we iterate our packed C# list in a single bounded loop from a central manager.
            // This entirely bypasses the Native-to-Managed bridge overhead per entity.
            var enemies = Enemy.ActiveEnemies;
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                enemies[i].Tick(deltaTime);
            }

            // PERFORMANCE: UPDATE MANAGER PATTERN for Environment Props
            // Handles animations for thousands of coins, trees, and gems without Unity Update()
            var props = EFYV.Core.Entities.Environment.PropEntity.ActiveAnimatedProps;
            int propCount = props.Count;
            for (int i = 0; i < propCount; i++)
            {
                props[i].TickAnimation(deltaTime);
            }
        }

        private void SpawnRandomEnemy()
        {
            if (enemyPrefabs == null || enemyPrefabs.Length == GameConfig.Runtime.EmptyCollectionCount) return;

            // Pick a random enemy from our array
            Enemy prefabToSpawn = enemyPrefabs[EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Spawner.RandomMinIndex, enemyPrefabs.Length)];

            // Generate a random angle in radians (-PI to PI) for our Taylor series
            float randomRad = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Spawner.MinRadians, EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.TwoPI) - EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.PI;
            
            EFYVBackend.Core.Math.FastMath.FastSinCosTaylor(randomRad, out float sin, out float cos);
            
            Vector2 offset = new Vector2(cos, sin) * spawnRadius;
            Vector3 spawnPosition = playerTransform.position + (Vector3)offset;

            if (prefabToSpawn != null && PoolManager.TryGetInstance(out PoolManager poolManager))
            {
                poolManager.Spawn(prefabToSpawn, spawnPosition, Quaternion.identity);
            }
        }
    }
}
