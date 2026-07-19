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
            public Layer Target;
            public int Index;
            public string Name;
            public bool IsVisible;
            public float Opacity;
            public uint[] Pixels;
        }

        public int FrameIndex { get; }
        public List<LayerState> Layers { get; }
        public Dictionary<string, HitboxData> Hitboxes { get; }
        // Item #6: deep-copied attachment list, captured only for tools that
        // can mutate attachments (the stamp tool) - the same opt-in contract
        // hitboxes have with the hitbox tool. Null means "not captured".
        public List<SubElementAttachment> Attachments { get; }

        private readonly List<Layer> layerMembership;

        private FrameEditCapture(
            int frameIndex,
            List<LayerState> layers,
            Dictionary<string, HitboxData> hitboxes,
            List<SubElementAttachment> attachments,
            List<Layer> layerMembership)
        {
            FrameIndex = frameIndex;
            Layers = layers;
            Hitboxes = hitboxes;
            Attachments = attachments;
            this.layerMembership = layerMembership;
        }

        public static FrameEditCapture Capture(Frame frame, ITool tool)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            var layerTool = tool as ILayerTool;
            return CaptureCore(
                frame,
                layerTool != null ? layerTool.ActiveLayerIndex : Config.Common.NotFoundIndex,
                tool is HitboxTool,
                tool is StampTool);
        }

        // Captures one layer by index without a tool: the floating-selection
        // lift/paste path uses this so its whole lift-move-anchor interaction
        // can commit as ONE sparse FrameEditCommand against this capture.
        internal static FrameEditCapture CaptureLayer(Frame frame, int layerIndex)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            return CaptureCore(frame, layerIndex, false, false);
        }

        private static FrameEditCapture CaptureCore(
            Frame frame,
            int layerIndex,
            bool captureHitboxes,
            bool captureAttachments)
        {
            var layers = new List<LayerState>();
            if (layerIndex >= Config.Common.FirstIndex && layerIndex < frame.Layers.Count)
            {
                Layer layer = frame.Layers[layerIndex];
                var pixels = new uint[layer.Pixels.Length];
                for (int index = Config.Common.FirstIndex; index < pixels.Length; index++)
                    pixels[index] = layer.Pixels[index].Rgba;
                layers.Add(new LayerState
                {
                    Target = layer,
                    Index = layerIndex,
                    Name = layer.Name,
                    IsVisible = layer.IsVisible,
                    Opacity = layer.Opacity,
                    Pixels = pixels
                });
            }

            Dictionary<string, HitboxData> hitboxes = null;
            if (captureHitboxes)
                hitboxes = new Dictionary<string, HitboxData>(frame.Hitboxes, StringComparer.Ordinal);
            List<SubElementAttachment> attachments = null;
            if (captureAttachments) attachments = CloneAttachments(frame.Attachments);
            return new FrameEditCapture(
                frame.FrameIndex,
                layers,
                hitboxes,
                attachments,
                new List<Layer>(frame.Layers));
        }

        internal static List<SubElementAttachment> CloneAttachments(
            List<SubElementAttachment> source)
        {
            var clones = new List<SubElementAttachment>(source.Count);
            foreach (SubElementAttachment attachment in source)
                clones.Add(attachment?.Clone());
            return clones;
        }

        // Gesture rollback: restores the captured state directly instead of diffing,
        // so it cannot fault on a frame a tool structurally mutated mid-gesture. The
        // membership snapshot holds layer REFERENCES, which puts added/removed/
        // reordered layers back exactly as captured; the captured active layer gets
        // its pixels and metadata rewritten in place.
        public void Restore(Frame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            frame.FrameIndex = FrameIndex;
            frame.Layers.Clear();
            frame.Layers.AddRange(layerMembership);

            foreach (LayerState state in Layers)
            {
                Layer layer = state.Target;
                layer.Name = state.Name;
                layer.IsVisible = state.IsVisible;
                layer.Opacity = state.Opacity;
                int pixelCount = Math.Min(state.Pixels.Length, layer.Pixels.Length);
                for (int index = Config.Common.FirstIndex; index < pixelCount; index++)
                    layer.Pixels[index].Rgba = state.Pixels[index];
            }

            if (Hitboxes != null)
            {
                frame.Hitboxes.Clear();
                foreach (KeyValuePair<string, HitboxData> pair in Hitboxes)
                    frame.Hitboxes[pair.Key] = pair.Value;
            }

            if (Attachments != null)
            {
                // Restore DEEP COPIES so repeated restores (or later live
                // mutations) can never corrupt the capture itself.
                frame.Attachments.Clear();
                frame.Attachments.AddRange(CloneAttachments(Attachments));
            }
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
        // Item #6 attachment diff: attachments are tiny records, so a changed
        // list is stored as full before/after deep copies instead of a sparse
        // per-field diff. Null when the capture skipped attachments or the
        // list is unchanged.
        private readonly List<SubElementAttachment> attachmentsBefore;
        private readonly List<SubElementAttachment> attachmentsAfter;
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
                // Structural-mutation contract: the diff is only valid while the
                // captured layer still occupies its captured slot with an unchanged
                // buffer size. A tool that removed, reordered, or resized it makes
                // this constructor throw InvalidOperationException; the session then
                // rolls the gesture back from the capture and rethrows this single
                // exception.
                if (beforeLayer.Index < Config.Common.FirstIndex || beforeLayer.Index >= target.Layers.Count)
                    throw new InvalidOperationException();
                Layer afterLayer = target.Layers[beforeLayer.Index];
                if (!ReferenceEquals(afterLayer, beforeLayer.Target)) throw new InvalidOperationException();
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

            if (before.Attachments != null &&
                !AreAttachmentListsEqual(before.Attachments, target.Attachments))
            {
                attachmentsBefore = FrameEditCapture.CloneAttachments(before.Attachments);
                attachmentsAfter = FrameEditCapture.CloneAttachments(target.Attachments);
                foreach (SubElementAttachment attachment in attachmentsBefore)
                    estimatedBytes += EstimateAttachmentBytes(attachment);
                foreach (SubElementAttachment attachment in attachmentsAfter)
                    estimatedBytes += EstimateAttachmentBytes(attachment);
            }

            HasChanges = beforeFrameIndex != afterFrameIndex ||
                layerDiffs.Count > Config.Common.EmptyCount ||
                hitboxDiffs.Count > Config.Common.EmptyCount ||
                attachmentsBefore != null;
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

            if (attachmentsBefore != null)
            {
                target.Attachments.Clear();
                target.Attachments.AddRange(FrameEditCapture.CloneAttachments(
                    forward ? attachmentsAfter : attachmentsBefore));
            }
        }

        private static bool AreEqual(HitboxData left, HitboxData right)
        {
            return left.X == right.X && left.Y == right.Y &&
                left.Width == right.Width && left.Height == right.Height;
        }

        private static bool AreAttachmentListsEqual(
            List<SubElementAttachment> left,
            List<SubElementAttachment> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = Config.Common.FirstIndex; index < left.Count; index++)
            {
                SubElementAttachment before = left[index];
                SubElementAttachment after = right[index];
                if (before == null || after == null)
                {
                    if (!ReferenceEquals(before, after)) return false;
                    continue;
                }
                if (!string.Equals(before.SubElementName, after.SubElementName, StringComparison.Ordinal) ||
                    before.X != after.X || before.Y != after.Y ||
                    before.ZOrder != after.ZOrder ||
                    before.FlipX != after.FlipX || before.FlipY != after.FlipY)
                    return false;
            }
            return true;
        }

        private static long EstimateAttachmentBytes(SubElementAttachment attachment)
        {
            return Config.Command.EstimatedCommandOverheadBytes +
                ((long)(attachment?.SubElementName.Length ?? Config.Common.EmptyCount) * sizeof(char));
        }
    }

    // Item #8 global color swap: replaces every pixel that is exactly fromRgba
    // with toRgba across the captured layers. The command stores only the
    // affected pixel INDICES (sparse): every one of those pixels held fromRgba
    // before Execute and toRgba after, so undo/redo rewrite the two known
    // values without before/after arrays. One instance covers the whole swap
    // scope (a frame or all frames) as ONE history entry.
    internal sealed class ColorSwapCommand : ISizedCommand
    {
        internal sealed class LayerSwap
        {
            public Layer Target { get; }
            public int[] PixelIndices { get; }

            public LayerSwap(Layer target, int[] pixelIndices)
            {
                Target = target;
                PixelIndices = pixelIndices;
            }
        }

        private readonly List<LayerSwap> swaps;
        private readonly uint fromRgba;
        private readonly uint toRgba;

        public long EstimatedBytes { get; }

        public ColorSwapCommand(List<LayerSwap> swaps, uint fromRgba, uint toRgba)
        {
            this.swaps = swaps ?? throw new ArgumentNullException(nameof(swaps));
            this.fromRgba = fromRgba;
            this.toRgba = toRgba;

            long estimatedBytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (LayerSwap swap in swaps)
                estimatedBytes += (long)swap.PixelIndices.Length * sizeof(int);
            EstimatedBytes = estimatedBytes;
        }

        public void Execute() => Apply(toRgba);
        public void Undo() => Apply(fromRgba);

        private void Apply(uint value)
        {
            foreach (LayerSwap swap in swaps)
            {
                Layer layer = swap.Target;
                for (int index = Config.Common.FirstIndex; index < swap.PixelIndices.Length; index++)
                    layer.Pixels[swap.PixelIndices[index]].Rgba = value;
            }
        }
    }

    // Item #5 map editing: ONE history entry covering a map mutation - a
    // sparse cell diff (indices into FastGridMap.RawData with before/after
    // ids) plus the prop records the operation APPENDED. Every current map
    // operation only ever appends props, so undo removes exactly the appended
    // instances from the tail (reverse order keeps the swap-list stable) and
    // redo re-adds the same instances (Remove reset their tracking indices).
    internal sealed class MapEditCommand : ISizedCommand
    {
        private readonly EFYVBackend.Core.Collections.FastGridMap target;
        private readonly int[] cellIndices;
        private readonly short[] beforeTiles;
        private readonly short[] afterTiles;
        private readonly EFYVBackend.Core.Collections.FastGridMap.MapPropData[] appendedProps;

        public bool HasChanges =>
            cellIndices.Length > Config.Command.EmptyStackCount ||
            appendedProps.Length > Config.Command.EmptyStackCount;
        public long EstimatedBytes { get; }

        public MapEditCommand(
            EFYVBackend.Core.Collections.FastGridMap target,
            int[] cellIndices,
            short[] beforeTiles,
            short[] afterTiles,
            EFYVBackend.Core.Collections.FastGridMap.MapPropData[] appendedProps)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            this.cellIndices = cellIndices ?? throw new ArgumentNullException(nameof(cellIndices));
            this.beforeTiles = beforeTiles ?? throw new ArgumentNullException(nameof(beforeTiles));
            this.afterTiles = afterTiles ?? throw new ArgumentNullException(nameof(afterTiles));
            this.appendedProps = appendedProps ??
                Array.Empty<EFYVBackend.Core.Collections.FastGridMap.MapPropData>();
            if (beforeTiles.Length != cellIndices.Length || afterTiles.Length != cellIndices.Length)
                throw new ArgumentException(nameof(beforeTiles));

            long estimatedBytes = Config.Command.EstimatedCommandOverheadBytes +
                (long)cellIndices.Length * (sizeof(int) + sizeof(short) + sizeof(short));
            foreach (EFYVBackend.Core.Collections.FastGridMap.MapPropData prop in this.appendedProps)
            {
                estimatedBytes += Config.Command.EstimatedCommandOverheadBytes +
                    ((long)(prop.AssetKey?.Length ?? Config.Common.EmptyCount) * sizeof(char));
            }
            EstimatedBytes = estimatedBytes;
        }

        public void Execute()
        {
            short[] tiles = target.RawData;
            for (int index = Config.Common.FirstIndex; index < cellIndices.Length; index++)
                tiles[cellIndices[index]] = afterTiles[index];
            foreach (EFYVBackend.Core.Collections.FastGridMap.MapPropData prop in appendedProps)
                target.Props.Add(prop);
        }

        public void Undo()
        {
            for (int index = appendedProps.Length - Config.Common.UnitCount;
                index >= Config.Common.FirstIndex;
                index--)
                target.Props.Remove(appendedProps[index]);
            short[] tiles = target.RawData;
            for (int index = Config.Common.FirstIndex; index < cellIndices.Length; index++)
                tiles[cellIndices[index]] = beforeTiles[index];
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
