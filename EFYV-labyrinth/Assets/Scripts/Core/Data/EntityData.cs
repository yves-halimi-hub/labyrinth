using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

namespace EFYV.Core.Data
{
    // Item #14: the single source for converting an authored (pixel-space)
    // hitbox rectangle into the entity's local Unity units. Hoisted here from
    // EFYVHitboxGizmo so BOTH the editor gizmo overlay and the runtime
    // BoxCollider2D sync (LivingEntity) share one implementation instead of
    // duplicating the pivot math. The bounds are laid out around the sprite
    // pivot (center of the frame) with Unity's Y axis pointing up, mirroring
    // how EFYVPixelArtImporter pivots each sliced frame.
    public static class EntityHitboxGeometry
    {
        public static bool TryGetLocalBounds(
            EntityAtlasMetadata atlas,
            Rect authoredBounds,
            out Vector3 center,
            out Vector3 size)
        {
            center = default;
            size = default;
            if (atlas.FrameWidth <= GameConfig.Runtime.EmptyCollectionCount ||
                atlas.FrameHeight <= GameConfig.Runtime.EmptyCollectionCount ||
                SharedConfig.PixelsPerUnit <= GameConfig.Runtime.EmptyCollectionCount ||
                authoredBounds.width <= GameConfig.Runtime.EmptyCollectionCount ||
                authoredBounds.height <= GameConfig.Runtime.EmptyCollectionCount ||
                !IsFinite(authoredBounds.x) ||
                !IsFinite(authoredBounds.y) ||
                !IsFinite(authoredBounds.width) ||
                !IsFinite(authoredBounds.height))
                return false;

            float frameWidth = atlas.FrameWidth / SharedConfig.PixelsPerUnit;
            float frameHeight = atlas.FrameHeight / SharedConfig.PixelsPerUnit;
            float pivot = GameConfig.Importer.SpritePivotNormalized;
            center = new Vector3(
                authoredBounds.x + (authoredBounds.width * pivot) - (frameWidth * pivot),
                (frameHeight * pivot) - authoredBounds.y - (authoredBounds.height * pivot),
                GameConfig.Runtime.EmptyCollectionCount);
            size = new Vector3(
                authoredBounds.width,
                authoredBounds.height,
                GameConfig.Runtime.EmptyCollectionCount);
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
    // Item #7: one authored runtime-effect descriptor imported from the
    // .efyvlaby atlas animation block (name + params + trigger tag).
    // EffectType is "flash"/"tint"/"particleHook" (see the shared config's
    // Backend.Exporter.EffectType* strings); Trigger is the runtime seam tag
    // ("OnSpawn"/"OnDamaged" or a custom tag). Flash and tint are interpreted
    // by LivingEntity against the SpriteRenderer color; particleHook is
    // STORED but its interpretation is deferred until a particle pipeline
    // exists (Name identifies the particle system to spawn).
    [System.Serializable]
    public struct EntityEffectDescriptor
    {
        public string Name;
        public string EffectType;
        public string Trigger;
        public uint ColorRgba;
        public int DurationMs;
        public float Strength;
    }

    [System.Serializable]
    public struct EntityAnimationMetadata
    {
        public string Name;
        public int FramesPerSecond;
        public int StartFrame;
        public int FrameCount;
        // Item #10 timing/playback data from the .efyvlaby atlas block.
        // FrameDurationsMs is null/empty when the designer did not override any
        // frame (play at FramesPerSecond); when present it has FrameCount
        // entries and an entry of 0 means "inherit FramesPerSecond" for that
        // frame. Loop frames are animation-local indices; PingPong bounces
        // playback between them.
        public int[] FrameDurationsMs;
        public int LoopStartFrame;
        public int LoopEndFrame;
        public bool PingPong;
        // Item #7 authored effect descriptors; null/empty when the animation
        // carries none.
        public EntityEffectDescriptor[] Effects;
    }

    [System.Serializable]
    public struct EntityAtlasMetadata
    {
        public int FormatVersion;
        public int FrameWidth;
        public int FrameHeight;
        public int AtlasWidth;
        public int AtlasHeight;
        public EntityAnimationMetadata[] Animations;
    }

    [System.Serializable]
    public struct EntityHitboxRecord
    {
        public int FrameIndex;
        public string HitboxType;
        public Rect Bounds;
    }

    // Item #6: one sub-element attachment record imported from the .efyvlaby
    // top-level "attachments" array. FrameIndex is the global atlas frame,
    // SubElementName names the designer-bank sub-element, and X/Y is the
    // designer-canvas pixel position of that sub-element's pivot. The
    // attachment pixels are ALREADY flattened into the imported atlas; these
    // records are STORED for future dynamic sub-element rendering, which is
    // deferred until a sprite pipeline for bank sub-elements exists.
    [System.Serializable]
    public struct EntityAttachmentRecord
    {
        public int FrameIndex;
        public string SubElementName;
        public int X;
        public int Y;
        public int ZOrder;
        public bool FlipX;
        public bool FlipY;
    }

    [System.Serializable]
    public struct EntityFacingImportData
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private EntityAtlasMetadata atlasMetadata;
        [SerializeField] private EntityHitboxRecord[] hitboxes;

        public Sprite[] Frames => frames;
        public EntityAtlasMetadata AtlasMetadata => atlasMetadata;
        public EntityHitboxRecord[] Hitboxes => hitboxes;
        // Metadata counts as imported data only when it describes a real frame:
        // BOTH dimensions must be positive (a width-only or height-only atlas is
        // torn data, not a usable import).
        public bool HasImportedData =>
            (frames != null && frames.Length > GameConfig.Runtime.EmptyCollectionCount) ||
            (atlasMetadata.FrameWidth > GameConfig.Runtime.EmptyCollectionCount &&
                atlasMetadata.FrameHeight > GameConfig.Runtime.EmptyCollectionCount) ||
            (hitboxes != null && hitboxes.Length > GameConfig.Runtime.EmptyCollectionCount);

        public EntityFacingImportData(
            Sprite[] frames,
            EntityAtlasMetadata atlasMetadata,
            EntityHitboxRecord[] hitboxes)
        {
            this.frames = frames;
            this.atlasMetadata = atlasMetadata;
            this.hitboxes = hitboxes;
        }
    }

    // The previous numerical stats have been fully migrated to FastSchemaBlock.
    // These ScriptableObjects now ONLY hold Art and Editor configuration references, plus the raw binary block.

    [CreateAssetMenu(fileName = GameConfig.DataConfig.AssetMenuFileName, menuName = GameConfig.DataConfig.AssetMenuName)]
    public class EntityData : SchemaBackedAssetData
    {
        [Header(GameConfig.DataConfig.HeaderGeneral)]
        [SerializeField] private string _entityName;
        public string entityName
        {
            get => _entityName;
            set
            {
                _entityName = value;
                var block = GetSchemaBlock();
                block.SetInt((int)EFYVBackend.Core.Data.AssetSchema.AssetIdHash, EFYVBackend.Core.Math.FastMath.FastHash(value));
                SetSchemaBlock(block);
            }
        }

        private void OnValidate()
        {
            entityName = _entityName;
        }

        [Header(GameConfig.DataConfig.HeaderArt)]
        public Sprite spriteSheet;

        [SerializeField, HideInInspector] private Sprite[] spriteFrames;
        [SerializeField, HideInInspector] private EntityAtlasMetadata atlasMetadata;
        [SerializeField, HideInInspector] private EntityHitboxRecord[] hitboxes;

        public Sprite[] SpriteFrames => spriteFrames;
        public EntityAtlasMetadata AtlasMetadata => atlasMetadata;
        public EntityHitboxRecord[] Hitboxes => hitboxes;

        public void SetImportedAtlas(EntityAtlasMetadata metadata, Sprite[] frames)
        {
            atlasMetadata = metadata;
            if (frames != null && frames.Length > GameConfig.Runtime.EmptyCollectionCount)
            {
                spriteFrames = frames;
                spriteSheet = frames[GameConfig.Runtime.FirstIndex];
            }
        }

        public void SetImportedHitboxes(EntityHitboxRecord[] records)
        {
            hitboxes = records;
        }

    }

    public class LivingEntityData : EntityData
    {
        [Header(GameConfig.DataConfig.HeaderDirectionalSprites)]
        public Sprite spriteSheetDown;
        public Sprite spriteSheetUp;
        public Sprite spriteSheetLeft;
        public Sprite spriteSheetRight;

        [SerializeField, HideInInspector] private EntityFacingImportData importedDown;
        [SerializeField, HideInInspector] private EntityFacingImportData importedUp;
        [SerializeField, HideInInspector] private EntityFacingImportData importedLeft;
        [SerializeField, HideInInspector] private EntityFacingImportData importedRight;

        public void SetImportedFacing(
            EFYVBackend.Core.Math.FastMath.FacingDirection facing,
            EntityAtlasMetadata metadata,
            Sprite[] frames,
            EntityHitboxRecord[] hitboxes)
        {
            EntityFacingImportData previous;
            TryGetImportedFacing(facing, out previous);
            Sprite[] retainedFrames = frames != null && frames.Length > GameConfig.Runtime.EmptyCollectionCount
                ? frames
                : previous.Frames;
            var importedData = new EntityFacingImportData(retainedFrames, metadata, hitboxes);
            Sprite firstFrame = retainedFrames != null && retainedFrames.Length > GameConfig.Runtime.EmptyCollectionCount
                ? retainedFrames[GameConfig.Runtime.FirstIndex]
                : null;

            switch (facing)
            {
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Up:
                    importedUp = importedData;
                    if (firstFrame != null) spriteSheetUp = firstFrame;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Down:
                    importedDown = importedData;
                    if (firstFrame != null) spriteSheetDown = firstFrame;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Left:
                    importedLeft = importedData;
                    if (firstFrame != null) spriteSheetLeft = firstFrame;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Right:
                    importedRight = importedData;
                    if (firstFrame != null) spriteSheetRight = firstFrame;
                    break;
            }
        }

        public bool TryGetImportedFacing(
            EFYVBackend.Core.Math.FastMath.FacingDirection facing,
            out EntityFacingImportData importedData)
        {
            switch (facing)
            {
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Up:
                    importedData = importedUp;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Down:
                    importedData = importedDown;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Left:
                    importedData = importedLeft;
                    break;
                case EFYVBackend.Core.Math.FastMath.FacingDirection.Right:
                    importedData = importedRight;
                    break;
                default:
                    importedData = default;
                    return false;
            }

            return importedData.HasImportedData;
        }
    }

    public class EnemyData : LivingEntityData
    {
        // Enemy specific visual config can go here
    }

    public class BossData : EnemyData
    {
        // Boss specific visual config can go here
    }
}
