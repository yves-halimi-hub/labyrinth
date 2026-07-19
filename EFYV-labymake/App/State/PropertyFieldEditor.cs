using System.Globalization;
using EFYVLabyMake.Core.Logic;

namespace EFYVLabyMake.App.State
{
    // Pure, UI-framework-free helpers for the Inspector panel (item #1): turn a
    // schema field's typed value into editable text and back, and turn a failed
    // PropertyEditResult into an inline error message. The parse rules mirror
    // what ToolbarAPI.TrySetProperty accepts (invariant-culture numbers, raw
    // text) so what the box accepts and what the session accepts stay aligned.
    public static class PropertyFieldEditor
    {
        public static string FormatValue(object value, SchemaValueKind kind)
        {
            if (value == null) return "";
            switch (kind)
            {
                case SchemaValueKind.Float:
                    return System.Convert.ToSingle(value, CultureInfo.InvariantCulture)
                        .ToString("0.###", CultureInfo.InvariantCulture);
                case SchemaValueKind.Integer:
                    return System.Convert.ToInt32(value, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture);
                default:
                    return value as string ?? value.ToString();
            }
        }

        // Parses the box text into the CLR value kind the session expects
        // (float / int / string). Text always parses (an empty string is a
        // legal text value); numeric kinds require an invariant-culture number.
        public static bool TryParse(string text, SchemaValueKind kind, out object value)
        {
            value = null;
            switch (kind)
            {
                case SchemaValueKind.Float:
                    float parsedFloat;
                    if (!float.TryParse(
                            text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out parsedFloat) ||
                        float.IsNaN(parsedFloat) || float.IsInfinity(parsedFloat))
                        return false;
                    value = parsedFloat;
                    return true;
                case SchemaValueKind.Integer:
                    int parsedInt;
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                        return false;
                    value = parsedInt;
                    return true;
                default:
                    value = text ?? "";
                    return true;
            }
        }

        // Inline error text for a rejected edit; empty for success.
        public static string DescribeStatus(PropertyEditStatus status, SchemaValueKind expectedKind)
        {
            switch (status)
            {
                case PropertyEditStatus.Success: return "";
                case PropertyEditStatus.UnknownAssetType: return "Unknown asset type.";
                case PropertyEditStatus.UnknownField: return "Unknown field.";
                case PropertyEditStatus.ReadOnly: return "This field is read-only.";
                case PropertyEditStatus.OutOfRange: return "Value is out of range.";
                case PropertyEditStatus.InvalidChoice: return "Not an allowed choice.";
                case PropertyEditStatus.InvalidValue:
                    return "Enter a valid " + DescribeKind(expectedKind) + " value.";
                default: return "Invalid value.";
            }
        }

        private static string DescribeKind(SchemaValueKind kind)
        {
            switch (kind)
            {
                case SchemaValueKind.Float: return "number";
                case SchemaValueKind.Integer: return "whole-number";
                case SchemaValueKind.Text: return "text";
                default: return "";
            }
        }
    }
}
