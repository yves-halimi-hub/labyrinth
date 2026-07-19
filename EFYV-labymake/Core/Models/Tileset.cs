using System;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    // Item #5: one designed tile - a named mini-frame of raw RGBA pixels at
    // the owning tileset's TileSize. The constructor is the validation gate
    // (like SubElementAttachment/EffectDescriptor): a TilesetTile instance is
    // well-formed by construction. The tile's LIST INDEX inside its
    // TilesetSection is its FastGridMap short tile id - reordering or
    // removing tiles therefore re-addresses every map cell painted with the
    // shifted ids (documented tileset-editing hazard, matching the exported
    // tile-ID manifest semantics).
    public sealed class TilesetTile
    {
        private string name;

        public string Name
        {
            get => name;
            set
            {
                if (string.IsNullOrWhiteSpace(value) ||
                    value.Length > Config.Tileset.MaxTileNameLength)
                    throw new ArgumentException(nameof(value));
                name = value;
            }
        }

        // Row-major straight RGBA, exactly TileSize * TileSize entries. The
        // array is owned by the tile; callers hand over or copy explicitly.
        public uint[] Pixels { get; }

        public int TileSize { get; }

        public TilesetTile(string name, int tileSize)
            : this(name, tileSize, null)
        {
        }

        public TilesetTile(string name, int tileSize, uint[] pixels)
        {
            if (tileSize < Config.Tileset.MinTileSize || tileSize > Config.Tileset.MaxTileSize)
                throw new ArgumentOutOfRangeException(nameof(tileSize));
            Name = name;
            TileSize = tileSize;
            int pixelCount = checked(tileSize * tileSize);
            if (pixels == null)
            {
                Pixels = new uint[pixelCount];
            }
            else
            {
                if (pixels.Length != pixelCount) throw new ArgumentException(nameof(pixels));
                Pixels = new uint[pixelCount];
                Array.Copy(pixels, Pixels, pixelCount);
            }
        }

        public TilesetTile Clone()
        {
            return new TilesetTile(Name, TileSize, Pixels);
        }
    }

    // Item #5: the designer-side tileset section of a project - a list of N
    // tiles all sharing one TileSize (fixed at creation; the exported
    // tile-sheet slices into TileSize-square sprites). Hosts mutate through
    // the undoable DesignerSession tileset CRUD; persisted in .efyvmake and
    // exported as tile-sheet PNG + tile-ID manifest in a .efyvlaby.
    public sealed class TilesetSection
    {
        public int TileSize { get; }
        public System.Collections.Generic.List<TilesetTile> Tiles { get; }

        public TilesetSection(int tileSize)
        {
            if (tileSize < Config.Tileset.MinTileSize || tileSize > Config.Tileset.MaxTileSize)
                throw new ArgumentOutOfRangeException(nameof(tileSize));
            TileSize = tileSize;
            Tiles = new System.Collections.Generic.List<TilesetTile>();
        }
    }
}
