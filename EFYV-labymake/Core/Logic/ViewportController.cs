using System.Collections.Generic;
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
        private PixelColor[] onionScratchBuffer;

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

        // --- Public zoom/pan API (item #31 carried gap) ------------------------------
        // The batch-2 shell could only zoom through OnScroll because the
        // setters are private; these are the direct host-facing spellings.

        // Sets the zoom level directly, clamped into [MinZoom, MaxZoom].
        // Offsets are left untouched (the canvas origin keeps its screen
        // position); use the anchored overload to keep a screen point fixed.
        public void SetZoom(float zoom)
        {
            if (float.IsNaN(zoom) || float.IsInfinity(zoom))
                throw new System.ArgumentOutOfRangeException(nameof(zoom));
            if (zoom < MinZoom) zoom = MinZoom;
            if (zoom > MaxZoom) zoom = MaxZoom;
            ZoomLevel = zoom;
        }

        // Anchored zoom: the canvas point under (anchorScreenX, anchorScreenY)
        // stays under it after the change - the exact math of the anchored
        // OnScroll overload, so wheel zoom and programmatic zoom agree.
        public void SetZoom(float zoom, int anchorScreenX, int anchorScreenY)
        {
            int canvasX;
            int canvasY;
            ScreenToCanvas(anchorScreenX, anchorScreenY, out canvasX, out canvasY);
            SetZoom(zoom);
            OffsetX = anchorScreenX - (int)(canvasX * ZoomLevel);
            OffsetY = anchorScreenY - (int)(canvasY * ZoomLevel);
        }

        // Absolute pan (Pan() is relative).
        public void SetPan(int offsetX, int offsetY)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        // Restores the construction-time view (default zoom, origin offsets).
        public void ResetView()
        {
            ZoomLevel = Config.Viewport.DefaultZoomLevel;
            OffsetX = Config.Viewport.DefaultOffsetX;
            OffsetY = Config.Viewport.DefaultOffsetY;
        }

        // Renders the raw frame memory to a display buffer at the current zoom scale
        public unsafe void RenderToScreenBuffer(Frame frame, uint[] screenBuffer, int screenWidth, int screenHeight)
        {
            RenderToScreenBuffer(frame, null, null, screenBuffer, screenWidth, screenHeight);
        }

        // Overload compositing a session-level FloatingSelection (a lifted or
        // pasted buffer being dragged) over the flattened frame before scaling,
        // so every host previews the pending move without mutating the layer.
        public unsafe void RenderToScreenBuffer(
            Frame frame,
            FloatingSelection floating,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            RenderToScreenBuffer(frame, floating, null, screenBuffer, screenWidth, screenHeight);
        }

        // Item #31: full overload with the optional designer overlay passes.
        // A null settings object (or ViewportOverlayKind.None) renders exactly
        // like the plain overloads; otherwise the enabled passes composite
        // into the screen buffer after the blit (see ViewportOverlays.cs for
        // the pass order and per-pass semantics). Zero-alloc steady state.
        public unsafe void RenderToScreenBuffer(
            Frame frame,
            FloatingSelection floating,
            ViewportOverlaySettings overlays,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
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
            if (floating != null) CompositeFloating(floating, frame.Width, frame.Height);
            BlitFlattenedToScreen(frame.Width, frame.Height, screenBuffer, screenWidth, screenHeight);
            ApplyOverlays(frame, overlays, screenBuffer, screenWidth, screenHeight);
        }

        // --- Onion skinning (item #10) --------------------------------------------

        // Host-agnostic onion-skin composite: ghost frames around
        // currentFrameIndex blend into the destination preview buffer at their
        // configured alphas (farthest first, so nearer ghosts read stronger),
        // then the current frame composites on top at full strength. The
        // destination must hold exactly the current frame's pixel count; every
        // participating frame must share the current frame's dimensions.
        public unsafe void ComposeOnionSkin(
            IReadOnlyList<Frame> frames,
            int currentFrameIndex,
            OnionSkinSettings settings,
            PixelColor[] destination)
        {
            if (frames == null) throw new System.ArgumentNullException(nameof(frames));
            if (settings == null) throw new System.ArgumentNullException(nameof(settings));
            if (destination == null) throw new System.ArgumentNullException(nameof(destination));
            if (currentFrameIndex < Config.Common.FirstIndex || currentFrameIndex >= frames.Count)
                throw new System.ArgumentOutOfRangeException(nameof(currentFrameIndex));

            Frame currentFrame = frames[currentFrameIndex];
            if (currentFrame == null) throw new System.ArgumentException(nameof(frames));
            int canvasPixelCount = checked(currentFrame.Width * currentFrame.Height);
            if (destination.Length != canvasPixelCount)
                throw new System.ArgumentException(nameof(destination));

            FastMemory.Clear(destination);
            if (onionScratchBuffer == null || onionScratchBuffer.Length != canvasPixelCount)
                onionScratchBuffer = new PixelColor[canvasPixelCount];

            // Previous ghosts, farthest to nearest.
            for (int distance = settings.PreviousFrameCount; distance >= Config.Common.UnitCount; distance--)
            {
                int ghostIndex = currentFrameIndex - distance;
                if (ghostIndex < Config.Common.FirstIndex) continue;
                BlendGhost(frames[ghostIndex], currentFrame, destination,
                    GhostAlpha(settings.PreviousAlpha, settings.AlphaFalloff, distance));
            }

            // Next ghosts, farthest to nearest.
            for (int distance = settings.NextFrameCount; distance >= Config.Common.UnitCount; distance--)
            {
                int ghostIndex = currentFrameIndex + distance;
                if (ghostIndex >= frames.Count) continue;
                BlendGhost(frames[ghostIndex], currentFrame, destination,
                    GhostAlpha(settings.NextAlpha, settings.AlphaFalloff, distance));
            }

            BlendGhost(currentFrame, currentFrame, destination, Config.Common.UnitScale);
        }

        // Onion-skin variant of RenderToScreenBuffer: composes ghosts + current
        // frame + optional floating selection into the internal canvas buffer,
        // then scale-blits it exactly like the plain overload.
        public unsafe void RenderToScreenBuffer(
            IReadOnlyList<Frame> frames,
            int currentFrameIndex,
            OnionSkinSettings onionSettings,
            FloatingSelection floating,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            RenderToScreenBuffer(
                frames, currentFrameIndex, onionSettings, floating, null,
                screenBuffer, screenWidth, screenHeight);
        }

        // Item #31: onion-skin overload with the optional overlay passes. The
        // overlay frame context (hitboxes, attachments, pivots) is the CURRENT
        // frame - ghosts only contribute pixels.
        public unsafe void RenderToScreenBuffer(
            IReadOnlyList<Frame> frames,
            int currentFrameIndex,
            OnionSkinSettings onionSettings,
            FloatingSelection floating,
            ViewportOverlaySettings overlays,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            if (frames == null) throw new System.ArgumentNullException(nameof(frames));
            if (onionSettings == null) throw new System.ArgumentNullException(nameof(onionSettings));
            if (screenBuffer == null) throw new System.ArgumentNullException(nameof(screenBuffer));
            if (currentFrameIndex < Config.Common.FirstIndex || currentFrameIndex >= frames.Count)
                throw new System.ArgumentOutOfRangeException(nameof(currentFrameIndex));
            if (screenWidth <= Config.Canvas.MinCoordinate || screenHeight <= Config.Canvas.MinCoordinate)
                throw new System.ArgumentOutOfRangeException(nameof(screenWidth));
            if (screenBuffer.Length != checked(screenWidth * screenHeight))
                throw new System.ArgumentException(nameof(screenBuffer));

            Frame currentFrame = frames[currentFrameIndex];
            if (currentFrame == null) throw new System.ArgumentException(nameof(frames));
            int canvasPixelCount = checked(currentFrame.Width * currentFrame.Height);
            if (flattenedBuffer == null || flattenedBuffer.Length != canvasPixelCount)
                flattenedBuffer = new PixelColor[canvasPixelCount];
            ComposeOnionSkin(frames, currentFrameIndex, onionSettings, flattenedBuffer);
            if (floating != null) CompositeFloating(floating, currentFrame.Width, currentFrame.Height);
            BlitFlattenedToScreen(currentFrame.Width, currentFrame.Height, screenBuffer, screenWidth, screenHeight);
            ApplyOverlays(currentFrame, overlays, screenBuffer, screenWidth, screenHeight);
        }

        private static float GhostAlpha(float baseAlpha, float falloff, int distance)
        {
            float alpha = baseAlpha;
            for (int step = Config.Common.UnitCount; step < distance; step++) alpha *= falloff;
            return alpha;
        }

        // Flattens one ghost frame into the scratch buffer and alpha-blends it
        // over the destination at the given global opacity. Ghosts whose
        // dimensions do not match the current frame indicate a structurally
        // invalid animation and fault loudly rather than compositing garbage.
        private unsafe void BlendGhost(Frame ghost, Frame currentFrame, PixelColor[] destination, float alpha)
        {
            if (ghost == null) return;
            if (alpha <= Config.Common.ZeroFloat) return;
            if (ghost.Width != currentFrame.Width || ghost.Height != currentFrame.Height)
                throw new System.InvalidOperationException();

            ghost.FlattenLayers(onionScratchBuffer);
            fixed (PixelColor* destPtr = destination)
            fixed (PixelColor* srcPtr = onionScratchBuffer)
            {
                FastMemory.BlendLayer(
                    (uint*)destPtr,
                    (uint*)srcPtr,
                    destination.Length,
                    Config.Layer.TransparentAlpha,
                    alpha);
            }
        }

        private unsafe void BlitFlattenedToScreen(
            int canvasWidth,
            int canvasHeight,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            fixed (PixelColor* srcPtr = flattenedBuffer)
            fixed (uint* destPtr = screenBuffer)
            {
                FastMemory.ScaleBlitNearestNeighbor(
                    (uint*)srcPtr, canvasWidth, canvasHeight,
                    destPtr, screenWidth, screenHeight,
                    ZoomLevel, OffsetX, OffsetY
                );
            }
        }

        private void CompositeFloating(FloatingSelection floating, int canvasWidth, int canvasHeight)
        {
            for (int localY = Config.Common.FirstIndex; localY < floating.Height; localY++)
            {
                int destY = floating.OffsetY + localY;
                if (destY < Config.Canvas.MinCoordinate || destY >= canvasHeight) continue;
                int sourceRow = localY * floating.Width;
                for (int localX = Config.Common.FirstIndex; localX < floating.Width; localX++)
                {
                    if (!floating.Mask[sourceRow + localX]) continue;
                    int destX = floating.OffsetX + localX;
                    if (destX < Config.Canvas.MinCoordinate || destX >= canvasWidth) continue;
                    int destIndex = destY * canvasWidth + destX;
                    uint blended = flattenedBuffer[destIndex].Rgba;
                    FastMemory.BlendColor(ref blended, floating.Pixels[sourceRow + localX]);
                    flattenedBuffer[destIndex].Rgba = blended;
                }
            }
        }

        // --- Designer overlay passes (item #31) --------------------------------------
        // All passes draw directly into the caller's screen buffer (straight
        // RGBA, red in the low byte) with no allocations. Geometry passes
        // (hitboxes, attachments, pivots) clip to the SCREEN only - an
        // attachment may legitimately hang off-canvas; the backdrop and grid
        // passes additionally confine themselves to the canvas area using the
        // exact ScaleBlitNearestNeighbor dest->source mapping so they can
        // never leak past (or drift off) the blitted canvas pixels.

        private const float PixelCenterOffset =
            Config.Common.UnitScale / Config.Canvas.AnchorCenterDivisor;

        // Deterministic per-key hitbox outline color: the shared FNV hash
        // spread over RGB, forced opaque (the MapRenderer placeholder scheme).
        public static uint GetHitboxKeyColor(string hitboxKey)
        {
            uint hashed = (uint)EFYVBackend.Core.Math.FastMath.FastHash(hitboxKey);
            return hashed | ((uint)EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.ColorMaxByte
                << Config.Color.AlphaShift);
        }

        private void ApplyOverlays(
            Frame frame,
            ViewportOverlaySettings overlays,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            if (overlays == null || overlays.Enabled == ViewportOverlayKind.None) return;
            ValidateOverlaySettings(overlays);

            if ((overlays.Enabled & ViewportOverlayKind.Checkerboard) != ViewportOverlayKind.None)
                ApplyCheckerboardUnderlay(
                    frame.Width, frame.Height, overlays.Checkerboard,
                    screenBuffer, screenWidth, screenHeight);
            if ((overlays.Enabled & ViewportOverlayKind.PixelGrid) != ViewportOverlayKind.None &&
                ZoomLevel >= overlays.PixelGrid.MinZoom)
                DrawGridLines(
                    frame.Width, frame.Height, Config.Common.UnitCount,
                    overlays.PixelGrid.LineRgba, screenBuffer, screenWidth, screenHeight);
            if ((overlays.Enabled & ViewportOverlayKind.TileGrid) != ViewportOverlayKind.None &&
                overlays.TileGrid.TileSize > Config.Overlay.InactiveTileSize)
                DrawGridLines(
                    frame.Width, frame.Height, overlays.TileGrid.TileSize,
                    overlays.TileGrid.LineRgba, screenBuffer, screenWidth, screenHeight);
            if ((overlays.Enabled & ViewportOverlayKind.Hitboxes) != ViewportOverlayKind.None)
                DrawHitboxes(frame, overlays.Hitboxes, screenBuffer, screenWidth, screenHeight);
            if ((overlays.Enabled & ViewportOverlayKind.AttachmentOutlines) != ViewportOverlayKind.None)
                DrawAttachmentOutlines(frame, overlays, screenBuffer, screenWidth, screenHeight);
            if ((overlays.Enabled & ViewportOverlayKind.PivotMarkers) != ViewportOverlayKind.None)
                DrawPivotMarkers(frame, overlays.Pivots, screenBuffer, screenWidth, screenHeight);
        }

        // Config faults are host programming errors and fault loudly instead
        // of rendering garbage; only the ENABLED passes are validated so an
        // untouched default struct on a disabled pass can never throw.
        private static void ValidateOverlaySettings(ViewportOverlaySettings overlays)
        {
            ViewportOverlayKind enabled = overlays.Enabled;
            if ((enabled & ViewportOverlayKind.Checkerboard) != ViewportOverlayKind.None &&
                (overlays.Checkerboard.CellShift < Config.Overlay.MinCheckerCellShift ||
                    overlays.Checkerboard.CellShift > Config.Overlay.MaxCheckerCellShift))
                throw new System.ArgumentOutOfRangeException(nameof(overlays));
            if ((enabled & ViewportOverlayKind.PixelGrid) != ViewportOverlayKind.None &&
                float.IsNaN(overlays.PixelGrid.MinZoom))
                throw new System.ArgumentOutOfRangeException(nameof(overlays));
            if ((enabled & ViewportOverlayKind.Hitboxes) != ViewportOverlayKind.None &&
                (float.IsNaN(overlays.Hitboxes.PixelsPerUnit) ||
                    float.IsInfinity(overlays.Hitboxes.PixelsPerUnit) ||
                    overlays.Hitboxes.PixelsPerUnit <= Config.Common.ZeroFloat))
                throw new System.ArgumentOutOfRangeException(nameof(overlays));
            if ((enabled & ViewportOverlayKind.PivotMarkers) != ViewportOverlayKind.None &&
                (overlays.Pivots.MarkerRadius < Config.Common.EmptyCount ||
                    overlays.Pivots.MarkerRadius > Config.Overlay.MaxPivotMarkerRadius))
                throw new System.ArgumentOutOfRangeException(nameof(overlays));
        }

        // Checkerboard backdrop: inside the canvas area, the blitted content
        // re-composites OVER a screen-anchored checker cell (opaque cells make
        // the result opaque); outside pixels are untouched so hosts keep
        // painting their own workspace color there.
        private void ApplyCheckerboardUnderlay(
            int canvasWidth,
            int canvasHeight,
            in CheckerboardOverlayConfig checkerboard,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            float invZoom = Config.Common.UnitScale / ZoomLevel;
            int shift = checkerboard.CellShift;
            int rowStart = Config.Common.FirstIndex;
            for (int y = Config.Common.FirstIndex; y < screenHeight; y++, rowStart += screenWidth)
            {
                float sourceY = (y - OffsetY) * invZoom;
                if (!(sourceY >= Config.Common.ZeroFloat && (uint)(int)sourceY < (uint)canvasHeight))
                    continue;
                for (int x = Config.Common.FirstIndex; x < screenWidth; x++)
                {
                    float sourceX = (x - OffsetX) * invZoom;
                    if (!(sourceX >= Config.Common.ZeroFloat && (uint)(int)sourceX < (uint)canvasWidth))
                        continue;
                    uint cell = (((x >> shift) + (y >> shift)) & Config.Common.UnitCount) ==
                        Config.Common.EmptyCount
                            ? checkerboard.LightRgba
                            : checkerboard.DarkRgba;
                    FastMemory.BlendColor(ref cell, screenBuffer[rowStart + x]);
                    screenBuffer[rowStart + x] = cell;
                }
            }
        }

        // Grid lines (pixel grid: cellSize 1; tile grid: cellSize TileSize):
        // a 1-screen-pixel line at every screen column/row where the mapped
        // source CELL changes (including the canvas leading edges), confined
        // to the canvas area. Lines alpha-blend over the content.
        private void DrawGridLines(
            int canvasWidth,
            int canvasHeight,
            int cellSize,
            uint lineRgba,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            float invZoom = Config.Common.UnitScale / ZoomLevel;

            for (int x = Config.Common.FirstIndex; x < screenWidth; x++)
            {
                int cell = MapScreenToCell(x, OffsetX, invZoom, canvasWidth, cellSize);
                if (cell < Config.Common.FirstIndex) continue;
                int previous = x == Config.Common.FirstIndex
                    ? Config.Common.NotFoundIndex
                    : MapScreenToCell(x - Config.Common.UnitCount, OffsetX, invZoom, canvasWidth, cellSize);
                if (previous == cell) continue;
                for (int y = Config.Common.FirstIndex; y < screenHeight; y++)
                {
                    float sourceY = (y - OffsetY) * invZoom;
                    if (!(sourceY >= Config.Common.ZeroFloat && (uint)(int)sourceY < (uint)canvasHeight))
                        continue;
                    FastMemory.BlendColor(ref screenBuffer[y * screenWidth + x], lineRgba);
                }
            }

            for (int y = Config.Common.FirstIndex; y < screenHeight; y++)
            {
                int cell = MapScreenToCell(y, OffsetY, invZoom, canvasHeight, cellSize);
                if (cell < Config.Common.FirstIndex) continue;
                int previous = y == Config.Common.FirstIndex
                    ? Config.Common.NotFoundIndex
                    : MapScreenToCell(y - Config.Common.UnitCount, OffsetY, invZoom, canvasHeight, cellSize);
                if (previous == cell) continue;
                int rowStart = y * screenWidth;
                for (int x = Config.Common.FirstIndex; x < screenWidth; x++)
                {
                    float sourceX = (x - OffsetX) * invZoom;
                    if (!(sourceX >= Config.Common.ZeroFloat && (uint)(int)sourceX < (uint)canvasWidth))
                        continue;
                    // A vertical line already claimed this pixel when this
                    // screen column sits on a cell boundary too; skip it so
                    // every grid pixel blends exactly once per pass.
                    int columnCell = MapScreenToCell(x, OffsetX, invZoom, canvasWidth, cellSize);
                    int columnPrevious = x == Config.Common.FirstIndex
                        ? Config.Common.NotFoundIndex
                        : MapScreenToCell(x - Config.Common.UnitCount, OffsetX, invZoom, canvasWidth, cellSize);
                    if (columnCell >= Config.Common.FirstIndex && columnPrevious != columnCell) continue;
                    FastMemory.BlendColor(ref screenBuffer[rowStart + x], lineRgba);
                }
            }
        }

        // The blit's dest->source mapping quantized to grid cells; -1 when the
        // screen coordinate maps outside the canvas on this axis.
        private static int MapScreenToCell(
            int screenCoordinate,
            int offset,
            float invZoom,
            int canvasExtent,
            int cellSize)
        {
            float source = (screenCoordinate - offset) * invZoom;
            int sourceIndex = (int)source;
            if (!(source >= Config.Common.ZeroFloat && (uint)sourceIndex < (uint)canvasExtent))
                return Config.Common.NotFoundIndex;
            return sourceIndex / cellSize;
        }

        // Hitbox rectangles: world units * PixelsPerUnit -> canvas pixels ->
        // screen outline in the key's deterministic color. Zero/negative or
        // non-finite boxes (default(HitboxData) bypasses the semantic ctor and
        // is all-zero) are skipped.
        private void DrawHitboxes(
            Frame frame,
            in HitboxOverlayConfig hitboxes,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            float pixelsPerUnit = hitboxes.PixelsPerUnit;
            foreach (KeyValuePair<string, EFYVBackend.Core.Models.HitboxData> pair in frame.Hitboxes)
            {
                EFYVBackend.Core.Models.HitboxData box = pair.Value;
                if (float.IsNaN(box.X) || float.IsInfinity(box.X) ||
                    float.IsNaN(box.Y) || float.IsInfinity(box.Y) ||
                    float.IsInfinity(box.Width) || float.IsInfinity(box.Height))
                    continue;
                if (!(box.Width > Config.Common.ZeroFloat) || !(box.Height > Config.Common.ZeroFloat))
                    continue;
                DrawCanvasRectOutline(
                    box.X * pixelsPerUnit,
                    box.Y * pixelsPerUnit,
                    (box.X + box.Width) * pixelsPerUnit,
                    (box.Y + box.Height) * pixelsPerUnit,
                    GetHitboxKeyColor(pair.Key),
                    screenBuffer, screenWidth, screenHeight);
            }
        }

        // Attachment outlines: bounds of each resolvable attachment placed
        // with the export flatten's pivot/flip math (the flipped pivot lands
        // on the anchor - ExportEngine.CompositeAttachment), so the outline
        // frames exactly the pixels the export would blend.
        private void DrawAttachmentOutlines(
            Frame frame,
            ViewportOverlaySettings overlays,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            IReadOnlyList<SubElement> sources = overlays.AttachmentSources;
            if (sources == null) return;
            for (int index = Config.Common.FirstIndex; index < frame.Attachments.Count; index++)
            {
                SubElementAttachment attachment = frame.Attachments[index];
                if (attachment == null) continue;
                SubElement element = FindAttachmentSource(sources, attachment.SubElementName);
                if (element == null) continue;
                int pivotX = attachment.FlipX
                    ? element.Width - Config.Common.UnitCount - element.PivotX
                    : element.PivotX;
                int pivotY = attachment.FlipY
                    ? element.Height - Config.Common.UnitCount - element.PivotY
                    : element.PivotY;
                int originX = attachment.X - pivotX;
                int originY = attachment.Y - pivotY;
                DrawCanvasRectOutline(
                    originX,
                    originY,
                    originX + element.Width,
                    originY + element.Height,
                    overlays.Attachments.OutlineRgba,
                    screenBuffer, screenWidth, screenHeight);
            }
        }

        private static SubElement FindAttachmentSource(IReadOnlyList<SubElement> sources, string name)
        {
            for (int index = Config.Common.FirstIndex; index < sources.Count; index++)
            {
                SubElement candidate = sources[index];
                if (candidate != null && string.Equals(candidate.Name, name, System.StringComparison.Ordinal))
                    return candidate;
            }
            return null;
        }

        // Pivot markers: the optional explicit host pivot plus every
        // attachment anchor (the canvas point the placed sub-element's pivot
        // lands on).
        private void DrawPivotMarkers(
            Frame frame,
            in PivotOverlayConfig pivots,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            if (pivots.HasExplicitPivot)
                DrawPivotCross(
                    pivots.ExplicitPivotX, pivots.ExplicitPivotY, pivots,
                    screenBuffer, screenWidth, screenHeight);
            for (int index = Config.Common.FirstIndex; index < frame.Attachments.Count; index++)
            {
                SubElementAttachment attachment = frame.Attachments[index];
                if (attachment == null) continue;
                DrawPivotCross(
                    attachment.X, attachment.Y, pivots,
                    screenBuffer, screenWidth, screenHeight);
            }
        }

        // Crosshair on the CENTER of the addressed canvas pixel; the vertical
        // arm skips the center pixel so every marker pixel blends exactly once
        // (semi-transparent marker colors stay uniform).
        private void DrawPivotCross(
            int canvasX,
            int canvasY,
            in PivotOverlayConfig pivots,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            int screenX = OffsetX + FloorToInt((canvasX + PixelCenterOffset) * ZoomLevel);
            int screenY = OffsetY + FloorToInt((canvasY + PixelCenterOffset) * ZoomLevel);
            int radius = pivots.MarkerRadius;
            DrawHorizontalSpan(
                screenY, screenX - radius, screenX + radius, pivots.MarkerRgba,
                screenBuffer, screenWidth, screenHeight);
            if (radius > Config.Common.EmptyCount)
            {
                DrawVerticalSpan(
                    screenX, screenY - radius, screenY - Config.Common.UnitCount, pivots.MarkerRgba,
                    screenBuffer, screenWidth, screenHeight);
                DrawVerticalSpan(
                    screenX, screenY + Config.Common.UnitCount, screenY + radius, pivots.MarkerRgba,
                    screenBuffer, screenWidth, screenHeight);
            }
        }

        // Canvas-space rect [left, rightExclusive) x [top, bottomExclusive) ->
        // 1-screen-pixel outline over the covered screen region (a rect thinner
        // than a screen pixel still draws a 1-pixel line). Every outline pixel
        // blends exactly once; clipping is against the screen only.
        private void DrawCanvasRectOutline(
            float canvasLeft,
            float canvasTop,
            float canvasRightExclusive,
            float canvasBottomExclusive,
            uint colorRgba,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            float zoom = ZoomLevel;
            int left = OffsetX + FloorToInt(canvasLeft * zoom);
            int top = OffsetY + FloorToInt(canvasTop * zoom);
            int right = OffsetX + FloorToInt(canvasRightExclusive * zoom) - Config.Common.UnitCount;
            int bottom = OffsetY + FloorToInt(canvasBottomExclusive * zoom) - Config.Common.UnitCount;
            if (right < left) right = left;
            if (bottom < top) bottom = top;

            DrawHorizontalSpan(top, left, right, colorRgba, screenBuffer, screenWidth, screenHeight);
            if (bottom != top)
                DrawHorizontalSpan(bottom, left, right, colorRgba, screenBuffer, screenWidth, screenHeight);
            if (bottom - top > Config.Common.UnitCount)
            {
                DrawVerticalSpan(
                    left, top + Config.Common.UnitCount, bottom - Config.Common.UnitCount,
                    colorRgba, screenBuffer, screenWidth, screenHeight);
                if (right != left)
                    DrawVerticalSpan(
                        right, top + Config.Common.UnitCount, bottom - Config.Common.UnitCount,
                        colorRgba, screenBuffer, screenWidth, screenHeight);
            }
        }

        private static void DrawHorizontalSpan(
            int y,
            int fromX,
            int toX,
            uint colorRgba,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            if ((uint)y >= (uint)screenHeight) return;
            if (fromX < Config.Common.FirstIndex) fromX = Config.Common.FirstIndex;
            if (toX >= screenWidth) toX = screenWidth - Config.Common.UnitCount;
            int rowStart = y * screenWidth;
            for (int x = fromX; x <= toX; x++)
                FastMemory.BlendColor(ref screenBuffer[rowStart + x], colorRgba);
        }

        private static void DrawVerticalSpan(
            int x,
            int fromY,
            int toY,
            uint colorRgba,
            uint[] screenBuffer,
            int screenWidth,
            int screenHeight)
        {
            if ((uint)x >= (uint)screenWidth) return;
            if (fromY < Config.Common.FirstIndex) fromY = Config.Common.FirstIndex;
            if (toY >= screenHeight) toY = screenHeight - Config.Common.UnitCount;
            for (int y = fromY; y <= toY; y++)
                FastMemory.BlendColor(ref screenBuffer[y * screenWidth + x], colorRgba);
        }

        // Truncation rounds toward zero; overlays need true floor so negative
        // canvas coordinates (off-canvas attachment origins) stay consistent.
        private static int FloorToInt(float value)
        {
            int truncated = (int)value;
            return value < truncated ? truncated - Config.Common.UnitCount : truncated;
        }
    }
}
