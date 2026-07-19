using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Logic;

namespace EFYVLabyMake.App.UI
{
    // Inspector panel (item #3.1): the open project's schema/identity fields
    // via ToolbarAPI.GetEditableProperties, each with a kind-appropriate editor
    // (choice combo, ranged spinner, numeric/text box, read-only label) and an
    // inline error line fed by the edit's PropertyEditStatus and the live
    // per-field validation issue. Edits route through EditorShell.SetProperty
    // (undoable where the session makes it so).
    public sealed class InspectorPanel : UserControl
    {
        private readonly EditorShell shell;
        private readonly StackPanel fieldsPanel;
        private readonly Dictionary<string, TextBlock> errorLabels =
            new Dictionary<string, TextBlock>(StringComparer.Ordinal);

        public InspectorPanel(EditorShell editorShell)
        {
            shell = editorShell ?? throw new ArgumentNullException(nameof(editorShell));

            fieldsPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            var scroller = new ScrollViewer
            {
                Content = fieldsPanel,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };

            var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            root.Children.Add(scroller);
            Content = root;

            shell.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(EditorShell.HasSession))
                {
                    Rebuild();
                }
                else if (args.PropertyName == nameof(EditorShell.Problems))
                {
                    // A publish can revert values (undo/redo of a property edit)
                    // and always refreshes field errors. Rebuild only when the
                    // user is not mid-edit inside the panel, so typing is safe.
                    if (IsKeyboardFocusWithin) RefreshErrors();
                    else Rebuild();
                }
            };
            Rebuild();
        }

        private void Rebuild()
        {
            fieldsPanel.Children.Clear();
            errorLabels.Clear();
            if (!shell.HasSession)
            {
                fieldsPanel.Children.Add(new TextBlock
                {
                    Text = "No project open.",
                    Foreground = Brushes.Gray
                });
                return;
            }

            foreach (DesignerProperty property in shell.GetEditableProperties())
                fieldsPanel.Children.Add(BuildFieldRow(property));
        }

        private Control BuildFieldRow(DesignerProperty property)
        {
            var row = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            string label = string.IsNullOrEmpty(property.DisplayLabel) ? property.FieldName : property.DisplayLabel;
            row.Children.Add(new TextBlock
            {
                Text = property.IsRequired ? label + " *" : label,
                FontWeight = FontWeight.SemiBold
            });
            row.Children.Add(BuildEditor(property));

            var errorLabel = new TextBlock
            {
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Text = shell.GetFieldError(property.FieldName)
            };
            errorLabel.IsVisible = errorLabel.Text.Length > 0;
            errorLabels[property.FieldName] = errorLabel;
            row.Children.Add(errorLabel);
            return row;
        }

        private Control BuildEditor(DesignerProperty property)
        {
            if (property.IsReadOnly)
            {
                return new TextBox
                {
                    Text = PropertyFieldEditor.FormatValue(property.Value, property.ValueKind),
                    IsReadOnly = true,
                    IsEnabled = false
                };
            }

            if (property.Choices != null && property.Choices.Count > 0)
                return BuildChoiceEditor(property);

            bool numeric = property.ValueKind == SchemaValueKind.Integer ||
                property.ValueKind == SchemaValueKind.Float;
            if (numeric && property.HasRange)
                return BuildRangeEditor(property);

            return BuildTextEditor(property);
        }

        private Control BuildChoiceEditor(DesignerProperty property)
        {
            var choices = new List<string>(property.Choices);
            var combo = new ComboBox
            {
                ItemsSource = choices,
                SelectedItem = property.Value as string,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            combo.SelectionChanged += (sender, args) =>
            {
                if (combo.SelectedItem is string choice) Commit(property, choice);
            };
            return combo;
        }

        private Control BuildRangeEditor(DesignerProperty property)
        {
            decimal current;
            try
            {
                current = Convert.ToDecimal(property.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (
                exception is FormatException || exception is InvalidCastException ||
                exception is OverflowException)
            {
                current = (decimal)property.Minimum;
            }

            var spinner = new NumericUpDown
            {
                Minimum = (decimal)property.Minimum,
                Maximum = (decimal)property.Maximum,
                Increment = property.Step > 0 ? (decimal)property.Step : 1m,
                Value = current,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            spinner.ValueChanged += (sender, args) =>
            {
                if (!args.NewValue.HasValue) return;
                object value = property.ValueKind == SchemaValueKind.Integer
                    ? (object)(int)args.NewValue.Value
                    : (object)(float)args.NewValue.Value;
                Commit(property, value);
            };
            return spinner;
        }

        private Control BuildTextEditor(DesignerProperty property)
        {
            var box = new TextBox
            {
                Text = PropertyFieldEditor.FormatValue(property.Value, property.ValueKind)
            };
            Action commit = () =>
            {
                object value;
                if (!PropertyFieldEditor.TryParse(box.Text, property.ValueKind, out value))
                {
                    ShowError(property.FieldName,
                        PropertyFieldEditor.DescribeStatus(PropertyEditStatus.InvalidValue, property.ValueKind));
                    return;
                }
                Commit(property, value);
            };
            box.LostFocus += (sender, args) => commit();
            box.KeyDown += (sender, args) =>
            {
                if (args.Key == Avalonia.Input.Key.Enter) commit();
            };
            return box;
        }

        private void Commit(DesignerProperty property, object value)
        {
            PropertyEditResult result = shell.SetProperty(property.FieldName, value);
            ShowError(
                property.FieldName,
                result.Succeeded
                    ? shell.GetFieldError(property.FieldName)
                    : PropertyFieldEditor.DescribeStatus(result.Status, result.ExpectedKind));
        }

        private void RefreshErrors()
        {
            foreach (KeyValuePair<string, TextBlock> entry in errorLabels)
                ShowError(entry.Key, shell.GetFieldError(entry.Key));
        }

        private void ShowError(string fieldName, string message)
        {
            TextBlock label;
            if (!errorLabels.TryGetValue(fieldName, out label)) return;
            label.Text = message ?? "";
            label.IsVisible = label.Text.Length > 0;
        }
    }
}
