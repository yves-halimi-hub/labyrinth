using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    // Item #6: the stamp tool's two behaviors. PlaceAttachment (the default)
    // records a repositionable per-frame ATTACHMENT referencing the active
    // sub-element; BakePixels is the legacy behavior that blends the
    // sub-element's pixels destructively into the active layer.
    public enum StampToolMode
    {
        PlaceAttachment = 0,
        BakePixels = 1
    }

    public sealed class StampTool : Tool, ILayerTool
    {
        private EFYVBackend.Core.Models.BrushToolData Data = new EFYVBackend.Core.Models.BrushToolData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        // Attachment-mode drag state: the attachment grabbed (or just placed)
        // by the current gesture and the pointer-to-anchor offset that keeps
        // a grabbed attachment from jumping under the cursor.
        private SubElementAttachment draggedAttachment;
        private int dragOffsetX;
        private int dragOffsetY;

        public SubElement ActiveSubElement { get; set; }
        public StampToolMode Mode { get; set; }
        public int ActiveLayerIndex
        {
            get => Data.ActiveLayerIndex;
            set => Data.ActiveLayerIndex = value;
        }

        public StampTool()
        {
            ActiveLayerIndex = Config.Tool.DefaultLayerIndex;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            draggedAttachment = null;
            if (currentFrame == null) return;
            if (Mode == StampToolMode.BakePixels)
            {
                BakePixels(currentFrame, x, y);
                return;
            }

            // Grab an existing attachment near the pointer (topmost = latest
            // in list order) to reposition it; grabbing needs no active
            // sub-element - repositioning must work regardless of selection.
            for (int index = currentFrame.Attachments.Count - Config.Common.UnitCount;
                index >= Config.Common.FirstIndex;
                index--)
            {
                SubElementAttachment candidate = currentFrame.Attachments[index];
                if (candidate == null || !candidate.IsNearAnchor(x, y, Config.Attachment.GrabRadius))
                    continue;
                draggedAttachment = candidate;
                dragOffsetX = candidate.X - x;
                dragOffsetY = candidate.Y - y;
                return;
            }

            // Place a NEW attachment seeded with the sub-element's default
            // transform. Silently a no-op without an active sub-element or at
            // the per-frame cap (tools never throw for unusable input).
            if (ActiveSubElement == null ||
                currentFrame.Attachments.Count >= Config.Attachment.MaxPerFrame)
                return;
            var placed = new SubElementAttachment(
                ActiveSubElement.Name,
                x + ActiveSubElement.DefaultOffsetX,
                y + ActiveSubElement.DefaultOffsetY,
                ActiveSubElement.DefaultZOrder,
                ActiveSubElement.DefaultFlipX,
                ActiveSubElement.DefaultFlipY);
            currentFrame.Attachments.Add(placed);
            draggedAttachment = placed;
            dragOffsetX = ActiveSubElement.DefaultOffsetX;
            dragOffsetY = ActiveSubElement.DefaultOffsetY;
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (Mode != StampToolMode.PlaceAttachment || draggedAttachment == null) return;
            draggedAttachment.X = x + dragOffsetX;
            draggedAttachment.Y = y + dragOffsetY;
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            draggedAttachment = null;
        }

        // Legacy destructive stamping (BakePixels mode). The blit's top-left
        // is the pointer minus the sub-element's PIVOT; a default (center)
        // pivot reproduces the historical centering exactly.
        private void BakePixels(Frame currentFrame, int x, int y)
        {
            Layer targetLayer;
            if (ActiveSubElement == null || !TryGetLayer(currentFrame, ActiveLayerIndex, out targetLayer)) return;

            // Use the Backend's ultra-fast stamp blitting engine
            unsafe
            {
                fixed (uint* srcPtr = ActiveSubElement.Pixels)
                fixed (PixelColor* destPtr = targetLayer.Pixels)
                {
                    int stampX = x - ActiveSubElement.PivotX;
                    int stampY = y - ActiveSubElement.PivotY;

                    EFYVBackend.Core.Memory.FastMemory.StampBlit(
                        srcPtr, ActiveSubElement.Width, ActiveSubElement.Height,
                        (uint*)destPtr, targetLayer.Width, targetLayer.Height,
                        stampX, stampY
                    );
                }
            }
        }
    }
}
