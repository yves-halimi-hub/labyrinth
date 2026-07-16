using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Environment.Implementations
{
    // A non-blocking interactable prop that gives the player experience when touched
    public class XPGem : InteractableProp
    {
        [Header(GameConfig.DataConfig.HeaderGemSettings)]
        [SerializeField] private float serializedXpValue = GameConfig.EnvironmentData.DefaultXPGemValue;
        public float xpValue
        {
            get => Data.Block.GetFloat((int)EFYVBackend.Core.Data.PropSchema.XpValue);
            set
            {
                serializedXpValue = value;
                Data.Block.SetFloat((int)EFYVBackend.Core.Data.PropSchema.XpValue, value);
            }
        }

        public override void Initialize()
        {
            base.Initialize();
            // A gem is a pickup, so it must not block the player's movement
            IsBlocking = GameConfig.EnvironmentData.NonBlocking;
        }

        protected override void Reset()
        {
            serializedXpValue = GameConfig.EnvironmentData.DefaultXPGemValue;
            base.Reset();
        }

        protected override void SyncSerializedSettings()
        {
            base.SyncSerializedSettings();
            Data.Block.SetFloat((int)EFYVBackend.Core.Data.PropSchema.XpValue, serializedXpValue);
        }

        public override void OnInteract(PlayerController player)
        {
            // Give experience to the player
            player.GainExperience(xpValue);

            // Optional: Play a sound effect or particle burst here
            
            // Return to the high-performance object pool instantly
            ReleaseToPool();
        }
    }
}
