using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EFYVLabyMake.App.UI
{
    // Modal editor for the export target Unity project path (item #5). Closes
    // with the entered path on OK, null on cancel. Validation (the Assets
    // directory check) is left to the export/live-debug validator so an
    // in-progress path can be typed and corrected.
    public sealed class UnityPathDialog : Window
    {
        private readonly TextBox pathBox;

        public UnityPathDialog(string currentPath)
        {
            Title = "Unity Project Path";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;

            pathBox = new TextBox
            {
                Text = currentPath ?? "",
                Watermark = @"C:\path\to\UnityProject"
            };

            var okButton = new Button { Content = "OK", IsDefault = true };
            okButton.Click += (sender, args) => Close(pathBox.Text?.Trim() ?? "");
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

            var layout = new StackPanel { Spacing = 6, Margin = new Thickness(14) };
            layout.Children.Add(new TextBlock { Text = "Unity project root (contains an Assets folder)" });
            layout.Children.Add(pathBox);
            layout.Children.Add(new TextBlock
            {
                Text = "Exports and live debug publish under <path>/Assets.",
                Foreground = Brushes.Gray,
                FontSize = 11
            });
            layout.Children.Add(buttons);
            Content = layout;
        }
    }
}
