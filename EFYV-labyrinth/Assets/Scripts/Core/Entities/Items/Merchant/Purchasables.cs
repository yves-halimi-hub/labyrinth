using UnityEngine;
using EFYV.Core.Entities;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Items.Merchant
{
    public abstract class PurchasableItem
    {
        protected EFYVBackend.Core.Models.PurchasableData Data = new EFYVBackend.Core.Models.PurchasableData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public string ItemName 
        { 
            get => Data.ItemName; 
            protected set => Data.ItemName = value; 
        }
        public int Cost
        {
            get => Data.Cost;
            protected set => Data.Cost = value;
        }

        public PurchasableItem(string itemName, int cost)
        {
            ItemName = itemName;
            Cost = cost;
        }

        // Returns true if the purchase was successfully applied
        public abstract bool Apply(PlayerController player);
    }

    public class HealingPurchase : PurchasableItem
    {
        public float HealAmount
        {
            get => Data.HealAmount;
            private set => Data.HealAmount = value;
        }

        public HealingPurchase(string itemName, int cost, float healAmount) : base(itemName, cost)
        {
            HealAmount = healAmount;
        }

        public override bool Apply(PlayerController player)
        {
            if (player.CurrentHealth >= player.MaxHealth) return false;
            
            player.Heal(HealAmount);
            Debug.LogFormat(GameConfig.Merchant.LogHealingApplied, HealAmount);
            return true;
        }
    }

    public class TemporaryBuffPurchase : PurchasableItem
    {
        public string BuffId 
        { 
            get => Data.BuffId; 
            private set => Data.BuffId = value; 
        }
        public float Duration
        {
            get => Data.Duration;
            private set => Data.Duration = value;
        }

        public TemporaryBuffPurchase(string itemName, int cost, string buffId, float duration) : base(itemName, cost)
        {
            BuffId = buffId;
            Duration = duration;
        }

        public override bool Apply(PlayerController player)
        {
            // Buff system isn't fully implemented yet, but we apply it locally via a mock Log for now
            Debug.LogFormat(GameConfig.Merchant.LogBuffApplied, BuffId, Duration);
            return true;
        }
    }

    public class WeaponUpgradePurchase : PurchasableItem
    {
        public string WeaponId 
        { 
            get => Data.WeaponId; 
            private set => Data.WeaponId = value; 
        }

        public WeaponUpgradePurchase(string itemName, int cost, string weaponId) : base(itemName, cost)
        {
            WeaponId = weaponId;
        }

        public override bool Apply(PlayerController player)
        {
            if (player.WeaponSystem != null)
            {
                Managers.UpgradeManager.Instance.OnPlayerLevelUp(); // Trigger a random upgrade UI screen
                Debug.LogFormat(GameConfig.Merchant.LogWeaponUpgradeApplied, WeaponId);
                return true;
            }
            return false;
        }
    }
}
