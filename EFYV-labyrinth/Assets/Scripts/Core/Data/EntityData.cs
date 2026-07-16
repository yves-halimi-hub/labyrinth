using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Data
{
    [System.Serializable]
    public struct EntityAnimationMetadata
    {
        public string Name;
        public int FramesPerSecond;
        public int StartFrame;
        public int FrameCount;
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

    [System.Serializable]
    public struct EntityFacingImportData
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private EntityAtlasMetadata atlasMetadata;
        [SerializeField] private EntityHitboxRecord[] hitboxes;

        public Sprite[] Frames => frames;
        public EntityAtlasMetadata AtlasMetadata => atlasMetadata;
        public EntityHitboxRecord[] Hitboxes => hitboxes;
        public bool HasImportedData =>
            (frames != null && frames.Length > GameConfig.Runtime.EmptyCollectionCount) ||
            atlasMetadata.FrameWidth > GameConfig.Runtime.EmptyCollectionCount ||
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
