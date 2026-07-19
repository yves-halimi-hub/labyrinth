using UnityEngine;
using EFYV.Core.Interfaces;
using EFYV.Core.Data;
using System;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public abstract class LivingEntity : GameEntity, IDamageable
    {
        public float MaxHealth { get => Data.MaxHealth; protected set => Data.MaxHealth = value; }
        public float CurrentHealth { get => Data.CurrentHealth; protected set => Data.CurrentHealth = value; }
        public float BaseSpeed { get => Data.BaseSpeed; protected set => Data.BaseSpeed = value; }
        
        public EFYV.Core.Controllers.WeaponController WeaponSystem { get; protected set; }

        public event Action<float> OnHealthChanged;
        
        [SerializeField] protected LivingEntityData entityData;
        [SerializeField] private int hitboxPreviewFrame = GameConfig.Runtime.FirstIndex;
        [SerializeField] private EFYVBackend.Core.Math.FastMath.FacingDirection hitboxPreviewFacing = GameConfig.Entity.InitialFacing;
        public LivingEntityData SourceData => entityData;
        public int HitboxPreviewFrame => hitboxPreviewFrame;
        public EFYVBackend.Core.Math.FastMath.FacingDirection HitboxPreviewFacing => hitboxPreviewFacing;
        protected EFYVBackend.Core.Math.FastMath.FacingDirection currentFacing = GameConfig.Entity.InitialFacing;
        protected bool HasAuthoredStats { get; private set; }
        protected float AuthoredMaxHealth { get; private set; }
        protected float AuthoredBaseSpeed { get; private set; }

        // ------------------------------------------------------------------
        // Item #13 runtime flipbook state. A LivingEntity plays the CURRENT
        // facing's imported animation over EntityFacingImportData.Frames; the
        // frame is advanced by the central update loops (Enemy.Tick /
        // PlayerController.Update via TickFlipbook), never a per-entity
        // Update(), and holds no per-frame allocations. When a facing carries
        // no imported frames the entity falls back to the static directional
        // sprite (hand-authored entities without an imported atlas).
        // ------------------------------------------------------------------
        private Sprite[] flipbookFrames;
        private EntityHitboxRecord[] flipbookHitboxes;
        private EntityAtlasMetadata flipbookAtlas;
        private EntityAnimationMetadata[] flipbookAnimations;
        private int flipbookAnimationIndex = GameConfig.Runtime.NotFoundIndex;
        private string flipbookAnimationName;
        private string desiredAnimationName = GameConfig.Animation.DefaultState;
        private int flipbookLocalFrame = GameConfig.Animation.InitialLocalFrame;
        private int flipbookLoopStart;
        private int flipbookLoopEnd;
        private float flipbookFrameTimer = GameConfig.Animation.InitialFrameTimer;
        private bool flipbookReverse = GameConfig.Animation.InitialReverse;
        private bool flipbookPingPong;
        private bool flipbookActive;

        // Item #14: the hand-placed collider the designer Hurtbox drives.
        private BoxCollider2D hurtboxCollider;

        public bool HasRuntimeFlipbook => flipbookActive;
        public EFYVBackend.Core.Math.FastMath.FacingDirection CurrentFacing => currentFacing;
        // The global atlas frame index the flipbook currently shows (the frame
        // the hurtbox and the play-mode gizmo overlay follow). Falls back to the
        // designer preview frame when no flipbook is active.
        public int CurrentFlipbookGlobalFrame => flipbookActive
            ? flipbookAnimations[flipbookAnimationIndex].StartFrame + flipbookLocalFrame
            : hitboxPreviewFrame;
        public string CurrentAnimationName => flipbookActive ? flipbookAnimationName : null;
        public int CurrentAnimationLocalFrame => flipbookLocalFrame;

        public virtual void LoadData(LivingEntityData data)
        {
            entityData = data;
            ApplySourceData(preserveHealthRatio: false);
        }

        public virtual void RefreshDataFromAsset()
        {
            ApplySourceData(preserveHealthRatio: true);
        }

        public override void Initialize()
        {
            base.Initialize();
            CacheLivingEntityComponents();
            ApplySourceData(preserveHealthRatio: false);
        }

        private void CacheLivingEntityComponents()
        {
            WeaponSystem = GetComponent<EFYV.Core.Controllers.WeaponController>();
            // Item #14: the designer-authored Hurtbox is synced onto this
            // existing (hand-placed) BoxCollider2D; absent when the prefab uses
            // another collider shape, in which case the sync is a safe no-op.
            hurtboxCollider = GetComponent<BoxCollider2D>();
        }

        private void ApplySourceData(bool preserveHealthRatio)
        {
            if (entityData == null) return;

            EFYVBackend.Core.Data.FastSchemaBlock block = entityData.GetSchemaBlock();
            ApplySchemaBlock(block, preserveHealthRatio);
            ApplyDirectionalSprite(currentFacing);
            // Item #6: keep the asset's imported sub-element attachment
            // records at hand for future dynamic rendering (see below).
            authoredAttachments = entityData.ImportedAttachments;
        }

        // ------------------------------------------------------------------
        // Item #6: minimal runtime attachment consumer. The imported atlas
        // already contains the attachments FLATTENED into its pixels, so
        // nothing needs to render here; this entity merely STORES the
        // structured records (and can answer per-frame queries) so a future
        // dynamic sub-element sprite pipeline has its data ready. Rendering
        // dynamic sub-element sprites is deferred until that pipeline exists.
        // ------------------------------------------------------------------

        private EntityAttachmentRecord[] authoredAttachments;

        public EntityAttachmentRecord[] AuthoredAttachments => authoredAttachments;

        public int CountAttachmentsForFrame(int frameIndex)
        {
            if (authoredAttachments == null) return GameConfig.Runtime.EmptyCollectionCount;
            int count = GameConfig.Runtime.EmptyCollectionCount;
            for (int index = GameConfig.Runtime.FirstIndex;
                index < authoredAttachments.Length;
                index++)
            {
                if (authoredAttachments[index].FrameIndex == frameIndex) count++;
            }
            return count;
        }

        protected void ApplySchemaBlock(EFYVBackend.Core.Data.FastSchemaBlock block, bool preserveHealthRatio)
        {
            float previousMaxHealth = MaxHealth;
            float healthRatio = preserveHealthRatio && previousMaxHealth > GameConfig.Entity.DeathHealthThreshold
                ? Mathf.Clamp01(CurrentHealth / previousMaxHealth)
                : GameConfig.Runtime.UnitIntervalMax;

            AuthoredMaxHealth = block.GetFloat((int)EFYVBackend.Core.Data.AssetSchema.MaxHealth);
            AuthoredBaseSpeed = block.GetFloat((int)EFYVBackend.Core.Data.AssetSchema.BaseSpeed);
            HasAuthoredStats = true;
            MaxHealth = AuthoredMaxHealth;
            BaseSpeed = AuthoredBaseSpeed;
            ApplyAdditionalSchemaData(block);
            CurrentHealth = MaxHealth * healthRatio;
            NotifyHealthChanged();
        }

        protected virtual void ApplyAdditionalSchemaData(EFYVBackend.Core.Data.FastSchemaBlock block)
        {
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            CurrentHealth = MaxHealth;
            // Item #7: pooled reuse starts color-clean, then the asset's
            // OnSpawn-tagged effect descriptors (e.g. a spawn tint) apply.
            ResetAuthoredEffects();
            TriggerAuthoredEffects(GameConfig.EntityEffects.TriggerOnSpawn);
        }

        public virtual void TakeDamage(float amount)
        {
            // Negative damage is clamped to zero: damage never heals. Healing is the
            // exclusive job of Heal, which clamps against MaxHealth.
            if (amount < GameConfig.Entity.PositiveAmountThreshold)
            {
                amount = GameConfig.Entity.PositiveAmountThreshold;
            }
            // Item #7: a real hit (positive post-clamp damage) fires the
            // OnDamaged-tagged effect descriptors (e.g. a hurt flash).
            if (amount > GameConfig.Entity.PositiveAmountThreshold)
            {
                TriggerAuthoredEffects(GameConfig.EntityEffects.TriggerOnDamaged);
            }
            CurrentHealth -= amount;
            NotifyHealthChanged();
            if (CurrentHealth <= GameConfig.Entity.DeathHealthThreshold)
            {
                Die();
            }
        }

        public virtual void Heal(float amount)
        {
            if (amount <= GameConfig.Entity.PositiveAmountThreshold) return;
            CurrentHealth = Mathf.Min(CurrentHealth + amount, MaxHealth);
            NotifyHealthChanged();
        }

        protected void NotifyHealthChanged()
        {
            OnHealthChanged?.Invoke(CurrentHealth);
        }

        public virtual void Die()
        {
            ReleaseToPool();
        }

        protected abstract void Move(float deltaTime);

        // PERFORMANCE: C-Optimized Sprite Swapper
        // Uses the FastMath backend to resolve the movement vector and swaps the ScriptableObject reference instantly.
        public void UpdateDirectionalSprite(float moveX, float moveY)
        {
            if (moveX == GameConfig.Entity.StationaryAxisValue && moveY == GameConfig.Entity.StationaryAxisValue) return;
            if (entityData == null || spriteRenderer == null) return;

            var newFacing = EFYVBackend.Core.Math.FastMath.Get4WayDirection(moveX, moveY);
            
            if (newFacing != currentFacing)
            {
                currentFacing = newFacing;
                hitboxPreviewFacing = newFacing;
                ApplyDirectionalSprite(currentFacing);
            }
        }

        // ------------------------------------------------------------------
        // Item #7: minimal runtime interpretation of authored effect
        // descriptors (flash/tint against the SpriteRenderer color).
        //
        // - Triggers fire from the existing seams: OnSpawn and TakeDamage.
        // - Matching scans the CURRENT facing's imported atlas animations and
        //   applies every descriptor whose Trigger equals the fired tag, in
        //   metadata order (the last tint/flash of a tag wins its slot). Once
        //   a runtime flipbook exists (plan item #13) matching can narrow to
        //   the playing animation.
        // - tint: persistently recolors toward ColorRgba by Strength; it
        //   becomes the color a finished flash restores to. DurationMs is
        //   ignored for tints (persistent while spawned) - minimal by design.
        // - flash: temporarily recolors toward ColorRgba by Strength for
        //   DurationMs; the countdown is ticked by the central update loops
        //   (Enemy.Tick via SpawnManager, PlayerController.Update).
        // - particleHook: STORED on the asset, interpretation deferred until
        //   a particle pipeline exists; triggering one changes nothing here.
        // ------------------------------------------------------------------

        private Color effectRestoreColor = Color.white;
        private float effectFlashRemainingSeconds;
        private bool effectFlashActive;

        public bool HasActiveEffectFlash => effectFlashActive;

        protected void ResetAuthoredEffects()
        {
            effectRestoreColor = Color.white;
            effectFlashActive = false;
            effectFlashRemainingSeconds = GameConfig.EntityEffects.ExpiredFlashSeconds;
            if (spriteRenderer != null) spriteRenderer.color = Color.white;
        }

        public void TriggerAuthoredEffects(string trigger)
        {
            if (entityData == null || spriteRenderer == null || trigger == null) return;
            if (!entityData.TryGetImportedFacing(currentFacing, out EntityFacingImportData imported)) return;
            EntityAnimationMetadata[] animations = imported.AtlasMetadata.Animations;
            if (animations == null) return;

            for (int animationIndex = GameConfig.Runtime.FirstIndex;
                animationIndex < animations.Length;
                animationIndex++)
            {
                EntityEffectDescriptor[] effects = animations[animationIndex].Effects;
                if (effects == null) continue;
                for (int effectIndex = GameConfig.Runtime.FirstIndex;
                    effectIndex < effects.Length;
                    effectIndex++)
                {
                    if (!string.Equals(effects[effectIndex].Trigger, trigger, StringComparison.Ordinal))
                        continue;
                    ApplyAuthoredEffect(effects[effectIndex]);
                }
            }
        }

        // Central-loop countdown for an active flash; restores the persistent
        // (tint or clean) color when the flash expires.
        public void TickAuthoredEffects(float deltaTime)
        {
            if (!effectFlashActive) return;
            effectFlashRemainingSeconds -= deltaTime;
            if (effectFlashRemainingSeconds > GameConfig.EntityEffects.ExpiredFlashSeconds) return;
            effectFlashActive = false;
            effectFlashRemainingSeconds = GameConfig.EntityEffects.ExpiredFlashSeconds;
            if (spriteRenderer != null) spriteRenderer.color = effectRestoreColor;
        }

        private void ApplyAuthoredEffect(in EntityEffectDescriptor effect)
        {
            if (string.Equals(effect.EffectType, GameConfig.EntityEffects.TypeTint, StringComparison.Ordinal))
            {
                effectRestoreColor = Color.Lerp(
                    Color.white,
                    RgbaToColor(effect.ColorRgba),
                    Mathf.Clamp01(effect.Strength));
                if (!effectFlashActive) spriteRenderer.color = effectRestoreColor;
            }
            else if (string.Equals(effect.EffectType, GameConfig.EntityEffects.TypeFlash, StringComparison.Ordinal))
            {
                effectFlashActive = true;
                effectFlashRemainingSeconds =
                    effect.DurationMs / GameConfig.EntityEffects.MillisecondsPerSecond;
                spriteRenderer.color = Color.Lerp(
                    effectRestoreColor,
                    RgbaToColor(effect.ColorRgba),
                    Mathf.Clamp01(effect.Strength));
            }
            // particleHook: stored only; interpretation deferred (see above).
        }

        private static Color RgbaToColor(uint rgba)
        {
            const float channelMax = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.ColorMaxByte;
            return new Color(
                (byte)rgba / channelMax,
                (byte)(rgba >> EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.RgbaGreenShift) / channelMax,
                (byte)(rgba >> EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.RgbaBlueShift) / channelMax,
                (byte)(rgba >> EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.RgbaAlphaShift) / channelMax);
        }

        // ------------------------------------------------------------------
        // Item #13 flipbook runtime.
        // ------------------------------------------------------------------

        // Requests a movement/combat animation state ("idle"/"walk"/"attack").
        // A state only takes effect when an authored animation carries the
        // matching Name (else the first animation keeps playing); switching to
        // a genuinely different clip restarts it. Cheap and side-effect-free
        // when no flipbook is active.
        public void PlayAnimation(string animationName)
        {
            if (string.Equals(desiredAnimationName, animationName, StringComparison.Ordinal)) return;
            desiredAnimationName = animationName;
            if (flipbookActive) ApplyDirectionalSprite(currentFacing);
        }

        // Central-loop frame advance for the live facing's animation. Honors
        // per-frame durations (0 = inherit fps), the loop range, and ping-pong.
        // A single-frame (or single-frame loop) animation is static: nothing
        // to advance.
        public void TickFlipbook(float deltaTime)
        {
            if (!flipbookActive || flipbookLoopEnd <= flipbookLoopStart) return;
            if (deltaTime <= GameConfig.Animation.NonPositiveDeltaThreshold || float.IsNaN(deltaTime)) return;

            flipbookFrameTimer += deltaTime;
            int previousLocalFrame = flipbookLocalFrame;
            float frameDuration = CurrentFrameDurationSeconds();
            int advances = GameConfig.Runtime.EmptyCollectionCount;
            while (flipbookFrameTimer >= frameDuration && advances < GameConfig.Animation.MaxAdvancesPerTick)
            {
                flipbookFrameTimer -= frameDuration;
                AdvanceFlipbookFrame();
                frameDuration = CurrentFrameDurationSeconds();
                advances++;
            }

            if (flipbookLocalFrame != previousLocalFrame) ApplyCurrentFlipbookFrame();
        }

        private float CurrentFrameDurationSeconds()
        {
            EntityAnimationMetadata animation = flipbookAnimations[flipbookAnimationIndex];
            int durationMs = GameConfig.Animation.InheritFrameDurationMs;
            int[] frameDurations = animation.FrameDurationsMs;
            if (frameDurations != null &&
                flipbookLocalFrame >= GameConfig.Runtime.FirstIndex &&
                flipbookLocalFrame < frameDurations.Length)
            {
                durationMs = frameDurations[flipbookLocalFrame];
            }
            if (durationMs > GameConfig.Animation.InheritFrameDurationMs)
                return durationMs / GameConfig.Animation.MillisecondsPerSecond;

            int framesPerSecond = animation.FramesPerSecond;
            if (framesPerSecond <= GameConfig.Runtime.EmptyCollectionCount)
                framesPerSecond = GameConfig.Animation.DefaultFramesPerSecond;
            return GameConfig.Runtime.UnitIntervalMax / framesPerSecond;
        }

        private void AdvanceFlipbookFrame()
        {
            if (flipbookPingPong)
            {
                if (flipbookReverse)
                {
                    flipbookLocalFrame--;
                    if (flipbookLocalFrame <= flipbookLoopStart)
                    {
                        flipbookLocalFrame = flipbookLoopStart;
                        flipbookReverse = false;
                    }
                }
                else
                {
                    flipbookLocalFrame++;
                    if (flipbookLocalFrame >= flipbookLoopEnd)
                    {
                        flipbookLocalFrame = flipbookLoopEnd;
                        flipbookReverse = true;
                    }
                }
                return;
            }

            flipbookLocalFrame++;
            if (flipbookLocalFrame > flipbookLoopEnd) flipbookLocalFrame = flipbookLoopStart;
        }

        private void ApplyCurrentFlipbookFrame()
        {
            if (spriteRenderer == null || flipbookFrames == null) return;

            int globalFrame = flipbookAnimations[flipbookAnimationIndex].StartFrame + flipbookLocalFrame;
            if (globalFrame < GameConfig.Runtime.FirstIndex) globalFrame = GameConfig.Runtime.FirstIndex;
            if (globalFrame >= flipbookFrames.Length)
                globalFrame = flipbookFrames.Length - GameConfig.Runtime.ExclusiveUpperBoundOffset;

            Sprite frame = flipbookFrames[globalFrame];
            if (frame != null) spriteRenderer.sprite = frame;
            SyncHurtboxCollider(globalFrame);
        }

        // Item #14: drives the hand-placed BoxCollider2D from the FIRST valid
        // Hurtbox record authored for the given global atlas frame of the live
        // facing. Runs only on a frame/facing change or a data reload (never
        // unconditionally per tick). Frames that define no Hurtbox leave the
        // collider at its last bounds (a designer authoring one hurtbox for a
        // whole clip keeps it), and an entity without a BoxCollider2D or
        // without imported hitboxes is a no-op (hand-placed colliders survive).
        private void SyncHurtboxCollider(int globalFrame)
        {
            if (hurtboxCollider == null || flipbookHitboxes == null) return;

            for (int i = GameConfig.Runtime.FirstIndex; i < flipbookHitboxes.Length; i++)
            {
                EntityHitboxRecord record = flipbookHitboxes[i];
                if (record.FrameIndex != globalFrame) continue;
                if (!string.Equals(record.HitboxType, GameConfig.Hitbox.HurtboxType, StringComparison.Ordinal))
                    continue;
                if (!EntityHitboxGeometry.TryGetLocalBounds(
                    flipbookAtlas, record.Bounds, out Vector3 center, out Vector3 size))
                    continue;

                hurtboxCollider.offset = new Vector2(center.x, center.y);
                hurtboxCollider.size = new Vector2(size.x, size.y);
                return;
            }
        }

        // Resolves the flipbook for a facing: captures its frames/hitboxes/atlas
        // and the animation selected by the requested state, computing the
        // clamped loop range. Progress is preserved across a facing change when
        // the SAME animation keeps playing (a smooth turn), otherwise the clip
        // restarts at its loop start. Leaves the flipbook inactive when the
        // facing carries no imported frames or animations.
        private void ResolveFlipbook(EFYVBackend.Core.Math.FastMath.FacingDirection facing)
        {
            string previousName = flipbookAnimationName;
            int previousLocalFrame = flipbookLocalFrame;
            float previousTimer = flipbookFrameTimer;
            bool previousReverse = flipbookReverse;

            flipbookActive = false;
            flipbookFrames = null;
            flipbookHitboxes = null;
            flipbookAtlas = default;
            flipbookAnimations = null;
            flipbookAnimationIndex = GameConfig.Runtime.NotFoundIndex;
            flipbookAnimationName = null;

            if (entityData == null) return;
            if (!entityData.TryGetImportedFacing(facing, out EntityFacingImportData imported)) return;

            // Capture hitboxes/atlas even for a static (no-animation) import so a
            // frame-0 Hurtbox still drives the collider.
            flipbookHitboxes = imported.Hitboxes;
            flipbookAtlas = imported.AtlasMetadata;

            Sprite[] frames = imported.Frames;
            EntityAnimationMetadata[] animations = imported.AtlasMetadata.Animations;
            if (frames == null || frames.Length <= GameConfig.Runtime.EmptyCollectionCount) return;
            if (animations == null || animations.Length <= GameConfig.Runtime.EmptyCollectionCount) return;

            int index = ResolveAnimationIndex(animations, desiredAnimationName);
            if (index < GameConfig.Runtime.FirstIndex) return;

            flipbookFrames = frames;
            flipbookAnimations = animations;
            flipbookAnimationIndex = index;
            EntityAnimationMetadata animation = animations[index];
            flipbookAnimationName = animation.Name;
            flipbookPingPong = animation.PingPong;
            int lastLocalFrame = ClampLoopBounds(animation, out flipbookLoopStart, out flipbookLoopEnd);

            if (string.Equals(previousName, flipbookAnimationName, StringComparison.Ordinal))
            {
                flipbookLocalFrame = previousLocalFrame < flipbookLoopStart
                    ? flipbookLoopStart
                    : previousLocalFrame > lastLocalFrame ? lastLocalFrame : previousLocalFrame;
                flipbookFrameTimer = previousTimer;
                flipbookReverse = previousReverse;
            }
            else
            {
                flipbookLocalFrame = flipbookLoopStart;
                flipbookFrameTimer = GameConfig.Animation.InitialFrameTimer;
                flipbookReverse = GameConfig.Animation.InitialReverse;
            }

            flipbookActive = true;
        }

        private static int ResolveAnimationIndex(EntityAnimationMetadata[] animations, string name)
        {
            if (name != null)
            {
                for (int i = GameConfig.Runtime.FirstIndex; i < animations.Length; i++)
                {
                    if (string.Equals(animations[i].Name, name, StringComparison.Ordinal)) return i;
                }
            }
            return GameConfig.Runtime.FirstIndex;
        }

        // Clamps the imported loop-range tags into [0, FrameCount-1] and returns
        // the last valid local frame; a malformed range collapses to a single
        // static frame at the loop start.
        private static int ClampLoopBounds(
            in EntityAnimationMetadata animation,
            out int loopStart,
            out int loopEnd)
        {
            int lastLocalFrame = animation.FrameCount - GameConfig.Runtime.ExclusiveUpperBoundOffset;
            if (lastLocalFrame < GameConfig.Runtime.FirstIndex) lastLocalFrame = GameConfig.Runtime.FirstIndex;
            loopStart = ClampInt(animation.LoopStartFrame, GameConfig.Runtime.FirstIndex, lastLocalFrame);
            loopEnd = ClampInt(animation.LoopEndFrame, GameConfig.Runtime.FirstIndex, lastLocalFrame);
            if (loopEnd < loopStart) loopEnd = loopStart;
            return lastLocalFrame;
        }

        private static int ClampInt(int value, int minInclusive, int maxInclusive)
        {
            if (value < minInclusive) return minInclusive;
            if (value > maxInclusive) return maxInclusive;
            return value;
        }

        private void ApplyDirectionalSprite(EFYVBackend.Core.Math.FastMath.FacingDirection facing)
        {
            if (entityData == null || spriteRenderer == null) return;

            // Item #13: the imported animation drives the sprite (replacing the
            // static frame-0 swap); ApplyCurrentFlipbookFrame also syncs the
            // hurtbox (#14) for the freshly resolved facing/frame.
            ResolveFlipbook(facing);
            if (flipbookActive)
            {
                ApplyCurrentFlipbookFrame();
                return;
            }

            // Static fallback: a facing with no imported animation shows its
            // hand-authored directional sprite, but a captured frame-0 Hurtbox
            // still drives the collider.
            Sprite directionalSprite = null;
            switch (facing)
            {
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Up:
                    directionalSprite = entityData.spriteSheetUp;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Down:
                    directionalSprite = entityData.spriteSheetDown;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Left:
                    directionalSprite = entityData.spriteSheetLeft;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Right:
                    directionalSprite = entityData.spriteSheetRight;
                    break;
            }

            spriteRenderer.sprite = directionalSprite != null ? directionalSprite : entityData.spriteSheet;
            SyncHurtboxCollider(GameConfig.Runtime.FirstIndex);
        }
    }
}
