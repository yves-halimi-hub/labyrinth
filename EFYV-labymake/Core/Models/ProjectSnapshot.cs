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

    // Item #6: immutable copy of one per-frame sub-element attachment.
    public readonly struct AttachmentSnapshot
    {
        public string SubElementName { get; }
        public int X { get; }
        public int Y { get; }
        public int ZOrder { get; }
        public bool FlipX { get; }
        public bool FlipY { get; }

        internal AttachmentSnapshot(SubElementAttachment attachment)
        {
            SubElementName = attachment.SubElementName;
            X = attachment.X;
            Y = attachment.Y;
            ZOrder = attachment.ZOrder;
            FlipX = attachment.FlipX;
            FlipY = attachment.FlipY;
        }
    }

    public sealed class FrameSnapshot
    {
        private readonly PixelColor[] pixels;

        public int Width { get; }
        public int Height { get; }
        // Raw per-frame duration override in milliseconds; 0 inherits the
        // owning animation's FPS (item #10).
        public int DurationMs { get; }
        public IReadOnlyList<HitboxSnapshot> Hitboxes { get; }
        // Item #6 attachment records in authored list order (null model
        // entries are skipped - the validator reports them; a snapshot only
        // carries well-formed records).
        public IReadOnlyList<AttachmentSnapshot> Attachments { get; }
        public int PixelCount => pixels.Length;
        internal PixelColor[] Pixels => pixels;

        internal FrameSnapshot(Frame frame)
        {
            Width = frame.Width;
            Height = frame.Height;
            DurationMs = frame.DurationMs;
            pixels = frame.FlattenLayers();

            var hitboxes = new List<HitboxSnapshot>(frame.Hitboxes.Count);
            foreach (var pair in frame.Hitboxes)
                hitboxes.Add(new HitboxSnapshot(pair.Key, pair.Value));
            Hitboxes = new ReadOnlyCollection<HitboxSnapshot>(hitboxes);

            var attachments = new List<AttachmentSnapshot>(frame.Attachments.Count);
            foreach (var attachment in frame.Attachments)
            {
                if (attachment != null) attachments.Add(new AttachmentSnapshot(attachment));
            }
            Attachments = new ReadOnlyCollection<AttachmentSnapshot>(attachments);
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
        // Item #10 playback tags, captured raw (possibly stale relative to the
        // frame count); consumers clamp — see EffectiveLoopStart/EffectiveLoopEnd.
        public int LoopStartFrame { get; }
        public int LoopEndFrame { get; }
        public bool PingPong { get; }
        public IReadOnlyList<FrameSnapshot> Frames { get; }
        // Item #7 authored effect descriptors (immutable instances, so the
        // snapshot may share them with the live model).
        public IReadOnlyList<EffectDescriptor> Effects { get; }

        // Clamped loop range against the actual frame count: LoopEndFrame of
        // FullRangeLoopEnd (-1) or past the end resolves to the last frame,
        // and the start can never sit past the end.
        public int EffectiveLoopStart
        {
            get
            {
                if (Frames.Count == Config.Common.EmptyCount) return Config.Common.FirstIndex;
                int lastFrame = Frames.Count - Config.Common.UnitCount;
                return LoopStartFrame < Config.Common.FirstIndex
                    ? Config.Common.FirstIndex
                    : (LoopStartFrame > lastFrame ? lastFrame : LoopStartFrame);
            }
        }

        public int EffectiveLoopEnd
        {
            get
            {
                if (Frames.Count == Config.Common.EmptyCount) return Config.Common.FirstIndex;
                int lastFrame = Frames.Count - Config.Common.UnitCount;
                if (LoopEndFrame == Config.Animation.FullRangeLoopEnd || LoopEndFrame > lastFrame)
                    return lastFrame;
                int start = EffectiveLoopStart;
                return LoopEndFrame < start ? start : LoopEndFrame;
            }
        }

        internal AnimationSnapshot(AnimationState animation, int startFrame)
        {
            StateName = animation.StateName;
            FPS = animation.FPS;
            StartFrame = startFrame;
            LoopStartFrame = animation.LoopStartFrame;
            LoopEndFrame = animation.LoopEndFrame;
            PingPong = animation.PingPong;
            Effects = new ReadOnlyCollection<EffectDescriptor>(
                new List<EffectDescriptor>(animation.Effects));

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

        private ProjectSnapshot(
            EFYVProject project,
            IReadOnlyList<AnimationState> capturedAnimations,
            string facingOverride)
        {
            TargetAssetType = project.TargetAssetType;
            UnityProjectPath = project.UnityProjectPath;
            CanvasWidth = project.CanvasWidth;
            CanvasHeight = project.CanvasHeight;
            DesignerSeed = project.DesignerSeed;

            var properties = new Dictionary<string, object>(project.AssetProperties.Count, StringComparer.Ordinal);
            foreach (var pair in project.AssetProperties)
                properties.Add(pair.Key, CapturePropertyValue(pair.Value));
            if (facingOverride != null)
                properties[Config.Entity.KeyFacing] = facingOverride;
            assetProperties = new ReadOnlyDictionary<string, object>(properties);

            var animations = new List<AnimationSnapshot>(capturedAnimations.Count);
            int totalFrames = Config.Common.EmptyCount;
            int totalHitboxes = Config.Common.EmptyCount;
            foreach (var animation in capturedAnimations)
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
            return new ProjectSnapshot(project, project.Animations, null);
        }

        // Item #33: captures ONE facing of a linked directional project - that
        // facing's animation set plus the shared properties with the "facing"
        // key overridden - so the export engine can publish all four facings
        // from one project. Throws for non-directional projects and unknown
        // facing names (via GetFacingAnimations).
        public static ProjectSnapshot CaptureFacing(EFYVProject project, string facing)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            return new ProjectSnapshot(project, project.GetFacingAnimations(facing), facing);
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
