using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using EFYV.Runtime.Media;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYV.Labyrinth.Artifacts
{
    public static class LabyMakeSnapshotParser
    {
        public static LabyMakeSnapshot Parse(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            if (source.Length == 0 || source.Length > LabyrinthArtifactLimits.MaxSnapshotBytes)
                throw new InvalidDataException("Snapshot must contain at most 16 MB.");
            RejectDuplicateProperties(source.Span);
            cancellationToken.ThrowIfCancellationRequested();

            using JsonDocument document = JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = LabyrinthArtifactLimits.MaxJsonDepth });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Snapshot must be an object.");
            int width = RequiredInt(root, "canvasWidth", 1, LabyrinthArtifactLimits.MaxCanvasDimension);
            int height = RequiredInt(root, "canvasHeight", 1, LabyrinthArtifactLimits.MaxCanvasDimension);
            int pixelCount = checked(width * height);
            long frameBytes = checked((long)pixelCount * 4);
            string assetType = OptionalString(root, "targetAssetType") ?? "EFYVSprite";
            string? baseAssetType = OptionalString(root, "baseAssetType");
            Dictionary<string, object> properties = ParseProperties(root);
            List<HitboxJson> hitboxes = ParseList<HitboxJson>(root, "hitboxes") ?? new List<HitboxJson>();
            List<AttachmentJson>? attachments = ParseList<AttachmentJson>(root, "attachments");
            TilesetManifestJson? tileset = ParseOptional<TilesetManifestJson>(root, "tileset", JsonValueKind.Object);

            if (!root.TryGetProperty("animations", out JsonElement animations) || animations.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("animations must be an array.");
            if (animations.GetArrayLength() == 0 || animations.GetArrayLength() > LabyrinthArtifactLimits.MaxAnimations)
                throw new InvalidDataException("animations must contain between 1 and the declared animation limit.");

            var frames = new List<uint[]>();
            var animationMetadata = new List<AnimationMetadataJson>();
            foreach (JsonElement animation in animations.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (animation.ValueKind != JsonValueKind.Object) throw new InvalidDataException("animation must be an object.");
                string name = OptionalString(animation, "stateName") ?? throw new InvalidDataException("animation stateName is required.");
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("animation stateName is required.");
                int fps = RequiredInt(animation, "fps", 1, LabyrinthArtifactLimits.MaxFps);
                if (!animation.TryGetProperty("frames", out JsonElement rawFrames) || rawFrames.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("animation frames must be an array.");
                int frameCount = rawFrames.GetArrayLength();
                if (frameCount == 0 || frameCount > LabyrinthArtifactLimits.MaxFrames)
                    throw new InvalidDataException("Each animation needs a bounded, non-empty frame list.");
                if (frames.Count + (long)frameCount > LabyrinthArtifactLimits.MaxFrames)
                    throw new InvalidDataException("Snapshot contains too many frames.");
                if (checked((frames.Count + (long)frameCount) * frameBytes) > LabyrinthArtifactLimits.MaxDecodedFrameBytes)
                    throw new InvalidDataException("Decoded frame data exceeds the 64 MB aggregate limit.");

                int startFrame = frames.Count;
                var durations = new List<int>(frameCount);
                bool hasDuration = false;
                foreach (JsonElement frame in rawFrames.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    frames.Add(FlattenFrame(frame, width, height, cancellationToken));
                    if (frame.TryGetProperty("durationMs", out JsonElement duration))
                    {
                        if (!duration.TryGetInt32(out int value) || value < 0 || value > LabyrinthArtifactLimits.MaxFrameDurationMs)
                            throw new InvalidDataException("frame durationMs is outside the released range.");
                        durations.Add(value);
                        hasDuration = true;
                    }
                    else durations.Add(0);
                }

                animationMetadata.Add(new AnimationMetadataJson
                {
                    name = name,
                    fps = fps,
                    startFrame = startFrame,
                    frameCount = frameCount,
                    frameDurationsMs = hasDuration ? durations : null,
                    loopStart = OptionalInt(animation, "loopStart"),
                    loopEnd = OptionalInt(animation, "loopEnd"),
                    pingPong = OptionalBool(animation, "pingPong"),
                    effects = ParseList<EffectDescriptorJson>(animation, "effects"),
                });
            }

            AtlasLayout.ComputeSquare(frames.Count, width, height, out int columns, out _, out int atlasWidth, out int atlasHeight);
            if (atlasWidth > LabyrinthArtifactLimits.MaxAtlasDimension || atlasHeight > LabyrinthArtifactLimits.MaxAtlasDimension ||
                (long)atlasWidth * atlasHeight > LabyrinthArtifactLimits.MaxAtlasPixels)
                throw new InvalidDataException("Atlas exceeds the released export limits.");
            var atlasPixels = new uint[checked(atlasWidth * atlasHeight)];
            AtlasLayout.PackRgbaFrames(frames.ToArray(), width, height, atlasPixels, atlasWidth, columns);

            var atlas = new AtlasMetadataJson
            {
                formatVersion = LabyrinthArtifactLimits.AtlasFormatVersion,
                frameWidth = width,
                frameHeight = height,
                atlasWidth = atlasWidth,
                atlasHeight = atlasHeight,
                animations = animationMetadata,
            };
            return new LabyMakeSnapshot(source.ToArray(), assetType, baseAssetType, width, height,
                new[] { atlasPixels }, properties, hitboxes, atlas, attachments, tileset);
        }

        private static unsafe uint[] FlattenFrame(JsonElement frame, int width, int height, CancellationToken cancellationToken)
        {
            if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("layers", out JsonElement layers) || layers.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("frame layers must be an array.");
            if (layers.GetArrayLength() > LabyrinthArtifactLimits.MaxLayersPerFrame)
                throw new InvalidDataException("frame contains too many layers.");
            int pixelCount = checked(width * height);
            var destination = new uint[pixelCount];
            foreach (JsonElement layer in layers.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (layer.ValueKind != JsonValueKind.Object) throw new InvalidDataException("layer must be an object.");
                if (layer.TryGetProperty("isVisible", out JsonElement visible) && visible.ValueKind == JsonValueKind.False) continue;
                if (!layer.TryGetProperty("rgbaBytes", out JsonElement encoded) || encoded.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("layer rgbaBytes is required.");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(encoded.GetString()!); }
                catch (FormatException exception) { throw new InvalidDataException("layer rgbaBytes is invalid.", exception); }
                if (bytes.Length != checked(pixelCount * 4)) throw new InvalidDataException("layer dimensions do not match the canvas.");
                var source = new uint[pixelCount];
                for (int pixel = 0, offset = 0; pixel < source.Length; pixel++, offset += 4)
                    source[pixel] = bytes[offset] | ((uint)bytes[offset + 1] << 8) | ((uint)bytes[offset + 2] << 16) | ((uint)bytes[offset + 3] << 24);

                float opacity = 1f;
                if (layer.TryGetProperty("opacity", out JsonElement rawOpacity))
                {
                    if (!rawOpacity.TryGetSingle(out opacity) || float.IsNaN(opacity) || float.IsInfinity(opacity))
                        throw new InvalidDataException("layer opacity must be finite.");
                    opacity = System.Math.Clamp(opacity, 0f, 1f);
                }
                byte opacityByte = (byte)(opacity * 255f + 0.5f);
                if (opacityByte == 0) continue;
                fixed (uint* destinationPointer = destination)
                fixed (uint* sourcePointer = source)
                {
                    for (int start = 0; start < pixelCount; start += LabyrinthArtifactLimits.BlendCancellationPixelBatch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = System.Math.Min(LabyrinthArtifactLimits.BlendCancellationPixelBatch, pixelCount - start);
                        RuntimeMediaKernel.BlendRgbaBatch(destinationPointer + start, sourcePointer + start, count, opacityByte, 0);
                    }
                }
            }
            return destination;
        }

        private static Dictionary<string, object> ParseProperties(JsonElement root)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (!root.TryGetProperty("assetProperties", out JsonElement properties)) return result;
            if (properties.ValueKind != JsonValueKind.Object) throw new InvalidDataException("assetProperties must be an object.");
            foreach (JsonProperty property in properties.EnumerateObject()) result.Add(property.Name, property.Value.Clone());
            return result;
        }

        private static List<T>? ParseList<T>(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException(name + " must be an array.");
            try { return JsonSerializer.Deserialize<List<T>>(value.GetRawText()); }
            catch (JsonException exception) { throw new InvalidDataException(name + " is invalid.", exception); }
        }

        private static T? ParseOptional<T>(JsonElement root, string name, JsonValueKind kind) where T : struct
        {
            if (!root.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind != kind) throw new InvalidDataException(name + " has the wrong JSON kind.");
            try { return JsonSerializer.Deserialize<T>(value.GetRawText()); }
            catch (JsonException exception) { throw new InvalidDataException(name + " is invalid.", exception); }
        }

        private static int RequiredInt(JsonElement root, string name, int minimum, int maximum)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int number) || number < minimum || number > maximum)
                throw new InvalidDataException(name + " must be from " + minimum + " through " + maximum + ".");
            return number;
        }

        private static int? OptionalInt(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value)) return null;
            if (!value.TryGetInt32(out int number)) throw new InvalidDataException(name + " must be an integer.");
            return number;
        }

        private static bool? OptionalBool(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            throw new InvalidDataException(name + " must be a boolean.");
        }

        private static string? OptionalString(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static void RejectDuplicateProperties(ReadOnlySpan<byte> bytes)
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = LabyrinthArtifactLimits.MaxJsonDepth });
            var objects = new Stack<HashSet<string>?>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new HashSet<string>(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.StartArray) objects.Push(null);
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) objects.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    HashSet<string>? current = objects.Peek();
                    string name = reader.GetString() ?? throw new InvalidDataException("JSON property name is invalid.");
                    if (current == null || !current.Add(name)) throw new InvalidDataException("Duplicate JSON property " + name + ".");
                }
            }
        }
    }
}
