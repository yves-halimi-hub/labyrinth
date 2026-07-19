using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.IO;
using EFYVLabyMake.Core.Logic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.App.UI
{
    // Modal new-project flow: project name + canvas size + designable-category
    // picker (ToolbarAPI.GetDesignableCategoryDefinitions). Closes with a
    // NewProjectRequest on OK, null on cancel.
    public sealed class NewProjectDialog : Window
    {
        private readonly TextBox nameBox;
        private readonly ComboBox categoryCombo;
        private readonly NumericUpDown widthUpDown;
        private readonly NumericUpDown heightUpDown;
        private readonly TextBox directoryBox;
        private readonly TextBlock errorText;
        private readonly List<DesignableCategory> categories;

        public NewProjectDialog(List<DesignableCategory> designableCategories, string defaultDirectory)
        {
            categories = designableCategories ?? throw new ArgumentNullException(nameof(designableCategories));
            Title = "New Project";
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;

            nameBox = new TextBox { Watermark = "MyEnemy" };

            var labels = new List<string>(categories.Count);
            foreach (DesignableCategory category in categories) labels.Add(category.Label);
            categoryCombo = new ComboBox
            {
                ItemsSource = labels,
                SelectedIndex = labels.Count > 0 ? 0 : -1,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            widthUpDown = MakeDimensionUpDown();
            heightUpDown = MakeDimensionUpDown();
            directoryBox = new TextBox { Text = defaultDirectory };
            errorText = new TextBlock
            {
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                IsVisible = false
            };

            var okButton = new Button { Content = "Create", IsDefault = true };
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
            layout.Children.Add(new TextBlock { Text = "Project name" });
            layout.Children.Add(nameBox);
            layout.Children.Add(new TextBlock { Text = "Category" });
            layout.Children.Add(categoryCombo);
            layout.Children.Add(new TextBlock { Text = "Canvas size (pixels)" });
            layout.Children.Add(sizeRow);
            layout.Children.Add(new TextBlock { Text = "Project directory" });
            layout.Children.Add(directoryBox);
            layout.Children.Add(errorText);
            layout.Children.Add(buttons);
            Content = layout;
        }

        private static NumericUpDown MakeDimensionUpDown()
        {
            return new NumericUpDown
            {
                Minimum = AppDefaults.MinCanvasInput,
                Maximum = Config.Persistence.MaxCanvasDimension,
                Value = Config.Canvas.DefaultWidth,
                Increment = 1,
                Width = 130
            };
        }

        private void TryAccept()
        {
            string name = nameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name) || !DesignerPathPolicy.IsSafeFileStem(name))
            {
                ShowError("Project name must be a valid file name (no path separators or reserved names).");
                return;
            }
            if (categoryCombo.SelectedIndex < 0 || categoryCombo.SelectedIndex >= categories.Count)
            {
                ShowError("Pick a category.");
                return;
            }
            if (!widthUpDown.Value.HasValue || !heightUpDown.Value.HasValue)
            {
                ShowError("Canvas size is required.");
                return;
            }
            string directory = directoryBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(directory))
            {
                ShowError("Project directory is required.");
                return;
            }

            Close(new NewProjectRequest(
                name,
                categories[categoryCombo.SelectedIndex].Label,
                (int)widthUpDown.Value.Value,
                (int)heightUpDown.Value.Value,
                directory));
        }

        private void ShowError(string message)
        {
            errorText.Text = message;
            errorText.IsVisible = true;
        }
    }
}
