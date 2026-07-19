using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using EFYV.Core.Data;
using EFYVBackend.Core.IO;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Editor
{
    // Item #5: ingests published .efyvmap binaries (FastMapExporter envelope)
    // into MapAssetData assets, the map counterpart of EFYVPixelArtImporter.
    // The map id is the file stem; the tileset link resolves against the
    // sibling tileset asset the pixel-art importer created from the tileset
    // .efyvlaby ("<tilesetName>_Data.asset" next to the map file).
    public class EFYVMapImporter : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string importedPath in importedAssets)
            {
                if (!importedPath.EndsWith(
                    GameConfig.MapImporter.ExtensionMap,
                    StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    ImportMapFile(importedPath);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        internal static void ImportMapFile(string path)
        {
            // The stem becomes the runtime map id (DoorProp.TargetMapId), so
            // it obeys the same safe-stem policy as .efyvlaby identities.
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!SafePathPolicy.IsSafeFileStem(stem))
            {
                Debug.LogError(string.Format(GameConfig.MapImporter.LogErrorUnsafeStem, path, stem));
                return;
            }

            // Tri-state parse (#16c pattern): a vanished file and a malformed
            // one report different causes.
            EfyvParseResult parseResult = FastMapImporter.TryParse(path, out MapFileData data);
            if (parseResult == EfyvParseResult.Missing)
            {
                Debug.LogError(string.Format(GameConfig.MapImporter.LogErrorMissingFile, path));
                return;
            }
            if (parseResult == EfyvParseResult.Malformed)
            {
                Debug.LogError(string.Format(GameConfig.MapImporter.LogErrorMalformed, path));
                return;
            }

            string directory = Path.GetDirectoryName(path);
            string assetPath = directory + GameConfig.Importer.PathSeparator + stem +
                GameConfig.MapImporter.ExtensionAsset;

            UnityEngine.Object existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            MapAssetData asset = existing as MapAssetData;
            if (existing != null && asset == null)
            {
                Debug.LogError(string.Format(
                    GameConfig.MapImporter.LogErrorExistingAssetTypeMismatch,
                    path,
                    assetPath));
                return;
            }

            bool isNew = GameConfig.Importer.InitialIsNewAsset;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MapAssetData>();
                isNew = GameConfig.Importer.IsNewAsset;
            }

            asset.name = stem;
            asset.mapId = stem;
            asset.width = data.Width;
            asset.height = data.Height;
            asset.tilesetName = data.TilesetName;
            asset.tiles = new int[data.Tiles.Length];
            for (int index = GameConfig.Runtime.FirstIndex; index < data.Tiles.Length; index++)
                asset.tiles[index] = data.Tiles[index];
            asset.props = new MapPropPlacement[data.Props.Length];
            for (int index = GameConfig.Runtime.FirstIndex; index < data.Props.Length; index++)
            {
                asset.props[index] = new MapPropPlacement
                {
                    assetKey = data.Props[index].AssetKey,
                    x = data.Props[index].X,
                    y = data.Props[index].Y,
                    scale = data.Props[index].Scale
                };
            }

            // Link the tileset asset the pixel-art importer produced from the
            // tileset .efyvlaby export sitting next to the map file.
            asset.tileset = null;
            if (!string.IsNullOrEmpty(data.TilesetName))
            {
                string tilesetAssetPath = directory + GameConfig.Importer.PathSeparator +
                    data.TilesetName + GameConfig.Importer.ExtensionAsset;
                asset.tileset = AssetDatabase.LoadAssetAtPath<TilesetAssetData>(tilesetAssetPath);
                if (asset.tileset == null ||
                    asset.tileset.tileSprites == null ||
                    asset.tileset.tileSprites.Length == GameConfig.Runtime.EmptyCollectionCount)
                {
                    Debug.LogWarning(string.Format(
                        GameConfig.MapImporter.LogWarningMissingTilesetSprites,
                        stem,
                        data.TilesetName));
                }
            }

            if (isNew) AssetDatabase.CreateAsset(asset, assetPath);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log(string.Format(
                GameConfig.MapImporter.LogImported,
                stem,
                data.Width,
                data.Height,
                data.Props.Length,
                path));
        }
    }
}
