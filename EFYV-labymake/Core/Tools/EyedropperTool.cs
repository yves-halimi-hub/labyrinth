using System;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    // Eyedropper: samples the COMPOSITED frame color - the exact value
    // Frame.FlattenLayers produces at the picked pixel (visibility, layer
    // opacity, and alpha blending all applied). This is a deliberate,
    // documented choice over active-layer sampling: the designer picks what
    // they SEE on the canvas, matching mainstream pixel editors. Sampling a
    // fully transparent area therefore yields the transparent dword.
    //
    // The tool never mutates pixels and implements neither ILayerTool nor
    // IColorTool, so a pick gesture captures no layer, produces an empty
    // diff, and records no history. Down and drag both re-sample (live
    // preview); pointer-up takes the final sample and raises ColorPicked
    // exactly once per gesture that touched the canvas. Off-canvas samples
    // are ignored and keep the previous PickedColor.
    public sealed class EyedropperTool : Tool
    {
        public PixelColor PickedColor { get; private set; }

        // True once the current/last gesture landed at least one on-canvas
        // sample; a gesture entirely outside the canvas raises no event.
        public bool HasSample { get; private set; }

        public event Action<PixelColor> ColorPicked;

        public EyedropperTool()
        {
            PickedColor = new PixelColor { Rgba = Config.Color.TransparentPixelRgba };
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            HasSample = false;
            Sample(currentFrame, x, y);
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Sample(currentFrame, x, y);
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Sample(currentFrame, x, y);
            if (!HasSample) return;
            ColorPicked?.Invoke(PickedColor);
        }

        private void Sample(Frame frame, int x, int y)
        {
            PixelColor color;
            if (!TrySampleComposited(frame, x, y, out color)) return;
            PickedColor = color;
            HasSample = true;
        }

        // Single-pixel replica of Frame.FlattenLayers: same layer walk order,
        // same visibility/zero-opacity skip, same dimension contract, and the
        // blend itself goes through the SAME backend BlendLayer entry point
        // (per-layer opacity byte rounding included), so a pick is bit-exact
        // with the flattened composite.
        public static unsafe bool TrySampleComposited(Frame frame, int x, int y, out PixelColor color)
        {
            color = new PixelColor { Rgba = Config.Color.TransparentPixelRgba };
            if (frame == null ||
                x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate ||
                x >= frame.Width || y >= frame.Height)
                return false;

            int totalPixels = checked(frame.Width * frame.Height);
            uint composited = Config.Color.TransparentPixelRgba;
            foreach (Layer layer in frame.Layers)
            {
                if (layer == null || !layer.IsVisible || layer.Opacity <= Config.Common.ZeroFloat)
                    continue;
                if (layer.Width != frame.Width || layer.Height != frame.Height ||
                    layer.Pixels.Length != totalPixels)
                    throw new InvalidOperationException();

                uint source = layer.GetPixel(x, y).Rgba;
                EFYVBackend.Core.Memory.FastMemory.BlendLayer(
                    &composited,
                    &source,
                    Config.Common.UnitCount,
                    Config.Layer.TransparentAlpha,
                    layer.Opacity);
            }

            color = new PixelColor { Rgba = composited };
            return true;
        }
    }
}
