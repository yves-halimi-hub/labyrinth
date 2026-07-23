using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using EFYVBackend.Core.Export;

namespace EFYV.Labyrinth.Artifacts
{
    public static class LabyMakeArtifactExporter
    {
        public static LabyMakeArtifactBundle Export(LabyMakeSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            cancellationToken.ThrowIfCancellationRequested();
            uint[] atlasPixels = snapshot.Frames[0];
            FastArtifactPayload artifact = FastExporter.BuildArtifact(
                snapshot.AssetType,
                snapshot.Properties,
                snapshot.Hitboxes,
                atlasPixels,
                snapshot.Atlas.atlasWidth,
                snapshot.Atlas.atlasHeight,
                snapshot.Atlas,
                snapshot.BaseAssetType,
                snapshot.Attachments,
                snapshot.Tileset,
                cancellationToken);

            byte[] bundle = CreateBundle(artifact, snapshot.SourceBytes, cancellationToken);
            if (bundle.Length > LabyrinthArtifactLimits.MaxBundleBytes)
                throw new InvalidDataException("export bundle exceeds 25 MB");
            string digest = Convert.ToHexString(SHA256.HashData(bundle)).ToLowerInvariant();
            return new LabyMakeArtifactBundle(artifact.Stem, artifact.Png, artifact.Metadata, bundle, digest);
        }

        private static byte[] CreateBundle(FastArtifactPayload artifact, byte[] source, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(zip, artifact.Stem + ".png", artifact.Png, cancellationToken);
                WriteEntry(zip, artifact.Stem + ".efyvlaby", artifact.Metadata, cancellationToken);
                WriteEntry(zip, artifact.Stem + ".efyvmake", source, cancellationToken);
                byte[] handoff = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 1,
                    destination = "browser-folder",
                    files = new[] { artifact.Stem + ".png", artifact.Stem + ".efyvlaby" },
                });
                WriteEntry(zip, "handoff.json", handoff, cancellationToken);
            }
            return output.ToArray();
        }

        private static void WriteEntry(ZipArchive zip, string name, byte[] bytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using Stream stream = entry.Open();
            const int chunkSize = LabyrinthArtifactLimits.StreamChunkBytes;
            for (int offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Write(bytes, offset, System.Math.Min(chunkSize, bytes.Length - offset));
            }
        }
    }
}
