using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    public sealed class StampTool : Tool, ILayerTool
    {
        private EFYVBackend.Core.Models.BrushToolData Data = new EFYVBackend.Core.Models.BrushToolData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public SubElement ActiveSubElement { get; set; }
        public int ActiveLayerIndex
        {
            get => Data.ActiveLayerIndex;
            set => Data.ActiveLayerIndex = value;
        }

        public StampTool()
        {
            ActiveLayerIndex = Config.Tool.DefaultLayerIndex;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            Layer targetLayer;
            if (ActiveSubElement == null || !TryGetLayer(currentFrame, ActiveLayerIndex, out targetLayer)) return;

            // Use the Backend's ultra-fast stamp blitting engine
            unsafe
            {
                fixed (uint* srcPtr = ActiveSubElement.Pixels)
                fixed (PixelColor* destPtr = targetLayer.Pixels)
                {
                    // Center the stamp on the cursor
                    int stampX = x - EFYVBackend.Core.Math.FastMath.FastDivPow2(ActiveSubElement.Width, Config.Tool.Stamp.CenterDivisorPower);
                    int stampY = y - EFYVBackend.Core.Math.FastMath.FastDivPow2(ActiveSubElement.Height, Config.Tool.Stamp.CenterDivisorPower);

                    EFYVBackend.Core.Memory.FastMemory.StampBlit(
                        srcPtr, ActiveSubElement.Width, ActiveSubElement.Height,
                        (uint*)destPtr, targetLayer.Width, targetLayer.Height,
                        stampX, stampY
                    );
                }
            }
        }

    }
}
