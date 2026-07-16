using UnityEngine;
using System.Collections.Generic;
using EFYV.Core.Entities.Items.Merchant;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Environment.Implementations
{
    public class BaseMerchantProp : InteractableProp
    {
        public static event System.Action<BaseMerchantProp, List<PurchasableItem>> OnMerchantInteracted;

        private List<PurchasableItem> _availableItems = new List<PurchasableItem>();

        public override void Initialize()
        {
            base.Initialize();
            IsBlocking = GameConfig.EnvironmentData.Blocking; // Merchants block movement
            GenerateInventory();
        }

        private void GenerateInventory()
        {
            _availableItems.Clear();
            
            // Using backend PRNG to randomly select 4 items for this merchant encounter
            int choices = GameConfig.Merchant.BaseItemChoices;
            Debug.LogFormat(GameConfig.Merchant.LogMerchantEncountered, choices);

            for (int i = 0; i < choices; i++)
            {
                float roll = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Runtime.UnitIntervalMin, GameConfig.Runtime.UnitIntervalMax);

                if (roll < GameConfig.Merchant.RollThresholdChicken)
                {
                    _availableItems.Add(new HealingPurchase(GameConfig.Merchant.ChickenName, GameConfig.Merchant.ChickenCost, GameConfig.Merchant.ChickenHeal));
                }
                else if (roll < GameConfig.Merchant.RollThresholdPotion)
                {
                    _availableItems.Add(new TemporaryBuffPurchase(GameConfig.Merchant.PotionName, GameConfig.Merchant.PotionCost, GameConfig.Merchant.PotionBuffId, GameConfig.Merchant.PotionDuration));
                }
                else
                {
                    _availableItems.Add(new WeaponUpgradePurchase(GameConfig.Merchant.MysteryWeaponName, GameConfig.Merchant.MysteryWeaponCost, GameConfig.Merchant.MysteryWeaponId));
                }
            }
        }

        public override void OnInteract(PlayerController player)
        {
            Debug.LogFormat(GameConfig.Merchant.LogMerchantInteract, _availableItems.Count);
            OnMerchantInteracted?.Invoke(this, _availableItems);
            
            // For now, auto-buy the first item to test logic
            if (_availableItems.Count > GameConfig.Runtime.EmptyCollectionCount)
            {
                AttemptPurchase(PlayerController.Instance, _availableItems[GameConfig.Runtime.FirstIndex]);
            }
        }

        public void AttemptPurchase(PlayerController player, PurchasableItem item)
        {
            if (player.SpendSessionCoins(item.Cost))
            {
                if (item.Apply(player))
                {
                    Debug.LogFormat(GameConfig.Merchant.LogItemPurchased, item.ItemName, item.Cost);
                    _availableItems.Remove(item);
                }
                else
                {
                    // Refund if apply failed (e.g. max health already)
                    player.AddSessionCoins(item.Cost);
                }
            }
            else
            {
                Debug.LogWarningFormat(GameConfig.Merchant.LogNotEnoughCoins, item.ItemName, item.Cost, player.SessionCoins);
            }
        }
    }
}
