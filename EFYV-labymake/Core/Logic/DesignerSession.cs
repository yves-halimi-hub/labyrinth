using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public sealed class DesignerSessionSnapshot
    {
        // Item #27: validation is resolved lazily so building and publishing a
        // snapshot on every command costs nothing - the (possibly heavy) full
        // ProjectValidator.Validate runs only if a reader actually asks for the
        // result, and never inside the per-command hot path. The session caches
        // the computed result behind a dirty flag, so many snapshots resolve to
        // ONE validation per edit; a reader always gets a current result.
        private readonly Lazy<ProjectValidationResult> validation;

        public string ProjectName { get; }
        public bool IsDirty { get; }
        public long ChangeVersion { get; }
        public int AnimationIndex { get; }
        public int FrameIndex { get; }
        public DateTimeOffset? LastSavedAt { get; }
        public ProjectValidationResult Validation => validation.Value;
        public CommandHistorySnapshot History { get; }
        public AutosaveSnapshot Autosave { get; }
        public LiveDebugSnapshot LiveDebug { get; }
        public Exception PersistenceException { get; }

        internal DesignerSessionSnapshot(
            string projectName,
            bool isDirty,
            long changeVersion,
            int animationIndex,
            int frameIndex,
            DateTimeOffset? lastSavedAt,
            Lazy<ProjectValidationResult> validation,
            CommandHistorySnapshot history,
            AutosaveSnapshot autosave,
            LiveDebugSnapshot liveDebug,
            Exception persistenceException)
        {
            ProjectName = projectName;
            IsDirty = isDirty;
            ChangeVersion = changeVersion;
            AnimationIndex = animationIndex;
            FrameIndex = frameIndex;
            LastSavedAt = lastSavedAt;
            this.validation = validation;
            History = history;
            Autosave = autosave;
            LiveDebug = liveDebug;
            PersistenceException = persistenceException;
        }
    }

    public sealed class DesignerSession : IDisposable
    {
        private readonly object gate = new object();
        private readonly ToolbarAPI toolbar;
        private readonly ProjectValidator validator;
        private readonly ProjectPersistenceService persistence;
        private readonly AutosaveController autosave;
        private readonly LiveDebugController liveDebug;
        private readonly PreviewController preview;
        private readonly CommandManager history;
        private readonly IDebounceScheduler clock;
        private readonly SynchronizationContext eventContext;

        private Frame gestureFrame;
        private FrameEditCapture gestureBefore;
        private ITool gestureTool;
        private bool gestureActive;
        private SelectionRegion selection;
        private FloatingSelection floating;
        private FrameEditCapture floatingBefore;
        private Frame floatingFrame;
        private int floatingLayerIndex = Config.Common.NotFoundIndex;
        private bool floatingDragActive;
        private int floatingDragLastX;
        private int floatingDragLastY;
        private FloatingSelection clipboard;
        private bool disposed;
        private bool isDirty;
        private long changeVersion;
        private DateTimeOffset? lastSavedAt;
        private Exception persistenceException;
        private ProjectValidationResult validation;
        // Item #27: set by MarkDirty, cleared when a reader resolves validation.
        // Lets the per-command hot path skip the synchronous full validate and
        // defer it to the next reader (compute-on-demand). Guarded by `gate`.
        private bool validationDirty;
        private int animationIndex = Config.Common.NotFoundIndex;
        private int frameIndex = Config.Common.NotFoundIndex;

        public event Action<DesignerSessionSnapshot> StateChanged;

        public EFYVProject Project { get; private set; }
        public string ProjectName { get; private set; }
        public ITool ActiveTool { get; set; }
        public bool AutosaveEnabled { get; set; } = Config.Persistence.DefaultAutosaveEnabled;
        public PreviewController Preview => preview;
        public LiveDebugController LiveDebug => liveDebug;
        public CommandManager History => history;
        public DesignerSessionSnapshot Current => CreateSnapshot();

        // Transient (non-persisted, non-undoable) selection state: the region the
        // selection tools produced, the lifted/pasted buffer being previewed, and
        // the copy/paste clipboard. Any structural or history mutation cancels
        // the floating buffer (restoring the lifted pixels) and clears the
        // region, so the floating capture can never diff against a reshaped
        // frame. Only AnchorFloating commits - as ONE sparse FrameEditCommand.
        public SelectionRegion Selection => selection;
        public FloatingSelection Floating => floating;
        public bool HasClipboard => clipboard != null;

        public Frame CurrentFrame
        {
            get
            {
                if (animationIndex < Config.Common.FirstIndex || animationIndex >= Project.Animations.Count)
                    return null;
                AnimationState animation = Project.Animations[animationIndex];
                return frameIndex >= Config.Common.FirstIndex && frameIndex < animation.Frames.Count
                    ? animation.Frames[frameIndex]
                    : null;
            }
        }

        public DesignerSession(
            string projectName,
            EFYVProject project,
            ToolbarAPI toolbar,
            ProjectValidator validator,
            ProjectPersistenceService persistence,
            AutosaveController autosave,
            LiveDebugController liveDebug,
            PreviewController preview,
            CommandManager history,
            IDebounceScheduler clock,
            SynchronizationContext eventContext = null)
        {
            if (string.IsNullOrWhiteSpace(projectName)) throw new ArgumentException(nameof(projectName));
            Project = project ?? throw new ArgumentNullException(nameof(project));
            this.toolbar = toolbar ?? throw new ArgumentNullException(nameof(toolbar));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
            this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            this.autosave = autosave ?? throw new ArgumentNullException(nameof(autosave));
            this.liveDebug = liveDebug ?? throw new ArgumentNullException(nameof(liveDebug));
            this.preview = preview ?? throw new ArgumentNullException(nameof(preview));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.eventContext = eventContext;
            ProjectName = projectName;
            persistence.GetProjectPath(projectName);

            validation = validator.Validate(Project);
            SelectFirstFrame();
            autosave.StateChanged += HandleAutosaveChanged;
            liveDebug.StateChanged += HandleLiveDebugChanged;
            history.HistoryChanged += HandleHistoryChanged;
        }

        // subElementResolver (item #6, optional): lets export/live-debug
        // flatten sub-element attachments into the atlas pixels; hosts pass
        // their AssetBankManager. Without one, attachments still export as
        // structured metadata but cannot be flattened.
        public static DesignerSession Create(
            string projectName,
            EFYVProject project,
            string projectDirectory,
            ISubElementResolver subElementResolver = null)
        {
            var schema = new AssetSchemaService();
            var toolbar = new ToolbarAPI(schema);
            var validator = new ProjectValidator(schema);
            var persistence = new ProjectPersistenceService(projectDirectory, schema);
            var autosave = new AutosaveController(persistence);
            var liveDebug = new LiveDebugController(
                new EFYVLabyMake.Core.Export.ExportEngine(validator, subElementResolver),
                validator);
            return new DesignerSession(
                projectName,
                project,
                toolbar,
                validator,
                persistence,
                autosave,
                liveDebug,
                new PreviewController(),
                new CommandManager(),
                new TaskDebounceScheduler(),
                SynchronizationContext.Current);
        }

        public bool SelectFrame(int selectedAnimationIndex, int selectedFrameIndex)
        {
            ThrowIfDisposed();
            if (gestureActive || selectedAnimationIndex < Config.Common.FirstIndex ||
                selectedAnimationIndex >= Project.Animations.Count) return false;
            AnimationState animation = Project.Animations[selectedAnimationIndex];
            if (animation == null || selectedFrameIndex < Config.Common.FirstIndex ||
                selectedFrameIndex >= animation.Frames.Count)
                return false;

            DropTransientSelectionState();
            animationIndex = selectedAnimationIndex;
            frameIndex = selectedFrameIndex;
            Publish();
            return true;
        }

        public bool PointerDown(int x, int y)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame;
            if (gestureActive) return false;

            if (floating != null)
            {
                if (floating.HitTest(x, y))
                {
                    floatingDragActive = true;
                    floatingDragLastX = x;
                    floatingDragLastY = y;
                    return true;
                }
                // Clicking outside the floating buffer anchors it in place; the
                // click itself is consumed rather than starting a tool gesture.
                AnchorFloating();
                return true;
            }

            var selectionTool = ActiveTool as EFYVLabyMake.Core.Tools.ISelectionTool;
            if (selectionTool != null && selection != null && selection.Contains(x, y))
            {
                // Dragging from inside the selection moves it: lift (cut) into a
                // floating buffer and start dragging it.
                if (LiftSelection(selectionTool.ActiveLayerIndex, true))
                {
                    floatingDragActive = true;
                    floatingDragLastX = x;
                    floatingDragLastY = y;
                    return true;
                }
            }

            if (ActiveTool == null || frame == null) return false;

            ApplyPaletteConstraint(ActiveTool);

            gestureFrame = frame;
            gestureTool = ActiveTool;
            gestureBefore = FrameEditCapture.Capture(frame, gestureTool);
            gestureActive = true;
            try
            {
                gestureTool.OnPointerDown(Project, frame, x, y);
                return true;
            }
            catch
            {
                RollbackGesture();
                ClearGesture();
                throw;
            }
        }

        public bool PointerDrag(int x, int y)
        {
            ThrowIfDisposed();
            if (floatingDragActive)
            {
                MoveFloating(x - floatingDragLastX, y - floatingDragLastY);
                floatingDragLastX = x;
                floatingDragLastY = y;
                return true;
            }
            if (!gestureActive) return false;
            try
            {
                gestureTool.OnPointerDrag(Project, gestureFrame, x, y);
                return true;
            }
            catch
            {
                RollbackGesture();
                ClearGesture();
                throw;
            }
        }

        public bool PointerUp(int x, int y)
        {
            ThrowIfDisposed();
            if (floatingDragActive)
            {
                floatingDragActive = false;
                return true;
            }
            if (!gestureActive) return false;

            try
            {
                gestureTool.OnPointerUp(Project, gestureFrame, x, y);
                var selectionTool = gestureTool as EFYVLabyMake.Core.Tools.ISelectionTool;
                if (selectionTool != null)
                {
                    // Selection gestures replace the session selection (a gesture
                    // that selected nothing clears it) and never touch pixels, so
                    // the command diff below stays empty.
                    selection = selectionTool.TakeCompletedRegion();
                }
                var command = new FrameEditCommand(gestureFrame, gestureBefore);
                if (command.HasChanges)
                {
                    history.RecordExecutedCommand(command);
                    // Item #27: a hitbox-tool gesture touched only hitboxes (wire
                    // metadata), so the live loop can republish without redrawing
                    // the PNG; every other tool edits pixels.
                    MarkDirty(gestureTool is HitboxTool
                        ? DesignerDirtyScope.Hitboxes
                        : DesignerDirtyScope.Everything);
                }
                else if (selectionTool != null)
                {
                    Publish();
                }
                return command.HasChanges;
            }
            catch
            {
                RollbackGesture();
                throw;
            }
            finally
            {
                ClearGesture();
            }
        }

        public void CancelGesture()
        {
            ThrowIfDisposed();
            if (mapPaintActive)
            {
                // Esc during a map paint stroke restores every touched cell
                // (item #5) - the map analogue of a pixel-gesture rollback.
                CancelMapPaint();
                return;
            }
            if (floating != null)
            {
                // Esc while a floating buffer exists (dragging or hovering)
                // cancels the whole lift: the cut pixels return to the layer.
                CancelFloating();
                return;
            }
            if (!gestureActive) return;
            RollbackGesture();
            ClearGesture();
            Publish();
        }

        // --- Selection / floating buffer / clipboard --------------------------------

        public void ClearSelection()
        {
            ThrowIfDisposed();
            if (floating != null)
            {
                CancelFloating();
                return;
            }
            if (selection == null) return;
            selection = null;
            Publish();
        }

        // Copies the selected pixels of the given layer to the session clipboard
        // without mutating anything.
        public bool CopySelection(int layerIndex)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame;
            if (gestureActive || floating != null || selection == null || frame == null) return false;
            if (!IsLiftableLayer(frame, layerIndex)) return false;
            clipboard = ExtractSelectionBuffer(frame.Layers[layerIndex], selection, false);
            Publish();
            return true;
        }

        // Lifts the current selection off the given layer into a floating buffer.
        // removeSource=true is the move gesture (the source pixels become
        // transparent); false leaves the source intact (copy-drag). Nothing is
        // recorded in history yet: AnchorFloating commits the whole interaction
        // as ONE sparse FrameEditCommand, CancelFloating restores the capture.
        public bool LiftSelection(int layerIndex, bool removeSource)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame;
            if (gestureActive || floating != null || selection == null || frame == null) return false;
            if (!IsLiftableLayer(frame, layerIndex)) return false;

            floatingBefore = FrameEditCapture.CaptureLayer(frame, layerIndex);
            floating = ExtractSelectionBuffer(frame.Layers[layerIndex], selection, removeSource);
            floatingFrame = frame;
            floatingLayerIndex = layerIndex;
            selection = null;
            Publish();
            return true;
        }

        // Pastes the clipboard as a new floating buffer over the given layer of
        // the current frame, positioned where it was copied from.
        public bool PasteClipboard(int layerIndex)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame;
            if (gestureActive || floating != null || clipboard == null || frame == null) return false;
            if (layerIndex < Config.Common.FirstIndex || layerIndex >= frame.Layers.Count) return false;
            if (frame.Layers[layerIndex] == null) return false;

            floatingBefore = FrameEditCapture.CaptureLayer(frame, layerIndex);
            floating = clipboard.Clone();
            floatingFrame = frame;
            floatingLayerIndex = layerIndex;
            selection = null;
            Publish();
            return true;
        }

        public bool MoveFloating(int deltaX, int deltaY)
        {
            ThrowIfDisposed();
            if (floating == null) return false;
            if (deltaX == Config.Common.EmptyCount && deltaY == Config.Common.EmptyCount) return true;
            floating.OffsetX += deltaX;
            floating.OffsetY += deltaY;
            Publish();
            return true;
        }

        // Blends the floating buffer into its layer at the current offset and
        // records the entire lift-move-anchor interaction as one undoable
        // sparse-diff command against the capture taken at lift/paste time.
        public bool AnchorFloating()
        {
            ThrowIfDisposed();
            if (floating == null) return false;
            Frame frame = floatingFrame;
            if (floatingLayerIndex < Config.Common.FirstIndex ||
                floatingLayerIndex >= frame.Layers.Count)
            {
                CancelFloating();
                return false;
            }

            Layer layer = frame.Layers[floatingLayerIndex];
            for (int localY = Config.Common.FirstIndex; localY < floating.Height; localY++)
            {
                int sourceRow = localY * floating.Width;
                int destY = floating.OffsetY + localY;
                for (int localX = Config.Common.FirstIndex; localX < floating.Width; localX++)
                {
                    if (!floating.Mask[sourceRow + localX]) continue;
                    int destX = floating.OffsetX + localX;
                    if (destX < Config.Canvas.MinCoordinate || destY < Config.Canvas.MinCoordinate ||
                        destX >= layer.Width || destY >= layer.Height) continue;
                    uint blended = layer.GetPixel(destX, destY).Rgba;
                    EFYVBackend.Core.Memory.FastMemory.BlendColor(
                        ref blended,
                        floating.Pixels[sourceRow + localX]);
                    layer.SetPixel(destX, destY, new PixelColor { Rgba = blended });
                }
            }

            FrameEditCommand command;
            try
            {
                command = new FrameEditCommand(frame, floatingBefore);
            }
            catch
            {
                // Same contract as gesture rollback: restore the capture and
                // surface exactly one exception.
                floatingBefore.Restore(frame);
                ClearFloatingState();
                Publish();
                throw;
            }

            ClearFloatingState();
            if (command.HasChanges)
            {
                history.RecordExecutedCommand(command);
                MarkDirty();
            }
            else
            {
                Publish();
            }
            return true;
        }

        // Discards the floating buffer and restores the lifted pixels.
        public void CancelFloating()
        {
            ThrowIfDisposed();
            if (floating == null) return;
            floatingBefore.Restore(floatingFrame);
            ClearFloatingState();
            Publish();
        }

        // --- Canvas resize ------------------------------------------------------------

        // Command-backed canvas resize/crop: rebuilds every frame of every
        // animation at the new size with the existing content anchored per the
        // 9-position anchor (hitboxes translate with the content and clamp to
        // the new canvas), updates the project canvas size, and records the
        // whole operation as one undoable command. This is the ONLY supported
        // way to change the canvas size of a project that has frames - the raw
        // EFYVProject setters are internal because they desync existing layer
        // buffers.
        public void ResizeCanvas(int newWidth, int newHeight, CanvasAnchor anchor)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            if (newWidth <= Config.Canvas.MinCoordinate ||
                newWidth > Config.Persistence.MaxCanvasDimension)
                throw new ArgumentOutOfRangeException(nameof(newWidth));
            if (newHeight <= Config.Canvas.MinCoordinate ||
                newHeight > Config.Persistence.MaxCanvasDimension)
                throw new ArgumentOutOfRangeException(nameof(newHeight));
            int alignmentX;
            int alignmentY;
            GetAnchorAlignments(anchor, out alignmentX, out alignmentY);

            EFYVProject project = Project;
            int previousWidth = project.CanvasWidth;
            int previousHeight = project.CanvasHeight;
            if (newWidth == previousWidth && newHeight == previousHeight) return;

            DropTransientSelectionState();

            int offsetX = ComputeAnchorShift(alignmentX, previousWidth, newWidth);
            int offsetY = ComputeAnchorShift(alignmentY, previousHeight, newHeight);

            var swaps = new List<FrameListSwap>(project.Animations.Count);
            long estimatedBytes = Config.Command.EstimatedCommandOverheadBytes;
            // Item #33: a directional project's INACTIVE facing sets resize in
            // the same command - all facings share the canvas size, so leaving
            // them behind would fail every facing's dimension check at export.
            foreach (AnimationState animation in project.EnumerateAllAnimations())
            {
                if (animation == null) continue;
                var before = new List<Frame>(animation.Frames);
                var after = new List<Frame>(animation.Frames.Count);
                foreach (Frame frame in animation.Frames)
                {
                    if (frame == null)
                    {
                        after.Add(null);
                        continue;
                    }
                    Frame resized = BuildResizedFrame(frame, newWidth, newHeight, offsetX, offsetY);
                    after.Add(resized);
                    estimatedBytes += EstimateFrameBytes(frame) + EstimateFrameBytes(resized);
                }
                swaps.Add(new FrameListSwap(animation, before, after));
            }

            Action apply = () =>
            {
                project.CanvasWidth = newWidth;
                project.CanvasHeight = newHeight;
                foreach (FrameListSwap swap in swaps)
                {
                    swap.Animation.Frames.Clear();
                    swap.Animation.Frames.AddRange(swap.After);
                }
            };
            Action revert = () =>
            {
                project.CanvasWidth = previousWidth;
                project.CanvasHeight = previousHeight;
                foreach (FrameListSwap swap in swaps)
                {
                    swap.Animation.Frames.Clear();
                    swap.Animation.Frames.AddRange(swap.Before);
                }
            };

            apply();
            history.RecordExecutedCommand(new DelegateCommand(apply, revert, estimatedBytes));
            MarkDirty();
        }

        private sealed class FrameListSwap
        {
            public AnimationState Animation { get; }
            public List<Frame> Before { get; }
            public List<Frame> After { get; }

            public FrameListSwap(AnimationState animation, List<Frame> before, List<Frame> after)
            {
                Animation = animation;
                Before = before;
                After = after;
            }
        }

        // Alignment codes for one axis of the 9-anchor grid.
        private const int AlignStart = 0;
        private const int AlignCenter = 1;
        private const int AlignEnd = 2;

        private static void GetAnchorAlignments(CanvasAnchor anchor, out int horizontal, out int vertical)
        {
            switch (anchor)
            {
                case CanvasAnchor.TopLeft: horizontal = AlignStart; vertical = AlignStart; return;
                case CanvasAnchor.TopCenter: horizontal = AlignCenter; vertical = AlignStart; return;
                case CanvasAnchor.TopRight: horizontal = AlignEnd; vertical = AlignStart; return;
                case CanvasAnchor.MiddleLeft: horizontal = AlignStart; vertical = AlignCenter; return;
                case CanvasAnchor.MiddleCenter: horizontal = AlignCenter; vertical = AlignCenter; return;
                case CanvasAnchor.MiddleRight: horizontal = AlignEnd; vertical = AlignCenter; return;
                case CanvasAnchor.BottomLeft: horizontal = AlignStart; vertical = AlignEnd; return;
                case CanvasAnchor.BottomCenter: horizontal = AlignCenter; vertical = AlignEnd; return;
                case CanvasAnchor.BottomRight: horizontal = AlignEnd; vertical = AlignEnd; return;
                default: throw new ArgumentOutOfRangeException(nameof(anchor));
            }
        }

        // How far existing content shifts (new position = old position + shift).
        // Center splits an odd difference with integer division (truncation
        // toward zero), biasing toward the top-left for both grow and crop.
        private static int ComputeAnchorShift(int alignment, int oldSize, int newSize)
        {
            switch (alignment)
            {
                case AlignStart: return Config.Common.EmptyCount;
                case AlignCenter: return (newSize - oldSize) / Config.Canvas.AnchorCenterDivisor;
                default: return newSize - oldSize;
            }
        }

        private static Frame BuildResizedFrame(Frame source, int newWidth, int newHeight, int offsetX, int offsetY)
        {
            var resized = new Frame(newWidth, newHeight, source.FrameIndex);
            resized.Layers.Clear();
            foreach (Layer layer in source.Layers)
            {
                if (layer == null)
                {
                    resized.Layers.Add(null);
                    continue;
                }
                var movedLayer = new Layer(layer.Name, newWidth, newHeight);
                movedLayer.IsVisible = layer.IsVisible;
                movedLayer.Opacity = layer.Opacity;
                CopyAnchoredPixels(layer, movedLayer, offsetX, offsetY);
                resized.Layers.Add(movedLayer);
            }

            // Item #6: attachments ride along with the anchored content -
            // their anchor points shift by the same offsets (no clamping: an
            // attachment may legally hang off-canvas; flattening clips).
            foreach (SubElementAttachment attachment in source.Attachments)
            {
                if (attachment == null)
                {
                    resized.Attachments.Add(null);
                    continue;
                }
                SubElementAttachment moved = attachment.Clone();
                moved.X += offsetX;
                moved.Y += offsetY;
                resized.Attachments.Add(moved);
            }

            resized.Hitboxes.Clear();
            float pixelsPerUnit = Config.Hitbox.PixelsPerUnit;
            float shiftX = offsetX / pixelsPerUnit;
            float shiftY = offsetY / pixelsPerUnit;
            float maxX = newWidth / pixelsPerUnit;
            float maxY = newHeight / pixelsPerUnit;
            foreach (KeyValuePair<string, EFYVBackend.Core.Models.HitboxData> pair in source.Hitboxes)
            {
                EFYVBackend.Core.Models.HitboxData hitbox = pair.Value;
                float left = EFYVBackend.Core.Math.FastMath.FastClamp(
                    hitbox.X + shiftX, Config.Common.ZeroFloat, maxX);
                float right = EFYVBackend.Core.Math.FastMath.FastClamp(
                    hitbox.X + shiftX + hitbox.Width, left, maxX);
                float top = EFYVBackend.Core.Math.FastMath.FastClamp(
                    hitbox.Y + shiftY, Config.Common.ZeroFloat, maxY);
                float bottom = EFYVBackend.Core.Math.FastMath.FastClamp(
                    hitbox.Y + shiftY + hitbox.Height, top, maxY);
                hitbox.X = left;
                hitbox.Y = top;
                hitbox.Width = right - left;
                hitbox.Height = bottom - top;
                resized.Hitboxes[pair.Key] = hitbox;
            }
            return resized;
        }

        private static void CopyAnchoredPixels(Layer source, Layer destination, int offsetX, int offsetY)
        {
            int sourceStartX = EFYVBackend.Core.Math.FastMath.FastMax(Config.Common.EmptyCount, -offsetX);
            int sourceEndX = EFYVBackend.Core.Math.FastMath.FastMin(source.Width, destination.Width - offsetX);
            int sourceStartY = EFYVBackend.Core.Math.FastMath.FastMax(Config.Common.EmptyCount, -offsetY);
            int sourceEndY = EFYVBackend.Core.Math.FastMath.FastMin(source.Height, destination.Height - offsetY);

            for (int y = sourceStartY; y < sourceEndY; y++)
            {
                int sourceRow = y * source.Width;
                int destinationRow = (y + offsetY) * destination.Width + offsetX;
                for (int x = sourceStartX; x < sourceEndX; x++)
                    destination.Pixels[destinationRow + x] = source.Pixels[sourceRow + x];
            }
        }

        private static bool IsLiftableLayer(Frame frame, int layerIndex)
        {
            if (layerIndex < Config.Common.FirstIndex || layerIndex >= frame.Layers.Count) return false;
            Layer layer = frame.Layers[layerIndex];
            return layer != null;
        }

        private static FloatingSelection ExtractSelectionBuffer(
            Layer layer,
            SelectionRegion region,
            bool removeSource)
        {
            var pixels = new uint[checked(region.Width * region.Height)];
            var mask = new bool[pixels.Length];
            var transparent = new PixelColor { Rgba = Config.Color.TransparentPixelRgba };
            bool[] regionMask = region.Mask;
            for (int localY = Config.Common.FirstIndex; localY < region.Height; localY++)
            {
                int row = localY * region.Width;
                int canvasY = region.Y + localY;
                for (int localX = Config.Common.FirstIndex; localX < region.Width; localX++)
                {
                    if (!regionMask[row + localX]) continue;
                    int canvasX = region.X + localX;
                    mask[row + localX] = true;
                    pixels[row + localX] = layer.GetPixel(canvasX, canvasY).Rgba;
                    if (removeSource) layer.SetPixel(canvasX, canvasY, transparent);
                }
            }
            return new FloatingSelection(region.X, region.Y, region.Width, region.Height, pixels, mask);
        }

        private void ClearFloatingState()
        {
            floating = null;
            floatingBefore = null;
            floatingFrame = null;
            floatingLayerIndex = Config.Common.NotFoundIndex;
            floatingDragActive = false;
        }

        // Structural and history mutations invalidate both the selection
        // geometry and the floating capture; the un-anchored buffer is CANCELED
        // (its lifted pixels restored), never silently committed.
        private void DropTransientSelectionState()
        {
            if (floating != null)
            {
                floatingBefore.Restore(floatingFrame);
                ClearFloatingState();
            }
            selection = null;
        }

        // --- Palette and color workflow (item #8) -------------------------------------
        //
        // Palette CRUD is index-addressed, undoable, and marks the project
        // dirty like every other structural session mutation. Palette
        // operations never touch frames or layers, so - unlike frame/layer
        // CRUD - they deliberately do NOT cancel an un-anchored floating
        // selection. Recent-color tracking (NotifyColorUsed / TrySelectSwatch)
        // is persisted state but NOT undoable: color selection history is not
        // part of the document edit history (standard editor behavior),
        // matching the non-undoable active tool/color themselves.

        public Palette AddPalette(string name)
        {
            ThrowIfDisposed();
            if (Project.Palettes.Count >= Config.Palette.MaxPalettes)
                throw new InvalidOperationException();
            var palette = new Palette(name);
            int insertionIndex = Project.Palettes.Count;
            Project.Palettes.Add(palette);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Palettes.Insert(insertionIndex, palette),
                () => Project.Palettes.RemoveAt(insertionIndex),
                EstimatePaletteBytes(palette)));
            // Item #27: palettes/recent colors are not part of the export at all.
            MarkDirty(DesignerDirtyScope.Properties);
            return palette;
        }

        public void RemovePalette(int index)
        {
            ThrowIfDisposed();
            ValidatePaletteIndex(index);
            Palette removed = Project.Palettes[index];
            Project.Palettes.RemoveAt(index);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Palettes.RemoveAt(index),
                () => Project.Palettes.Insert(index, removed),
                EstimatePaletteBytes(removed)));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        public void RenamePalette(int index, string name)
        {
            ThrowIfDisposed();
            ValidatePaletteIndex(index);
            Palette palette = Project.Palettes[index];
            string previous = palette.Name;
            if (string.Equals(previous, name, StringComparison.Ordinal)) return;
            palette.Name = name;
            history.RecordExecutedCommand(new DelegateCommand(
                () => palette.Name = name,
                () => palette.Name = previous,
                Config.Command.EstimatedCommandOverheadBytes +
                    ((long)previous.Length + name.Length) * sizeof(char)));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        public void AddSwatch(int paletteIndex, uint rgba)
        {
            ThrowIfDisposed();
            ValidatePaletteIndex(paletteIndex);
            Palette palette = Project.Palettes[paletteIndex];
            if (palette.Colors.Count >= Config.Palette.MaxSwatchesPerPalette)
                throw new InvalidOperationException();
            int insertionIndex = palette.Colors.Count;
            palette.Colors.Add(rgba);
            history.RecordExecutedCommand(new DelegateCommand(
                () => palette.Colors.Insert(insertionIndex, rgba),
                () => palette.Colors.RemoveAt(insertionIndex),
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        public void RemoveSwatch(int paletteIndex, int swatchIndex)
        {
            ThrowIfDisposed();
            ValidatePaletteIndex(paletteIndex);
            Palette palette = Project.Palettes[paletteIndex];
            ValidateSwatchIndex(palette, swatchIndex);
            uint removed = palette.Colors[swatchIndex];
            palette.Colors.RemoveAt(swatchIndex);
            history.RecordExecutedCommand(new DelegateCommand(
                () => palette.Colors.RemoveAt(swatchIndex),
                () => palette.Colors.Insert(swatchIndex, removed),
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        public void MoveSwatch(int paletteIndex, int fromIndex, int toIndex)
        {
            ThrowIfDisposed();
            ValidatePaletteIndex(paletteIndex);
            Palette palette = Project.Palettes[paletteIndex];
            ValidateSwatchIndex(palette, fromIndex);
            ValidateSwatchIndex(palette, toIndex);
            if (fromIndex == toIndex) return;
            Move(palette.Colors, fromIndex, toIndex);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Move(palette.Colors, fromIndex, toIndex),
                () => Move(palette.Colors, toIndex, fromIndex),
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        // Swatch selection: resolves the swatch color, applies it to the
        // active tool when that tool exposes a brush color (IColorTool), and
        // records it as recently used. The out value lets a host update its
        // own color UI even when the active tool is not a color tool. Returns
        // false (without throwing) for invalid indices - selection is a
        // UI-driven operation and stale indices are expected.
        public bool TrySelectSwatch(int paletteIndex, int swatchIndex, out uint rgba)
        {
            ThrowIfDisposed();
            rgba = Config.Color.TransparentPixelRgba;
            if (paletteIndex < Config.Common.FirstIndex ||
                paletteIndex >= Project.Palettes.Count) return false;
            Palette palette = Project.Palettes[paletteIndex];
            if (palette == null || swatchIndex < Config.Common.FirstIndex ||
                swatchIndex >= palette.Colors.Count) return false;

            rgba = palette.Colors[swatchIndex];
            var colorTool = ActiveTool as IColorTool;
            if (colorTool != null) colorTool.CurrentColor = new PixelColor { Rgba = rgba };
            NotifyColorUsed(rgba);
            return true;
        }

        // Records a color as recently used (most-recent-first ring persisted
        // in .efyvmake). Dirty-marks only when the ring actually changed, so
        // re-using the current color is a complete no-op. Never records
        // history (see the section comment above).
        public void NotifyColorUsed(uint rgba)
        {
            ThrowIfDisposed();
            if (Project.RecentColors.Push(rgba)) MarkDirty(DesignerDirtyScope.Properties);
        }

        // Undoable global color swap: every pixel exactly equal to fromRgba
        // becomes toRgba on EVERY layer (hidden and zero-opacity layers
        // included - the swap is a data operation, not a visual one) of either
        // the current frame (allFrames=false; throws InvalidOperationException
        // when no frame is selected) or all frames of all animations. The
        // whole swap is ONE history entry with sparse per-layer diffs; a layer
        // shared by several frames in scope is swapped exactly once. Returns
        // the number of replaced pixels; 0 means nothing matched and no
        // history entry was recorded. An un-anchored floating selection is
        // canceled (restored) first so the swap sees the true layer bytes.
        public int ReplaceColor(uint fromRgba, uint toRgba, bool allFrames)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            if (fromRgba == toRgba) return Config.Common.EmptyCount;

            var scopeFrames = new List<Frame>();
            if (allFrames)
            {
                // Item #33: "all frames" spans every facing of a directional
                // project - the palette is shared, so a global swap that
                // skipped parked facings would silently fork the colorway.
                foreach (AnimationState animation in Project.EnumerateAllAnimations())
                {
                    if (animation == null) continue;
                    foreach (Frame frame in animation.Frames)
                    {
                        if (frame != null) scopeFrames.Add(frame);
                    }
                }
            }
            else
            {
                Frame frame = CurrentFrame ?? throw new InvalidOperationException();
                scopeFrames.Add(frame);
            }

            DropTransientSelectionState();

            var visitedLayers = new HashSet<Layer>();
            var swaps = new List<ColorSwapCommand.LayerSwap>();
            int replaced = Config.Common.EmptyCount;
            foreach (Frame frame in scopeFrames)
            {
                foreach (Layer layer in frame.Layers)
                {
                    if (layer == null || !visitedLayers.Add(layer)) continue;
                    List<int> indices = null;
                    PixelColor[] pixels = layer.Pixels;
                    for (int index = Config.Common.FirstIndex; index < pixels.Length; index++)
                    {
                        if (pixels[index].Rgba != fromRgba) continue;
                        if (indices == null) indices = new List<int>();
                        indices.Add(index);
                    }
                    if (indices == null) continue;
                    swaps.Add(new ColorSwapCommand.LayerSwap(layer, indices.ToArray()));
                    replaced += indices.Count;
                }
            }

            if (replaced == Config.Common.EmptyCount)
            {
                Publish();
                return replaced;
            }

            var command = new ColorSwapCommand(swaps, fromRgba, toRgba);
            command.Execute();
            history.RecordExecutedCommand(command);
            MarkDirty();
            return replaced;
        }

        // Palette-constraint mode: when enabled, starting a drawing gesture
        // snaps the active color tool's brush color to the NEAREST entry of
        // the active palette before the tool sees the pointer (metric:
        // squared-Euclidean over straight RGBA with ties to the lowest index -
        // see Palette.FindNearestIndex). The snap persistently updates the
        // tool color (the host sees the snapped value), it is not a per-stroke
        // override. No-ops when disabled, when the active palette index is
        // stale/empty, or when the active tool has no brush color (eraser,
        // eyedropper, selection tools).
        public bool PaletteConstraintEnabled { get; set; }
        public int ActivePaletteIndex { get; set; } = Config.Palette.DefaultActivePaletteIndex;

        private void ApplyPaletteConstraint(ITool tool)
        {
            if (!PaletteConstraintEnabled) return;
            var colorTool = tool as IColorTool;
            if (colorTool == null) return;
            if (ActivePaletteIndex < Config.Common.FirstIndex ||
                ActivePaletteIndex >= Project.Palettes.Count) return;
            Palette palette = Project.Palettes[ActivePaletteIndex];
            if (palette == null || palette.Colors.Count == Config.Common.EmptyCount) return;

            uint current = colorTool.CurrentColor.Rgba;
            int nearest = Palette.FindNearestIndex(palette.Colors, current);
            uint snapped = palette.Colors[nearest];
            if (snapped != current)
                colorTool.CurrentColor = new PixelColor { Rgba = snapped };
        }

        private void ValidatePaletteIndex(int index)
        {
            if (index < Config.Common.FirstIndex || index >= Project.Palettes.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void ValidateSwatchIndex(Palette palette, int index)
        {
            if (index < Config.Common.FirstIndex || index >= palette.Colors.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static long EstimatePaletteBytes(Palette palette)
        {
            return Config.Command.EstimatedCommandOverheadBytes +
                ((long)palette.Name.Length * sizeof(char)) +
                ((long)palette.Colors.Count * sizeof(uint));
        }

        // --- Linked directional authoring (item #33) ----------------------------------
        //
        // A directional project holds all four facings; the ACTIVE facing's
        // animations live in Project.Animations (every existing tool/command
        // path keeps working on them) and the other three sets are parked in
        // Project.Directional. Enabling the mode, switching facings, and
        // mirror-generating a facing are all UNDOABLE commands: history is
        // strictly LIFO, so commands recorded against one facing's list are
        // always replayed with that facing swapped back in.

        public bool IsDirectionalProject => Project.Directional != null;
        public string ActiveFacing => Project.Directional?.ActiveFacing;

        // Converts the current project into a linked 4-direction project: the
        // existing animations become the ACTIVE facing's set (taken from the
        // current facing property when valid, DefaultActiveFacing otherwise)
        // and the other three facings start empty. Requires a directional-
        // capable asset type (LivingEntityData family). Undoable.
        public void EnableDirectionalAuthoring()
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            if (Project.Directional != null) throw new InvalidOperationException();
            SchemaDefinition definition;
            if (!validator.SchemaService.TryGetTypeDefinition(Project.TargetAssetType, out definition) ||
                !definition.IsDirectional)
                throw new InvalidOperationException();

            DropTransientSelectionState();
            object previousFacingValue;
            bool hadFacingValue = Project.AssetProperties.TryGetValue(
                Config.Entity.KeyFacing,
                out previousFacingValue);
            string facingProperty = previousFacingValue as string;
            string activeFacing = DirectionalState.IsFacingName(facingProperty)
                ? facingProperty
                : Config.Entity.DefaultActiveFacing;

            var state = new DirectionalState(activeFacing);
            Action apply = () =>
            {
                Project.Directional = state;
                Project.AssetProperties[Config.Entity.KeyFacing] = state.ActiveFacing;
            };
            Action revert = () =>
            {
                Project.Directional = null;
                if (hadFacingValue)
                    Project.AssetProperties[Config.Entity.KeyFacing] = previousFacingValue;
                else
                    Project.AssetProperties.Remove(Config.Entity.KeyFacing);
            };
            apply();
            history.RecordExecutedCommand(new DelegateCommand(
                apply,
                revert,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        // Switches the visible facing (an undoable command; the animation
        // sets swap by reference, no pixels are copied). The facing property
        // tracks the active facing so single-facing consumers stay coherent.
        // Returns false (recording nothing) when the facing is already active.
        public bool SwitchFacing(string facing)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            DirectionalState state = Project.Directional;
            if (state == null) throw new InvalidOperationException();
            if (!DirectionalState.IsFacingName(facing)) throw new ArgumentException(nameof(facing));
            if (string.Equals(facing, state.ActiveFacing, StringComparison.Ordinal)) return false;

            if (mapPaintActive) CancelMapPaint();
            DropTransientSelectionState();
            string previousFacing = state.ActiveFacing;
            Action apply = () => SwitchFacingCore(facing);
            Action revert = () => SwitchFacingCore(previousFacing);
            apply();
            history.RecordExecutedCommand(new DelegateCommand(
                apply,
                revert,
                Config.Command.EstimatedCommandOverheadBytes));
            SelectFirstFrame();
            MarkDirty();
            return true;
        }

        private void SwitchFacingCore(string facing)
        {
            Project.Directional.Switch(Project, facing);
            Project.AssetProperties[Config.Entity.KeyFacing] = facing;
        }

        // Mirror-generates one horizontal facing from the other (Left from
        // Right or Right from Left): deep-clones every source animation with
        // pixels flipped horizontally, hitboxes X-mirrored, and attachments
        // X-mirrored with FlipX toggled, then REPLACES the target facing's
        // set as one undoable command. Returns the generated animation count
        // (0 - recording nothing - when source and target are both empty).
        public int GenerateMirroredFacing(string sourceFacing, string targetFacing)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            DirectionalState state = Project.Directional;
            if (state == null) throw new InvalidOperationException();
            if (!DirectionalState.IsFacingName(sourceFacing)) throw new ArgumentException(nameof(sourceFacing));
            if (!DirectionalState.IsFacingName(targetFacing)) throw new ArgumentException(nameof(targetFacing));
            bool horizontalPair =
                (sourceFacing == Config.Entity.FacingLeft && targetFacing == Config.Entity.FacingRight) ||
                (sourceFacing == Config.Entity.FacingRight && targetFacing == Config.Entity.FacingLeft);
            if (!horizontalPair) throw new ArgumentException(nameof(targetFacing));

            IReadOnlyList<AnimationState> source = Project.GetFacingAnimations(sourceFacing);
            var mirrored = new List<AnimationState>(source.Count);
            long estimatedBytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (AnimationState animation in source)
            {
                if (animation == null) continue;
                AnimationState clone = MirrorAnimationHorizontally(animation);
                mirrored.Add(clone);
                estimatedBytes += EstimateAnimationBytes(clone);
            }

            var before = new List<AnimationState>(Project.GetFacingAnimations(targetFacing));
            if (mirrored.Count == Config.Common.EmptyCount &&
                before.Count == Config.Common.EmptyCount)
                return Config.Common.EmptyCount;
            foreach (AnimationState animation in before)
            {
                if (animation != null) estimatedBytes += EstimateAnimationBytes(animation);
            }

            DropTransientSelectionState();
            Action apply = () => SetFacingAnimations(targetFacing, mirrored);
            Action revert = () => SetFacingAnimations(targetFacing, before);
            apply();
            history.RecordExecutedCommand(new DelegateCommand(apply, revert, estimatedBytes));
            if (string.Equals(targetFacing, state.ActiveFacing, StringComparison.Ordinal))
                SelectFirstFrame();
            MarkDirty();
            return mirrored.Count;
        }

        private void SetFacingAnimations(string facing, List<AnimationState> animations)
        {
            if (string.Equals(facing, Project.Directional.ActiveFacing, StringComparison.Ordinal))
            {
                Project.Animations.Clear();
                Project.Animations.AddRange(animations);
            }
            else
            {
                Project.Directional.SetInactiveSet(facing, new List<AnimationState>(animations));
            }
        }

        private static AnimationState MirrorAnimationHorizontally(AnimationState source)
        {
            var mirrored = new AnimationState(source.StateName, source.FPS);
            mirrored.LoopStartFrame = source.LoopStartFrame;
            mirrored.LoopEndFrame = source.LoopEndFrame;
            mirrored.PingPong = source.PingPong;
            mirrored.Effects.AddRange(source.Effects);
            foreach (Frame frame in source.Frames)
                mirrored.Frames.Add(frame == null ? null : MirrorFrameHorizontally(frame));
            return mirrored;
        }

        private static Frame MirrorFrameHorizontally(Frame source)
        {
            Frame mirrored = source.Clone();
            foreach (Layer layer in mirrored.Layers)
            {
                if (layer != null) MirrorLayerPixelsHorizontally(layer);
            }

            // Hitboxes live in world units measured from the LEFT edge; the
            // mirrored left edge is the span minus the old right edge.
            float spanUnits = mirrored.Width / Config.Hitbox.PixelsPerUnit;
            var mirroredHitboxes =
                new List<KeyValuePair<string, EFYVBackend.Core.Models.HitboxData>>(mirrored.Hitboxes.Count);
            foreach (KeyValuePair<string, EFYVBackend.Core.Models.HitboxData> pair in mirrored.Hitboxes)
            {
                EFYVBackend.Core.Models.HitboxData hitbox = pair.Value;
                hitbox.X = EFYVBackend.Core.Math.FastMath.FastClamp(
                    spanUnits - hitbox.X - hitbox.Width,
                    Config.Common.ZeroFloat,
                    spanUnits);
                mirroredHitboxes.Add(new KeyValuePair<string, EFYVBackend.Core.Models.HitboxData>(pair.Key, hitbox));
            }
            mirrored.Hitboxes.Clear();
            foreach (KeyValuePair<string, EFYVBackend.Core.Models.HitboxData> pair in mirroredHitboxes)
                mirrored.Hitboxes[pair.Key] = pair.Value;

            // Attachment anchors mirror around the pixel grid; toggling FlipX
            // makes the referenced sub-element render mirrored as well.
            foreach (SubElementAttachment attachment in mirrored.Attachments)
            {
                if (attachment == null) continue;
                attachment.X = mirrored.Width - Config.Common.UnitCount - attachment.X;
                attachment.FlipX = !attachment.FlipX;
            }
            return mirrored;
        }

        private static void MirrorLayerPixelsHorizontally(Layer layer)
        {
            PixelColor[] pixels = layer.Pixels;
            int width = layer.Width;
            for (int y = Config.Common.FirstIndex; y < layer.Height; y++)
            {
                int row = y * width;
                int left = row;
                int right = row + width - Config.Common.UnitCount;
                while (left < right)
                {
                    PixelColor swap = pixels[left];
                    pixels[left] = pixels[right];
                    pixels[right] = swap;
                    left++;
                    right--;
                }
            }
        }

        public PropertyEditResult SetProperty(string fieldName, object value)
        {
            ThrowIfDisposed();
            // Item #33: in directional mode the facing property IS the facing
            // switcher - routing it through SwitchFacing keeps the parked
            // animation sets, the property, and the undo history in lockstep.
            if (Project.Directional != null &&
                string.Equals(fieldName, Config.Entity.KeyFacing, StringComparison.Ordinal))
            {
                string facing = value as string;
                if (!DirectionalState.IsFacingName(facing))
                    return new PropertyEditResult(
                        PropertyEditStatus.InvalidChoice,
                        fieldName,
                        SchemaValueKind.Text);
                SwitchFacing(facing);
                return new PropertyEditResult(PropertyEditStatus.Success, fieldName, SchemaValueKind.Text);
            }
            object previousValue;
            bool hadPreviousValue = Project.AssetProperties.TryGetValue(fieldName, out previousValue);
            PropertyEditResult result = toolbar.TrySetProperty(Project, fieldName, value);
            if (!result.Succeeded) return result;

            object nextValue = Project.AssetProperties[fieldName];
            if (hadPreviousValue && Equals(previousValue, nextValue)) return result;
            history.RecordExecutedCommand(new PropertyEditCommand(
                Project.AssetProperties,
                fieldName,
                hadPreviousValue,
                previousValue,
                nextValue));
            // Item #27: an asset property rides in the .efyvlaby, never the PNG.
            MarkDirty(DesignerDirtyScope.Properties);
            return result;
        }

        public bool Undo()
        {
            ThrowIfDisposed();
            // An open map paint stroke (item #5) would diff against a grid the
            // replayed command is about to rewrite; cancel it like the
            // transient selection state below.
            if (mapPaintActive) CancelMapPaint();
            DropTransientSelectionState();
            bool undone;
            try
            {
                undone = history.Undo();
            }
            finally
            {
                // Runs even when the replayed command throws (see the
                // CommandManager.CommandFailed contract), so a failed replay cannot
                // strand the selection on a removed frame.
                EnsureSelectionValid();
            }
            if (!undone) return false;
            MarkDirty();
            return true;
        }

        public bool Redo()
        {
            ThrowIfDisposed();
            if (mapPaintActive) CancelMapPaint();
            DropTransientSelectionState();
            bool redone;
            try
            {
                redone = history.Redo();
            }
            finally
            {
                EnsureSelectionValid();
            }
            if (!redone) return false;
            MarkDirty();
            return true;
        }

        public void AddAnimation(AnimationState animation)
        {
            ThrowIfDisposed();
            if (animation == null) throw new ArgumentNullException(nameof(animation));
            DropTransientSelectionState();
            int insertionIndex = Project.Animations.Count;
            Project.Animations.Add(animation);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Animations.Insert(insertionIndex, animation),
                () => Project.Animations.RemoveAt(insertionIndex),
                EstimateAnimationBytes(animation)));
            animationIndex = insertionIndex;
            frameIndex = animation.Frames.Count > Config.Common.EmptyCount
                ? Config.Common.FirstIndex
                : Config.Common.NotFoundIndex;
            MarkDirty();
        }

        public AnimationState GenerateAnimation(MovingTool tool)
        {
            ThrowIfDisposed();
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            Frame baseFrame = CurrentFrame ?? throw new InvalidOperationException();
            // Restore any un-anchored floating pixels BEFORE flattening the base
            // frame, so generation never bakes a half-moved selection.
            DropTransientSelectionState();
            AnimationState generated = tool.GenerateAnimation(baseFrame);
            int existingIndex = Project.Animations.FindIndex(
                animation => string.Equals(animation.StateName, generated.StateName, StringComparison.Ordinal));
            if (existingIndex == Config.Common.NotFoundIndex)
            {
                AddAnimation(generated);
                return generated;
            }

            // Layer-preserving regenerate (item #10): only the generated layer
            // is replaced per frame; manual touch-up layers, hitboxes, frame
            // durations, and playback tags of the existing animation survive.
            AnimationState previous = Project.Animations[existingIndex];
            AnimationState merged = AnimationGeneratorAPI.MergeOntoTargetLayer(
                previous,
                generated,
                Config.Animation.GeneratedLayerName);
            Project.Animations[existingIndex] = merged;
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Animations[existingIndex] = merged,
                () => Project.Animations[existingIndex] = previous,
                Math.Max(EstimateAnimationBytes(previous), EstimateAnimationBytes(merged))));
            animationIndex = existingIndex;
            frameIndex = merged.Frames.Count > Config.Common.EmptyCount
                ? Config.Common.FirstIndex
                : Config.Common.NotFoundIndex;
            MarkDirty();
            return merged;
        }

        public AnimationState DuplicateAnimation(int index)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            DropTransientSelectionState();
            AnimationState clone = Project.Animations[index].Clone();
            int insertionIndex = index + EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep;
            Project.Animations.Insert(insertionIndex, clone);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Animations.Insert(insertionIndex, clone),
                () => Project.Animations.RemoveAt(insertionIndex),
                EstimateAnimationBytes(clone)));
            animationIndex = insertionIndex;
            frameIndex = clone.Frames.Count > Config.Common.EmptyCount
                ? Config.Common.FirstIndex
                : Config.Common.NotFoundIndex;
            MarkDirty();
            return clone;
        }

        public void RemoveAnimation(int index)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            DropTransientSelectionState();
            AnimationState removed = Project.Animations[index];
            Project.Animations.RemoveAt(index);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Animations.RemoveAt(index),
                () => Project.Animations.Insert(index, removed),
                EstimateAnimationBytes(removed)));
            SelectFirstFrame();
            MarkDirty();
        }

        public void MoveAnimation(int fromIndex, int toIndex)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(fromIndex);
            ValidateAnimationIndex(toIndex);
            if (fromIndex == toIndex) return;
            DropTransientSelectionState();
            Move(Project.Animations, fromIndex, toIndex);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Move(Project.Animations, fromIndex, toIndex),
                () => Move(Project.Animations, toIndex, fromIndex),
                Config.Command.EstimatedCommandOverheadBytes));
            animationIndex = toIndex;
            MarkDirty();
        }

        public void RenameAnimation(int index, string name)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            AnimationState animation = Project.Animations[index];
            string previous = animation.StateName;
            if (string.Equals(previous, name, StringComparison.Ordinal)) return;
            animation.StateName = name;
            history.RecordExecutedCommand(new DelegateCommand(
                () => animation.StateName = name,
                () => animation.StateName = previous,
                Config.Command.EstimatedCommandOverheadBytes +
                    ((long)(previous?.Length ?? Config.Common.EmptyCount) + name.Length) * sizeof(char)));
            MarkDirty();
        }

        public void SetAnimationFps(int index, int fps)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            if (fps <= Config.Common.EmptyCount) throw new ArgumentOutOfRangeException(nameof(fps));
            AnimationState animation = Project.Animations[index];
            int previous = animation.FPS;
            if (previous == fps) return;
            animation.FPS = fps;
            history.RecordExecutedCommand(new DelegateCommand(
                () => animation.FPS = fps,
                () => animation.FPS = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            // Item #27: playback tags ride in the .efyvlaby metadata, not the PNG.
            MarkDirty(DesignerDirtyScope.Properties);
        }

        // --- Item #10: per-frame durations and playback tags -----------------------

        // Sets one frame's duration override (milliseconds) on the SELECTED
        // animation as an undoable command. InheritFrameDurationMs (0) restores
        // "derive from the animation FPS"; other values must sit inside
        // [MinFrameDurationMs .. MaxFrameDurationMs] (the Frame setter throws).
        public void SetFrameDurationMs(int index, int durationMs)
        {
            ThrowIfDisposed();
            AnimationState animation = GetSelectedAnimation();
            ValidateFrameIndex(animation, index);
            Frame frame = animation.Frames[index];
            int previous = frame.DurationMs;
            frame.DurationMs = durationMs;
            if (previous == durationMs) return;
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.DurationMs = durationMs,
                () => frame.DurationMs = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        // Sets an animation's loop range as ONE undoable command. loopEnd may
        // be FullRangeLoopEnd (-1) for "the last frame"; otherwise the range
        // must sit inside the animation's current frames with start <= end.
        public void SetAnimationLoopRange(int index, int loopStart, int loopEnd)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            AnimationState animation = Project.Animations[index];
            if (loopStart < Config.Common.FirstIndex ||
                (animation.Frames.Count > Config.Common.EmptyCount && loopStart >= animation.Frames.Count) ||
                (animation.Frames.Count == Config.Common.EmptyCount && loopStart != Config.Common.FirstIndex))
                throw new ArgumentOutOfRangeException(nameof(loopStart));
            if (loopEnd != Config.Animation.FullRangeLoopEnd &&
                (loopEnd < loopStart || loopEnd >= animation.Frames.Count))
                throw new ArgumentOutOfRangeException(nameof(loopEnd));

            int previousStart = animation.LoopStartFrame;
            int previousEnd = animation.LoopEndFrame;
            if (previousStart == loopStart && previousEnd == loopEnd) return;
            animation.LoopStartFrame = loopStart;
            animation.LoopEndFrame = loopEnd;
            history.RecordExecutedCommand(new DelegateCommand(
                () => { animation.LoopStartFrame = loopStart; animation.LoopEndFrame = loopEnd; },
                () => { animation.LoopStartFrame = previousStart; animation.LoopEndFrame = previousEnd; },
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        public void SetAnimationPingPong(int index, bool pingPong)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            AnimationState animation = Project.Animations[index];
            bool previous = animation.PingPong;
            if (previous == pingPong) return;
            animation.PingPong = pingPong;
            history.RecordExecutedCommand(new DelegateCommand(
                () => animation.PingPong = pingPong,
                () => animation.PingPong = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        // --- Item #7: authored runtime-effect descriptors ---------------------------
        //
        // Index-addressed, undoable CRUD over AnimationState.Effects.
        // EffectDescriptor instances are immutable (the constructor is the
        // validation gate), so the commands only insert/remove/swap
        // references. Effect operations never touch frames or layers, so -
        // like the palette CRUD - they do not cancel an un-anchored floating
        // selection.

        public void AddAnimationEffect(int animationIndex, EffectDescriptor descriptor)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(animationIndex);
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            AnimationState animation = Project.Animations[animationIndex];
            if (animation.Effects.Count >= Config.Effect.MaxEffectsPerAnimation)
                throw new InvalidOperationException();

            int insertionIndex = animation.Effects.Count;
            animation.Effects.Add(descriptor);
            history.RecordExecutedCommand(new DelegateCommand(
                () => animation.Effects.Insert(insertionIndex, descriptor),
                () => animation.Effects.RemoveAt(insertionIndex),
                EstimateEffectBytes(descriptor)));
            // Item #27: effect descriptors ride in the .efyvlaby metadata.
            MarkDirty(DesignerDirtyScope.Properties);
        }

        public void RemoveAnimationEffect(int animationIndex, int effectIndex)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(animationIndex);
            AnimationState animation = Project.Animations[animationIndex];
            ValidateEffectIndex(animation, effectIndex);
            EffectDescriptor removed = animation.Effects[effectIndex];
            animation.Effects.RemoveAt(effectIndex);
            history.RecordExecutedCommand(new DelegateCommand(
                () => animation.Effects.RemoveAt(effectIndex),
                () => animation.Effects.Insert(effectIndex, removed),
                EstimateEffectBytes(removed)));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        public void ReplaceAnimationEffect(int animationIndex, int effectIndex, EffectDescriptor descriptor)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(animationIndex);
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            AnimationState animation = Project.Animations[animationIndex];
            ValidateEffectIndex(animation, effectIndex);
            EffectDescriptor previous = animation.Effects[effectIndex];
            if (ReferenceEquals(previous, descriptor)) return;
            animation.Effects[effectIndex] = descriptor;
            history.RecordExecutedCommand(new DelegateCommand(
                () => animation.Effects[effectIndex] = descriptor,
                () => animation.Effects[effectIndex] = previous,
                EstimateEffectBytes(descriptor)));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        private static void ValidateEffectIndex(AnimationState animation, int index)
        {
            if (index < Config.Common.FirstIndex || index >= animation.Effects.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        // --- Item #6: sub-element attachment CRUD ------------------------------------
        //
        // Index-addressed, undoable operations over CurrentFrame.Attachments.
        // Attachment records never touch pixels, so - like the palette and
        // effect CRUD - they do not cancel an un-anchored floating selection.
        // They DO refuse to run mid-gesture: the stamp tool's attachment mode
        // may be diffing the very same list inside the active gesture.

        public SubElementAttachment AddAttachment(
            string subElementName,
            int x,
            int y,
            int zOrder,
            bool flipX,
            bool flipY)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            if (frame.Attachments.Count >= Config.Attachment.MaxPerFrame)
                throw new InvalidOperationException();

            var attachment = new SubElementAttachment(subElementName, x, y, zOrder, flipX, flipY);
            int insertionIndex = frame.Attachments.Count;
            frame.Attachments.Add(attachment);
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.Attachments.Insert(insertionIndex, attachment),
                () => frame.Attachments.RemoveAt(insertionIndex),
                Config.Command.EstimatedCommandOverheadBytes +
                    ((long)subElementName.Length * sizeof(char))));
            MarkDirty();
            return attachment;
        }

        public void MoveAttachment(int index, int x, int y)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateAttachmentIndex(frame, index);
            SubElementAttachment attachment = frame.Attachments[index];
            int previousX = attachment.X;
            int previousY = attachment.Y;
            if (previousX == x && previousY == y) return;
            attachment.X = x;
            attachment.Y = y;
            history.RecordExecutedCommand(new DelegateCommand(
                () => { attachment.X = x; attachment.Y = y; },
                () => { attachment.X = previousX; attachment.Y = previousY; },
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        public void RemoveAttachment(int index)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateAttachmentIndex(frame, index);
            SubElementAttachment removed = frame.Attachments[index];
            frame.Attachments.RemoveAt(index);
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.Attachments.RemoveAt(index),
                () => frame.Attachments.Insert(index, removed),
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        private static void ValidateAttachmentIndex(Frame frame, int index)
        {
            if (index < Config.Common.FirstIndex || index >= frame.Attachments.Count ||
                frame.Attachments[index] == null)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static long EstimateEffectBytes(EffectDescriptor descriptor)
        {
            return Config.Command.EstimatedCommandOverheadBytes +
                ((long)(descriptor.Name.Length + descriptor.Trigger.Length +
                    descriptor.EffectType.Length) * sizeof(char));
        }

        // --- Item #5: tileset section (undoable CRUD) --------------------------------
        //
        // The tileset is an optional project section: a list of named tiles
        // all at one TileSize (see TilesetSection). Every mutation is one
        // history entry and marks the project dirty. Tileset operations never
        // touch frames or layers, so - like the palette CRUD - they do not
        // cancel an un-anchored floating selection; they DO refuse to run
        // mid-gesture or mid-map-paint.

        public TilesetSection CreateTileset(int tileSize)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            if (Project.Tileset != null) throw new InvalidOperationException();
            var section = new TilesetSection(tileSize);
            Project.Tileset = section;
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Tileset = section,
                () => Project.Tileset = null,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
            return section;
        }

        public void RemoveTileset()
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            TilesetSection removed = Project.Tileset ?? throw new InvalidOperationException();
            Project.Tileset = null;
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Tileset = null,
                () => Project.Tileset = removed,
                EstimateTilesetBytes(removed)));
            MarkDirty();
        }

        // Appends a blank (fully transparent) tile. The new tile's list index
        // is its FastGridMap tile id.
        public TilesetTile AddTilesetTile(string name)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            return AddTilesetTileCore(name, null);
        }

        // Appends a tile whose pixels are captured from the CURRENT frame:
        // the flattened top-left TileSize-square region (transparent padding
        // when the canvas is smaller). This is the "author a tile like a
        // mini-frame" loop: paint on the canvas (TileMakerTool wraps strokes
        // inside the tile cell), then capture it into the tileset.
        public TilesetTile AddTilesetTileFromCurrentFrame(string name)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            TilesetSection tileset = Project.Tileset ?? throw new InvalidOperationException();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            return AddTilesetTileCore(name, CaptureTilePixels(frame, tileset.TileSize));
        }

        private TilesetTile AddTilesetTileCore(string name, uint[] pixels)
        {
            TilesetSection tileset = Project.Tileset ?? throw new InvalidOperationException();
            if (tileset.Tiles.Count >= Config.Tileset.MaxTiles)
                throw new InvalidOperationException();
            var tile = new TilesetTile(name, tileset.TileSize, pixels);
            int insertionIndex = tileset.Tiles.Count;
            tileset.Tiles.Add(tile);
            history.RecordExecutedCommand(new DelegateCommand(
                () => tileset.Tiles.Insert(insertionIndex, tile),
                () => tileset.Tiles.RemoveAt(insertionIndex),
                EstimateTileBytes(tile)));
            MarkDirty();
            return tile;
        }

        // Removing a tile shifts every later tile's id down by one; map cells
        // painted with shifted ids re-address accordingly (the same semantics
        // the exported tile-ID manifest has). Undo restores the exact list.
        public void RemoveTilesetTile(int index)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            TilesetSection tileset = Project.Tileset ?? throw new InvalidOperationException();
            ValidateTilesetTileIndex(tileset, index);
            TilesetTile removed = tileset.Tiles[index];
            tileset.Tiles.RemoveAt(index);
            history.RecordExecutedCommand(new DelegateCommand(
                () => tileset.Tiles.RemoveAt(index),
                () => tileset.Tiles.Insert(index, removed),
                EstimateTileBytes(removed)));
            MarkDirty();
        }

        public void RenameTilesetTile(int index, string name)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            TilesetSection tileset = Project.Tileset ?? throw new InvalidOperationException();
            ValidateTilesetTileIndex(tileset, index);
            TilesetTile tile = tileset.Tiles[index];
            string previous = tile.Name;
            if (string.Equals(previous, name, StringComparison.Ordinal)) return;
            tile.Name = name;
            history.RecordExecutedCommand(new DelegateCommand(
                () => tile.Name = name,
                () => tile.Name = previous,
                Config.Command.EstimatedCommandOverheadBytes +
                    ((long)previous.Length + name.Length) * sizeof(char)));
            MarkDirty(DesignerDirtyScope.Properties);
        }

        private static void ValidateTilesetTileIndex(TilesetSection tileset, int index)
        {
            if (index < Config.Common.FirstIndex || index >= tileset.Tiles.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static uint[] CaptureTilePixels(Frame frame, int tileSize)
        {
            PixelColor[] flattened = frame.FlattenLayers();
            var pixels = new uint[checked(tileSize * tileSize)];
            int copyWidth = Math.Min(tileSize, frame.Width);
            int copyHeight = Math.Min(tileSize, frame.Height);
            for (int y = Config.Common.FirstIndex; y < copyHeight; y++)
            {
                int sourceRow = y * frame.Width;
                int destinationRow = y * tileSize;
                for (int x = Config.Common.FirstIndex; x < copyWidth; x++)
                    pixels[destinationRow + x] = flattened[sourceRow + x].Rgba;
            }
            return pixels;
        }

        private static long EstimateTileBytes(TilesetTile tile)
        {
            return Config.Command.EstimatedCommandOverheadBytes +
                ((long)tile.Pixels.Length * sizeof(uint)) +
                ((long)tile.Name.Length * sizeof(char));
        }

        private static long EstimateTilesetBytes(TilesetSection tileset)
        {
            long bytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (TilesetTile tile in tileset.Tiles) bytes += EstimateTileBytes(tile);
            return bytes;
        }

        // --- Item #5: map section editing (undoable commands) ------------------------
        //
        // The map is an optional project section (short tile grid + prop
        // placements + tileset reference). EVERY mutation lands in history
        // and dirties/autosaves the project - the old MapTool path mutated a
        // free-floating FastGridMap and bypassed all three. Cell paint
        // strokes batch through Begin/Paint/EndMapPaint into ONE history
        // entry, mirroring pointer gestures; bulk operations and MapTool
        // gestures diff sparsely against a pre-operation snapshot.

        private bool mapPaintActive;
        private short mapPaintTileId;
        private Dictionary<int, short> mapPaintBefore;

        public bool MapPaintActive => mapPaintActive;

        public MapSection CreateMapSection(string mapId, int width, int height, string tilesetName)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            if (Project.Map != null) throw new InvalidOperationException();
            var section = new MapSection(mapId, width, height, tilesetName);
            Project.Map = section;
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Map = section,
                () => Project.Map = null,
                EstimateMapBytes(section)));
            MarkDirty();
            return section;
        }

        public void RemoveMapSection()
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            MapSection removed = Project.Map ?? throw new InvalidOperationException();
            Project.Map = null;
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Map = null,
                () => Project.Map = removed,
                EstimateMapBytes(removed)));
            MarkDirty();
        }

        public void SetMapTilesetName(string tilesetName)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            MapSection map = RequireMap();
            string previous = map.TilesetName;
            map.TilesetName = tilesetName;
            string next = map.TilesetName;
            if (string.Equals(previous, next, StringComparison.Ordinal)) return;
            history.RecordExecutedCommand(new DelegateCommand(
                () => map.TilesetName = next,
                () => map.TilesetName = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        // Writes one cell as one history entry. Returns false (and records
        // nothing) outside the map - lost strokes are surfaced, not dropped.
        public bool SetMapTile(int x, int y, short tileId)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            ValidateTileId(tileId);
            MapSection map = RequireMap();
            EFYVBackend.Core.Collections.FastGridMap grid = map.Grid;
            if ((uint)x >= (uint)grid.Width || (uint)y >= (uint)grid.Height) return false;
            int cellIndex = y * grid.Width + x;
            short before = grid.RawData[cellIndex];
            if (before == tileId) return true;
            grid.RawData[cellIndex] = tileId;
            history.RecordExecutedCommand(new MapEditCommand(
                grid,
                new[] { cellIndex },
                new[] { before },
                new[] { tileId },
                null));
            MarkDirty();
            return true;
        }

        // Bulk rectangle fill (FastGridMap.FillRect clipping rules) as ONE
        // sparse history entry. Returns FastGridMap.FillRect's count (cells
        // WRITTEN); the recorded diff covers only the cells that changed.
        public int FillMapRect(int x, int y, int rectWidth, int rectHeight, short tileId)
        {
            ValidateTileId(tileId);
            return RunMapMutation(grid => grid.FillRect(x, y, rectWidth, rectHeight, tileId));
        }

        // Scanline flood fill (FastGridMap.FloodFillTiles) as ONE sparse
        // history entry. Returns the number of cells changed.
        public int FloodFillMapTiles(int x, int y, short tileId)
        {
            ValidateTileId(tileId);
            return RunMapMutation(grid => grid.FloodFillTiles(x, y, tileId));
        }

        // Runs one MapTool gesture against the project map as ONE undoable
        // command (tile diff + appended props together). The tool's TargetMap
        // is pointed at the section grid and its seed is synced from the
        // project, exactly like the tool's own OnPointerDown path - so
        // recorded operations replay deterministically.
        public MapOperationResult ApplyMapTool(MapTool tool, int x, int y)
        {
            ThrowIfDisposed();
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            ThrowIfEditingBlocked();
            MapSection map = RequireMap();
            EFYVBackend.Core.Collections.FastGridMap grid = map.Grid;
            tool.TargetMap = grid;
            if (tool.Seed != Project.DesignerSeed) tool.Seed = Project.DesignerSeed;

            short[] beforeTiles = (short[])grid.RawData.Clone();
            int propCountBefore = grid.Props.Count;
            MapOperationResult result = tool.Execute(CurrentFrame, x, y);
            RecordMapChanges(grid, beforeTiles, propCountBefore);
            return result;
        }

        // --- Map paint stroke: one history entry per Begin..End interaction ---

        public bool BeginMapPaint(short tileId)
        {
            ThrowIfDisposed();
            ValidateTileId(tileId);
            if (gestureActive || mapPaintActive || Project.Map == null) return false;
            mapPaintActive = true;
            mapPaintTileId = tileId;
            mapPaintBefore = new Dictionary<int, short>();
            return true;
        }

        // Paints one cell of the active stroke; out-of-map cells report false
        // and write nothing. The first touch of each cell records its
        // original id so EndMapPaint can diff the whole stroke sparsely.
        public bool PaintMapCell(int x, int y)
        {
            ThrowIfDisposed();
            if (!mapPaintActive) return false;
            EFYVBackend.Core.Collections.FastGridMap grid = Project.Map.Grid;
            if ((uint)x >= (uint)grid.Width || (uint)y >= (uint)grid.Height) return false;
            int cellIndex = y * grid.Width + x;
            if (!mapPaintBefore.ContainsKey(cellIndex))
                mapPaintBefore[cellIndex] = grid.RawData[cellIndex];
            grid.RawData[cellIndex] = mapPaintTileId;
            return true;
        }

        // Commits the stroke as ONE MapEditCommand; a stroke that changed
        // nothing records nothing and returns false.
        public bool EndMapPaint()
        {
            ThrowIfDisposed();
            if (!mapPaintActive) return false;
            EFYVBackend.Core.Collections.FastGridMap grid = Project.Map.Grid;
            Dictionary<int, short> touched = mapPaintBefore;
            mapPaintActive = false;
            mapPaintBefore = null;

            var indices = new List<int>(touched.Count);
            foreach (KeyValuePair<int, short> pair in touched)
            {
                if (grid.RawData[pair.Key] != pair.Value) indices.Add(pair.Key);
            }
            if (indices.Count == Config.Common.EmptyCount)
            {
                Publish();
                return false;
            }
            indices.Sort();
            var beforeTiles = new short[indices.Count];
            var afterTiles = new short[indices.Count];
            for (int index = Config.Common.FirstIndex; index < indices.Count; index++)
            {
                beforeTiles[index] = touched[indices[index]];
                afterTiles[index] = grid.RawData[indices[index]];
            }
            history.RecordExecutedCommand(new MapEditCommand(
                grid,
                indices.ToArray(),
                beforeTiles,
                afterTiles,
                null));
            MarkDirty();
            return true;
        }

        // Aborts the stroke and restores every touched cell.
        public void CancelMapPaint()
        {
            ThrowIfDisposed();
            if (!mapPaintActive) return;
            EFYVBackend.Core.Collections.FastGridMap grid = Project.Map.Grid;
            foreach (KeyValuePair<int, short> pair in mapPaintBefore)
                grid.RawData[pair.Key] = pair.Value;
            mapPaintActive = false;
            mapPaintBefore = null;
            Publish();
        }

        private int RunMapMutation(Func<EFYVBackend.Core.Collections.FastGridMap, int> operation)
        {
            ThrowIfDisposed();
            ThrowIfEditingBlocked();
            MapSection map = RequireMap();
            EFYVBackend.Core.Collections.FastGridMap grid = map.Grid;
            short[] beforeTiles = (short[])grid.RawData.Clone();
            int propCountBefore = grid.Props.Count;
            int result = operation(grid);
            RecordMapChanges(grid, beforeTiles, propCountBefore);
            return result;
        }

        // Sparse diff of a completed map mutation: changed cells plus any
        // props APPENDED by the operation become one MapEditCommand. A no-op
        // mutation records nothing (and publishes so hosts refresh).
        private void RecordMapChanges(
            EFYVBackend.Core.Collections.FastGridMap grid,
            short[] beforeTiles,
            int propCountBefore)
        {
            short[] current = grid.RawData;
            var indices = new List<int>();
            for (int index = Config.Common.FirstIndex; index < current.Length; index++)
            {
                if (beforeTiles[index] != current[index]) indices.Add(index);
            }

            int appendedCount = grid.Props.Count - propCountBefore;
            EFYVBackend.Core.Collections.FastGridMap.MapPropData[] appendedProps;
            if (appendedCount <= Config.Common.EmptyCount)
            {
                appendedProps = Array.Empty<EFYVBackend.Core.Collections.FastGridMap.MapPropData>();
            }
            else
            {
                appendedProps = new EFYVBackend.Core.Collections.FastGridMap.MapPropData[appendedCount];
                for (int index = Config.Common.FirstIndex; index < appendedCount; index++)
                    appendedProps[index] = grid.Props[propCountBefore + index];
            }

            if (indices.Count == Config.Common.EmptyCount &&
                appendedProps.Length == Config.Common.EmptyCount)
            {
                Publish();
                return;
            }

            var before = new short[indices.Count];
            var after = new short[indices.Count];
            for (int index = Config.Common.FirstIndex; index < indices.Count; index++)
            {
                before[index] = beforeTiles[indices[index]];
                after[index] = current[indices[index]];
            }
            history.RecordExecutedCommand(new MapEditCommand(
                grid,
                indices.ToArray(),
                before,
                after,
                appendedProps));
            MarkDirty();
        }

        private MapSection RequireMap()
        {
            return Project.Map ?? throw new InvalidOperationException();
        }

        private static void ValidateTileId(short tileId)
        {
            if (tileId < Config.MapDocument.BlankTileId)
                throw new ArgumentOutOfRangeException(nameof(tileId));
        }

        private static long EstimateMapBytes(MapSection map)
        {
            return Config.Command.EstimatedCommandOverheadBytes +
                ((long)map.Grid.RawData.Length * sizeof(short)) +
                ((long)map.Grid.Props.Count * Config.Command.EstimatedCommandOverheadBytes);
        }

        // Structural session mutations must not run while a pointer gesture
        // OR a map paint stroke is open: both diff against captured state.
        private void ThrowIfEditingBlocked()
        {
            if (gestureActive || mapPaintActive) throw new InvalidOperationException();
        }

        // --- Item #7: destructive layer filters --------------------------------------
        //
        // Each filter runs the backend FastEffects primitive over ONE layer of
        // the current frame and commits through the same sparse
        // FrameEditCommand diff path as pointer gestures - so undo restores
        // the exact previous bytes and no-op filters record nothing. When a
        // selection region exists it MASKS the write-back (the filter still
        // samples the whole layer, standard region-filter semantics) and the
        // region survives the operation; an un-anchored floating buffer is
        // canceled first (its lifted pixels restored) so the filter sees the
        // true layer bytes. Returns true when the filter changed pixels and a
        // history entry was recorded.

        public bool ApplyBlurFilter(int layerIndex, int radius)
        {
            if (radius < Config.Filter.MinBlurRadius || radius > Config.Filter.MaxBlurRadius)
                throw new ArgumentOutOfRangeException(nameof(radius));
            return ApplyLayerFilterCore(layerIndex, (source, filtered, width, height) =>
            {
                unsafe
                {
                    fixed (uint* sourcePointer = source)
                    fixed (uint* filteredPointer = filtered)
                    {
                        EFYVBackend.Core.Memory.FastEffects.BoxBlur(
                            sourcePointer, filteredPointer, width, height, radius);
                    }
                }
            });
        }

        public bool ApplyOutlineFilter(int layerIndex, uint outlineRgba)
        {
            return ApplyLayerFilterCore(layerIndex, (source, filtered, width, height) =>
            {
                unsafe
                {
                    fixed (uint* sourcePointer = source)
                    fixed (uint* filteredPointer = filtered)
                    {
                        EFYVBackend.Core.Memory.FastEffects.Outline(
                            sourcePointer, filteredPointer, width, height, outlineRgba);
                    }
                }
            });
        }

        public bool ApplyGlowFilter(int layerIndex, uint glowRgba, int radius)
        {
            if (radius < Config.Filter.MinGlowRadius || radius > Config.Filter.MaxGlowRadius)
                throw new ArgumentOutOfRangeException(nameof(radius));
            return ApplyLayerFilterCore(layerIndex, (source, filtered, width, height) =>
            {
                unsafe
                {
                    fixed (uint* sourcePointer = source)
                    fixed (uint* filteredPointer = filtered)
                    {
                        EFYVBackend.Core.Memory.FastEffects.Glow(
                            sourcePointer, filteredPointer, width, height, glowRgba, radius);
                    }
                }
            });
        }

        public bool ApplyColorShiftFilter(
            int layerIndex,
            float hueDeltaDegrees,
            float saturationDelta,
            float valueDelta)
        {
            if (float.IsNaN(hueDeltaDegrees) || float.IsInfinity(hueDeltaDegrees))
                throw new ArgumentOutOfRangeException(nameof(hueDeltaDegrees));
            if (float.IsNaN(saturationDelta) ||
                saturationDelta < Config.Filter.MinColorComponentDelta ||
                saturationDelta > Config.Filter.MaxColorComponentDelta)
                throw new ArgumentOutOfRangeException(nameof(saturationDelta));
            if (float.IsNaN(valueDelta) ||
                valueDelta < Config.Filter.MinColorComponentDelta ||
                valueDelta > Config.Filter.MaxColorComponentDelta)
                throw new ArgumentOutOfRangeException(nameof(valueDelta));
            return ApplyLayerFilterCore(layerIndex, (source, filtered, width, height) =>
            {
                unsafe
                {
                    fixed (uint* sourcePointer = source)
                    fixed (uint* filteredPointer = filtered)
                    {
                        EFYVBackend.Core.Memory.FastEffects.ColorShift(
                            sourcePointer,
                            filteredPointer,
                            width,
                            height,
                            hueDeltaDegrees,
                            saturationDelta,
                            valueDelta);
                    }
                }
            });
        }

        private bool ApplyLayerFilterCore(int layerIndex, Action<uint[], uint[], int, int> filter)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, layerIndex);
            Layer layer = frame.Layers[layerIndex] ?? throw new InvalidOperationException();

            // Restore any un-anchored floating pixels so the filter sees the
            // true layer bytes; the selection region (if any) stays alive as
            // the write mask.
            if (floating != null) CancelFloating();
            SelectionRegion region = selection;

            FrameEditCapture capture = FrameEditCapture.CaptureLayer(frame, layerIndex);
            int pixelCount = layer.Pixels.Length;
            var source = new uint[pixelCount];
            for (int index = Config.Common.FirstIndex; index < pixelCount; index++)
                source[index] = layer.Pixels[index].Rgba;
            var filtered = new uint[pixelCount];
            filter(source, filtered, layer.Width, layer.Height);

            for (int y = Config.Common.FirstIndex; y < layer.Height; y++)
            {
                int row = y * layer.Width;
                for (int x = Config.Common.FirstIndex; x < layer.Width; x++)
                {
                    if (region != null && !region.Contains(x, y)) continue;
                    layer.Pixels[row + x].Rgba = filtered[row + x];
                }
            }

            var command = new FrameEditCommand(frame, capture);
            if (command.HasChanges)
            {
                history.RecordExecutedCommand(command);
                MarkDirty();
            }
            else
            {
                Publish();
            }
            return command.HasChanges;
        }

        // --- Item #10: cross-frame layer batch operations ---------------------------
        //
        // Frame keeps owning its own layer list (no shared layer identity);
        // these session APIs apply one layer operation to EVERY frame of the
        // SELECTED animation and record the whole batch as ONE undoable
        // command. Index-addressed variants require the index to be valid in
        // every frame up front, so the batch can never half-apply.

        // Appends a NEW layer (one instance per frame — layers are never shared
        // across frames) to every frame of the selected animation.
        public void AddLayerToAllFrames(string name)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            AnimationState animation = GetSelectedAnimation();
            if (animation.Frames.Count == Config.Common.EmptyCount) throw new InvalidOperationException();
            foreach (Frame frame in animation.Frames)
            {
                if (frame == null || frame.Layers.Count >= Config.Persistence.MaxLayersPerFrame)
                    throw new InvalidOperationException();
            }

            DropTransientSelectionState();
            var targets = new List<Frame>(animation.Frames);
            var added = new List<Layer>(targets.Count);
            long estimatedBytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (Frame frame in targets)
            {
                var layer = new Layer(name, frame.Width, frame.Height);
                added.Add(layer);
                estimatedBytes += EstimateLayerBytes(layer);
            }

            Action apply = () =>
            {
                for (int index = Config.Common.FirstIndex; index < targets.Count; index++)
                    targets[index].Layers.Add(added[index]);
            };
            Action revert = () =>
            {
                for (int index = Config.Common.FirstIndex; index < targets.Count; index++)
                    targets[index].Layers.Remove(added[index]);
            };
            apply();
            history.RecordExecutedCommand(new DelegateCommand(apply, revert, estimatedBytes));
            MarkDirty();
        }

        // Removes the layer at layerIndex from every frame. Every frame must
        // have that index AND keep at least one layer afterwards (the same
        // last-layer invariant RemoveLayer enforces per frame).
        public void RemoveLayerFromAllFrames(int layerIndex)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            AnimationState animation = GetSelectedAnimation();
            if (animation.Frames.Count == Config.Common.EmptyCount) throw new InvalidOperationException();
            foreach (Frame frame in animation.Frames)
            {
                if (frame == null ||
                    layerIndex < Config.Common.FirstIndex ||
                    layerIndex >= frame.Layers.Count ||
                    frame.Layers.Count <= EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep)
                    throw new InvalidOperationException();
            }

            DropTransientSelectionState();
            var targets = new List<Frame>(animation.Frames);
            var removed = new List<Layer>(targets.Count);
            long estimatedBytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (Frame frame in targets)
            {
                Layer layer = frame.Layers[layerIndex];
                removed.Add(layer);
                if (layer != null) estimatedBytes += EstimateLayerBytes(layer);
            }

            Action apply = () =>
            {
                foreach (Frame frame in targets) frame.Layers.RemoveAt(layerIndex);
            };
            Action revert = () =>
            {
                for (int index = Config.Common.FirstIndex; index < targets.Count; index++)
                    targets[index].Layers.Insert(layerIndex, removed[index]);
            };
            apply();
            history.RecordExecutedCommand(new DelegateCommand(apply, revert, estimatedBytes));
            MarkDirty();
        }

        // Renames the layer at layerIndex in every frame; undo restores each
        // frame's ORIGINAL (possibly divergent) name.
        public void RenameLayerInAllFrames(int layerIndex, string name)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            AnimationState animation = GetSelectedAnimation();
            if (animation.Frames.Count == Config.Common.EmptyCount) throw new InvalidOperationException();
            ValidateBatchLayerIndex(animation, layerIndex);

            var targets = new List<Frame>(animation.Frames);
            var previousNames = new List<string>(targets.Count);
            bool changed = false;
            long estimatedBytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (Frame frame in targets)
            {
                string previous = frame.Layers[layerIndex].Name;
                previousNames.Add(previous);
                if (!string.Equals(previous, name, StringComparison.Ordinal)) changed = true;
                estimatedBytes += ((long)(previous?.Length ?? Config.Common.EmptyCount) + name.Length) * sizeof(char);
            }
            if (!changed) return;

            // Dropping a floating capture restores the floated layer's captured
            // metadata (including its name), so re-read the before-values after
            // the drop or undo could restore a mid-lift name.
            DropTransientSelectionState();
            for (int index = Config.Common.FirstIndex; index < targets.Count; index++)
                previousNames[index] = targets[index].Layers[layerIndex].Name;
            Action apply = () =>
            {
                foreach (Frame frame in targets) frame.Layers[layerIndex].Name = name;
            };
            Action revert = () =>
            {
                for (int index = Config.Common.FirstIndex; index < targets.Count; index++)
                    targets[index].Layers[layerIndex].Name = previousNames[index];
            };
            apply();
            history.RecordExecutedCommand(new DelegateCommand(apply, revert, estimatedBytes));
            MarkDirty();
        }

        // Sets the visibility of the layer at layerIndex in every frame; undo
        // restores each frame's original flag.
        public void SetLayerVisibilityInAllFrames(int layerIndex, bool isVisible)
        {
            ThrowIfDisposed();
            if (gestureActive) throw new InvalidOperationException();
            AnimationState animation = GetSelectedAnimation();
            if (animation.Frames.Count == Config.Common.EmptyCount) throw new InvalidOperationException();
            ValidateBatchLayerIndex(animation, layerIndex);

            var targets = new List<Frame>(animation.Frames);
            var previousFlags = new List<bool>(targets.Count);
            bool changed = false;
            foreach (Frame frame in targets)
            {
                bool previous = frame.Layers[layerIndex].IsVisible;
                previousFlags.Add(previous);
                if (previous != isVisible) changed = true;
            }
            if (!changed) return;

            // Same capture-after-drop rule as the rename batch above.
            DropTransientSelectionState();
            for (int index = Config.Common.FirstIndex; index < targets.Count; index++)
                previousFlags[index] = targets[index].Layers[layerIndex].IsVisible;
            Action apply = () =>
            {
                foreach (Frame frame in targets) frame.Layers[layerIndex].IsVisible = isVisible;
            };
            Action revert = () =>
            {
                for (int index = Config.Common.FirstIndex; index < targets.Count; index++)
                    targets[index].Layers[layerIndex].IsVisible = previousFlags[index];
            };
            apply();
            history.RecordExecutedCommand(new DelegateCommand(
                apply,
                revert,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        private static void ValidateBatchLayerIndex(AnimationState animation, int layerIndex)
        {
            foreach (Frame frame in animation.Frames)
            {
                if (frame == null ||
                    layerIndex < Config.Common.FirstIndex ||
                    layerIndex >= frame.Layers.Count ||
                    frame.Layers[layerIndex] == null)
                    throw new InvalidOperationException();
            }
        }

        public Frame AddFrame()
        {
            ThrowIfDisposed();
            if (animationIndex < Config.Common.FirstIndex || animationIndex >= Project.Animations.Count)
                throw new InvalidOperationException();

            DropTransientSelectionState();
            AnimationState animation = Project.Animations[animationIndex];
            var frame = new Frame(Project.CanvasWidth, Project.CanvasHeight, animation.Frames.Count);
            int insertionIndex = animation.Frames.Count;
            animation.Frames.Add(frame);
            history.RecordExecutedCommand(new DelegateCommand(
                () => { animation.Frames.Insert(insertionIndex, frame); NormalizeFrameIndices(animation); },
                () => { animation.Frames.RemoveAt(insertionIndex); NormalizeFrameIndices(animation); },
                EstimateFrameBytes(frame)));
            frameIndex = insertionIndex;
            MarkDirty();
            return frame;
        }

        public Frame DuplicateFrame(int index)
        {
            ThrowIfDisposed();
            AnimationState animation = GetSelectedAnimation();
            ValidateFrameIndex(animation, index);
            DropTransientSelectionState();
            Frame clone = animation.Frames[index].Clone();
            int insertionIndex = index + EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep;
            clone.FrameIndex = insertionIndex;
            animation.Frames.Insert(insertionIndex, clone);
            NormalizeFrameIndices(animation);
            history.RecordExecutedCommand(new DelegateCommand(
                () => { animation.Frames.Insert(insertionIndex, clone); NormalizeFrameIndices(animation); },
                () => { animation.Frames.RemoveAt(insertionIndex); NormalizeFrameIndices(animation); },
                EstimateFrameBytes(clone)));
            frameIndex = insertionIndex;
            MarkDirty();
            return clone;
        }

        public void RemoveFrame(int index)
        {
            ThrowIfDisposed();
            AnimationState animation = GetSelectedAnimation();
            ValidateFrameIndex(animation, index);
            DropTransientSelectionState();
            Frame removed = animation.Frames[index];
            animation.Frames.RemoveAt(index);
            NormalizeFrameIndices(animation);
            history.RecordExecutedCommand(new DelegateCommand(
                () => { animation.Frames.RemoveAt(index); NormalizeFrameIndices(animation); },
                () => { animation.Frames.Insert(index, removed); NormalizeFrameIndices(animation); },
                EstimateFrameBytes(removed)));
            frameIndex = animation.Frames.Count == Config.Common.EmptyCount
                ? Config.Common.NotFoundIndex
                : Math.Min(index, animation.Frames.Count - 1);
            MarkDirty();
        }

        public void MoveFrame(int fromIndex, int toIndex)
        {
            ThrowIfDisposed();
            AnimationState animation = GetSelectedAnimation();
            ValidateFrameIndex(animation, fromIndex);
            ValidateFrameIndex(animation, toIndex);
            if (fromIndex == toIndex) return;
            DropTransientSelectionState();
            Move(animation.Frames, fromIndex, toIndex);
            NormalizeFrameIndices(animation);
            history.RecordExecutedCommand(new DelegateCommand(
                () => { Move(animation.Frames, fromIndex, toIndex); NormalizeFrameIndices(animation); },
                () => { Move(animation.Frames, toIndex, fromIndex); NormalizeFrameIndices(animation); },
                Config.Command.EstimatedCommandOverheadBytes));
            frameIndex = toIndex;
            MarkDirty();
        }

        public Layer AddLayer(string name)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            DropTransientSelectionState();
            var layer = new Layer(name, frame.Width, frame.Height);
            int insertionIndex = frame.Layers.Count;
            frame.Layers.Add(layer);
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.Layers.Insert(insertionIndex, layer),
                () => frame.Layers.RemoveAt(insertionIndex),
                EstimateLayerBytes(layer)));
            MarkDirty();
            return layer;
        }

        public Layer DuplicateLayer(int index)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            DropTransientSelectionState();
            Layer clone = frame.Layers[index].Clone();
            int insertionIndex = index + EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep;
            frame.Layers.Insert(insertionIndex, clone);
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.Layers.Insert(insertionIndex, clone),
                () => frame.Layers.RemoveAt(insertionIndex),
                EstimateLayerBytes(clone)));
            MarkDirty();
            return clone;
        }

        public void RemoveLayer(int index)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            if (frame.Layers.Count <= EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep)
                throw new InvalidOperationException();
            DropTransientSelectionState();
            Layer removed = frame.Layers[index];
            frame.Layers.RemoveAt(index);
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.Layers.RemoveAt(index),
                () => frame.Layers.Insert(index, removed),
                EstimateLayerBytes(removed)));
            MarkDirty();
        }

        public void MoveLayer(int fromIndex, int toIndex)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, fromIndex);
            ValidateLayerIndex(frame, toIndex);
            if (fromIndex == toIndex) return;
            DropTransientSelectionState();
            Move(frame.Layers, fromIndex, toIndex);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Move(frame.Layers, fromIndex, toIndex),
                () => Move(frame.Layers, toIndex, fromIndex),
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        public void RenameLayer(int index, string name)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            Layer layer = frame.Layers[index];
            string previous = layer.Name;
            if (string.Equals(previous, name, StringComparison.Ordinal)) return;
            DropTransientSelectionState();
            layer.Name = name;
            history.RecordExecutedCommand(new DelegateCommand(
                () => layer.Name = name,
                () => layer.Name = previous,
                Config.Command.EstimatedCommandOverheadBytes +
                    ((long)(previous?.Length ?? Config.Common.EmptyCount) + name.Length) * sizeof(char)));
            MarkDirty();
        }

        public void SetLayerVisibility(int index, bool isVisible)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            Layer layer = frame.Layers[index];
            bool previous = layer.IsVisible;
            if (previous == isVisible) return;
            DropTransientSelectionState();
            layer.IsVisible = isVisible;
            history.RecordExecutedCommand(new DelegateCommand(
                () => layer.IsVisible = isVisible,
                () => layer.IsVisible = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        public void SetLayerOpacity(int index, float opacity)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            Layer layer = frame.Layers[index];
            float previous = layer.Opacity;
            layer.Opacity = opacity;
            float normalized = layer.Opacity;
            if (previous == normalized) return;
            // Dropping a floating capture restores the floated layer's captured
            // metadata, so re-apply the new opacity afterwards.
            DropTransientSelectionState();
            layer.Opacity = normalized;
            history.RecordExecutedCommand(new DelegateCommand(
                () => layer.Opacity = normalized,
                () => layer.Opacity = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        public void LoadPreview(int selectedAnimationIndex)
        {
            ThrowIfDisposed();
            // Preview needs only STRUCTURAL validity (canvas/animation/frame/layer/
            // hitbox integrity): schema-level issues such as an empty identity name
            // must not block playback while sketching. Export keeps the full scope.
            ProjectValidationResult result = validator.Validate(
                Project,
                ProjectValidationScope.Structural);
            if (!result.IsValid) throw new ProjectValidationException(result);
            preview.Load(ProjectSnapshot.Capture(Project), selectedAnimationIndex);
        }

        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ProjectPersistenceSnapshot snapshot = ProjectPersistenceSnapshot.Capture(Project);
            long savedVersion;
            lock (gate) savedVersion = changeVersion;

            try
            {
                await Task.Run(
                    () => persistence.SaveProject(ProjectName, snapshot, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                lock (gate)
                {
                    lastSavedAt = clock.UtcNow;
                    persistenceException = null;
                    if (changeVersion == savedVersion) isDirty = false;
                }
                if (!isDirty) persistence.DeleteAutosave(ProjectName);
            }
            catch (Exception exception)
            {
                lock (gate) persistenceException = exception;
                Publish();
                throw;
            }
            Publish();
        }

        public void ReloadFromDisk(bool preferAutosave)
        {
            ThrowIfDisposed();
            CancelGesture();
            DropTransientSelectionState();
            autosave.Cancel();
            liveDebug.CancelPending();

            Project = preferAutosave && persistence.AutosaveExists(ProjectName)
                ? persistence.LoadAutosave(ProjectName)
                : persistence.LoadProject(ProjectName);
            history.Clear();
            changeVersion = Config.Common.EmptyCount;
            isDirty = false;
            persistenceException = null;
            validation = validator.Validate(Project);
            validationDirty = false;
            SelectFirstFrame();
            Publish();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            autosave.StateChanged -= HandleAutosaveChanged;
            liveDebug.StateChanged -= HandleLiveDebugChanged;
            history.HistoryChanged -= HandleHistoryChanged;
            autosave.Dispose();
            liveDebug.Dispose();
        }

        // Item #27: the scope declares what the command touched so the live
        // debug loop can take a metadata-only publish path when no exported
        // pixels changed. The default is Everything, so an untagged or legacy
        // call site never silently narrows to the fast path. Validation is NOT
        // recomputed here: it is deferred to the next reader (see
        // ResolveValidation), keeping the per-command cost off this hot path.
        private void MarkDirty(DesignerDirtyScope scope = DesignerDirtyScope.Everything)
        {
            lock (gate)
            {
                isDirty = true;
                changeVersion++;
                validationDirty = true;
            }
            if (AutosaveEnabled)
                autosave.Schedule(ProjectName, () => ProjectPersistenceSnapshot.Capture(Project));
            liveDebug.NotifyProjectChanged(() => Project, scope);
            Publish();
        }

        // Item #27: computes the project validation on demand, caching it behind
        // the dirty flag so repeated reads without an intervening edit reuse one
        // result. The validate runs OUTSIDE the gate lock (it can be heavy and
        // must not block the autosave/live-debug callback threads that also take
        // the gate). Readers that need a current result - Current, the shell
        // problems display via the published snapshot - all route through here;
        // LoadPreview and export validate their own scopes separately.
        private ProjectValidationResult ResolveValidation()
        {
            lock (gate)
            {
                if (!validationDirty && validation != null) return validation;
            }
            ProjectValidationResult computed = validator.Validate(Project);
            lock (gate)
            {
                if (validationDirty)
                {
                    validation = computed;
                    validationDirty = false;
                }
                return validation;
            }
        }

        // Rollback restores the captured before-state directly (never by constructing
        // a diffing FrameEditCommand, whose constructor faults on structural layer
        // mutations): a failing gesture therefore rolls back cleanly and surfaces
        // exactly ONE exception - the original failure - to the caller.
        private void RollbackGesture()
        {
            if (gestureFrame == null || gestureBefore == null) return;
            gestureBefore.Restore(gestureFrame);
        }

        private void ClearGesture()
        {
            gestureFrame = null;
            gestureBefore = null;
            gestureTool = null;
            gestureActive = false;
        }

        private void SelectFirstFrame()
        {
            animationIndex = Config.Common.NotFoundIndex;
            frameIndex = Config.Common.NotFoundIndex;
            for (int index = Config.Common.FirstIndex; index < Project.Animations.Count; index++)
            {
                if (Project.Animations[index]?.Frames.Count > Config.Common.EmptyCount)
                {
                    animationIndex = index;
                    frameIndex = Config.Common.FirstIndex;
                    return;
                }
            }
        }

        // Selection policy for history replay: Undo/Redo CLAMP the selection rather
        // than capturing/restoring it per command. The current indices are kept when
        // they still address a frame, the frame index is clamped into the selected
        // animation's range, and otherwise selection falls back to the first
        // animation that has frames (SelectFirstFrame). CurrentFrame is therefore
        // null after a history operation only when NO animation has frames.
        private void EnsureSelectionValid()
        {
            if (animationIndex >= Config.Common.FirstIndex && animationIndex < Project.Animations.Count)
            {
                AnimationState animation = Project.Animations[animationIndex];
                if (animation != null && animation.Frames.Count > Config.Common.EmptyCount)
                {
                    if (frameIndex < Config.Common.FirstIndex)
                        frameIndex = Config.Common.FirstIndex;
                    else if (frameIndex >= animation.Frames.Count)
                        frameIndex = animation.Frames.Count - 1;
                    return;
                }
            }
            SelectFirstFrame();
        }

        private DesignerSessionSnapshot CreateSnapshot()
        {
            lock (gate)
            {
                // Validation is captured as a lazy resolver, not computed here:
                // publishing a snapshot on every command stays cheap, and the
                // full validate runs only if a reader touches Validation (item
                // #27). ExecutionAndPublication mode makes concurrent reads safe.
                return new DesignerSessionSnapshot(
                    ProjectName,
                    isDirty,
                    changeVersion,
                    animationIndex,
                    frameIndex,
                    lastSavedAt,
                    new Lazy<ProjectValidationResult>(ResolveValidation),
                    history.Current,
                    autosave.Current,
                    liveDebug.Current,
                    persistenceException);
            }
        }

        private void HandleAutosaveChanged(AutosaveSnapshot snapshot) => Publish();
        private void HandleLiveDebugChanged(LiveDebugSnapshot snapshot) => Publish();
        private void HandleHistoryChanged(CommandHistorySnapshot snapshot) => Publish();

        private void Publish()
        {
            Action<DesignerSessionSnapshot> handler = StateChanged;
            if (handler == null) return;
            DesignerSessionSnapshot snapshot = CreateSnapshot();
            if (eventContext == null) handler(snapshot);
            else eventContext.Post(value => handler((DesignerSessionSnapshot)value), snapshot);
        }

        private static long EstimateAnimationBytes(AnimationState animation)
        {
            long bytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (var frame in animation.Frames) bytes += EstimateFrameBytes(frame);
            return bytes;
        }

        private static long EstimateFrameBytes(Frame frame)
        {
            long bytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (var layer in frame.Layers) bytes += EstimateLayerBytes(layer);
            return bytes;
        }

        private static long EstimateLayerBytes(Layer layer)
        {
            return Config.Command.EstimatedCommandOverheadBytes +
                ((long)layer.Pixels.Length * sizeof(uint));
        }

        private AnimationState GetSelectedAnimation()
        {
            ValidateAnimationIndex(animationIndex);
            return Project.Animations[animationIndex];
        }

        private void ValidateAnimationIndex(int index)
        {
            if (index < Config.Common.FirstIndex || index >= Project.Animations.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void ValidateFrameIndex(AnimationState animation, int index)
        {
            if (index < Config.Common.FirstIndex || index >= animation.Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void ValidateLayerIndex(Frame frame, int index)
        {
            if (index < Config.Common.FirstIndex || index >= frame.Layers.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void Move<T>(System.Collections.Generic.List<T> values, int fromIndex, int toIndex)
        {
            T value = values[fromIndex];
            values.RemoveAt(fromIndex);
            values.Insert(toIndex, value);
        }

        private static void NormalizeFrameIndices(AnimationState animation)
        {
            for (int index = Config.Common.FirstIndex; index < animation.Frames.Count; index++)
                animation.Frames[index].FrameIndex = index;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DesignerSession));
        }
    }
}
