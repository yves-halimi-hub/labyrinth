using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Models;

namespace EFYVLabyMake.App.UI
{
    // Layers panel (item #3.2): the current frame's layers (top-most first) with
    // active-layer selection - the layer every drawing/selection tool writes to,
    // closing the batch-3 "always layer 0" deferral - plus per-layer visibility,
    // opacity, rename, reorder, duplicate, and remove. The "All frames" toggle
    // routes add/remove/rename/visibility through the session's cross-frame batch
    // ops so one change lands on the same index in every frame.
    public sealed class LayersPanel : UserControl
    {
        private readonly EditorShell shell;
        private readonly StackPanel listPanel;
        private readonly CheckBox allFramesCheck;
        private bool building;

        public LayersPanel(EditorShell editorShell)
        {
            shell = editorShell ?? throw new ArgumentNullException(nameof(editorShell));

            listPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            allFramesCheck = new CheckBox { Content = "All frames" };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(allFramesCheck);
            header.Children.Add(MakeButton("+ Add", () =>
            {
                if (allFramesCheck.IsChecked == true) shell.AddLayerToAllFrames();
                else shell.AddLayer();
            }));

            var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            root.Children.Add(header);
            root.Children.Add(new ScrollViewer
            {
                Content = listPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = AppDefaults.PanelListHeight
            });
            Content = root;

            shell.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(EditorShell.CurrentLayers) ||
                    args.PropertyName == nameof(EditorShell.ActiveLayerIndex))
                    Rebuild();
            };
            Rebuild();
        }

        private void Rebuild()
        {
            building = true;
            try
            {
                listPanel.Children.Clear();
                var layers = shell.CurrentLayers;
                // Top-most layer first (highest index), matching editor convention.
                for (int index = layers.Count - 1; index >= 0; index--)
                    listPanel.Children.Add(BuildLayerRow(layers[index], index, layers.Count));
            }
            finally
            {
                building = false;
            }
        }

        private Control BuildLayerRow(Layer layer, int index, int layerCount)
        {
            bool isActive = index == shell.ActiveLayerIndex;

            var activeToggle = new ToggleButton
            {
                Content = isActive ? "◉" : "○",
                IsChecked = isActive,
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(activeToggle, "Active layer");
            activeToggle.Click += (sender, args) => shell.SetActiveLayerIndex(index);

            var visibleCheck = new CheckBox
            {
                IsChecked = layer.IsVisible,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(visibleCheck, "Visible");
            visibleCheck.IsCheckedChanged += (sender, args) =>
            {
                if (building) return;
                bool visible = visibleCheck.IsChecked == true;
                if (allFramesCheck.IsChecked == true) shell.SetLayerVisibilityInAllFrames(index, visible);
                else shell.SetLayerVisibility(index, visible);
            };

            var nameBox = new TextBox
            {
                Text = layer.Name,
                MinWidth = 90,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Action commitName = () =>
            {
                if (building) return;
                string name = nameBox.Text?.Trim();
                if (string.IsNullOrEmpty(name) || name == layer.Name) return;
                if (allFramesCheck.IsChecked == true) shell.RenameLayerInAllFrames(index, name);
                else shell.RenameLayer(index, name);
            };
            nameBox.LostFocus += (sender, args) => commitName();
            nameBox.KeyDown += (sender, args) => { if (args.Key == Key.Enter) commitName(); };

            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            topRow.Children.Add(activeToggle);
            topRow.Children.Add(visibleCheck);
            topRow.Children.Add(nameBox);

            var opacity = new NumericUpDown
            {
                Minimum = 0m,
                Maximum = 1m,
                Increment = 0.1m,
                Value = (decimal)layer.Opacity,
                Width = 90,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(opacity, "Opacity");
            opacity.ValueChanged += (sender, args) =>
            {
                if (building || !args.NewValue.HasValue) return;
                shell.SetLayerOpacity(index, (float)args.NewValue.Value);
            };

            var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            bottomRow.Children.Add(opacity);
            bottomRow.Children.Add(MakeSmallButton("▲", "Move up",
                () => shell.MoveLayer(index, index + 1), index < layerCount - 1));
            bottomRow.Children.Add(MakeSmallButton("▼", "Move down",
                () => shell.MoveLayer(index, index - 1), index > 0));
            bottomRow.Children.Add(MakeSmallButton("⧉", "Duplicate",
                () => shell.DuplicateLayer(index), true));
            bottomRow.Children.Add(MakeSmallButton("✕", "Remove",
                () =>
                {
                    if (allFramesCheck.IsChecked == true) shell.RemoveLayerFromAllFrames(index);
                    else shell.RemoveLayer(index);
                },
                layerCount > 1));

            var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            rows.Children.Add(topRow);
            rows.Children.Add(bottomRow);

            return new Border
            {
                Child = rows,
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(3),
                Background = isActive ? new SolidColorBrush(Color.FromArgb(40, 120, 170, 255)) : null,
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1)
            };
        }

        private static Button MakeButton(string caption, Action onClick)
        {
            var button = new Button { Content = caption, VerticalAlignment = VerticalAlignment.Center };
            button.Click += (sender, args) => onClick();
            return button;
        }

        private static Button MakeSmallButton(string glyph, string tip, Action onClick, bool enabled)
        {
            var button = new Button
            {
                Content = glyph,
                Padding = new Thickness(5, 1, 5, 1),
                IsEnabled = enabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(button, tip);
            button.Click += (sender, args) => onClick();
            return button;
        }
    }
}
