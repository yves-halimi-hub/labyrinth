using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;

namespace EFYV.Editor
{
    internal static class EFYVLiveDebugBridge
    {
        private static readonly HashSet<SchemaBackedAssetData> PendingAssets = new HashSet<SchemaBackedAssetData>();
        private static bool applyScheduled;

        public static void QueueRefresh(SchemaBackedAssetData data)
        {
            if (!EditorApplication.isPlaying || data == null) return;

            PendingAssets.Add(data);
            if (applyScheduled) return;

            applyScheduled = true;
            EditorApplication.delayCall += ApplyPendingRefreshes;
        }

        private static void ApplyPendingRefreshes()
        {
            applyScheduled = false;
            if (!EditorApplication.isPlaying)
            {
                PendingAssets.Clear();
                return;
            }

            LivingEntity[] entities = Resources.FindObjectsOfTypeAll<LivingEntity>();
            for (int i = 0; i < entities.Length; i++)
            {
                LivingEntity entity = entities[i];
                if (!entity.gameObject.scene.IsValid() || !entity.gameObject.scene.isLoaded) continue;
                if (entity.SourceData != null && PendingAssets.Contains(entity.SourceData))
                {
                    entity.RefreshDataFromAsset();
                }
            }

            PropEntity[] props = Resources.FindObjectsOfTypeAll<PropEntity>();
            for (int i = 0; i < props.Length; i++)
            {
                PropEntity prop = props[i];
                if (!prop.gameObject.scene.IsValid() || !prop.gameObject.scene.isLoaded) continue;
                if (prop.SourceData != null && PendingAssets.Contains(prop.SourceData))
                {
                    prop.RefreshDataFromAsset();
                }
            }

            PendingAssets.Clear();
        }
    }
}
