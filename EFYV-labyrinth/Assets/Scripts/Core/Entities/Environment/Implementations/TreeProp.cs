using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Environment.Implementations
{
    // A non-blocking, non-interactable decoration (e.g. Grass, Waving Trees)
    public class TreeProp : NonInteractableProp
    {
        public override void Initialize()
        {
            base.Initialize();
            // Trees are usually something the player can walk behind/through in these games
            IsBlocking = GameConfig.EnvironmentData.NonBlocking;
            
            // Unity's renderer handles depth sorting automatically based on Y position
        }
    }
}
