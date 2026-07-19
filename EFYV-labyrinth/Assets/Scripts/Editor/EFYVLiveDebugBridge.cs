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

        // Item #4: the seam the debug spawn palette hooks to auto-offer the
        // newest import. LastRefreshedAsset records the most recently imported
        // or refreshed asset (in edit mode as well as Play Mode, since a spawn
        // candidate can be imported before play starts), and RefreshVersion
        // increments on every refresh so the window can cheaply poll for
        // "something changed" without wiring an event.
        internal static SchemaBackedAssetData LastRefreshedAsset { get; private set; }
        internal static int RefreshVersion { get; private set; }

        public static void QueueRefresh(SchemaBackedAssetData data)
        {
            if (data == null) return;

            // Record the newest import for the spawn palette regardless of play
            // state; the scene-entity refresh below still only runs in Play Mode.
            LastRefreshedAsset = data;
            RefreshVersion++;

            if (!EditorApplication.isPlaying) return;

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
