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

        internal PreviewStateSnapshot(
            PreviewPlaybackState state,
            int animationIndex,
            int frameIndex,
            int frameCount,
            int fps,
            bool isLooping)
        {
            State = state;
            AnimationIndex = animationIndex;
            FrameIndex = frameIndex;
            FrameCount = frameCount;
            FPS = fps;
            IsLooping = isLooping;
        }
    }

    public sealed class PreviewController
    {
        private ProjectSnapshot project;
        private AnimationSnapshot animation;
        private int animationIndex = Config.Common.NotFoundIndex;
        private int frameIndex = Config.Common.FirstIndex;
        private decimal accumulatedFrames;
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
            accumulatedFrames = decimal.Zero;
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
            if (animation == null) return;
            frameIndex = Config.Common.FirstIndex;
            accumulatedFrames = decimal.Zero;
            state = PreviewPlaybackState.Stopped;
            PublishState();
        }

        public void SeekFrame(int index)
        {
            if (animation == null || index < Config.Common.FirstIndex || index >= animation.Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            frameIndex = index;
            accumulatedFrames = decimal.Zero;
            PublishState();
        }

        public bool Tick(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (state != PreviewPlaybackState.Playing || animation == null || animation.FPS <= Config.Common.EmptyCount)
                return false;

            accumulatedFrames += ((decimal)elapsed.Ticks * animation.FPS) / TimeSpan.TicksPerSecond;
            decimal framesToAdvance = decimal.Floor(accumulatedFrames);
            if (framesToAdvance < decimal.One) return false;

            accumulatedFrames -= framesToAdvance;
            if (IsLooping)
            {
                int advance = (int)(framesToAdvance % animation.Frames.Count);
                frameIndex = (frameIndex + advance) % animation.Frames.Count;
            }
            else if (framesToAdvance >= animation.Frames.Count - frameIndex)
            {
                frameIndex = animation.Frames.Count - 1;
                state = PreviewPlaybackState.Paused;
                accumulatedFrames = decimal.Zero;
            }
            else
            {
                frameIndex += (int)framesToAdvance;
            }

            PublishState();
            return true;
        }

        public void CopyCurrentPixelsTo(PixelColor[] destination)
        {
            if (animation == null || state == PreviewPlaybackState.Empty)
                throw new InvalidOperationException();
            animation.Frames[frameIndex].CopyPixelsTo(destination);
        }

        private PreviewStateSnapshot CreateStateSnapshot()
        {
            return new PreviewStateSnapshot(
                state,
                animationIndex,
                frameIndex,
                animation?.Frames.Count ?? Config.Common.EmptyCount,
                animation?.FPS ?? Config.Common.EmptyCount,
                IsLooping);
        }

        private void PublishState()
        {
            StateChanged?.Invoke(CreateStateSnapshot());
        }
    }
}
