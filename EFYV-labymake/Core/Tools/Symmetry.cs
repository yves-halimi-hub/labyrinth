using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    // Mirror mode applied by the drawing tools. Horizontal mirrors across the
    // canvas's vertical center axis (left/right copies), Vertical across the
    // horizontal center axis (top/bottom copies), Both produces all four
    // quadrant copies. Mirroring maps x to width-1-x (and y likewise), so the
    // canvas center of an odd dimension maps onto itself; drawing the same
    // pixel twice is idempotent for the direct-write stroke/shape/fill paths.
    public enum SymmetryMode
    {
        None,
        Horizontal,
        Vertical,
        Both
    }

    internal static class SymmetryVariants
    {
        // Enumerates the mirrored copies of a segment: variant 0 is always the
        // original; bits select an X and/or Y flip. Returns false for variants
        // the current mode does not produce.
        internal static bool TryGetVariant(
            SymmetryMode mode,
            int variant,
            int width,
            int height,
            int x0,
            int y0,
            int x1,
            int y1,
            out int variantX0,
            out int variantY0,
            out int variantX1,
            out int variantY1)
        {
            bool flipX = (variant & Config.Tool.Symmetry.FlipXBit) != Config.Common.EmptyCount;
            bool flipY = (variant & Config.Tool.Symmetry.FlipYBit) != Config.Common.EmptyCount;
            bool mirrorX = mode == SymmetryMode.Horizontal || mode == SymmetryMode.Both;
            bool mirrorY = mode == SymmetryMode.Vertical || mode == SymmetryMode.Both;
            if ((flipX && !mirrorX) || (flipY && !mirrorY))
            {
                variantX0 = variantY0 = variantX1 = variantY1 = Config.Common.EmptyCount;
                return false;
            }

            variantX0 = flipX ? width - Config.Common.UnitCount - x0 : x0;
            variantX1 = flipX ? width - Config.Common.UnitCount - x1 : x1;
            variantY0 = flipY ? height - Config.Common.UnitCount - y0 : y0;
            variantY1 = flipY ? height - Config.Common.UnitCount - y1 : y1;
            return true;
        }
    }

    // Shared stroke rasterizer for the pencil and eraser: one segment (plus its
    // symmetry variants) with the exact thin/thick dispatch the pencil has
    // always used - the effective brush is capped by MAX(width, height) and a
    // 1-pixel effective brush takes the thin Bresenham path.
    internal static class StrokeRenderer
    {
        internal static unsafe void DrawSegment(
            Layer layer,
            SymmetryMode symmetry,
            int x0,
            int y0,
            int x1,
            int y1,
            uint rgba,
            int brushSize,
            EFYVBackend.Core.Math.Algorithms.BrushShape shape)
        {
            int effectiveBrushSize = EFYVBackend.Core.Math.FastMath.FastMin(
                brushSize,
                EFYVBackend.Core.Math.FastMath.FastMax(layer.Width, layer.Height));

            // Pin the memory ONCE for all mirror variants of the segment.
            fixed (PixelColor* ptr = layer.Pixels)
            {
                for (int variant = Config.Common.FirstIndex;
                    variant < Config.Tool.Symmetry.VariantCount;
                    variant++)
                {
                    int variantX0, variantY0, variantX1, variantY1;
                    if (!SymmetryVariants.TryGetVariant(
                        symmetry, variant, layer.Width, layer.Height,
                        x0, y0, x1, y1,
                        out variantX0, out variantY0, out variantX1, out variantY1))
                        continue;

                    if (effectiveBrushSize <= Config.Tool.Pencil.MinThickBrushSize)
                    {
                        EFYVBackend.Core.Math.Algorithms.DrawLineBresenham(
                            (uint*)ptr,
                            layer.Width,
                            layer.Height,
                            variantX0, variantY0, variantX1, variantY1,
                            rgba);
                    }
                    else
                    {
                        EFYVBackend.Core.Math.Algorithms.DrawThickLineBresenham(
                            (uint*)ptr,
                            layer.Width,
                            layer.Height,
                            variantX0, variantY0, variantX1, variantY1,
                            rgba,
                            effectiveBrushSize,
                            shape);
                    }
                }
            }
        }
    }
}
