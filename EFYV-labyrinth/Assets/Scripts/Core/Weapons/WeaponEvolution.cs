using System;
using UnityEngine;

namespace EFYV.Core.Weapons
{
    // Represents a potential final upgrade (evolution) for a Weapon
    [Serializable]
    public class WeaponEvolution
    {
        [SerializeField] private string requiredPowerUpId;
        [SerializeField] private Weapon evolvedWeaponPrefab;

        public string RequiredPowerUpId => requiredPowerUpId;
        public Weapon EvolvedWeaponPrefab => evolvedWeaponPrefab;

        public WeaponEvolution(string requiredPowerUpId, Weapon evolvedWeaponPrefab)
        {
            this.requiredPowerUpId = requiredPowerUpId;
            this.evolvedWeaponPrefab = evolvedWeaponPrefab;
        }
    }
}
