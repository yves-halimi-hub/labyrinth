using System;
using System.Collections.Generic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    // Item #33: linked 4-direction authoring state. A directional project
    // holds ONE animation set per facing (Up/Down/Left/Right) while sharing
    // everything else - canvas size, palettes, asset properties, tileset/map
    // sections. The ACTIVE facing's animations live in EFYVProject.Animations
    // (so every existing tool/command/undo path keeps operating on the list it
    // already knows); the three INACTIVE facings' sets are parked here and
    // swapped in/out by DesignerSession.SwitchFacing. AnimationState objects
    // move BY REFERENCE between the live list and the parked sets - a facing
    // switch never clones frames.
    //
    // Null EFYVProject.Directional means "not a directional project" (the
    // default; legacy documents restore to null).
    public sealed class DirectionalState
    {
        private readonly Dictionary<string, List<AnimationState>> inactiveSets =
            new Dictionary<string, List<AnimationState>>(StringComparer.Ordinal);

        public string ActiveFacing { get; private set; }

        public DirectionalState(string activeFacing)
        {
            if (!IsFacingName(activeFacing)) throw new ArgumentException(nameof(activeFacing));
            ActiveFacing = activeFacing;
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (!string.Equals(facing, activeFacing, StringComparison.Ordinal))
                    inactiveSets[facing] = new List<AnimationState>();
            }
        }

        // True for exactly the four canonical facing names (never for the
        // empty "no facing" marker non-directional projects may carry).
        public static bool IsFacingName(string value)
        {
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (string.Equals(facing, value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // Read access for hosts/validators. The active facing's animations are
        // NOT here - read them from EFYVProject.Animations (or use
        // EFYVProject.GetFacingAnimations, which routes both cases).
        public IReadOnlyList<AnimationState> GetInactiveFacingAnimations(string facing)
        {
            return GetInactiveSet(facing);
        }

        internal List<AnimationState> GetInactiveSet(string facing)
        {
            List<AnimationState> animations;
            if (facing == null || !inactiveSets.TryGetValue(facing, out animations))
                throw new ArgumentException(nameof(facing));
            return animations;
        }

        internal void SetInactiveSet(string facing, List<AnimationState> animations)
        {
            if (animations == null) throw new ArgumentNullException(nameof(animations));
            if (facing == null || !inactiveSets.ContainsKey(facing))
                throw new ArgumentException(nameof(facing));
            inactiveSets[facing] = animations;
        }

        // Moves the live list into parking and the target facing's parked set
        // into the live list. Idempotence under undo/redo replay comes from
        // operating on CURRENT contents only (no captured list snapshots).
        internal void Switch(EFYVProject project, string toFacing)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            List<AnimationState> incoming = GetInactiveSet(toFacing);
            inactiveSets.Remove(toFacing);
            inactiveSets[ActiveFacing] = new List<AnimationState>(project.Animations);
            project.Animations.Clear();
            project.Animations.AddRange(incoming);
            ActiveFacing = toFacing;
        }
    }
}
