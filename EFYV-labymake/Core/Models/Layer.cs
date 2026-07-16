using System;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = Config.Layout.StructPack)]
    public struct PixelColor
    {
        public uint Rgba;

        public byte R { get => (byte)(Rgba & Config.Color.ChannelMask); set => Rgba = (Rgba & Config.Color.ClearRedMask) | value; }
        public byte G { get => (byte)((Rgba >> Config.Color.GreenShift) & Config.Color.ChannelMask); set => Rgba = (Rgba & Config.Color.ClearGreenMask) | (uint)(value << Config.Color.GreenShift); }
        public byte B { get => (byte)((Rgba >> Config.Color.BlueShift) & Config.Color.ChannelMask); set => Rgba = (Rgba & Config.Color.ClearBlueMask) | (uint)(value << Config.Color.BlueShift); }
        public byte A { get => (byte)((Rgba >> Config.Color.AlphaShift) & Config.Color.ChannelMask); set => Rgba = (Rgba & Config.Color.ClearAlphaMask) | (uint)(value << Config.Color.AlphaShift); }

        public bool IsTransparent => A == Config.Layer.TransparentAlpha;

        public PixelColor() => Rgba = Config.Color.TransparentPixelRgba;
        
    }

    public sealed class Layer
    {
        private EFYVBackend.Core.Models.LayerData Data;

        public string Name { get; set; }
        
        public bool IsVisible
        {
            get => Data.IsVisible;
            set => Data.IsVisible = value;
        }
        
        public float Opacity
        {
            get => Data.Opacity;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                if (value <= Config.Common.ZeroFloat) Data.Opacity = Config.Common.ZeroFloat;
                else if (value >= Config.Common.UnitScale) Data.Opacity = Config.Common.UnitScale;
                else Data.Opacity = value;
            }
        }
        
        public int Width
        {
            get => Data.Width;
            private set => Data.Width = value;
        }
        
        public int Height
        {
            get => Data.Height;
            private set => Data.Height = value;
        }

        // PERFORMANCE AUDIT: Changed from 2D Array [,] to 1D Array []
        // 1D arrays are stored continuously in RAM, ensuring massive CPU Cache locality boosts 
        // when iterating through millions of pixels during the flattening phase.
        public PixelColor[] Pixels { get; private set; }

        public Layer(string name, int width, int height)
        {
            if (width <= Config.Canvas.MinCoordinate) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= Config.Canvas.MinCoordinate) throw new ArgumentOutOfRangeException(nameof(height));

            Data = new EFYVBackend.Core.Models.LayerData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
            Name = name;
            Pixels = new PixelColor[width * height];
            Data.IsVisible = Config.Layer.DefaultVisibility;
            Data.Opacity = Config.Layer.DefaultOpacity;
            Data.Width = width;
            Data.Height = height;
        }

        // REUSABILITY & PERFORMANCE: Uses our C-Level memset to instantly erase a layer
        public void Clear()
        {
            EFYVBackend.Core.Memory.FastMemory.Clear(Pixels);
        }

        // REUSABILITY & PERFORMANCE: Uses our C-Level memcpy to instantly duplicate a layer
        public Layer Clone()
        {
            return Clone(Name + Config.Layer.CopySuffix);
        }

        public Layer Clone(string name)
        {
            Layer newLayer = new Layer(name, Width, Height);
            newLayer.IsVisible = IsVisible;
            newLayer.Opacity = Opacity;
            EFYVBackend.Core.Memory.FastMemory.Copy(this.Pixels, newLayer.Pixels);
            return newLayer;
        }

        public void CopyPixelsFrom(PixelColor[] pixels)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != Pixels.Length) throw new ArgumentException(nameof(pixels));
            EFYVBackend.Core.Memory.FastMemory.Copy(pixels, Pixels);
        }

        // Helper for setting pixels easily using math
        public void SetPixel(int x, int y, PixelColor color)
        {
            // We do a single boundary check here for safety when drawing
            if (x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate || x >= Width || y >= Height) return;
            
            // MIGRATION: Extracted to backend.
            // Bypasses the C# Array boundary checking when writing to memory.
            EFYVBackend.Core.Memory.FastMemory.Write2DArrayUnsafe(ref Pixels[Config.Common.FirstIndex], Width, x, y, color);
        }

        public PixelColor GetPixel(int x, int y)
        {
            if (x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate || x >= Width || y >= Height)
                return new PixelColor { Rgba = Config.Color.TransparentPixelRgba };
            
            // MIGRATION: Extracted to backend.
            return EFYVBackend.Core.Memory.FastMemory.Read2DArrayUnsafe(ref Pixels[Config.Common.FirstIndex], Width, x, y);
        }
    }
}
