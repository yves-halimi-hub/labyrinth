using UnityEngine;
using System.Collections.Generic;
using EFYV.Core.Weapons;
using EFYV.Core.Data;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public class PlayerController : LivingEntity
    {
        private EFYVBackend.Core.Models.PlayerData playerData = new EFYVBackend.Core.Models.PlayerData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
        
        public string ActiveToonId 
        { 
            get => playerData.ActiveToonId; 
            set => playerData.ActiveToonId = value; 
        }
        
        public static PlayerController Instance { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            if (Instance == null) Instance = this;
            else
            {
                Destroy(gameObject);
                return;
            }
            
            playerData.SessionCoins = GameConfig.Player.InitialSessionCoins;
            playerData.IFrameDuration = GameConfig.Player.InvincibilityFramesDuration;
            playerData.IFrameTimer = GameConfig.Player.InitialIFrameTimer;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void Initialize()
        {
            base.Initialize();
            if (SourceData == null)
            {
                MaxHealth = GameConfig.Player.DefaultMaxHealth;
                CurrentHealth = MaxHealth;
                BaseSpeed = GameConfig.Player.DefaultBaseSpeed;
            }
            playerData.Level = GameConfig.Player.DefaultLevel;
            
            if (WeaponSystem != null)
            {
                WeaponSystem.Initialize(GameConfig.Weapons.Inventory.PlayerMaxWeapons);
            }
        }

        public float iFrameDuration { get => playerData.IFrameDuration; set => playerData.IFrameDuration = value; }

        public override void TakeDamage(float amount)
        {
            if (playerData.IFrameTimer > GameConfig.Player.IFrameZeroThreshold) return; // Ignore damage during invincibility window
            
            base.TakeDamage(amount);
            playerData.IFrameTimer = iFrameDuration; // Reset i-frames
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (playerData.IFrameTimer > GameConfig.Player.IFrameZeroThreshold) playerData.IFrameTimer -= deltaTime;

            HandleInput(deltaTime);
            
            // Fire all active weapons using the inherited weapon system
            if (WeaponSystem != null)
            {
                WeaponSystem.TickWeapons(deltaTime);
            }

            // PERFORMANCE: UPDATE MANAGER PATTERN
            // Iterates all bullets in a single bounded C# loop, saving Native-to-Managed overhead.
            TickActiveProjectiles(deltaTime);
        }

        internal static void TickActiveProjectiles(float deltaTime)
        {
            var projectiles = Projectile.ActiveProjectiles;
            for (int i = projectiles.Count - 1; i >= GameConfig.Runtime.FirstIndex; i--)
            {
                projectiles[i].Tick(deltaTime);
            }
        }

        protected override void Move(float deltaTime)
        {
            // Handled inside HandleInput for the Player
        }

        private void HandleInput(float deltaTime)
        {
            float moveX = Input.GetAxisRaw(GameConfig.Player.InputHorizontal);
            float moveY = Input.GetAxisRaw(GameConfig.Player.InputVertical);

            Vector2 inputDir = new Vector2(moveX, moveY).FastNormalized();

            // Update Directional Sprite based on input
            UpdateDirectionalSprite(inputDir.x, inputDir.y);
            
            // MIGRATION: Bypassed Unity's Transform.Translate wrapper
            entityTransform.ApplyFastTranslation(inputDir.x, inputDir.y, BaseSpeed, deltaTime);
        }

        public void GainExperience(float amount)
        {
            playerData.Experience += amount;
            if (playerData.Experience >= playerData.Level * GameConfig.Player.ExpNeededPerLevelMultiplier) 
            {
                LevelUp();
            }
        }

        public void AddSessionCoins(int amount)
        {
            if (amount <= GameConfig.Player.ZeroAmount) return;
            playerData.SessionCoins += amount;
        }

        public bool SpendSessionCoins(int amount)
        {
            if (amount <= GameConfig.Player.ZeroAmount) return false;
            if (playerData.SessionCoins >= amount)
            {
                playerData.SessionCoins -= amount;
                return true;
            }
            return false;
        }

        public int SessionCoins { get => playerData.SessionCoins; }

        public void LevelUp()
        {
            playerData.Level += GameConfig.Player.LevelIncrement;
            playerData.Experience = GameConfig.Player.InitialExperience; 
            if (Managers.UpgradeManager.Instance != null)
            {
                Managers.UpgradeManager.Instance.OnPlayerLevelUp();
            }
        }
        
        public override void Die()
        {
            base.Die();
            Debug.Log(GameConfig.UI.GameOverMessage);
            // Handle Game Over State
        }
    }
}
