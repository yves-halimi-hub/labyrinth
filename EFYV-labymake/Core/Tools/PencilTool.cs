using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    public sealed class PencilTool : ColorLayerTool
    {
        public int BrushSize
        {
            get => Data.BrushSize;
            set
            {
                if (value < Config.Tool.Pencil.DefaultBrushSize)
                    Data.BrushSize = Config.Tool.Pencil.DefaultBrushSize;
                else if (value > Config.Tool.MaxBrushSize)
                    Data.BrushSize = Config.Tool.MaxBrushSize;
                else
                    Data.BrushSize = value;
            }
        }
        public EFYVBackend.Core.Math.Algorithms.BrushShape BrushShape { get; set; }

        private int lastX 
        { 
            get => Data.LastX; 
            set => Data.LastX = value; 
        }
        private int lastY 
        { 
            get => Data.LastY; 
            set => Data.LastY = value; 
        }

        public PencilTool()
        {
            BrushSize = Config.Tool.Pencil.DefaultBrushSize;
            BrushShape = Config.Tool.Pencil.DefaultBrushShape;
            lastX = Config.Tool.InvalidCoordinate;
            lastY = Config.Tool.InvalidCoordinate;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Layer layer;
            if (!TryGetLayer(currentFrame, ActiveLayerIndex, out layer) ||
                x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate ||
                x >= layer.Width || y >= layer.Height) return;

            lastX = x;
            lastY = y;
            DrawPixel(currentFrame, x, y);
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (lastX == Config.Tool.InvalidCoordinate || lastY == Config.Tool.InvalidCoordinate) return;
            Layer layer;
            if (!TryGetLayer(currentFrame, ActiveLayerIndex, out layer) ||
                x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate ||
                x >= layer.Width || y >= layer.Height) return;

            DrawLineBresenham(currentFrame, lastX, lastY, x, y);
            
            lastX = x;
            lastY = y;
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            lastX = Config.Tool.InvalidCoordinate;
            lastY = Config.Tool.InvalidCoordinate;
        }

        private unsafe void DrawLineBresenham(Frame frame, int x0, int y0, int x1, int y1)
        {
            Layer targetLayer;
            if (!TryGetLayer(frame, ActiveLayerIndex, out targetLayer)) return;
            int effectiveBrushSize = EFYVBackend.Core.Math.FastMath.FastMin(
                BrushSize,
                EFYVBackend.Core.Math.FastMath.FastMax(targetLayer.Width, targetLayer.Height));

            // Pin the memory ONCE for the entire line drawing operation
            fixed (PixelColor* ptr = targetLayer.Pixels)
            {
                // MIGRATION: Handed the heavy pointer-math and algorithmic tracing to the Backend Engine.
                if (effectiveBrushSize <= Config.Tool.Pencil.MinThickBrushSize)
                {
                    EFYVBackend.Core.Math.Algorithms.DrawLineBresenham(
                        (uint*)ptr, 
                        targetLayer.Width, 
                        targetLayer.Height, 
                        x0, y0, x1, y1, 
                        CurrentColor.Rgba
                    );
                }
                else
                {
                    EFYVBackend.Core.Math.Algorithms.DrawThickLineBresenham(
                        (uint*)ptr, 
                        targetLayer.Width, 
                        targetLayer.Height, 
                        x0, y0, x1, y1, 
                        CurrentColor.Rgba,
                        effectiveBrushSize,
                        BrushShape
                    );
                }
            }
        }

        private void DrawPixel(Frame frame, int x, int y)
        {
            Layer targetLayer;
            if (!TryGetLayer(frame, ActiveLayerIndex, out targetLayer)) return;
            
            if (BrushSize <= Config.Tool.Pencil.MinThickBrushSize)
            {
                targetLayer.SetPixel(x, y, CurrentColor);
            }
            else
            {
                // Route a single tap through the thick line algorithm using a 0-distance line
                DrawLineBresenham(frame, x, y, x, y);
            }
        }
    }
}
