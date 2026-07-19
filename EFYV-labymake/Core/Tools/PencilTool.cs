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
        public SymmetryMode Symmetry { get; set; }

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

        private void DrawLineBresenham(Frame frame, int x0, int y0, int x1, int y1)
        {
            Layer targetLayer;
            if (!TryGetLayer(frame, ActiveLayerIndex, out targetLayer)) return;

            // MIGRATION: Handed the heavy pointer-math and algorithmic tracing to
            // the Backend Engine; the shared StrokeRenderer keeps the exact
            // thin/thick dispatch and applies the mirror variants.
            StrokeRenderer.DrawSegment(
                targetLayer,
                Symmetry,
                x0, y0, x1, y1,
                CurrentColor.Rgba,
                BrushSize,
                BrushShape);
        }

        private void DrawPixel(Frame frame, int x, int y)
        {
            // A single tap is a 0-distance line: the renderer degrades a 1-pixel
            // effective brush to the thin single-pixel write.
            DrawLineBresenham(frame, x, y, x, y);
        }
    }
}
