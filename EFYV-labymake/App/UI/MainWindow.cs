using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EFYVLabyMake.App.State;

namespace EFYVLabyMake.App.UI
{
    // Code-built main window: toolbar (project/history/tool strip), CanvasView
    // center, status bar bottom. Views bind to EditorShell (INotifyPropertyChanged)
    // and call its methods; no UI logic lives in the shell.
    public sealed class MainWindow : Window
    {
        private readonly EditorShell shell = new EditorShell();
        private readonly CanvasView canvas = new CanvasView();
        private readonly Dictionary<string, ToggleButton> toolButtons =
            new Dictionary<string, ToggleButton>(StringComparer.Ordinal);
        private readonly Dictionary<string, ToggleButton> facingButtons =
            new Dictionary<string, ToggleButton>(StringComparer.Ordinal);
        private readonly DispatcherTimer errorFlashTimer;
        private readonly DispatcherTimer noticeFlashTimer;
        // Cross-session shell preferences (item #5 Unity project path). Loaded
        // once and applied to each created/opened project; the app-setting is
        // the persisted default even when a project file does not carry a path.
        private readonly AppSettingsStore settingsStore = new AppSettingsStore();
        private AppSettings settings;
        private TextBox colorBox;
        private Border colorPreview;
        private StackPanel recentColorsPanel;
        private StackPanel timelineFramesPanel;
        private NumericUpDown frameDurationUpDown;
        private bool suppressDurationChanged;
        private StackPanel bankListPanel;
        private TextBox bankNameBox;
        private StackPanel mapTilesPanel;
        private TextBox mapIdBox;
        private NumericUpDown mapWidthUpDown;
        private NumericUpDown mapHeightUpDown;
        private ToggleButton mapModeToggle;

        public MainWindow()
        {
            Title = AppDefaults.WindowTitle;
            Width = AppDefaults.WindowWidth;
            Height = AppDefaults.WindowHeight;

            settings = settingsStore.Load();

            errorFlashTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppDefaults.ErrorFlashMilliseconds)
            };
            errorFlashTimer.Tick += (sender, args) =>
            {
                errorFlashTimer.Stop();
                shell.ClearError();
            };
            noticeFlashTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppDefaults.NoticeFlashMilliseconds)
            };
            noticeFlashTimer.Tick += (sender, args) =>
            {
                noticeFlashTimer.Stop();
                shell.ClearNotice();
            };

            var root = new DockPanel();
            var toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            var statusBar = BuildStatusBar();
            DockPanel.SetDock(statusBar, Dock.Bottom);
            root.Children.Add(statusBar);

            // Timeline strip (item #10): frame buttons + per-frame duration
            // editor + onion-skin toggle, docked just above the status bar.
            var timeline = BuildTimelineStrip();
            DockPanel.SetDock(timeline, Dock.Bottom);
            root.Children.Add(timeline);

            // Asset bank panel (item #6): sub-element thumbnails feeding the
            // stamp tool, save-selection-as-sub-element, and the stamp mode
            // toggle, docked at the right edge.
            var bankPanel = BuildAssetBankPanel();
            DockPanel.SetDock(bankPanel, Dock.Right);
            root.Children.Add(bankPanel);

            // Map/tileset panel (item #5): tile picker + paint/erase/flood
            // mode + map creation/export, docked inside the bank panel.
            var mapPanel = BuildMapPanel();
            DockPanel.SetDock(mapPanel, Dock.Right);
            root.Children.Add(mapPanel);

            // Editor panel set (item #3): collapsible sections docked at the
            // left edge. Added before the canvas so the canvas fills the rest.
            var editorPanels = BuildEditorPanels();
            DockPanel.SetDock(editorPanels, Dock.Left);
            root.Children.Add(editorPanels);

            root.Children.Add(canvas);
            Content = root;

            canvas.Attach(shell);
            shell.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(EditorShell.ActiveToolName)) UpdateToolChecks();
                else if (args.PropertyName == nameof(EditorShell.ColorHex)) UpdateColorControls();
                else if (args.PropertyName == nameof(EditorShell.RecentColors)) RebuildRecentColors();
                else if (args.PropertyName == nameof(EditorShell.ErrorMessage)) RestartErrorFlash();
                else if (args.PropertyName == nameof(EditorShell.NoticeMessage)) RestartNoticeFlash();
                else if (args.PropertyName == nameof(EditorShell.TimelineFrameCount) ||
                    args.PropertyName == nameof(EditorShell.TimelineFrameIndex)) RebuildTimelineFrames();
                else if (args.PropertyName == nameof(EditorShell.CurrentFrameDurationMs)) UpdateFrameDurationBox();
                else if (args.PropertyName == nameof(EditorShell.BankSubElements) ||
                    args.PropertyName == nameof(EditorShell.SelectedBankIndex)) RebuildBankList();
                else if (args.PropertyName == nameof(EditorShell.MapTiles) ||
                    args.PropertyName == nameof(EditorShell.SelectedMapTileIndex)) RebuildMapTiles();
                else if (args.PropertyName == nameof(EditorShell.MapModeActive)) UpdateMapModeToggle();
                else if (args.PropertyName == nameof(EditorShell.IsDirectionalProject) ||
                    args.PropertyName == nameof(EditorShell.ActiveFacing)) UpdateFacingButtons();
            };
            UpdateToolChecks();
            UpdateColorControls();
            RebuildRecentColors();
            RebuildTimelineFrames();
            UpdateFrameDurationBox();
            RebuildBankList();
            RebuildMapTiles();
            UpdateFacingButtons();
            Closed += (sender, args) => shell.Dispose();
        }

        // --- Toolbar -----------------------------------------------------------------

        private Control BuildToolbar()
        {
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(8, 6, 8, 6)
            };

            strip.Children.Add(MakeButton("New Project…", async () => await OpenNewProjectDialog()));
            strip.Children.Add(MakeButton("Open…", async () => await OpenExistingProject()));

            var saveButton = MakeButton("Save", async () => await shell.SaveAsync());
            saveButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.CanSave)));
            strip.Children.Add(saveButton);

            var undoButton = MakeButton("Undo", shell.Undo);
            undoButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.CanUndo)));
            strip.Children.Add(undoButton);

            var redoButton = MakeButton("Redo", shell.Redo);
            redoButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.CanRedo)));
            strip.Children.Add(redoButton);

            strip.Children.Add(MakeSeparator());

            AddToolToggle(strip, AppDefaults.ToolPencil);
            AddToolToggle(strip, AppDefaults.ToolEraser);
            AddToolToggle(strip, AppDefaults.ToolFill);
            AddToolToggle(strip, AppDefaults.ToolLine);
            AddToolToggle(strip, AppDefaults.ToolRect);
            AddToolToggle(strip, AppDefaults.ToolEllipse);
            AddToolToggle(strip, AppDefaults.ToolSelectRect);
            AddToolToggle(strip, AppDefaults.ToolSelectLasso);
            AddToolToggle(strip, AppDefaults.ToolStamp);
            AddToolToggle(strip, AppDefaults.ToolTileMaker);
            AddToolToggle(strip, AppDefaults.ToolHitbox);
            AddToolToggle(strip, AppDefaults.ToolMoving);

            var generateButton = MakeButton("Generate Motion", shell.GenerateMotion);
            generateButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            strip.Children.Add(generateButton);

            strip.Children.Add(MakeSeparator());

            // Color cluster: eyedropper toggle (a tool - picks the composited
            // canvas color into the working color), preview well, hex box,
            // fixed swatch row, then the persisted recent-colors row.
            AddToolToggle(strip, AppDefaults.ToolEyedropper);

            colorPreview = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(3),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            strip.Children.Add(colorPreview);

            colorBox = new TextBox
            {
                Width = 100,
                VerticalAlignment = VerticalAlignment.Center,
                Watermark = "#RRGGBBAA"
            };
            colorBox.KeyDown += (sender, args) =>
            {
                if (args.Key == Key.Enter) CommitColorText();
            };
            colorBox.LostFocus += (sender, args) => CommitColorText();
            strip.Children.Add(colorBox);

            foreach (uint swatch in AppDefaults.SwatchesRgba)
            {
                uint captured = swatch;
                var swatchButton = new Button
                {
                    Width = 18,
                    Height = 18,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(ToAvaloniaColor(captured)),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                swatchButton.Click += (sender, args) => shell.SetColor(captured);
                strip.Children.Add(swatchButton);
            }

            strip.Children.Add(MakeCaption("Recent"));
            recentColorsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center
            };
            strip.Children.Add(recentColorsPanel);

            strip.Children.Add(MakeSeparator());

            strip.Children.Add(MakeCaption("Brush"));
            var brushUpDown = new NumericUpDown
            {
                Minimum = AppDefaults.DefaultBrushSizeInput,
                Maximum = AppDefaults.MaxBrushSizeInput,
                Value = shell.BrushSize,
                Increment = 1,
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center
            };
            brushUpDown.ValueChanged += (sender, args) =>
            {
                if (args.NewValue.HasValue) shell.SetBrushSize((int)args.NewValue.Value);
            };
            strip.Children.Add(brushUpDown);

            strip.Children.Add(MakeCaption("Thick"));
            var thicknessUpDown = new NumericUpDown
            {
                Minimum = AppDefaults.DefaultBrushSizeInput,
                Maximum = AppDefaults.MaxBrushSizeInput,
                Value = shell.GetShapeThickness(),
                Increment = 1,
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center
            };
            thicknessUpDown.ValueChanged += (sender, args) =>
            {
                if (args.NewValue.HasValue)
                {
                    shell.SetShapeThickness((int)args.NewValue.Value);
                    thicknessUpDown.Value = shell.GetShapeThickness();
                }
            };
            strip.Children.Add(thicknessUpDown);

            var filledCheck = new CheckBox
            {
                Content = "Filled",
                IsChecked = shell.GetShapeFilled(),
                VerticalAlignment = VerticalAlignment.Center
            };
            filledCheck.IsCheckedChanged += (sender, args) =>
                shell.SetShapeFilled(filledCheck.IsChecked == true);
            strip.Children.Add(filledCheck);

            strip.Children.Add(MakeCaption("Mirror"));
            var mirrorCombo = new ComboBox
            {
                ItemsSource = new[]
                {
                    EFYVLabyMake.Core.Tools.SymmetryMode.None.ToString(),
                    EFYVLabyMake.Core.Tools.SymmetryMode.Horizontal.ToString(),
                    EFYVLabyMake.Core.Tools.SymmetryMode.Vertical.ToString(),
                    EFYVLabyMake.Core.Tools.SymmetryMode.Both.ToString()
                },
                SelectedIndex = (int)shell.GetSymmetry(),
                VerticalAlignment = VerticalAlignment.Center
            };
            mirrorCombo.SelectionChanged += (sender, args) =>
            {
                if (mirrorCombo.SelectedIndex >= 0)
                    shell.SetSymmetry((EFYVLabyMake.Core.Tools.SymmetryMode)mirrorCombo.SelectedIndex);
            };
            strip.Children.Add(mirrorCombo);

            strip.Children.Add(MakeSeparator());

            var copyButton = MakeButton("Copy", shell.CopySelection);
            copyButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            strip.Children.Add(copyButton);

            var pasteButton = MakeButton("Paste", shell.PasteClipboard);
            pasteButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            strip.Children.Add(pasteButton);

            var anchorButton = MakeButton("Anchor", shell.AnchorFloating);
            anchorButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            strip.Children.Add(anchorButton);

            var resizeButton = MakeButton("Resize…", async () => await OpenResizeCanvasDialog());
            resizeButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            strip.Children.Add(resizeButton);

            strip.Children.Add(MakeCaption("Tile"));
            var tileUpDown = new NumericUpDown
            {
                Minimum = 1,
                Maximum = AppDefaults.MaxBrushSizeInput,
                Value = shell.GetTileSize(),
                Increment = 1,
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center
            };
            tileUpDown.ValueChanged += (sender, args) =>
            {
                if (args.NewValue.HasValue)
                {
                    shell.SetTileSize((int)args.NewValue.Value);
                    tileUpDown.Value = shell.GetTileSize();
                }
            };
            strip.Children.Add(tileUpDown);

            strip.Children.Add(MakeCaption("Hitbox key"));
            var hitboxKeyBox = new TextBox
            {
                Width = 110,
                Text = shell.GetHitboxKey(),
                VerticalAlignment = VerticalAlignment.Center
            };
            hitboxKeyBox.LostFocus += (sender, args) => shell.SetHitboxKey(hitboxKeyBox.Text);
            strip.Children.Add(hitboxKeyBox);

            return new Border
            {
                Child = strip,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = Brushes.DimGray
            };
        }

        private void AddToolToggle(StackPanel strip, string toolName)
        {
            var toggle = new ToggleButton { Content = toolName };
            toggle.Click += (sender, args) =>
            {
                shell.SetActiveTool(toolName);
                UpdateToolChecks();
            };
            toolButtons[toolName] = toggle;
            strip.Children.Add(toggle);
        }

        private void UpdateToolChecks()
        {
            foreach (KeyValuePair<string, ToggleButton> entry in toolButtons)
                entry.Value.IsChecked = string.Equals(entry.Key, shell.ActiveToolName, StringComparison.Ordinal);
        }

        private void UpdateColorControls()
        {
            if (colorBox != null) colorBox.Text = shell.ColorHex;
            if (colorPreview != null)
                colorPreview.Background = new SolidColorBrush(ToAvaloniaColor(shell.CurrentColorRgba));
        }

        // Rebuilds the recent-colors row (bounded by the core ring capacity,
        // most recent on the left). Clicking a recent color re-selects it.
        private void RebuildRecentColors()
        {
            if (recentColorsPanel == null) return;
            recentColorsPanel.Children.Clear();
            foreach (uint recent in shell.RecentColors)
            {
                uint captured = recent;
                var recentButton = new Button
                {
                    Width = 18,
                    Height = 18,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(ToAvaloniaColor(captured)),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTip.SetTip(recentButton, EditorShell.FormatColorHex(captured));
                recentButton.Click += (sender, args) => shell.SetColor(captured);
                recentColorsPanel.Children.Add(recentButton);
            }
        }

        private void CommitColorText()
        {
            if (colorBox == null) return;
            if (!shell.TrySetColorHex(colorBox.Text))
            {
                shell.ReportError("Invalid color; expected #RRGGBB or #RRGGBBAA");
                colorBox.Text = shell.ColorHex;
                return;
            }
            colorBox.Text = shell.ColorHex;
        }

        // --- Timeline strip (item #10) ---------------------------------------------

        private Control BuildTimelineStrip()
        {
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(8, 4, 8, 4)
            };

            strip.Children.Add(MakeCaption("Timeline"));

            timelineFramesPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center
            };
            strip.Children.Add(new ScrollViewer
            {
                Content = timelineFramesPanel,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                MaxWidth = AppDefaults.TimelineMaxFramesWidth,
                VerticalAlignment = VerticalAlignment.Center
            });

            strip.Children.Add(MakeSeparator());
            strip.Children.Add(MakeCaption("Dur ms (0=fps)"));
            frameDurationUpDown = new NumericUpDown
            {
                Minimum = 0,
                Maximum = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake.Animation.MaxFrameDurationMs,
                Value = 0,
                Increment = AppDefaults.FrameDurationIncrementMs,
                Width = 130,
                VerticalAlignment = VerticalAlignment.Center
            };
            frameDurationUpDown.ValueChanged += (sender, args) =>
            {
                if (suppressDurationChanged || !args.NewValue.HasValue) return;
                shell.SetCurrentFrameDurationMs((int)args.NewValue.Value);
                UpdateFrameDurationBox();
            };
            strip.Children.Add(frameDurationUpDown);

            var onionCheck = new CheckBox
            {
                Content = "Onion skin",
                IsChecked = shell.OnionSkinEnabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            onionCheck.IsCheckedChanged += (sender, args) =>
                shell.OnionSkinEnabled = onionCheck.IsChecked == true;
            strip.Children.Add(onionCheck);

            // Item #31: zoom buttons through the new public ViewportController
            // API (view-center anchored; Reset restores the default view).
            strip.Children.Add(MakeSeparator());
            strip.Children.Add(MakeCaption("View"));
            strip.Children.Add(MakeButton("Z+", canvas.ZoomIn));
            strip.Children.Add(MakeButton("Z-", canvas.ZoomOut));
            strip.Children.Add(MakeButton("Reset", canvas.ResetView));

            // Item #31: overlay toggles - the Core overlay passes composited
            // by the canvas render (checkerboard on by default; map mode
            // masks the frame-object passes in the shell).
            strip.Children.Add(MakeSeparator());
            strip.Children.Add(MakeCaption("Overlays"));
            AddOverlayCheck(strip, "Checker", shell.OverlayCheckerboard,
                value => shell.OverlayCheckerboard = value);
            AddOverlayCheck(strip, "Pixel grid", shell.OverlayPixelGrid,
                value => shell.OverlayPixelGrid = value);
            AddOverlayCheck(strip, "Cells", shell.OverlayTileGrid,
                value => shell.OverlayTileGrid = value);
            AddOverlayCheck(strip, "Hitboxes", shell.OverlayHitboxes,
                value => shell.OverlayHitboxes = value);
            AddOverlayCheck(strip, "Attach", shell.OverlayAttachmentOutlines,
                value => shell.OverlayAttachmentOutlines = value);
            AddOverlayCheck(strip, "Pivots", shell.OverlayPivotMarkers,
                value => shell.OverlayPivotMarkers = value);

            // Item #33: linked 4-direction authoring. "Link 4-Dir" converts a
            // directional-capable project into a linked 4-facing project; the
            // four toggles switch the visible facing (an undoable session
            // command) and enable only while the project is directional.
            strip.Children.Add(MakeSeparator());
            strip.Children.Add(MakeCaption("Facing"));
            var linkButton = MakeButton("Link 4-Dir", shell.EnableDirectionalAuthoring);
            linkButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            strip.Children.Add(linkButton);
            foreach (string facing in
                EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake.Schema.FacingChoices)
            {
                string captured = facing;
                var facingToggle = new ToggleButton
                {
                    Content = facing,
                    IsEnabled = false
                };
                facingToggle.Click += (sender, args) =>
                {
                    shell.SwitchFacing(captured);
                    UpdateFacingButtons();
                };
                facingButtons[captured] = facingToggle;
                strip.Children.Add(facingToggle);
            }

            return new Border
            {
                Child = strip,
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = Brushes.DimGray
            };
        }

        private void UpdateFacingButtons()
        {
            foreach (KeyValuePair<string, ToggleButton> entry in facingButtons)
            {
                entry.Value.IsEnabled = shell.IsDirectionalProject;
                entry.Value.IsChecked = shell.IsDirectionalProject &&
                    string.Equals(entry.Key, shell.ActiveFacing, StringComparison.Ordinal);
            }
        }

        private static void AddOverlayCheck(
            StackPanel strip,
            string caption,
            bool initialChecked,
            Action<bool> apply)
        {
            var check = new CheckBox
            {
                Content = caption,
                IsChecked = initialChecked,
                VerticalAlignment = VerticalAlignment.Center
            };
            check.IsCheckedChanged += (sender, args) => apply(check.IsChecked == true);
            strip.Children.Add(check);
        }

        private void RebuildTimelineFrames()
        {
            if (timelineFramesPanel == null) return;
            timelineFramesPanel.Children.Clear();
            for (int index = 0; index < shell.TimelineFrameCount; index++)
            {
                int captured = index;
                var frameToggle = new ToggleButton
                {
                    Content = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    IsChecked = index == shell.TimelineFrameIndex,
                    MinWidth = 30,
                    Padding = new Thickness(4, 2, 4, 2)
                };
                frameToggle.Click += (sender, args) =>
                {
                    shell.SelectTimelineFrame(captured);
                    RebuildTimelineFrames();
                };
                timelineFramesPanel.Children.Add(frameToggle);
            }
        }

        private void UpdateFrameDurationBox()
        {
            if (frameDurationUpDown == null) return;
            suppressDurationChanged = true;
            try
            {
                frameDurationUpDown.Value = shell.CurrentFrameDurationMs;
                frameDurationUpDown.IsEnabled = shell.TimelineFrameIndex >= 0;
            }
            finally
            {
                suppressDurationChanged = false;
            }
        }

        // --- Asset bank panel (item #6) ----------------------------------------------

        private Control BuildAssetBankPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Margin = new Thickness(8, 6, 8, 6),
                Width = AppDefaults.BankPanelWidth
            };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(MakeCaption("Asset Bank"));
            var refreshButton = MakeButton("Refresh", shell.RefreshAssetBank);
            refreshButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            header.Children.Add(refreshButton);
            panel.Children.Add(header);

            bankListPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            panel.Children.Add(new ScrollViewer
            {
                Content = bankListPanel,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Height = 380
            });

            bankNameBox = new TextBox { Watermark = "sub-element name" };
            panel.Children.Add(bankNameBox);

            // Saves the current floating buffer / selection region (whole
            // frame when neither exists) into the bank under the given name.
            var saveButton = MakeButton(
                "Save selection as sub-element",
                () => shell.SaveSelectionAsSubElement(bankNameBox.Text));
            saveButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            panel.Children.Add(saveButton);

            var bakeCheck = new CheckBox
            {
                Content = "Bake pixels (legacy stamp)",
                IsChecked = shell.StampBakePixels
            };
            bakeCheck.IsCheckedChanged += (sender, args) =>
                shell.StampBakePixels = bakeCheck.IsChecked == true;
            panel.Children.Add(bakeCheck);

            return new Border
            {
                Child = panel,
                BorderThickness = new Thickness(1, 0, 0, 0),
                BorderBrush = Brushes.DimGray
            };
        }

        private void RebuildBankList()
        {
            if (bankListPanel == null) return;
            bankListPanel.Children.Clear();
            var elements = shell.BankSubElements;
            for (int index = 0; index < elements.Count; index++)
            {
                int captured = index;
                var element = elements[index];

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                row.Children.Add(new Border
                {
                    Width = AppDefaults.BankThumbnailBoxSize,
                    Height = AppDefaults.BankThumbnailBoxSize,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Child = BuildThumbnail(element)
                });
                row.Children.Add(new TextBlock
                {
                    Text = element.Name + " (" + element.Width + "x" + element.Height + ")",
                    VerticalAlignment = VerticalAlignment.Center
                });

                var toggle = new ToggleButton
                {
                    Content = row,
                    IsChecked = index == shell.SelectedBankIndex,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(4)
                };
                toggle.Click += (sender, args) =>
                {
                    shell.SelectBankSubElement(captured);
                    RebuildBankList();
                };
                bankListPanel.Children.Add(toggle);
            }
        }

        // Renders a sub-element's RGBA pixels into a small BGRA bitmap. The
        // Image scales it into the thumbnail box without smoothing so pixels
        // stay crisp.
        private static Control BuildThumbnail(EFYVLabyMake.Core.Models.SubElement element)
        {
            return BuildPixelsThumbnail(element.Pixels, element.Width, element.Height);
        }

        // Shared straight-RGBA -> BGRA thumbnail builder (bank sub-elements
        // and item #5 tileset tiles), via the reusable BitmapFactory.
        private static Control BuildPixelsThumbnail(uint[] pixels, int width, int height)
        {
            return BitmapFactory.FromRgbaImage(pixels, width, height);
        }

        // --- Map/tileset panel (item #5) ----------------------------------------------

        private Control BuildMapPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Margin = new Thickness(8, 6, 8, 6),
                Width = AppDefaults.MapPanelWidth
            };

            panel.Children.Add(MakeCaption("Map / Tileset"));

            mapModeToggle = new ToggleButton
            {
                Content = "Map Mode",
                IsChecked = shell.MapModeActive,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            mapModeToggle.Click += (sender, args) =>
            {
                shell.MapModeActive = mapModeToggle.IsChecked == true;
            };
            panel.Children.Add(mapModeToggle);

            var actionCombo = new ComboBox
            {
                ItemsSource = new[]
                {
                    MapEditAction.Paint.ToString(),
                    MapEditAction.Erase.ToString(),
                    MapEditAction.Flood.ToString()
                },
                SelectedIndex = (int)shell.MapAction,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            actionCombo.SelectionChanged += (sender, args) =>
            {
                if (actionCombo.SelectedIndex >= 0)
                    shell.MapAction = (MapEditAction)actionCombo.SelectedIndex;
            };
            panel.Children.Add(actionCombo);

            panel.Children.Add(MakeCaption("Tiles"));
            mapTilesPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            panel.Children.Add(new ScrollViewer
            {
                Content = mapTilesPanel,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Height = 220
            });

            var addTileButton = MakeButton("Add tile from frame", shell.AddTileFromCurrentFrame);
            addTileButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            panel.Children.Add(addTileButton);

            panel.Children.Add(MakeCaption("Map id / size"));
            mapIdBox = new TextBox
            {
                Watermark = "map id",
                Text = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake.MapDocument.DefaultMapId
            };
            panel.Children.Add(mapIdBox);

            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            mapWidthUpDown = new NumericUpDown
            {
                Minimum = AppDefaults.MinMapDimensionInput,
                Maximum = AppDefaults.MaxMapDimensionInput,
                Value = AppDefaults.DefaultMapDimensionInput,
                Increment = 1,
                Width = 96
            };
            mapHeightUpDown = new NumericUpDown
            {
                Minimum = AppDefaults.MinMapDimensionInput,
                Maximum = AppDefaults.MaxMapDimensionInput,
                Value = AppDefaults.DefaultMapDimensionInput,
                Increment = 1,
                Width = 96
            };
            sizeRow.Children.Add(mapWidthUpDown);
            sizeRow.Children.Add(mapHeightUpDown);
            panel.Children.Add(sizeRow);

            var createMapButton = MakeButton("Create map", () => shell.CreateMapSection(
                mapIdBox.Text,
                (int)(mapWidthUpDown.Value ?? AppDefaults.DefaultMapDimensionInput),
                (int)(mapHeightUpDown.Value ?? AppDefaults.DefaultMapDimensionInput)));
            createMapButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasSession)));
            panel.Children.Add(createMapButton);

            var exportTilesetButton = MakeButton("Export tileset", shell.ExportTileset);
            exportTilesetButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasTileset)));
            panel.Children.Add(exportTilesetButton);

            var exportMapButton = MakeButton("Export map (.efyvmap)", shell.ExportMap);
            exportMapButton.Bind(IsEnabledProperty, BindTo(nameof(EditorShell.HasMapSection)));
            panel.Children.Add(exportMapButton);

            return new Border
            {
                Child = panel,
                BorderThickness = new Thickness(1, 0, 0, 0),
                BorderBrush = Brushes.DimGray
            };
        }

        private void RebuildMapTiles()
        {
            if (mapTilesPanel == null) return;
            mapTilesPanel.Children.Clear();
            var tiles = shell.MapTiles;
            for (int index = 0; index < tiles.Count; index++)
            {
                int captured = index;
                var tile = tiles[index];

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                row.Children.Add(new Border
                {
                    Width = AppDefaults.BankThumbnailBoxSize,
                    Height = AppDefaults.BankThumbnailBoxSize,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Child = BuildPixelsThumbnail(tile.Pixels, tile.TileSize, tile.TileSize)
                });
                row.Children.Add(new TextBlock
                {
                    Text = index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ": " + tile.Name,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var toggle = new ToggleButton
                {
                    Content = row,
                    IsChecked = index == shell.SelectedMapTileIndex,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(4)
                };
                toggle.Click += (sender, args) =>
                {
                    shell.SelectMapTile(captured);
                    RebuildMapTiles();
                };
                mapTilesPanel.Children.Add(toggle);
            }
        }

        private void UpdateMapModeToggle()
        {
            if (mapModeToggle != null) mapModeToggle.IsChecked = shell.MapModeActive;
        }

        // --- Status bar ---------------------------------------------------------------

        private Control BuildStatusBar()
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Margin = new Thickness(8, 4, 8, 4)
            };
            bar.Children.Add(MakeStatusText(nameof(EditorShell.ProjectLabel)));
            bar.Children.Add(MakeStatusText(nameof(EditorShell.ActiveToolName)));
            bar.Children.Add(MakeStatusText(nameof(EditorShell.FrameLabel)));
            bar.Children.Add(MakeStatusText(nameof(EditorShell.ZoomLabel)));
            bar.Children.Add(MakeStatusText(nameof(EditorShell.DirtyLabel)));
            bar.Children.Add(MakeStatusText(nameof(EditorShell.HistoryLabel)));
            bar.Children.Add(MakeStatusText(nameof(EditorShell.ValidationLabel)));

            var notice = MakeStatusText(nameof(EditorShell.NoticeMessage));
            notice.Foreground = Brushes.MediumSpringGreen;
            bar.Children.Add(notice);

            var error = MakeStatusText(nameof(EditorShell.ErrorMessage));
            error.Foreground = Brushes.OrangeRed;
            error.FontWeight = FontWeight.Bold;
            bar.Children.Add(error);

            return new Border
            {
                Child = bar,
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = Brushes.DimGray
            };
        }

        private TextBlock MakeStatusText(string propertyName)
        {
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            text.Bind(TextBlock.TextProperty, BindTo(propertyName));
            return text;
        }

        // --- Keyboard ------------------------------------------------------------------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;

            if (e.Key == Key.Escape)
            {
                canvas.CancelActiveGesture();
                e.Handled = true;
                return;
            }
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;

            switch (e.Key)
            {
                case Key.Z:
                    shell.Undo();
                    e.Handled = true;
                    break;
                case Key.Y:
                    shell.Redo();
                    e.Handled = true;
                    break;
                case Key.S:
                    _ = shell.SaveAsync();
                    e.Handled = true;
                    break;
                // Item #9 selection clipboard. A focused TextBox consumes its own
                // Ctrl+C/V/X first (the e.Handled guard above), so these only
                // fire for canvas/panel focus - the selection copy/paste/cut.
                case Key.C:
                    shell.CopySelection();
                    e.Handled = true;
                    break;
                case Key.V:
                    shell.PasteClipboard();
                    e.Handled = true;
                    break;
                case Key.X:
                    shell.CutSelection();
                    e.Handled = true;
                    break;
            }
        }

        // --- New-project flow ------------------------------------------------------------

        private async System.Threading.Tasks.Task OpenNewProjectDialog()
        {
            var dialog = new NewProjectDialog(shell.GetCategories(), AppDefaults.DefaultProjectDirectory());
            NewProjectRequest request = await dialog.ShowDialog<NewProjectRequest>(this);
            if (request == null) return;
            try
            {
                shell.CreateProject(request);
                ApplyUnitySettingToShell();
                Title = AppDefaults.WindowTitle + " - " + request.ProjectName;
            }
            catch (Exception exception)
            {
                shell.ReportError("Project creation failed: " + exception.Message);
            }
        }

        // --- Canvas-resize flow ------------------------------------------------------------

        private async System.Threading.Tasks.Task OpenResizeCanvasDialog()
        {
            var project = shell.Session?.Project;
            if (project == null) return;
            var dialog = new ResizeCanvasDialog(project.CanvasWidth, project.CanvasHeight);
            ResizeCanvasRequest request = await dialog.ShowDialog<ResizeCanvasRequest>(this);
            if (request == null) return;
            shell.ResizeCanvas(request);
        }

        // --- Editor panel set (item #3) -----------------------------------------------

        // Collapsible left-dock sections binding the shell to each panel; the
        // panels own their own rebuild off EditorShell property notifications.
        private Control BuildEditorPanels()
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Margin = new Thickness(6)
            };
            stack.Children.Add(MakePanelSection("Inspector", new InspectorPanel(shell), true));
            stack.Children.Add(MakePanelSection("Layers", new LayersPanel(shell), true));
            stack.Children.Add(MakePanelSection("Animations", new AnimationsPanel(shell), false));
            stack.Children.Add(MakePanelSection("Palette", new PalettePanel(shell), false));
            stack.Children.Add(MakePanelSection("Problems", new ProblemsPanel(shell), true));
            stack.Children.Add(MakePanelSection("Preview", new PreviewPanel(shell), false));
            stack.Children.Add(MakePanelSection(
                "Live Debug", new LiveDebugPanel(shell, EditUnityPathAsync), false));

            return new Border
            {
                Width = AppDefaults.PanelDockWidth,
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = Brushes.DimGray,
                Child = new ScrollViewer
                {
                    Content = stack,
                    HorizontalScrollBarVisibility =
                        Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility =
                        Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                }
            };
        }

        private static Control MakePanelSection(string title, Control panel, bool expanded)
        {
            return new Expander
            {
                Header = title,
                IsExpanded = expanded,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = panel
            };
        }

        // --- Open existing / autosave recovery (item #6) ------------------------------

        private async System.Threading.Tasks.Task OpenExistingProject()
        {
            string directory = AppDefaults.DefaultProjectDirectory();
            var dialog = new ProjectBrowserDialog(shell.ListProjects(directory));
            string projectName = await dialog.ShowDialog<string>(this);
            if (string.IsNullOrEmpty(projectName)) return;

            bool preferAutosave = false;
            if (shell.HasAutosave(directory, projectName))
            {
                bool? restore = await ConfirmAutosaveRecovery(projectName);
                if (restore == null) return;
                preferAutosave = restore.Value;
                if (!preferAutosave) shell.DiscardAutosave(directory, projectName);
            }

            try
            {
                shell.OpenProject(directory, projectName, preferAutosave);
                ApplyUnitySettingToShell();
                Title = AppDefaults.WindowTitle + " - " + projectName;
            }
            catch (Exception exception)
            {
                shell.ReportError("Open failed: " + exception.Message);
            }
        }

        // Restore / discard / cancel prompt when an autosave sidecar exists.
        // Returns true = restore autosave, false = discard + open committed,
        // null = cancel the whole open.
        private async System.Threading.Tasks.Task<bool?> ConfirmAutosaveRecovery(string projectName)
        {
            var dialog = new Window
            {
                Title = "Recover Autosave",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            var restoreButton = new Button { Content = "Restore autosave", IsDefault = true };
            restoreButton.Click += (sender, args) => dialog.Close("restore");
            var discardButton = new Button { Content = "Discard autosave" };
            discardButton.Click += (sender, args) => dialog.Close("discard");
            var cancelButton = new Button { Content = "Cancel", IsCancel = true };
            cancelButton.Click += (sender, args) => dialog.Close(null);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(restoreButton);
            buttons.Children.Add(discardButton);
            buttons.Children.Add(cancelButton);

            var layout = new StackPanel { Spacing = 10, Margin = new Thickness(14) };
            layout.Children.Add(new TextBlock
            {
                Text = "\"" + projectName + "\" has a newer autosave. Restore it, or discard " +
                    "it and open the last saved version?",
                TextWrapping = TextWrapping.Wrap
            });
            layout.Children.Add(buttons);
            dialog.Content = layout;

            string result = await dialog.ShowDialog<string>(this);
            if (result == "restore") return true;
            if (result == "discard") return false;
            return null;
        }

        // --- Unity project path (item #5) ---------------------------------------------

        private async System.Threading.Tasks.Task EditUnityPathAsync()
        {
            string current = shell.HasSession ? shell.UnityProjectPath : settings.UnityProjectPath;
            var dialog = new UnityPathDialog(current);
            string path = await dialog.ShowDialog<string>(this);
            if (path == null) return;
            settings.UnityProjectPath = path;
            settingsStore.Save(settings);
            if (shell.HasSession) shell.SetUnityProjectPath(path);
        }

        private void ApplyUnitySettingToShell()
        {
            if (shell.HasSession && !string.IsNullOrEmpty(settings.UnityProjectPath))
                shell.SetUnityProjectPath(settings.UnityProjectPath);
        }

        // --- Helpers ------------------------------------------------------------------------

        private void RestartErrorFlash()
        {
            errorFlashTimer.Stop();
            if (!string.IsNullOrEmpty(shell.ErrorMessage)) errorFlashTimer.Start();
        }

        private void RestartNoticeFlash()
        {
            noticeFlashTimer.Stop();
            if (!string.IsNullOrEmpty(shell.NoticeMessage)) noticeFlashTimer.Start();
        }

        private Binding BindTo(string propertyName)
        {
            return new Binding(propertyName) { Source = shell };
        }

        private static Button MakeButton(string caption, Action onClick)
        {
            var button = new Button { Content = caption };
            button.Click += (sender, args) => onClick();
            return button;
        }

        private static TextBlock MakeCaption(string caption)
        {
            return new TextBlock
            {
                Text = caption,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };
        }

        private static Control MakeSeparator()
        {
            return new Border
            {
                Width = 1,
                Background = Brushes.DimGray,
                Margin = new Thickness(4, 2, 4, 2)
            };
        }

        // shell colors are straight RGBA with red in the low byte (PixelColor layout).
        private static Color ToAvaloniaColor(uint rgba)
        {
            byte red = (byte)(rgba & 0xFFu);
            byte green = (byte)((rgba >> 8) & 0xFFu);
            byte blue = (byte)((rgba >> 16) & 0xFFu);
            byte alpha = (byte)((rgba >> 24) & 0xFFu);
            return Color.FromArgb(alpha, red, green, blue);
        }
    }
}
