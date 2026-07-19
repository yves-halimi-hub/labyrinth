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

        // Raised exactly once per death, after the player entered the dead state and
        // despawned. Managers (spawning, map flow, UI) subscribe to react to game
        // over; subscribers must unsubscribe when destroyed (static event).
        public static event System.Action OnPlayerDied;

        // True from Die() until the next OnSpawn. While dead the player ignores
        // input, stops ticking weapons/projectiles, and is no longer a valid target
        // for enemy chase or enemy-owned weapons.
        public bool IsDead { get; private set; }

        // The player is repositioned (never auto-despawned) on map switches, so it
        // opts out of the scene-placed registration/cleanup (#25).
        protected override bool TracksAsScenePlaced => false;

        // ------------------------------------------------------------------
        // Timed buffs (#34): registered here, ticked centrally from Update (no
        // per-frame allocations or string ops - buff ids are matched by FastHash),
        // and reverted on expiry. Re-applying an active buff refreshes its timer
        // to the longer remainder; multipliers never stack.
        // ------------------------------------------------------------------
        private struct TimedBuff
        {
            public int BuffIdHash;
            public float Remaining;
            public float SpeedMultiplier;
        }

        // The buff-effect value lives in the shared config; this alias keeps
        // the public API.
        public const float MoveSpeedBuffMultiplier = GameConfig.Player.MoveSpeedBuffMultiplier;
        private const int InitialBuffCapacity = 4;
        private static readonly int MoveSpeedBuffIdHash =
            EFYVBackend.Core.Math.FastMath.FastHash(GameConfig.Merchant.PotionBuffId);

        private readonly List<TimedBuff> activeBuffs = new List<TimedBuff>(InitialBuffCapacity);

        public int ActiveBuffCount => activeBuffs.Count;

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
            // Base stats were just re-applied above, so any live buff multiplier is
            // already gone from BaseSpeed: drop the buffs WITHOUT reverting.
            activeBuffs.Clear();
            ApplyMetaProgressionStats();
            playerData.Level = GameConfig.Player.DefaultLevel;

            if (WeaponSystem != null)
            {
                WeaponSystem.Initialize(GameConfig.Weapons.Inventory.PlayerMaxWeapons);
            }
        }

        // Meta-progression fold (#34): base stats (authored SourceData or defaults,
        // both re-applied above, so repeated calls never compound) are scaled by
        // the account-legacy + toon stat multipliers persisted by SaveManager.
        // MaxHealth and MoveSpeed are 1.0-based multipliers in StatSchema.
        private void ApplyMetaProgressionStats()
        {
            if (!Managers.SaveManager.TryGetInstance(out Managers.SaveManager saveManager)) return;

            EFYVBackend.Core.Data.FastSchemaBlock combined = saveManager.GetCombinedStatsForToon(ActiveToonId);
            MaxHealth *= SanitizeStatMultiplier(combined.GetFloat((int)EFYVBackend.Core.Data.StatSchema.MaxHealth));
            BaseSpeed *= SanitizeStatMultiplier(combined.GetFloat((int)EFYVBackend.Core.Data.StatSchema.MoveSpeed));
            CurrentHealth = MaxHealth;
        }

        // Corrupt or unset save slots must never zero out or invert the player's
        // stats: anything non-finite or non-positive falls back to the neutral 1x.
        private static float SanitizeStatMultiplier(float multiplier)
        {
            return float.IsNaN(multiplier) || float.IsInfinity(multiplier) ||
                multiplier <= GameConfig.Entity.PositiveAmountThreshold
                ? GameConfig.Progression.DefaultMultiplier
                : multiplier;
        }

        // Pre-run toon selection entry point: stamps the toon and re-runs the full
        // initialization (stats fold included). Resets the weapon inventory, so it
        // is meant for the pre-run/character-select flow, not mid-run.
        public void ReinitializeForToon(string toonId)
        {
            ActiveToonId = toonId;
            Initialize();
        }

        // Applies (or refreshes) a timed buff by id. Returns false for unknown ids
        // or non-positive durations so purchase flows can refund. Known buffs:
        // GameConfig.Merchant.PotionBuffId ("MoveSpeed") - BaseSpeed multiplier.
        public bool ApplyTimedBuff(string buffId, float duration)
        {
            if (string.IsNullOrEmpty(buffId)) return false;
            if (float.IsNaN(duration) || duration <= GameConfig.Entity.PositiveAmountThreshold) return false;

            int buffIdHash = EFYVBackend.Core.Math.FastMath.FastHash(buffId);
            if (buffIdHash != MoveSpeedBuffIdHash) return false;

            for (int i = 0; i < activeBuffs.Count; i++)
            {
                if (activeBuffs[i].BuffIdHash != buffIdHash) continue;
                TimedBuff refreshed = activeBuffs[i];
                refreshed.Remaining = refreshed.Remaining < duration ? duration : refreshed.Remaining;
                activeBuffs[i] = refreshed;
                return true;
            }

            activeBuffs.Add(new TimedBuff
            {
                BuffIdHash = buffIdHash,
                Remaining = duration,
                SpeedMultiplier = MoveSpeedBuffMultiplier
            });
            BaseSpeed *= MoveSpeedBuffMultiplier;
            return true;
        }

        // Central buff ticking: reverse swap-remove iteration, zero allocations.
        private void TickActiveBuffs(float deltaTime)
        {
            for (int i = activeBuffs.Count - 1; i >= GameConfig.Runtime.FirstIndex; i--)
            {
                TimedBuff buff = activeBuffs[i];
                buff.Remaining -= deltaTime;
                if (buff.Remaining > GameConfig.Entity.PositiveAmountThreshold)
                {
                    activeBuffs[i] = buff;
                    continue;
                }

                BaseSpeed /= buff.SpeedMultiplier; // Revert on expiry.
                int lastIndex = activeBuffs.Count - 1;
                activeBuffs[i] = activeBuffs[lastIndex];
                activeBuffs.RemoveAt(lastIndex);
            }
        }

        private void ClearActiveBuffs()
        {
            for (int i = activeBuffs.Count - 1; i >= GameConfig.Runtime.FirstIndex; i--)
            {
                BaseSpeed /= activeBuffs[i].SpeedMultiplier;
            }
            activeBuffs.Clear();
        }

        public float iFrameDuration { get => playerData.IFrameDuration; set => playerData.IFrameDuration = value; }

        public override void TakeDamage(float amount)
        {
            if (playerData.IFrameTimer > GameConfig.Player.IFrameZeroThreshold) return; // Ignore damage during invincibility window
            
            base.TakeDamage(amount);
            playerData.IFrameTimer = iFrameDuration; // Reset i-frames
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            IsDead = false;
            // A respawn starts clean: expire-and-revert every timed buff (#34).
            ClearActiveBuffs();
        }

        private void Update()
        {
            // Game over: a dead player accepts no input and drives no combat loops.
            if (IsDead) return;

            float deltaTime = Time.deltaTime;
            if (playerData.IFrameTimer > GameConfig.Player.IFrameZeroThreshold) playerData.IFrameTimer -= deltaTime;

            // Central timed-buff ticking (#34).
            TickActiveBuffs(deltaTime);

            // Item #7: authored flash countdowns (the player ticks its own).
            TickAuthoredEffects(deltaTime);

            HandleInput(deltaTime);

            // Item #13: advance the imported animation (the player ticks its own).
            TickFlipbook(deltaTime);

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

            // Item #13: moving plays the walk clip, standing still the idle clip
            // (data-driven: only takes effect if the atlas names such a clip).
            bool moving = inputDir.x != GameConfig.Entity.StationaryAxisValue ||
                inputDir.y != GameConfig.Entity.StationaryAxisValue;
            PlayAnimation(moving ? GameConfig.Animation.StateWalk : GameConfig.Animation.StateIdle);

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
            // Saturating addition (mirrors SaveManager.SaturatingAdd): huge pickup
            // totals cap at int.MaxValue instead of wrapping negative.
            long total = (long)playerData.SessionCoins + amount;
            playerData.SessionCoins = total > int.MaxValue ? int.MaxValue : (int)total;
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
            // Idempotent: overkill damage or duplicate calls raise game over once.
            if (IsDead) return;
            IsDead = true;

            base.Die();
            Debug.Log(GameConfig.UI.GameOverMessage);

            // Game-over broadcast: managers stop spawning, enemies stop targeting the
            // corpse (Enemy.Tick and enemy-owned weapons check IsDead).
            OnPlayerDied?.Invoke();
        }
    }
}
