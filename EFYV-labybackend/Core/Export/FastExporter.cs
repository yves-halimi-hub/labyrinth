using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Export
{
    public static class FastExporter
    {
        public static void PushToUnityLiveHook<T>(
            string rawArtDir,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            T[] atlasData,
            int atlasWidth,
            int atlasHeight) where T : unmanaged
        {
            PushToUnityLiveHook(
                rawArtDir,
                targetAssetType,
                assetProperties,
                hitboxes,
                atlasData,
                atlasWidth,
                atlasHeight,
                null);
        }

        public static void PushToUnityLiveHook<T>(
            string rawArtDir,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            T[] atlasData,
            int atlasWidth,
            int atlasHeight,
            AtlasMetadataJson atlasMetadata) where T : unmanaged
        {
            PushToUnityLiveHook(
                rawArtDir,
                targetAssetType,
                assetProperties,
                hitboxes,
                atlasData,
                atlasWidth,
                atlasHeight,
                (AtlasMetadataJson?)atlasMetadata);
        }

        private static unsafe void PushToUnityLiveHook<T>(
            string rawArtDir,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            T[] atlasData,
            int atlasWidth,
            int atlasHeight,
            AtlasMetadataJson? atlasMetadata) where T : unmanaged
        {
            if (string.IsNullOrWhiteSpace(rawArtDir)) throw new ArgumentException(null, nameof(rawArtDir));
            if (string.IsNullOrWhiteSpace(targetAssetType)) throw new ArgumentException(null, nameof(targetAssetType));
            if (assetProperties == null) throw new ArgumentNullException(nameof(assetProperties));
            if (hitboxes == null) throw new ArgumentNullException(nameof(hitboxes));
            ValidateAtlas(atlasData, atlasWidth, atlasHeight);
            if (atlasMetadata.HasValue) ValidateMetadata(atlasMetadata.Value, atlasWidth, atlasHeight);

            string rootDirectory = Path.GetFullPath(rawArtDir);
            Directory.CreateDirectory(rootDirectory);

            string entityName;
            object entityNameValue;
            if (assetProperties.TryGetValue(BackendConfig.Exporter.FieldEntityName, out entityNameValue) && entityNameValue != null)
            {
                entityName = Convert.ToString(entityNameValue, CultureInfo.InvariantCulture);
            }
            else if (assetProperties.TryGetValue(BackendConfig.Exporter.FieldAssetName, out entityNameValue) && entityNameValue != null)
            {
                entityName = Convert.ToString(entityNameValue, CultureInfo.InvariantCulture);
            }
            else
            {
                entityName = targetAssetType + BackendConfig.Exporter.ExportSuffix;
            }
            if (!SafePathPolicy.IsSafeFileStem(entityName)) throw new ArgumentException(null, nameof(entityName));

            string jsonPath = SafePathPolicy.GetContainedPath(rootDirectory, entityName + BackendConfig.Exporter.EfyvExtension);
            string pngPath = SafePathPolicy.GetContainedPath(rootDirectory, entityName + BackendConfig.Exporter.PngExtension);
            string temporaryStem = BackendConfig.Exporter.TemporaryNamePrefix + entityName +
                BackendConfig.Exporter.TemporaryNamePrefix +
                Guid.NewGuid().ToString(BackendConfig.Exporter.CompactGuidFormat, CultureInfo.InvariantCulture);
            string temporaryJsonPath = SafePathPolicy.GetContainedPath(
                rootDirectory,
                temporaryStem + BackendConfig.Exporter.EfyvExtension + BackendConfig.Exporter.TemporaryExtension);
            string temporaryPngPath = SafePathPolicy.GetContainedPath(
                rootDirectory,
                temporaryStem + BackendConfig.Exporter.PngExtension + BackendConfig.Exporter.TemporaryExtension);

            try
            {
                WritePng(temporaryPngPath, atlasData, atlasWidth, atlasHeight);
                WriteJson(temporaryJsonPath, targetAssetType, assetProperties, hitboxes, atlasMetadata);

                PublishFile(temporaryPngPath, pngPath);
                PublishFile(temporaryJsonPath, jsonPath);
            }
            finally
            {
                DeleteIfPresent(temporaryPngPath);
                DeleteIfPresent(temporaryJsonPath);
            }
        }

        private static unsafe void ValidateAtlas<T>(T[] atlasData, int atlasWidth, int atlasHeight) where T : unmanaged
        {
            if (atlasData == null) throw new ArgumentNullException(nameof(atlasData));
            if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
            if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
            if (sizeof(T) != sizeof(uint)) throw new ArgumentException(null, nameof(atlasData));
            if (atlasData.Length != checked(atlasWidth * atlasHeight)) throw new ArgumentException(null, nameof(atlasData));
        }

        private static void ValidateMetadata(AtlasMetadataJson metadata, int atlasWidth, int atlasHeight)
        {
            if (metadata.formatVersion != BackendConfig.Exporter.CurrentFormatVersion) throw new ArgumentOutOfRangeException(nameof(metadata.formatVersion));
            if (metadata.frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(metadata.frameWidth));
            if (metadata.frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(metadata.frameHeight));
            if (metadata.atlasWidth != atlasWidth) throw new ArgumentException(null, nameof(metadata.atlasWidth));
            if (metadata.atlasHeight != atlasHeight) throw new ArgumentException(null, nameof(metadata.atlasHeight));
            if (atlasWidth % metadata.frameWidth != 0) throw new ArgumentException(null, nameof(metadata.frameWidth));
            if (atlasHeight % metadata.frameHeight != 0) throw new ArgumentException(null, nameof(metadata.frameHeight));
            if (metadata.animations == null) throw new ArgumentNullException(nameof(metadata.animations));

            int frameCapacity = checked((atlasWidth / metadata.frameWidth) * (atlasHeight / metadata.frameHeight));
            int previousAnimationEnd = 0;
            for (int i = 0; i < metadata.animations.Count; i++)
            {
                AnimationMetadataJson animation = metadata.animations[i];
                if (string.IsNullOrWhiteSpace(animation.name)) throw new ArgumentException(null, nameof(metadata.animations));
                if (animation.fps <= 0) throw new ArgumentOutOfRangeException(nameof(metadata.animations));
                if (animation.startFrame < 0) throw new ArgumentOutOfRangeException(nameof(metadata.animations));
                if (animation.frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(metadata.animations));
                if (animation.startFrame < previousAnimationEnd) throw new ArgumentException(null, nameof(metadata.animations));
                if (checked(animation.startFrame + animation.frameCount) > frameCapacity)
                    throw new ArgumentException(null, nameof(metadata.animations));
                previousAnimationEnd = animation.startFrame + animation.frameCount;
            }
        }

        private static void WriteJson(
            string path,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            AtlasMetadataJson? atlasMetadata)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BackendConfig.IO.DefaultFileStreamBufferSize,
                FileOptions.SequentialScan))
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString(BackendConfig.Exporter.FieldAssetType, targetAssetType);

                    writer.WriteStartObject(BackendConfig.Exporter.FieldProperties);
                    foreach (KeyValuePair<string, object> property in assetProperties)
                    {
                        WriteProperty(writer, property.Key, property.Value);
                    }
                    writer.WriteEndObject();

                    writer.WriteStartArray(BackendConfig.Exporter.FieldHitboxes);
                    for (int i = 0; i < hitboxes.Count; i++)
                    {
                        HitboxJson hitbox = hitboxes[i];
                        writer.WriteStartObject();
                        writer.WriteNumber(BackendConfig.Exporter.FieldFrameIndex, hitbox.frameIndex);
                        writer.WriteString(BackendConfig.Exporter.FieldHitboxType, hitbox.hitboxType);
                        writer.WriteNumber(BackendConfig.Exporter.FieldX, hitbox.x);
                        writer.WriteNumber(BackendConfig.Exporter.FieldY, hitbox.y);
                        writer.WriteNumber(BackendConfig.Exporter.FieldWidth, hitbox.width);
                        writer.WriteNumber(BackendConfig.Exporter.FieldHeight, hitbox.height);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();

                    if (atlasMetadata.HasValue)
                    {
                        WriteAtlasMetadata(writer, atlasMetadata.Value);
                    }

                    writer.WriteEndObject();
                    writer.Flush();
                }
                stream.Flush(true);
            }
        }

        private static void WriteProperty(Utf8JsonWriter writer, string name, object value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (value == null)
            {
                writer.WriteNull(name);
                return;
            }

            if (value is string stringValue) writer.WriteString(name, stringValue);
            else if (value is bool boolValue) writer.WriteBoolean(name, boolValue);
            else if (value is byte byteValue) writer.WriteNumber(name, byteValue);
            else if (value is sbyte sbyteValue) writer.WriteNumber(name, sbyteValue);
            else if (value is short shortValue) writer.WriteNumber(name, shortValue);
            else if (value is ushort ushortValue) writer.WriteNumber(name, ushortValue);
            else if (value is int intValue) writer.WriteNumber(name, intValue);
            else if (value is uint uintValue) writer.WriteNumber(name, uintValue);
            else if (value is long longValue) writer.WriteNumber(name, longValue);
            else if (value is ulong ulongValue) writer.WriteNumber(name, ulongValue);
            else if (value is float floatValue) writer.WriteNumber(name, floatValue);
            else if (value is double doubleValue) writer.WriteNumber(name, doubleValue);
            else if (value is decimal decimalValue) writer.WriteNumber(name, decimalValue);
            else if (value is JsonElement jsonElement)
            {
                writer.WritePropertyName(name);
                jsonElement.WriteTo(writer);
            }
            else
            {
                writer.WritePropertyName(name);
                JsonSerializer.Serialize(writer, value, value.GetType());
            }
        }

        private static void WriteAtlasMetadata(Utf8JsonWriter writer, AtlasMetadataJson metadata)
        {
            writer.WriteStartObject(BackendConfig.Exporter.FieldAtlas);
            writer.WriteNumber(BackendConfig.Exporter.FieldFormatVersion, metadata.formatVersion);
            writer.WriteNumber(BackendConfig.Exporter.FieldFrameWidth, metadata.frameWidth);
            writer.WriteNumber(BackendConfig.Exporter.FieldFrameHeight, metadata.frameHeight);
            writer.WriteNumber(BackendConfig.Exporter.FieldAtlasWidth, metadata.atlasWidth);
            writer.WriteNumber(BackendConfig.Exporter.FieldAtlasHeight, metadata.atlasHeight);
            writer.WriteStartArray(BackendConfig.Exporter.FieldAnimations);
            for (int i = 0; i < metadata.animations.Count; i++)
            {
                AnimationMetadataJson animation = metadata.animations[i];
                writer.WriteStartObject();
                writer.WriteString(BackendConfig.Exporter.FieldName, animation.name);
                writer.WriteNumber(BackendConfig.Exporter.FieldFps, animation.fps);
                writer.WriteNumber(BackendConfig.Exporter.FieldStartFrame, animation.startFrame);
                writer.WriteNumber(BackendConfig.Exporter.FieldFrameCount, animation.frameCount);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WritePng<T>(string path, T[] atlasData, int atlasWidth, int atlasHeight) where T : unmanaged
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BackendConfig.IO.DefaultFileStreamBufferSize,
                FileOptions.SequentialScan))
            {
                FastPngEncoder.Write(stream, atlasData, atlasWidth, atlasHeight);
                stream.Flush(true);
            }
        }

        private static void PublishFile(string temporaryPath, string destinationPath)
        {
            if (File.Exists(destinationPath)) File.Replace(temporaryPath, destinationPath, null);
            else File.Move(temporaryPath, destinationPath);
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public static unsafe void PackFramesToAtlas<T>(
            T[] destAtlas,
            int atlasWidth,
            T[] sourceFrame,
            int frameWidth,
            int frameHeight,
            int destX,
            int destY) where T : unmanaged
        {
            if (destAtlas == null) throw new ArgumentNullException(nameof(destAtlas));
            if (sourceFrame == null) throw new ArgumentNullException(nameof(sourceFrame));
            if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
            if (frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
            if (frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));
            if (destX < 0) throw new ArgumentOutOfRangeException(nameof(destX));
            if (destY < 0) throw new ArgumentOutOfRangeException(nameof(destY));
            if (destAtlas.Length == 0 || destAtlas.Length % atlasWidth != 0) throw new ArgumentException(null, nameof(destAtlas));

            int atlasHeight = destAtlas.Length / atlasWidth;
            int requiredSourcePixels = checked(frameWidth * frameHeight);
            if (sourceFrame.Length < requiredSourcePixels) throw new ArgumentException(null, nameof(sourceFrame));
            if (checked(destX + frameWidth) > atlasWidth) throw new ArgumentException(null, nameof(destX));
            if (checked(destY + frameHeight) > atlasHeight) throw new ArgumentException(null, nameof(destY));

            fixed (T* destination = destAtlas)
            fixed (T* source = sourceFrame)
            {
                long rowBytes = checked((long)frameWidth * sizeof(T));
                for (int y = 0; y < frameHeight; y++)
                {
                    T* destinationRow = destination + (destY + y) * atlasWidth + destX;
                    T* sourceRow = source + y * frameWidth;
                    Buffer.MemoryCopy(sourceRow, destinationRow, rowBytes, rowBytes);
                }
            }
        }
    }
}
