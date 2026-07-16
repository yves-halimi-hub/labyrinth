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

    }
}
