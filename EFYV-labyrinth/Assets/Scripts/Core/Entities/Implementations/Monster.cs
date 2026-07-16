using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public class Monster : Enemy
    {
        protected override int MaxWeapons => GameConfig.Weapons.Inventory.MonsterMaxWeapons;
    }
}
