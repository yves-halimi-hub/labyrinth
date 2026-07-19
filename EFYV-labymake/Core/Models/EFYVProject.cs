using System.Collections.Generic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    public sealed class EFYVProject
    {
        private EFYVBackend.Core.Models.EFYVProjectData Data;

        // --- Game Logic Stats (Bridged directly to Unity via Schema) ---
        public string TargetAssetType 
        { 
            get => Data.TargetAssetType; 
            set => Data.TargetAssetType = value; 
        }
        public Dictionary<string, object> AssetProperties { get; }
        
        // --- Export Pathing ---
        public string UnityProjectPath 
        { 
            get => Data.UnityProjectPath; 
            set => Data.UnityProjectPath = value; 
        }

        // --- Art Settings ---
        // The setters are INTERNAL on purpose: assigning a new canvas size never
        // resizes the Layer buffers already allocated inside existing frames, so
        // a public raw setter silently desynced the project from its own pixel
        // data (flatten/validate/export all fault on the mismatch). Hosts resize
        // through DesignerSession.ResizeCanvas, which rebuilds every frame with
        // anchored content as one undoable command. Internal callers (session
        // command, persistence restore, project construction) only assign these
        // when the frame graph is rebuilt or empty.
        public int CanvasWidth
        {
            get => Data.CanvasWidth;
            internal set => Data.CanvasWidth = value;
        }
        public int CanvasHeight
        {
            get => Data.CanvasHeight;
            internal set => Data.CanvasHeight = value;
        }

        public uint DesignerSeed { get; set; }

        // --- Visual Hierarchy ---
        public List<AnimationState> Animations { get; }

        // --- Color workflow (item #8) ---
        // Named palettes with ordered swatches plus the recent-colors ring,
        // both persisted in .efyvmake. Hosts mutate palettes through the
        // undoable DesignerSession CRUD surface; the ring is not undoable
        // (color selection history) but is saved with the project.
        public List<Palette> Palettes { get; }
        public RecentColorRing RecentColors { get; }

        // --- Maps + tilesets (item #5) ---
        // Optional sections: null means the project has none (the default -
        // legacy documents restore to null). The setters are INTERNAL like the
        // canvas size: hosts create/remove the sections through the undoable
        // DesignerSession surface; internal callers (session commands and
        // persistence restore) assign them directly.
        public TilesetSection Tileset { get; internal set; }
        public MapSection Map { get; internal set; }

        // --- Linked directional authoring (item #33) ---
        // Null (the default) means a plain single-facing project. Non-null
        // marks a linked 4-direction project: Animations holds the ACTIVE
        // facing's set and Directional parks the other three. The setter is
        // INTERNAL like the sections above: hosts enable the mode through
        // DesignerSession.EnableDirectionalAuthoring (undoable) or create one
        // via ToolbarAPI.CreateNewLinkedDirectionalProject; internal callers
        // (session commands and persistence restore) assign directly.
        public DirectionalState Directional { get; internal set; }
        public bool IsDirectional => Directional != null;

        // Per-facing read access: the active facing routes to Animations, the
        // other three to the parked sets. Throws for non-directional projects
        // and unknown facing names.
        public IReadOnlyList<AnimationState> GetFacingAnimations(string facing)
        {
            if (Directional == null) throw new System.InvalidOperationException();
            return string.Equals(facing, Directional.ActiveFacing, System.StringComparison.Ordinal)
                ? Animations
                : Directional.GetInactiveFacingAnimations(facing);
        }

        // Every animation across every facing (just Animations for plain
        // projects). Whole-project operations that must keep facings
        // consistent (canvas resize, global color swap) iterate this.
        internal IEnumerable<AnimationState> EnumerateAllAnimations()
        {
            foreach (AnimationState animation in Animations) yield return animation;
            if (Directional == null) yield break;
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (string.Equals(facing, Directional.ActiveFacing, System.StringComparison.Ordinal))
                    continue;
                foreach (AnimationState animation in Directional.GetInactiveFacingAnimations(facing))
                    yield return animation;
            }
        }

        public EFYVProject(string targetAssetType)
        {
            Data = new EFYVBackend.Core.Models.EFYVProjectData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
            Data.TargetAssetType = targetAssetType;
            AssetProperties = new Dictionary<string, object>();
            Animations = new List<AnimationState>();
            Palettes = new List<Palette>();
            RecentColors = new RecentColorRing();
            Data.CanvasWidth = Config.Canvas.DefaultWidth;
            Data.CanvasHeight = Config.Canvas.DefaultHeight;
            DesignerSeed = Config.Tool.Map.DefaultSeed;
        }
    }
}
