using System;
using EFYVLabyMake.Core.IO;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    // Item #6: one per-frame sub-element attachment - a REFERENCE to a bank
    // sub-element (by name) placed on the canvas, instead of pixels baked
    // into a layer. X/Y is the canvas-space position the sub-element's PIVOT
    // lands on; ZOrder orders attachments within the frame (ascending, ties
    // keep list order); the flips mirror the sub-element around its own grid.
    //
    // The NAME is the immutable identity (it must stay a safe file stem - it
    // addresses a .efyvsub file in the asset bank); position/z-order/flips
    // are mutable so the stamp tool can reposition an attachment inside one
    // gesture. Undo safety comes from FrameEditCapture/FrameEditCommand deep-
    // copying the whole attachment list, never from immutability here.
    public sealed class SubElementAttachment
    {
        private int zOrder;

        public string SubElementName { get; }
        public int X { get; set; }
        public int Y { get; set; }

        public int ZOrder
        {
            get => zOrder;
            set
            {
                if (value < Config.Attachment.MinZOrder || value > Config.Attachment.MaxZOrder)
                    throw new ArgumentOutOfRangeException(nameof(value));
                zOrder = value;
            }
        }

        public bool FlipX { get; set; }
        public bool FlipY { get; set; }

        public SubElementAttachment(
            string subElementName,
            int x,
            int y,
            int zOrder,
            bool flipX,
            bool flipY)
        {
            if (!DesignerPathPolicy.IsSafeFileStem(subElementName))
                throw new ArgumentException(nameof(subElementName));
            if (zOrder < Config.Attachment.MinZOrder || zOrder > Config.Attachment.MaxZOrder)
                throw new ArgumentOutOfRangeException(nameof(zOrder));

            SubElementName = subElementName;
            X = x;
            Y = y;
            this.zOrder = zOrder;
            FlipX = flipX;
            FlipY = flipY;
        }

        public SubElementAttachment Clone()
        {
            return new SubElementAttachment(SubElementName, X, Y, ZOrder, FlipX, FlipY);
        }

        // Grab test used by the stamp tool: within the per-axis tolerance of
        // the attachment's anchor point.
        public bool IsNearAnchor(int canvasX, int canvasY, int radius)
        {
            int deltaX = canvasX - X;
            int deltaY = canvasY - Y;
            if (deltaX < -radius || deltaX > radius) return false;
            return deltaY >= -radius && deltaY <= radius;
        }
    }
}
