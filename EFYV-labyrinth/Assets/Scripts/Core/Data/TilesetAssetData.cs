using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Data
{
    // Item #5: an imported tileset - a GameAssetData whose source .efyvlaby
    // carried the tile-ID manifest block. The importer fills tileSprites with
    // the sliced tile-sheet sprites IN TILE-ID ORDER (slice i = FastGridMap
    // short tile id i), so tileSprites can feed
    // MapViewportController.tilePalette directly.
    public class TilesetAssetData : GameAssetData
    {
        [Header(GameConfig.DataConfig.HeaderMapSettings)]
        public int tileSize;
        public string[] tileNames;
        public Sprite[] tileSprites;
    }
}
