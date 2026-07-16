using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Logic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    // High-level tool wrapper for Procedural Animation Generation
    // In the future UI, selecting this tool opens the "Movement Gauges" window.
    public sealed class MovingTool : Tool
    {
        private readonly AnimationGeneratorAPI generatorAPI;

        private EFYVBackend.Core.Models.MovingToolData Data = new EFYVBackend.Core.Models.MovingToolData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public enum MovementType
        {
            ToonWalk = Config.Tool.Moving.ModeToonWalk,
            ElementJitter = Config.Tool.Moving.ModeElementJitter
        }
        public MovementType ActiveMode 
        { 
            get => (MovementType)Data.ActiveMode; 
            set => Data.ActiveMode = (int)value; 
        }

        // Walk Controls
        public int WalkSplitY 
        { 
            get => Data.WalkSplitY; 
            set => Data.WalkSplitY = value; 
        }
        public float WalkBounceAmp 
        { 
            get => Data.WalkBounceAmp; 
            set => Data.WalkBounceAmp = value; 
        }
        public float WalkStrideAmp 
        { 
            get => Data.WalkStrideAmp; 
            set => Data.WalkStrideAmp = value; 
        }
        public int WalkFrameCount 
        { 
            get => Data.WalkFrameCount; 
            set => Data.WalkFrameCount = value; 
        }

        // Jitter Controls (8 sides)
        public float GetJitterAmplitude(int index)
        {
            ValidateJitterIndex(index);
            return Data.GetJitterAmplitude(index);
        }

        public void SetJitterAmplitude(int index, float val)
        {
            ValidateJitterIndex(index);
            Data.SetJitterAmplitude(index, val);
        }
        
        public float GetJitterFrequency(int index)
        {
            ValidateJitterIndex(index);
            return Data.GetJitterFrequency(index);
        }

        public void SetJitterFrequency(int index, float val)
        {
            ValidateJitterIndex(index);
            Data.SetJitterFrequency(index, val);
        }
        
        public int JitterFrameCount 
        { 
            get => Data.JitterFrameCount; 
            set => Data.JitterFrameCount = value; 
        }

        public MovingTool()
            : this(new AnimationGeneratorAPI())
        {
        }

        public MovingTool(AnimationGeneratorAPI generator)
        {
            if (generator == null) throw new System.ArgumentNullException(nameof(generator));
            generatorAPI = generator;
            
            // Set defaults in the schema
            ActiveMode = MovementType.ToonWalk;
            WalkSplitY = Config.Tool.Moving.DefaultWalkSplitY;
            WalkBounceAmp = Config.Tool.Moving.DefaultWalkBounceAmp;
            WalkStrideAmp = Config.Tool.Moving.DefaultWalkStrideAmp;
            WalkFrameCount = Config.Tool.Moving.DefaultWalkFrameCount;
            JitterFrameCount = Config.Tool.Moving.DefaultJitterFrameCount;

            // Set some default funky jitter defaults
            for (int i = Config.Common.FirstIndex; i < Config.Tool.Moving.JitterOctantCount; i++)
            {
                SetJitterAmplitude(i, Config.Tool.Moving.DefaultJitterAmp);
                SetJitterFrequency(i, Config.Tool.Moving.DefaultJitterFreq);
            }
        }

        // Called when the user clicks "Generate" or "Apply" in the future UI window
        public AnimationState GenerateAnimation(Frame baseFrame)
        {
            if (baseFrame == null) throw new System.ArgumentNullException(nameof(baseFrame));

            AnimationState generated;
            if (ActiveMode == MovementType.ToonWalk)
            {
                generated = generatorAPI.GenerateWalkAnimation(
                    Config.Animation.WalkAnimName, 
                    baseFrame, 
                    WalkFrameCount, 
                    WalkSplitY, 
                    WalkBounceAmp, 
                    WalkStrideAmp
                );
            }
            else
            {
                float[] amplitudes = new float[Config.Tool.Moving.JitterOctantCount];
                float[] frequencies = new float[Config.Tool.Moving.JitterOctantCount];
                for (int i = Config.Common.FirstIndex; i < Config.Tool.Moving.JitterOctantCount; i++)
                {
                    amplitudes[i] = GetJitterAmplitude(i);
                    frequencies[i] = GetJitterFrequency(i);
                }

                generated = generatorAPI.GenerateJitterAnimation(
                    Config.Animation.JitterAnimName,
                    baseFrame,
                    JitterFrameCount,
                    amplitudes,
                    frequencies
                );
            }

            return generated;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            // For a procedural tool, clicking the canvas might select the SplitY or origin point.
            if (ActiveMode == MovementType.ToonWalk && currentFrame != null)
            {
                WalkSplitY = EFYVBackend.Core.Math.FastMath.FastMax(
                    Config.Canvas.MinCoordinate,
                    EFYVBackend.Core.Math.FastMath.FastMin(y, currentFrame.Height));
            }
        }

        private static void ValidateJitterIndex(int index)
        {
            if (index < Config.Common.FirstIndex || index >= Config.Tool.Moving.JitterOctantCount)
                throw new System.ArgumentOutOfRangeException(nameof(index));
        }
    }
}
