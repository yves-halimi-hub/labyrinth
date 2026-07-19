using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    // First-class eraser: writes TRUE transparency (0x00000000) rather than
    // painting with a zero-alpha brush color, and is independent of the current
    // brush color entirely. It edits the active layer through the same
    // ILayerTool contract as the pencil, so gestures flow through the sparse
    // pixel-diff undo path and every stroke is one reversible command.
    public sealed class EraserTool : Tool, ILayerTool
    {
        private EFYVBackend.Core.Models.BrushToolData Data;

        public SymmetryMode Symmetry { get; set; }
        public EFYVBackend.Core.Math.Algorithms.BrushShape BrushShape { get; set; }

        public int ActiveLayerIndex
        {
            get => Data.ActiveLayerIndex;
            set => Data.ActiveLayerIndex = value;
        }

        public int BrushSize
        {
            get => Data.BrushSize;
            set
            {
                if (value < Config.Tool.Eraser.DefaultBrushSize)
                    Data.BrushSize = Config.Tool.Eraser.DefaultBrushSize;
                else if (value > Config.Tool.MaxBrushSize)
                    Data.BrushSize = Config.Tool.MaxBrushSize;
                else
                    Data.BrushSize = value;
            }
        }

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

        public EraserTool()
        {
            Data = new EFYVBackend.Core.Models.BrushToolData
            {
                Block = new EFYVBackend.Core.Data.FastSchemaBlock()
            };
            ActiveLayerIndex = Config.Tool.DefaultLayerIndex;
            BrushSize = Config.Tool.Eraser.DefaultBrushSize;
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
            EraseSegment(layer, x, y, x, y);
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (lastX == Config.Tool.InvalidCoordinate || lastY == Config.Tool.InvalidCoordinate) return;
            Layer layer;
            if (!TryGetLayer(currentFrame, ActiveLayerIndex, out layer) ||
                x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate ||
                x >= layer.Width || y >= layer.Height) return;

            EraseSegment(layer, lastX, lastY, x, y);
            lastX = x;
            lastY = y;
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            lastX = Config.Tool.InvalidCoordinate;
            lastY = Config.Tool.InvalidCoordinate;
        }

        private void EraseSegment(Layer layer, int x0, int y0, int x1, int y1)
        {
            StrokeRenderer.DrawSegment(
                layer,
                Symmetry,
                x0, y0, x1, y1,
                Config.Tool.Eraser.EraseRgba,
                BrushSize,
                BrushShape);
        }
    }
}
