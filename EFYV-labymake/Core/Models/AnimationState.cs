using System;
using System.Collections.Generic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    public sealed class AnimationState
    {
        private EFYVBackend.Core.Models.AnimationStateData Data;

        public string StateName 
        { 
            get => Data.StateName; 
            set => Data.StateName = value; 
        }
        
        public int FPS 
        { 
            get => Data.FPS; 
            set => Data.FPS = value; 
        }
        public List<Frame> Frames { get; }

        public AnimationState(string name, int fps = Config.Animation.DefaultFPS)
        {
            if (fps <= Config.Common.EmptyCount) throw new ArgumentOutOfRangeException(nameof(fps));

            Data = new EFYVBackend.Core.Models.AnimationStateData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
            Data.StateName = name;
            Data.FPS = fps;
            Frames = new List<Frame>();
        }

        public AnimationState Clone()
        {
            var clone = new AnimationState(StateName, FPS);
            foreach (var frame in Frames) clone.Frames.Add(frame.Clone());
            return clone;
        }
    }
}
