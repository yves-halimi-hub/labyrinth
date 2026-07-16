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

        internal ExportResult(
            string metadataPath,
            string imagePath,
            int frameCount,
            int hitboxCount,
            int atlasWidth,
            int atlasHeight)
        {
            MetadataPath = metadataPath;
            ImagePath = imagePath;
            FrameCount = frameCount;
            HitboxCount = hitboxCount;
            AtlasWidth = atlasWidth;
            AtlasHeight = atlasHeight;
        }
    }

    public sealed class ExportEngine
    {
        private readonly ProjectValidator validator;

        public ExportEngine()
            : this(new ProjectValidator(new AssetSchemaService()))
        {
        }

        public ExportEngine(ProjectValidator validator)
        {
            if (validator == null) throw new ArgumentNullException(nameof(validator));
            this.validator = validator;
        }

        public void PushToUnityLiveHook(EFYVProject project)
        {
            Export(project, CancellationToken.None);
        }

        public ExportResult Export(EFYVProject project, CancellationToken cancellationToken)
        {
            ProjectValidationResult validation = validator.Validate(project, ProjectValidationScope.Export);
            if (!validation.IsValid) throw new ProjectValidationException(validation);
            cancellationToken.ThrowIfCancellationRequested();
            return Export(ProjectSnapshot.Capture(project), cancellationToken);
        }

        public ExportResult Export(ProjectSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.TotalFrameCount <= Config.Export.InitialFrameCount)
                throw new InvalidOperationException();

            cancellationToken.ThrowIfCancellationRequested();

            int atlasWidth = checked(snapshot.CanvasWidth * snapshot.TotalFrameCount);
            int atlasHeight = snapshot.CanvasHeight;
            PixelColor[] atlasData = new PixelColor[checked(atlasWidth * atlasHeight)];
            var hitboxes = new List<HitboxJson>(snapshot.TotalHitboxCount);

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

                    EFYVBackend.Core.Export.FastExporter.PackFramesToAtlas(
                        atlasData,
                        atlasWidth,
                        frame.Pixels,
                        snapshot.CanvasWidth,
                        snapshot.CanvasHeight,
                        checked(globalFrameIndex * snapshot.CanvasWidth),
                        Config.Export.AtlasDestinationY);
                    globalFrameIndex++;
                }
            }

            return WriteAtomically(snapshot, hitboxes, atlasData, atlasWidth, atlasHeight, cancellationToken);
        }

        private static ExportResult WriteAtomically(
            ProjectSnapshot snapshot,
            List<HitboxJson> hitboxes,
            PixelColor[] atlasData,
            int atlasWidth,
            int atlasHeight,
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
                    animationMetadata.Add(new AnimationMetadataJson
                    {
                        name = animation.StateName,
                        fps = animation.FPS,
                        startFrame = animation.StartFrame,
                        frameCount = animation.Frames.Count
                    });
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

                EFYVBackend.Core.Export.FastExporter.PushToUnityLiveHook(
                    stagingDirectory,
                    snapshot.TargetAssetType,
                    snapshot.CopyAssetProperties(),
                    hitboxes,
                    atlasData,
                    atlasWidth,
                    atlasHeight,
                    atlasMetadata);

                cancellationToken.ThrowIfCancellationRequested();

                object identityValue;
                if (!snapshot.AssetProperties.TryGetValue(BackendConfig.Exporter.FieldEntityName, out identityValue))
                    identityValue = snapshot.AssetProperties[BackendConfig.Exporter.FieldAssetName];
                string entityName = (string)identityValue;
                string exportStem = entityName + GetFacingFileSuffix(snapshot);
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

                // The metadata is authoritative; publish the image first and metadata last.
                PublishPair(stagedImagePath, imagePath, stagedMetadataPath, metadataPath);

                return new ExportResult(
                    metadataPath,
                    imagePath,
                    snapshot.TotalFrameCount,
                    snapshot.TotalHitboxCount,
                    atlasWidth,
                    atlasHeight);
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

        private static void PublishWithBackup(
            string stagedPath,
            string destinationPath,
            string backupPath,
            bool hadOriginal)
        {
            if (!hadOriginal)
            {
                File.Move(stagedPath, destinationPath);
                return;
            }

            try
            {
                File.Replace(stagedPath, destinationPath, backupPath);
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
}
