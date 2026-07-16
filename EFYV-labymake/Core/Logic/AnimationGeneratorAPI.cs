using System.Collections.Generic;
using EFYVLabyMake.Core.Models;
using EFYVBackend.Core.Math;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public sealed class AnimationGeneratorAPI
    {
        // Generates an AnimationState with procedural walk cycle frames from a base frame
        public unsafe AnimationState GenerateWalkAnimation(
            string animationName, 
            Frame baseFrame, 
            int frameCount, 
            int splitY, 
            float bounceAmp, 
            float strideAmp)
        {
            ValidateBaseFrame(baseFrame, frameCount);
            if (splitY < Config.Canvas.MinCoordinate || splitY > baseFrame.Height)
                throw new System.ArgumentOutOfRangeException(nameof(splitY));

            AnimationState anim = new AnimationState(animationName, Config.Animation.WalkDefaultFPS);
            
            // Flatten the base frame so we have a solid static image to deform
            PixelColor[] flatBase = baseFrame.FlattenLayers();

            for (int i = Config.Common.FirstIndex; i < frameCount; i++)
            {
                float timeT = (float)i / frameCount;

                // Create a new frame
                Frame newFrame = new Frame(baseFrame.Width, baseFrame.Height, i);
                newFrame.CopyHitboxesFrom(baseFrame);
                Layer activeLayer = newFrame.Layers[Config.Tool.DefaultLayerIndex];

                fixed (PixelColor* srcPtr = flatBase)
                fixed (PixelColor* destPtr = activeLayer.Pixels)
                {
                    // Pass to the C-Level backend for ultra-fast generation
                    FastDeformation.GenerateWalkFrame(
                        (uint*)srcPtr, 
                        (uint*)destPtr, 
                        baseFrame.Width, 
                        baseFrame.Height, 
                        timeT, 
                        splitY, 
                        bounceAmp, 
                        strideAmp
                    );
                }

                anim.Frames.Add(newFrame);
            }

            return anim;
        }

        // Generates an AnimationState with procedural radial 8-directional jitter
        public unsafe AnimationState GenerateJitterAnimation(
            string animationName,
            Frame baseFrame,
            int frameCount,
            float[] amplitudes, // Must be length 8
            float[] frequencies // Must be length 8
        )
        {
            ValidateBaseFrame(baseFrame, frameCount);
            if (amplitudes == null) throw new System.ArgumentNullException(nameof(amplitudes));
            if (frequencies == null) throw new System.ArgumentNullException(nameof(frequencies));
            if (amplitudes.Length != Config.Tool.Moving.JitterOctantCount)
                throw new System.ArgumentException(nameof(amplitudes));
            if (frequencies.Length != Config.Tool.Moving.JitterOctantCount)
                throw new System.ArgumentException(nameof(frequencies));

            AnimationState anim = new AnimationState(animationName, Config.Animation.JitterDefaultFPS);
            PixelColor[] flatBase = baseFrame.FlattenLayers();

            for (int i = Config.Common.FirstIndex; i < frameCount; i++)
            {
                float timeT = (float)i / frameCount;

                Frame newFrame = new Frame(baseFrame.Width, baseFrame.Height, i);
                newFrame.CopyHitboxesFrom(baseFrame);
                Layer activeLayer = newFrame.Layers[Config.Tool.DefaultLayerIndex];

                fixed (PixelColor* srcPtr = flatBase)
                fixed (PixelColor* destPtr = activeLayer.Pixels)
                fixed (float* ampPtr = amplitudes)
                fixed (float* freqPtr = frequencies)
                {
                    FastDeformation.GenerateJitterFrame(
                        (uint*)srcPtr,
                        (uint*)destPtr,
                        baseFrame.Width,
                        baseFrame.Height,
                        timeT,
                        ampPtr,
                        freqPtr
                    );
                }

                anim.Frames.Add(newFrame);
            }

            return anim;
        }

        private static void ValidateBaseFrame(Frame baseFrame, int frameCount)
        {
            if (baseFrame == null) throw new System.ArgumentNullException(nameof(baseFrame));
            if (frameCount <= Config.Common.EmptyCount)
                throw new System.ArgumentOutOfRangeException(nameof(frameCount));
        }
    }
}
