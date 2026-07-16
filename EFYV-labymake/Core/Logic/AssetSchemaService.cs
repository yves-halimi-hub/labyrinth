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
            if (string.IsNullOrWhiteSpace(registration.AssetType) ||
                string.IsNullOrWhiteSpace(registration.DisplayName) ||
                string.IsNullOrWhiteSpace(registration.BaseAssetType) ||
                assetsByType.ContainsKey(registration.AssetType)) return false;

            SchemaDefinition baseDefinition;
            if (!assetsByType.TryGetValue(registration.BaseAssetType, out baseDefinition)) return false;

            AddDefinition(new SchemaDefinition(
                registration.AssetType,
                registration.DisplayName,
                registration.BaseAssetType,
                baseDefinition.IsDirectional,
                new List<SchemaField>(baseDefinition.Fields)));
            return true;
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
