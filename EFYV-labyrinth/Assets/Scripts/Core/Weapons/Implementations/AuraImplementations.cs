using UnityEngine;
using EFYV.Core.Weapons.Types;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Implementations
{
    public class GarlicAura : AuraWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Aura.Garlic.Damage;
            radius = GameConfig.Weapons.Aura.Garlic.Radius;
            CooldownTime = GameConfig.Weapons.Aura.Garlic.Cooldown;
        }
    }

    public class SpinningSwordsAura : AuraWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Aura.Swords.Damage;
            radius = GameConfig.Weapons.Aura.Swords.Radius;
            CooldownTime = GameConfig.Weapons.Aura.Swords.Cooldown;
        }
    }
}
