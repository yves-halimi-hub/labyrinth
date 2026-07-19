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

        // The pool of weapon prefabs the default upgrade application path can grant
        // while the player still has a free weapon slot.
        [Tooltip(GameConfig.Weapons.TooltipNormalWeaponPool)]
        public Weapons.Weapon[] normalWeaponPool;

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
                // A free slot means a NEW weapon can always be offered: an empty
                // inventory must never flip the run into the special-attack phase.
                if (p.WeaponSystem.HasFreeWeaponSlot) return true;
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
            if (OnNormalUpgradesRequested != null)
            {
                // A UI is listening: it presents the choices and applies the picks
                // itself (WeaponController.TryAddWeapon / Weapon.Upgrade are public).
                OnNormalUpgradesRequested.Invoke(count);
                return;
            }

            // No UI subscriber: apply the upgrades directly so chests and level-ups
            // still progress the run.
            ApplyNormalUpgrades(count);
        }

        // Default upgrade application path: fill free weapon slots from the prefab
        // pool first, then raise the lowest-level weapon. Returns how many upgrades
        // were actually applied.
        public int ApplyNormalUpgrades(int count)
        {
            var p = PlayerController.Instance;
            if (p == null || p.WeaponSystem == null) return GameConfig.Runtime.EmptyCollectionCount;

            int applied = GameConfig.Runtime.EmptyCollectionCount;
            for (int i = GameConfig.Runtime.FirstIndex; i < count; i++)
            {
                if (!TryApplyOneNormalUpgrade(p.WeaponSystem)) break;
                applied += GameConfig.Weapons.Inventory.LevelIncrement;
            }
            return applied;
        }

        private bool TryApplyOneNormalUpgrade(EFYV.Core.Controllers.WeaponController weapons)
        {
            // 1. Prefer granting a NEW weapon while a slot is free.
            if (weapons.HasFreeWeaponSlot && normalWeaponPool != null &&
                normalWeaponPool.Length > GameConfig.Runtime.EmptyCollectionCount)
            {
                int prefabIndex = EFYVBackend.Core.Math.FastRandom.Range(
                    GameConfig.Weapons.RandomChoiceMinIndex, normalWeaponPool.Length);
                Weapons.Weapon prefab = normalWeaponPool[prefabIndex];
                if (prefab != null)
                {
                    Weapons.Weapon granted = Instantiate(prefab, weapons.transform);
                    granted.transform.localPosition = Vector3.zero;
                    if (weapons.TryAddWeapon(granted))
                    {
                        Debug.LogFormat(GameConfig.Weapons.LogGrantedNewWeapon, granted.GetType().Name);
                        return true;
                    }
                    Destroy(granted.gameObject);
                }
            }

            // 2. Otherwise raise the lowest-level weapon that is still below max.
            Weapons.Weapon lowest = null;
            for (int i = GameConfig.Runtime.FirstIndex; i < weapons.activeWeapons.Count; i++)
            {
                Weapons.Weapon candidate = weapons.activeWeapons[i];
                if (candidate.Level >= GameConfig.Weapons.Inventory.MaxLevel) continue;
                if (lowest == null || candidate.Level < lowest.Level) lowest = candidate;
            }
            if (lowest == null) return false;

            lowest.Upgrade();
            Debug.LogFormat(GameConfig.Weapons.LogUpgradedWeapon, lowest.GetType().Name, lowest.Level);
            return true;
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
