using System.Collections.Generic;
using UnityEngine;
using EFYV.Core.Entities;
using EFYV.Core.Weapons;
using EFYV.Core.Items;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Controllers
{
    // Attached to Player, Monster, MiniBoss, Boss.
    // Manages equipped Weapons, active PowerUps, and the complex Level 10 random evolution synergy logic.
    public class WeaponController : MonoBehaviour
    {
        private EFYVBackend.Core.Models.InventoryData Data = new EFYVBackend.Core.Models.InventoryData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public List<Weapon> activeWeapons = new List<Weapon>();
        public List<PowerUp> activePowerUps = new List<PowerUp>();

        // The combat side of this controller's owner. Every added or evolved weapon
        // is stamped with this faction so targeting and projectile damage never hit
        // the owner's own side.
        public Faction OwnerFaction { get; private set; }

        // True while the inventory has room for a NEW weapon. UpgradeManager uses
        // this to keep offering normal upgrades to players with empty slots.
        public bool HasFreeWeaponSlot => activeWeapons.Count < Data.MaxWeapons;

        public void Initialize(int maxWeapons)
        {
            Data.MaxWeapons = maxWeapons;
            // The host object decides the side once: enemies fight the player,
            // everything else fights enemies (Faction.Player is the zero default).
            OwnerFaction = GetComponent<Enemy>() != null ? Faction.Enemy : Faction.Player;
            for (int i = 0; i < activeWeapons.Count; i++)
            {
                activeWeapons[i].OwnerFaction = OwnerFaction;
            }
        }

        public void TickWeapons(float deltaTime)
        {
            int count = activeWeapons.Count;
            for (int i = 0; i < count; i++)
            {
                activeWeapons[i].Tick(deltaTime);
            }
        }

        public bool TryAddWeapon(Weapon weaponPrefabKey)
        {
            if (activeWeapons.Count >= Data.MaxWeapons) return false;

            weaponPrefabKey.OwnerFaction = OwnerFaction;
            activeWeapons.Add(weaponPrefabKey);
            return true;
        }

        public void AddPowerUp(PowerUp powerUp)
        {
            activePowerUps.Add(powerUp);
        }

        // Tries to evolve a weapon if it reached Max Level (10)
        public bool TryEvolveWeapon(Weapon weaponToEvolve)
        {
            if (weaponToEvolve.Level < GameConfig.Weapons.Inventory.MaxLevel) return false;
            if (weaponToEvolve.AvailableEvolutions.Count == GameConfig.Runtime.EmptyCollectionCount) return false;

            // Find all level 10 PowerUps we currently hold that match this weapon's possible evolutions
            List<WeaponEvolution> validEvolutions = new List<WeaponEvolution>();
            List<int> matchingPowerUpIndices = new List<int>();

            foreach (var evolution in weaponToEvolve.AvailableEvolutions)
            {
                // Check if we have the required powerup at max level
                int requiredPowerUpHash = EFYVBackend.Core.Math.FastMath.FastHash(evolution.RequiredPowerUpId);
                int validPowerUpIndex = activePowerUps.FindIndex(p => 
                    p.PowerUpIdHash == requiredPowerUpHash && 
                    p.Level >= GameConfig.Weapons.Inventory.MaxLevel);

                if (validPowerUpIndex != GameConfig.Weapons.MissingPowerUpIndex)
                {
                    validEvolutions.Add(evolution);
                    matchingPowerUpIndices.Add(validPowerUpIndex);
                }
            }

            if (validEvolutions.Count == GameConfig.Runtime.EmptyCollectionCount) return false;

            // "If more than 1 powerup level 10 that fit a different upgrade exist, the upgrade is random"
            int randomIndex = EFYVBackend.Core.Math.FastRandom.Range(GameConfig.Weapons.RandomChoiceMinIndex, validEvolutions.Count);
            WeaponEvolution chosenEvolution = validEvolutions[randomIndex];
            int consumedPowerUpIndex = matchingPowerUpIndices[randomIndex];
            int index = activeWeapons.IndexOf(weaponToEvolve);
            if (index == GameConfig.Weapons.MissingWeaponIndex || chosenEvolution.EvolvedWeaponPrefab == null) return false;

            Weapon evolvedWeapon = UnityEngine.Object.Instantiate(chosenEvolution.EvolvedWeaponPrefab, transform);
            evolvedWeapon.transform.localPosition = Vector3.zero;
            evolvedWeapon.OwnerFaction = OwnerFaction;
            activeWeapons[index] = evolvedWeapon;

            PowerUp consumedPowerUp = activePowerUps[consumedPowerUpIndex];
            consumedPowerUp.ConsumeUse();
            activePowerUps[consumedPowerUpIndex] = consumedPowerUp;

            UnityEngine.Object.Destroy(weaponToEvolve.gameObject);

            return true;
        }
    }
}
