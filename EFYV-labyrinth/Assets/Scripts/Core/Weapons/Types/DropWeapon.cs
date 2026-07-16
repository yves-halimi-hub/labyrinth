using UnityEngine;
using EFYV.Core.Entities;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Types
{
    // Random non-close effects dropped anywhere in the FOV (e.g. Bombs, Meteors)
    public abstract class DropWeapon : Weapon
    {
        public GameObject bombVisualPrefab;
        
        public float damageRadius
        {
            get => Data.DamageRadius;
            set => Data.DamageRadius = value;
        }

        public int dropCount
        {
            get => Data.DropCount;
            set => Data.DropCount = value;
        }

        protected override void Awake()
        {
            base.Awake();
            damageRadius = GameConfig.Weapons.Drop.DefaultDamageRadius;
            dropCount = GameConfig.Weapons.Drop.DefaultCount;
        }

        public override void Fire()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;

            // Get camera FOV bounds
            float fovHeight = mainCamera.orthographicSize;
            float fovWidth = fovHeight * mainCamera.aspect;
            Vector3 camPos = mainCamera.transform.position;
            float sqrDamageRadius = damageRadius * damageRadius;

            for (int d = 0; d < dropCount; d++)
            {
                // Pick a random spot inside the screen
                float randX = EFYVBackend.Core.Math.FastRandom.Range(camPos.x - fovWidth, camPos.x + fovWidth);
                float randY = EFYVBackend.Core.Math.FastRandom.Range(camPos.y - fovHeight, camPos.y + fovHeight);
                Vector3 dropPoint = new Vector3(randX, randY, GameConfig.Weapons.DefaultZOffset);

                if (bombVisualPrefab != null)
                {
                    GameObject vfx = Managers.PoolManager.Instance.SpawnGameObject(bombVisualPrefab, dropPoint, Quaternion.identity);
                    if (vfx != null)
                    {
                        Managers.PoolManager.Instance.DespawnGameObject(vfx, bombVisualPrefab.GetInstanceID(), GameConfig.Weapons.Drop.VfxLifetime);
                    }
                }

                Enemy.ApplyDamageInRadius(dropPoint, sqrDamageRadius, BaseDamage);
            }
        }
    }
}
