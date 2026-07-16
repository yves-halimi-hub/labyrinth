using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Environment.Implementations
{
    // A blocking interactable prop that gives the player a weapon upgrade or massive gold when touched
    public class ChestProp : InteractableProp
    {
        public Sprite[] gradeSprites;
        
        public int Grade { get => Data.Block.GetInt((int)EFYVBackend.Core.Data.PropSchema.Grade); private set => Data.Block.SetInt((int)EFYVBackend.Core.Data.PropSchema.Grade, value); }

        public void InitializeGrade(int grade)
        {
            Grade = EFYVBackend.Core.Math.FastMath.FastClamp(grade, GameConfig.Drops.MinChestGrade, GameConfig.Drops.MaxChestGrade);
            
            if (spriteRenderer != null && gradeSprites != null && gradeSprites.Length >= Grade)
            {
                spriteRenderer.sprite = gradeSprites[Grade - GameConfig.EnvironmentData.GradeToSpriteIndexOffset];
            }
        }

        public override void Initialize()
        {
            base.Initialize();
            Grade = GameConfig.Drops.EnemyChestGrade;
            // A chest blocks the player's movement until it is opened
            IsBlocking = GameConfig.EnvironmentData.Blocking;
        }

        public override void OnInteract(PlayerController player)
        {
            int rewards = Grade == GameConfig.Drops.Grade3 ? GameConfig.Drops.ChestGrade3Rewards : (Grade == GameConfig.Drops.Grade2 ? GameConfig.Drops.ChestGrade2Rewards : GameConfig.Drops.ChestGrade1Rewards);
            Debug.Log(string.Format(GameConfig.Drops.LogChestOpened, Grade, rewards));
            
            Managers.UpgradeManager.Instance.OpenChest(rewards);
            
            // After interacting, we despawn the chest.
            ReleaseToPool();
        }
    }
}
