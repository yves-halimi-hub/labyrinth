using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EFYVLabyMake.Core.Persistence;

namespace EFYVLabyMake.App.UI
{
    // Modal "open existing project" browser (item #6): lists the committed
    // projects discovered by ProjectPersistenceService.ListProjects (name +
    // last-write time). Closes with the selected project name on Open, null on
    // cancel. Autosave recovery is handled by the caller after selection.
    public sealed class ProjectBrowserDialog : Window
    {
        private readonly ListBox listBox;
        private readonly List<ProjectListEntry> entries;

        public ProjectBrowserDialog(IReadOnlyList<ProjectListEntry> projects)
        {
            entries = new List<ProjectListEntry>(projects ?? Array.Empty<ProjectListEntry>());
            Title = "Open Project";
            Width = 460;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var rows = new List<string>(entries.Count);
            foreach (ProjectListEntry entry in entries)
            {
                rows.Add(entry.Name + "    —    " +
                    entry.LastWriteUtc.ToLocalTime().ToString(
                        "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }

            listBox = new ListBox
            {
                ItemsSource = rows,
                SelectedIndex = rows.Count > 0 ? 0 : -1
            };
            listBox.DoubleTapped += (sender, args) => TryAccept();

            var openButton = new Button { Content = "Open", IsDefault = true };
            openButton.Click += (sender, args) => TryAccept();
            var cancelButton = new Button { Content = "Cancel", IsCancel = true };
            cancelButton.Click += (sender, args) => Close(null);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(openButton);
            buttons.Children.Add(cancelButton);

            var layout = new DockPanel { Margin = new Thickness(14) };
            var header = new TextBlock
            {
                Text = entries.Count == 0
                    ? "No saved projects in this folder."
                    : "Saved projects:",
                Foreground = entries.Count == 0 ? Brushes.Gray : Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            buttons.Margin = new Thickness(0, 8, 0, 0);
            layout.Children.Add(header);
            layout.Children.Add(buttons);
            layout.Children.Add(listBox);
            Content = layout;
        }

        private void TryAccept()
        {
            int index = listBox.SelectedIndex;
            if (index < 0 || index >= entries.Count) return;
            Close(entries[index].Name);
        }
    }
}
