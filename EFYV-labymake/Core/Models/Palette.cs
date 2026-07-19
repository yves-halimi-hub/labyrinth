using System;
using System.Collections.Generic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    // A named, ordered swatch list persisted inside .efyvmake. Palettes are
    // addressed by index (names are display labels and are NOT required to be
    // unique); swatch order is authoring order and is preserved by
    // persistence. Swatch values are straight RGBA packed like PixelColor
    // (R | G<<8 | B<<16 | A<<24); duplicate swatch colors are allowed.
    public sealed class Palette
    {
        private string name;

        public string Name
        {
            get => name;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(nameof(value));
                if (value.Length > Config.Palette.MaxNameLength)
                    throw new ArgumentException(nameof(value));
                name = value;
            }
        }

        public List<uint> Colors { get; }

        public Palette(string name)
        {
            Name = name;
            Colors = new List<uint>();
        }

        public int FindNearestIndex(uint rgba)
        {
            return FindNearestIndex(Colors, rgba);
        }

        // Palette-constraint distance metric (documented contract): squared
        // Euclidean distance over the four STRAIGHT (non-premultiplied) 8-bit
        // RGBA channels - dR*dR + dG*dG + dB*dB + dA*dA. Alpha participates so
        // translucent working colors snap to translucent entries rather than to
        // an opaque entry with similar hue. Ties break to the LOWEST palette
        // index, so the result is deterministic for duplicate/equidistant
        // swatches. Returns NotFoundIndex for an empty list.
        public static int FindNearestIndex(IReadOnlyList<uint> colors, uint rgba)
        {
            if (colors == null) throw new ArgumentNullException(nameof(colors));

            int bestIndex = Config.Common.NotFoundIndex;
            int bestDistance = int.MaxValue;
            for (int index = Config.Common.FirstIndex; index < colors.Count; index++)
            {
                int distance = SquaredRgbaDistance(colors[index], rgba);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }
            return bestIndex;
        }

        private static int SquaredRgbaDistance(uint left, uint right)
        {
            int deltaR = (int)(left & Config.Color.ChannelMask) -
                (int)(right & Config.Color.ChannelMask);
            int deltaG = (int)((left >> Config.Color.GreenShift) & Config.Color.ChannelMask) -
                (int)((right >> Config.Color.GreenShift) & Config.Color.ChannelMask);
            int deltaB = (int)((left >> Config.Color.BlueShift) & Config.Color.ChannelMask) -
                (int)((right >> Config.Color.BlueShift) & Config.Color.ChannelMask);
            int deltaA = (int)((left >> Config.Color.AlphaShift) & Config.Color.ChannelMask) -
                (int)((right >> Config.Color.AlphaShift) & Config.Color.ChannelMask);
            return deltaR * deltaR + deltaG * deltaG + deltaB * deltaB + deltaA * deltaA;
        }
    }

    // Bounded most-recent-first ring of recently used colors, persisted in
    // .efyvmake alongside the palettes. Push de-duplicates: re-using a color
    // moves it to the front instead of storing it twice, and pushing the color
    // that is already most recent is a no-op (Push returns whether the ring
    // changed, so callers can skip dirty-marking on repeats). Index 0 is the
    // most recently used color.
    public sealed class RecentColorRing
    {
        private readonly List<uint> colors = new List<uint>();
        private readonly int capacity;

        public int Capacity => capacity;
        public int Count => colors.Count;

        public uint this[int index]
        {
            get
            {
                if (index < Config.Common.FirstIndex || index >= colors.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return colors[index];
            }
        }

        public RecentColorRing()
            : this(Config.Palette.RecentColorCapacity)
        {
        }

        public RecentColorRing(int capacity)
        {
            if (capacity <= Config.Common.EmptyCount)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }

        public bool Push(uint rgba)
        {
            int existing = colors.IndexOf(rgba);
            if (existing == Config.Common.FirstIndex) return false;
            if (existing != Config.Common.NotFoundIndex) colors.RemoveAt(existing);
            colors.Insert(Config.Common.FirstIndex, rgba);
            while (colors.Count > capacity) colors.RemoveAt(colors.Count - Config.Common.UnitCount);
            return true;
        }

        public void Clear()
        {
            colors.Clear();
        }

        public uint[] ToArray()
        {
            return colors.ToArray();
        }
    }
}
