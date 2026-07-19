using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    public sealed class FillTool : ColorLayerTool
    {
        public SymmetryMode Symmetry { get; set; }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Layer targetLayer;
            if (!TryGetLayer(currentFrame, ActiveLayerIndex, out targetLayer) ||
                x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate ||
                x >= targetLayer.Width || y >= targetLayer.Height) return;

            // Trigger the heavily optimized C-Level flood fill; mirror mode
            // seeds an additional fill at each mirrored coordinate (a seed whose
            // region was already recolored is a backend no-op).
            for (int variant = Config.Common.FirstIndex;
                variant < Config.Tool.Symmetry.VariantCount;
                variant++)
            {
                int seedX, seedY, unusedX, unusedY;
                if (!SymmetryVariants.TryGetVariant(
                    Symmetry, variant, targetLayer.Width, targetLayer.Height,
                    x, y, x, y,
                    out seedX, out seedY, out unusedX, out unusedY))
                    continue;
                UnsafeFloodFill(targetLayer, seedX, seedY, CurrentColor);
            }
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
