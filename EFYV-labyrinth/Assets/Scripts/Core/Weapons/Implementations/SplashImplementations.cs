using UnityEngine;
using EFYV.Core.Weapons.Types;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Implementations
{
    public class LightningSplash : SplashWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Splash.Lightning.Damage;
            splashRadius = GameConfig.Weapons.Splash.Lightning.SplashRadius;
            damageRadius = GameConfig.Weapons.Splash.Lightning.DamageRadius;
            splashCount = GameConfig.Weapons.Splash.Lightning.Count;
            CooldownTime = GameConfig.Weapons.Splash.Lightning.Cooldown;
        }
    }

    public class HolyWaterSplash : SplashWeapon
    {
        protected override void Awake()
        {
            base.Awake();
            BaseDamage = GameConfig.Weapons.Splash.HolyWater.Damage;
            splashRadius = GameConfig.Weapons.Splash.HolyWater.SplashRadius;
            damageRadius = GameConfig.Weapons.Splash.HolyWater.DamageRadius;
            splashCount = GameConfig.Weapons.Splash.HolyWater.Count;
            CooldownTime = GameConfig.Weapons.Splash.HolyWater.Cooldown;
        }
    }
}
