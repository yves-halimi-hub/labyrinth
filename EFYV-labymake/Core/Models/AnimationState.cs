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

        // Item #10 playback tags. The raw values are authoring intent, not
        // clamped live: removing frames can leave a stale range, so every
        // consumer (preview, export) clamps to the actual frame count and
        // treats FullRangeLoopEnd (-1) as "the last frame".
        public int LoopStartFrame
        {
            get => Data.LoopStartFrame;
            set
            {
                if (value < Config.Common.FirstIndex) throw new ArgumentOutOfRangeException(nameof(value));
                Data.LoopStartFrame = value;
            }
        }

        public int LoopEndFrame
        {
            get => Data.LoopEndFrame;
            set
            {
                if (value < Config.Animation.FullRangeLoopEnd) throw new ArgumentOutOfRangeException(nameof(value));
                Data.LoopEndFrame = value;
            }
        }

        public bool PingPong
        {
            get => Data.PingPong;
            set => Data.PingPong = value;
        }

        // Item #7 authored runtime-effect descriptors, exported into the
        // .efyvlaby atlas animation block. EffectDescriptor instances are
        // immutable, so clones and snapshots share references; hosts mutate
        // the list through the undoable DesignerSession effect CRUD.
        public List<EffectDescriptor> Effects { get; }

        public AnimationState(string name, int fps = Config.Animation.DefaultFPS)
        {
            if (fps <= Config.Common.EmptyCount) throw new ArgumentOutOfRangeException(nameof(fps));

            Data = new EFYVBackend.Core.Models.AnimationStateData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
            Data.StateName = name;
            Data.FPS = fps;
            Data.LoopStartFrame = Config.Animation.DefaultLoopStartFrame;
            Data.LoopEndFrame = Config.Animation.FullRangeLoopEnd;
            Data.PingPong = Config.Animation.DefaultPingPong;
            Frames = new List<Frame>();
            Effects = new List<EffectDescriptor>();
        }

        public AnimationState Clone()
        {
            var clone = new AnimationState(StateName, FPS);
            clone.LoopStartFrame = LoopStartFrame;
            clone.LoopEndFrame = LoopEndFrame;
            clone.PingPong = PingPong;
            foreach (var frame in Frames) clone.Frames.Add(frame.Clone());
            clone.Effects.AddRange(Effects);
            return clone;
        }
    }
}
