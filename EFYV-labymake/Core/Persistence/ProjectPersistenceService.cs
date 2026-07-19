using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.IO;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Persistence
{
    public sealed class ProjectPersistenceSnapshot
    {
        internal ProjectDocument Document { get; }

        private ProjectPersistenceSnapshot(ProjectDocument document)
        {
            Document = document;
        }

        public static ProjectPersistenceSnapshot Capture(EFYVProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            return new ProjectPersistenceSnapshot(ProjectDocument.Capture(project));
        }
    }

    // One committed project discovered by ProjectPersistenceService.ListProjects:
    // its safe project name (the .efyvmake stem, the key every per-name op takes)
    // and the file's last-write time in UTC, for an "open existing" browser.
    public readonly struct ProjectListEntry
    {
        public string Name { get; }
        public DateTime LastWriteUtc { get; }

        internal ProjectListEntry(string name, DateTime lastWriteUtc)
        {
            Name = name;
            LastWriteUtc = lastWriteUtc;
        }
    }

    public sealed class ProjectPersistenceService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly string projectDirectory;
        private readonly string projectDirectoryPrefix;
        private readonly AssetSchemaService schemaService;

        public string ProjectDirectory => projectDirectory;

        public ProjectPersistenceService(string directoryPath)
            : this(directoryPath, new AssetSchemaService())
        {
        }

        public ProjectPersistenceService(string directoryPath, AssetSchemaService schemaService)
        {
            if (string.IsNullOrWhiteSpace(directoryPath)) throw new ArgumentException(nameof(directoryPath));
            this.schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
            projectDirectory = Path.GetFullPath(directoryPath);
            Directory.CreateDirectory(projectDirectory);
            projectDirectoryPrefix = projectDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        public string GetProjectPath(string projectName)
        {
            return ResolveProjectPath(projectName, Config.Persistence.ProjectExtension);
        }

        public string GetAutosavePath(string projectName)
        {
            return ResolveProjectPath(
                projectName,
                Config.Persistence.ProjectExtension + Config.Persistence.AutosaveSuffix);
        }

        public string SaveProject(string projectName, EFYVProject project, CancellationToken cancellationToken)
        {
            return SaveProject(projectName, ProjectPersistenceSnapshot.Capture(project), cancellationToken);
        }

        public string SaveAutosave(string projectName, EFYVProject project, CancellationToken cancellationToken)
        {
            return SaveAutosave(projectName, ProjectPersistenceSnapshot.Capture(project), cancellationToken);
        }

        public string SaveProject(
            string projectName,
            ProjectPersistenceSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return Save(GetProjectPath(projectName), snapshot.Document, cancellationToken);
        }

        public string SaveAutosave(
            string projectName,
            ProjectPersistenceSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return Save(GetAutosavePath(projectName), snapshot.Document, cancellationToken);
        }

        public EFYVProject LoadProject(string projectName)
        {
            return Load(GetProjectPath(projectName));
        }

        public EFYVProject LoadAutosave(string projectName)
        {
            return Load(GetAutosavePath(projectName));
        }

        public bool AutosaveExists(string projectName)
        {
            return File.Exists(GetAutosavePath(projectName));
        }

        // Enumerates the committed projects in this service's directory as
        // name + last-write-time pairs, sorted by name (ordinal, ignoring case).
        // Only the top level is scanned - subdirectories are ignored, never
        // recursed - and every candidate is held to the SAME safety gate the
        // per-name operations enforce: a listed name always round-trips through
        // GetProjectPath. Entries are therefore skipped (not surfaced, never
        // faulted on) when the on-disk stem is not a safe file stem, when the
        // file is an autosave sidecar (.efyvmake.autosave, whose stem would
        // otherwise collide with its own project), or when its timestamp cannot
        // be read. A missing/unreadable directory lists as empty rather than
        // throwing - the directory can be deleted out from under a long-lived
        // service. The list is a discovery aid: a returned name can still fail
        // to LoadProject if the file is corrupt (the loader stays the gate).
        public IReadOnlyList<ProjectListEntry> ListProjects()
        {
            var entries = new List<ProjectListEntry>();
            if (!Directory.Exists(projectDirectory)) return entries;

            string extension = Config.Persistence.ProjectExtension;
            string autosaveExtension = extension + Config.Persistence.AutosaveSuffix;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(projectDirectory);
            }
            catch (IOException)
            {
                return entries;
            }
            catch (UnauthorizedAccessException)
            {
                return entries;
            }

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                // An autosave sidecar ends with ".efyvmake.autosave", so it does
                // not match the project extension above - but a project literally
                // named "<x>.autosave" would, and its .efyvmake file must not be
                // mistaken for one; this guard keeps both cases straight.
                if (fileName.EndsWith(autosaveExtension, StringComparison.OrdinalIgnoreCase)) continue;

                string stem = fileName.Substring(0, fileName.Length - extension.Length);
                if (!DesignerPathPolicy.IsSafeFileStem(stem)) continue;
                if (!seenNames.Add(stem)) continue;

                DateTime lastWriteUtc;
                try
                {
                    lastWriteUtc = File.GetLastWriteTimeUtc(file);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                entries.Add(new ProjectListEntry(stem, lastWriteUtc));
            }

            entries.Sort((left, right) =>
                string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return entries;
        }

        public void DeleteAutosave(string projectName)
        {
            string path = GetAutosavePath(projectName);
            if (File.Exists(path)) File.Delete(path);
        }

        private string Save(
            string destinationPath,
            ProjectDocument document,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDocument(document);
            string directory = Path.GetDirectoryName(destinationPath);
            string temporaryPath = Path.Combine(directory, Path.GetRandomFileName());
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    JsonSerializer.Serialize(stream, document, SerializerOptions);
                    stream.Flush(true);
                }

                if (new FileInfo(temporaryPath).Length > Config.Persistence.MaxProjectFileBytes)
                    throw new InvalidDataException();

                cancellationToken.ThrowIfCancellationRequested();
                ExportEngine.AtomicReplace(temporaryPath, destinationPath);
                return destinationPath;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private EFYVProject Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > Config.Persistence.MaxProjectFileBytes) throw new InvalidDataException();
                ProjectDocument document = JsonSerializer.Deserialize<ProjectDocument>(stream, SerializerOptions);
                if (document == null || document.FormatVersion != Config.Persistence.ProjectFormatVersion)
                    throw new InvalidDataException();
                ValidateDocument(document);
                return document.Restore();
            }
        }

        private string ResolveProjectPath(string projectName, string suffix)
        {
            if (!DesignerPathPolicy.IsSafeFileStem(projectName)) throw new ArgumentException(nameof(projectName));

            string fullPath = Path.GetFullPath(Path.Combine(projectDirectory, projectName + suffix));
            if (!fullPath.StartsWith(projectDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException();
            return fullPath;
        }

        private void ValidateDocument(ProjectDocument document)
        {
            SchemaDefinition definition;
            if (document == null ||
                !schemaService.TryGetTypeDefinition(document.TargetAssetType, out definition) ||
                document.CanvasWidth <= Config.Canvas.MinCoordinate ||
                document.CanvasHeight <= Config.Canvas.MinCoordinate ||
                document.CanvasWidth > Config.Persistence.MaxCanvasDimension ||
                document.CanvasHeight > Config.Persistence.MaxCanvasDimension ||
                document.AssetProperties == null ||
                document.Animations == null ||
                document.Animations.Count > Config.Persistence.MaxAnimations)
                throw new InvalidDataException();

            ValidateAnimationDocuments(document, document.Animations);

            // Directional section (item #33). Null is LEGAL (a plain or
            // pre-directional document); a present section must carry a valid
            // active facing, NO list for the active facing (its animations
            // ARE the main Animations list), and a valid list for each of the
            // other three facings - each held to the same rules as the main
            // list.
            if (document.Directional != null)
            {
                DirectionalSectionDocument directional = document.Directional;
                if (!DirectionalState.IsFacingName(directional.ActiveFacing))
                    throw new InvalidDataException();
                foreach (string facing in Config.Schema.FacingChoices)
                {
                    List<AnimationDocument> facingAnimations = directional.GetFacingList(facing);
                    if (string.Equals(facing, directional.ActiveFacing, StringComparison.Ordinal))
                    {
                        if (facingAnimations != null) throw new InvalidDataException();
                        continue;
                    }
                    if (facingAnimations == null ||
                        facingAnimations.Count > Config.Persistence.MaxAnimations)
                        throw new InvalidDataException();
                    ValidateAnimationDocuments(document, facingAnimations);
                }
            }

            ValidatePaletteAndSectionDocuments(document);
        }

        private void ValidateAnimationDocuments(ProjectDocument document, List<AnimationDocument> animations)
        {
            long totalFrames = Config.Common.EmptyCount;
            foreach (var animation in animations)
            {
                if (animation == null || string.IsNullOrWhiteSpace(animation.StateName) ||
                    animation.FPS <= Config.Common.EmptyCount ||
                    animation.Frames == null ||
                    animation.Frames.Count > Config.Persistence.MaxFramesPerAnimation ||
                    // Item #10 playback tags: raw authoring values may be stale
                    // relative to the frame count (playback clamps), but they
                    // must at least be well-formed.
                    animation.LoopStart < Config.Common.FirstIndex ||
                    (animation.LoopEnd.HasValue &&
                        animation.LoopEnd.Value < Config.Animation.FullRangeLoopEnd))
                    throw new InvalidDataException();
                totalFrames += animation.Frames.Count;

                // Item #7 effect section: null is LEGAL (pre-effects document);
                // a present list must satisfy the same bounds the
                // EffectDescriptor constructor enforces.
                if (animation.Effects != null)
                {
                    if (animation.Effects.Count > Config.Effect.MaxEffectsPerAnimation)
                        throw new InvalidDataException();
                    foreach (var effect in animation.Effects)
                    {
                        if (effect == null ||
                            !EffectDescriptor.IsKnownEffectType(effect.EffectType) ||
                            string.IsNullOrWhiteSpace(effect.Trigger) ||
                            effect.Trigger.Length > Config.Effect.MaxTriggerLength ||
                            (effect.Name != null && effect.Name.Length > Config.Effect.MaxNameLength) ||
                            (effect.EffectType == Config.Effect.TypeParticleHook &&
                                string.IsNullOrWhiteSpace(effect.Name)) ||
                            effect.DurationMs < Config.Effect.MinDurationMs ||
                            effect.DurationMs > Config.Effect.MaxDurationMs ||
                            float.IsNaN(effect.Strength) ||
                            effect.Strength < Config.Effect.MinStrength ||
                            effect.Strength > Config.Effect.MaxStrength)
                            throw new InvalidDataException();
                    }
                }

                foreach (var frame in animation.Frames)
                {
                    if (frame == null || frame.Layers == null || frame.Hitboxes == null ||
                        frame.Layers.Count > Config.Persistence.MaxLayersPerFrame ||
                        frame.DurationMs < Config.Animation.InheritFrameDurationMs ||
                        frame.DurationMs > Config.Animation.MaxFrameDurationMs)
                        throw new InvalidDataException();

                    // Item #6 attachment section: null is LEGAL (pre-attachment
                    // document); a present list must satisfy the same bounds
                    // the SubElementAttachment constructor enforces.
                    if (frame.Attachments != null)
                    {
                        if (frame.Attachments.Count > Config.Attachment.MaxPerFrame)
                            throw new InvalidDataException();
                        foreach (var attachment in frame.Attachments)
                        {
                            if (attachment == null ||
                                !DesignerPathPolicy.IsSafeFileStem(attachment.SubElementName) ||
                                attachment.ZOrder < Config.Attachment.MinZOrder ||
                                attachment.ZOrder > Config.Attachment.MaxZOrder)
                                throw new InvalidDataException();
                        }
                    }

                    int expectedByteCount = checked(
                        document.CanvasWidth * document.CanvasHeight * Config.Color.RgbaChannelCount);
                    foreach (var layer in frame.Layers)
                    {
                        if (layer == null || layer.RgbaBytes == null ||
                            layer.RgbaBytes.Length != expectedByteCount ||
                            float.IsNaN(layer.Opacity) || float.IsInfinity(layer.Opacity))
                            throw new InvalidDataException();
                    }

                    var hitboxKeys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var hitbox in frame.Hitboxes)
                    {
                        if (hitbox == null || string.IsNullOrWhiteSpace(hitbox.Key) ||
                            !hitboxKeys.Add(hitbox.Key) ||
                            !IsFinite(hitbox.X) || !IsFinite(hitbox.Y) ||
                            !IsFinite(hitbox.Width) || !IsFinite(hitbox.Height) ||
                            hitbox.X < Config.Common.ZeroFloat ||
                            hitbox.Y < Config.Common.ZeroFloat ||
                            hitbox.Width < Config.Common.ZeroFloat ||
                            hitbox.Height < Config.Common.ZeroFloat ||
                            hitbox.X > document.CanvasWidth / Config.Hitbox.PixelsPerUnit ||
                            hitbox.Y > document.CanvasHeight / Config.Hitbox.PixelsPerUnit ||
                            hitbox.Width > (document.CanvasWidth / Config.Hitbox.PixelsPerUnit) - hitbox.X ||
                            hitbox.Height > (document.CanvasHeight / Config.Hitbox.PixelsPerUnit) - hitbox.Y)
                            throw new InvalidDataException();
                    }
                }
            }

            long atlasWidth = document.CanvasWidth * totalFrames;
            long atlasPixels = atlasWidth * document.CanvasHeight;
            if (atlasWidth > Config.Export.MaxAtlasDimension ||
                atlasPixels > Config.Export.MaxAtlasPixelCount)
                throw new InvalidDataException();
        }

        private void ValidatePaletteAndSectionDocuments(ProjectDocument document)
        {
            // Palette/recent-color section (item #8). Null sections are LEGAL:
            // they identify a pre-palette document, which restores to empty
            // palette state - the format was extended without a version bump
            // because the addition is fully optional (see ProjectDocument).
            if (document.Palettes != null)
            {
                if (document.Palettes.Count > Config.Palette.MaxPalettes)
                    throw new InvalidDataException();
                foreach (var palette in document.Palettes)
                {
                    if (palette == null || string.IsNullOrWhiteSpace(palette.Name) ||
                        palette.Name.Length > Config.Palette.MaxNameLength ||
                        palette.Colors == null ||
                        palette.Colors.Count > Config.Palette.MaxSwatchesPerPalette)
                        throw new InvalidDataException();
                }
            }

            if (document.RecentColors != null &&
                document.RecentColors.Count > Config.Palette.RecentColorCapacity)
                throw new InvalidDataException();

            // Tileset/map sections (item #5). Null sections are LEGAL: they
            // identify a pre-map document, which restores to no sections -
            // the same optional-extension rule as the palette section.
            if (document.Tileset != null)
            {
                TilesetSectionDocument tileset = document.Tileset;
                if (tileset.TileSize < Config.Tileset.MinTileSize ||
                    tileset.TileSize > Config.Tileset.MaxTileSize ||
                    tileset.Tiles == null ||
                    tileset.Tiles.Count > Config.Tileset.MaxTiles)
                    throw new InvalidDataException();
                int expectedTileBytes = checked(
                    tileset.TileSize * tileset.TileSize * Config.Color.RgbaChannelCount);
                foreach (TilesetTileDocument tile in tileset.Tiles)
                {
                    if (tile == null || string.IsNullOrWhiteSpace(tile.Name) ||
                        tile.Name.Length > Config.Tileset.MaxTileNameLength ||
                        tile.RgbaBytes == null || tile.RgbaBytes.Length != expectedTileBytes)
                        throw new InvalidDataException();
                }
            }

            if (document.Map != null)
            {
                MapSectionDocument map = document.Map;
                if (!DesignerPathPolicy.IsSafeFileStem(map.MapId) ||
                    map.Width <= Config.Common.EmptyCount ||
                    map.Width > Config.MapDocument.MaxDimension ||
                    map.Height <= Config.Common.EmptyCount ||
                    map.Height > Config.MapDocument.MaxDimension ||
                    (!string.IsNullOrEmpty(map.TilesetName) &&
                        !DesignerPathPolicy.IsSafeFileStem(map.TilesetName)) ||
                    map.TileBytes == null ||
                    map.TileBytes.LongLength !=
                        (long)map.Width * map.Height * MapSectionDocument.BytesPerTile ||
                    map.Props == null ||
                    map.Props.Count > Config.MapDocument.MaxProps)
                    throw new InvalidDataException();
                foreach (MapPropDocument prop in map.Props)
                {
                    if (prop == null || !DesignerPathPolicy.IsSafeFileStem(prop.AssetKey) ||
                        float.IsNaN(prop.Scale) || float.IsInfinity(prop.Scale))
                        throw new InvalidDataException();
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

    }

    internal sealed class ProjectDocument
    {
        public int FormatVersion { get; set; }
        public string TargetAssetType { get; set; }
        public string UnityProjectPath { get; set; }
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }
        public uint DesignerSeed { get; set; }
        public Dictionary<string, object> AssetProperties { get; set; }
        public List<AnimationDocument> Animations { get; set; }

        // Item #8 palette extension. The project format was EXTENDED, not
        // version-bumped: both sections are optional (null when absent), a
        // legacy document restores to empty palette state, and older readers
        // ignore the extra JSON members - so no existing .efyvmake is
        // invalidated by the hard-pinned FormatVersion equality check.
        // RecentColors is most-recent-first, matching RecentColorRing order.
        public List<PaletteDocument> Palettes { get; set; }
        public List<uint> RecentColors { get; set; }

        // Item #5 tileset/map sections: same optional-extension rule - null
        // identifies a pre-map document and restores to no sections; older
        // readers ignore the extra members.
        public TilesetSectionDocument Tileset { get; set; }
        public MapSectionDocument Map { get; set; }

        // Item #33 linked directional section: same optional-extension rule -
        // null identifies a plain (or pre-directional) document. A present
        // section stores the active facing plus the three PARKED facing sets;
        // the active facing's animations stay in the main Animations list, so
        // an older reader opening a directional document still sees the
        // facing the designer last worked on.
        public DirectionalSectionDocument Directional { get; set; }

        public static ProjectDocument Capture(EFYVProject project)
        {
            var document = new ProjectDocument
            {
                FormatVersion = Config.Persistence.ProjectFormatVersion,
                TargetAssetType = project.TargetAssetType,
                UnityProjectPath = project.UnityProjectPath,
                CanvasWidth = project.CanvasWidth,
                CanvasHeight = project.CanvasHeight,
                DesignerSeed = project.DesignerSeed,
                AssetProperties = new Dictionary<string, object>(project.AssetProperties, StringComparer.Ordinal),
                Animations = new List<AnimationDocument>(project.Animations.Count),
                Palettes = new List<PaletteDocument>(project.Palettes.Count),
                RecentColors = new List<uint>(project.RecentColors.ToArray())
            };

            foreach (var animation in project.Animations)
                document.Animations.Add(AnimationDocument.Capture(animation));
            foreach (var palette in project.Palettes)
                document.Palettes.Add(PaletteDocument.Capture(palette));
            if (project.Tileset != null)
                document.Tileset = TilesetSectionDocument.Capture(project.Tileset);
            if (project.Map != null)
                document.Map = MapSectionDocument.Capture(project.Map);
            if (project.Directional != null)
                document.Directional = DirectionalSectionDocument.Capture(project.Directional);
            return document;
        }

        public EFYVProject Restore()
        {
            if (CanvasWidth <= Config.Canvas.MinCoordinate || CanvasHeight <= Config.Canvas.MinCoordinate ||
                CanvasWidth > Config.Persistence.MaxCanvasDimension ||
                CanvasHeight > Config.Persistence.MaxCanvasDimension ||
                Animations == null || Animations.Count > Config.Persistence.MaxAnimations)
                throw new InvalidDataException();

            var project = new EFYVProject(TargetAssetType)
            {
                UnityProjectPath = UnityProjectPath,
                CanvasWidth = CanvasWidth,
                CanvasHeight = CanvasHeight,
                DesignerSeed = DesignerSeed
            };
            project.AssetProperties.Clear();
            if (AssetProperties != null)
            {
                foreach (var property in AssetProperties)
                    project.AssetProperties[property.Key] = RestorePropertyValue(property.Value);
            }

            foreach (var animation in Animations)
                project.Animations.Add(animation.Restore(CanvasWidth, CanvasHeight));

            if (Palettes != null)
            {
                if (Palettes.Count > Config.Palette.MaxPalettes) throw new InvalidDataException();
                foreach (var palette in Palettes)
                    project.Palettes.Add(palette.Restore());
            }

            if (RecentColors != null)
            {
                if (RecentColors.Count > Config.Palette.RecentColorCapacity)
                    throw new InvalidDataException();
                // The document stores most-recent-first; pushing in reverse
                // rebuilds the ring with identical ordering.
                for (int index = RecentColors.Count - Config.Common.UnitCount;
                    index >= Config.Common.FirstIndex;
                    index--)
                    project.RecentColors.Push(RecentColors[index]);
            }

            if (Tileset != null) project.Tileset = Tileset.Restore();
            if (Map != null) project.Map = Map.Restore();
            if (Directional != null)
            {
                project.Directional = Directional.Restore(CanvasWidth, CanvasHeight);
                // The section's active facing is authoritative; resync the
                // facing property so a hand-edited mismatch cannot survive a
                // load (the two are kept in lockstep by DesignerSession).
                project.AssetProperties[
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake.Entity.KeyFacing] =
                    project.Directional.ActiveFacing;
            }
            return project;
        }

        private static object RestorePropertyValue(object value)
        {
            if (!(value is JsonElement)) return value;
            JsonElement element = (JsonElement)value;
            switch (element.ValueKind)
            {
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number:
                    int integer;
                    if (element.TryGetInt32(out integer)) return integer;
                    long longInteger;
                    if (element.TryGetInt64(out longInteger)) return longInteger;
                    return element.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return null;
                default: return element.Clone();
            }
        }
    }

    internal sealed class AnimationDocument
    {
        public string StateName { get; set; }
        public int FPS { get; set; }
        // Item #10 playback tags. Like the palette section, these EXTENDED the
        // document without a format-version bump: legacy documents deserialize
        // LoopStart 0 / LoopEnd null / PingPong false, which restore to the
        // defaults (full-range forward loop), and older readers ignore the
        // extra members. LoopEnd is nullable so "absent" is distinguishable
        // from an explicit value; null restores to FullRangeLoopEnd (-1).
        public int LoopStart { get; set; }
        public int? LoopEnd { get; set; }
        public bool PingPong { get; set; }
        public List<FrameDocument> Frames { get; set; }
        // Item #7 authored effect descriptors: same optional-extension rule -
        // null identifies a pre-effects document and restores to an empty
        // effect list; older readers ignore the extra member.
        public List<EffectDocument> Effects { get; set; }

        public static AnimationDocument Capture(AnimationState animation)
        {
            var document = new AnimationDocument
            {
                StateName = animation.StateName,
                FPS = animation.FPS,
                LoopStart = animation.LoopStartFrame,
                LoopEnd = animation.LoopEndFrame,
                PingPong = animation.PingPong,
                Frames = new List<FrameDocument>(animation.Frames.Count),
                Effects = new List<EffectDocument>(animation.Effects.Count)
            };
            foreach (var frame in animation.Frames) document.Frames.Add(FrameDocument.Capture(frame));
            // Null entries are captured as null so the ValidateDocument save
            // gate rejects them with the persistence contract's
            // InvalidDataException instead of faulting mid-capture.
            foreach (var effect in animation.Effects)
                document.Effects.Add(effect == null ? null : EffectDocument.Capture(effect));
            return document;
        }

        public AnimationState Restore(int canvasWidth, int canvasHeight)
        {
            if (FPS <= Config.Common.EmptyCount || Frames == null ||
                Frames.Count > Config.Persistence.MaxFramesPerAnimation ||
                LoopStart < Config.Common.FirstIndex ||
                (LoopEnd.HasValue && LoopEnd.Value < Config.Animation.FullRangeLoopEnd))
                throw new InvalidDataException();

            var animation = new AnimationState(StateName, FPS);
            animation.LoopStartFrame = LoopStart;
            animation.LoopEndFrame = LoopEnd ?? Config.Animation.FullRangeLoopEnd;
            animation.PingPong = PingPong;
            foreach (var frame in Frames) animation.Frames.Add(frame.Restore(canvasWidth, canvasHeight));
            if (Effects != null)
            {
                if (Effects.Count > Config.Effect.MaxEffectsPerAnimation)
                    throw new InvalidDataException();
                foreach (var effect in Effects)
                {
                    if (effect == null) throw new InvalidDataException();
                    animation.Effects.Add(effect.Restore());
                }
            }
            return animation;
        }
    }

    // Item #7: persisted effect descriptor. Restore routes through the
    // EffectDescriptor constructor - the single validation gate - and
    // translates its argument failures into the persistence contract's
    // InvalidDataException.
    internal sealed class EffectDocument
    {
        public string EffectType { get; set; }
        public string Name { get; set; }
        public string Trigger { get; set; }
        public uint ColorRgba { get; set; }
        public int DurationMs { get; set; }
        public float Strength { get; set; }

        public static EffectDocument Capture(EffectDescriptor effect)
        {
            return new EffectDocument
            {
                EffectType = effect.EffectType,
                Name = effect.Name,
                Trigger = effect.Trigger,
                ColorRgba = effect.ColorRgba,
                DurationMs = effect.DurationMs,
                Strength = effect.Strength
            };
        }

        public EffectDescriptor Restore()
        {
            try
            {
                return new EffectDescriptor(EffectType, Name, Trigger, ColorRgba, DurationMs, Strength);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(null, exception);
            }
        }
    }

    internal sealed class FrameDocument
    {
        public int FrameIndex { get; set; }
        // Item #10 per-frame duration override; 0 (also the legacy-document
        // default) means "inherit the animation FPS".
        public int DurationMs { get; set; }
        public List<LayerDocument> Layers { get; set; }
        public List<HitboxDocument> Hitboxes { get; set; }
        // Item #6 sub-element attachments: same optional-extension rule as
        // the palette/effect sections - null identifies a pre-attachment
        // document and restores to an empty attachment list; older readers
        // ignore the extra member.
        public List<AttachmentDocument> Attachments { get; set; }

        public static FrameDocument Capture(Frame frame)
        {
            var document = new FrameDocument
            {
                FrameIndex = frame.FrameIndex,
                DurationMs = frame.DurationMs,
                Layers = new List<LayerDocument>(frame.Layers.Count),
                Hitboxes = new List<HitboxDocument>(frame.Hitboxes.Count),
                Attachments = new List<AttachmentDocument>(frame.Attachments.Count)
            };
            foreach (var layer in frame.Layers) document.Layers.Add(LayerDocument.Capture(layer));
            foreach (var hitbox in frame.Hitboxes)
                document.Hitboxes.Add(HitboxDocument.Capture(hitbox.Key, hitbox.Value));
            // Null entries are captured as null so the ValidateDocument save
            // gate rejects them with the persistence contract's
            // InvalidDataException instead of faulting mid-capture.
            foreach (var attachment in frame.Attachments)
                document.Attachments.Add(
                    attachment == null ? null : AttachmentDocument.Capture(attachment));
            return document;
        }

        public Frame Restore(int width, int height)
        {
            if (Layers == null || Layers.Count > Config.Persistence.MaxLayersPerFrame || Hitboxes == null ||
                DurationMs < Config.Animation.InheritFrameDurationMs ||
                DurationMs > Config.Animation.MaxFrameDurationMs)
                throw new InvalidDataException();

            var frame = new Frame(width, height, FrameIndex);
            frame.DurationMs = DurationMs;
            frame.Layers.Clear();
            foreach (var layer in Layers) frame.Layers.Add(layer.Restore(width, height));
            frame.Hitboxes.Clear();
            foreach (var hitbox in Hitboxes) frame.Hitboxes.Add(hitbox.Key, hitbox.Restore());
            if (Attachments != null)
            {
                if (Attachments.Count > Config.Attachment.MaxPerFrame)
                    throw new InvalidDataException();
                foreach (var attachment in Attachments)
                {
                    if (attachment == null) throw new InvalidDataException();
                    frame.Attachments.Add(attachment.Restore());
                }
            }
            return frame;
        }
    }

    // Item #6: persisted sub-element attachment. Restore routes through the
    // SubElementAttachment constructor - the single validation gate - and
    // translates its argument failures into the persistence contract's
    // InvalidDataException.
    internal sealed class AttachmentDocument
    {
        public string SubElementName { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int ZOrder { get; set; }
        public bool FlipX { get; set; }
        public bool FlipY { get; set; }

        public static AttachmentDocument Capture(SubElementAttachment attachment)
        {
            return new AttachmentDocument
            {
                SubElementName = attachment.SubElementName,
                X = attachment.X,
                Y = attachment.Y,
                ZOrder = attachment.ZOrder,
                FlipX = attachment.FlipX,
                FlipY = attachment.FlipY
            };
        }

        public SubElementAttachment Restore()
        {
            try
            {
                return new SubElementAttachment(SubElementName, X, Y, ZOrder, FlipX, FlipY);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(null, exception);
            }
        }
    }

    internal sealed class LayerDocument
    {
        public string Name { get; set; }
        public bool IsVisible { get; set; }
        public float Opacity { get; set; }
        public byte[] RgbaBytes { get; set; }

        public static LayerDocument Capture(Layer layer)
        {
            var bytes = new byte[checked(layer.Pixels.Length * Config.Color.RgbaChannelCount)];
            for (int index = Config.Common.FirstIndex; index < layer.Pixels.Length; index++)
            {
                uint rgba = layer.Pixels[index].Rgba;
                int byteIndex = index * Config.Color.RgbaChannelCount;
                bytes[byteIndex + Config.Color.RedByteOffset] = (byte)rgba;
                bytes[byteIndex + Config.Color.GreenByteOffset] = (byte)(rgba >> Config.Color.GreenShift);
                bytes[byteIndex + Config.Color.BlueByteOffset] = (byte)(rgba >> Config.Color.BlueShift);
                bytes[byteIndex + Config.Color.AlphaByteOffset] = (byte)(rgba >> Config.Color.AlphaShift);
            }

            return new LayerDocument
            {
                Name = layer.Name,
                IsVisible = layer.IsVisible,
                Opacity = layer.Opacity,
                RgbaBytes = bytes
            };
        }

        public Layer Restore(int width, int height)
        {
            int pixelCount = checked(width * height);
            int byteCount = checked(pixelCount * Config.Color.RgbaChannelCount);
            if (RgbaBytes == null || RgbaBytes.Length != byteCount) throw new InvalidDataException();

            var pixels = new PixelColor[pixelCount];
            for (int index = Config.Common.FirstIndex; index < pixelCount; index++)
            {
                int byteIndex = index * Config.Color.RgbaChannelCount;
                pixels[index].Rgba =
                    RgbaBytes[byteIndex + Config.Color.RedByteOffset] |
                    ((uint)RgbaBytes[byteIndex + Config.Color.GreenByteOffset] << Config.Color.GreenShift) |
                    ((uint)RgbaBytes[byteIndex + Config.Color.BlueByteOffset] << Config.Color.BlueShift) |
                    ((uint)RgbaBytes[byteIndex + Config.Color.AlphaByteOffset] << Config.Color.AlphaShift);
            }

            var layer = new Layer(Name, width, height)
            {
                IsVisible = IsVisible,
                Opacity = Opacity
            };
            layer.CopyPixelsFrom(pixels);
            return layer;
        }
    }

    internal sealed class HitboxDocument
    {
        public string Key { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }

        public static HitboxDocument Capture(string key, HitboxData hitbox)
        {
            return new HitboxDocument
            {
                Key = key,
                X = hitbox.X,
                Y = hitbox.Y,
                Width = hitbox.Width,
                Height = hitbox.Height
            };
        }

        public HitboxData Restore()
        {
            if (string.IsNullOrWhiteSpace(Key)) throw new InvalidDataException();
            return new HitboxData { X = X, Y = Y, Width = Width, Height = Height };
        }
    }

    // Item #5: persisted tileset section. Tile pixels use the same RGBA byte
    // packing as LayerDocument; Restore routes through the TilesetTile
    // constructor - the single validation gate - and translates its argument
    // failures into the persistence contract's InvalidDataException.
    internal sealed class TilesetSectionDocument
    {
        public int TileSize { get; set; }
        public List<TilesetTileDocument> Tiles { get; set; }

        public static TilesetSectionDocument Capture(TilesetSection tileset)
        {
            var document = new TilesetSectionDocument
            {
                TileSize = tileset.TileSize,
                Tiles = new List<TilesetTileDocument>(tileset.Tiles.Count)
            };
            // Null entries are captured as null so the ValidateDocument save
            // gate rejects them with the persistence contract's exception.
            foreach (TilesetTile tile in tileset.Tiles)
                document.Tiles.Add(tile == null ? null : TilesetTileDocument.Capture(tile));
            return document;
        }

        public TilesetSection Restore()
        {
            if (Tiles == null || Tiles.Count > Config.Tileset.MaxTiles)
                throw new InvalidDataException();
            TilesetSection tileset;
            try
            {
                tileset = new TilesetSection(TileSize);
                foreach (TilesetTileDocument tile in Tiles)
                {
                    if (tile == null) throw new InvalidDataException();
                    tileset.Tiles.Add(tile.Restore(TileSize));
                }
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(null, exception);
            }
            return tileset;
        }
    }

    internal sealed class TilesetTileDocument
    {
        public string Name { get; set; }
        public byte[] RgbaBytes { get; set; }

        public static TilesetTileDocument Capture(TilesetTile tile)
        {
            var bytes = new byte[checked(tile.Pixels.Length * Config.Color.RgbaChannelCount)];
            for (int index = Config.Common.FirstIndex; index < tile.Pixels.Length; index++)
            {
                uint rgba = tile.Pixels[index];
                int byteIndex = index * Config.Color.RgbaChannelCount;
                bytes[byteIndex + Config.Color.RedByteOffset] = (byte)rgba;
                bytes[byteIndex + Config.Color.GreenByteOffset] = (byte)(rgba >> Config.Color.GreenShift);
                bytes[byteIndex + Config.Color.BlueByteOffset] = (byte)(rgba >> Config.Color.BlueShift);
                bytes[byteIndex + Config.Color.AlphaByteOffset] = (byte)(rgba >> Config.Color.AlphaShift);
            }
            return new TilesetTileDocument { Name = tile.Name, RgbaBytes = bytes };
        }

        public TilesetTile Restore(int tileSize)
        {
            int pixelCount = checked(tileSize * tileSize);
            int byteCount = checked(pixelCount * Config.Color.RgbaChannelCount);
            if (RgbaBytes == null || RgbaBytes.Length != byteCount) throw new InvalidDataException();

            var pixels = new uint[pixelCount];
            for (int index = Config.Common.FirstIndex; index < pixelCount; index++)
            {
                int byteIndex = index * Config.Color.RgbaChannelCount;
                pixels[index] =
                    RgbaBytes[byteIndex + Config.Color.RedByteOffset] |
                    ((uint)RgbaBytes[byteIndex + Config.Color.GreenByteOffset] << Config.Color.GreenShift) |
                    ((uint)RgbaBytes[byteIndex + Config.Color.BlueByteOffset] << Config.Color.BlueShift) |
                    ((uint)RgbaBytes[byteIndex + Config.Color.AlphaByteOffset] << Config.Color.AlphaShift);
            }
            return new TilesetTile(Name, tileSize, pixels);
        }
    }

    // Item #5: persisted map section. Tile ids serialize as little-endian
    // int16 pairs; Restore routes through the MapSection constructor - the
    // single validation gate.
    internal sealed class MapSectionDocument
    {
        internal const int BytesPerTile =
            EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.MapFile.BytesPerTile;

        public string MapId { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string TilesetName { get; set; }
        public byte[] TileBytes { get; set; }
        public List<MapPropDocument> Props { get; set; }

        public static MapSectionDocument Capture(MapSection map)
        {
            short[] tiles = map.Grid.RawData;
            var bytes = new byte[checked(tiles.Length * BytesPerTile)];
            for (int index = Config.Common.FirstIndex; index < tiles.Length; index++)
            {
                ushort value = (ushort)tiles[index];
                bytes[index * BytesPerTile] = (byte)value;
                bytes[(index * BytesPerTile) + Config.Common.UnitCount] = (byte)(value >> 8);
            }

            var document = new MapSectionDocument
            {
                MapId = map.MapId,
                Width = map.Grid.Width,
                Height = map.Grid.Height,
                TilesetName = map.TilesetName,
                TileBytes = bytes,
                Props = new List<MapPropDocument>(map.Grid.Props.Count)
            };
            for (int index = Config.Common.FirstIndex; index < map.Grid.Props.Count; index++)
            {
                EFYVBackend.Core.Collections.FastGridMap.MapPropData prop = map.Grid.Props[index];
                document.Props.Add(new MapPropDocument
                {
                    AssetKey = prop.AssetKey,
                    X = prop.X,
                    Y = prop.Y,
                    Scale = prop.Scale
                });
            }
            return document;
        }

        public MapSection Restore()
        {
            long cellCount = (long)Width * Height;
            if (TileBytes == null || Props == null ||
                Props.Count > Config.MapDocument.MaxProps ||
                cellCount <= Config.Common.EmptyCount ||
                TileBytes.LongLength != cellCount * BytesPerTile)
                throw new InvalidDataException();

            MapSection map;
            try
            {
                map = new MapSection(MapId, Width, Height, TilesetName);
            }
            catch (ArgumentException exception)
            {
                // Also covers ArgumentOutOfRangeException (dimension caps).
                throw new InvalidDataException(null, exception);
            }

            short[] tiles = map.Grid.RawData;
            for (int index = Config.Common.FirstIndex; index < tiles.Length; index++)
            {
                tiles[index] = (short)(TileBytes[index * BytesPerTile] |
                    (TileBytes[(index * BytesPerTile) + Config.Common.UnitCount] << 8));
            }

            foreach (MapPropDocument prop in Props)
            {
                if (prop == null || !DesignerPathPolicy.IsSafeFileStem(prop.AssetKey) ||
                    float.IsNaN(prop.Scale) || float.IsInfinity(prop.Scale))
                    throw new InvalidDataException();
                map.Grid.Props.Add(new EFYVBackend.Core.Collections.FastGridMap.MapPropData
                {
                    AssetKey = prop.AssetKey,
                    X = prop.X,
                    Y = prop.Y,
                    Scale = prop.Scale
                });
            }
            return map;
        }
    }

    internal sealed class MapPropDocument
    {
        public string AssetKey { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public float Scale { get; set; }
    }

    // Item #33: persisted linked-directional section. Holds the active facing
    // name plus one AnimationDocument list per PARKED facing (the active
    // facing's property stays null - its animations are the document's main
    // Animations list). Restore routes through the DirectionalState
    // constructor - the single validation gate for the facing name - and the
    // same AnimationDocument.Restore path the main list uses.
    internal sealed class DirectionalSectionDocument
    {
        public string ActiveFacing { get; set; }
        public List<AnimationDocument> Up { get; set; }
        public List<AnimationDocument> Down { get; set; }
        public List<AnimationDocument> Left { get; set; }
        public List<AnimationDocument> Right { get; set; }

        public static DirectionalSectionDocument Capture(DirectionalState state)
        {
            var document = new DirectionalSectionDocument { ActiveFacing = state.ActiveFacing };
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (string.Equals(facing, state.ActiveFacing, StringComparison.Ordinal)) continue;
                var animations = new List<AnimationDocument>();
                foreach (AnimationState animation in state.GetInactiveFacingAnimations(facing))
                {
                    // Null entries are captured as null so the ValidateDocument
                    // save gate rejects them with the persistence contract's
                    // InvalidDataException instead of faulting mid-capture.
                    animations.Add(animation == null ? null : AnimationDocument.Capture(animation));
                }
                document.SetFacingList(facing, animations);
            }
            return document;
        }

        public DirectionalState Restore(int canvasWidth, int canvasHeight)
        {
            DirectionalState state;
            try
            {
                state = new DirectionalState(ActiveFacing);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(null, exception);
            }

            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (string.Equals(facing, ActiveFacing, StringComparison.Ordinal)) continue;
                List<AnimationDocument> documents = GetFacingList(facing);
                if (documents == null || documents.Count > Config.Persistence.MaxAnimations)
                    throw new InvalidDataException();
                var animations = new List<AnimationState>(documents.Count);
                foreach (AnimationDocument animation in documents)
                {
                    if (animation == null) throw new InvalidDataException();
                    animations.Add(animation.Restore(canvasWidth, canvasHeight));
                }
                state.SetInactiveSet(facing, animations);
            }
            return state;
        }

        internal List<AnimationDocument> GetFacingList(string facing)
        {
            if (facing == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FacingUp) return Up;
            if (facing == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FacingDown) return Down;
            if (facing == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FacingLeft) return Left;
            if (facing == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FacingRight) return Right;
            throw new ArgumentException(nameof(facing));
        }

        private void SetFacingList(string facing, List<AnimationDocument> animations)
        {
            if (facing == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FacingUp) Up = animations;
            else if (facing == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FacingDown) Down = animations;
            else if (facing == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FacingLeft) Left = animations;
            else if (facing == EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.FacingRight) Right = animations;
            else throw new ArgumentException(nameof(facing));
        }
    }

    internal sealed class PaletteDocument
    {
        public string Name { get; set; }
        public List<uint> Colors { get; set; }

        public static PaletteDocument Capture(Palette palette)
        {
            return new PaletteDocument
            {
                Name = palette.Name,
                Colors = new List<uint>(palette.Colors)
            };
        }

        public Palette Restore()
        {
            if (string.IsNullOrWhiteSpace(Name) ||
                Name.Length > Config.Palette.MaxNameLength ||
                Colors == null ||
                Colors.Count > Config.Palette.MaxSwatchesPerPalette)
                throw new InvalidDataException();

            var palette = new Palette(Name);
            palette.Colors.AddRange(Colors);
            return palette;
        }
    }
}
