using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.App.State
{
    public sealed class NewProjectRequest
    {
        public string ProjectName { get; }
        public string CategoryLabel { get; }
        public int CanvasWidth { get; }
        public int CanvasHeight { get; }
        public string ProjectDirectory { get; }

        public NewProjectRequest(
            string projectName,
            string categoryLabel,
            int canvasWidth,
            int canvasHeight,
            string projectDirectory)
        {
            ProjectName = projectName;
            CategoryLabel = categoryLabel;
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            ProjectDirectory = projectDirectory;
        }
    }

    public sealed class ResizeCanvasRequest
    {
        public int CanvasWidth { get; }
        public int CanvasHeight { get; }
        public CanvasAnchor Anchor { get; }

        public ResizeCanvasRequest(int canvasWidth, int canvasHeight, CanvasAnchor anchor)
        {
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            Anchor = anchor;
        }
    }

    // Item #5: what a map-mode pointer stroke does - paint the selected tile,
    // erase to blank, or flood-fill the touched region with the selected tile.
    public enum MapEditAction
    {
        Paint = 0,
        Erase = 1,
        Flood = 2
    }

    // The single view-model of the shell. Owns the DesignerSession, the tool
    // instances, and every bindable status property. All members must be used from
    // the Avalonia UI thread: DesignerSession is synchronous and not thread-safe,
    // and it is created here on the UI thread so its SynchronizationContext posts
    // autosave/live-debug events back to the UI thread.
    public sealed class EditorShell : ObservableObject, IDisposable
    {
        private readonly AssetSchemaService schemaService = new AssetSchemaService();
        private readonly ToolbarAPI toolbar;
        // Captured at construction (the UI thread): bank load failures can
        // fire from the live-debug export worker (the bank doubles as the
        // export-time attachment resolver), so the toast marshals here.
        private readonly System.Threading.SynchronizationContext uiContext =
            System.Threading.SynchronizationContext.Current;

        private readonly PencilTool pencil = new PencilTool();
        private readonly EraserTool eraser = new EraserTool();
        private readonly EyedropperTool eyedropper = new EyedropperTool();
        private readonly FillTool fill = new FillTool();
        private readonly LineTool line = new LineTool();
        private readonly RectangleTool rectShape = new RectangleTool();
        private readonly EllipseTool ellipseShape = new EllipseTool();
        private readonly RectSelectTool selectRect = new RectSelectTool();
        private readonly LassoSelectTool selectLasso = new LassoSelectTool();
        private readonly StampTool stamp = new StampTool();
        private readonly TileMakerTool tileMaker = new TileMakerTool();
        private readonly HitboxTool hitbox = new HitboxTool();
        private readonly MovingTool moving = new MovingTool();

        private DesignerSession session;
        private bool disposed;

        private string activeToolName = AppDefaults.ToolPencil;
        private uint currentColorRgba = AppDefaults.DefaultColorRgba;
        private uint[] recentColors = Array.Empty<uint>();
        private int brushSize = AppDefaults.DefaultBrushSizeInput;
        private bool isSaving;

        // Asset bank state (item #6): the per-project sub-element bank the
        // stamp tool draws from. Created with the project (a bank directory
        // next to the project files); load failures surface through the
        // shell's error toast via AssetBankManager.LoadFailed.
        private AssetBankManager assetBank;
        private IReadOnlyList<SubElement> bankSubElements = Array.Empty<SubElement>();
        private int selectedBankIndex = -1;

        // Map/tileset editing mode state (item #5). While map mode is active
        // the canvas renders the project's map section (via Core MapRenderer)
        // and pointer input paints/erases/flood-fills map CELLS through the
        // session's undoable map surface instead of driving pixel tools.
        private bool mapModeActive;
        private MapEditAction mapAction = MapEditAction.Paint;
        private bool isDirectionalProject;
        private string activeFacing = "";
        private int selectedMapTileIndex;
        private Frame mapSurfaceFrame;
        private uint[] mapSurfaceBuffer;

        // Designer overlay state (item #31). The user's toggle set lives in
        // overlayKinds; the single reusable settings object gets stamped with
        // the effective flag set and the per-render context (tile cell size,
        // attachment sources) by PrepareOverlayContext just before each
        // render. Checkerboard starts on - the pre-#31 shell always drew it.
        private readonly ViewportOverlaySettings overlaySettings = new ViewportOverlaySettings();
        private ViewportOverlayKind overlayKinds = ViewportOverlayKind.Checkerboard;

        // Timeline strip + onion skin state (item #10). The animation/frame
        // indices mirror the last session snapshot so the strip and the
        // duration editor always address the frame the canvas shows.
        private readonly OnionSkinSettings onionSettings = new OnionSkinSettings();
        private bool onionSkinEnabled;
        private int timelineAnimationIndex = -1;
        private int timelineFrameCount;
        private int timelineFrameIndex = -1;
        private int currentFrameDurationMs;

        private string projectLabel = "No project";
        private string frameLabel = "No frame";
        private string zoomLabel = "100%";
        private string dirtyLabel = "";
        private string historyLabel = "Undo 0 · Redo 0";
        private string validationLabel = "";
        private string errorMessage = "";
        private string noticeMessage = "";
        private bool canUndo;
        private bool canRedo;

        // Panel state (item #3). The active layer index (item #3.2) is the layer
        // every drawing/selection tool writes to and copy/paste addresses - it
        // closes the batch-3 "always layer 0" deferral by threading the panel's
        // selected layer through the shared tool instances instead of the
        // Config.Tool.DefaultLayerIndex constant. The rest mirror the latest
        // session snapshot so the panels rebind from one place.
        private readonly ILayerTool[] layerTools;
        private int activeLayerIndex = Config.Tool.DefaultLayerIndex;
        private int selectedAnimationIndex = Config.Common.NotFoundIndex;
        private IReadOnlyList<ProjectIssue> problems = Array.Empty<ProjectIssue>();
        private LiveDebugSnapshot liveDebugSnapshot;
        private bool isPushing;

        public event Action CanvasInvalidated;

        public EditorShell()
        {
            toolbar = new ToolbarAPI(schemaService);
            // The eyedropper samples the COMPOSITED canvas color (the exact
            // FlattenLayers value - see EyedropperTool); a completed pick
            // becomes the working color and lands in the recent ring.
            eyedropper.ColorPicked += picked => SetColor(picked.Rgba);
            // The tools that paint into a specific layer (item #3.2): the shell's
            // active-layer selection sets ActiveLayerIndex on every one of them.
            var candidates = new ITool[]
            {
                pencil, eraser, fill, line, rectShape, ellipseShape,
                selectRect, selectLasso, stamp, tileMaker
            };
            var collected = new List<ILayerTool>(candidates.Length);
            foreach (ITool tool in candidates)
            {
                if (tool is ILayerTool layerTool) collected.Add(layerTool);
            }
            layerTools = collected.ToArray();
            SetColor(AppDefaults.DefaultColorRgba);
            SetBrushSize(AppDefaults.DefaultBrushSizeInput);
        }

        public DesignerSession Session => session;
        public bool HasSession => session != null;
        public Frame CurrentFrame => session?.CurrentFrame;
        public FloatingSelection CurrentFloating => session?.Floating;

        // --- Designer overlays (item #31) -------------------------------------------

        public bool OverlayCheckerboard
        {
            get => GetOverlay(ViewportOverlayKind.Checkerboard);
            set => SetOverlay(ViewportOverlayKind.Checkerboard, value, nameof(OverlayCheckerboard));
        }

        public bool OverlayPixelGrid
        {
            get => GetOverlay(ViewportOverlayKind.PixelGrid);
            set => SetOverlay(ViewportOverlayKind.PixelGrid, value, nameof(OverlayPixelGrid));
        }

        public bool OverlayTileGrid
        {
            get => GetOverlay(ViewportOverlayKind.TileGrid);
            set => SetOverlay(ViewportOverlayKind.TileGrid, value, nameof(OverlayTileGrid));
        }

        public bool OverlayHitboxes
        {
            get => GetOverlay(ViewportOverlayKind.Hitboxes);
            set => SetOverlay(ViewportOverlayKind.Hitboxes, value, nameof(OverlayHitboxes));
        }

        public bool OverlayAttachmentOutlines
        {
            get => GetOverlay(ViewportOverlayKind.AttachmentOutlines);
            set => SetOverlay(ViewportOverlayKind.AttachmentOutlines, value, nameof(OverlayAttachmentOutlines));
        }

        public bool OverlayPivotMarkers
        {
            get => GetOverlay(ViewportOverlayKind.PivotMarkers);
            set => SetOverlay(ViewportOverlayKind.PivotMarkers, value, nameof(OverlayPivotMarkers));
        }

        private bool GetOverlay(ViewportOverlayKind kind)
        {
            return (overlayKinds & kind) != ViewportOverlayKind.None;
        }

        private void SetOverlay(ViewportOverlayKind kind, bool enabled, string propertyName)
        {
            ViewportOverlayKind next = enabled ? overlayKinds | kind : overlayKinds & ~kind;
            if (next == overlayKinds) return;
            overlayKinds = next;
            RaisePropertyChanged(propertyName);
            RequestCanvasRedraw();
        }

        // Stamps the reusable overlay settings with the effective flag set and
        // the per-render context, then hands it to the canvas. Map mode masks
        // the frame-object passes: the composed map surface is a synthetic
        // frame (it carries a default hurtbox and no real attachments), so
        // only the backdrop and the grids apply there; the tile grid uses the
        // map cell size in map mode and the project tileset's TileSize while
        // pixel-editing (0 = no tileset/map context, pass inactive).
        public ViewportOverlaySettings PrepareOverlayContext()
        {
            ViewportOverlayKind effective = overlayKinds;
            var tileset = session?.Project.Tileset;
            int tileSize = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake.Overlay.InactiveTileSize;
            if (MapModeActive && session?.Project.Map != null)
            {
                effective &= ViewportOverlayKind.Checkerboard |
                    ViewportOverlayKind.PixelGrid |
                    ViewportOverlayKind.TileGrid;
                tileSize = MapRenderer.GetCellSize(tileset);
            }
            else if (tileset != null)
            {
                tileSize = tileset.TileSize;
            }
            overlaySettings.Enabled = effective;
            overlaySettings.TileGrid.TileSize = tileSize;
            overlaySettings.AttachmentSources = bankSubElements;
            return overlaySettings;
        }

        // --- Timeline strip + onion skin (item #10) --------------------------------

        public OnionSkinSettings OnionSettings => onionSettings;

        public bool OnionSkinEnabled
        {
            get => onionSkinEnabled;
            set
            {
                if (SetField(ref onionSkinEnabled, value)) RequestCanvasRedraw();
            }
        }

        public int TimelineFrameCount
        {
            get => timelineFrameCount;
            private set => SetField(ref timelineFrameCount, value);
        }

        public int TimelineFrameIndex
        {
            get => timelineFrameIndex;
            private set => SetField(ref timelineFrameIndex, value);
        }

        // Raw duration override of the selected frame; 0 = inherit FPS.
        public int CurrentFrameDurationMs
        {
            get => currentFrameDurationMs;
            private set => SetField(ref currentFrameDurationMs, value);
        }

        // The selected animation's frame list, for onion-skin rendering; null
        // without a session or frame selection.
        public IReadOnlyList<Frame> CurrentAnimationFrames
        {
            get
            {
                if (session == null || timelineAnimationIndex < 0 ||
                    timelineAnimationIndex >= session.Project.Animations.Count)
                    return null;
                return session.Project.Animations[timelineAnimationIndex]?.Frames;
            }
        }

        public void SelectTimelineFrame(int frameIndex)
        {
            if (session == null || timelineAnimationIndex < 0) return;
            session.SelectFrame(timelineAnimationIndex, frameIndex);
            RequestCanvasRedraw();
        }

        public void SetCurrentFrameDurationMs(int durationMs)
        {
            if (session == null || timelineFrameIndex < 0) return;
            try
            {
                session.SetFrameDurationMs(timelineFrameIndex, durationMs);
            }
            catch (Exception exception)
            {
                ReportError("Frame duration rejected: " + exception.Message);
            }
        }

        public string ActiveToolName
        {
            get => activeToolName;
            private set => SetField(ref activeToolName, value);
        }

        public uint CurrentColorRgba
        {
            get => currentColorRgba;
            private set
            {
                if (SetField(ref currentColorRgba, value)) RaisePropertyChanged(nameof(ColorHex));
            }
        }

        public string ColorHex => FormatColorHex(currentColorRgba);

        // Most-recent-first snapshot of the session project's persisted
        // recent-colors ring; empty without a session. Refreshed (with a
        // property-changed notification only on actual change) from every
        // session state publication.
        public IReadOnlyList<uint> RecentColors => recentColors;

        public int BrushSize
        {
            get => brushSize;
            private set => SetField(ref brushSize, value);
        }

        public bool IsSaving
        {
            get => isSaving;
            private set
            {
                if (SetField(ref isSaving, value)) RaisePropertyChanged(nameof(CanSave));
            }
        }

        public bool CanSave => HasSession && !IsSaving;
        public bool CanUndo { get => canUndo; private set => SetField(ref canUndo, value); }
        public bool CanRedo { get => canRedo; private set => SetField(ref canRedo, value); }

        public string ProjectLabel { get => projectLabel; private set => SetField(ref projectLabel, value); }
        public string FrameLabel { get => frameLabel; private set => SetField(ref frameLabel, value); }
        public string ZoomLabel { get => zoomLabel; private set => SetField(ref zoomLabel, value); }
        public string DirtyLabel { get => dirtyLabel; private set => SetField(ref dirtyLabel, value); }
        public string HistoryLabel { get => historyLabel; private set => SetField(ref historyLabel, value); }
        public string ValidationLabel { get => validationLabel; private set => SetField(ref validationLabel, value); }
        public string ErrorMessage { get => errorMessage; private set => SetField(ref errorMessage, value); }

        public List<DesignableCategory> GetCategories()
        {
            return toolbar.GetDesignableCategoryDefinitions();
        }

        // --- Project lifecycle ---------------------------------------------------

        public void CreateProject(NewProjectRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            EFYVProject project = toolbar.CreateNewProject(request.CategoryLabel);
            if (project == null)
                throw new InvalidOperationException("Unknown category: " + request.CategoryLabel);

            project.CanvasWidth = request.CanvasWidth;
            project.CanvasHeight = request.CanvasHeight;
            var animation = new AnimationState(AppDefaults.InitialAnimationName);
            animation.Frames.Add(new Frame(project.CanvasWidth, project.CanvasHeight, Config.Frame.DefaultIndex));
            project.Animations.Add(animation);

            StartSession(request.ProjectDirectory, request.ProjectName, project);
        }

        // --- Project browser + open existing (item #6) -----------------------------

        // Committed projects saved under the given directory (name + timestamp),
        // via the sanctioned ProjectPersistenceService.ListProjects seam. Returns
        // an empty list (never throws) for a missing/unreadable directory.
        public IReadOnlyList<ProjectListEntry> ListProjects(string projectDirectory)
        {
            try
            {
                return new ProjectPersistenceService(projectDirectory, schemaService).ListProjects();
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is System.IO.IOException ||
                exception is UnauthorizedAccessException)
            {
                ReportError("Could not list projects: " + exception.Message);
                return Array.Empty<ProjectListEntry>();
            }
        }

        // Whether an autosave sidecar exists for the given project (drives the
        // restore/discard recovery prompt on open).
        public bool HasAutosave(string projectDirectory, string projectName)
        {
            try
            {
                return new ProjectPersistenceService(projectDirectory, schemaService)
                    .AutosaveExists(projectName);
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is System.IO.IOException ||
                exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void DiscardAutosave(string projectDirectory, string projectName)
        {
            try
            {
                new ProjectPersistenceService(projectDirectory, schemaService).DeleteAutosave(projectName);
            }
            catch (Exception exception)
            {
                ReportError("Could not discard autosave: " + exception.Message);
            }
        }

        // Loads a saved project (or its newer autosave when preferAutosave) and
        // makes it the live session. Throws on a corrupt/invalid file so the
        // host can surface it - the loader is the validation gate.
        public void OpenProject(string projectDirectory, string projectName, bool preferAutosave)
        {
            var persistence = new ProjectPersistenceService(projectDirectory, schemaService);
            EFYVProject project = preferAutosave && persistence.AutosaveExists(projectName)
                ? persistence.LoadAutosave(projectName)
                : persistence.LoadProject(projectName);
            StartSession(projectDirectory, projectName, project);
        }

        // Shared session bring-up for create/open: the asset bank lives next to
        // the project files and doubles as the export-time attachment resolver,
        // so live debug can flatten placed attachments into the published atlas.
        private void StartSession(string projectDirectory, string projectName, EFYVProject project)
        {
            AssetBankManager bank = new AssetBankManager(
                System.IO.Path.Combine(projectDirectory, Config.Export.AssetBankDirectoryName));
            bank.LoadFailed += HandleBankLoadFailed;

            DesignerSession created = DesignerSession.Create(projectName, project, projectDirectory, bank);
            ReplaceAssetBank(bank);
            ReplaceSession(created);
            RefreshAssetBank();
        }

        private void ReplaceSession(DesignerSession next)
        {
            DesignerSession previous = session;
            if (previous != null)
            {
                previous.StateChanged -= HandleStateChanged;
                previous.History.CommandFailed -= HandleCommandFailed;
                previous.Dispose();
            }

            session = next;
            session.StateChanged += HandleStateChanged;
            session.History.CommandFailed += HandleCommandFailed;
            ApplyActiveTool();
            // A fresh project starts on its base layer; SetActiveLayerIndex both
            // stores it and stamps every layer tool so drawing/selection begin
            // on the right layer without a first panel click.
            activeLayerIndex = Config.Tool.DefaultLayerIndex;
            ApplyActiveLayerToTools();
            ErrorMessage = "";
            RaisePropertyChanged(nameof(HasSession));
            RaisePropertyChanged(nameof(CanSave));
            RaisePropertyChanged(nameof(ActiveLayerIndex));
            HandleStateChanged(session.Current);
        }

        // --- Asset bank (item #6) --------------------------------------------------

        // Loaded bank sub-elements in bank (ordinal name) order.
        public IReadOnlyList<SubElement> BankSubElements => bankSubElements;

        // Index into BankSubElements of the stamp tool's active sub-element;
        // -1 when none is selected (the stamp tool then only repositions
        // existing attachments and cannot place or bake new ones).
        public int SelectedBankIndex
        {
            get => selectedBankIndex;
            private set => SetField(ref selectedBankIndex, value);
        }

        // Stamp mode toggle: false (default) places repositionable
        // attachments, true is the legacy destructive bake-pixels behavior.
        public bool StampBakePixels
        {
            get => stamp.Mode == StampToolMode.BakePixels;
            set
            {
                StampToolMode next = value ? StampToolMode.BakePixels : StampToolMode.PlaceAttachment;
                if (stamp.Mode == next) return;
                stamp.Mode = next;
                RaisePropertyChanged(nameof(StampBakePixels));
            }
        }

        public void RefreshAssetBank()
        {
            if (assetBank == null) return;
            string selectedName = selectedBankIndex >= 0 && selectedBankIndex < bankSubElements.Count
                ? bankSubElements[selectedBankIndex].Name
                : null;
            bankSubElements = assetBank.LoadAllSubElements();
            RaisePropertyChanged(nameof(BankSubElements));

            // Re-select the previously selected element by NAME (indices may
            // have shifted); a vanished selection clears the active stamp.
            int reselected = -1;
            if (selectedName != null)
            {
                for (int index = 0; index < bankSubElements.Count; index++)
                {
                    if (string.Equals(bankSubElements[index].Name, selectedName, StringComparison.Ordinal))
                    {
                        reselected = index;
                        break;
                    }
                }
            }
            if (reselected >= 0) SelectBankSubElement(reselected);
            else
            {
                stamp.ActiveSubElement = null;
                SelectedBankIndex = -1;
            }
        }

        public bool SelectBankSubElement(int index)
        {
            if (index < 0 || index >= bankSubElements.Count) return false;
            stamp.ActiveSubElement = bankSubElements[index];
            SelectedBankIndex = index;
            return true;
        }

        // Saves pixels into the bank as a new sub-element: the floating
        // selection buffer when one is being dragged, else the masked
        // selection region, else the whole flattened frame.
        public void SaveSelectionAsSubElement(string name)
        {
            if (assetBank == null || session == null)
            {
                ReportError("Create a project before saving sub-elements.");
                return;
            }
            string trimmed = name?.Trim();
            if (!EFYVLabyMake.Core.IO.DesignerPathPolicy.IsSafeFileStem(trimmed))
            {
                ReportError("Sub-element name must be a safe file name (letters, digits, - or _).");
                return;
            }

            try
            {
                SubElement element = CaptureSelectionAsSubElement(trimmed);
                if (element == null)
                {
                    ReportError("Nothing to save (no frame selected).");
                    return;
                }
                assetBank.SaveSubElement(element);
                RefreshAssetBank();
            }
            catch (Exception exception)
            {
                ReportError("Sub-element save failed: " + exception.Message);
            }
        }

        private SubElement CaptureSelectionAsSubElement(string name)
        {
            FloatingSelection floating = session.Floating;
            if (floating != null)
            {
                var pixels = new uint[floating.Pixels.Length];
                for (int index = 0; index < pixels.Length; index++)
                {
                    if (floating.Mask[index]) pixels[index] = floating.Pixels[index];
                }
                return new SubElement(name, floating.Width, floating.Height, pixels);
            }

            Frame frame = session.CurrentFrame;
            if (frame == null) return null;
            PixelColor[] flattened = frame.FlattenLayers();

            SelectionRegion selection = session.Selection;
            if (selection == null)
            {
                var whole = new uint[flattened.Length];
                for (int index = 0; index < whole.Length; index++) whole[index] = flattened[index].Rgba;
                return new SubElement(name, frame.Width, frame.Height, whole);
            }

            var masked = new uint[selection.Width * selection.Height];
            for (int localY = 0; localY < selection.Height; localY++)
            {
                int row = localY * selection.Width;
                int canvasRow = (selection.Y + localY) * frame.Width;
                for (int localX = 0; localX < selection.Width; localX++)
                {
                    int canvasX = selection.X + localX;
                    if (!selection.Contains(canvasX, selection.Y + localY)) continue;
                    masked[row + localX] = flattened[canvasRow + canvasX].Rgba;
                }
            }
            return new SubElement(name, selection.Width, selection.Height, masked);
        }

        private void ReplaceAssetBank(AssetBankManager next)
        {
            if (assetBank != null) assetBank.LoadFailed -= HandleBankLoadFailed;
            assetBank = next;
            bankSubElements = Array.Empty<SubElement>();
            stamp.ActiveSubElement = null;
            SelectedBankIndex = -1;
            RaisePropertyChanged(nameof(BankSubElements));
        }

        // Load-failure toast: one bad .efyvsub reports and is skipped; the
        // rest of the bank still loads (AssetBankManager contract). May be
        // raised from the live-debug export worker, so post to the UI thread
        // when necessary.
        private void HandleBankLoadFailed(string path, Exception exception)
        {
            string message = "Sub-element failed to load: " +
                System.IO.Path.GetFileName(path) + " (" + exception.GetType().Name + ")";
            if (uiContext == null || ReferenceEquals(
                System.Threading.SynchronizationContext.Current, uiContext))
            {
                ReportError(message);
                return;
            }
            uiContext.Post(state => ReportError((string)state), message);
        }

        // --- Linked directional authoring (item #33) ---------------------------------

        // True when the session project is a linked 4-direction project; the
        // facing buttons enable exactly then. Both properties refresh from the
        // session on every state change (facing switches are session commands,
        // so undo/redo keeps them coherent automatically).
        public bool IsDirectionalProject
        {
            get => isDirectionalProject;
            private set => SetField(ref isDirectionalProject, value);
        }

        public string ActiveFacing
        {
            get => activeFacing;
            private set => SetField(ref activeFacing, value);
        }

        // Converts the current directional-capable project into a linked
        // 4-direction project (undoable). Reports instead of throwing when the
        // project's asset type has no facings or the mode is already on.
        public void EnableDirectionalAuthoring()
        {
            if (session == null) return;
            try
            {
                session.EnableDirectionalAuthoring();
            }
            catch (InvalidOperationException)
            {
                ReportError("Linked 4-direction authoring needs a directional entity project (and enables only once).");
            }
        }

        // Switches the visible facing of a linked directional project (an
        // undoable session command; no-op for the already-active facing).
        public void SwitchFacing(string facing)
        {
            if (session == null) return;
            try
            {
                session.SwitchFacing(facing);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException || exception is ArgumentException)
            {
                ReportError("Facing switch failed: " + exception.Message);
            }
        }

        private void RefreshDirectionalState()
        {
            IsDirectionalProject = session?.Project.Directional != null;
            ActiveFacing = session?.Project.Directional?.ActiveFacing ?? "";
        }

        // --- Map/tileset editing mode (item #5) --------------------------------------

        public bool MapModeActive
        {
            get => mapModeActive;
            set
            {
                if (SetField(ref mapModeActive, value)) RequestCanvasRedraw();
            }
        }

        public MapEditAction MapAction
        {
            get => mapAction;
            set => SetField(ref mapAction, value);
        }

        public bool HasMapSection => session?.Project.Map != null;
        public bool HasTileset => session?.Project.Tileset != null;

        // The tile picker source: the project tileset's tiles in tile-id
        // order (empty without a session/tileset).
        public IReadOnlyList<EFYVLabyMake.Core.Models.TilesetTile> MapTiles
        {
            get
            {
                var tiles = session?.Project.Tileset?.Tiles;
                return tiles ?? (IReadOnlyList<EFYVLabyMake.Core.Models.TilesetTile>)
                    Array.Empty<EFYVLabyMake.Core.Models.TilesetTile>();
            }
        }

        public int SelectedMapTileIndex
        {
            get => selectedMapTileIndex;
            private set => SetField(ref selectedMapTileIndex, value);
        }

        public bool SelectMapTile(int index)
        {
            if (index < 0 || index >= MapTiles.Count) return false;
            SelectedMapTileIndex = index;
            return true;
        }

        // The frame the canvas should render: the composed map surface in map
        // mode (when a map section exists), otherwise the session frame.
        public Frame ActiveCanvasFrame
        {
            get
            {
                if (MapModeActive && session?.Project.Map != null) return BuildMapSurfaceFrame();
                return CurrentFrame;
            }
        }

        private Frame BuildMapSurfaceFrame()
        {
            var project = session.Project;
            MapRenderer.GetSurfaceSize(project.Map, project.Tileset, out int width, out int height);
            if (mapSurfaceFrame == null || mapSurfaceFrame.Width != width || mapSurfaceFrame.Height != height)
                mapSurfaceFrame = new Frame(width, height, Config.Frame.DefaultIndex);
            if (mapSurfaceBuffer == null || mapSurfaceBuffer.Length != width * height)
                mapSurfaceBuffer = new uint[width * height];
            MapRenderer.Render(project.Map, project.Tileset, mapSurfaceBuffer);
            Layer surface = mapSurfaceFrame.Layers[0];
            for (int index = 0; index < mapSurfaceBuffer.Length; index++)
                surface.Pixels[index].Rgba = mapSurfaceBuffer[index];
            return mapSurfaceFrame;
        }

        // Creates the map section (and switches into map mode). The map's
        // tileset reference is this project's own export identity when a
        // tileset section exists - the tile-sheet publishes under that stem.
        public void CreateMapSection(string mapId, int width, int height)
        {
            if (session == null)
            {
                ReportError("Create a project before creating a map.");
                return;
            }
            try
            {
                string trimmed = string.IsNullOrWhiteSpace(mapId)
                    ? Config.MapDocument.DefaultMapId
                    : mapId.Trim();
                session.CreateMapSection(
                    trimmed,
                    width,
                    height,
                    HasTileset ? ResolveProjectIdentityStem() : "");
                MapModeActive = true;
                RaisePropertyChanged(nameof(HasMapSection));
            }
            catch (Exception exception)
            {
                ReportError("Map creation failed: " + exception.Message);
            }
            RequestCanvasRedraw();
        }

        // Captures the current frame's top-left tile-size square as a new
        // tileset tile (creating the tileset at the toolbar tile size first
        // when the project has none) and selects it in the picker.
        public void AddTileFromCurrentFrame()
        {
            if (session == null)
            {
                ReportError("Create a project before adding tiles.");
                return;
            }
            try
            {
                if (session.Project.Tileset == null) session.CreateTileset(GetTileSize());
                int nextNumber = session.Project.Tileset.Tiles.Count + 1;
                session.AddTilesetTileFromCurrentFrame(
                    AppDefaults.TileNamePrefix + nextNumber.ToString(CultureInfo.InvariantCulture));
                SelectedMapTileIndex = session.Project.Tileset.Tiles.Count - 1;
                RaisePropertyChanged(nameof(MapTiles));
                RaisePropertyChanged(nameof(HasTileset));
            }
            catch (Exception exception)
            {
                ReportError("Add tile failed: " + exception.Message);
            }
            RequestCanvasRedraw();
        }

        public void ExportTileset()
        {
            if (session == null)
            {
                ReportError("Create a project before exporting.");
                return;
            }
            try
            {
                var engine = new EFYVLabyMake.Core.Export.ExportEngine(
                    new ProjectValidator(schemaService));
                engine.ExportTileset(session.Project, System.Threading.CancellationToken.None);
            }
            catch (Exception exception)
            {
                ReportError("Tileset export failed: " + exception.Message);
            }
        }

        public void ExportMap()
        {
            if (session == null)
            {
                ReportError("Create a project before exporting.");
                return;
            }
            try
            {
                var engine = new EFYVLabyMake.Core.Export.ExportEngine(
                    new ProjectValidator(schemaService));
                engine.ExportMap(session.Project);
            }
            catch (Exception exception)
            {
                ReportError("Map export failed: " + exception.Message);
            }
        }

        private string ResolveProjectIdentityStem()
        {
            object identity;
            var properties = session.Project.AssetProperties;
            if (!properties.TryGetValue(
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Exporter.FieldEntityName,
                    out identity) || !(identity is string))
            {
                properties.TryGetValue(
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Exporter.FieldAssetName,
                    out identity);
            }
            string stem = identity as string;
            return EFYVLabyMake.Core.IO.DesignerPathPolicy.IsSafeFileStem(stem) ? stem : "";
        }

        // Map-mode pointer routing: surface pixels -> map cells.

        private void MapPointerDown(int x, int y)
        {
            try
            {
                if (!TryGetMapCell(x, y, out int cellX, out int cellY)) return;
                short tileId = ResolveActiveTileId();
                if (mapAction == MapEditAction.Flood)
                {
                    session.FloodFillMapTiles(cellX, cellY, tileId);
                }
                else if (session.BeginMapPaint(tileId))
                {
                    session.PaintMapCell(cellX, cellY);
                }
            }
            catch (Exception exception)
            {
                ReportError("Map edit failed: " + exception.Message);
            }
            RequestCanvasRedraw();
        }

        private void MapPointerDrag(int x, int y)
        {
            if (session == null || !session.MapPaintActive) return;
            if (TryGetMapCell(x, y, out int cellX, out int cellY))
                session.PaintMapCell(cellX, cellY);
            RequestCanvasRedraw();
        }

        private void MapPointerUp()
        {
            try
            {
                if (session != null && session.MapPaintActive) session.EndMapPaint();
            }
            catch (Exception exception)
            {
                ReportError("Map edit failed: " + exception.Message);
            }
            RequestCanvasRedraw();
        }

        private bool TryGetMapCell(int x, int y, out int cellX, out int cellY)
        {
            cellX = 0;
            cellY = 0;
            if (x < 0 || y < 0) return false;
            int cellSize = MapRenderer.GetCellSize(session.Project.Tileset);
            cellX = x / cellSize;
            cellY = y / cellSize;
            return true;
        }

        // Erase paints the blank id; paint/flood use the picked tile (id 0
        // when the picker has nothing to offer yet).
        private short ResolveActiveTileId()
        {
            if (mapAction == MapEditAction.Erase)
                return Config.MapDocument.BlankTileId;
            var tiles = MapTiles;
            if (selectedMapTileIndex >= 0 && selectedMapTileIndex < tiles.Count)
                return (short)selectedMapTileIndex;
            return 0;
        }

        // --- Tool strip ----------------------------------------------------------

        public void SetActiveTool(string toolName)
        {
            ActiveToolName = toolName;
            ApplyActiveTool();
        }

        public void SetColor(uint rgba)
        {
            CurrentColorRgba = rgba;
            var color = new PixelColor { Rgba = rgba };
            pencil.CurrentColor = color;
            fill.CurrentColor = color;
            line.CurrentColor = color;
            rectShape.CurrentColor = color;
            ellipseShape.CurrentColor = color;
            tileMaker.CurrentColor = color;
            // Choosing a working color records it in the project's persisted
            // recent-colors ring (no-op when it is already the most recent).
            session?.NotifyColorUsed(rgba);
        }

        public bool TrySetColorHex(string text)
        {
            uint rgba;
            if (!TryParseColorHex(text, out rgba)) return false;
            SetColor(rgba);
            return true;
        }

        public void SetBrushSize(int size)
        {
            pencil.BrushSize = size;
            eraser.BrushSize = size;
            tileMaker.BrushSize = size;
            // Read the clamped value back from the tool so the UI shows reality.
            BrushSize = pencil.BrushSize;
        }

        public void SetShapeThickness(int thickness)
        {
            line.Thickness = thickness;
            rectShape.Thickness = thickness;
            ellipseShape.Thickness = thickness;
        }

        public int GetShapeThickness() => line.Thickness;

        public void SetShapeFilled(bool filled)
        {
            rectShape.Filled = filled;
            ellipseShape.Filled = filled;
        }

        public bool GetShapeFilled() => rectShape.Filled;

        public void SetSymmetry(SymmetryMode mode)
        {
            pencil.Symmetry = mode;
            eraser.Symmetry = mode;
            fill.Symmetry = mode;
            line.Symmetry = mode;
            rectShape.Symmetry = mode;
            ellipseShape.Symmetry = mode;
        }

        public SymmetryMode GetSymmetry() => pencil.Symmetry;

        public void SetTileSize(int size)
        {
            tileMaker.TileSize = size;
        }

        public int GetTileSize() => tileMaker.TileSize;

        public void SetHitboxKey(string key)
        {
            if (!string.IsNullOrWhiteSpace(key)) hitbox.ActiveHitboxKey = key;
        }

        public string GetHitboxKey() => hitbox.ActiveHitboxKey;

        private void ApplyActiveTool()
        {
            if (session == null) return;
            session.ActiveTool = ResolveTool(activeToolName);
        }

        private ITool ResolveTool(string name)
        {
            switch (name)
            {
                case AppDefaults.ToolEraser: return eraser;
                case AppDefaults.ToolEyedropper: return eyedropper;
                case AppDefaults.ToolFill: return fill;
                case AppDefaults.ToolLine: return line;
                case AppDefaults.ToolRect: return rectShape;
                case AppDefaults.ToolEllipse: return ellipseShape;
                case AppDefaults.ToolSelectRect: return selectRect;
                case AppDefaults.ToolSelectLasso: return selectLasso;
                case AppDefaults.ToolStamp: return stamp;
                case AppDefaults.ToolTileMaker: return tileMaker;
                case AppDefaults.ToolHitbox: return hitbox;
                case AppDefaults.ToolMoving: return moving;
                default: return pencil;
            }
        }

        // --- Canvas input (already translated to canvas coordinates) -------------

        public void PointerDown(int x, int y)
        {
            if (session == null) return;
            if (MapModeActive && session.Project.Map != null)
            {
                MapPointerDown(x, y);
                return;
            }
            try { session.PointerDown(x, y); }
            catch (Exception exception) { ReportError("Tool gesture failed: " + exception.Message); }
        }

        public void PointerDrag(int x, int y)
        {
            if (session == null) return;
            if (MapModeActive)
            {
                MapPointerDrag(x, y);
                return;
            }
            try { session.PointerDrag(x, y); }
            catch (Exception exception) { ReportError("Tool gesture failed: " + exception.Message); }
        }

        public void PointerUp(int x, int y)
        {
            if (session == null) return;
            if (MapModeActive)
            {
                MapPointerUp();
                return;
            }
            try { session.PointerUp(x, y); }
            catch (Exception exception) { ReportError("Tool gesture failed: " + exception.Message); }
        }

        public void CancelGesture()
        {
            session?.CancelGesture();
        }

        public void ReportZoom(float zoom)
        {
            ZoomLabel = ((int)System.Math.Round(zoom * 100f)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        // --- History / persistence ------------------------------------------------

        public void Undo()
        {
            if (session == null) return;
            try { session.Undo(); }
            catch (Exception exception) { ReportError("Undo failed: " + exception.Message); }
            RequestCanvasRedraw();
        }

        public void Redo()
        {
            if (session == null) return;
            try { session.Redo(); }
            catch (Exception exception) { ReportError("Redo failed: " + exception.Message); }
            RequestCanvasRedraw();
        }

        public async Task SaveAsync()
        {
            if (session == null || IsSaving) return;
            IsSaving = true;
            try
            {
                await session.SaveAsync();
            }
            catch (Exception exception)
            {
                ReportError("Save failed: " + exception.Message);
            }
            finally
            {
                IsSaving = false;
            }
        }

        public void GenerateMotion()
        {
            if (session == null) return;
            try
            {
                session.GenerateAnimation(moving);
                RequestCanvasRedraw();
            }
            catch (Exception exception)
            {
                ReportError("Motion generation failed: " + exception.Message);
            }
        }

        // --- Selection / clipboard / canvas resize --------------------------------

        public void CopySelection()
        {
            if (session == null) return;
            if (!session.CopySelection(activeLayerIndex))
                ReportError("Nothing selected to copy (make a selection first).");
        }

        public void PasteClipboard()
        {
            if (session == null) return;
            if (!session.PasteClipboard(activeLayerIndex))
                ReportError("Nothing to paste (copy a selection first).");
            RequestCanvasRedraw();
        }

        // Cut (item #9): copies the selection to the clipboard, then LIFTS it off
        // the active layer into a floating buffer (the pixels leave the layer).
        // This is this editor's native detach-and-relocate model - drag + click
        // anchors the cut content elsewhere, Esc restores it. A committed
        // erase-in-place is a Core-side gap (no region-clear command exists and
        // Core edits are restricted for this item).
        public void CutSelection()
        {
            if (session == null) return;
            if (!session.CopySelection(activeLayerIndex))
            {
                ReportError("Nothing selected to cut (make a selection first).");
                return;
            }
            session.LiftSelection(activeLayerIndex, true);
            RequestCanvasRedraw();
        }

        public void AnchorFloating()
        {
            if (session == null) return;
            try { session.AnchorFloating(); }
            catch (Exception exception) { ReportError("Anchor failed: " + exception.Message); }
            RequestCanvasRedraw();
        }

        public void ResizeCanvas(ResizeCanvasRequest request)
        {
            if (session == null || request == null) return;
            try
            {
                session.ResizeCanvas(request.CanvasWidth, request.CanvasHeight, request.Anchor);
            }
            catch (Exception exception)
            {
                ReportError("Resize failed: " + exception.Message);
            }
            RequestCanvasRedraw();
        }

        public void ClearError()
        {
            ErrorMessage = "";
        }

        public void ReportError(string message)
        {
            ErrorMessage = message ?? "";
        }

        // Neutral, non-error status notice (item #3.5 push result), auto-cleared
        // by the host like the error flash but styled unobtrusively.
        public string NoticeMessage { get => noticeMessage; private set => SetField(ref noticeMessage, value); }

        public void ReportNotice(string message)
        {
            NoticeMessage = message ?? "";
        }

        public void ClearNotice()
        {
            NoticeMessage = "";
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (assetBank != null)
            {
                assetBank.LoadFailed -= HandleBankLoadFailed;
                assetBank = null;
            }
            if (session != null)
            {
                session.StateChanged -= HandleStateChanged;
                session.History.CommandFailed -= HandleCommandFailed;
                session.Dispose();
                session = null;
            }
        }

        // --- Inspector panel (item #3.1) -------------------------------------------

        // The schema/identity fields of the open project with their current
        // values, for the inspector; empty without a session.
        public List<DesignerProperty> GetEditableProperties()
        {
            return session != null
                ? toolbar.GetEditableProperties(session.Project)
                : new List<DesignerProperty>();
        }

        // Edits one field through the session (undoable where the session makes
        // it so). The result carries the PropertyEditStatus the inspector turns
        // into an inline error; a thrown failure is reported and surfaced as an
        // InvalidValue result rather than escaping to the UI.
        public PropertyEditResult SetProperty(string fieldName, object value)
        {
            if (session == null)
                return new PropertyEditResult(PropertyEditStatus.UnknownAssetType, fieldName);
            PropertyEditResult result;
            try
            {
                result = session.SetProperty(fieldName, value);
            }
            catch (Exception exception)
            {
                ReportError("Property edit failed: " + exception.Message);
                return new PropertyEditResult(PropertyEditStatus.InvalidValue, fieldName);
            }
            RequestCanvasRedraw();
            return result;
        }

        // The current-validation error attached to a field (by Subject), for a
        // persistent inline marker beyond the last edit's transient status.
        public string GetFieldError(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return "";
            foreach (ProjectIssue issue in problems)
            {
                if (issue.Severity == ProjectIssueSeverity.Error &&
                    string.Equals(issue.Subject, fieldName, StringComparison.Ordinal))
                    return ProblemFormatter.Describe(issue);
            }
            return "";
        }

        // --- Active layer (item #3.2) ----------------------------------------------

        // The layer index every drawing/selection tool writes to and copy/paste
        // addresses. Clamped to the current frame and re-clamped on frame/layer
        // changes (see RefreshPanels), closing the batch-3 "always layer 0"
        // deferral.
        public int ActiveLayerIndex => activeLayerIndex;

        public void SetActiveLayerIndex(int index)
        {
            int clamped = ClampLayerIndex(index);
            bool changed = clamped != activeLayerIndex;
            activeLayerIndex = clamped;
            ApplyActiveLayerToTools();
            if (changed)
            {
                RaisePropertyChanged(nameof(ActiveLayerIndex));
                RequestCanvasRedraw();
            }
        }

        private int ClampLayerIndex(int index)
        {
            int count = CurrentLayers.Count;
            if (count <= Config.Common.EmptyCount) return Config.Tool.DefaultLayerIndex;
            if (index < Config.Common.FirstIndex) return Config.Common.FirstIndex;
            if (index >= count) return count - 1;
            return index;
        }

        private void ApplyActiveLayerToTools()
        {
            foreach (ILayerTool tool in layerTools) tool.ActiveLayerIndex = activeLayerIndex;
        }

        // --- Layers panel (item #3.2) ----------------------------------------------

        // The current frame's layers in bottom-to-top order; empty without a
        // frame. The panel drives selection (the active layer) and the CRUD.
        public IReadOnlyList<Layer> CurrentLayers
        {
            get
            {
                Frame frame = session?.CurrentFrame;
                return frame != null ? frame.Layers : (IReadOnlyList<Layer>)Array.Empty<Layer>();
            }
        }

        public void AddLayer() => RunSessionOp(() => session.AddLayer(NextLayerName()), "Add layer failed");
        public void DuplicateLayer(int index) =>
            RunSessionOp(() => session.DuplicateLayer(index), "Duplicate layer failed");
        public void RemoveLayer(int index) =>
            RunSessionOp(() => session.RemoveLayer(index), "Remove layer failed");
        public void MoveLayer(int fromIndex, int toIndex) =>
            RunSessionOp(() => session.MoveLayer(fromIndex, toIndex), "Move layer failed");
        public void RenameLayer(int index, string name) =>
            RunSessionOp(() => session.RenameLayer(index, name), "Rename layer failed");
        public void SetLayerVisibility(int index, bool isVisible) =>
            RunSessionOp(() => session.SetLayerVisibility(index, isVisible), "Layer visibility failed");
        public void SetLayerOpacity(int index, float opacity) =>
            RunSessionOp(() => session.SetLayerOpacity(index, opacity), "Layer opacity failed");

        // Batch-3 cross-frame layer batch ops: apply one layer change to the
        // same index across EVERY frame of the selected animation.
        public void AddLayerToAllFrames() =>
            RunSessionOp(() => session.AddLayerToAllFrames(NextLayerName()), "Add layer (all frames) failed");
        public void RemoveLayerFromAllFrames(int index) =>
            RunSessionOp(() => session.RemoveLayerFromAllFrames(index), "Remove layer (all frames) failed");
        public void RenameLayerInAllFrames(int index, string name) =>
            RunSessionOp(() => session.RenameLayerInAllFrames(index, name), "Rename layer (all frames) failed");
        public void SetLayerVisibilityInAllFrames(int index, bool isVisible) =>
            RunSessionOp(
                () => session.SetLayerVisibilityInAllFrames(index, isVisible),
                "Layer visibility (all frames) failed");

        private string NextLayerName()
        {
            return AppDefaults.LayerNamePrefix +
                (CurrentLayers.Count + Config.Common.UnitCount).ToString(CultureInfo.InvariantCulture);
        }

        // --- Animations / frames panel (item #3.2) ---------------------------------

        public IReadOnlyList<AnimationState> Animations
        {
            get => session != null
                ? (IReadOnlyList<AnimationState>)session.Project.Animations
                : Array.Empty<AnimationState>();
        }

        public int SelectedAnimationIndex => selectedAnimationIndex;

        // Selects an animation by making its first frame current (a frameless
        // animation cannot become the current selection - the session tracks a
        // frame, and its emptiness is a reported validation error).
        public void SelectAnimation(int index)
        {
            if (session == null || index < Config.Common.FirstIndex ||
                index >= session.Project.Animations.Count) return;
            AnimationState animation = session.Project.Animations[index];
            if (animation.Frames.Count == Config.Common.EmptyCount)
            {
                ReportError("Animation has no frames.");
                return;
            }
            session.SelectFrame(index, Config.Common.FirstIndex);
            RequestCanvasRedraw();
        }

        public void AddAnimation(string name)
        {
            if (session == null) return;
            string trimmed = string.IsNullOrWhiteSpace(name) ? AppDefaults.NewAnimationName : name.Trim();
            try
            {
                var animation = new AnimationState(trimmed);
                animation.Frames.Add(new Frame(
                    session.Project.CanvasWidth,
                    session.Project.CanvasHeight,
                    Config.Frame.DefaultIndex));
                session.AddAnimation(animation);
            }
            catch (Exception exception)
            {
                ReportError("Add animation failed: " + exception.Message);
            }
            RequestCanvasRedraw();
        }

        public void RemoveAnimation(int index) =>
            RunSessionOp(() => session.RemoveAnimation(index), "Remove animation failed");
        public void DuplicateAnimation(int index) =>
            RunSessionOp(() => session.DuplicateAnimation(index), "Duplicate animation failed");
        public void MoveAnimation(int fromIndex, int toIndex) =>
            RunSessionOp(() => session.MoveAnimation(fromIndex, toIndex), "Move animation failed");
        public void RenameAnimation(int index, string name) =>
            RunSessionOp(() => session.RenameAnimation(index, name), "Rename animation failed");
        public void SetAnimationFps(int index, int fps) =>
            RunSessionOp(() => session.SetAnimationFps(index, fps), "Set FPS failed");
        public void SetAnimationPingPong(int index, bool pingPong) =>
            RunSessionOp(() => session.SetAnimationPingPong(index, pingPong), "Set ping-pong failed");
        public void SetAnimationLoopRange(int index, int loopStart, int loopEnd) =>
            RunSessionOp(() => session.SetAnimationLoopRange(index, loopStart, loopEnd), "Set loop range failed");

        public void AddFrame() => RunSessionOp(() => session.AddFrame(), "Add frame failed");
        public void DuplicateFrame(int index) =>
            RunSessionOp(() => session.DuplicateFrame(index), "Duplicate frame failed");
        public void RemoveFrame(int index) =>
            RunSessionOp(() => session.RemoveFrame(index), "Remove frame failed");
        public void MoveFrame(int fromIndex, int toIndex) =>
            RunSessionOp(() => session.MoveFrame(fromIndex, toIndex), "Move frame failed");

        // --- Problems panel (item #3.3) --------------------------------------------

        // The latest snapshot's validation issues (item #27 lazy snapshot keeps
        // this cheap - one validate per edit, cached).
        public IReadOnlyList<ProjectIssue> Problems => problems;

        // Selects the frame an issue points at (when it carries a location), so
        // the canvas shows what the problem is about.
        public void FocusProblem(ProjectIssue issue)
        {
            if (session == null || !ProblemFormatter.HasFocusLocation(issue)) return;
            int frame = issue.FrameIndex >= Config.Common.FirstIndex
                ? issue.FrameIndex
                : Config.Common.FirstIndex;
            session.SelectFrame(issue.AnimationIndex, frame);
            RequestCanvasRedraw();
        }

        // --- Preview panel (item #3.4) ---------------------------------------------

        public PreviewController Preview => session?.Preview;

        // Loads the selected animation into the preview player. Preview needs
        // only STRUCTURAL validity, so schema-level gaps (e.g. an empty identity
        // name) do not block playback; the reason is handed back for the
        // disabled-state label on failure.
        public bool TryLoadPreview(out string reason)
        {
            reason = "";
            if (session == null)
            {
                reason = "No project open.";
                return false;
            }
            int index = selectedAnimationIndex >= Config.Common.FirstIndex
                ? selectedAnimationIndex
                : Config.Common.FirstIndex;
            if (index >= session.Project.Animations.Count)
            {
                reason = "No animation to preview.";
                return false;
            }
            try
            {
                session.LoadPreview(index);
                return true;
            }
            catch (EFYVLabyMake.Core.Export.ProjectValidationException exception)
            {
                reason = DescribeFirstError(exception.Validation);
                return false;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        // --- Live-debug controls (item #3.5) ---------------------------------------

        public string UnityProjectPath => session?.Project.UnityProjectPath ?? "";

        // Sets the export target Unity project path on the open project. Not
        // undoable and not dirty-marking (it is a config field, not document
        // content); it is captured by the next save and read live by export/
        // live-debug validation (Export scope). The host also persists it as a
        // cross-session default (AppSettings).
        public void SetUnityProjectPath(string path)
        {
            if (session == null) return;
            session.Project.UnityProjectPath = string.IsNullOrWhiteSpace(path) ? "" : path.Trim();
            RaisePropertyChanged(nameof(UnityProjectPath));
        }

        public LiveDebugSnapshot LiveDebug => liveDebugSnapshot;

        // The StartWatching toggle. OFF by default (the controller starts
        // Stopped); the panel surfaces the off state explicitly.
        public bool LiveWatching
        {
            get => liveDebugSnapshot != null && liveDebugSnapshot.IsWatching;
            set
            {
                if (session == null) return;
                try
                {
                    if (value) session.LiveDebug.StartWatching();
                    else session.LiveDebug.StopWatching();
                }
                catch (Exception exception)
                {
                    ReportError("Live watch toggle failed: " + exception.Message);
                }
            }
        }

        public bool IsPushing
        {
            get => isPushing;
            private set
            {
                if (SetField(ref isPushing, value)) RaisePropertyChanged(nameof(CanPush));
            }
        }

        public bool CanPush => HasSession && !isPushing;

        // Manual "Push to game": a full export through the live-debug engine,
        // regardless of the watch toggle, with a busy flag and a result notice.
        public async Task PushToGameAsync()
        {
            if (session == null || isPushing) return;
            IsPushing = true;
            try
            {
                await session.LiveDebug.ExportNowAsync(session.Project);
                RefreshLiveDebug(session.LiveDebug.Current);
                LiveDebugSnapshot snapshot = liveDebugSnapshot;
                if (snapshot != null && snapshot.State == LiveDebugState.Succeeded)
                    ReportNotice("Pushed to game.");
                else
                    ReportError("Push to game: " + LiveDebugFormatter.FormatStatus(snapshot));
            }
            catch (Exception exception)
            {
                ReportError("Push to game failed: " + exception.Message);
            }
            finally
            {
                IsPushing = false;
            }
        }

        // --- Palette panel (item #3.7) ---------------------------------------------

        public IReadOnlyList<Palette> Palettes
        {
            get => session != null
                ? (IReadOnlyList<Palette>)session.Project.Palettes
                : Array.Empty<Palette>();
        }

        public int ActivePaletteIndex
        {
            get => session?.ActivePaletteIndex ?? Config.Palette.DefaultActivePaletteIndex;
            set
            {
                if (session == null) return;
                session.ActivePaletteIndex = value;
                RaisePropertyChanged(nameof(ActivePaletteIndex));
            }
        }

        public bool PaletteConstraintEnabled
        {
            get => session != null && session.PaletteConstraintEnabled;
            set
            {
                if (session == null) return;
                session.PaletteConstraintEnabled = value;
                RaisePropertyChanged(nameof(PaletteConstraintEnabled));
            }
        }

        public void AddPalette(string name)
        {
            if (session == null) return;
            string trimmed = string.IsNullOrWhiteSpace(name) ? AppDefaults.NewPaletteName : name.Trim();
            try
            {
                session.AddPalette(trimmed);
            }
            catch (Exception exception)
            {
                ReportError("Add palette failed: " + exception.Message);
            }
            RequestCanvasRedraw();
        }

        public void RemovePalette(int index) =>
            RunSessionOp(() => session.RemovePalette(index), "Remove palette failed");
        public void RenamePalette(int index, string name) =>
            RunSessionOp(() => session.RenamePalette(index, name), "Rename palette failed");
        public void AddSwatchFromCurrentColor(int paletteIndex) =>
            RunSessionOp(() => session.AddSwatch(paletteIndex, currentColorRgba), "Add swatch failed");
        public void RemoveSwatch(int paletteIndex, int swatchIndex) =>
            RunSessionOp(() => session.RemoveSwatch(paletteIndex, swatchIndex), "Remove swatch failed");
        public void MoveSwatch(int paletteIndex, int fromIndex, int toIndex) =>
            RunSessionOp(() => session.MoveSwatch(paletteIndex, fromIndex, toIndex), "Move swatch failed");

        // Selecting a swatch snaps the working color (and the active tool's
        // brush color, via the session) to it and records it as recently used.
        public void SelectSwatch(int paletteIndex, int swatchIndex)
        {
            if (session == null) return;
            uint rgba;
            if (session.TrySelectSwatch(paletteIndex, swatchIndex, out rgba)) SetColor(rgba);
            else ReportError("Swatch selection out of range.");
        }

        // --- Panel refresh plumbing ------------------------------------------------

        private void RunSessionOp(Action operation, string errorPrefix)
        {
            if (session == null) return;
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                ReportError(errorPrefix + ": " + exception.Message);
            }
            RequestCanvasRedraw();
        }

        private void RefreshPanels(DesignerSessionSnapshot snapshot)
        {
            if (selectedAnimationIndex != snapshot.AnimationIndex)
            {
                selectedAnimationIndex = snapshot.AnimationIndex;
                RaisePropertyChanged(nameof(SelectedAnimationIndex));
            }

            // Re-clamp the active layer against the (possibly changed) current
            // frame and keep the shared tools in lockstep.
            int clampedLayer = ClampLayerIndex(activeLayerIndex);
            if (clampedLayer != activeLayerIndex)
            {
                activeLayerIndex = clampedLayer;
                RaisePropertyChanged(nameof(ActiveLayerIndex));
            }
            ApplyActiveLayerToTools();

            problems = snapshot.Validation?.Issues ?? (IReadOnlyList<ProjectIssue>)Array.Empty<ProjectIssue>();
            RaisePropertyChanged(nameof(Problems));
            RaisePropertyChanged(nameof(Animations));
            RaisePropertyChanged(nameof(CurrentLayers));
            RaisePropertyChanged(nameof(Palettes));
            RaisePropertyChanged(nameof(ActivePaletteIndex));
            RaisePropertyChanged(nameof(PaletteConstraintEnabled));
            RefreshLiveDebug(snapshot.LiveDebug);
        }

        private void RefreshLiveDebug(LiveDebugSnapshot snapshot)
        {
            liveDebugSnapshot = snapshot;
            RaisePropertyChanged(nameof(LiveDebug));
            RaisePropertyChanged(nameof(LiveWatching));
            RaisePropertyChanged(nameof(UnityProjectPath));
        }

        private static string DescribeFirstError(ProjectValidationResult validation)
        {
            if (validation == null) return "Preview unavailable.";
            foreach (ProjectIssue issue in validation.Issues)
            {
                if (issue.Severity == ProjectIssueSeverity.Error)
                    return ProblemFormatter.Describe(issue);
            }
            return "Preview unavailable.";
        }

        // --- Session events (always on the UI thread) -----------------------------

        private void HandleStateChanged(DesignerSessionSnapshot snapshot)
        {
            ProjectLabel = snapshot.ProjectName;
            DirtyLabel = snapshot.IsDirty ? "Modified" : "Saved";
            HistoryLabel = "Undo " + snapshot.History.UndoCount.ToString(CultureInfo.InvariantCulture) +
                " · Redo " + snapshot.History.RedoCount.ToString(CultureInfo.InvariantCulture);
            CanUndo = snapshot.History.CanUndo;
            CanRedo = snapshot.History.CanRedo;
            FrameLabel = FormatFrameLabel(snapshot);
            ValidationLabel = FormatValidationLabel(snapshot.Validation);
            RefreshTimeline(snapshot);
            RefreshRecentColors();
            RefreshMapState();
            RefreshDirectionalState();
            RefreshPanels(snapshot);
            if (snapshot.PersistenceException != null)
                ReportError("Persistence: " + snapshot.PersistenceException.Message);
            RequestCanvasRedraw();
        }

        private void RefreshTimeline(DesignerSessionSnapshot snapshot)
        {
            timelineAnimationIndex = snapshot.AnimationIndex;
            AnimationState animation =
                session != null &&
                snapshot.AnimationIndex >= 0 &&
                snapshot.AnimationIndex < session.Project.Animations.Count
                    ? session.Project.Animations[snapshot.AnimationIndex]
                    : null;
            TimelineFrameCount = animation?.Frames.Count ?? 0;
            TimelineFrameIndex = animation != null ? snapshot.FrameIndex : -1;
            Frame frame = CurrentFrame;
            CurrentFrameDurationMs = frame?.DurationMs ?? 0;
        }

        // Item #5: tileset/map sections can change under undo/redo and
        // reloads; keep the picker and the section-dependent buttons honest.
        // The selection is clamped instead of reset so the common case
        // (adding/removing the tail tile) keeps the user's pick.
        private void RefreshMapState()
        {
            int tileCount = MapTiles.Count;
            if (selectedMapTileIndex >= tileCount)
                SelectedMapTileIndex = tileCount - 1;
            else if (selectedMapTileIndex < 0 && tileCount > 0)
                SelectedMapTileIndex = 0;
            RaisePropertyChanged(nameof(MapTiles));
            RaisePropertyChanged(nameof(HasMapSection));
            RaisePropertyChanged(nameof(HasTileset));
        }

        private void RefreshRecentColors()
        {
            uint[] next = session == null
                ? Array.Empty<uint>()
                : session.Project.RecentColors.ToArray();
            if (AreEqual(recentColors, next)) return;
            recentColors = next;
            RaisePropertyChanged(nameof(RecentColors));
        }

        private static bool AreEqual(uint[] left, uint[] right)
        {
            if (left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index]) return false;
            }
            return true;
        }

        private void HandleCommandFailed(ICommand command, Exception exception)
        {
            ReportError("Command dropped from history: " + exception.Message);
        }

        private void RequestCanvasRedraw()
        {
            CanvasInvalidated?.Invoke();
        }

        private string FormatFrameLabel(DesignerSessionSnapshot snapshot)
        {
            if (session == null || snapshot.AnimationIndex < 0 || snapshot.FrameIndex < 0 ||
                snapshot.AnimationIndex >= session.Project.Animations.Count)
                return "No frame";
            AnimationState animation = session.Project.Animations[snapshot.AnimationIndex];
            return animation.StateName +
                " · Frame " + (snapshot.FrameIndex + 1).ToString(CultureInfo.InvariantCulture) +
                "/" + animation.Frames.Count.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatValidationLabel(ProjectValidationResult validation)
        {
            if (validation == null) return "";
            int errors = 0;
            int warnings = 0;
            foreach (ProjectIssue issue in validation.Issues)
            {
                if (issue.Severity == ProjectIssueSeverity.Error) errors++;
                else warnings++;
            }
            if (errors == 0 && warnings == 0) return "Valid";
            return errors.ToString(CultureInfo.InvariantCulture) + " errors · " +
                warnings.ToString(CultureInfo.InvariantCulture) + " warnings";
        }

        // --- Color helpers (uint RGBA, red in the low byte) ------------------------

        public static string FormatColorHex(uint rgba)
        {
            byte red = (byte)(rgba & 0xFFu);
            byte green = (byte)((rgba >> 8) & 0xFFu);
            byte blue = (byte)((rgba >> 16) & 0xFFu);
            byte alpha = (byte)((rgba >> 24) & 0xFFu);
            return "#" + red.ToString("X2", CultureInfo.InvariantCulture) +
                green.ToString("X2", CultureInfo.InvariantCulture) +
                blue.ToString("X2", CultureInfo.InvariantCulture) +
                alpha.ToString("X2", CultureInfo.InvariantCulture);
        }

        public static bool TryParseColorHex(string text, out uint rgba)
        {
            rgba = 0u;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal)) trimmed = trimmed.Substring(1);
            if (trimmed.Length != 6 && trimmed.Length != 8) return false;

            uint parsed;
            if (!uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                return false;

            byte red, green, blue, alpha;
            if (trimmed.Length == 6)
            {
                red = (byte)((parsed >> 16) & 0xFFu);
                green = (byte)((parsed >> 8) & 0xFFu);
                blue = (byte)(parsed & 0xFFu);
                alpha = 0xFF;
            }
            else
            {
                red = (byte)((parsed >> 24) & 0xFFu);
                green = (byte)((parsed >> 16) & 0xFFu);
                blue = (byte)((parsed >> 8) & 0xFFu);
                alpha = (byte)(parsed & 0xFFu);
            }

            rgba = red | ((uint)green << 8) | ((uint)blue << 16) | ((uint)alpha << 24);
            return true;
        }

    }
}
