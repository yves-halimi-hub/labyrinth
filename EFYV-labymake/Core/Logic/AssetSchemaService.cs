using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public enum SchemaValueKind
    {
        Unknown,
        Float,
        Integer,
        Text
    }

    public readonly struct SchemaField
    {
        public string FieldName { get; }
        public string FieldType { get; }
        public SchemaValueKind ValueKind { get; }
        public string DisplayLabel { get; }
        public object DefaultValue { get; }
        public bool HasRange { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Step { get; }
        public bool IsRequired { get; }
        public bool IsReadOnly { get; }
        public IReadOnlyList<string> Choices { get; }

        internal SchemaField(Config.Schema.FieldDefinition definition)
        {
            FieldName = definition.Name;
            FieldType = definition.FieldType;
            ValueKind = AssetSchemaService.ResolveValueKind(definition.FieldType);
            DisplayLabel = definition.Editor.Label;
            DefaultValue = definition.Editor.DefaultValue;
            HasRange = definition.Editor.HasRange;
            Minimum = definition.Editor.Minimum;
            Maximum = definition.Editor.Maximum;
            Step = definition.Editor.Step;
            IsRequired = definition.Editor.IsRequired;
            IsReadOnly = definition.Editor.IsReadOnly;
            Choices = definition.Editor.Choices == null
                ? new ReadOnlyCollection<string>(new string[Config.Common.EmptyCount])
                : new ReadOnlyCollection<string>((string[])definition.Editor.Choices.Clone());
        }

        // Item #33b: a runtime-registered custom field (name + slot kind, no
        // backend config edit). Optional, unranged, label = field name; the
        // default value matches the kind's designer default.
        internal SchemaField(string fieldName, SchemaValueKind valueKind)
        {
            FieldName = fieldName;
            FieldType = AssetSchemaService.ResolveFieldType(valueKind);
            ValueKind = valueKind;
            DisplayLabel = fieldName;
            DefaultValue = ToolbarAPI.CreateDefaultValue(valueKind);
            HasRange = false;
            Minimum = 0d;
            Maximum = 0d;
            Step = 0d;
            IsRequired = false;
            IsReadOnly = false;
            Choices = new ReadOnlyCollection<string>(new string[Config.Common.EmptyCount]);
        }
    }

    // Item #33b: one custom field to add when registering an asset type -
    // the field name (the .efyvlaby properties key it will ride under) and
    // the slot kind (Float/Integer/Text).
    public readonly struct CustomFieldRegistration
    {
        public string FieldName { get; }
        public SchemaValueKind ValueKind { get; }

        public CustomFieldRegistration(string fieldName, SchemaValueKind valueKind)
        {
            FieldName = fieldName;
            ValueKind = valueKind;
        }
    }

    public readonly struct AssetSchemaRegistration
    {
        public string AssetType { get; }
        public string DisplayName { get; }
        public string BaseAssetType { get; }

        public AssetSchemaRegistration(string assetType, string displayName, string baseAssetType)
        {
            AssetType = assetType;
            DisplayName = displayName;
            BaseAssetType = baseAssetType;
        }
    }

    public sealed class SchemaDefinition
    {
        public string AssetType { get; }
        public string DisplayName { get; }
        public string BaseAssetType { get; }
        public bool IsDirectional { get; }
        public string IdentityFieldName { get; }
        public IReadOnlyList<SchemaField> Fields { get; }

        internal SchemaDefinition(
            string assetType,
            string displayName,
            string baseAssetType,
            bool isDirectional,
            IList<SchemaField> fields)
        {
            AssetType = assetType;
            DisplayName = displayName;
            BaseAssetType = baseAssetType;
            IsDirectional = isDirectional;
            Fields = new ReadOnlyCollection<SchemaField>(new List<SchemaField>(fields));
            IdentityFieldName = ResolveIdentityField(Fields);
        }

        private static string ResolveIdentityField(IReadOnlyList<SchemaField> fields)
        {
            foreach (var field in fields)
            {
                if (field.FieldName == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.EntityNameField ||
                    field.FieldName == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.AssetNameField)
                    return field.FieldName;
            }
            return null;
        }
    }

    public sealed class AssetSchemaService
    {
        private readonly List<SchemaDefinition> assets = new List<SchemaDefinition>();
        private readonly Dictionary<string, SchemaDefinition> assetsByType =
            new Dictionary<string, SchemaDefinition>(StringComparer.Ordinal);
        private readonly ReadOnlyCollection<SchemaDefinition> readOnlyAssets;

        public AssetSchemaService()
        {
            readOnlyAssets = assets.AsReadOnly();
            LoadBuiltIns();
        }

        public IReadOnlyList<SchemaDefinition> GetAvailableTypes()
        {
            return readOnlyAssets;
        }

        public SchemaDefinition GetTypeDefinition(string assetType)
        {
            SchemaDefinition definition;
            return assetType != null && assetsByType.TryGetValue(assetType, out definition)
                ? definition
                : null;
        }

        public bool TryGetTypeDefinition(string assetType, out SchemaDefinition definition)
        {
            if (assetType == null)
            {
                definition = null;
                return false;
            }

            return assetsByType.TryGetValue(assetType, out definition);
        }

        public bool RegisterAssetType(AssetSchemaRegistration registration)
        {
            return RegisterAssetType(registration, System.Array.Empty<CustomFieldRegistration>());
        }

        // Item #33b: registers a derived type AND adds custom fields to it
        // (name + slot kind) without any backend config edit. Custom-field
        // values flow end to end as ordinary .efyvlaby properties keys: the
        // designer edits them through the normal property surface, the
        // exporter writes them, and the Unity importer parks them on
        // SchemaBackedAssetData's string-keyed custom-property store (they are
        // unknown to the compiled schema manifest by construction). Rejected
        // (returns false, nothing registered) when any field name is blank,
        // over the length cap, duplicated, colliding with an inherited field,
        // a shared manifest key, or an identity/routing key, when the kind is
        // not Float/Integer/Text, or when the count exceeds the cap.
        public bool RegisterAssetType(
            AssetSchemaRegistration registration,
            IReadOnlyList<CustomFieldRegistration> customFields)
        {
            if (customFields == null) throw new ArgumentNullException(nameof(customFields));
            if (string.IsNullOrWhiteSpace(registration.AssetType) ||
                string.IsNullOrWhiteSpace(registration.DisplayName) ||
                string.IsNullOrWhiteSpace(registration.BaseAssetType) ||
                assetsByType.ContainsKey(registration.AssetType)) return false;

            SchemaDefinition baseDefinition;
            if (!assetsByType.TryGetValue(registration.BaseAssetType, out baseDefinition)) return false;

            var fields = new List<SchemaField>(baseDefinition.Fields);
            if (customFields.Count > Config.Schema.MaxCustomFieldsPerType) return false;
            if (customFields.Count > Config.Common.EmptyCount)
            {
                var claimedNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (SchemaField inherited in baseDefinition.Fields)
                    claimedNames.Add(inherited.FieldName);
                foreach (CustomFieldRegistration custom in customFields)
                {
                    if (!IsValidCustomField(custom, claimedNames)) return false;
                    claimedNames.Add(custom.FieldName);
                    fields.Add(new SchemaField(custom.FieldName, custom.ValueKind));
                }
            }

            AddDefinition(new SchemaDefinition(
                registration.AssetType,
                registration.DisplayName,
                registration.BaseAssetType,
                baseDefinition.IsDirectional,
                fields));
            return true;
        }

        // A custom field may not shadow anything the wire format already
        // owns: inherited designer fields, shared manifest slots, or the
        // identity/routing keys - shadowing would either double-map a block
        // slot on import or hijack identity resolution.
        private static bool IsValidCustomField(
            CustomFieldRegistration custom,
            HashSet<string> claimedNames)
        {
            if (string.IsNullOrWhiteSpace(custom.FieldName) ||
                custom.FieldName.Length > Config.Schema.MaxCustomFieldNameLength ||
                claimedNames.Contains(custom.FieldName))
                return false;
            if (custom.ValueKind != SchemaValueKind.Float &&
                custom.ValueKind != SchemaValueKind.Integer &&
                custom.ValueKind != SchemaValueKind.Text)
                return false;
            foreach (EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.SchemaFieldMapping mapping in
                EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.AssetSchemaFieldManifest)
            {
                if (string.Equals(mapping.FieldName, custom.FieldName, StringComparison.Ordinal))
                    return false;
            }
            foreach (string reserved in
                EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.NonSchemaPropertyFields)
            {
                if (string.Equals(reserved, custom.FieldName, StringComparison.Ordinal))
                    return false;
            }
            return !string.Equals(
                EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.AssetTypeField,
                custom.FieldName,
                StringComparison.Ordinal);
        }

        public int RegisterManifest(IEnumerable<AssetSchemaRegistration> registrations)
        {
            if (registrations == null) throw new ArgumentNullException(nameof(registrations));

            int registered = Config.Common.EmptyCount;
            foreach (var registration in registrations)
            {
                if (RegisterAssetType(registration)) registered++;
            }
            return registered;
        }

        // Inverse of ResolveValueKind for runtime-registered custom fields.
        internal static string ResolveFieldType(SchemaValueKind valueKind)
        {
            switch (valueKind)
            {
                case SchemaValueKind.Float: return Config.Types.FloatSingle;
                case SchemaValueKind.Integer: return Config.Types.Int32;
                case SchemaValueKind.Text: return Config.Types.StringUpper;
                default: return null;
            }
        }

        internal static SchemaValueKind ResolveValueKind(string fieldType)
        {
            if (fieldType == Config.Types.FloatSingle || fieldType == Config.Types.FloatLower)
                return SchemaValueKind.Float;
            if (fieldType == Config.Types.Int32 || fieldType == Config.Types.IntLower)
                return SchemaValueKind.Integer;
            if (fieldType == Config.Types.StringUpper || fieldType == Config.Types.StringLower)
                return SchemaValueKind.Text;
            return SchemaValueKind.Unknown;
        }

        private void LoadBuiltIns()
        {
            foreach (var asset in Config.Schema.AssetDefinitions)
            {
                Config.Schema.FieldDefinition[] inheritedFields = asset.UsesLivingEntityFields
                    ? Config.Schema.LivingEntityFields
                    : Config.Schema.GameAssetFields;
                var fields = new List<SchemaField>(
                    inheritedFields.Length +
                    (asset.IncludesEnemyFields ? Config.Schema.AdditionalEnemyFieldCount : Config.Common.EmptyCount) +
                    (asset.IncludesBossFields ? Config.Schema.AdditionalBossFieldCount : Config.Common.EmptyCount));

                foreach (var field in inheritedFields)
                    fields.Add(new SchemaField(field));

                if (asset.IncludesEnemyFields)
                {
                    foreach (var field in Config.Schema.EnemyFields)
                        fields.Add(new SchemaField(field));
                }

                if (asset.IncludesBossFields)
                {
                    foreach (var field in Config.Schema.BossFields)
                        fields.Add(new SchemaField(field));
                }

                AddDefinition(new SchemaDefinition(
                    asset.AssetType,
                    asset.DisplayName,
                    asset.AssetType,
                    asset.IsDirectional,
                    fields));
            }

            foreach (var registration in Config.Schema.BuiltInAssetRegistrations)
            {
                RegisterAssetType(new AssetSchemaRegistration(
                    registration.AssetType,
                    registration.DisplayName,
                    registration.BaseAssetType));
            }
        }

        private void AddDefinition(SchemaDefinition definition)
        {
            assets.Add(definition);
            assetsByType.Add(definition.AssetType, definition);
        }
    }
}
