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

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                Data.IsSwitchingMap = GameConfig.Map.InitialIsSwitching;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// General entry point to seamlessly switch maps (used by Doors, Sarcophages, etc.)
        /// </summary>
        public void SwitchMap(string targetMapId)
        {
            if (Data.IsSwitchingMap) return;
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
            
            var viewport = FindObjectOfType<MapViewportController>();
            if (viewport != null)
            {
                viewport.LoadMapData(targetMapId);
            }

            // Unload all active entities (O(1) despawns where possible)
            var allEnemies = EFYV.Core.Entities.Enemy.ActiveEnemies;
            for (int i = allEnemies.Count - 1; i >= 0; i--) PoolManager.Instance.Despawn(allEnemies[i]);
            
            var allProjectiles = EFYV.Core.Entities.Projectile.ActiveProjectiles;
            for (int i = allProjectiles.Count - 1; i >= 0; i--) PoolManager.Instance.Despawn(allProjectiles[i]);

            // Find Objects is slow, but perfectly acceptable during a completely blocked Map Switch loading screen
            var allProps = FindObjectsOfType<EFYV.Core.Entities.Environment.PropEntity>();
            foreach (var p in allProps) PoolManager.Instance.Despawn(p);
            
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
