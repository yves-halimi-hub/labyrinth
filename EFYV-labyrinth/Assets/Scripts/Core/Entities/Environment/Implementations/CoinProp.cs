using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Interfaces;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Environment.Implementations
{
    public class CoinProp : InteractableProp
    {
        public Sprite[] gradeSprites;

        public int Grade { get => Data.Block.GetInt((int)EFYVBackend.Core.Data.PropSchema.Grade); private set => Data.Block.SetInt((int)EFYVBackend.Core.Data.PropSchema.Grade, value); }
        public int Value { get => Data.Block.GetInt((int)EFYVBackend.Core.Data.PropSchema.Value); private set => Data.Block.SetInt((int)EFYVBackend.Core.Data.PropSchema.Value, value); }

        public void InitializeGrade(int grade)
        {
            Grade = EFYVBackend.Core.Math.FastMath.FastClamp(grade, GameConfig.Drops.MinCoinGrade, GameConfig.Drops.MaxCoinGrade);
            Value = GameConfig.Drops.BaseCoinValue * Grade * Grade; // Higher grades scale better
            
            if (spriteRenderer != null && gradeSprites != null && gradeSprites.Length >= Grade)
            {
                spriteRenderer.sprite = gradeSprites[Grade - GameConfig.EnvironmentData.GradeToSpriteIndexOffset];
            }
        }

        public override void Initialize()
        {
            base.Initialize();
            Grade = GameConfig.Drops.MinCoinGrade;
            Value = GameConfig.Drops.BaseCoinValue;
            IsBlocking = GameConfig.EnvironmentData.NonBlocking;
        }

        public override void OnInteract(PlayerController player)
        {
            Debug.Log(string.Format(GameConfig.Drops.LogCoinPickedUp, Grade, Value));
            
            // Increment the Toon's persistent coin stash for out-of-game leveling
            EFYV.Core.Managers.SaveManager.Instance.AddCoinsToToon(player.ActiveToonId, Value);
            
            // Increment the in-game session coins for Merchants
            player.AddSessionCoins(Value);
            
            ReleaseToPool();
        }
    }
}
