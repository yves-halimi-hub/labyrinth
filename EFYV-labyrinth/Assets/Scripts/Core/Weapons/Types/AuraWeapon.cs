using UnityEngine;
using EFYV.Core.Entities;
using EFYV.Core.Managers;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Types
{
    // Constant close-area effect (e.g. Garlic, Spinning Swords)
    public abstract class AuraWeapon : Weapon
    {
        public float radius
        {
            get => Data.AuraRadius;
            set => Data.AuraRadius = value;
        }

        protected override void Awake()
        {
            base.Awake();
            radius = GameConfig.Weapons.Aura.DefaultRadius;
        }

        public override void Fire()
        {
            // Temporary simple implementation: just damage everything close
            // A fully optimized version uses grid.GetEntitiesInRadius()
            float sqrRadius = radius * radius;
            Enemy.ApplyDamageInRadius(transform.position, sqrRadius, BaseDamage);
        }
    }
}
