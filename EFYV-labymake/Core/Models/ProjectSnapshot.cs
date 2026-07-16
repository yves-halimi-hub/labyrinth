using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    public readonly struct HitboxSnapshot
    {
        public string Key { get; }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        internal HitboxSnapshot(string key, EFYVBackend.Core.Models.HitboxData value)
        {
            Key = key;
            X = value.X;
            Y = value.Y;
            Width = value.Width;
            Height = value.Height;
        }
    }

    public sealed class FrameSnapshot
    {
        private readonly PixelColor[] pixels;

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<HitboxSnapshot> Hitboxes { get; }
        public int PixelCount => pixels.Length;
        internal PixelColor[] Pixels => pixels;

        internal FrameSnapshot(Frame frame)
        {
            Width = frame.Width;
            Height = frame.Height;
            pixels = frame.FlattenLayers();

            var hitboxes = new List<HitboxSnapshot>(frame.Hitboxes.Count);
            foreach (var pair in frame.Hitboxes)
                hitboxes.Add(new HitboxSnapshot(pair.Key, pair.Value));
            Hitboxes = new ReadOnlyCollection<HitboxSnapshot>(hitboxes);
        }

        public void CopyPixelsTo(PixelColor[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination.Length != pixels.Length) throw new ArgumentException(nameof(destination));
            Array.Copy(pixels, destination, pixels.Length);
        }

    }

    public sealed class AnimationSnapshot
    {
        public string StateName { get; }
        public int FPS { get; }
        public int StartFrame { get; }
        public IReadOnlyList<FrameSnapshot> Frames { get; }

        internal AnimationSnapshot(AnimationState animation, int startFrame)
        {
            StateName = animation.StateName;
            FPS = animation.FPS;
            StartFrame = startFrame;

            var frames = new List<FrameSnapshot>(animation.Frames.Count);
            foreach (var frame in animation.Frames) frames.Add(new FrameSnapshot(frame));
            Frames = new ReadOnlyCollection<FrameSnapshot>(frames);
        }
    }

    public sealed class ProjectSnapshot
    {
        private readonly ReadOnlyDictionary<string, object> assetProperties;

        public string TargetAssetType { get; }
        public string UnityProjectPath { get; }
        public int CanvasWidth { get; }
        public int CanvasHeight { get; }
        public uint DesignerSeed { get; }
        public IReadOnlyDictionary<string, object> AssetProperties => assetProperties;
        public IReadOnlyList<AnimationSnapshot> Animations { get; }
        public int TotalFrameCount { get; }
        public int TotalHitboxCount { get; }

        private ProjectSnapshot(EFYVProject project)
        {
            TargetAssetType = project.TargetAssetType;
            UnityProjectPath = project.UnityProjectPath;
            CanvasWidth = project.CanvasWidth;
            CanvasHeight = project.CanvasHeight;
            DesignerSeed = project.DesignerSeed;

            var properties = new Dictionary<string, object>(project.AssetProperties.Count, StringComparer.Ordinal);
            foreach (var pair in project.AssetProperties)
                properties.Add(pair.Key, CapturePropertyValue(pair.Value));
            assetProperties = new ReadOnlyDictionary<string, object>(properties);

            var animations = new List<AnimationSnapshot>(project.Animations.Count);
            int totalFrames = Config.Common.EmptyCount;
            int totalHitboxes = Config.Common.EmptyCount;
            foreach (var animation in project.Animations)
            {
                var snapshot = new AnimationSnapshot(animation, totalFrames);
                animations.Add(snapshot);
                totalFrames += snapshot.Frames.Count;
                foreach (var frame in snapshot.Frames) totalHitboxes += frame.Hitboxes.Count;
            }
            Animations = new ReadOnlyCollection<AnimationSnapshot>(animations);
            TotalFrameCount = totalFrames;
            TotalHitboxCount = totalHitboxes;
        }

        public static ProjectSnapshot Capture(EFYVProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            return new ProjectSnapshot(project);
        }

        public Dictionary<string, object> CopyAssetProperties()
        {
            return new Dictionary<string, object>(assetProperties, StringComparer.Ordinal);
        }

        private static object CapturePropertyValue(object value)
        {
            if (value == null || value is string || value.GetType().IsValueType)
            {
                if (value is JsonElement) return ((JsonElement)value).Clone();
                return value;
            }
            return value.ToString();
        }
    }
}
