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

                Frame newFrame = CreateGeneratedFrame(baseFrame, i, out Layer activeLayer);

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

                Frame newFrame = CreateGeneratedFrame(baseFrame, i, out Layer activeLayer);

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

        // Item #10 preset: vertical bob plus optional bottom-anchored breathe
        // squash-and-stretch (one sine cycle; frame 0 is the identity pose).
        public unsafe AnimationState GenerateBobAnimation(
            string animationName,
            Frame baseFrame,
            int frameCount,
            float bobAmp,
            float breatheAmp)
        {
            ValidateBaseFrame(baseFrame, frameCount);

            AnimationState anim = new AnimationState(animationName, Config.Animation.BobDefaultFPS);
            PixelColor[] flatBase = baseFrame.FlattenLayers();

            for (int i = Config.Common.FirstIndex; i < frameCount; i++)
            {
                float timeT = (float)i / frameCount;

                Frame newFrame = CreateGeneratedFrame(baseFrame, i, out Layer activeLayer);

                fixed (PixelColor* srcPtr = flatBase)
                fixed (PixelColor* destPtr = activeLayer.Pixels)
                {
                    FastDeformation.GenerateBobBreatheFrame(
                        (uint*)srcPtr,
                        (uint*)destPtr,
                        baseFrame.Width,
                        baseFrame.Height,
                        timeT,
                        bobAmp,
                        breatheAmp
                    );
                }

                anim.Frames.Add(newFrame);
            }

            return anim;
        }

        // Item #10 preset: decaying horizontal shake plus a white hit-flash
        // that is strongest on frame 0 (the impact) and fades over the cycle.
        public unsafe AnimationState GenerateShakeFlashAnimation(
            string animationName,
            Frame baseFrame,
            int frameCount,
            float shakeAmp,
            float flashStrength)
        {
            ValidateBaseFrame(baseFrame, frameCount);

            AnimationState anim = new AnimationState(animationName, Config.Animation.ShakeDefaultFPS);
            PixelColor[] flatBase = baseFrame.FlattenLayers();

            for (int i = Config.Common.FirstIndex; i < frameCount; i++)
            {
                float timeT = (float)i / frameCount;

                Frame newFrame = CreateGeneratedFrame(baseFrame, i, out Layer activeLayer);

                fixed (PixelColor* srcPtr = flatBase)
                fixed (PixelColor* destPtr = activeLayer.Pixels)
                {
                    FastDeformation.GenerateShakeFlashFrame(
                        (uint*)srcPtr,
                        (uint*)destPtr,
                        baseFrame.Width,
                        baseFrame.Height,
                        timeT,
                        shakeAmp,
                        flashStrength
                    );
                }

                anim.Frames.Add(newFrame);
            }

            return anim;
        }

        // Layer-preserving regeneration (item #10): rebuilds `generated`'s frame
        // list on top of `existing`, replacing ONLY the target layer's pixels in
        // each frame and keeping every other layer (manual touch-ups), the
        // hitboxes, and the per-frame duration of the existing frame. Preserved
        // layers are CLONED so the returned animation shares no mutable state
        // with `existing` — undo of a regenerate swap must restore the old
        // animation bit-exactly even after further edits.
        //
        // Rules per generated frame index i:
        // - existing has a same-sized frame i: keep its layers in order; the
        //   first layer named targetLayerName gets the generated pixels (its
        //   visibility/opacity survive); when no layer carries that name the
        //   generated content is inserted at the BOTTOM of the stack.
        // - otherwise (existing shorter, or dimension mismatch): the generated
        //   frame is used as-is.
        // Frames of `existing` beyond the generated count are dropped — the
        // generator owns the cycle length. Playback tags (loop range/ping-pong)
        // carry over from `existing`; name and FPS come from `generated`.
        public static AnimationState MergeOntoTargetLayer(
            AnimationState existing,
            AnimationState generated,
            string targetLayerName)
        {
            if (existing == null) throw new System.ArgumentNullException(nameof(existing));
            if (generated == null) throw new System.ArgumentNullException(nameof(generated));
            if (string.IsNullOrWhiteSpace(targetLayerName))
                throw new System.ArgumentException(nameof(targetLayerName));

            var merged = new AnimationState(generated.StateName, generated.FPS);
            merged.LoopStartFrame = existing.LoopStartFrame;
            merged.LoopEndFrame = existing.LoopEndFrame;
            merged.PingPong = existing.PingPong;

            for (int index = Config.Common.FirstIndex; index < generated.Frames.Count; index++)
            {
                Frame generatedFrame = generated.Frames[index];
                Frame existingFrame = index < existing.Frames.Count ? existing.Frames[index] : null;
                if (existingFrame == null ||
                    existingFrame.Width != generatedFrame.Width ||
                    existingFrame.Height != generatedFrame.Height)
                {
                    merged.Frames.Add(generatedFrame);
                    continue;
                }

                var frame = new Frame(generatedFrame.Width, generatedFrame.Height, index);
                frame.DurationMs = existingFrame.DurationMs;
                frame.Layers.Clear();
                bool replaced = false;
                foreach (Layer layer in existingFrame.Layers)
                {
                    if (!replaced && layer != null &&
                        string.Equals(layer.Name, targetLayerName, System.StringComparison.Ordinal))
                    {
                        Layer regenerated = generatedFrame.Layers[Config.Tool.DefaultLayerIndex]
                            .Clone(targetLayerName);
                        regenerated.IsVisible = layer.IsVisible;
                        regenerated.Opacity = layer.Opacity;
                        frame.Layers.Add(regenerated);
                        replaced = true;
                        continue;
                    }
                    frame.Layers.Add(layer?.Clone(layer.Name));
                }
                if (!replaced)
                {
                    frame.Layers.Insert(
                        Config.Common.FirstIndex,
                        generatedFrame.Layers[Config.Tool.DefaultLayerIndex].Clone(targetLayerName));
                }
                frame.CopyHitboxesFrom(existingFrame);
                merged.Frames.Add(frame);
            }

            return merged;
        }

        // Every generator emits single-layer frames whose layer carries the
        // designated generated-layer name, so a later regenerate knows exactly
        // which layer it owns (MergeOntoTargetLayer above).
        private static Frame CreateGeneratedFrame(Frame baseFrame, int index, out Layer activeLayer)
        {
            var newFrame = new Frame(baseFrame.Width, baseFrame.Height, index);
            newFrame.CopyHitboxesFrom(baseFrame);
            activeLayer = newFrame.Layers[Config.Tool.DefaultLayerIndex];
            activeLayer.Name = Config.Animation.GeneratedLayerName;
            return newFrame;
        }

        private static void ValidateBaseFrame(Frame baseFrame, int frameCount)
        {
            if (baseFrame == null) throw new System.ArgumentNullException(nameof(baseFrame));
            if (frameCount <= Config.Common.EmptyCount)
                throw new System.ArgumentOutOfRangeException(nameof(frameCount));
        }
    }
}
