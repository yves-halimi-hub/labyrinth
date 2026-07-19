using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Export
{
    // Per-cause atlas metadata validation outcome (#16b/#16d). Shared by the
    // exporter, the LabyMake export engine/validator, and the Unity importer so
    // every path enforces the exact same contract (including the Unity texture
    // size caps two of the old copies skipped).
    public enum AtlasMetadataError
    {
        None = 0,
        FormatVersion,
        FrameDimensions,
        AtlasDimensions,
        DimensionMismatch,
        AtlasLimit,
        FrameAlignment,
        AnimationsMissing,
        AnimationName,
        AnimationFps,
        AnimationStartFrame,
        AnimationFrameCount,
        AnimationOverlap,
        AnimationPastCapacity,
        // Item #10 optional timing/playback fields (appended; values are part
        // of no wire format, but stay stable for log readability).
        AnimationFrameDurations,
        AnimationLoopRange,
        // Item #7 optional per-animation runtime-effect descriptors (appended).
        AnimationEffects
    }

    // Item #5: per-cause tileset-manifest validation outcome, mirroring
    // AtlasMetadataError. Shared by the exporter (throws) and the Unity
    // importer (logs + rejects) so both ends enforce the same contract.
    public enum TilesetManifestError
    {
        None = 0,
        TileSize,
        TilesMissing,
        TileCount,
        TileName,
        // The sibling atlas block must slice the sheet into exactly
        // tileSize-square frames with room for every declared tile.
        AtlasFrameMismatch,
        AtlasCapacity
    }

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
                null,
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
                (AtlasMetadataJson?)atlasMetadata,
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
            AtlasMetadataJson atlasMetadata,
            string baseAssetType) where T : unmanaged
        {
            PushToUnityLiveHook(
                rawArtDir,
                targetAssetType,
                assetProperties,
                hitboxes,
                atlasData,
                atlasWidth,
                atlasHeight,
                (AtlasMetadataJson?)atlasMetadata,
                baseAssetType,
                null,
                null);
        }

        // Item #6: overload carrying the optional sub-element attachment
        // records (documentVersion 4). A null or empty list writes no
        // "attachments" member at all, keeping attachment-free documents
        // byte-identical to earlier output.
        public static void PushToUnityLiveHook<T>(
            string rawArtDir,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            T[] atlasData,
            int atlasWidth,
            int atlasHeight,
            AtlasMetadataJson atlasMetadata,
            string baseAssetType,
            List<AttachmentJson> attachments) where T : unmanaged
        {
            PushToUnityLiveHook(
                rawArtDir,
                targetAssetType,
                assetProperties,
                hitboxes,
                atlasData,
                atlasWidth,
                atlasHeight,
                (AtlasMetadataJson?)atlasMetadata,
                baseAssetType,
                attachments,
                null);
        }

        // Item #5: overload carrying the optional tileset manifest block
        // (documentVersion 5). A null manifest writes no "tileset" member at
        // all, keeping non-tileset documents byte-identical to earlier output.
        public static void PushToUnityLiveHook<T>(
            string rawArtDir,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            T[] atlasData,
            int atlasWidth,
            int atlasHeight,
            AtlasMetadataJson atlasMetadata,
            string baseAssetType,
            List<AttachmentJson> attachments,
            TilesetManifestJson? tilesetManifest) where T : unmanaged
        {
            PushToUnityLiveHook(
                rawArtDir,
                targetAssetType,
                assetProperties,
                hitboxes,
                atlasData,
                atlasWidth,
                atlasHeight,
                (AtlasMetadataJson?)atlasMetadata,
                baseAssetType,
                attachments,
                tilesetManifest);
        }

        // Item #27 live fast path: publishes ONLY the .efyvlaby metadata,
        // leaving the sibling PNG untouched. LabyMake's live-debug loop takes
        // this path when the accumulated edit scope since the last publish
        // changed no exported pixels (a hitbox nudge, a property tweak, a
        // playback-tag edit), so the atlas is never re-packed or re-encoded.
        // The atlas dimensions still travel (they pin the metadata to the
        // already-published sheet) but no pixel payload is required. Callers
        // must have confirmed the sibling PNG already exists; the exporter
        // never fabricates one here.
        public static void PushMetadataOnlyToUnityLiveHook(
            string rawArtDir,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            int atlasWidth,
            int atlasHeight,
            AtlasMetadataJson atlasMetadata,
            string baseAssetType,
            List<AttachmentJson> attachments)
        {
            PushToUnityLiveHook(
                rawArtDir,
                targetAssetType,
                assetProperties,
                hitboxes,
                System.Array.Empty<uint>(),
                atlasWidth,
                atlasHeight,
                (AtlasMetadataJson?)atlasMetadata,
                baseAssetType,
                attachments,
                null,
                false);
        }

        private static unsafe void PushToUnityLiveHook<T>(
            string rawArtDir,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            T[] atlasData,
            int atlasWidth,
            int atlasHeight,
            AtlasMetadataJson? atlasMetadata,
            string baseAssetType) where T : unmanaged
        {
            PushToUnityLiveHook(
                rawArtDir,
                targetAssetType,
                assetProperties,
                hitboxes,
                atlasData,
                atlasWidth,
                atlasHeight,
                atlasMetadata,
                baseAssetType,
                null,
                null);
        }

        private static unsafe void PushToUnityLiveHook<T>(
            string rawArtDir,
            string targetAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            T[] atlasData,
            int atlasWidth,
            int atlasHeight,
            AtlasMetadataJson? atlasMetadata,
            string baseAssetType,
            List<AttachmentJson> attachments,
            TilesetManifestJson? tilesetManifest,
            // Item #27: false takes the metadata-only path - the PNG is neither
            // re-encoded nor published, and atlasData carries no pixels (the
            // declared atlasWidth/atlasHeight still pin the metadata to the
            // existing sheet).
            bool writeImage = true) where T : unmanaged
        {
            if (string.IsNullOrWhiteSpace(rawArtDir)) throw new ArgumentException(null, nameof(rawArtDir));
            if (string.IsNullOrWhiteSpace(targetAssetType)) throw new ArgumentException(null, nameof(targetAssetType));
            if (assetProperties == null) throw new ArgumentNullException(nameof(assetProperties));
            if (hitboxes == null) throw new ArgumentNullException(nameof(hitboxes));
            if (writeImage) ValidateAtlas(atlasData, atlasWidth, atlasHeight);
            else if (atlasWidth <= 0 || atlasHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(atlasWidth));
            if (atlasMetadata.HasValue) ValidateMetadata(atlasMetadata.Value, atlasWidth, atlasHeight);
            if (!TryValidateAttachments(attachments, out int invalidAttachmentIndex))
                throw new ArgumentException(
                    "attachments[" + invalidAttachmentIndex + "]",
                    nameof(attachments));
            if (tilesetManifest.HasValue &&
                !TryValidateTilesetManifest(tilesetManifest.Value, atlasMetadata, out TilesetManifestError tilesetError))
                throw new ArgumentException(tilesetError.ToString(), nameof(tilesetManifest));

            string rootDirectory = Path.GetFullPath(rawArtDir);
            Directory.CreateDirectory(rootDirectory);

            // No-identity exports are rejected outright (#36): the three legacy
            // fallbacks (type-suffixed stem here, KeyNotFoundException in the
            // export engine, an "UnknownEntity" collapse in the Unity importer)
            // disagreed with each other and silently aliased unrelated assets.
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
                throw new ArgumentException(null, nameof(assetProperties));
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
                if (writeImage) WritePng(temporaryPngPath, atlasData, atlasWidth, atlasHeight);
                WriteJson(
                    temporaryJsonPath,
                    targetAssetType,
                    baseAssetType,
                    assetProperties,
                    hitboxes,
                    atlasMetadata,
                    attachments,
                    tilesetManifest);

                if (writeImage) PublishFile(temporaryPngPath, pngPath);
                PublishFile(temporaryJsonPath, jsonPath);
            }
            finally
            {
                if (writeImage) DeleteIfPresent(temporaryPngPath);
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

        // The ONE atlas-metadata validator (#16b). Validates the metadata against
        // itself; the exporter-side overload below additionally pins the declared
        // dimensions to the actual pixel payload.
        public static bool TryValidateAtlasMetadata(AtlasMetadataJson metadata, out AtlasMetadataError error)
        {
            return TryValidateAtlasMetadata(metadata, metadata.atlasWidth, metadata.atlasHeight, out error);
        }

        public static bool TryValidateAtlasMetadata(
            AtlasMetadataJson metadata,
            int atlasWidth,
            int atlasHeight,
            out AtlasMetadataError error)
        {
            error = Classify(metadata, atlasWidth, atlasHeight);
            return error == AtlasMetadataError.None;
        }

        private static AtlasMetadataError Classify(AtlasMetadataJson metadata, int atlasWidth, int atlasHeight)
        {
            if (metadata.formatVersion != BackendConfig.Exporter.CurrentFormatVersion)
                return AtlasMetadataError.FormatVersion;
            if (metadata.frameWidth <= 0 || metadata.frameHeight <= 0)
                return AtlasMetadataError.FrameDimensions;
            if (atlasWidth <= 0 || atlasHeight <= 0)
                return AtlasMetadataError.AtlasDimensions;
            if (metadata.atlasWidth != atlasWidth || metadata.atlasHeight != atlasHeight)
                return AtlasMetadataError.DimensionMismatch;
            // Unity texture caps: the two legacy copies that skipped these let
            // exports through that the importer then refused.
            if (atlasWidth > EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasDimension ||
                atlasHeight > EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasDimension ||
                (long)atlasWidth * atlasHeight > EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasPixelCount)
                return AtlasMetadataError.AtlasLimit;
            if (atlasWidth % metadata.frameWidth != 0 || atlasHeight % metadata.frameHeight != 0)
                return AtlasMetadataError.FrameAlignment;
            if (metadata.animations == null)
                return AtlasMetadataError.AnimationsMissing;

            long frameCapacity = ((long)atlasWidth / metadata.frameWidth) * (atlasHeight / metadata.frameHeight);
            long previousAnimationEnd = 0;
            for (int i = 0; i < metadata.animations.Count; i++)
            {
                AnimationMetadataJson animation = metadata.animations[i];
                if (string.IsNullOrWhiteSpace(animation.name)) return AtlasMetadataError.AnimationName;
                if (animation.fps <= 0) return AtlasMetadataError.AnimationFps;
                if (animation.startFrame < 0) return AtlasMetadataError.AnimationStartFrame;
                if (animation.frameCount <= 0) return AtlasMetadataError.AnimationFrameCount;
                if (animation.startFrame < previousAnimationEnd) return AtlasMetadataError.AnimationOverlap;
                if ((long)animation.startFrame + animation.frameCount > frameCapacity)
                    return AtlasMetadataError.AnimationPastCapacity;
                previousAnimationEnd = (long)animation.startFrame + animation.frameCount;

                // Item #10 optional timing/playback fields. frameDurationsMs
                // must cover every frame exactly once; entry 0 is the "inherit
                // fps" sentinel. The loop range is animation-local (relative to
                // startFrame) and must stay inside the frame count.
                if (animation.frameDurationsMs != null)
                {
                    if (animation.frameDurationsMs.Count != animation.frameCount)
                        return AtlasMetadataError.AnimationFrameDurations;
                    for (int durationIndex = 0; durationIndex < animation.frameDurationsMs.Count; durationIndex++)
                    {
                        int duration = animation.frameDurationsMs[durationIndex];
                        if (duration < BackendConfig.Exporter.InheritFrameDurationMs ||
                            duration > BackendConfig.Exporter.MaxFrameDurationMs)
                            return AtlasMetadataError.AnimationFrameDurations;
                    }
                }
                if (animation.loopStart.HasValue &&
                    (animation.loopStart.Value < 0 || animation.loopStart.Value >= animation.frameCount))
                    return AtlasMetadataError.AnimationLoopRange;
                if (animation.loopEnd.HasValue)
                {
                    int effectiveLoopStart = animation.loopStart ?? 0;
                    if (animation.loopEnd.Value < effectiveLoopStart ||
                        animation.loopEnd.Value >= animation.frameCount)
                        return AtlasMetadataError.AnimationLoopRange;
                }

                // Item #7 optional runtime-effect descriptors: bounded count,
                // known effect type, non-empty trigger, particleHook carries
                // the particle name, and numeric params inside their wire caps.
                if (animation.effects != null)
                {
                    if (animation.effects.Count > BackendConfig.Exporter.MaxEffectsPerAnimation)
                        return AtlasMetadataError.AnimationEffects;
                    for (int effectIndex = 0; effectIndex < animation.effects.Count; effectIndex++)
                    {
                        EffectDescriptorJson effect = animation.effects[effectIndex];
                        if (!IsKnownEffectType(effect.effectType))
                            return AtlasMetadataError.AnimationEffects;
                        if (string.IsNullOrWhiteSpace(effect.trigger))
                            return AtlasMetadataError.AnimationEffects;
                        if (effect.effectType == BackendConfig.Exporter.EffectTypeParticleHook &&
                            string.IsNullOrWhiteSpace(effect.name))
                            return AtlasMetadataError.AnimationEffects;
                        if (effect.durationMs.HasValue &&
                            (effect.durationMs.Value < BackendConfig.Exporter.MinEffectDurationMs ||
                                effect.durationMs.Value > BackendConfig.Exporter.MaxEffectDurationMs))
                            return AtlasMetadataError.AnimationEffects;
                        if (effect.strength.HasValue &&
                            (float.IsNaN(effect.strength.Value) ||
                                effect.strength.Value < BackendConfig.Exporter.MinEffectStrength ||
                                effect.strength.Value > BackendConfig.Exporter.MaxEffectStrength))
                            return AtlasMetadataError.AnimationEffects;
                    }
                }
            }
            return AtlasMetadataError.None;
        }

        private static bool IsKnownEffectType(string effectType)
        {
            return effectType == BackendConfig.Exporter.EffectTypeFlash ||
                effectType == BackendConfig.Exporter.EffectTypeTint ||
                effectType == BackendConfig.Exporter.EffectTypeParticleHook;
        }

        // Item #6: the ONE attachment-record validator, shared by the exporter
        // (throws) and the Unity importer (logs + rejects) - mirroring the
        // TryValidateAtlasMetadata pattern. A null list is legal (the member
        // is optional). Rules: subElement must be a safe file stem (it names a
        // designer-bank .efyvsub), frameIndex must be non-negative, zOrder
        // must sit inside the wire bounds, and no frame may carry more than
        // MaxAttachmentsPerFrame records. invalidIndex reports the first
        // offending entry (-1 when valid).
        public static bool TryValidateAttachments(List<AttachmentJson> attachments, out int invalidIndex)
        {
            invalidIndex = -1;
            if (attachments == null) return true;

            Dictionary<int, int> perFrameCounts = new Dictionary<int, int>();
            for (int index = 0; index < attachments.Count; index++)
            {
                AttachmentJson attachment = attachments[index];
                if (!SafePathPolicy.IsSafeFileStem(attachment.subElement) ||
                    attachment.frameIndex < 0 ||
                    attachment.zOrder < BackendConfig.Exporter.MinAttachmentZOrder ||
                    attachment.zOrder > BackendConfig.Exporter.MaxAttachmentZOrder)
                {
                    invalidIndex = index;
                    return false;
                }

                perFrameCounts.TryGetValue(attachment.frameIndex, out int frameCount);
                frameCount++;
                if (frameCount > BackendConfig.Exporter.MaxAttachmentsPerFrame)
                {
                    invalidIndex = index;
                    return false;
                }
                perFrameCounts[attachment.frameIndex] = frameCount;
            }
            return true;
        }

        // Item #5: the ONE tileset-manifest validator, shared by the exporter
        // (throws) and the Unity importer (logs + rejects) - mirroring the
        // TryValidateAtlasMetadata pattern. Rules: tileSize positive, a
        // non-empty bounded tile-name list (names non-blank and within the
        // length cap), and - when an atlas block rides alongside - the sheet
        // must slice into exactly tileSize-square frames with capacity for
        // every declared tile (list index i is FastGridMap short tile id i).
        public static bool TryValidateTilesetManifest(
            TilesetManifestJson manifest,
            AtlasMetadataJson? atlas,
            out TilesetManifestError error)
        {
            error = ClassifyTileset(manifest, atlas);
            return error == TilesetManifestError.None;
        }

        private static TilesetManifestError ClassifyTileset(TilesetManifestJson manifest, AtlasMetadataJson? atlas)
        {
            if (manifest.tileSize <= 0) return TilesetManifestError.TileSize;
            if (manifest.tiles == null) return TilesetManifestError.TilesMissing;
            if (manifest.tiles.Count <= 0 ||
                manifest.tiles.Count > BackendConfig.Exporter.MaxTilesPerTileset)
                return TilesetManifestError.TileCount;
            for (int index = 0; index < manifest.tiles.Count; index++)
            {
                string name = manifest.tiles[index];
                if (string.IsNullOrWhiteSpace(name) ||
                    name.Length > BackendConfig.Exporter.MaxTileNameLength)
                    return TilesetManifestError.TileName;
            }

            if (atlas.HasValue)
            {
                AtlasMetadataJson metadata = atlas.Value;
                if (metadata.frameWidth != manifest.tileSize ||
                    metadata.frameHeight != manifest.tileSize)
                    return TilesetManifestError.AtlasFrameMismatch;
                long capacity = ((long)metadata.atlasWidth / metadata.frameWidth) *
                    (metadata.atlasHeight / metadata.frameHeight);
                if (manifest.tiles.Count > capacity) return TilesetManifestError.AtlasCapacity;
            }
            return TilesetManifestError.None;
        }

        private static void ValidateMetadata(AtlasMetadataJson metadata, int atlasWidth, int atlasHeight)
        {
            if (TryValidateAtlasMetadata(metadata, atlasWidth, atlasHeight, out AtlasMetadataError error)) return;

            switch (error)
            {
                case AtlasMetadataError.FormatVersion:
                case AtlasMetadataError.FrameDimensions:
                case AtlasMetadataError.AtlasDimensions:
                case AtlasMetadataError.AtlasLimit:
                case AtlasMetadataError.AnimationFps:
                case AtlasMetadataError.AnimationStartFrame:
                case AtlasMetadataError.AnimationFrameCount:
                case AtlasMetadataError.AnimationFrameDurations:
                case AtlasMetadataError.AnimationLoopRange:
                    throw new ArgumentOutOfRangeException(nameof(metadata), error.ToString());
                case AtlasMetadataError.AnimationsMissing:
                    throw new ArgumentNullException(nameof(metadata), error.ToString());
                default:
                    throw new ArgumentException(error.ToString(), nameof(metadata));
            }
        }

        private static void WriteJson(
            string path,
            string targetAssetType,
            string baseAssetType,
            Dictionary<string, object> assetProperties,
            List<HitboxJson> hitboxes,
            AtlasMetadataJson? atlasMetadata,
            List<AttachmentJson> attachments,
            TilesetManifestJson? tilesetManifest)
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
                    writer.WriteNumber(
                        BackendConfig.Exporter.FieldDocumentVersion,
                        BackendConfig.Exporter.CurrentDocumentVersion);
                    writer.WriteString(BackendConfig.Exporter.FieldAssetType, targetAssetType);
                    if (baseAssetType != null)
                        writer.WriteString(BackendConfig.Exporter.FieldBaseAssetType, baseAssetType);

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

                    // Item #6: the attachments array is omitted entirely when
                    // absent/empty so attachment-free documents stay
                    // byte-identical to earlier output. flipX/flipY are written
                    // only when true (absent resolves to false on read).
                    if (attachments != null && attachments.Count > 0)
                    {
                        writer.WriteStartArray(BackendConfig.Exporter.FieldAttachments);
                        for (int i = 0; i < attachments.Count; i++)
                        {
                            AttachmentJson attachment = attachments[i];
                            writer.WriteStartObject();
                            writer.WriteNumber(BackendConfig.Exporter.FieldFrameIndex, attachment.frameIndex);
                            writer.WriteString(BackendConfig.Exporter.FieldSubElement, attachment.subElement);
                            writer.WriteNumber(BackendConfig.Exporter.FieldX, attachment.x);
                            writer.WriteNumber(BackendConfig.Exporter.FieldY, attachment.y);
                            writer.WriteNumber(BackendConfig.Exporter.FieldZOrder, attachment.zOrder);
                            if (attachment.flipX == true)
                                writer.WriteBoolean(BackendConfig.Exporter.FieldFlipX, true);
                            if (attachment.flipY == true)
                                writer.WriteBoolean(BackendConfig.Exporter.FieldFlipY, true);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }

                    // Item #5: the tileset manifest is omitted entirely when
                    // absent so non-tileset documents stay byte-identical to
                    // earlier output.
                    if (tilesetManifest.HasValue)
                    {
                        TilesetManifestJson manifest = tilesetManifest.Value;
                        writer.WriteStartObject(BackendConfig.Exporter.FieldTileset);
                        writer.WriteNumber(BackendConfig.Exporter.FieldTileSize, manifest.tileSize);
                        writer.WriteStartArray(BackendConfig.Exporter.FieldTiles);
                        for (int i = 0; i < manifest.tiles.Count; i++)
                            writer.WriteStringValue(manifest.tiles[i]);
                        writer.WriteEndArray();
                        writer.WriteEndObject();
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
                // Item #10 optional fields: written only when populated so
                // documents that do not use them stay byte-identical.
                if (animation.frameDurationsMs != null)
                {
                    writer.WriteStartArray(BackendConfig.Exporter.FieldFrameDurationsMs);
                    for (int durationIndex = 0; durationIndex < animation.frameDurationsMs.Count; durationIndex++)
                        writer.WriteNumberValue(animation.frameDurationsMs[durationIndex]);
                    writer.WriteEndArray();
                }
                if (animation.loopStart.HasValue)
                    writer.WriteNumber(BackendConfig.Exporter.FieldLoopStart, animation.loopStart.Value);
                if (animation.loopEnd.HasValue)
                    writer.WriteNumber(BackendConfig.Exporter.FieldLoopEnd, animation.loopEnd.Value);
                if (animation.pingPong.HasValue)
                    writer.WriteBoolean(BackendConfig.Exporter.FieldPingPong, animation.pingPong.Value);
                // Item #7 effect descriptors: the array is omitted entirely
                // when absent/empty so documents without effects stay
                // byte-identical to earlier output.
                if (animation.effects != null && animation.effects.Count > 0)
                {
                    writer.WriteStartArray(BackendConfig.Exporter.FieldEffects);
                    for (int effectIndex = 0; effectIndex < animation.effects.Count; effectIndex++)
                    {
                        EffectDescriptorJson effect = animation.effects[effectIndex];
                        writer.WriteStartObject();
                        writer.WriteString(BackendConfig.Exporter.FieldName, effect.name);
                        writer.WriteString(BackendConfig.Exporter.FieldEffectType, effect.effectType);
                        writer.WriteString(BackendConfig.Exporter.FieldTrigger, effect.trigger);
                        if (effect.colorRgba.HasValue)
                            writer.WriteNumber(BackendConfig.Exporter.FieldColorRgba, effect.colorRgba.Value);
                        if (effect.durationMs.HasValue)
                            writer.WriteNumber(BackendConfig.Exporter.FieldDurationMs, effect.durationMs.Value);
                        if (effect.strength.HasValue)
                            writer.WriteNumber(BackendConfig.Exporter.FieldStrength, effect.strength.Value);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
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

        // Atomic publish with bounded retry (#12): Unity holding the destination
        // during its own reimport makes the swap sporadically fail.
        internal static void PublishFile(string temporaryPath, string destinationPath)
        {
            FastIoRetry.Run(() =>
            {
                if (File.Exists(destinationPath)) File.Replace(temporaryPath, destinationPath, null);
                else File.Move(temporaryPath, destinationPath);
            });
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public static void ComputeAtlasLayout(int frameCount, int frameWidth, int frameHeight, out int columns, out int rows)
        {
            if (frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
            if (frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));

            // Near-square grid: columns is the smallest count whose square covers every
            // frame (so columns >= rows), and rows is the minimum row count for that
            // column count. Frames are placed row-major, matching the Unity importer's
            // slice order.
            columns = (int)System.Math.Ceiling(System.Math.Sqrt(frameCount));
            if (columns < 1) columns = 1;
            rows = (frameCount + columns - 1) / columns;
            _ = checked(columns * frameWidth);
            _ = checked(rows * frameHeight);
        }

        public static void GetAtlasFrameOrigin(int frameIndex, int columns, int frameWidth, int frameHeight, out int destX, out int destY)
        {
            if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            if (frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
            if (frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));
            destX = checked((frameIndex % columns) * frameWidth);
            destY = checked((frameIndex / columns) * frameHeight);
        }

        public static unsafe void ExtractFrameFromAtlas<T>(
            T[] sourceAtlas,
            int atlasWidth,
            T[] destFrame,
            int frameWidth,
            int frameHeight,
            int sourceX,
            int sourceY) where T : unmanaged
        {
            if (sourceAtlas == null) throw new ArgumentNullException(nameof(sourceAtlas));
            if (destFrame == null) throw new ArgumentNullException(nameof(destFrame));
            if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
            if (frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
            if (frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));
            if (sourceX < 0) throw new ArgumentOutOfRangeException(nameof(sourceX));
            if (sourceY < 0) throw new ArgumentOutOfRangeException(nameof(sourceY));
            if (sourceAtlas.Length == 0 || sourceAtlas.Length % atlasWidth != 0) throw new ArgumentException(null, nameof(sourceAtlas));

            int atlasHeight = sourceAtlas.Length / atlasWidth;
            int requiredDestinationPixels = checked(frameWidth * frameHeight);
            if (destFrame.Length < requiredDestinationPixels) throw new ArgumentException(null, nameof(destFrame));
            if (checked(sourceX + frameWidth) > atlasWidth) throw new ArgumentException(null, nameof(sourceX));
            if (checked(sourceY + frameHeight) > atlasHeight) throw new ArgumentException(null, nameof(sourceY));

            fixed (T* source = sourceAtlas)
            fixed (T* destination = destFrame)
            {
                long rowBytes = checked((long)frameWidth * sizeof(T));
                for (int y = 0; y < frameHeight; y++)
                {
                    T* sourceRow = source + (sourceY + y) * atlasWidth + sourceX;
                    T* destinationRow = destination + y * frameWidth;
                    Buffer.MemoryCopy(sourceRow, destinationRow, rowBytes, rowBytes);
                }
            }
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
