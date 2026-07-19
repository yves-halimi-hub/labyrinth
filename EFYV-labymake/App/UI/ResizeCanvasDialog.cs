using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.App.UI
{
    // Modal canvas-resize flow: new width/height plus the 9-position content
    // anchor. Closes with a ResizeCanvasRequest on OK, null on cancel; the
    // shell routes it into DesignerSession.ResizeCanvas (one undoable command).
    public sealed class ResizeCanvasDialog : Window
    {
        private static readonly CanvasAnchor[] Anchors =
        {
            CanvasAnchor.TopLeft,
            CanvasAnchor.TopCenter,
            CanvasAnchor.TopRight,
            CanvasAnchor.MiddleLeft,
            CanvasAnchor.MiddleCenter,
            CanvasAnchor.MiddleRight,
            CanvasAnchor.BottomLeft,
            CanvasAnchor.BottomCenter,
            CanvasAnchor.BottomRight
        };

        private readonly NumericUpDown widthUpDown;
        private readonly NumericUpDown heightUpDown;
        private readonly ComboBox anchorCombo;
        private readonly TextBlock errorText;

        public ResizeCanvasDialog(int currentWidth, int currentHeight)
        {
            Title = "Resize Canvas";
            Width = 340;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;

            widthUpDown = MakeDimensionUpDown(currentWidth);
            heightUpDown = MakeDimensionUpDown(currentHeight);

            var anchorLabels = new List<string>(Anchors.Length);
            foreach (CanvasAnchor anchor in Anchors) anchorLabels.Add(anchor.ToString());
            anchorCombo = new ComboBox
            {
                ItemsSource = anchorLabels,
                SelectedIndex = Array.IndexOf(Anchors, CanvasAnchor.MiddleCenter),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            errorText = new TextBlock
            {
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                IsVisible = false
            };

            var okButton = new Button { Content = "Resize", IsDefault = true };
            okButton.Click += (sender, args) => TryAccept();
            var cancelButton = new Button { Content = "Cancel", IsCancel = true };
            cancelButton.Click += (sender, args) => Close(null);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            sizeRow.Children.Add(widthUpDown);
            sizeRow.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
            sizeRow.Children.Add(heightUpDown);

            var layout = new StackPanel { Spacing = 6, Margin = new Thickness(14) };
            layout.Children.Add(new TextBlock { Text = "New canvas size (pixels)" });
            layout.Children.Add(sizeRow);
            layout.Children.Add(new TextBlock { Text = "Anchor existing content at" });
            layout.Children.Add(anchorCombo);
            layout.Children.Add(errorText);
            layout.Children.Add(buttons);
            Content = layout;
        }

        private static NumericUpDown MakeDimensionUpDown(int value)
        {
            return new NumericUpDown
            {
                Minimum = AppDefaults.MinCanvasInput,
                Maximum = Config.Persistence.MaxCanvasDimension,
                Value = value,
                Increment = 1,
                Width = 120
            };
        }

        private void TryAccept()
        {
            if (!widthUpDown.Value.HasValue || !heightUpDown.Value.HasValue)
            {
                ShowError("Canvas size is required.");
                return;
            }
            if (anchorCombo.SelectedIndex < 0 || anchorCombo.SelectedIndex >= Anchors.Length)
            {
                ShowError("Pick an anchor.");
                return;
            }

            Close(new ResizeCanvasRequest(
                (int)widthUpDown.Value.Value,
                (int)heightUpDown.Value.Value,
                Anchors[anchorCombo.SelectedIndex]));
        }

        private void ShowError(string message)
        {
            errorText.Text = message;
            errorText.IsVisible = true;
        }
    }
}
