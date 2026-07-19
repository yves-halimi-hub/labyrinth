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

        // #34: touching a merchant is an interaction REQUEST, never a purchase.
        // OnMerchantInteracted carries the merchant and its live offer list; a
        // shop UI (or test) subscribes and completes purchases explicitly through
        // AttemptPurchase. The old prototype auto-buy of item[0] on contact (which
        // drained coins on every re-entry) is gone.
        public override void OnInteract(PlayerController player)
        {
            Debug.LogFormat(GameConfig.Merchant.LogMerchantInteract, _availableItems.Count);
            OnMerchantInteracted?.Invoke(this, _availableItems);
        }

        /// <summary>
        /// The documented purchase API (#34): deducts the item's cost from the
        /// player's session coins, applies the item, and removes it from the offer
        /// list. A failed apply (e.g. healing at full health, unknown buff id)
        /// refunds the coins and keeps the item on offer. Returns true only when
        /// the item was successfully bought and applied.
        /// </summary>
        public bool AttemptPurchase(PlayerController player, PurchasableItem item)
        {
            if (player == null || item == null) return false;

            if (player.SpendSessionCoins(item.Cost))
            {
                if (item.Apply(player))
                {
                    Debug.LogFormat(GameConfig.Merchant.LogItemPurchased, item.ItemName, item.Cost);
                    _availableItems.Remove(item);
                    return true;
                }

                // Refund if apply failed (e.g. max health already)
                player.AddSessionCoins(item.Cost);
                return false;
            }

            Debug.LogWarningFormat(GameConfig.Merchant.LogNotEnoughCoins, item.ItemName, item.Cost, player.SessionCoins);
            return false;
        }
    }
}
