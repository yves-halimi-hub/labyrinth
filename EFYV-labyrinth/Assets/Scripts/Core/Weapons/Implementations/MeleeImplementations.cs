using UnityEngine;
using EFYV.Core.Weapons.Types;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Implementations
{
    public class AluminumBat : MeleeWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Melee.Bat.Damage;
            attackRange = GameConfig.Weapons.Melee.Bat.AttackRange;
            knockbackForce = GameConfig.Weapons.Melee.Bat.Knockback;
            CooldownTime = GameConfig.Weapons.Melee.Bat.Cooldown;
        }
    }

    public class Longsword : MeleeWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Melee.Sword.Damage;
            attackRange = GameConfig.Weapons.Melee.Sword.AttackRange;
            knockbackForce = GameConfig.Weapons.Melee.Sword.Knockback;
            CooldownTime = GameConfig.Weapons.Melee.Sword.Cooldown;
        }
    }
}
