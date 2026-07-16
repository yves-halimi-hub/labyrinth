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

            long totalFrames = Config.Common.EmptyCount;
            foreach (var animation in document.Animations)
            {
                if (animation == null || string.IsNullOrWhiteSpace(animation.StateName) ||
                    animation.FPS <= Config.Common.EmptyCount ||
                    animation.Frames == null ||
                    animation.Frames.Count > Config.Persistence.MaxFramesPerAnimation)
                    throw new InvalidDataException();
                totalFrames += animation.Frames.Count;

                foreach (var frame in animation.Frames)
                {
                    if (frame == null || frame.Layers == null || frame.Hitboxes == null ||
                        frame.Layers.Count > Config.Persistence.MaxLayersPerFrame)
                        throw new InvalidDataException();

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
                Animations = new List<AnimationDocument>(project.Animations.Count)
            };

            foreach (var animation in project.Animations)
                document.Animations.Add(AnimationDocument.Capture(animation));
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
        public List<FrameDocument> Frames { get; set; }

        public static AnimationDocument Capture(AnimationState animation)
        {
            var document = new AnimationDocument
            {
                StateName = animation.StateName,
                FPS = animation.FPS,
                Frames = new List<FrameDocument>(animation.Frames.Count)
            };
            foreach (var frame in animation.Frames) document.Frames.Add(FrameDocument.Capture(frame));
            return document;
        }

        public AnimationState Restore(int canvasWidth, int canvasHeight)
        {
            if (FPS <= Config.Common.EmptyCount || Frames == null ||
                Frames.Count > Config.Persistence.MaxFramesPerAnimation)
                throw new InvalidDataException();

            var animation = new AnimationState(StateName, FPS);
            foreach (var frame in Frames) animation.Frames.Add(frame.Restore(canvasWidth, canvasHeight));
            return animation;
        }
    }

    internal sealed class FrameDocument
    {
        public int FrameIndex { get; set; }
        public List<LayerDocument> Layers { get; set; }
        public List<HitboxDocument> Hitboxes { get; set; }

        public static FrameDocument Capture(Frame frame)
        {
            var document = new FrameDocument
            {
                FrameIndex = frame.FrameIndex,
                Layers = new List<LayerDocument>(frame.Layers.Count),
                Hitboxes = new List<HitboxDocument>(frame.Hitboxes.Count)
            };
            foreach (var layer in frame.Layers) document.Layers.Add(LayerDocument.Capture(layer));
            foreach (var hitbox in frame.Hitboxes)
                document.Hitboxes.Add(HitboxDocument.Capture(hitbox.Key, hitbox.Value));
            return document;
        }

        public Frame Restore(int width, int height)
        {
            if (Layers == null || Layers.Count > Config.Persistence.MaxLayersPerFrame || Hitboxes == null)
                throw new InvalidDataException();

            var frame = new Frame(width, height, FrameIndex);
            frame.Layers.Clear();
            foreach (var layer in Layers) frame.Layers.Add(layer.Restore(width, height));
            frame.Hitboxes.Clear();
            foreach (var hitbox in Hitboxes) frame.Hitboxes.Add(hitbox.Key, hitbox.Restore());
            return frame;
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
}
