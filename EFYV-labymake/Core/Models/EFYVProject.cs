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
        public int CanvasWidth 
        { 
            get => Data.CanvasWidth; 
            set => Data.CanvasWidth = value; 
        }
        public int CanvasHeight 
        { 
            get => Data.CanvasHeight; 
            set => Data.CanvasHeight = value; 
        }

        public uint DesignerSeed { get; set; }

        // --- Visual Hierarchy ---
        public List<AnimationState> Animations { get; }

        public EFYVProject(string targetAssetType)
        {
            Data = new EFYVBackend.Core.Models.EFYVProjectData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
            Data.TargetAssetType = targetAssetType;
            AssetProperties = new Dictionary<string, object>();
            Animations = new List<AnimationState>();
            Data.CanvasWidth = Config.Canvas.DefaultWidth;
            Data.CanvasHeight = Config.Canvas.DefaultHeight;
            DesignerSeed = Config.Tool.Map.DefaultSeed;
        }
    }
}
