using System;
using System.Collections.Generic;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    public sealed class Frame
    {
        private EFYVBackend.Core.Models.FrameData Data;

        public int FrameIndex 
        { 
            get => Data.FrameIndex; 
            set => Data.FrameIndex = value; 
        }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public List<Layer> Layers { get; }
        
        // CONTROLLABILITY AUDIT: Replaced single 'CollisionBox' with a generic Dictionary.
        // The artist can now define a "Hurtbox" (where the enemy takes damage) AND an "AttackBox" (e.g. a swinging sword that deals damage) on the exact same frame!
        public Dictionary<string, HitboxData> Hitboxes { get; }

        public Frame(int index)
            : this(Config.Canvas.DefaultWidth, Config.Canvas.DefaultHeight, index)
        {
        }

        public Frame(int width, int height)
            : this(width, height, Config.Frame.DefaultIndex)
        {
        }

        public Frame(int width, int height, int index)
        {
            if (width <= Config.Canvas.MinCoordinate) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= Config.Canvas.MinCoordinate) throw new ArgumentOutOfRangeException(nameof(height));

            Data = new EFYVBackend.Core.Models.FrameData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
            Data.FrameIndex = index;
            Width = width;
            Height = height;
            Layers = new List<Layer> { new Layer(Config.Layer.DefaultName, width, height) };
            Hitboxes = new Dictionary<string, HitboxData>(StringComparer.Ordinal)
            {
                { Config.Hitbox.DefaultKeyHurtbox, new HitboxData() } // Default base hitbox
            };
        }

        public PixelColor[] FlattenLayers(int width, int height)
        {
            if (width != Width) throw new ArgumentException(nameof(width));
            if (height != Height) throw new ArgumentException(nameof(height));
            return FlattenLayers();
        }

        public unsafe PixelColor[] FlattenLayers()
        {
            int totalPixels = checked(Width * Height);
            PixelColor[] flattened = new PixelColor[totalPixels];
            FlattenLayers(flattened);
            return flattened;
        }

        public unsafe void FlattenLayers(PixelColor[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            int totalPixels = checked(Width * Height);
            if (destination.Length != totalPixels) throw new ArgumentException(nameof(destination));

            EFYVBackend.Core.Memory.FastMemory.Clear(destination);

            fixed (PixelColor* destPtr = destination)
            {
                foreach (var layer in Layers)
                {
                    if (!layer.IsVisible || layer.Opacity <= Config.Common.ZeroFloat) continue;
                    if (layer.Width != Width || layer.Height != Height || layer.Pixels.Length != totalPixels)
                        throw new InvalidOperationException();

                    fixed (PixelColor* srcPtr = layer.Pixels)
                    {
                        EFYVBackend.Core.Memory.FastMemory.BlendLayer(
                            (uint*)destPtr,
                            (uint*)srcPtr,
                            totalPixels,
                            Config.Layer.TransparentAlpha,
                            layer.Opacity);
                    }
                }
            }
        }

        public Frame Clone()
        {
            var clone = new Frame(Width, Height, FrameIndex);
            clone.Layers.Clear();
            foreach (var layer in Layers) clone.Layers.Add(layer.Clone(layer.Name));
            clone.CopyHitboxesFrom(this);
            return clone;
        }

        public void CopyHitboxesFrom(Frame source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            Hitboxes.Clear();
            foreach (var pair in source.Hitboxes) Hitboxes.Add(pair.Key, pair.Value);
        }

    }
}
