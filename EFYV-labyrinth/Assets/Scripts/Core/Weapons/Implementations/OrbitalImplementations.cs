using UnityEngine;
using EFYV.Core.Weapons.Types;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Implementations
{
    public class SpinningAxe : OrbitalWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Orbital.Axe.Damage;
            orbitRadius = GameConfig.Weapons.Orbital.Axe.OrbitRadius;
            rotationSpeed = GameConfig.Weapons.Orbital.Axe.RotationSpeed;
            projectileCount = GameConfig.Weapons.Orbital.Axe.Count;
            damageRadius = GameConfig.Weapons.Orbital.Axe.DamageRadius;
        }
    }

    public class Beyblade : OrbitalWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Orbital.Beyblade.Damage;
            orbitRadius = GameConfig.Weapons.Orbital.Beyblade.OrbitRadius;
            rotationSpeed = GameConfig.Weapons.Orbital.Beyblade.RotationSpeed;
            projectileCount = GameConfig.Weapons.Orbital.Beyblade.Count;
            damageRadius = GameConfig.Weapons.Orbital.Beyblade.DamageRadius;
        }
    }
}
