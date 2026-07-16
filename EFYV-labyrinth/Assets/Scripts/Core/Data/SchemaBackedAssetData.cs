using UnityEngine;

namespace EFYV.Core.Data
{
    public abstract class SchemaBackedAssetData : ScriptableObject
    {
        private const int SchemaBlockByteCount = EFYVBackend.Core.Data.FastSchemaBlock.MaxSize * sizeof(int);

        [SerializeField, HideInInspector]
        private int[] schemaBlockData = new int[EFYVBackend.Core.Data.FastSchemaBlock.MaxSize];

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
