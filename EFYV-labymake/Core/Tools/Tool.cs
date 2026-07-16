using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    public interface ITool
    {
        void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y);
        void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y);
        void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y);
    }

    public interface ILayerTool
    {
        int ActiveLayerIndex { get; set; }
    }

    public interface IColorTool
    {
        PixelColor CurrentColor { get; set; }
    }

    public abstract class Tool : ITool
    {
        public abstract void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y);
        public virtual void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y) { }
        public virtual void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y) { }

        protected static bool TryGetLayer(Frame frame, int layerIndex, out Layer layer)
        {
            if (frame != null && layerIndex >= 0 && layerIndex < frame.Layers.Count)
            {
                layer = frame.Layers[layerIndex];
                return true;
            }

            layer = null;
            return false;
        }
    }

    public abstract class ColorLayerTool : Tool, ILayerTool, IColorTool
    {
        protected EFYVBackend.Core.Models.BrushToolData Data;

        protected ColorLayerTool()
        {
            Data = new EFYVBackend.Core.Models.BrushToolData
            {
                Block = new EFYVBackend.Core.Data.FastSchemaBlock()
            };
            Data.CurrentColorRgba = unchecked((int)Config.Color.DefaultBrushRgba);
            Data.ActiveLayerIndex = Config.Tool.DefaultLayerIndex;
        }

        public PixelColor CurrentColor
        {
            get => new PixelColor { Rgba = unchecked((uint)Data.CurrentColorRgba) };
            set => Data.CurrentColorRgba = unchecked((int)value.Rgba);
        }

        public int ActiveLayerIndex
        {
            get => Data.ActiveLayerIndex;
            set => Data.ActiveLayerIndex = value;
        }
    }
}
