using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Utils;
using System.Collections.Generic;

using EFYV.Core.Entities;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    public class UpgradeManager : Singleton<UpgradeManager>
    {
        public event System.Action<int> OnNormalUpgradesRequested;
        public event System.Action<int, float> OnSpecialAttacksRequested;
        private EFYVBackend.Core.Models.UpgradeManagerData Data = new EFYVBackend.Core.Models.UpgradeManagerData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public bool IsSpecialAttackPhase
        {
            get => Data.IsSpecialAttackPhase;
            private set => Data.IsSpecialAttackPhase = value;
        }
        
        // As special attacks are used, this counter goes up, causing them to get worse
        private int specialAttackInvokes 
        { 
            get => Data.SpecialAttackInvokes; 
            set => Data.SpecialAttackInvokes = value; 
        }

        protected override void Awake()
        {
            base.Awake();
            Data.Block.SetInt((int)EFYVBackend.Core.Data.SystemSchema.IsSpecialAttackPhase, GameConfig.Weapons.Inventory.InitialSpecialAttackPhaseFlag);
            specialAttackInvokes = GameConfig.Weapons.Inventory.InitialSpecialAttackInvokes;
        }

        public void OpenChest(int rewardsCount)
        {
            // Called by ChestProp.cs
            TriggerUpgradeSelection(rewardsCount);
        }

        public void OnPlayerLevelUp()
        {
            // Normally player gets 3 choices
            int choices = IsSpecialAttackPhase ? GameConfig.Weapons.Inventory.UpgradeChoicesSpecialPhase : GameConfig.Weapons.Inventory.UpgradeChoicesNormalPhase;
            TriggerUpgradeSelection(choices);
        }

        private void TriggerUpgradeSelection(int choiceCount)
        {
            // If the player has maxed all weapons/evolutions, transition to Special Attack phase
            if (!CanOfferNormalUpgrades() && !IsSpecialAttackPhase)
            {
                IsSpecialAttackPhase = GameConfig.Weapons.Inventory.SpecialAttackPhase;
                Debug.Log(GameConfig.Weapons.LogSpecialAttackPhase);
            }

            if (IsSpecialAttackPhase)
            {
                float penaltyMultiplier = GameConfig.Weapons.Inventory.PenaltyMultiplierBase + (specialAttackInvokes * GameConfig.Weapons.Inventory.PenaltyMultiplierIncrement);
                OfferSpecialAttacks(choiceCount, penaltyMultiplier);
            }
            else
            {
                OfferNormalUpgrades(choiceCount);
            }
        }

        private bool CanOfferNormalUpgrades()
        {
            var p = PlayerController.Instance;
            if (p != null && p.WeaponSystem != null)
            {
                foreach (var w in p.WeaponSystem.activeWeapons)
                {
                    if (w.Level < GameConfig.Weapons.Inventory.MaxLevel) return true;
                }
            }
            return false;
        }

        private void OfferNormalUpgrades(int count)
        {
            Debug.LogFormat(GameConfig.Weapons.LogOfferingNormalUpgrades, count);
            OnNormalUpgradesRequested?.Invoke(count);
        }

        private void OfferSpecialAttacks(int count, float penaltyMultiplier)
        {
            specialAttackInvokes += GameConfig.Weapons.Inventory.SpecialAttackInvokeIncrement;
            Debug.LogFormat(GameConfig.Weapons.LogOfferingSpecialAttacks, count, penaltyMultiplier);
            OnSpecialAttacksRequested?.Invoke(count, penaltyMultiplier);
        }
        
        // UI Callback for when a Special Attack is clicked
        public void InvokeSpecialAttack(string attackName)
        {
            if (attackName == GameConfig.Weapons.SpecialAttackScreenWipeName)
            {
                Debug.Log(GameConfig.Weapons.LogScreenWipe);
                var enemies = Enemy.ActiveEnemies;
                for (int i = enemies.Count - 1; i >= 0; i--)
                {
                    enemies[i].TakeDamage(GameConfig.Weapons.SpecialAttackScreenWipeDamage); // Screen wipe
                }
            }
            else if (attackName == GameConfig.Weapons.SpecialAttackHalfMobHealthName)
            {
                Debug.Log(GameConfig.Weapons.LogMobHealthHalved);
                var enemies = Enemy.ActiveEnemies;
                for (int i = enemies.Count - 1; i >= 0; i--)
                {
                    float hp = enemies[i].CurrentHealth;
                    enemies[i].TakeDamage(hp * GameConfig.Weapons.SpecialAttackHalfMobHealthMultiplier);
                }
            }
        }
    }
}
