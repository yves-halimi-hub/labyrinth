using UnityEngine;
using EFYV.Core.Data;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons
{
    public abstract class Weapon : MonoBehaviour
    {
        protected EFYVBackend.Core.Models.WeaponData Data = new EFYVBackend.Core.Models.WeaponData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public float CooldownTime 
        { 
            get => Data.CooldownTime; 
            protected set => Data.CooldownTime = value; 
        }
        public float BaseDamage 
        { 
            get => Data.BaseDamage; 
            protected set => Data.BaseDamage = value; 
        }
        public int Level 
        { 
            get => Data.Level; 
            protected set => Data.Level = value; 
        }
        
        public System.Collections.Generic.List<WeaponEvolution> AvailableEvolutions = new System.Collections.Generic.List<WeaponEvolution>();

        protected float currentCooldown 
        { 
            get => Data.CurrentCooldown; 
            set => Data.CurrentCooldown = value; 
        }

        protected virtual void Awake()
        {
            Level = GameConfig.Weapons.Inventory.InitialLevel;
        }

        public virtual void Tick(float deltaTime)
        {
            currentCooldown -= deltaTime;
            if (currentCooldown <= GameConfig.Weapons.CooldownReadyThreshold)
            {
                Fire();
                currentCooldown = CooldownTime;
            }
        }

        public abstract void Fire();

        public virtual void Upgrade()
        {
            Level += GameConfig.Weapons.Inventory.LevelIncrement;
            // Specific weapon subclasses will implement what level up means 
            // (e.g., more damage, lower cooldown, more projectiles)
        }
    }
}
