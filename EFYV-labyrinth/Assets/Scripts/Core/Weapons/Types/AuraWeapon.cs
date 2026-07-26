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
            // One native point-radius query; faction and health mutation remain
            // in the managed weapon layer.
            DamageTargetsInRadius(transform.position, radius, BaseDamage);
        }
    }
}
