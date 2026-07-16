using System;

namespace EFYVLabyMake.Core.Models
{
    // A reusable piece of pixel art that can be stamped onto larger projects
    public sealed class SubElement
    {
        private EFYVBackend.Core.Models.SubElementData Data;

        public string Name 
        { 
            get => Data.Name; 
            set => Data.Name = value; 
        }
        
        public int Width 
        { 
            get => Data.Width; 
            set => Data.Width = value; 
        }
        
        public int Height 
        { 
            get => Data.Height; 
            set => Data.Height = value; 
        }
        
        // Stored as an array of 32-bit RGBA colors
        public uint[] Pixels { get; }

        public SubElement(string name, int width, int height, uint[] pixels)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != checked(width * height)) throw new ArgumentException(nameof(pixels));

            Data = new EFYVBackend.Core.Models.SubElementData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
            Data.Name = name;
            Data.Width = width;
            Data.Height = height;
            Pixels = (uint[])pixels.Clone();
        }
    }
}
