using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    // Gesture-preview shape tools: pointer-down anchors the shape and snapshots
    // the active layer; every drag restores that snapshot and re-rasterizes the
    // shape from the anchor to the (canvas-clamped) pointer, so the in-progress
    // shape is previewed live without accumulating strokes; pointer-up renders
    // the final shape, which the session's sparse pixel diff commits as ONE
    // undoable command. Mirror mode re-rasterizes the shape once per variant.
    public abstract class ShapeTool : ColorLayerTool
    {
        private PixelColor[] baseline;
        private Layer target;
        private bool active;

        public SymmetryMode Symmetry { get; set; }

        // Whether the interior is filled; outline thickness applies otherwise.
        // The LineTool ignores this flag.
        public bool Filled { get; set; }

        public int Thickness
        {
            get => Data.BrushSize;
            set
            {
                if (value < Config.Tool.Shape.DefaultThickness)
                    Data.BrushSize = Config.Tool.Shape.DefaultThickness;
                else if (value > Config.Tool.Shape.MaxThickness)
                    Data.BrushSize = Config.Tool.Shape.MaxThickness;
                else
                    Data.BrushSize = value;
            }
        }

        private int anchorX
        {
            get => Data.LastX;
            set => Data.LastX = value;
        }
        private int anchorY
        {
            get => Data.LastY;
            set => Data.LastY = value;
        }

        protected ShapeTool()
        {
            Thickness = Config.Tool.Shape.DefaultThickness;
            Filled = Config.Tool.Shape.DefaultFilled;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            active = false;
            target = null;
            Layer layer;
            if (!TryGetLayer(currentFrame, ActiveLayerIndex, out layer) ||
                x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate ||
                x >= layer.Width || y >= layer.Height) return;

            target = layer;
            if (baseline == null || baseline.Length != layer.Pixels.Length)
                baseline = new PixelColor[layer.Pixels.Length];
            EFYVBackend.Core.Memory.FastMemory.Copy(layer.Pixels, baseline);
            anchorX = x;
            anchorY = y;
            active = true;
            RenderPreview(x, y);
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (!active) return;
            RenderPreview(x, y);
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (!active) return;
            RenderPreview(x, y);
            active = false;
            target = null;
        }

        private void RenderPreview(int pointerX, int pointerY)
        {
            // Restore the pre-gesture pixels, then rasterize the current shape.
            EFYVBackend.Core.Memory.FastMemory.Copy(baseline, target.Pixels);

            int endX = EFYVBackend.Core.Math.FastMath.FastClamp(
                pointerX, Config.Canvas.MinCoordinate, target.Width - Config.Common.UnitCount);
            int endY = EFYVBackend.Core.Math.FastMath.FastClamp(
                pointerY, Config.Canvas.MinCoordinate, target.Height - Config.Common.UnitCount);

            for (int variant = Config.Common.FirstIndex;
                variant < Config.Tool.Symmetry.VariantCount;
                variant++)
            {
                int variantX0, variantY0, variantX1, variantY1;
                if (!SymmetryVariants.TryGetVariant(
                    Symmetry, variant, target.Width, target.Height,
                    anchorX, anchorY, endX, endY,
                    out variantX0, out variantY0, out variantX1, out variantY1))
                    continue;
                DrawShape(target, variantX0, variantY0, variantX1, variantY1);
            }
        }

        // Rasterizes one (already mirrored) shape instance onto the layer.
        protected abstract void DrawShape(Layer layer, int x0, int y0, int x1, int y1);
    }

    public sealed class LineTool : ShapeTool
    {
        protected override void DrawShape(Layer layer, int x0, int y0, int x1, int y1)
        {
            // Symmetry is handled by the ShapeTool preview loop, so the segment
            // renders exactly once here.
            StrokeRenderer.DrawSegment(
                layer,
                SymmetryMode.None,
                x0, y0, x1, y1,
                CurrentColor.Rgba,
                Thickness,
                Config.Tool.Pencil.DefaultBrushShape);
        }
    }

    public sealed class RectangleTool : ShapeTool
    {
        protected override void DrawShape(Layer layer, int x0, int y0, int x1, int y1)
        {
            unsafe
            {
                fixed (PixelColor* ptr = layer.Pixels)
                {
                    EFYVBackend.Core.Math.Algorithms.DrawRectangle(
                        (uint*)ptr,
                        layer.Width,
                        layer.Height,
                        x0, y0, x1, y1,
                        CurrentColor.Rgba,
                        Thickness,
                        Filled);
                }
            }
        }
    }

    public sealed class EllipseTool : ShapeTool
    {
        protected override void DrawShape(Layer layer, int x0, int y0, int x1, int y1)
        {
            unsafe
            {
                fixed (PixelColor* ptr = layer.Pixels)
                {
                    EFYVBackend.Core.Math.Algorithms.DrawEllipse(
                        (uint*)ptr,
                        layer.Width,
                        layer.Height,
                        x0, y0, x1, y1,
                        CurrentColor.Rgba,
                        Thickness,
                        Filled);
                }
            }
        }
    }
}
