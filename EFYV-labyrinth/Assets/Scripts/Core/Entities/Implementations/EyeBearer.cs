using UnityEngine;
using EFYV.Core.Managers;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Implementations
{
    public class EyeBearer : Enemy
    {
        [Tooltip(GameConfig.EyeBearer.TooltipEvilEyePrefab)]
        public Enemy evilEyePrefab;

        public override void Die()
        {
            // First spawn the two Evil Eyes
            if (evilEyePrefab != null)
            {
                for (int i = 0; i < GameConfig.EyeBearer.SpawnCount; i++)
                {
                    // Delegated offset calculation to backend VectorExtensions
                    Vector3 spawnPos = entityTransform.position + EFYV.Core.Utils.VectorExtensions.GetRandomOffset(GameConfig.EyeBearer.SpawnOffsetRadius);

                    // Delegate to the PoolManager for zero-allocation instantiation
                    PoolManager.Instance.Spawn(evilEyePrefab, spawnPos, Quaternion.identity);
                }
            }
            
            // Call base.Die() to grant EXP, drop loot (coins/chests), and despawn this object
            base.Die();
        }
    }
}
