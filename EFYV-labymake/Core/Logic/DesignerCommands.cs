using System;
using System.Collections.Generic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Tools;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    internal sealed class FrameEditCapture
    {
        internal sealed class LayerState
        {
            public int Index;
            public string Name;
            public bool IsVisible;
            public float Opacity;
            public uint[] Pixels;
        }

        public int FrameIndex { get; }
        public List<LayerState> Layers { get; }
        public Dictionary<string, HitboxData> Hitboxes { get; }

        private FrameEditCapture(
            int frameIndex,
            List<LayerState> layers,
            Dictionary<string, HitboxData> hitboxes)
        {
            FrameIndex = frameIndex;
            Layers = layers;
            Hitboxes = hitboxes;
        }

        public static FrameEditCapture Capture(Frame frame, ITool tool)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (tool == null) throw new ArgumentNullException(nameof(tool));

            var layers = new List<LayerState>();
            var layerTool = tool as ILayerTool;
            if (layerTool != null && layerTool.ActiveLayerIndex >= Config.Common.FirstIndex &&
                layerTool.ActiveLayerIndex < frame.Layers.Count)
            {
                Layer layer = frame.Layers[layerTool.ActiveLayerIndex];
                var pixels = new uint[layer.Pixels.Length];
                for (int index = Config.Common.FirstIndex; index < pixels.Length; index++)
                    pixels[index] = layer.Pixels[index].Rgba;
                layers.Add(new LayerState
                {
                    Index = layerTool.ActiveLayerIndex,
                    Name = layer.Name,
                    IsVisible = layer.IsVisible,
                    Opacity = layer.Opacity,
                    Pixels = pixels
                });
            }

            Dictionary<string, HitboxData> hitboxes = null;
            if (tool is HitboxTool)
                hitboxes = new Dictionary<string, HitboxData>(frame.Hitboxes, StringComparer.Ordinal);
            return new FrameEditCapture(frame.FrameIndex, layers, hitboxes);
        }
    }

    internal sealed class DelegateCommand : ISizedCommand
    {
        private readonly Action execute;
        private readonly Action undo;

        public long EstimatedBytes { get; }

        public DelegateCommand(Action execute, Action undo, long estimatedBytes)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
            EstimatedBytes = estimatedBytes < Config.Command.EstimatedCommandOverheadBytes
                ? Config.Command.EstimatedCommandOverheadBytes
                : estimatedBytes;
        }

        public void Execute() => execute();
        public void Undo() => undo();
    }

    internal sealed class FrameEditCommand : ISizedCommand
    {
        private sealed class LayerDiff
        {
            public Layer Target;
            public int[] PixelIndices;
            public uint[] BeforePixels;
            public uint[] AfterPixels;
            public string BeforeName;
            public string AfterName;
            public bool BeforeVisible;
            public bool AfterVisible;
            public float BeforeOpacity;
            public float AfterOpacity;
        }

        private readonly struct HitboxDiff
        {
            public string Key { get; }
            public bool ExistedBefore { get; }
            public bool ExistsAfter { get; }
            public HitboxData Before { get; }
            public HitboxData After { get; }

            public HitboxDiff(
                string key,
                bool existedBefore,
                bool existsAfter,
                HitboxData before,
                HitboxData after)
            {
                Key = key;
                ExistedBefore = existedBefore;
                ExistsAfter = existsAfter;
                Before = before;
                After = after;
            }
        }

        private readonly Frame target;
        private readonly List<LayerDiff> layerDiffs = new List<LayerDiff>();
        private readonly List<HitboxDiff> hitboxDiffs = new List<HitboxDiff>();
        private readonly int beforeFrameIndex;
        private readonly int afterFrameIndex;

        public bool HasChanges { get; }
        public long EstimatedBytes { get; }

        public FrameEditCommand(Frame target, FrameEditCapture before)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            if (before == null) throw new ArgumentNullException(nameof(before));

            beforeFrameIndex = before.FrameIndex;
            afterFrameIndex = target.FrameIndex;
            long estimatedBytes = Config.Command.EstimatedCommandOverheadBytes;

            foreach (var beforeLayer in before.Layers)
            {
                if (beforeLayer.Index < Config.Common.FirstIndex || beforeLayer.Index >= target.Layers.Count)
                    throw new InvalidOperationException();
                Layer afterLayer = target.Layers[beforeLayer.Index];
                if (beforeLayer.Pixels.Length != afterLayer.Pixels.Length) throw new InvalidOperationException();

                var indices = new List<int>();
                var beforePixels = new List<uint>();
                var afterPixels = new List<uint>();
                for (int pixelIndex = Config.Common.FirstIndex;
                    pixelIndex < beforeLayer.Pixels.Length;
                    pixelIndex++)
                {
                    uint beforeRgba = beforeLayer.Pixels[pixelIndex];
                    uint afterRgba = afterLayer.Pixels[pixelIndex].Rgba;
                    if (beforeRgba == afterRgba) continue;
                    indices.Add(pixelIndex);
                    beforePixels.Add(beforeRgba);
                    afterPixels.Add(afterRgba);
                }

                bool metadataChanged =
                    !string.Equals(beforeLayer.Name, afterLayer.Name, StringComparison.Ordinal) ||
                    beforeLayer.IsVisible != afterLayer.IsVisible ||
                    beforeLayer.Opacity != afterLayer.Opacity;
                if (indices.Count == Config.Common.EmptyCount && !metadataChanged) continue;

                layerDiffs.Add(new LayerDiff
                {
                    Target = afterLayer,
                    PixelIndices = indices.ToArray(),
                    BeforePixels = beforePixels.ToArray(),
                    AfterPixels = afterPixels.ToArray(),
                    BeforeName = beforeLayer.Name,
                    AfterName = afterLayer.Name,
                    BeforeVisible = beforeLayer.IsVisible,
                    AfterVisible = afterLayer.IsVisible,
                    BeforeOpacity = beforeLayer.Opacity,
                    AfterOpacity = afterLayer.Opacity
                });
                estimatedBytes += (long)indices.Count * (sizeof(int) + sizeof(uint) + sizeof(uint));
                estimatedBytes += ((beforeLayer.Name?.Length ?? Config.Common.EmptyCount) +
                    (afterLayer.Name?.Length ?? Config.Common.EmptyCount)) * sizeof(char);
            }

            if (before.Hitboxes != null)
            {
                var hitboxKeys = new HashSet<string>(before.Hitboxes.Keys, StringComparer.Ordinal);
                hitboxKeys.UnionWith(target.Hitboxes.Keys);
                foreach (string key in hitboxKeys)
                {
                    HitboxData beforeValue;
                    HitboxData afterValue;
                    bool existedBefore = before.Hitboxes.TryGetValue(key, out beforeValue);
                    bool existsAfter = target.Hitboxes.TryGetValue(key, out afterValue);
                    if (existedBefore == existsAfter &&
                        (!existedBefore || AreEqual(beforeValue, afterValue))) continue;

                    hitboxDiffs.Add(new HitboxDiff(key, existedBefore, existsAfter, beforeValue, afterValue));
                    estimatedBytes += Config.Command.EstimatedCommandOverheadBytes +
                        ((long)key.Length * sizeof(char));
                }
            }

            HasChanges = beforeFrameIndex != afterFrameIndex ||
                layerDiffs.Count > Config.Common.EmptyCount ||
                hitboxDiffs.Count > Config.Common.EmptyCount;
            EstimatedBytes = estimatedBytes;
        }

        public void Execute()
        {
            Apply(true);
        }

        public void Undo()
        {
            Apply(false);
        }

        private void Apply(bool forward)
        {
            target.FrameIndex = forward ? afterFrameIndex : beforeFrameIndex;
            foreach (var layerDiff in layerDiffs)
            {
                layerDiff.Target.Name = forward ? layerDiff.AfterName : layerDiff.BeforeName;
                layerDiff.Target.IsVisible = forward ? layerDiff.AfterVisible : layerDiff.BeforeVisible;
                layerDiff.Target.Opacity = forward ? layerDiff.AfterOpacity : layerDiff.BeforeOpacity;
                uint[] values = forward ? layerDiff.AfterPixels : layerDiff.BeforePixels;
                for (int index = Config.Common.FirstIndex; index < layerDiff.PixelIndices.Length; index++)
                    layerDiff.Target.Pixels[layerDiff.PixelIndices[index]].Rgba = values[index];
            }

            foreach (var hitboxDiff in hitboxDiffs)
            {
                bool exists = forward ? hitboxDiff.ExistsAfter : hitboxDiff.ExistedBefore;
                if (exists)
                    target.Hitboxes[hitboxDiff.Key] = forward ? hitboxDiff.After : hitboxDiff.Before;
                else
                    target.Hitboxes.Remove(hitboxDiff.Key);
            }
        }

        private static bool AreEqual(HitboxData left, HitboxData right)
        {
            return left.X == right.X && left.Y == right.Y &&
                left.Width == right.Width && left.Height == right.Height;
        }
    }

    internal sealed class PropertyEditCommand : ISizedCommand
    {
        private readonly IDictionary<string, object> properties;
        private readonly string fieldName;
        private readonly bool hadPreviousValue;
        private readonly object previousValue;
        private readonly object nextValue;

        public long EstimatedBytes { get; }

        public PropertyEditCommand(
            IDictionary<string, object> properties,
            string fieldName,
            bool hadPreviousValue,
            object previousValue,
            object nextValue)
        {
            this.properties = properties ?? throw new ArgumentNullException(nameof(properties));
            this.fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
            this.hadPreviousValue = hadPreviousValue;
            this.previousValue = previousValue;
            this.nextValue = nextValue;
            EstimatedBytes = Config.Command.EstimatedCommandOverheadBytes +
                ((long)fieldName.Length * sizeof(char));
        }

        public void Execute()
        {
            properties[fieldName] = nextValue;
        }

        public void Undo()
        {
            if (hadPreviousValue) properties[fieldName] = previousValue;
            else properties.Remove(fieldName);
        }
    }
}
