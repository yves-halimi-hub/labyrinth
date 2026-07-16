using EFYVLabyMake.Core.Models;
using EFYVBackend.Core.Memory;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public class ViewportController
    {
        private EFYVBackend.Core.Models.ViewportData Data = new EFYVBackend.Core.Models.ViewportData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public float ZoomLevel
        {
            get => Data.ZoomLevel;
            private set => Data.ZoomLevel = value;
        }
        public int OffsetX
        {
            get => Data.OffsetX;
            private set => Data.OffsetX = value;
        }
        public int OffsetY
        {
            get => Data.OffsetY;
            private set => Data.OffsetY = value;
        }

        private const float MinZoom = Config.Viewport.MinZoom;
        private const float MaxZoom = Config.Viewport.MaxZoom;
        private const float ZoomStep = Config.Viewport.ZoomStep;
        private PixelColor[] flattenedBuffer;

        public ViewportController()
        {
            ZoomLevel = Config.Viewport.DefaultZoomLevel;
            OffsetX = Config.Viewport.DefaultOffsetX;
            OffsetY = Config.Viewport.DefaultOffsetY;
        }

        // Processes Mouse Scroll Input
        public void OnScroll(float scrollDelta)
        {
            if (scrollDelta > Config.Viewport.NeutralScrollDelta)
            {
                ZoomLevel += ZoomStep;
            }
            else if (scrollDelta < Config.Viewport.NeutralScrollDelta)
            {
                ZoomLevel -= ZoomStep;
            }
            
            // Clamp Zoom
            if (ZoomLevel < MinZoom) ZoomLevel = MinZoom;
            if (ZoomLevel > MaxZoom) ZoomLevel = MaxZoom;
        }

        public void OnScroll(float scrollDelta, int anchorScreenX, int anchorScreenY)
        {
            int canvasX;
            int canvasY;
            ScreenToCanvas(anchorScreenX, anchorScreenY, out canvasX, out canvasY);
            OnScroll(scrollDelta);
            OffsetX = anchorScreenX - (int)(canvasX * ZoomLevel);
            OffsetY = anchorScreenY - (int)(canvasY * ZoomLevel);
        }

        // Translates a raw UI mouse click (screen coordinates) into underlying raw pixel array coordinates
        public void ScreenToCanvas(int screenX, int screenY, out int canvasX, out int canvasY)
        {
            canvasX = (int)((screenX - OffsetX) / ZoomLevel);
            canvasY = (int)((screenY - OffsetY) / ZoomLevel);
        }

        // Pans the camera view
        public void Pan(int deltaX, int deltaY)
        {
            OffsetX += deltaX;
            OffsetY += deltaY;
        }

        // Renders the raw frame memory to a display buffer at the current zoom scale
        public unsafe void RenderToScreenBuffer(Frame frame, uint[] screenBuffer, int screenWidth, int screenHeight)
        {
            if (frame == null) throw new System.ArgumentNullException(nameof(frame));
            if (screenBuffer == null) throw new System.ArgumentNullException(nameof(screenBuffer));
            if (screenWidth <= Config.Canvas.MinCoordinate || screenHeight <= Config.Canvas.MinCoordinate)
                throw new System.ArgumentOutOfRangeException(nameof(screenWidth));
            if (screenBuffer.Length != checked(screenWidth * screenHeight))
                throw new System.ArgumentException(nameof(screenBuffer));

            int canvasPixelCount = checked(frame.Width * frame.Height);
            if (flattenedBuffer == null || flattenedBuffer.Length != canvasPixelCount)
                flattenedBuffer = new PixelColor[canvasPixelCount];
            frame.FlattenLayers(flattenedBuffer);

            fixed (PixelColor* srcPtr = flattenedBuffer)
            fixed (uint* destPtr = screenBuffer)
            {
                FastMemory.ScaleBlitNearestNeighbor(
                    (uint*)srcPtr, frame.Width, frame.Height,
                    destPtr, screenWidth, screenHeight,
                    ZoomLevel, OffsetX, OffsetY
                );
            }
        }
    }
}
