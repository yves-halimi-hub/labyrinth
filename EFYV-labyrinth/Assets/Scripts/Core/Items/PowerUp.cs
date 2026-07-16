using EFYVBackend.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Items
{
    // MIGRATION: Eliminated heap allocation. 
    // PowerUp is now a lightweight struct wrapper around a FastSchemaBlock.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = GameConfig.Runtime.SequentialStructPack)]
    public struct PowerUp
    {
        public EFYVBackend.Core.Models.PowerUpData Data;

        public int PowerUpIdHash => Data.PowerUpIdHash;
        public int Level => Data.Level;
        public PowerUpGrade Grade => (PowerUpGrade)Data.Grade;
        public int UsesRemaining => Data.UsesRemaining;

        public PowerUp(int idHash, PowerUpGrade grade)
        {
            Data = new EFYVBackend.Core.Models.PowerUpData { Block = new FastSchemaBlock() };
            Data.PowerUpIdHash = idHash;
            Data.Grade = (int)grade;
            // Seed the compact runtime block from the unified configuration.
            Data.Level = GameConfig.Weapons.Inventory.InitialLevel;
            Data.UsesRemaining = GameConfig.Weapons.Inventory.PowerUpUses; 
        }

        public void UpgradeLevel()
        {
            if (Level < GameConfig.Weapons.Inventory.MaxLevel)
            {
                Data.Level = Level + GameConfig.Weapons.Inventory.LevelIncrement;
            }
        }

        public void ConsumeUse()
        {
            int currentUses = UsesRemaining - GameConfig.Weapons.Inventory.UseCost;
            Data.UsesRemaining = currentUses;
            
            if (currentUses <= GameConfig.Weapons.Inventory.ExhaustedUses)
            {
                Degrade();
            }
        }

        private void Degrade()
        {
            if (Grade > PowerUpGrade.Normal)
            {
                Data.Grade = (int)Grade - GameConfig.Weapons.Inventory.GradeDecrement;
            }
            else
            {
                // Reset level if it's already normal
                Data.Level = GameConfig.Weapons.Inventory.InitialLevel;
            }
            
            // Reset uses for the degraded tier
            Data.UsesRemaining = GameConfig.Weapons.Inventory.PowerUpUses;
        }
    }
}
