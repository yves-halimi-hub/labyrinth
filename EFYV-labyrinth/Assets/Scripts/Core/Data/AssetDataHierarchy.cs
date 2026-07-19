using UnityEngine;
using System;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Data
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DesignableAssetAttribute : Attribute
    {
        public string DisplayName { get; }

        public DesignableAssetAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }

    // The previous numerical stats have been fully migrated to FastSchemaBlock.
    // These ScriptableObjects now ONLY hold Art and Editor configuration references, plus the raw binary block.

    [CreateAssetMenu(fileName = GameConfig.DataConfig.AssetMenuFileName, menuName = GameConfig.DataConfig.AssetMenuName)]
    public class GameAssetData : SchemaBackedAssetData
    {
        [Header(GameConfig.DataConfig.HeaderGeneral)]
        [SerializeField] private string _assetName;
        public string assetName
        {
            get => _assetName;
            set
            {
                _assetName = value;
                var block = GetSchemaBlock();
                block.SetInt((int)EFYVBackend.Core.Data.AssetSchema.AssetIdHash, EFYVBackend.Core.Math.FastMath.FastHash(value));
                SetSchemaBlock(block);
            }
        }

        private void OnValidate()
        {
            assetName = _assetName;
        }

        [Header(GameConfig.DataConfig.HeaderArt)]
        public Sprite sprite;

        // Item #13: the full imported frame set (in atlas order) when the
        // source art was a multi-frame sheet, so animated props play the
        // designer's frames instead of a hand-authored inspector array. Null
        // for single-sprite imports; `sprite` remains frame 0 either way.
        [SerializeField, HideInInspector] private Sprite[] importedFrames;
        public Sprite[] ImportedFrames => importedFrames;

        public void SetImportedFrames(Sprite[] frames)
        {
            importedFrames = frames;
            if (frames != null && frames.Length > GameConfig.Runtime.EmptyCollectionCount)
            {
                sprite = frames[GameConfig.Runtime.FirstIndex];
            }
        }
    }
}
