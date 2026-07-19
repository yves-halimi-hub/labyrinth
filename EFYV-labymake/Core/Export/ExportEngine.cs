using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVLabyMake.Core.Export
{
    public sealed class ProjectValidationException : Exception
    {
        public ProjectValidationResult Validation { get; }

        public ProjectValidationException(ProjectValidationResult validation)
        {
            Validation = validation;
        }
    }

    public sealed class ExportResult
    {
        public string MetadataPath { get; }
        public string ImagePath { get; }
        public int FrameCount { get; }
        public int HitboxCount { get; }
        public int AtlasWidth { get; }
        public int AtlasHeight { get; }
        // Item #27: whether this publish actually rewrote each artifact on disk.
        // A live republish of byte-identical content leaves the matching file
        // untouched (content-hash suppression); a metadata-only publish never
        // touches the PNG. The paths always point at the current files
        // regardless.
        public bool ImageWritten { get; }
        public bool MetadataWritten { get; }

        internal ExportResult(
            string metadataPath,
            string imagePath,
            int frameCount,
            int hitboxCount,
            int atlasWidth,
            int atlasHeight,
            bool imageWritten = true,
            bool metadataWritten = true)
        {
            MetadataPath = metadataPath;
            ImagePath = imagePath;
            FrameCount = frameCount;
            HitboxCount = hitboxCount;
            AtlasWidth = atlasWidth;
            AtlasHeight = atlasHeight;
            ImageWritten = imageWritten;
            MetadataWritten = metadataWritten;
        }
    }

    public sealed class ExportEngine
    {
        private readonly ProjectValidator validator;
        // Item #6: resolves attachment sub-element names to pixels so they can
        // be FLATTENED into the atlas. Optional: without a resolver (or for a
        // name it cannot resolve) the attachment is skipped in the pixels but
        // still emitted as structured metadata - flattening is best-effort by
        // design so a missing bank file never breaks the live-debug loop.
        private readonly ISubElementResolver subElementResolver;
        // Item #27: remembers the content hash last published to each artifact
        // path so a live cycle producing byte-identical PNG or .efyvlaby output
        // skips that file's publish entirely - the expensive downstream Unity
        // re-import only fires when the bytes actually changed. Per artifact and
        // per path, so a directional export's four facings suppress
        // independently. One cache per engine (the live-debug loop holds one),
        // so a fresh engine always publishes on its first cycle.
        private readonly LivePublishCache publishCache = new LivePublishCache();

        public ExportEngine()
            : this(new ProjectValidator(new AssetSchemaService()))
        {
        }

        public ExportEngine(ProjectValidator validator)
            : this(validator, null)
        {
        }

        public ExportEngine(ProjectValidator validator, ISubElementResolver subElementResolver)
        {
            if (validator == null) throw new ArgumentNullException(nameof(validator));
            this.validator = validator;
            this.subElementResolver = subElementResolver;
        }

        public void PushToUnityLiveHook(EFYVProject project)
        {
            Export(project, CancellationToken.None);
        }

        // Item #33: for a linked directional project, ONE export publishes all
        // four facings (each as its own suffixed .png/.efyvlaby pair via the
        // existing facing-suffix convention) and the returned result is the
        // ACTIVE facing's pair. Plain projects export exactly as before.
        public ExportResult Export(EFYVProject project, CancellationToken cancellationToken)
        {
            ProjectValidationResult validation = validator.Validate(project, ProjectValidationScope.Export);
            if (!validation.IsValid) throw new ProjectValidationException(validation);
            cancellationToken.ThrowIfCancellationRequested();
            if (project.Directional == null)
                return Export(ProjectSnapshot.Capture(project), cancellationToken);

            ExportResult activeResult = null;
            foreach (ExportResult result in ExportValidatedFacings(project, cancellationToken))
            {
                activeResult = result;
            }
            return activeResult;
        }

        // Item #33: exports every facing of a linked directional project and
        // returns the per-facing results with the ACTIVE facing LAST (so the
        // freshest publish is the one the designer is looking at). Each
        // facing's pair publishes atomically on its own; cancellation between
        // facings leaves the already-published pairs in place. Throws
        // InvalidOperationException for non-directional projects.
        public IReadOnlyList<ExportResult> ExportAllFacings(
            EFYVProject project,
            CancellationToken cancellationToken)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (project.Directional == null) throw new InvalidOperationException();
            ProjectValidationResult validation = validator.Validate(project, ProjectValidationScope.Export);
            if (!validation.IsValid) throw new ProjectValidationException(validation);
            cancellationToken.ThrowIfCancellationRequested();

            var results = new List<ExportResult>(Config.Entity.DirectionalVariantCount);
            foreach (ExportResult result in ExportValidatedFacings(project, cancellationToken))
                results.Add(result);
            return results;
        }

        // Shared post-validation facing loop: inactive facings in the shared
        // catalog order first, the active facing last.
        private IEnumerable<ExportResult> ExportValidatedFacings(
            EFYVProject project,
            CancellationToken cancellationToken)
        {
            string activeFacing = project.Directional.ActiveFacing;
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (string.Equals(facing, activeFacing, StringComparison.Ordinal)) continue;
                yield return Export(ProjectSnapshot.CaptureFacing(project, facing), cancellationToken);
            }
            yield return Export(ProjectSnapshot.CaptureFacing(project, activeFacing), cancellationToken);
        }

        public ExportResult Export(ProjectSnapshot snapshot, CancellationToken cancellationToken)
        {
            return Export(snapshot, cancellationToken, false);
        }

        // Item #27: preferMetadataOnly lets the live-debug loop publish only the
        // .efyvlaby when it knows the edit scope since the last publish changed
        // no exported pixels and no atlas layout (a hitbox nudge, a property or
        // playback-tag tweak). The PNG is then never re-packed or re-encoded.
        // The hint is honoured only when the sibling PNG already exists on disk;
        // otherwise the export falls back to a full publish, so a first-ever
        // export can never leave the sheet missing regardless of the caller's
        // scope bookkeeping. A full publish additionally suppresses either
        // artifact whose bytes match what was last published (content hashing),
        // so a mis-tagged pixel edit still re-encodes but never needlessly
        // rewrites byte-identical output.
        public ExportResult Export(
            ProjectSnapshot snapshot,
            CancellationToken cancellationToken,
            bool preferMetadataOnly)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.TotalFrameCount <= Config.Export.InitialFrameCount)
                throw new InvalidOperationException();

            cancellationToken.ThrowIfCancellationRequested();

            EFYVBackend.Core.Export.FastExporter.ComputeAtlasLayout(
                snapshot.TotalFrameCount,
                snapshot.CanvasWidth,
                snapshot.CanvasHeight,
                out int atlasColumns,
                out int atlasRows);
            int atlasWidth = checked(snapshot.CanvasWidth * atlasColumns);
            int atlasHeight = checked(snapshot.CanvasHeight * atlasRows);

            // The snapshot overload bypasses ProjectValidator, so it must enforce
            // the shared Unity texture caps itself (#16b) before allocating the
            // atlas; oversized exports would only fail later inside Unity.
            if (atlasWidth > Config.Export.MaxAtlasDimension ||
                atlasHeight > Config.Export.MaxAtlasDimension ||
                (long)atlasWidth * atlasHeight > Config.Export.MaxAtlasPixelCount)
            {
                throw new InvalidOperationException(
                    "Atlas " + atlasWidth + "x" + atlasHeight + " exceeds the export caps.");
            }

            string baseAssetType = ResolveBaseAssetType(snapshot.TargetAssetType);

            if (preferMetadataOnly && PublishedImageExists(snapshot))
            {
                var metadataHitboxes = new List<HitboxJson>(snapshot.TotalHitboxCount);
                var metadataAttachments = new List<EFYVBackend.Core.Models.AttachmentJson>();
                CollectFrameRecords(snapshot, metadataHitboxes, metadataAttachments, cancellationToken);
                return WriteAtomically(
                    snapshot,
                    baseAssetType,
                    metadataHitboxes,
                    metadataAttachments,
                    null,
                    atlasWidth,
                    atlasHeight,
                    false,
                    cancellationToken);
            }

            PixelColor[] atlasData = new PixelColor[checked(atlasWidth * atlasHeight)];
            var hitboxes = new List<HitboxJson>(snapshot.TotalHitboxCount);
            var attachments = new List<EFYVBackend.Core.Models.AttachmentJson>();
            var resolvedElements = new Dictionary<string, SubElement>(StringComparer.Ordinal);

            int globalFrameIndex = Config.Export.InitialFrameIndex;
            foreach (var animation in snapshot.Animations)
            {
                foreach (var frame in animation.Frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var hitbox in frame.Hitboxes)
                    {
                        hitboxes.Add(new HitboxJson
                        {
                            frameIndex = globalFrameIndex,
                            hitboxType = hitbox.Key,
                            x = hitbox.X,
                            y = hitbox.Y,
                            width = hitbox.Width,
                            height = hitbox.Height
                        });
                    }

                    // Item #6: attachments export BOTH ways - flattened into
                    // the frame pixels below (so the game's atlas needs no
                    // dynamic compositing) AND as structured records for
                    // future dynamic consumers. Records keep authored list
                    // order; zOrder is the layering key. flipX/flipY are
                    // emitted only when true (absent reads as false).
                    foreach (AttachmentSnapshot attachment in frame.Attachments)
                    {
                        attachments.Add(new EFYVBackend.Core.Models.AttachmentJson
                        {
                            frameIndex = globalFrameIndex,
                            subElement = attachment.SubElementName,
                            x = attachment.X,
                            y = attachment.Y,
                            zOrder = attachment.ZOrder,
                            flipX = attachment.FlipX ? true : (bool?)null,
                            flipY = attachment.FlipY ? true : (bool?)null
                        });
                    }

                    EFYVBackend.Core.Export.FastExporter.GetAtlasFrameOrigin(
                        globalFrameIndex,
                        atlasColumns,
                        snapshot.CanvasWidth,
                        snapshot.CanvasHeight,
                        out int frameDestinationX,
                        out int frameDestinationY);
                    EFYVBackend.Core.Export.FastExporter.PackFramesToAtlas(
                        atlasData,
                        atlasWidth,
                        ComposeFramePixels(frame, resolvedElements),
                        snapshot.CanvasWidth,
                        snapshot.CanvasHeight,
                        frameDestinationX,
                        frameDestinationY);
                    globalFrameIndex++;
                }
            }

            return WriteAtomically(
                snapshot,
                baseAssetType,
                hitboxes,
                attachments,
                atlasData,
                atlasWidth,
                atlasHeight,
                true,
                cancellationToken);
        }

        // Item #27: collects the frame hitbox and attachment records in exactly
        // the global-frame-index order the full pack loop uses, WITHOUT packing
        // any pixels - the metadata-only publish path needs the wire records but
        // never touches the atlas.
        private static void CollectFrameRecords(
            ProjectSnapshot snapshot,
            List<HitboxJson> hitboxes,
            List<EFYVBackend.Core.Models.AttachmentJson> attachments,
            CancellationToken cancellationToken)
        {
            int globalFrameIndex = Config.Export.InitialFrameIndex;
            foreach (var animation in snapshot.Animations)
            {
                foreach (var frame in animation.Frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var hitbox in frame.Hitboxes)
                    {
                        hitboxes.Add(new HitboxJson
                        {
                            frameIndex = globalFrameIndex,
                            hitboxType = hitbox.Key,
                            x = hitbox.X,
                            y = hitbox.Y,
                            width = hitbox.Width,
                            height = hitbox.Height
                        });
                    }
                    foreach (AttachmentSnapshot attachment in frame.Attachments)
                    {
                        attachments.Add(new EFYVBackend.Core.Models.AttachmentJson
                        {
                            frameIndex = globalFrameIndex,
                            subElement = attachment.SubElementName,
                            x = attachment.X,
                            y = attachment.Y,
                            zOrder = attachment.ZOrder,
                            flipX = attachment.FlipX ? true : (bool?)null,
                            flipY = attachment.FlipY ? true : (bool?)null
                        });
                    }
                    globalFrameIndex++;
                }
            }
        }

        // Item #27: whether the facing-suffixed PNG this snapshot would publish
        // already exists, gating the metadata-only fast path. A snapshot without
        // a resolvable identity returns false so the full path raises the proper
        // identity error instead of this silently swallowing it.
        private bool PublishedImageExists(ProjectSnapshot snapshot)
        {
            if (!TryResolveExportStem(snapshot, out _, out string exportStem)) return false;
            string rawArtDirectory = Path.Combine(
                snapshot.UnityProjectPath,
                Config.Export.DirAssets,
                Config.Export.DirRawArt);
            return File.Exists(Path.Combine(
                rawArtDirectory,
                exportStem + BackendConfig.Exporter.PngExtension));
        }

        // --- Item #5: tileset + map publication ---------------------------------------

        // Publishes the project's tileset section as a tile-sheet .efyvlaby:
        // the tiles pack into a near-square atlas (one TileSize-square frame
        // per tile, row-major - tile id order), the atlas block declares ONE
        // animation covering exactly tileCount frames (so Unity slices one
        // sprite per tile), and the tile-ID manifest ({tileSize, tiles})
        // rides as the documentVersion-5 "tileset" block. Identity, path
        // safety, and the shared manifest/atlas validators are enforced by
        // FastExporter, which also owns the atomic publish.
        public ExportResult ExportTileset(EFYVProject project, CancellationToken cancellationToken)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            TilesetSection tileset = project.Tileset ?? throw new InvalidOperationException();
            if (tileset.Tiles.Count <= Config.Common.EmptyCount) throw new InvalidOperationException();
            if (string.IsNullOrWhiteSpace(project.UnityProjectPath)) throw new InvalidOperationException();
            cancellationToken.ThrowIfCancellationRequested();

            int tileSize = tileset.TileSize;
            int tileCount = tileset.Tiles.Count;
            EFYVBackend.Core.Export.FastExporter.ComputeAtlasLayout(
                tileCount,
                tileSize,
                tileSize,
                out int atlasColumns,
                out int atlasRows);
            int atlasWidth = checked(tileSize * atlasColumns);
            int atlasHeight = checked(tileSize * atlasRows);
            if (atlasWidth > Config.Export.MaxAtlasDimension ||
                atlasHeight > Config.Export.MaxAtlasDimension ||
                (long)atlasWidth * atlasHeight > Config.Export.MaxAtlasPixelCount)
            {
                throw new InvalidOperationException(
                    "Tile sheet " + atlasWidth + "x" + atlasHeight + " exceeds the export caps.");
            }

            var atlasData = new uint[checked(atlasWidth * atlasHeight)];
            var tileNames = new List<string>(tileCount);
            for (int index = Config.Common.FirstIndex; index < tileCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TilesetTile tile = tileset.Tiles[index] ?? throw new InvalidOperationException();
                tileNames.Add(tile.Name);
                EFYVBackend.Core.Export.FastExporter.GetAtlasFrameOrigin(
                    index,
                    atlasColumns,
                    tileSize,
                    tileSize,
                    out int destinationX,
                    out int destinationY);
                EFYVBackend.Core.Export.FastExporter.PackFramesToAtlas(
                    atlasData,
                    atlasWidth,
                    tile.Pixels,
                    tileSize,
                    tileSize,
                    destinationX,
                    destinationY);
            }

            var atlasMetadata = new AtlasMetadataJson
            {
                formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                frameWidth = tileSize,
                frameHeight = tileSize,
                atlasWidth = atlasWidth,
                atlasHeight = atlasHeight,
                animations = new List<AnimationMetadataJson>
                {
                    new AnimationMetadataJson
                    {
                        name = Config.Export.TilesetAnimationName,
                        fps = Config.Export.TilesetAnimationFps,
                        startFrame = Config.Common.FirstIndex,
                        frameCount = tileCount
                    }
                }
            };
            var manifest = new EFYVBackend.Core.Models.TilesetManifestJson
            {
                tileSize = tileSize,
                tiles = tileNames
            };

            string rawArtDirectory = Path.Combine(
                project.UnityProjectPath,
                Config.Export.DirAssets,
                Config.Export.DirRawArt);
            var properties = new Dictionary<string, object>(project.AssetProperties, StringComparer.Ordinal);
            cancellationToken.ThrowIfCancellationRequested();
            EFYVBackend.Core.Export.FastExporter.PushToUnityLiveHook(
                rawArtDirectory,
                project.TargetAssetType,
                properties,
                new List<HitboxJson>(),
                atlasData,
                atlasWidth,
                atlasHeight,
                atlasMetadata,
                ResolveBaseAssetType(project.TargetAssetType),
                null,
                manifest);

            string stem = ResolveIdentityStem(properties);
            return new ExportResult(
                Path.Combine(rawArtDirectory, stem + BackendConfig.Exporter.EfyvExtension),
                Path.Combine(rawArtDirectory, stem + BackendConfig.Exporter.PngExtension),
                tileCount,
                Config.Common.EmptyCount,
                atlasWidth,
                atlasHeight);
        }

        // Publishes the project's map section as a versioned .efyvmap binary
        // (FastMapExporter envelope + atomic publish) into Assets/RawArt.
        // Returns the published path. The map's identity is its MapId stem.
        public string ExportMap(EFYVProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            MapSection map = project.Map ?? throw new InvalidOperationException();
            if (string.IsNullOrWhiteSpace(project.UnityProjectPath)) throw new InvalidOperationException();

            string rawArtDirectory = Path.Combine(
                project.UnityProjectPath,
                Config.Export.DirAssets,
                Config.Export.DirRawArt);
            Directory.CreateDirectory(rawArtDirectory);

            var props = new EFYVBackend.Core.IO.MapPropRecord[map.Grid.Props.Count];
            for (int index = Config.Common.FirstIndex; index < props.Length; index++)
            {
                EFYVBackend.Core.Collections.FastGridMap.MapPropData prop = map.Grid.Props[index];
                props[index] = new EFYVBackend.Core.IO.MapPropRecord
                {
                    AssetKey = prop.AssetKey,
                    X = prop.X,
                    Y = prop.Y,
                    Scale = prop.Scale
                };
            }
            var data = new EFYVBackend.Core.IO.MapFileData
            {
                Width = map.Grid.Width,
                Height = map.Grid.Height,
                TilesetName = map.TilesetName,
                Tiles = (short[])map.Grid.RawData.Clone(),
                Props = props
            };

            string destinationPath = Path.Combine(
                rawArtDirectory,
                map.MapId + EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.MapFile.Extension);
            EFYVBackend.Core.IO.FastMapExporter.Export(destinationPath, data);
            return destinationPath;
        }

        // Identity precedence mirrors FastExporter (entityName, then assetName).
        private static string ResolveIdentityStem(Dictionary<string, object> properties)
        {
            object identityValue;
            if (!properties.TryGetValue(BackendConfig.Exporter.FieldEntityName, out identityValue) ||
                identityValue == null)
            {
                properties.TryGetValue(BackendConfig.Exporter.FieldAssetName, out identityValue);
            }
            return Convert.ToString(identityValue, System.Globalization.CultureInfo.InvariantCulture);
        }

        // Returns the frame's pixels with every RESOLVABLE attachment blended
        // on top (ascending zOrder, ties keep authored order); the snapshot's
        // own pixel buffer is never mutated. Frames without attachments (or
        // without any resolvable one) return the snapshot buffer unchanged.
        private PixelColor[] ComposeFramePixels(
            FrameSnapshot frame,
            Dictionary<string, SubElement> resolvedElements)
        {
            if (frame.Attachments.Count == Config.Common.EmptyCount) return frame.Pixels;

            // Stable z-order: sort indices by (zOrder, authored index).
            var order = new List<int>(frame.Attachments.Count);
            for (int index = Config.Common.FirstIndex; index < frame.Attachments.Count; index++)
                order.Add(index);
            order.Sort((left, right) =>
            {
                int compared = frame.Attachments[left].ZOrder.CompareTo(frame.Attachments[right].ZOrder);
                return compared != Config.Common.EmptyCount ? compared : left.CompareTo(right);
            });

            PixelColor[] composed = null;
            foreach (int index in order)
            {
                AttachmentSnapshot attachment = frame.Attachments[index];
                SubElement element = ResolveSubElement(attachment.SubElementName, resolvedElements);
                if (element == null) continue;
                if (composed == null)
                {
                    composed = new PixelColor[frame.Pixels.Length];
                    Array.Copy(frame.Pixels, composed, frame.Pixels.Length);
                }
                CompositeAttachment(composed, frame.Width, frame.Height, element, attachment);
            }
            return composed ?? frame.Pixels;
        }

        private SubElement ResolveSubElement(
            string name,
            Dictionary<string, SubElement> resolvedElements)
        {
            if (resolvedElements.TryGetValue(name, out SubElement cached)) return cached;
            SubElement element = null;
            if (subElementResolver != null &&
                subElementResolver.TryResolveSubElement(name, out SubElement resolved))
                element = resolved;
            // Negative results are cached too: one unresolved name costs one
            // lookup per export, not one per frame.
            resolvedElements[name] = element;
            return element;
        }

        // Alpha-blends the sub-element onto the frame pixels with the
        // attachment's flips applied and its PIVOT (in flipped space) landing
        // on (attachment.X, attachment.Y); off-canvas parts clip. Blending is
        // FastMemory.BlendColor - the exact math the legacy bake-pixels stamp
        // uses, so an attachment flattens identically to baking it.
        internal static void CompositeAttachment(
            PixelColor[] destination,
            int destinationWidth,
            int destinationHeight,
            SubElement element,
            AttachmentSnapshot attachment)
        {
            int pivotX = attachment.FlipX
                ? element.Width - Config.Common.UnitCount - element.PivotX
                : element.PivotX;
            int pivotY = attachment.FlipY
                ? element.Height - Config.Common.UnitCount - element.PivotY
                : element.PivotY;
            int originX = attachment.X - pivotX;
            int originY = attachment.Y - pivotY;

            for (int localY = Config.Common.FirstIndex; localY < element.Height; localY++)
            {
                int destinationY = originY + localY;
                if (destinationY < Config.Canvas.MinCoordinate || destinationY >= destinationHeight)
                    continue;
                int sourceY = attachment.FlipY
                    ? element.Height - Config.Common.UnitCount - localY
                    : localY;
                int sourceRow = sourceY * element.Width;
                int destinationRow = destinationY * destinationWidth;
                for (int localX = Config.Common.FirstIndex; localX < element.Width; localX++)
                {
                    int destinationX = originX + localX;
                    if (destinationX < Config.Canvas.MinCoordinate || destinationX >= destinationWidth)
                        continue;
                    int sourceX = attachment.FlipX
                        ? element.Width - Config.Common.UnitCount - localX
                        : localX;
                    uint source = element.Pixels[sourceRow + sourceX];
                    if ((byte)(source >> Config.Color.AlphaShift) == Config.Layer.TransparentAlpha)
                        continue;
                    EFYVBackend.Core.Memory.FastMemory.BlendColor(
                        ref destination[destinationRow + destinationX].Rgba,
                        source);
                }
            }
        }

        // The registered base type travels inside the .efyvlaby (#16e) so the
        // Unity importer can fall back to the base factory for custom types it
        // has no concrete class for. Unknown types simply omit the field.
        private string ResolveBaseAssetType(string targetAssetType)
        {
            SchemaDefinition definition;
            return validator.SchemaService.TryGetTypeDefinition(targetAssetType, out definition)
                ? definition.BaseAssetType
                : null;
        }

        private ExportResult WriteAtomically(
            ProjectSnapshot snapshot,
            string baseAssetType,
            List<HitboxJson> hitboxes,
            List<EFYVBackend.Core.Models.AttachmentJson> attachments,
            PixelColor[] atlasData,
            int atlasWidth,
            int atlasHeight,
            bool writeImage,
            CancellationToken cancellationToken)
        {
            string rawArtDirectory = Path.Combine(
                snapshot.UnityProjectPath,
                Config.Export.DirAssets,
                Config.Export.DirRawArt);
            Directory.CreateDirectory(rawArtDirectory);

            string stagingDirectory = Path.Combine(snapshot.UnityProjectPath, Path.GetRandomFileName());
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                var animationMetadata = new List<AnimationMetadataJson>(snapshot.Animations.Count);
                foreach (var animation in snapshot.Animations)
                {
                    animationMetadata.Add(BuildAnimationMetadata(animation));
                }
                var atlasMetadata = new AtlasMetadataJson
                {
                    formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                    frameWidth = snapshot.CanvasWidth,
                    frameHeight = snapshot.CanvasHeight,
                    atlasWidth = atlasWidth,
                    atlasHeight = atlasHeight,
                    animations = animationMetadata
                };
                List<EFYVBackend.Core.Models.AttachmentJson> emittedAttachments =
                    attachments.Count > Config.Common.EmptyCount ? attachments : null;

                if (writeImage)
                {
                    EFYVBackend.Core.Export.FastExporter.PushToUnityLiveHook(
                        stagingDirectory,
                        snapshot.TargetAssetType,
                        snapshot.CopyAssetProperties(),
                        hitboxes,
                        atlasData,
                        atlasWidth,
                        atlasHeight,
                        atlasMetadata,
                        baseAssetType,
                        emittedAttachments);
                }
                else
                {
                    // Item #27 metadata-only: stage just the .efyvlaby; the PNG
                    // already on disk stays as published.
                    EFYVBackend.Core.Export.FastExporter.PushMetadataOnlyToUnityLiveHook(
                        stagingDirectory,
                        snapshot.TargetAssetType,
                        snapshot.CopyAssetProperties(),
                        hitboxes,
                        atlasWidth,
                        atlasHeight,
                        atlasMetadata,
                        baseAssetType,
                        emittedAttachments);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Identity precedence mirrors FastExporter (entityName first, then
                // assetName); a snapshot without either was already REJECTED by the
                // FastExporter call above (#36) - no silent fallback stem exists.
                if (!TryResolveExportStem(snapshot, out string entityName, out string exportStem))
                {
                    throw new InvalidOperationException("Export requires an entityName or assetName identity property.");
                }
                string stagedMetadataPath = Path.Combine(
                    stagingDirectory,
                    entityName + BackendConfig.Exporter.EfyvExtension);
                string stagedImagePath = Path.Combine(
                    stagingDirectory,
                    entityName + BackendConfig.Exporter.PngExtension);
                string metadataPath = Path.Combine(
                    rawArtDirectory,
                    exportStem + BackendConfig.Exporter.EfyvExtension);
                string imagePath = Path.Combine(
                    rawArtDirectory,
                    exportStem + BackendConfig.Exporter.PngExtension);

                // Item #27 content-hash suppression: a staged artifact whose
                // bytes match what this engine last published to that path (and
                // which is still on disk) is not re-published - the downstream
                // Unity re-import only fires for a real change. A hitbox nudge
                // rewrites the .efyvlaby but leaves the byte-identical PNG alone.
                byte[] metadataBytes = File.ReadAllBytes(stagedMetadataPath);
                bool metadataChanged = publishCache.ShouldPublish(metadataPath, metadataBytes, out uint metadataCrc);

                byte[] imageBytes = null;
                uint imageCrc = default;
                bool imageChanged = false;
                if (writeImage)
                {
                    imageBytes = File.ReadAllBytes(stagedImagePath);
                    imageChanged = publishCache.ShouldPublish(imagePath, imageBytes, out imageCrc);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (imageChanged && metadataChanged)
                {
                    // The metadata is authoritative; publish the image first and
                    // metadata last (the existing atomic pair with rollback).
                    PublishPair(stagedImagePath, imagePath, stagedMetadataPath, metadataPath);
                    publishCache.Record(imagePath, imageCrc, imageBytes.Length);
                    publishCache.Record(metadataPath, metadataCrc, metadataBytes.Length);
                }
                else
                {
                    // A suppressed artifact is provably already current on disk,
                    // so publishing only the changed one keeps the pair coherent.
                    if (imageChanged)
                    {
                        PublishSingle(stagedImagePath, imagePath);
                        publishCache.Record(imagePath, imageCrc, imageBytes.Length);
                    }
                    if (metadataChanged)
                    {
                        PublishSingle(stagedMetadataPath, metadataPath);
                        publishCache.Record(metadataPath, metadataCrc, metadataBytes.Length);
                    }
                }

                return new ExportResult(
                    metadataPath,
                    imagePath,
                    snapshot.TotalFrameCount,
                    snapshot.TotalHitboxCount,
                    atlasWidth,
                    atlasHeight,
                    imageWritten: imageChanged,
                    metadataWritten: metadataChanged);
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                {
                    try { Directory.Delete(stagingDirectory, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        // Identity precedence mirrors FastExporter (entityName first, then
        // assetName). Returns false when a snapshot carries neither, so callers
        // can fall back to the full path (whose FastExporter call raises the
        // proper #36 identity error) instead of guessing a stem.
        private static bool TryResolveExportStem(
            ProjectSnapshot snapshot,
            out string entityName,
            out string exportStem)
        {
            entityName = null;
            exportStem = null;
            object identityValue;
            if (!snapshot.AssetProperties.TryGetValue(BackendConfig.Exporter.FieldEntityName, out identityValue) &&
                !snapshot.AssetProperties.TryGetValue(BackendConfig.Exporter.FieldAssetName, out identityValue))
            {
                return false;
            }
            entityName = Convert.ToString(identityValue, System.Globalization.CultureInfo.InvariantCulture);
            exportStem = entityName + GetFacingFileSuffix(snapshot);
            return true;
        }

        // Single-artifact atomic publish (with the same bounded IOException
        // retry PublishPair uses), for when content-hash suppression leaves only
        // one of the pair to publish.
        private static void PublishSingle(string stagedPath, string destinationPath)
        {
            string directory = Path.GetDirectoryName(stagedPath);
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException(null, nameof(stagedPath));
            string backupPath = Path.Combine(directory, Path.GetRandomFileName());
            bool hadOriginal = File.Exists(destinationPath);
            try
            {
                PublishWithBackup(stagedPath, destinationPath, backupPath, hadOriginal);
            }
            finally
            {
                DeleteIfPresent(backupPath);
            }
        }

        // Item #10: the optional timing/playback fields ride along ONLY when
        // they differ from their defaults, so exports that do not use the
        // features stay byte-identical to documentVersion-1-era output (fps
        // remains the fallback for every reader). The loop range is written
        // CLAMPED (via the snapshot's effective accessors) because the model
        // values may be stale after frame removals.
        private static AnimationMetadataJson BuildAnimationMetadata(AnimationSnapshot animation)
        {
            var metadata = new AnimationMetadataJson
            {
                name = animation.StateName,
                fps = animation.FPS,
                startFrame = animation.StartFrame,
                frameCount = animation.Frames.Count
            };

            bool hasDurationOverride = false;
            foreach (FrameSnapshot frame in animation.Frames)
            {
                if (frame.DurationMs != Config.Animation.InheritFrameDurationMs)
                {
                    hasDurationOverride = true;
                    break;
                }
            }
            if (hasDurationOverride)
            {
                var durations = new List<int>(animation.Frames.Count);
                foreach (FrameSnapshot frame in animation.Frames) durations.Add(frame.DurationMs);
                metadata.frameDurationsMs = durations;
            }

            int lastFrame = animation.Frames.Count - Config.Common.UnitCount;
            int effectiveLoopStart = animation.EffectiveLoopStart;
            int effectiveLoopEnd = animation.EffectiveLoopEnd;
            if (effectiveLoopStart != Config.Animation.DefaultLoopStartFrame)
                metadata.loopStart = effectiveLoopStart;
            if (animation.Frames.Count > Config.Common.EmptyCount && effectiveLoopEnd != lastFrame)
                metadata.loopEnd = effectiveLoopEnd;
            if (animation.PingPong) metadata.pingPong = true;

            // Item #7 authored effect descriptors ride along only when the
            // animation has any (the writer omits an absent list), keeping
            // effect-free exports byte-identical to earlier output. Designer
            // exports always populate every descriptor field.
            if (animation.Effects.Count > Config.Common.EmptyCount)
            {
                var effects = new List<EffectDescriptorJson>(animation.Effects.Count);
                foreach (EffectDescriptor effect in animation.Effects)
                {
                    effects.Add(new EffectDescriptorJson
                    {
                        name = effect.Name,
                        effectType = effect.EffectType,
                        trigger = effect.Trigger,
                        colorRgba = effect.ColorRgba,
                        durationMs = effect.DurationMs,
                        strength = effect.Strength
                    });
                }
                metadata.effects = effects;
            }
            return metadata;
        }

        private static string GetFacingFileSuffix(ProjectSnapshot snapshot)
        {
            object facingValue;
            if (!snapshot.AssetProperties.TryGetValue(Config.Entity.KeyFacing, out facingValue))
                return Config.Common.EmptyString;

            string facing = facingValue as string;
            if (facing == Config.Entity.FacingUp) return Config.Entity.FileSuffixUp;
            if (facing == Config.Entity.FacingDown) return Config.Entity.FileSuffixDown;
            if (facing == Config.Entity.FacingLeft) return Config.Entity.FileSuffixLeft;
            if (facing == Config.Entity.FacingRight) return Config.Entity.FileSuffixRight;
            return Config.Common.EmptyString;
        }

        internal static void AtomicReplace(string sourcePath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(sourcePath, destinationPath);
                return;
            }

            string backupPath = Path.Combine(
                Path.GetDirectoryName(destinationPath),
                Path.GetRandomFileName());
            try
            {
                File.Replace(sourcePath, destinationPath, backupPath);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceWithRollback(sourcePath, destinationPath, backupPath);
            }
            finally
            {
                if (File.Exists(backupPath))
                {
                    try { File.Delete(backupPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        internal static void PublishPair(
            string stagedImagePath,
            string imagePath,
            string stagedMetadataPath,
            string metadataPath)
        {
            string imageStagingDirectory = Path.GetDirectoryName(stagedImagePath);
            string metadataStagingDirectory = Path.GetDirectoryName(stagedMetadataPath);
            if (string.IsNullOrEmpty(imageStagingDirectory) || string.IsNullOrEmpty(metadataStagingDirectory))
                throw new ArgumentException();

            string imageBackup = Path.Combine(imageStagingDirectory, Path.GetRandomFileName());
            string metadataBackup = Path.Combine(metadataStagingDirectory, Path.GetRandomFileName());
            bool hadImage = File.Exists(imagePath);
            bool hadMetadata = File.Exists(metadataPath);

            try
            {
                PublishWithBackup(stagedImagePath, imagePath, imageBackup, hadImage);
                try
                {
                    PublishWithBackup(stagedMetadataPath, metadataPath, metadataBackup, hadMetadata);
                }
                catch
                {
                    RestorePublishedFile(imagePath, imageBackup, hadImage);
                    RestorePublishedFile(metadataPath, metadataBackup, hadMetadata);
                    throw;
                }
            }
            finally
            {
                DeleteIfPresent(imageBackup);
                DeleteIfPresent(metadataBackup);
            }
        }

        private static void RestorePublishedFile(string destinationPath, string backupPath, bool hadOriginal)
        {
            if (hadOriginal && File.Exists(backupPath))
            {
                if (!File.Exists(destinationPath))
                {
                    File.Move(backupPath, destinationPath);
                    return;
                }

                try
                {
                    File.Replace(backupPath, destinationPath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(destinationPath);
                    File.Move(backupPath, destinationPath);
                }
            }
            else if (!hadOriginal && File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }

        // Bounded IOException retry (#12): Unity re-importing the previous
        // publish briefly holds the destination and made live pushes silently
        // fail; the shared backend helper absorbs the transient sharing window.
        private static void PublishWithBackup(
            string stagedPath,
            string destinationPath,
            string backupPath,
            bool hadOriginal)
        {
            if (!hadOriginal)
            {
                EFYVBackend.Core.IO.FastIoRetry.Run(() => File.Move(stagedPath, destinationPath));
                return;
            }

            try
            {
                EFYVBackend.Core.IO.FastIoRetry.Run(() => File.Replace(stagedPath, destinationPath, backupPath));
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceWithRollback(stagedPath, destinationPath, backupPath);
            }
        }

        private static void DeleteIfPresent(string path)
        {
            if (!File.Exists(path)) return;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void ReplaceWithRollback(string sourcePath, string destinationPath, string backupPath)
        {
            File.Move(destinationPath, backupPath);
            try
            {
                File.Move(sourcePath, destinationPath);
            }
            catch
            {
                if (!File.Exists(destinationPath) && File.Exists(backupPath))
                    File.Move(backupPath, destinationPath);
                throw;
            }
        }
    }

    // Item #27 content-hash suppression state: the last content signature this
    // engine published to each artifact path. A signature is (CRC-32, length) -
    // the length guards the astronomically-rare CRC collision so "matches" only
    // ever means byte-identical output for these small pixel-art artifacts.
    // Suppression additionally requires the destination to still exist on disk,
    // so an externally deleted file is always re-published. The hasher is the
    // shared backend FastCrc32 (no bespoke hash). Thread-safe: the live-debug
    // export runs on a task thread while the cache is read from the owning
    // engine.
    internal sealed class LivePublishCache
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, Signature> signatures =
            new Dictionary<string, Signature>(StringComparer.OrdinalIgnoreCase);

        private readonly struct Signature
        {
            public uint Crc { get; }
            public long Length { get; }

            public Signature(uint crc, long length)
            {
                Crc = crc;
                Length = length;
            }
        }

        // True when this content must be published: the path has no remembered
        // signature, the bytes differ from it, or the destination file no longer
        // exists. crc is returned so a subsequent Record need not re-hash.
        public bool ShouldPublish(string destinationPath, byte[] content, out uint crc)
        {
            crc = EFYVBackend.Core.IO.FastCrc32.Compute(content);
            lock (gate)
            {
                if (signatures.TryGetValue(destinationPath, out Signature signature) &&
                    signature.Crc == crc &&
                    signature.Length == content.Length &&
                    File.Exists(destinationPath))
                {
                    return false;
                }
            }
            return true;
        }

        // Records the signature of content just successfully published to a path
        // (called only after the publish succeeds, so a failed publish never
        // leaves the cache claiming stale bytes are on disk).
        public void Record(string destinationPath, uint crc, long length)
        {
            lock (gate) signatures[destinationPath] = new Signature(crc, length);
        }
    }
}
