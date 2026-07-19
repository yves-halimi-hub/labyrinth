using System;

namespace EFYVLabyMake.Core.Logic
{
    // Item #27 live fast path: what a session mutation touched, so the live
    // debug loop can tell an edit that changes exported PIXELS (a brush stroke,
    // a color swap, an attachment placement that flattens into the atlas, any
    // structural add/remove/reorder) apart from one that changes only wire
    // METADATA (a hitbox nudge, a property or playback-tag tweak, an effect
    // descriptor). The latter takes the metadata-only publish path - the PNG is
    // never re-packed or re-encoded.
    //
    // The default for an untagged / legacy MarkDirty call is Everything, so the
    // fast path can only ever NARROW behavior on sites that were deliberately
    // tagged as pixel-clean; a forgotten tag falls back to a full publish and is
    // never silently skipped.
    [Flags]
    public enum DesignerDirtyScope
    {
        None = 0,
        // The exported atlas pixels changed (drawing, filters, color swaps,
        // floating-selection anchors, sub-element attachments - attachments
        // flatten into the atlas so they count as pixels).
        Pixels = 1,
        // Only frame hitboxes changed (they ride in the .efyvlaby, not the PNG).
        Hitboxes = 2,
        // Only wire metadata that lives in the .efyvlaby changed: asset
        // properties, palette/recent-color state, animation playback tags
        // (fps/durations/loop/ping-pong), animation names, effect descriptors.
        Properties = 4,
        // The animation/frame/layer topology changed (add/remove/reorder,
        // canvas resize, directional restructure), which can move the atlas
        // layout - treated like a pixel change for publish purposes.
        Structure = 8,
        Everything = Pixels | Hitboxes | Properties | Structure
    }

    public static class DesignerDirtyScopeExtensions
    {
        // The metadata-only publish path is safe only when neither the atlas
        // pixels nor the atlas layout could have changed since the last publish.
        public static bool IsMetadataOnly(this DesignerDirtyScope scope)
        {
            return (scope & (DesignerDirtyScope.Pixels | DesignerDirtyScope.Structure)) ==
                DesignerDirtyScope.None;
        }
    }
}
