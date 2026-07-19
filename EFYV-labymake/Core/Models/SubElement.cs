using System;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    // A reusable piece of pixel art that can be stamped onto larger projects.
    //
    // Item #6: a sub-element also carries a PIVOT (the anchor pixel that
    // placement coordinates address; defaults to the center, matching the
    // legacy stamp centering) and a DEFAULT TRANSFORM (offset applied when a
    // new attachment is placed, its starting z-order, and optional flips).
    // The pivot/transform ride in the .efyvsub version-2 header; version-1
    // files load with these defaults.
    public sealed class SubElement
    {
        private EFYVBackend.Core.Models.SubElementData Data;
        private int pivotX;
        private int pivotY;
        private int defaultZOrder;

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

        // Anchor pixel inside the sub-element's own grid. Placement (stamp
        // bake, attachment flatten) puts THIS pixel at the addressed canvas
        // position. Validated against the construction-time dimensions.
        public int PivotX
        {
            get => pivotX;
            set
            {
                if (value < Config.Canvas.MinCoordinate || value >= Width)
                    throw new ArgumentOutOfRangeException(nameof(value));
                pivotX = value;
            }
        }

        public int PivotY
        {
            get => pivotY;
            set
            {
                if (value < Config.Canvas.MinCoordinate || value >= Height)
                    throw new ArgumentOutOfRangeException(nameof(value));
                pivotY = value;
            }
        }

        // Default transform seeded into NEW attachments of this sub-element:
        // the offset shifts the placement away from the pointer, z-order
        // starts the attachment's layering, and the flips mirror the pixels.
        public int DefaultOffsetX { get; set; }
        public int DefaultOffsetY { get; set; }

        public int DefaultZOrder
        {
            get => defaultZOrder;
            set
            {
                if (value < Config.Attachment.MinZOrder || value > Config.Attachment.MaxZOrder)
                    throw new ArgumentOutOfRangeException(nameof(value));
                defaultZOrder = value;
            }
        }

        public bool DefaultFlipX { get; set; }
        public bool DefaultFlipY { get; set; }

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
            // Center pivot by default - the exact legacy stamp centering
            // (pointer - size/2), so default-pivot elements behave as before.
            pivotX = EFYVBackend.Core.Math.FastMath.FastDivPow2(width, Config.Tool.Stamp.CenterDivisorPower);
            pivotY = EFYVBackend.Core.Math.FastMath.FastDivPow2(height, Config.Tool.Stamp.CenterDivisorPower);
            if (pivotX >= width) pivotX = width - Config.Common.UnitCount;
            if (pivotY >= height) pivotY = height - Config.Common.UnitCount;
            defaultZOrder = Config.Attachment.DefaultZOrder;
        }
    }
}
