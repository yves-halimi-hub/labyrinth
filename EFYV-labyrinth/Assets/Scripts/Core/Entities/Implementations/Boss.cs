using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public class Boss : BossEnemy
    {
        protected override int MaxWeapons => GameConfig.Weapons.Inventory.BossMaxWeapons;
    }
}
