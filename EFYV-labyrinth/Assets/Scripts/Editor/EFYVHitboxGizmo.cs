using UnityEditor;
using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

namespace EFYV.Editor
{
    internal static class EFYVHitboxGizmo
    {
        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawImportedHitboxes(LivingEntity entity, GizmoType gizmoType)
        {
            LivingEntityData sourceData = entity.SourceData;
            if (sourceData == null) return;

            EntityHitboxRecord[] hitboxes;
            EntityAtlasMetadata atlas;
            if (sourceData.TryGetImportedFacing(entity.HitboxPreviewFacing, out EntityFacingImportData facingData))
            {
                hitboxes = facingData.Hitboxes;
                atlas = facingData.AtlasMetadata;
            }
            else
            {
                hitboxes = sourceData.Hitboxes;
                atlas = sourceData.AtlasMetadata;
            }
            if (hitboxes == null) return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = entity.transform.localToWorldMatrix;

            for (int i = GameConfig.Runtime.FirstIndex; i < hitboxes.Length; i++)
            {
                EntityHitboxRecord hitbox = hitboxes[i];
                if (hitbox.FrameIndex != entity.HitboxPreviewFrame) continue;
                if (!TryGetLocalBounds(atlas, hitbox.Bounds, out Vector3 center, out Vector3 size)) continue;

                int typeHash = EFYVBackend.Core.Math.FastMath.FastHash(hitbox.HitboxType);
                Gizmos.color = (typeHash & GameConfig.Runtime.ExclusiveUpperBoundOffset) == GameConfig.Runtime.FirstIndex
                    ? Color.cyan
                    : Color.magenta;

                Gizmos.DrawWireCube(center, size);
                Handles.Label(entity.transform.TransformPoint(center), hitbox.HitboxType);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        internal static bool TryGetLocalBounds(
            EntityAtlasMetadata atlas,
            Rect authoredBounds,
            out Vector3 center,
            out Vector3 size)
        {
            center = default;
            size = default;
            if (atlas.FrameWidth <= GameConfig.Runtime.EmptyCollectionCount ||
                atlas.FrameHeight <= GameConfig.Runtime.EmptyCollectionCount ||
                SharedConfig.PixelsPerUnit <= GameConfig.Runtime.EmptyCollectionCount ||
                authoredBounds.width <= GameConfig.Runtime.EmptyCollectionCount ||
                authoredBounds.height <= GameConfig.Runtime.EmptyCollectionCount ||
                !IsFinite(authoredBounds.x) ||
                !IsFinite(authoredBounds.y) ||
                !IsFinite(authoredBounds.width) ||
                !IsFinite(authoredBounds.height))
                return false;

            float frameWidth = atlas.FrameWidth / SharedConfig.PixelsPerUnit;
            float frameHeight = atlas.FrameHeight / SharedConfig.PixelsPerUnit;
            float pivot = GameConfig.Importer.SpritePivotNormalized;
            center = new Vector3(
                authoredBounds.x + (authoredBounds.width * pivot) - (frameWidth * pivot),
                (frameHeight * pivot) - authoredBounds.y - (authoredBounds.height * pivot),
                GameConfig.Runtime.EmptyCollectionCount);
            size = new Vector3(
                authoredBounds.width,
                authoredBounds.height,
                GameConfig.Runtime.EmptyCollectionCount);
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
