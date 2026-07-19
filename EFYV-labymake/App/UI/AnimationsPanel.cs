using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.App.UI
{
    // Animations + frames panel (item #3.2): full CRUD over the session's
    // animation and frame surfaces - add/remove/rename/select/duplicate/reorder
    // animations, edit the selected animation's FPS / loop range / ping-pong,
    // and add/remove/duplicate/reorder its frames. Frame selection and per-frame
    // duration stay on the bottom timeline strip; this panel owns structure.
    public sealed class AnimationsPanel : UserControl
    {
        private readonly EditorShell shell;
        private readonly StackPanel animationList;
        private readonly StackPanel detailsPanel;
        private readonly StackPanel framesPanel;
        private bool building;

        public AnimationsPanel(EditorShell editorShell)
        {
            shell = editorShell ?? throw new ArgumentNullException(nameof(editorShell));

            animationList = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
            detailsPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            framesPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(MakeButton("+ Add animation", () => shell.AddAnimation(null)));

            var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            root.Children.Add(header);
            root.Children.Add(new ScrollViewer
            {
                Content = animationList,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 150
            });
            root.Children.Add(detailsPanel);
            root.Children.Add(new TextBlock { Text = "Frames", Foreground = Brushes.Gray });
            root.Children.Add(new ScrollViewer
            {
                Content = framesPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 120
            });
            Content = root;

            shell.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(EditorShell.Animations) ||
                    args.PropertyName == nameof(EditorShell.SelectedAnimationIndex) ||
                    args.PropertyName == nameof(EditorShell.TimelineFrameCount) ||
                    args.PropertyName == nameof(EditorShell.TimelineFrameIndex))
                    Rebuild();
            };
            Rebuild();
        }

        private void Rebuild()
        {
            building = true;
            try
            {
                RebuildAnimationList();
                RebuildDetails();
                RebuildFrames();
            }
            finally
            {
                building = false;
            }
        }

        private void RebuildAnimationList()
        {
            animationList.Children.Clear();
            var animations = shell.Animations;
            int selected = shell.SelectedAnimationIndex;
            for (int index = 0; index < animations.Count; index++)
            {
                int captured = index;
                AnimationState animation = animations[index];
                var selectToggle = new ToggleButton
                {
                    Content = animation.StateName + "  (" +
                        animation.Frames.Count.ToString(CultureInfo.InvariantCulture) + "f, " +
                        animation.FPS.ToString(CultureInfo.InvariantCulture) + "fps)",
                    IsChecked = index == selected,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                selectToggle.Click += (sender, args) => shell.SelectAnimation(captured);

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                row.Children.Add(new Border { Child = selectToggle, MinWidth = 150 });
                row.Children.Add(MakeSmallButton("▲", "Move up",
                    () => shell.MoveAnimation(captured, captured + 1), index < animations.Count - 1));
                row.Children.Add(MakeSmallButton("▼", "Move down",
                    () => shell.MoveAnimation(captured, captured - 1), index > 0));
                row.Children.Add(MakeSmallButton("⧉", "Duplicate",
                    () => shell.DuplicateAnimation(captured), true));
                row.Children.Add(MakeSmallButton("✕", "Remove",
                    () => shell.RemoveAnimation(captured), animations.Count > 1));
                animationList.Children.Add(row);
            }
        }

        private void RebuildDetails()
        {
            detailsPanel.Children.Clear();
            var animations = shell.Animations;
            int index = shell.SelectedAnimationIndex;
            if (index < 0 || index >= animations.Count) return;
            AnimationState animation = animations[index];

            var nameBox = new TextBox { Text = animation.StateName };
            Action commitName = () =>
            {
                if (building) return;
                string name = nameBox.Text?.Trim();
                if (!string.IsNullOrEmpty(name) && name != animation.StateName)
                    shell.RenameAnimation(index, name);
            };
            nameBox.LostFocus += (sender, args) => commitName();
            nameBox.KeyDown += (sender, args) => { if (args.Key == Key.Enter) commitName(); };
            detailsPanel.Children.Add(LabeledRow("Name", nameBox));

            var fps = new NumericUpDown
            {
                Minimum = 1m,
                Maximum = 240m,
                Increment = 1m,
                Value = animation.FPS
            };
            fps.ValueChanged += (sender, args) =>
            {
                if (building || !args.NewValue.HasValue) return;
                shell.SetAnimationFps(index, (int)args.NewValue.Value);
            };
            detailsPanel.Children.Add(LabeledRow("FPS", fps));

            int frameCount = animation.Frames.Count;
            int lastFrame = frameCount > 0 ? frameCount - 1 : 0;
            var loopStart = new NumericUpDown
            {
                Minimum = 0m,
                Maximum = lastFrame,
                Increment = 1m,
                Value = System.Math.Min(animation.LoopStartFrame, lastFrame)
            };
            var loopEnd = new NumericUpDown
            {
                Minimum = 0m,
                Maximum = lastFrame,
                Increment = 1m,
                // FullRangeLoopEnd (-1) or a stale end shows the last frame.
                Value = animation.LoopEndFrame == Config.Animation.FullRangeLoopEnd ||
                    animation.LoopEndFrame > lastFrame
                    ? lastFrame
                    : animation.LoopEndFrame
            };
            EventHandler<NumericUpDownValueChangedEventArgs> loopChanged = (sender, args) =>
            {
                if (building || !loopStart.Value.HasValue || !loopEnd.Value.HasValue) return;
                shell.SetAnimationLoopRange(index, (int)loopStart.Value.Value, (int)loopEnd.Value.Value);
            };
            loopStart.ValueChanged += loopChanged;
            loopEnd.ValueChanged += loopChanged;
            var loopRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            loopRow.Children.Add(new TextBlock
            {
                Text = "Loop",
                Width = 40,
                VerticalAlignment = VerticalAlignment.Center
            });
            loopRow.Children.Add(loopStart);
            loopRow.Children.Add(new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center });
            loopRow.Children.Add(loopEnd);
            detailsPanel.Children.Add(loopRow);

            var pingPong = new CheckBox { Content = "Ping-pong", IsChecked = animation.PingPong };
            pingPong.IsCheckedChanged += (sender, args) =>
            {
                if (building) return;
                shell.SetAnimationPingPong(index, pingPong.IsChecked == true);
            };
            detailsPanel.Children.Add(pingPong);
        }

        private void RebuildFrames()
        {
            framesPanel.Children.Clear();
            var animations = shell.Animations;
            int index = shell.SelectedAnimationIndex;

            framesPanel.Children.Add(MakeButton("+ Add frame", () => shell.AddFrame()));
            if (index < 0 || index >= animations.Count) return;
            IReadOnlyList<Frame> frames = animations[index].Frames;
            int selectedFrame = shell.TimelineFrameIndex;

            var grid = new WrapPanel { Orientation = Orientation.Horizontal };
            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                int captured = frameIndex;
                var cell = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 1,
                    Margin = new Thickness(0, 0, 4, 4)
                };
                var selectToggle = new ToggleButton
                {
                    Content = (frameIndex + 1).ToString(CultureInfo.InvariantCulture),
                    IsChecked = frameIndex == selectedFrame,
                    MinWidth = 34
                };
                selectToggle.Click += (sender, args) => shell.SelectTimelineFrame(captured);
                cell.Children.Add(selectToggle);

                var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
                ops.Children.Add(MakeSmallButton("⧉", "Duplicate frame",
                    () => shell.DuplicateFrame(captured), true));
                ops.Children.Add(MakeSmallButton("✕", "Remove frame",
                    () => shell.RemoveFrame(captured), true));
                cell.Children.Add(ops);
                var moveOps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
                moveOps.Children.Add(MakeSmallButton("◀", "Move earlier",
                    () => shell.MoveFrame(captured, captured - 1), frameIndex > 0));
                moveOps.Children.Add(MakeSmallButton("▶", "Move later",
                    () => shell.MoveFrame(captured, captured + 1), frameIndex < frames.Count - 1));
                cell.Children.Add(moveOps);

                grid.Children.Add(cell);
            }
            framesPanel.Children.Add(grid);
        }

        private static Control LabeledRow(string label, Control editor)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Width = 40,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(editor);
            return row;
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
