using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Logic;

namespace EFYVLabyMake.App.UI
{
    // Problems panel (item #3.3): the live ProjectValidationResult grouped by
    // severity (errors first). An issue that carries a location renders as a
    // clickable row that selects the frame it points at; the rest are plain
    // lines. Fed by the item #27 lazy snapshot, so it stays cheap.
    public sealed class ProblemsPanel : UserControl
    {
        private readonly EditorShell shell;
        private readonly TextBlock summary;
        private readonly StackPanel listPanel;

        public ProblemsPanel(EditorShell editorShell)
        {
            shell = editorShell ?? throw new ArgumentNullException(nameof(editorShell));

            summary = new TextBlock { Foreground = Brushes.Gray };
            listPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };

            var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(summary);
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
                if (args.PropertyName == nameof(EditorShell.Problems)) Rebuild();
            };
            Rebuild();
        }

        private void Rebuild()
        {
            listPanel.Children.Clear();
            var errors = new List<ProjectIssue>();
            var warnings = new List<ProjectIssue>();
            foreach (ProjectIssue issue in shell.Problems)
            {
                if (issue.Severity == ProjectIssueSeverity.Error) errors.Add(issue);
                else warnings.Add(issue);
            }

            summary.Text = errors.Count.ToString(CultureInfo.InvariantCulture) + " errors · " +
                warnings.Count.ToString(CultureInfo.InvariantCulture) + " warnings";

            if (errors.Count == 0 && warnings.Count == 0)
            {
                listPanel.Children.Add(new TextBlock { Text = "No problems.", Foreground = Brushes.Gray });
                return;
            }

            AppendGroup("Errors", errors, Brushes.OrangeRed);
            AppendGroup("Warnings", warnings, Brushes.Goldenrod);
        }

        private void AppendGroup(string title, List<ProjectIssue> issues, IBrush color)
        {
            if (issues.Count == 0) return;
            listPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.Bold,
                Foreground = color,
                Margin = new Thickness(0, 4, 0, 0)
            });
            foreach (ProjectIssue issue in issues) listPanel.Children.Add(BuildRow(issue, color));
        }

        private Control BuildRow(ProjectIssue issue, IBrush color)
        {
            string line = ProblemFormatter.FormatLine(issue);
            if (!ProblemFormatter.HasFocusLocation(issue))
            {
                return new TextBlock
                {
                    Text = "• " + line,
                    Foreground = color,
                    TextWrapping = TextWrapping.Wrap
                };
            }

            ProjectIssue captured = issue;
            var button = new Button
            {
                Content = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap },
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(4, 2, 4, 2)
            };
            ToolTip.SetTip(button, "Go to " + ProblemFormatter.FormatLocation(issue));
            button.Click += (sender, args) => shell.FocusProblem(captured);
            return button;
        }
    }
}
