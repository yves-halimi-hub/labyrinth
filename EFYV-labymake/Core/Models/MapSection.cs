using System;
using EFYVLabyMake.Core.IO;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    // Item #5: the designer-side map section of a project - a FastGridMap of
    // short tile ids (BlankTileId = empty cell) plus prop placements, and the
    // identity of the tileset whose exported sprites render it. MapId is the
    // export stem of the versioned .efyvmap binary and is fixed at creation
    // (it is validated as a safe file stem, exactly like project names).
    // Hosts mutate tiles/props through the undoable DesignerSession map
    // editing surface; persisted in .efyvmake.
    public sealed class MapSection
    {
        private string tilesetName;

        public string MapId { get; }

        // Identity of the tileset asset this map renders with (the assetName
        // of a tileset .efyvlaby export). Empty = none; non-empty values must
        // be safe file stems. Mutate through DesignerSession.SetMapTilesetName
        // so the change is undoable.
        public string TilesetName
        {
            get => tilesetName;
            internal set
            {
                string normalized = value ?? Config.Common.EmptyString;
                if (normalized.Length > Config.Common.EmptyCount &&
                    !DesignerPathPolicy.IsSafeFileStem(normalized))
                    throw new ArgumentException(nameof(value));
                tilesetName = normalized;
            }
        }

        // The tile grid + prop placements. The grid instance is stable for
        // the section's lifetime (undo commands hold cell diffs against it).
        public EFYVBackend.Core.Collections.FastGridMap Grid { get; }

        public MapSection(string mapId, int width, int height, string tilesetName)
        {
            if (!DesignerPathPolicy.IsSafeFileStem(mapId)) throw new ArgumentException(nameof(mapId));
            if (width <= Config.Common.EmptyCount || width > Config.MapDocument.MaxDimension)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= Config.Common.EmptyCount || height > Config.MapDocument.MaxDimension)
                throw new ArgumentOutOfRangeException(nameof(height));

            MapId = mapId;
            TilesetName = tilesetName;
            Grid = new EFYVBackend.Core.Collections.FastGridMap(width, height);
            // Fresh maps start blank, not as palette tile 0: BlankTileId is
            // below the runtime palette floor, so untouched cells render as
            // empty space in both the shell preview and the game.
            Grid.FillRect(
                Config.Common.FirstIndex,
                Config.Common.FirstIndex,
                width,
                height,
                Config.MapDocument.BlankTileId);
        }
    }
}
