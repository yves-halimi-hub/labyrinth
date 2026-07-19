using System.IO;
using System;
using System.Globalization;
using EFYVBackend.Core.Data; // Hooking into the unified schemas
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.IO
{
    // ATOMIC, VERSIONED BINARY SAVE (#19).
    // On-disk layout: {magic u32, version i32, CRC32-of-payload u32} little-endian
    // header followed by the raw PlayerMetaSchema bytes. Writes go to a dotted
    // temporary sibling first and land via File.Replace/Move (with the shared
    // bounded IO retry), so a crash mid-write can never truncate the live save.
    // Stale, corrupt, truncated, or version-mismatched files fail LoadGame
    // cleanly: it returns false and the caller keeps the default profile.
    public static class FastSaveEngine
    {
        public static unsafe void SaveGame(string path, ref PlayerMetaSchema data)
        {
            string destinationPath = ResolveSavePath(path);
            string directory = Path.GetDirectoryName(destinationPath);
            string temporaryPath = Path.Combine(
                directory,
                BackendConfig.Exporter.TemporaryNamePrefix + Path.GetFileName(destinationPath) +
                BackendConfig.Exporter.TemporaryNamePrefix +
                Guid.NewGuid().ToString(BackendConfig.Exporter.CompactGuidFormat, CultureInfo.InvariantCulture) +
                BackendConfig.Exporter.TemporaryExtension);

            try
            {
                using (FileStream fs = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BackendConfig.IO.DefaultFileStreamBufferSize,
                    FileOptions.SequentialScan))
                {
                    int size = sizeof(PlayerMetaSchema);
                    byte[] header = new byte[BackendConfig.Save.HeaderSizeBytes];

                    // Pin the struct in memory and write header + raw payload bytes.
                    fixed (PlayerMetaSchema* ptr = &data)
                    {
                        ReadOnlySpan<byte> payload = new ReadOnlySpan<byte>(ptr, size);
                        WriteUInt32LittleEndian(header, BackendConfig.Save.MagicOffset, BackendConfig.Save.MagicNumber);
                        WriteUInt32LittleEndian(header, BackendConfig.Save.VersionOffset, (uint)BackendConfig.Save.FormatVersion);
                        WriteUInt32LittleEndian(header, BackendConfig.Save.ChecksumOffset, FastCrc32.Compute(payload));
                        fs.Write(header, BackendConfig.IO.InitialReadOffset, header.Length);
                        fs.Write(payload);
                    }
                    fs.Flush(true);
                }

                FastIoRetry.Run(() =>
                {
                    if (File.Exists(destinationPath)) File.Replace(temporaryPath, destinationPath, null);
                    else File.Move(temporaryPath, destinationPath);
                });
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

        public static unsafe bool LoadGame(string path, out PlayerMetaSchema data)
        {
            data = PlayerMetaSchema.Default();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BackendConfig.IO.DefaultFileStreamBufferSize, FileOptions.SequentialScan))
            {
                int size = sizeof(PlayerMetaSchema);

                // Exact-size check first: truncated files and header-less legacy
                // dumps (or files with trailing garbage) are all rejected.
                if (fs.Length != (long)BackendConfig.Save.HeaderSizeBytes + size) return false;

                byte[] header = new byte[BackendConfig.Save.HeaderSizeBytes];
                if (!ReadExactly(fs, header, header.Length)) return false;
                if (ReadUInt32LittleEndian(header, BackendConfig.Save.MagicOffset) != BackendConfig.Save.MagicNumber) return false;
                if (ReadUInt32LittleEndian(header, BackendConfig.Save.VersionOffset) != (uint)BackendConfig.Save.FormatVersion) return false;
                uint expectedChecksum = ReadUInt32LittleEndian(header, BackendConfig.Save.ChecksumOffset);

                // Pin the struct in memory and populate it directly from the file bytes
                fixed (PlayerMetaSchema* ptr = &data)
                {
                    Span<byte> span = new Span<byte>(ptr, size);
                    int totalRead = BackendConfig.IO.InitialReadOffset;
                    while (totalRead < size)
                    {
                        int bytesRead = fs.Read(span.Slice(totalRead));
                        if (bytesRead == BackendConfig.IO.EndOfStreamReadCount)
                        {
                            data = PlayerMetaSchema.Default();
                            return false;
                        }
                        totalRead += bytesRead;
                    }

                    if (FastCrc32.Compute(span) != expectedChecksum)
                    {
                        data = PlayerMetaSchema.Default();
                        return false;
                    }
                }
            }
            return true;
        }

        // Save paths are routed through SafePathPolicy: the destination must be a
        // well-formed direct child of its directory (no traversal via crafted
        // file names) and the path must be non-empty.
        private static string ResolveSavePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(null, nameof(path));
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException(null, nameof(path));
            return SafePathPolicy.GetContainedPath(directory, Path.GetFileName(fullPath));
        }

        private static bool ReadExactly(Stream stream, byte[] destination, int count)
        {
            int totalRead = BackendConfig.IO.InitialReadOffset;
            while (totalRead < count)
            {
                int bytesRead = stream.Read(destination, totalRead, count - totalRead);
                if (bytesRead == BackendConfig.IO.EndOfStreamReadCount) return false;
                totalRead += bytesRead;
            }
            return true;
        }

        private static void WriteUInt32LittleEndian(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32LittleEndian(byte[] source, int offset)
        {
            return source[offset] |
                ((uint)source[offset + 1] << 8) |
                ((uint)source[offset + 2] << 16) |
                ((uint)source[offset + 3] << 24);
        }
    }
}
