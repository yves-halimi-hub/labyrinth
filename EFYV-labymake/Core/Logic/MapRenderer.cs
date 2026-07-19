using System;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    // Item #5: host-agnostic map-surface compositor for the shell's map
    // editing mode. Renders a MapSection into a flat RGBA canvas at one
    // TileSize-square cell per map cell:
    //   - a cell whose id addresses a tileset tile blits that tile's pixels;
    //   - a non-blank id OUTSIDE the tileset (external/absent tileset) fills
    //     the cell with a deterministic id-derived placeholder color, so
    //     painting stays visible before the tileset exists;
    //   - blank cells (ids below the runtime palette floor) stay transparent
    //     (the host's checkerboard shows through).
    public static class MapRenderer
    {
        // The cell edge used for rendering: the tileset's TileSize, or the
        // default tile size when the map has no tileset yet.
        public static int GetCellSize(TilesetSection tileset)
        {
            return tileset?.TileSize ?? Config.Tileset.DefaultTileSize;
        }

        public static void GetSurfaceSize(
            MapSection map,
            TilesetSection tileset,
            out int width,
            out int height)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int cellSize = GetCellSize(tileset);
            width = checked(map.Grid.Width * cellSize);
            height = checked(map.Grid.Height * cellSize);
        }

        public static void Render(MapSection map, TilesetSection tileset, uint[] destination)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            GetSurfaceSize(map, tileset, out int surfaceWidth, out int surfaceHeight);
            if (destination.Length != checked(surfaceWidth * surfaceHeight))
                throw new ArgumentException(nameof(destination));

            int cellSize = GetCellSize(tileset);
            int tileCount = tileset?.Tiles.Count ?? Config.Common.EmptyCount;
            short[] tiles = map.Grid.RawData;
            int mapWidth = map.Grid.Width;
            int mapHeight = map.Grid.Height;

            Array.Clear(destination, Config.Common.FirstIndex, destination.Length);
            for (int cellY = Config.Common.FirstIndex; cellY < mapHeight; cellY++)
            {
                int cellRow = cellY * mapWidth;
                for (int cellX = Config.Common.FirstIndex; cellX < mapWidth; cellX++)
                {
                    short tileId = tiles[cellRow + cellX];
                    if (tileId < Config.Common.FirstIndex) continue;

                    int originX = cellX * cellSize;
                    int originY = cellY * cellSize;
                    if (tileId < tileCount)
                    {
                        uint[] pixels = tileset.Tiles[tileId].Pixels;
                        for (int y = Config.Common.FirstIndex; y < cellSize; y++)
                        {
                            int sourceRow = y * cellSize;
                            int destinationRow = ((originY + y) * surfaceWidth) + originX;
                            for (int x = Config.Common.FirstIndex; x < cellSize; x++)
                                destination[destinationRow + x] = pixels[sourceRow + x];
                        }
                    }
                    else
                    {
                        uint placeholder = GetPlaceholderColor(tileId);
                        for (int y = Config.Common.FirstIndex; y < cellSize; y++)
                        {
                            int destinationRow = ((originY + y) * surfaceWidth) + originX;
                            for (int x = Config.Common.FirstIndex; x < cellSize; x++)
                                destination[destinationRow + x] = placeholder;
                        }
                    }
                }
            }
        }

        // Deterministic opaque placeholder for ids without tileset pixels:
        // the shared FNV hash spread over RGB, forced opaque.
        public static uint GetPlaceholderColor(short tileId)
        {
            uint hashed = (uint)EFYVBackend.Core.Math.FastMath.FastHash(
                tileId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return hashed | ((uint)EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.ColorMaxByte
                << Config.Color.AlphaShift);
        }
    }
}
