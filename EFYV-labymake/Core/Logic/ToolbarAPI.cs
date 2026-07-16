using System;
using System.Collections.Generic;
using System.Globalization;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public enum PropertyEditStatus
    {
        Success,
        UnknownAssetType,
        UnknownField,
        InvalidValue,
        ReadOnly,
        OutOfRange,
        InvalidChoice
    }

    public readonly struct PropertyEditResult
    {
        public PropertyEditStatus Status { get; }
        public string FieldName { get; }
        public SchemaValueKind ExpectedKind { get; }
        public bool Succeeded => Status == PropertyEditStatus.Success;

        public PropertyEditResult(
            PropertyEditStatus status,
            string fieldName,
            SchemaValueKind expectedKind = SchemaValueKind.Unknown)
        {
            Status = status;
            FieldName = fieldName;
            ExpectedKind = expectedKind;
        }
    }

    public sealed class DesignableCategory
    {
        public string Label { get; }
        public string AssetType { get; }
        public string DisplayName { get; }
        public string Facing { get; }

        internal DesignableCategory(string label, string assetType, string displayName, string facing)
        {
            Label = label;
            AssetType = assetType;
            DisplayName = displayName;
            Facing = facing;
        }
    }

    public sealed class DesignerProperty
    {
        public string FieldName { get; }
        public string FieldType { get; }
        public SchemaValueKind ValueKind { get; }
        public object Value { get; }
        public string DisplayLabel { get; }
        public object DefaultValue { get; }
        public bool HasRange { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Step { get; }
        public bool IsRequired { get; }
        public bool IsReadOnly { get; }
        public IReadOnlyList<string> Choices { get; }

        internal DesignerProperty(SchemaField field, object value)
        {
            FieldName = field.FieldName;
            FieldType = field.FieldType;
            ValueKind = field.ValueKind;
            Value = value;
            DisplayLabel = field.DisplayLabel;
            DefaultValue = field.DefaultValue;
            HasRange = field.HasRange;
            Minimum = field.Minimum;
            Maximum = field.Maximum;
            Step = field.Step;
            IsRequired = field.IsRequired;
            IsReadOnly = field.IsReadOnly;
            Choices = field.Choices;
        }
    }

    public sealed class ToolbarAPI
    {
        private readonly AssetSchemaService schemaService;

        public ToolbarAPI(AssetSchemaService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            schemaService = service;
        }

        public List<string> GetDesignableCategories()
        {
            var definitions = GetDesignableCategoryDefinitions();
            var labels = new List<string>(definitions.Count);
            foreach (var definition in definitions) labels.Add(definition.Label);
            return labels;
        }

        public List<DesignableCategory> GetDesignableCategoryDefinitions()
        {
            var types = schemaService.GetAvailableTypes();
            var categories = new List<DesignableCategory>(types.Count * Config.Entity.DirectionalVariantCount);

            foreach (var type in types)
            {
                if (type.IsDirectional)
                {
                    AddDirectionalCategory(categories, type, Config.Entity.SuffixUp, Config.Entity.FacingUp);
                    AddDirectionalCategory(categories, type, Config.Entity.SuffixDown, Config.Entity.FacingDown);
                    AddDirectionalCategory(categories, type, Config.Entity.SuffixLeft, Config.Entity.FacingLeft);
                    AddDirectionalCategory(categories, type, Config.Entity.SuffixRight, Config.Entity.FacingRight);
                }
                else
                {
                    categories.Add(new DesignableCategory(
                        type.DisplayName,
                        type.AssetType,
                        type.DisplayName,
                        Config.Entity.FacingNone));
                }
            }

            return categories;
        }

        public EFYVProject CreateNewProject(string categoryLabel)
        {
            if (categoryLabel == null) return null;

            DesignableCategory selected = null;
            foreach (var category in GetDesignableCategoryDefinitions())
            {
                if (string.Equals(category.Label, categoryLabel, StringComparison.Ordinal))
                {
                    selected = category;
                    break;
                }
            }
            if (selected == null) return null;

            SchemaDefinition definition;
            if (!schemaService.TryGetTypeDefinition(selected.AssetType, out definition)) return null;

            var project = new EFYVProject(definition.AssetType);
            foreach (var field in definition.Fields)
                project.AssetProperties[field.FieldName] = field.DefaultValue;

            if (definition.IsDirectional)
                project.AssetProperties[Config.Entity.KeyFacing] = selected.Facing;
            return project;
        }

        public List<DesignerProperty> GetEditableProperties(EFYVProject project)
        {
            var properties = new List<DesignerProperty>();
            if (project == null) return properties;

            SchemaDefinition definition;
            if (!schemaService.TryGetTypeDefinition(project.TargetAssetType, out definition)) return properties;

            properties.Capacity = definition.Fields.Count;
            foreach (var field in definition.Fields)
            {
                object value;
                if (!project.AssetProperties.TryGetValue(field.FieldName, out value))
                    value = field.DefaultValue;
                properties.Add(new DesignerProperty(field, value));
            }
            return properties;
        }

        public PropertyEditResult TrySetProperty(EFYVProject project, string fieldName, object value)
        {
            if (project == null) return new PropertyEditResult(PropertyEditStatus.UnknownAssetType, fieldName);

            SchemaDefinition definition;
            if (!schemaService.TryGetTypeDefinition(project.TargetAssetType, out definition))
                return new PropertyEditResult(PropertyEditStatus.UnknownAssetType, fieldName);

            foreach (var field in definition.Fields)
            {
                if (!string.Equals(field.FieldName, fieldName, StringComparison.Ordinal)) continue;
                if (field.IsReadOnly)
                    return new PropertyEditResult(PropertyEditStatus.ReadOnly, fieldName, field.ValueKind);

                object normalized;
                if (!TryNormalizeValue(field.ValueKind, value, out normalized))
                    return new PropertyEditResult(PropertyEditStatus.InvalidValue, fieldName, field.ValueKind);

                if (field.HasRange)
                {
                    double numeric = Convert.ToDouble(normalized, CultureInfo.InvariantCulture);
                    if (numeric < field.Minimum || numeric > field.Maximum)
                        return new PropertyEditResult(PropertyEditStatus.OutOfRange, fieldName, field.ValueKind);
                }

                if (field.Choices.Count > Config.Common.EmptyCount &&
                    !ContainsChoice(field.Choices, normalized as string))
                    return new PropertyEditResult(PropertyEditStatus.InvalidChoice, fieldName, field.ValueKind);

                project.AssetProperties[fieldName] = normalized;
                return new PropertyEditResult(PropertyEditStatus.Success, fieldName, field.ValueKind);
            }

            return new PropertyEditResult(PropertyEditStatus.UnknownField, fieldName);
        }

        internal static object CreateDefaultValue(SchemaValueKind kind)
        {
            switch (kind)
            {
                case SchemaValueKind.Float: return Config.Types.DefaultFloat;
                case SchemaValueKind.Integer: return Config.Types.DefaultInt;
                case SchemaValueKind.Text: return Config.Types.DefaultString;
                default: return null;
            }
        }

        internal static bool ContainsChoice(IReadOnlyList<string> choices, string value)
        {
            for (int index = Config.Common.FirstIndex; index < choices.Count; index++)
            {
                if (string.Equals(choices[index], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal static bool TryNormalizeValue(SchemaValueKind kind, object value, out object normalized)
        {
            normalized = null;
            if (value == null) return false;

            try
            {
                switch (kind)
                {
                    case SchemaValueKind.Float:
                        if (value is string || value is bool || value is char) return false;
                        float numeric = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                        if (float.IsNaN(numeric) || float.IsInfinity(numeric)) return false;
                        normalized = numeric;
                        return true;

                    case SchemaValueKind.Integer:
                        if (value is byte || value is sbyte || value is short || value is ushort ||
                            value is int || value is uint || value is long || value is ulong)
                        {
                            normalized = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                            return true;
                        }
                        return false;

                    case SchemaValueKind.Text:
                        normalized = value as string;
                        return normalized != null;
                }
            }
            catch (Exception exception) when (
                exception is FormatException ||
                exception is InvalidCastException ||
                exception is OverflowException)
            {
                return false;
            }

            return false;
        }

        private static void AddDirectionalCategory(
            ICollection<DesignableCategory> categories,
            SchemaDefinition definition,
            string suffix,
            string facing)
        {
            categories.Add(new DesignableCategory(
                definition.DisplayName + suffix,
                definition.AssetType,
                definition.DisplayName,
                facing));
        }
    }
}
