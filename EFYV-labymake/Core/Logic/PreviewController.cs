using System;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public enum PreviewPlaybackState
    {
        Empty,
        Stopped,
        Paused,
        Playing
    }

    public readonly struct PreviewStateSnapshot
    {
        public PreviewPlaybackState State { get; }
        public int AnimationIndex { get; }
        public int FrameIndex { get; }
        public int FrameCount { get; }
        public int FPS { get; }
        public bool IsLooping { get; }
        // Item #10 playback tags of the loaded animation (effective, clamped
        // to the frame count) plus the current frame's RAW duration override
        // (0 = inherits FPS).
        public bool PingPong { get; }
        public int LoopStartFrame { get; }
        public int LoopEndFrame { get; }
        public int CurrentFrameDurationMs { get; }

        internal PreviewStateSnapshot(
            PreviewPlaybackState state,
            int animationIndex,
            int frameIndex,
            int frameCount,
            int fps,
            bool isLooping,
            bool pingPong,
            int loopStartFrame,
            int loopEndFrame,
            int currentFrameDurationMs)
        {
            State = state;
            AnimationIndex = animationIndex;
            FrameIndex = frameIndex;
            FrameCount = frameCount;
            FPS = fps;
            IsLooping = isLooping;
            PingPong = pingPong;
            LoopStartFrame = loopStartFrame;
            LoopEndFrame = loopEndFrame;
            CurrentFrameDurationMs = currentFrameDurationMs;
        }
    }

    // Plays immutable ProjectSnapshot animations with exact time accumulation.
    //
    // Timing model (item #10): every frame has a duration — its DurationMs
    // override when positive, otherwise one FPS interval. To keep the
    // arithmetic EXACT in decimal (no repeating fractions from 1/fps), the
    // accumulator holds elapsed time in "tick × fps" units: one inherited
    // frame costs exactly TimeSpan.TicksPerSecond scaled units and an
    // overridden frame costs durationMs × TicksPerMillisecond × fps.
    //
    // Playback tags: when looping, the playhead cycles the animation's
    // effective loop range [EffectiveLoopStart .. EffectiveLoopEnd]; frames
    // before the range play once as an intro. PingPong bounces direction at
    // the range ends (endpoints are visited once per bounce, matching the
    // classic pixel-art convention). Non-looping playback ignores the tags:
    // it plays 0..N-1 forward once and pauses on the last frame.
    //
    // Arbitrarily large elapsed times stay O(frameCount): once the playhead
    // is inside the steady loop cycle the remaining time is reduced modulo
    // one full cycle (which is state-preserving), never stepped frame by
    // frame.
    public sealed class PreviewController
    {
        private const int ForwardDirection = 1;

        private ProjectSnapshot project;
        private AnimationSnapshot animation;
        private int animationIndex = Config.Common.NotFoundIndex;
        private int frameIndex = Config.Common.FirstIndex;
        private decimal accumulatedScaledTicks;
        private int playbackDirection = ForwardDirection;
        private PreviewPlaybackState state = PreviewPlaybackState.Empty;

        public event Action<PreviewStateSnapshot> StateChanged;

        public bool IsLooping { get; set; } = Config.Animation.DefaultPreviewLoop;
        public PreviewStateSnapshot Current => CreateStateSnapshot();

        public void Load(ProjectSnapshot snapshot, int selectedAnimationIndex)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (selectedAnimationIndex < Config.Common.FirstIndex ||
                selectedAnimationIndex >= snapshot.Animations.Count)
                throw new ArgumentOutOfRangeException(nameof(selectedAnimationIndex));

            project = snapshot;
            animationIndex = selectedAnimationIndex;
            animation = project.Animations[animationIndex];
            frameIndex = Config.Common.FirstIndex;
            accumulatedScaledTicks = decimal.Zero;
            playbackDirection = ForwardDirection;
            state = animation.Frames.Count == Config.Common.EmptyCount
                ? PreviewPlaybackState.Empty
                : PreviewPlaybackState.Stopped;
            PublishState();
        }

        public void Play()
        {
            if (animation == null || animation.Frames.Count == Config.Common.EmptyCount) return;
            state = PreviewPlaybackState.Playing;
            PublishState();
        }

        public void Pause()
        {
            if (state != PreviewPlaybackState.Playing) return;
            state = PreviewPlaybackState.Paused;
            PublishState();
        }

        public void Stop()
        {
            // A zero-frame animation stays Empty: leaving that state would bypass the
            // Empty guard in CopyCurrentPixelsTo and turn its intended
            // InvalidOperationException into an out-of-range frame lookup.
            if (animation == null || animation.Frames.Count == Config.Common.EmptyCount) return;
            frameIndex = Config.Common.FirstIndex;
            accumulatedScaledTicks = decimal.Zero;
            playbackDirection = ForwardDirection;
            state = PreviewPlaybackState.Stopped;
            PublishState();
        }

        public void SeekFrame(int index)
        {
            if (animation == null || index < Config.Common.FirstIndex || index >= animation.Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            frameIndex = index;
            accumulatedScaledTicks = decimal.Zero;
            playbackDirection = ForwardDirection;
            PublishState();
        }

        public bool Tick(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (state != PreviewPlaybackState.Playing || animation == null || animation.FPS <= Config.Common.EmptyCount)
                return false;

            accumulatedScaledTicks += (decimal)elapsed.Ticks * animation.FPS;

            bool advanced = false;
            bool reduced = false;
            int loopStart = animation.EffectiveLoopStart;
            int loopEnd = animation.EffectiveLoopEnd;
            bool pingPong = animation.PingPong;
            int lastFrame = animation.Frames.Count - Config.Common.UnitCount;

            while (true)
            {
                decimal frameCost = GetScaledFrameCost(frameIndex);

                // State-preserving modulo reduction: advancing a whole loop
                // cycle returns the playhead to the exact same (frame,
                // direction) state, so any surplus whole cycles are consumed
                // in O(1) once the playhead sits inside the loop range.
                if (!reduced && IsLooping && frameIndex >= loopStart && frameIndex <= loopEnd)
                {
                    reduced = true;
                    decimal cycleCost = GetScaledCycleCost(loopStart, loopEnd, pingPong);
                    if (cycleCost > decimal.Zero && accumulatedScaledTicks >= cycleCost)
                    {
                        decimal wholeCycles = decimal.Floor(accumulatedScaledTicks / cycleCost);
                        accumulatedScaledTicks -= wholeCycles * cycleCost;
                        advanced = true;
                    }
                }

                if (accumulatedScaledTicks < frameCost) break;

                if (IsLooping)
                {
                    accumulatedScaledTicks -= frameCost;
                    StepLoopingFrame(loopStart, loopEnd, pingPong, lastFrame);
                    advanced = true;
                }
                else if (frameIndex >= lastFrame)
                {
                    // Crossing the final frame boundary one-shot: clamp, pause,
                    // and drop the residual (matching the historical contract).
                    frameIndex = lastFrame;
                    state = PreviewPlaybackState.Paused;
                    accumulatedScaledTicks = decimal.Zero;
                    advanced = true;
                    break;
                }
                else
                {
                    accumulatedScaledTicks -= frameCost;
                    frameIndex++;
                    advanced = true;
                }
            }

            if (advanced) PublishState();
            return advanced;
        }

        public void CopyCurrentPixelsTo(PixelColor[] destination)
        {
            if (animation == null || state == PreviewPlaybackState.Empty)
                throw new InvalidOperationException();
            animation.Frames[frameIndex].CopyPixelsTo(destination);
        }

        // One frame-step of looping playback. Non-ping-pong cycles forward
        // through [loopStart .. loopEnd] (a stale index past the range walks
        // forward and wraps from the last frame). Ping-pong bounces direction
        // at the range ends; frames outside the range walk toward it.
        private void StepLoopingFrame(int loopStart, int loopEnd, bool pingPong, int lastFrame)
        {
            if (!pingPong)
            {
                frameIndex = frameIndex == loopEnd || frameIndex >= lastFrame
                    ? loopStart
                    : frameIndex + Config.Common.UnitCount;
                return;
            }

            if (loopStart == loopEnd)
            {
                if (frameIndex > loopStart) frameIndex--;
                else if (frameIndex < loopStart) frameIndex++;
                // frameIndex == loopStart holds (a one-frame loop consumes its
                // duration without moving).
                return;
            }

            int next = frameIndex + playbackDirection;
            if (next > loopEnd)
            {
                playbackDirection = -ForwardDirection;
                next = frameIndex - Config.Common.UnitCount;
                if (next < loopStart) next = loopStart;
            }
            else if (next < loopStart)
            {
                playbackDirection = ForwardDirection;
                next = frameIndex + Config.Common.UnitCount;
                if (next > loopEnd) next = loopEnd;
            }
            frameIndex = next;
        }

        // Scaled cost (tick × fps units) of one full loop cycle from any
        // in-range state back to itself. Non-ping-pong visits each range frame
        // once; ping-pong visits the endpoints once and every middle frame
        // twice per period.
        private decimal GetScaledCycleCost(int loopStart, int loopEnd, bool pingPong)
        {
            decimal cost = decimal.Zero;
            for (int index = loopStart; index <= loopEnd; index++)
            {
                decimal frameCost = GetScaledFrameCost(index);
                cost += frameCost;
                if (pingPong && index > loopStart && index < loopEnd) cost += frameCost;
            }
            return cost;
        }

        // Exact scaled duration of one frame: an inherited frame costs one FPS
        // interval (ticksPerSecond/fps ticks → exactly TicksPerSecond scaled),
        // an overridden frame costs durationMs milliseconds (exact integer in
        // scaled units as well).
        private decimal GetScaledFrameCost(int index)
        {
            int overrideMs = animation.Frames[index].DurationMs;
            return overrideMs == Config.Animation.InheritFrameDurationMs
                ? TimeSpan.TicksPerSecond
                : (decimal)overrideMs * TimeSpan.TicksPerMillisecond * animation.FPS;
        }

        private PreviewStateSnapshot CreateStateSnapshot()
        {
            int currentDurationMs = Config.Animation.InheritFrameDurationMs;
            if (animation != null &&
                frameIndex >= Config.Common.FirstIndex &&
                frameIndex < animation.Frames.Count)
                currentDurationMs = animation.Frames[frameIndex].DurationMs;

            return new PreviewStateSnapshot(
                state,
                animationIndex,
                frameIndex,
                animation?.Frames.Count ?? Config.Common.EmptyCount,
                animation?.FPS ?? Config.Common.EmptyCount,
                IsLooping,
                animation?.PingPong ?? Config.Animation.DefaultPingPong,
                animation?.EffectiveLoopStart ?? Config.Common.FirstIndex,
                animation?.EffectiveLoopEnd ?? Config.Common.FirstIndex,
                currentDurationMs);
        }

        private void PublishState()
        {
            StateChanged?.Invoke(CreateStateSnapshot());
        }
    }
}
