using System.Collections;
using UnityEngine;
using EFYV.Core.Data;
using System;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class MapManager : MonoBehaviour
    {
        public static MapManager Instance { get; private set; }
        
        [Header(GameConfig.Map.HeaderBackendBlur)]
        public MapTransitionCameraEffect blurCameraEffect;

        private EFYVBackend.Core.Models.MapManagerData Data = new EFYVBackend.Core.Models.MapManagerData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        // Game over (#25): latched by PlayerController.OnPlayerDied - map
        // transitions halt for the rest of the run. A clean restart/reset path is
        // out of scope for this batch.
        private bool isGameOver;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                Data.IsSwitchingMap = GameConfig.Map.InitialIsSwitching;
                // Static event: pair keeps a double Awake idempotent; OnDestroy
                // unsubscribes (#25).
                EFYV.Core.Entities.PlayerController.OnPlayerDied -= HandlePlayerDied;
                EFYV.Core.Entities.PlayerController.OnPlayerDied += HandlePlayerDied;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            EFYV.Core.Entities.PlayerController.OnPlayerDied -= HandlePlayerDied;
            if (Instance == this) Instance = null;
        }

        private void HandlePlayerDied()
        {
            isGameOver = true;
        }

        /// <summary>
        /// General entry point to seamlessly switch maps (used by Doors, Sarcophages, etc.)
        /// Ignored while a switch is in flight or after game over (#25).
        /// </summary>
        public void SwitchMap(string targetMapId)
        {
            if (Data.IsSwitchingMap || isGameOver) return;
            StartCoroutine(MapSwitchRoutine(targetMapId));
        }

        private IEnumerator MapSwitchRoutine(string targetMapId)
        {
            Data.IsSwitchingMap = GameConfig.Map.Switching;
            
            // Step 1: Blurring Effect (Fade out to heavy blur)
            float elapsed = GameConfig.Map.InitialTransitionElapsed;
            float duration = GameConfig.Map.MapTransitionDuration;
            
            if (blurCameraEffect != null)
            {
                blurCameraEffect.enabled = GameConfig.Map.BlurEnabled;
                float halfDuration = duration * GameConfig.Map.HalfTransitionMultiplier;
                while (elapsed < halfDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / halfDuration;
                    blurCameraEffect.CurrentBlurRadius = (int)EFYVBackend.Core.Math.FastMath.FastLerp(GameConfig.Map.MinimumBlurRadius, GameConfig.Map.MapTransitionMaxBlurRadius, t);
                    yield return null;
                }
            }

            // Step 2: The actual Map Data switch happens while the screen is fully blurred
            
            var viewport = FindAnyObjectByType<MapViewportController>();
            if (viewport != null)
            {
                viewport.LoadMapData(targetMapId);
            }

            // #25: promote any scene-dropped entities that never went through a
            // frame of SpawnManager.Update, so the cleanup below sees them too.
            EFYV.Core.Entities.GameEntity.ActivatePendingSceneEntities();

            // Unload all active entities (O(1) despawns where possible)
            var allEnemies = EFYV.Core.Entities.Enemy.ActiveEnemies;
            for (int i = allEnemies.Count - 1; i >= 0; i--) PoolManager.Instance.Despawn(allEnemies[i]);

            var allProjectiles = EFYV.Core.Entities.Projectile.ActiveProjectiles;
            for (int i = allProjectiles.Count - 1; i >= 0; i--) PoolManager.Instance.Despawn(allProjectiles[i]);

            // Find Objects is slow, but perfectly acceptable during a completely blocked Map Switch loading screen
            var allProps = FindObjectsByType<EFYV.Core.Entities.Environment.PropEntity>();
            foreach (var p in allProps) PoolManager.Instance.Despawn(p);

            // #25: scene-placed (never pool-spawned) entities of every archetype are
            // deactivated and forgotten - the old map's scene content must not
            // follow the player into the new map. (The player itself opted out of
            // this tracking and is repositioned below instead.)
            EFYV.Core.Entities.GameEntity.DespawnTrackedSceneEntities();

            // A map switch is this game's scene transition: refresh the singleton
            // negative caches (#24, see SingletonSearchCache rule 2).
            EFYV.Core.Utils.SingletonSearchCache.Invalidate();

            // Reposition player
            if (EFYV.Core.Entities.PlayerController.Instance != null)
            {
                EFYV.Core.Entities.PlayerController.Instance.transform.position = Vector3.zero; 
            }

            if (DropManager.Instance != null)
            {
                DropManager.Instance.ResetTimers();
            }

            Debug.LogFormat(GameConfig.Map.LogMapManagerSwitchSuccess, targetMapId);

            // Step 3: De-blurring Effect (Fade in)
            if (blurCameraEffect != null)
            {
                elapsed = GameConfig.Map.InitialTransitionElapsed;
                float halfDuration = duration * GameConfig.Map.HalfTransitionMultiplier;
                while (elapsed < halfDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / halfDuration;
                    blurCameraEffect.CurrentBlurRadius = (int)EFYVBackend.Core.Math.FastMath.FastLerp(GameConfig.Map.MapTransitionMaxBlurRadius, GameConfig.Map.MinimumBlurRadius, t);
                    yield return null;
                }
                blurCameraEffect.enabled = GameConfig.Map.BlurDisabled;
            }

            Data.IsSwitchingMap = GameConfig.Map.NotSwitching;
        }
    }
}
