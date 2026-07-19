using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Logic;

namespace EFYVLabyMake.App.UI
{
    // Live-debug controls (item #3.5): the persisted Unity project path, the
    // StartWatching toggle (OFF by default, surfaced clearly), a manual
    // "Push to game" (ExportNowAsync) with a busy state, and the live-debug
    // status + export-scope problems from the LiveDebugSnapshot.
    public sealed class LiveDebugPanel : UserControl
    {
        private readonly EditorShell shell;
        private readonly Func<Task> editUnityPath;
        private readonly TextBlock unityPathLabel;
        private readonly ToggleButton watchToggle;
        private readonly Button pushButton;
        private readonly TextBlock statusLabel;
        private readonly TextBlock syncLabel;
        private readonly StackPanel problemsPanel;
        private bool building;

        public LiveDebugPanel(EditorShell editorShell, Func<Task> onEditUnityPath)
        {
            shell = editorShell ?? throw new ArgumentNullException(nameof(editorShell));
            editUnityPath = onEditUnityPath ?? throw new ArgumentNullException(nameof(onEditUnityPath));

            unityPathLabel = new TextBlock
            {
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            };
            var pathRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            pathRow.Children.Add(MakeButton("Unity path…", async () => await editUnityPath()));
            var pathColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            pathColumn.Children.Add(pathRow);
            pathColumn.Children.Add(unityPathLabel);

            watchToggle = new ToggleButton { Content = "Watch: OFF" };
            watchToggle.Click += (sender, args) =>
            {
                if (building) return;
                shell.LiveWatching = watchToggle.IsChecked == true;
            };

            pushButton = MakeButton("Push to game", async () => await shell.PushToGameAsync());

            statusLabel = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
            syncLabel = new TextBlock { Foreground = Brushes.Gray, FontSize = 11 };
            problemsPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };

            var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            root.Children.Add(pathColumn);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            actions.Children.Add(watchToggle);
            actions.Children.Add(pushButton);
            root.Children.Add(actions);
            root.Children.Add(statusLabel);
            root.Children.Add(syncLabel);
            root.Children.Add(new ScrollViewer
            {
                Content = problemsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 140
            });
            Content = root;

            shell.PropertyChanged += (sender, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(EditorShell.LiveDebug):
                    case nameof(EditorShell.LiveWatching):
                    case nameof(EditorShell.UnityProjectPath):
                    case nameof(EditorShell.CanPush):
                    case nameof(EditorShell.HasSession):
                        Refresh();
                        break;
                }
            };
            Refresh();
        }

        private void Refresh()
        {
            building = true;
            try
            {
                string path = shell.UnityProjectPath;
                unityPathLabel.Text = string.IsNullOrEmpty(path) ? "(not set)" : path;

                bool watching = shell.LiveWatching;
                watchToggle.IsChecked = watching;
                watchToggle.Content = watching ? "Watch: ON" : "Watch: OFF";
                watchToggle.IsEnabled = shell.HasSession;

                pushButton.IsEnabled = shell.CanPush;
                pushButton.Content = shell.IsPushing ? "Pushing…" : "Push to game";

                LiveDebugSnapshot snapshot = shell.LiveDebug;
                statusLabel.Text = LiveDebugFormatter.FormatStatus(snapshot);
                syncLabel.Text = LiveDebugFormatter.FormatLastSync(snapshot);
                syncLabel.IsVisible = syncLabel.Text.Length > 0;

                RebuildProblems(snapshot);
            }
            finally
            {
                building = false;
            }
        }

        private void RebuildProblems(LiveDebugSnapshot snapshot)
        {
            problemsPanel.Children.Clear();
            if (snapshot?.Validation == null) return;
            foreach (ProjectIssue issue in snapshot.Validation.Issues)
            {
                problemsPanel.Children.Add(new TextBlock
                {
                    Text = "• " + ProblemFormatter.FormatLine(issue),
                    Foreground = issue.Severity == ProjectIssueSeverity.Error
                        ? Brushes.OrangeRed
                        : Brushes.Goldenrod,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private static Button MakeButton(string caption, Action onClick)
        {
            var button = new Button { Content = caption };
            button.Click += (sender, args) => onClick();
            return button;
        }
    }
}
