using UnityEditor;
using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Editor
{
    internal static class EFYVHitboxGizmo
    {
        // Item #14: an ALWAYS-ON overlay (NonSelected added), not selection-only.
        // In edit mode it previews the designer's HitboxPreviewFrame/Facing; in
        // Play mode it follows the LIVE facing and the current animation frame
        // of the item #13 runtime flipbook.
        [DrawGizmo(GizmoType.Selected | GizmoType.Active | GizmoType.NonSelected)]
        private static void DrawImportedHitboxes(LivingEntity entity, GizmoType gizmoType)
        {
            LivingEntityData sourceData = entity.SourceData;
            if (sourceData == null) return;

            bool followFlipbook = EditorApplication.isPlaying && entity.HasRuntimeFlipbook;
            EFYVBackend.Core.Math.FastMath.FacingDirection previewFacing =
                followFlipbook ? entity.CurrentFacing : entity.HitboxPreviewFacing;
            int previewFrame = followFlipbook ? entity.CurrentFlipbookGlobalFrame : entity.HitboxPreviewFrame;

            EntityHitboxRecord[] hitboxes;
            EntityAtlasMetadata atlas;
            if (sourceData.TryGetImportedFacing(previewFacing, out EntityFacingImportData facingData))
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
                if (hitbox.FrameIndex != previewFrame) continue;
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

        // Item #14: the pixel-to-local-units math now lives in the runtime
        // EntityHitboxGeometry so the gameplay BoxCollider2D sync and this
        // gizmo share ONE implementation; this stays as the editor-facing name.
        internal static bool TryGetLocalBounds(
            EntityAtlasMetadata atlas,
            Rect authoredBounds,
            out Vector3 center,
            out Vector3 size)
        {
            return EntityHitboxGeometry.TryGetLocalBounds(atlas, authoredBounds, out center, out size);
        }
    }
}
