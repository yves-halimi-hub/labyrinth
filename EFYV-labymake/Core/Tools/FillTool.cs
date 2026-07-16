using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    public sealed class FillTool : ColorLayerTool
    {
        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Layer targetLayer;
            if (!TryGetLayer(currentFrame, ActiveLayerIndex, out targetLayer) ||
                x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate ||
                x >= targetLayer.Width || y >= targetLayer.Height) return;

            // Trigger the heavily optimized C-Level flood fill
            UnsafeFloodFill(targetLayer, x, y, CurrentColor);
        }

        // ALGORITHMIC AUDIT: Fast Pointer-Based Flood Fill (Bucket Tool)
        private unsafe void UnsafeFloodFill(Layer layer, int startX, int startY, PixelColor targetColor)
        {
            fixed (PixelColor* ptr = layer.Pixels)
            {
                // MIGRATION: Handed the heavy stackalloc recursive tracking to the Backend Engine.
                EFYVBackend.Core.Math.Algorithms.FloodFill(
                    (uint*)ptr,
                    layer.Width,
                    layer.Height,
                    startX,
                    startY,
                    targetColor.Rgba
                );
            }
        }
    }
}
