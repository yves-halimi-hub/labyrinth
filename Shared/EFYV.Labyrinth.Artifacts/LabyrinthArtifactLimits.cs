using EFYVBackend.Core.Data;

namespace EFYV.Labyrinth.Artifacts
{
    public static class LabyrinthArtifactLimits
    {
        public const int ContractVersion = 1;
        public const int DocumentVersion = EFYVLabyrinthConfig.Backend.Exporter.CurrentDocumentVersion;
        public const int AtlasFormatVersion = EFYVLabyrinthConfig.Backend.Exporter.CurrentFormatVersion;
        public const int MaxSnapshotBytes = 16 * 1024 * 1024;
        public const int MaxCanvasDimension = EFYVLabyrinthConfig.LabyMake.Persistence.MaxCanvasDimension;
        public const int MaxAnimations = EFYVLabyrinthConfig.LabyMake.Persistence.MaxAnimations;
        public const int MaxFrames = EFYVLabyrinthConfig.LabyMake.Persistence.MaxFramesPerAnimation;
        public const int MaxLayersPerFrame = EFYVLabyrinthConfig.LabyMake.Persistence.MaxLayersPerFrame;
        public const long MaxDecodedFrameBytes = 64L * 1024 * 1024;
        public const int MaxAtlasDimension = EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasDimension;
        public const long MaxAtlasPixels = EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasPixelCount;
        public const int MaxBundleBytes = 25 * 1024 * 1024;
        public const int StreamChunkBytes = 64 * 1024;
        public const int MaxConcurrentExports = 2;
        public const int MaxQueuedExports = 8;
        public const int MaxJsonDepth = 64;
        public const int MaxFps = 240;
        public const int MaxFrameDurationMs = EFYVLabyrinthConfig.Backend.Exporter.MaxFrameDurationMs;
        public const int BlendCancellationPixelBatch = 64 * 1024;
    }
}
