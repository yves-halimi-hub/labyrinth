using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public class MiniBoss : Enemy
    {
        protected override int MaxWeapons => GameConfig.Weapons.Inventory.MiniBossMaxWeapons;
    }
}
