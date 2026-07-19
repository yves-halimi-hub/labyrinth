using UnityEngine;

namespace EFYV.Core.Data
{
    public abstract class SchemaBackedAssetData : ScriptableObject
    {
        private const int SchemaBlockByteCount = EFYVBackend.Core.Data.FastSchemaBlock.MaxSize * sizeof(int);

        [SerializeField, HideInInspector]
        private int[] schemaBlockData = new int[EFYVBackend.Core.Data.FastSchemaBlock.MaxSize];

        // Item #6: sub-element attachment records from the .efyvlaby
        // "attachments" array. Stored on the schema-backed BASE so every
        // designable asset (entities and plain game assets alike) keeps them
        // through one importer call; null/empty when the document had none.
        // The attachment pixels are already flattened into the imported
        // atlas - dynamic rendering from these records is deferred.
        [SerializeField, HideInInspector]
        private EntityAttachmentRecord[] importedAttachments;

        public EntityAttachmentRecord[] ImportedAttachments => importedAttachments;

        public void SetImportedAttachments(EntityAttachmentRecord[] records)
        {
            importedAttachments = records;
        }

        // Item #33b: runtime-extensible asset fields. Properties keys the
        // compiled schema manifest does NOT know (custom fields registered at
        // runtime through the designer's AssetSchemaService) are parked here
        // by the importer as parallel key/value string arrays - the values
        // keep their raw JSON text (strings unquoted) so custom-registered
        // consumers can read them through the string-keyed accessors without
        // the importer guessing types. Null/empty means the document carried
        // no unknown keys (the importer clears stale entries on reimport).
        [SerializeField, HideInInspector]
        private string[] customPropertyKeys;

        [SerializeField, HideInInspector]
        private string[] customPropertyValues;

        public int CustomPropertyCount =>
            customPropertyKeys == null
                ? EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.EmptyCount
                : customPropertyKeys.Length;

        public void SetCustomProperties(string[] keys, string[] values)
        {
            if (keys == null || values == null)
            {
                customPropertyKeys = null;
                customPropertyValues = null;
                return;
            }
            if (keys.Length != values.Length)
                throw new System.ArgumentException(nameof(values));
            customPropertyKeys = keys;
            customPropertyValues = values;
        }

        public bool TryGetCustomProperty(string key, out string value)
        {
            value = null;
            if (customPropertyKeys == null || customPropertyValues == null || key == null)
                return false;
            for (int index = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FirstIndex;
                index < customPropertyKeys.Length;
                index++)
            {
                if (!string.Equals(customPropertyKeys[index], key, System.StringComparison.Ordinal))
                    continue;
                value = index < customPropertyValues.Length ? customPropertyValues[index] : null;
                return value != null;
            }
            return false;
        }

        public bool TryGetCustomFloat(string key, out float value)
        {
            value = default;
            string text;
            return TryGetCustomProperty(key, out text) &&
                float.TryParse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value);
        }

        public bool TryGetCustomInt(string key, out int value)
        {
            value = default;
            string text;
            return TryGetCustomProperty(key, out text) &&
                int.TryParse(
                    text,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value);
        }

        public EFYVBackend.Core.Data.FastSchemaBlock GetSchemaBlock()
        {
            var block = new EFYVBackend.Core.Data.FastSchemaBlock();
            if (schemaBlockData == null || schemaBlockData.Length != EFYVBackend.Core.Data.FastSchemaBlock.MaxSize)
            {
                return block;
            }

            unsafe
            {
                fixed (int* source = schemaBlockData)
                {
                    System.Buffer.MemoryCopy(
                        source,
                        block.Raw,
                        SchemaBlockByteCount,
                        SchemaBlockByteCount);
                }
            }

            return block;
        }

        public void SetSchemaBlock(EFYVBackend.Core.Data.FastSchemaBlock block)
        {
            if (schemaBlockData == null || schemaBlockData.Length != EFYVBackend.Core.Data.FastSchemaBlock.MaxSize)
            {
                schemaBlockData = new int[EFYVBackend.Core.Data.FastSchemaBlock.MaxSize];
            }

            unsafe
            {
                fixed (int* destination = schemaBlockData)
                {
                    System.Buffer.MemoryCopy(
                        block.Raw,
                        destination,
                        SchemaBlockByteCount,
                        SchemaBlockByteCount);
                }
            }
        }
    }
}
