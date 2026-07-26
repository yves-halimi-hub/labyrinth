using System;
using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Types
{
    // Random non-close effects dropped anywhere in the FOV (e.g. Bombs, Meteors)
    public abstract class DropWeapon : Weapon
    {
        public GameObject bombVisualPrefab;
        private Vector3[] dropPoints = Array.Empty<Vector3>();
        
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

            // #32: fill the VFX pool up-front so the first drop never hitches
            // on Instantiate. No-op without a prefab or PoolManager;
            // populate-up-to-target keeps repeated grants idempotent.
            Managers.PoolManager.TryPrewarmGameObject(bombVisualPrefab, GameConfig.Pool.WeaponVfxPrewarmCount);
        }

        public override void Fire()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;

            // Get camera FOV bounds
            float fovHeight = mainCamera.orthographicSize;
            float fovWidth = fovHeight * mainCamera.aspect;
            Vector3 camPos = mainCamera.transform.position;
            if (dropCount <= GameConfig.Runtime.EmptyCollectionCount) return;
            EnsureDropCapacity(dropCount);

            for (int d = 0; d < dropCount; d++)
            {
                // Pick a random spot inside the screen
                float randX = EFYVBackend.Core.Math.FastRandom.Range(camPos.x - fovWidth, camPos.x + fovWidth);
                float randY = EFYVBackend.Core.Math.FastRandom.Range(camPos.y - fovHeight, camPos.y + fovHeight);
                Vector3 dropPoint = new Vector3(randX, randY, GameConfig.Weapons.DefaultZOffset);
                dropPoints[d] = dropPoint;

                if (bombVisualPrefab != null)
                {
                    GameObject vfx = Managers.PoolManager.Instance.SpawnGameObject(bombVisualPrefab, dropPoint, Quaternion.identity);
                    if (vfx != null)
                    {
                        Managers.PoolManager.Instance.DespawnGameObject(vfx, Managers.PoolManager.GetPoolKey(bombVisualPrefab), GameConfig.Weapons.Drop.VfxLifetime);
                    }
                }
            }

            DamageTargetsInRadiusBatch(
                dropPoints.AsSpan(0, dropCount),
                damageRadius,
                BaseDamage);
        }

        private void EnsureDropCapacity(int required)
        {
            if (dropPoints.Length >= required) return;
            int capacity = 4;
            while (capacity < required)
            {
                capacity = checked(capacity * 2);
            }
            Array.Resize(ref dropPoints, capacity);
        }
    }
}
