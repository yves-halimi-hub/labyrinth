using System;
using UnityEngine;
using EFYVBackend.Core.Collections;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Data
{
    // Item #5: one prop placement carried by an imported map. Instantiation
    // is deferred until the data-to-prefab factory exists (plan item #4);
    // the records are stored so that work has designer data to consume.
    [Serializable]
    public struct MapPropPlacement
    {
        public string assetKey;
        public int x;
        public int y;
        public float scale;
    }

    // Item #5: an imported .efyvmap as a Unity asset. Written by
    // EFYVMapImporter; consumed by MapViewportController.LoadMapData, which
    // copies the tile grid into its FastGridMap and (when the tileset link
    // resolved at import time) feeds tilePalette from the tileset's sliced
    // sprites. Tile values below the runtime palette floor
    // (GameConfig.Map.MinimumTileId) render as blank cells.
    public class MapAssetData : ScriptableObject
    {
        [Header(GameConfig.DataConfig.HeaderMapSettings)]
        public string mapId;
        public int width;
        public int height;
        // Row-major tile ids (int-typed because Unity does not serialize
        // short arrays; values stay within the .efyvmap int16 range).
        public int[] tiles;
        public MapPropPlacement[] props;
        public string tilesetName;
        public TilesetAssetData tileset;

        // A map asset the viewport can actually load: positive dimensions
        // and a tile array covering exactly width*height cells.
        public bool HasLoadableTiles()
        {
            return width > GameConfig.Runtime.EmptyCollectionCount &&
                height > GameConfig.Runtime.EmptyCollectionCount &&
                tiles != null &&
                tiles.LongLength == (long)width * height;
        }

        // Copies the tile grid into a backend map of matching dimensions.
        public bool CopyTilesTo(FastGridMap grid)
        {
            if (grid == null || !HasLoadableTiles()) return false;
            if (grid.Width != width || grid.Height != height) return false;
            short[] destination = grid.RawData;
            for (int index = GameConfig.Runtime.FirstIndex; index < destination.Length; index++)
                destination[index] = (short)tiles[index];
            return true;
        }
    }
}
