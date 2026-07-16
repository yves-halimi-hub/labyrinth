using UnityEngine;
using EFYV.Core.Entities;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Types
{
    // Random area effect splashing out of the character (e.g. Electricity bursts)
    public abstract class SplashWeapon : Weapon
    {
        public GameObject splashVisualPrefab;
        
        public float splashRadius
        {
            get => Data.SplashRadius;
            set => Data.SplashRadius = value;
        }

        public float damageRadius
        {
            get => Data.DamageRadius;
            set => Data.DamageRadius = value;
        }

        public int splashCount
        {
            get => Data.SplashCount;
            set => Data.SplashCount = value;
        }

        protected override void Awake()
        {
            base.Awake();
            splashRadius = GameConfig.Weapons.Splash.DefaultSplashRadius;
            damageRadius = GameConfig.Weapons.Splash.DefaultDamageRadius;
            splashCount = GameConfig.Weapons.Splash.DefaultCount;
        }

        public override void Fire()
        {
            Vector3 center = transform.position;
            float sqrRadius = damageRadius * damageRadius;

            for (int s = 0; s < splashCount; s++)
            {
                // Pick a random point near the player
                Vector3 splashPoint = center + VectorExtensions.GetRandomOffset(splashRadius, GameConfig.Weapons.DefaultZOffset);

                if (splashVisualPrefab != null)
                {
                    GameObject vfx = Managers.PoolManager.Instance.SpawnGameObject(splashVisualPrefab, splashPoint, Quaternion.identity);
                    if (vfx != null)
                    {
                        Managers.PoolManager.Instance.DespawnGameObject(vfx, splashVisualPrefab.GetInstanceID(), GameConfig.Weapons.Splash.VfxLifetime);
                    }
                }
                
                Enemy.ApplyDamageInRadius(splashPoint, sqrRadius, BaseDamage);
            }
        }
    }
}
