using System;
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
    // Palette panel (item #3.7): named-palette CRUD, a swatch grid for the
    // active palette (click a swatch to make it the working color), swatch
    // add/remove/reorder, and the palette-constraint-mode toggle - the shell
    // surface the batch-3 v2 deferral left unexposed.
    public sealed class PalettePanel : UserControl
    {
        private readonly EditorShell shell;
        private readonly CheckBox constraintCheck;
        private readonly StackPanel paletteList;
        private readonly WrapPanel swatchGrid;
        private readonly Button removeSwatchButton;
        private readonly Button moveLeftButton;
        private readonly Button moveRightButton;
        private int selectedSwatchIndex = -1;
        private bool building;

        public PalettePanel(EditorShell editorShell)
        {
            shell = editorShell ?? throw new ArgumentNullException(nameof(editorShell));

            constraintCheck = new CheckBox { Content = "Constrain to palette" };
            constraintCheck.IsCheckedChanged += (sender, args) =>
            {
                if (building) return;
                shell.PaletteConstraintEnabled = constraintCheck.IsChecked == true;
            };

            paletteList = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
            swatchGrid = new WrapPanel { Orientation = Orientation.Horizontal };

            var swatchTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            swatchTools.Children.Add(MakeButton("+ Add color", () =>
                shell.AddSwatchFromCurrentColor(shell.ActivePaletteIndex)));
            removeSwatchButton = MakeButton("Remove", () =>
            {
                if (selectedSwatchIndex >= 0) shell.RemoveSwatch(shell.ActivePaletteIndex, selectedSwatchIndex);
            });
            moveLeftButton = MakeButton("◀", () =>
            {
                if (selectedSwatchIndex > 0)
                    shell.MoveSwatch(shell.ActivePaletteIndex, selectedSwatchIndex, selectedSwatchIndex - 1);
            });
            moveRightButton = MakeButton("▶", () =>
            {
                if (selectedSwatchIndex >= 0)
                    shell.MoveSwatch(shell.ActivePaletteIndex, selectedSwatchIndex, selectedSwatchIndex + 1);
            });
            swatchTools.Children.Add(removeSwatchButton);
            swatchTools.Children.Add(moveLeftButton);
            swatchTools.Children.Add(moveRightButton);

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(MakeButton("+ Palette", () => shell.AddPalette(null)));

            var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            root.Children.Add(header);
            root.Children.Add(constraintCheck);
            root.Children.Add(new ScrollViewer
            {
                Content = paletteList,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 120
            });
            root.Children.Add(new TextBlock { Text = "Swatches", Foreground = Brushes.Gray });
            root.Children.Add(swatchTools);
            root.Children.Add(new ScrollViewer
            {
                Content = swatchGrid,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 130
            });
            Content = root;

            shell.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(EditorShell.Palettes) ||
                    args.PropertyName == nameof(EditorShell.ActivePaletteIndex) ||
                    args.PropertyName == nameof(EditorShell.PaletteConstraintEnabled))
                    Rebuild();
            };
            Rebuild();
        }

        private void Rebuild()
        {
            building = true;
            try
            {
                constraintCheck.IsChecked = shell.PaletteConstraintEnabled;
                RebuildPaletteList();
                RebuildSwatches();
            }
            finally
            {
                building = false;
            }
        }

        private void RebuildPaletteList()
        {
            paletteList.Children.Clear();
            var palettes = shell.Palettes;
            int active = shell.ActivePaletteIndex;
            for (int index = 0; index < palettes.Count; index++)
            {
                int captured = index;
                Palette palette = palettes[index];

                var selectToggle = new ToggleButton
                {
                    Content = palette.Name + " (" + palette.Colors.Count + ")",
                    IsChecked = index == active,
                    MinWidth = 120,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                selectToggle.Click += (sender, args) =>
                {
                    shell.ActivePaletteIndex = captured;
                    selectedSwatchIndex = -1;
                };

                var nameBox = new TextBox { Text = palette.Name, Width = 90 };
                Action commitName = () =>
                {
                    if (building) return;
                    string name = nameBox.Text?.Trim();
                    if (!string.IsNullOrEmpty(name) && name != palette.Name)
                        shell.RenamePalette(captured, name);
                };
                nameBox.LostFocus += (sender, args) => commitName();
                nameBox.KeyDown += (sender, args) => { if (args.Key == Key.Enter) commitName(); };

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                row.Children.Add(selectToggle);
                row.Children.Add(nameBox);
                row.Children.Add(MakeSmallButton("✕", "Remove palette",
                    () => shell.RemovePalette(captured)));
                paletteList.Children.Add(row);
            }
        }

        private void RebuildSwatches()
        {
            swatchGrid.Children.Clear();
            var palettes = shell.Palettes;
            int active = shell.ActivePaletteIndex;
            if (active < 0 || active >= palettes.Count)
            {
                selectedSwatchIndex = -1;
                UpdateSwatchToolState(0);
                return;
            }

            Palette palette = palettes[active];
            if (selectedSwatchIndex >= palette.Colors.Count) selectedSwatchIndex = -1;
            for (int index = 0; index < palette.Colors.Count; index++)
            {
                int captured = index;
                uint rgba = palette.Colors[index];
                var swatch = new Button
                {
                    Width = 26,
                    Height = 20,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 3, 3),
                    Background = new SolidColorBrush(ToColor(rgba)),
                    BorderBrush = index == selectedSwatchIndex ? Brushes.White : Brushes.Gray,
                    BorderThickness = new Thickness(index == selectedSwatchIndex ? 2 : 1)
                };
                ToolTip.SetTip(swatch, EditorShell.FormatColorHex(rgba));
                swatch.Click += (sender, args) =>
                {
                    selectedSwatchIndex = captured;
                    shell.SelectSwatch(shell.ActivePaletteIndex, captured);
                    RebuildSwatches();
                };
                swatchGrid.Children.Add(swatch);
            }
            UpdateSwatchToolState(palette.Colors.Count);
        }

        private void UpdateSwatchToolState(int swatchCount)
        {
            bool hasSelection = selectedSwatchIndex >= 0 && selectedSwatchIndex < swatchCount;
            removeSwatchButton.IsEnabled = hasSelection;
            moveLeftButton.IsEnabled = hasSelection && selectedSwatchIndex > 0;
            moveRightButton.IsEnabled = hasSelection && selectedSwatchIndex < swatchCount - 1;
        }

        private static Color ToColor(uint rgba)
        {
            byte red = (byte)(rgba & 0xFFu);
            byte green = (byte)((rgba >> 8) & 0xFFu);
            byte blue = (byte)((rgba >> 16) & 0xFFu);
            byte alpha = (byte)((rgba >> 24) & 0xFFu);
            return Color.FromArgb(alpha, red, green, blue);
        }

        private static Button MakeButton(string caption, Action onClick)
        {
            var button = new Button { Content = caption, VerticalAlignment = VerticalAlignment.Center };
            button.Click += (sender, args) => onClick();
            return button;
        }

        private static Button MakeSmallButton(string glyph, string tip, Action onClick)
        {
            var button = new Button
            {
                Content = glyph,
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(button, tip);
            button.Click += (sender, args) => onClick();
            return button;
        }
    }
}
