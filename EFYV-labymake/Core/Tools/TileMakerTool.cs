using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    public sealed class TileMakerTool : ColorLayerTool
    {
        public int TileSize
        {
            get => Data.TileSize;
            set
            {
                if (value < Config.Tool.TileMaker.MinTileSize) Data.TileSize = Config.Tool.TileMaker.MinTileSize;
                else if (value > Config.Tool.TileMaker.MaxTileSize) Data.TileSize = Config.Tool.TileMaker.MaxTileSize;
                else Data.TileSize = value;
            }
        }
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
        public TileMakerTool()
        {
            TileSize = Config.Tool.TileMaker.DefaultTileSize;
            BrushSize = Config.Tool.Pencil.DefaultBrushSize;
        }

        public void Execute(Frame frame, int x, int y)
        {
            Layer targetLayer;
            if (!TryGetLayer(frame, ActiveLayerIndex, out targetLayer)) return;
            if (x < Config.Canvas.MinCoordinate || y < Config.Canvas.MinCoordinate ||
                x >= targetLayer.Width || y >= targetLayer.Height) return;

            int tileOriginX = (x / TileSize) * TileSize;
            int tileOriginY = (y / TileSize) * TileSize;
            int effectiveBrushSize = EFYVBackend.Core.Math.FastMath.FastMin(
                EFYVBackend.Core.Math.FastMath.FastMin(BrushSize, TileSize),
                EFYVBackend.Core.Math.FastMath.FastMax(targetLayer.Width, targetLayer.Height));
            int minimumOffset = -EFYVBackend.Core.Math.FastMath.FastDivPow2(
                effectiveBrushSize,
                Config.Tool.Stamp.CenterDivisorPower);
            int maximumOffset = minimumOffset + effectiveBrushSize - Config.Common.UnitCount;

            for (int offsetY = minimumOffset; offsetY <= maximumOffset; offsetY++)
            {
                int localY = EFYVBackend.Core.Math.FastMath.FastWrap(y - tileOriginY + offsetY, TileSize);
                int pixelY = tileOriginY + localY;
                if (pixelY < Config.Canvas.MinCoordinate || pixelY >= targetLayer.Height) continue;

                for (int offsetX = minimumOffset; offsetX <= maximumOffset; offsetX++)
                {
                    int localX = EFYVBackend.Core.Math.FastMath.FastWrap(x - tileOriginX + offsetX, TileSize);
                    int pixelX = tileOriginX + localX;
                    targetLayer.SetPixel(pixelX, pixelY, CurrentColor);
                }
            }
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Execute(currentFrame, x, y);
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Execute(currentFrame, x, y);
        }
    }
}
