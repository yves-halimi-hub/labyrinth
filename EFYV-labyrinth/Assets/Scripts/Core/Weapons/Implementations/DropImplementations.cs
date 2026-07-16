using UnityEngine;
using EFYV.Core.Weapons.Types;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Implementations
{
    public class BombDrop : DropWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Drop.Bomb.Damage;
            damageRadius = GameConfig.Weapons.Drop.Bomb.DamageRadius;
            dropCount = GameConfig.Weapons.Drop.Bomb.Count;
            CooldownTime = GameConfig.Weapons.Drop.Bomb.Cooldown;
        }
    }

    public class MeteorDrop : DropWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Drop.Meteor.Damage;
            damageRadius = GameConfig.Weapons.Drop.Meteor.DamageRadius;
            dropCount = GameConfig.Weapons.Drop.Meteor.Count;
            CooldownTime = GameConfig.Weapons.Drop.Meteor.Cooldown;
        }
    }
}
