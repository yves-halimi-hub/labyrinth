using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;

namespace EFYVLabyMake.App.UI
{
    // Renders the session's current frame through the Core ViewportController into
    // a BGRA WriteableBitmap and routes pointer input back into the session:
    //   pointer position (physical pixels) -> ViewportController.ScreenToCanvas ->
    //   EditorShell.PointerDown/Drag/Up, with a redraw after every pointer event.
    // All viewport math runs in physical pixels so pixels stay crisp under DPI
    // scaling; Avalonia gives logical coordinates, so both directions multiply by
    // the render scaling exactly once, here.
    public sealed class CanvasView : Control
    {
        private readonly ViewportController viewport = new ViewportController();
        private EditorShell shell;
        private WriteableBitmap bitmap;
        private uint[] canvasBuffer;
        private uint[] bgraBuffer;
        private int surfaceWidth;
        private int surfaceHeight;

        private bool drawing;
        private bool panning;
        private Point lastPanPosition;

        public ViewportController Viewport => viewport;

        public CanvasView()
        {
            Focusable = true;
            ClipToBounds = true;
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        }

        public void Attach(EditorShell editorShell)
        {
            shell = editorShell ?? throw new ArgumentNullException(nameof(editorShell));
            shell.CanvasInvalidated += RenderFrame;
            shell.ReportZoom(viewport.ZoomLevel);
        }

        // --- Rendering -------------------------------------------------------------

        public void RenderFrame()
        {
            if (shell == null) return;
            double scaling = RenderScaling();
            int width = (int)System.Math.Floor(Bounds.Width * scaling);
            int height = (int)System.Math.Floor(Bounds.Height * scaling);
            if (width <= 0 || height <= 0) return;
            EnsureSurfaces(width, height);

            // Item #5: in map mode the shell hands back the composed map
            // surface instead of the session frame; onion skin and the
            // floating selection are pixel-editing concepts and stay off.
            Frame frame = shell.ActiveCanvasFrame;
            bool hasCanvas = frame != null;
            if (hasCanvas)
            {
                // Item #31: the shell stamps its reusable overlay settings
                // (effective toggles + tile-grid/attachment context) and Core
                // composites the enabled overlay passes after the blit - the
                // checkerboard backdrop now renders in Core, not here.
                EFYVLabyMake.Core.Logic.ViewportOverlaySettings overlays =
                    shell.PrepareOverlayContext();

                // Onion skinning (item #10): when toggled on and the session has
                // a frame selection, Core composites ghost neighbors under the
                // current frame; otherwise the plain single-frame path runs.
                // The session's floating selection (a lifted/pasted buffer being
                // dragged) composites over the flattened result inside Core.
                var animationFrames = !shell.MapModeActive && shell.OnionSkinEnabled
                    ? shell.CurrentAnimationFrames
                    : null;
                int frameIndex = shell.TimelineFrameIndex;
                if (animationFrames != null && frameIndex >= 0 && frameIndex < animationFrames.Count &&
                    ReferenceEquals(animationFrames[frameIndex], frame))
                {
                    viewport.RenderToScreenBuffer(
                        animationFrames,
                        frameIndex,
                        shell.OnionSettings,
                        shell.CurrentFloating,
                        overlays,
                        canvasBuffer,
                        width,
                        height);
                }
                else
                {
                    viewport.RenderToScreenBuffer(
                        frame,
                        shell.MapModeActive ? null : shell.CurrentFloating,
                        overlays,
                        canvasBuffer,
                        width,
                        height);
                }
            }
            else
            {
                Array.Clear(canvasBuffer, 0, canvasBuffer.Length);
            }

            ScreenPixelConverter.ConvertToBgra(
                canvasBuffer,
                bgraBuffer,
                width,
                height,
                AppDefaults.WorkspaceBgra);

            CopyIntoBitmap(width, height);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(new SolidColorBrush(Color.FromUInt32(AppDefaults.WorkspaceBgra)), new Rect(Bounds.Size));
            if (bitmap == null) return;
            context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(Bounds.Size));
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            RenderFrame();
        }

        private void EnsureSurfaces(int width, int height)
        {
            if (bitmap != null && surfaceWidth == width && surfaceHeight == height) return;
            surfaceWidth = width;
            surfaceHeight = height;
            canvasBuffer = new uint[checked(width * height)];
            bgraBuffer = new uint[canvasBuffer.Length];
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Bgra8888,
                AlphaFormat.Opaque);
        }

        private unsafe void CopyIntoBitmap(int width, int height)
        {
            using (ILockedFramebuffer framebuffer = bitmap.Lock())
            {
                fixed (uint* source = bgraBuffer)
                {
                    byte* destinationRow = (byte*)framebuffer.Address;
                    int rowBytes = width * sizeof(uint);
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            source + ((long)y * width),
                            destinationRow,
                            framebuffer.RowBytes,
                            rowBytes);
                        destinationRow += framebuffer.RowBytes;
                    }
                }
            }
        }

        // --- Pointer routing ---------------------------------------------------------

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            PointerPoint point = e.GetCurrentPoint(this);
            if (point.Properties.IsMiddleButtonPressed)
            {
                panning = true;
                lastPanPosition = point.Position;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            if (!point.Properties.IsLeftButtonPressed || shell == null) return;

            int canvasX, canvasY;
            ToCanvas(point.Position, out canvasX, out canvasY);
            drawing = true;
            e.Pointer.Capture(this);
            shell.PointerDown(canvasX, canvasY);
            RenderFrame();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            Point position = e.GetCurrentPoint(this).Position;
            if (panning)
            {
                double scaling = RenderScaling();
                viewport.Pan(
                    (int)System.Math.Round((position.X - lastPanPosition.X) * scaling),
                    (int)System.Math.Round((position.Y - lastPanPosition.Y) * scaling));
                lastPanPosition = position;
                RenderFrame();
                e.Handled = true;
                return;
            }
            if (!drawing || shell == null) return;

            int canvasX, canvasY;
            ToCanvas(position, out canvasX, out canvasY);
            shell.PointerDrag(canvasX, canvasY);
            RenderFrame();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (panning)
            {
                panning = false;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }
            if (!drawing || shell == null) return;

            drawing = false;
            e.Pointer.Capture(null);
            int canvasX, canvasY;
            ToCanvas(e.GetCurrentPoint(this).Position, out canvasX, out canvasY);
            shell.PointerUp(canvasX, canvasY);
            RenderFrame();
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            panning = false;
            if (drawing)
            {
                // The gesture can no longer complete coherently; roll it back.
                drawing = false;
                shell?.CancelGesture();
                RenderFrame();
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            double scaling = RenderScaling();
            Point position = e.GetCurrentPoint(this).Position;
            viewport.OnScroll(
                (float)e.Delta.Y,
                (int)System.Math.Round(position.X * scaling),
                (int)System.Math.Round(position.Y * scaling));
            shell?.ReportZoom(viewport.ZoomLevel);
            RenderFrame();
            e.Handled = true;
        }

        public void CancelActiveGesture()
        {
            drawing = false;
            panning = false;
            shell?.CancelGesture();
            RenderFrame();
        }

        // --- Zoom controls (item #31 carried gap) -------------------------------------
        // Button-driven zoom through the public ViewportController API,
        // anchored on the view center so the art stays put; Reset restores
        // the default zoom and origin offsets.

        public void ZoomIn()
        {
            StepZoom(EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake.Viewport.ZoomStep);
        }

        public void ZoomOut()
        {
            StepZoom(-EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake.Viewport.ZoomStep);
        }

        public void ResetView()
        {
            viewport.ResetView();
            shell?.ReportZoom(viewport.ZoomLevel);
            RenderFrame();
        }

        private void StepZoom(float delta)
        {
            double scaling = RenderScaling();
            viewport.SetZoom(
                viewport.ZoomLevel + delta,
                (int)System.Math.Round(Bounds.Width * scaling / 2.0),
                (int)System.Math.Round(Bounds.Height * scaling / 2.0));
            shell?.ReportZoom(viewport.ZoomLevel);
            RenderFrame();
        }

        private void ToCanvas(Point position, out int canvasX, out int canvasY)
        {
            double scaling = RenderScaling();
            viewport.ScreenToCanvas(
                (int)System.Math.Round(position.X * scaling),
                (int)System.Math.Round(position.Y * scaling),
                out canvasX,
                out canvasY);
        }

        private double RenderScaling()
        {
            return VisualRoot?.RenderScaling ?? 1.0;
        }
    }
}
