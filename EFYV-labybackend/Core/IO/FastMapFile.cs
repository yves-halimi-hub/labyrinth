using System;
using System.Globalization;
using System.IO;
using System.Text;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.IO
{
    // One prop placement inside a .efyvmap payload (item #5). AssetKey names
    // a designer asset (a safe file stem, like .efyvlaby identities); X/Y are
    // map-space pixel coordinates and Scale a finite uniform multiplier.
    public struct MapPropRecord
    {
        public string AssetKey;
        public int X;
        public int Y;
        public float Scale;
    }

    // The in-memory image of one .efyvmap file (item #5): dimensions, the
    // referenced tileset identity (empty string = none), row-major int16 tile
    // ids (BackendConfig.MapFile.BlankTileId = blank cell), and prop
    // placements. Consumers copy Tiles into a FastGridMap.
    public sealed class MapFileData
    {
        public int Width;
        public int Height;
        public string TilesetName;
        public short[] Tiles;
        public MapPropRecord[] Props;
    }

    // VERSIONED BINARY MAP CONTAINER (item #5).
    // On-disk layout: the same {magic u32, version i32, CRC32-of-payload u32}
    // little-endian envelope FastSaveEngine uses, followed by the payload:
    //   i32 width, i32 height,
    //   i32 tilesetNameByteCount + UTF-8 bytes,
    //   width*height little-endian int16 tile ids (row-major),
    //   i32 propCount, then per prop:
    //     i32 assetKeyByteCount + UTF-8 bytes, i32 x, i32 y, f32 scale.
    // Writes go to a dotted temporary sibling first and land via
    // File.Replace/Move under the shared bounded IO retry, so a crash
    // mid-write can never truncate a published map.
    public static class FastMapExporter
    {
        // Upper bound for a well-formed payload; anything longer is rejected
        // before allocation on both the write and read side.
        internal static readonly long MaxPayloadBytes = ComputeMaxPayloadBytes();

        private static long ComputeMaxPayloadBytes()
        {
            long stringBytes = sizeof(int) +
                (long)BackendConfig.IO.MaxFileStemLength * MaxUtf8BytesPerChar;
            long tileBytes = (long)BackendConfig.MapFile.MaxMapDimension *
                BackendConfig.MapFile.MaxMapDimension *
                BackendConfig.MapFile.BytesPerTile;
            long propBytes = sizeof(int) + (long)BackendConfig.MapFile.MaxMapProps *
                (stringBytes + sizeof(int) + sizeof(int) + sizeof(float));
            return sizeof(int) + sizeof(int) + stringBytes + tileBytes + propBytes;
        }

        private const int MaxUtf8BytesPerChar = 4;

        public static void Export(string path, MapFileData data)
        {
            if (!TryValidate(data)) throw new ArgumentException(null, nameof(data));

            string destinationPath = ResolveDestinationPath(path);
            string directory = Path.GetDirectoryName(destinationPath);
            string temporaryPath = Path.Combine(
                directory,
                BackendConfig.Exporter.TemporaryNamePrefix + Path.GetFileName(destinationPath) +
                BackendConfig.Exporter.TemporaryNamePrefix +
                Guid.NewGuid().ToString(BackendConfig.Exporter.CompactGuidFormat, CultureInfo.InvariantCulture) +
                BackendConfig.Exporter.TemporaryExtension);

            byte[] payload = BuildPayload(data);
            try
            {
                using (FileStream stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BackendConfig.IO.DefaultFileStreamBufferSize,
                    FileOptions.SequentialScan))
                {
                    byte[] header = new byte[BackendConfig.MapFile.HeaderSizeBytes];
                    WriteUInt32LittleEndian(header, BackendConfig.MapFile.MagicOffset, BackendConfig.MapFile.MagicNumber);
                    WriteUInt32LittleEndian(header, BackendConfig.MapFile.VersionOffset, (uint)BackendConfig.MapFile.FormatVersion);
                    WriteUInt32LittleEndian(header, BackendConfig.MapFile.ChecksumOffset, FastCrc32.Compute(payload));
                    stream.Write(header, BackendConfig.IO.InitialReadOffset, header.Length);
                    stream.Write(payload, BackendConfig.IO.InitialReadOffset, payload.Length);
                    stream.Flush(true);
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

        // The ONE structural validator, shared by the writer (throws) and the
        // reader (returns Malformed). Tile VALUES are unconstrained shorts by
        // design: ids below the runtime palette floor render as blank cells,
        // so a stricter wire rule would add no safety.
        public static bool TryValidate(MapFileData data)
        {
            if (data == null) return false;
            if (data.Width <= 0 || data.Width > BackendConfig.MapFile.MaxMapDimension) return false;
            if (data.Height <= 0 || data.Height > BackendConfig.MapFile.MaxMapDimension) return false;
            if (data.Tiles == null || data.Tiles.LongLength != (long)data.Width * data.Height) return false;
            if (data.Props == null || data.Props.Length > BackendConfig.MapFile.MaxMapProps) return false;
            if (!IsValidOptionalStem(data.TilesetName)) return false;

            for (int index = 0; index < data.Props.Length; index++)
            {
                MapPropRecord prop = data.Props[index];
                if (!SafePathPolicy.IsSafeFileStem(prop.AssetKey)) return false;
                if (float.IsNaN(prop.Scale) || float.IsInfinity(prop.Scale)) return false;
            }
            return true;
        }

        private static bool IsValidOptionalStem(string value)
        {
            return string.IsNullOrEmpty(value) || SafePathPolicy.IsSafeFileStem(value);
        }

        private static byte[] BuildPayload(MapFileData data)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                writer.Write(data.Width);
                writer.Write(data.Height);
                WriteBoundedString(writer, data.TilesetName ?? string.Empty);
                for (int index = 0; index < data.Tiles.Length; index++) writer.Write(data.Tiles[index]);
                writer.Write(data.Props.Length);
                for (int index = 0; index < data.Props.Length; index++)
                {
                    MapPropRecord prop = data.Props[index];
                    WriteBoundedString(writer, prop.AssetKey);
                    writer.Write(prop.X);
                    writer.Write(prop.Y);
                    writer.Write(prop.Scale);
                }
                writer.Flush();
                return buffer.ToArray();
            }
        }

        private static void WriteBoundedString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        // Same containment rule as FastSaveEngine: the destination must be a
        // well-formed direct child of its directory.
        private static string ResolveDestinationPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(null, nameof(path));
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException(null, nameof(path));
            return SafePathPolicy.GetContainedPath(directory, Path.GetFileName(fullPath));
        }

        internal static void WriteUInt32LittleEndian(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }
    }

    // Tri-state .efyvmap reader (item #5), mirroring FastImporter.TryParse:
    // Missing (no file), Malformed (present but failing the envelope, the
    // CRC, or the structural payload contract), Valid. IO failures (locks,
    // permissions) still propagate - they are transient environment errors,
    // not document states.
    public static class FastMapImporter
    {
        public static EfyvParseResult TryParse(string path, out MapFileData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return EfyvParseResult.Missing;

            byte[] payload;
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BackendConfig.IO.DefaultFileStreamBufferSize,
                FileOptions.SequentialScan))
            {
                long payloadLength = stream.Length - BackendConfig.MapFile.HeaderSizeBytes;
                if (payloadLength < 0 || payloadLength > FastMapExporter.MaxPayloadBytes)
                    return EfyvParseResult.Malformed;

                byte[] header = new byte[BackendConfig.MapFile.HeaderSizeBytes];
                if (!ReadExactly(stream, header, header.Length)) return EfyvParseResult.Malformed;
                if (ReadUInt32LittleEndian(header, BackendConfig.MapFile.MagicOffset) != BackendConfig.MapFile.MagicNumber)
                    return EfyvParseResult.Malformed;
                if (ReadUInt32LittleEndian(header, BackendConfig.MapFile.VersionOffset) != (uint)BackendConfig.MapFile.FormatVersion)
                    return EfyvParseResult.Malformed;
                uint expectedChecksum = ReadUInt32LittleEndian(header, BackendConfig.MapFile.ChecksumOffset);

                payload = new byte[payloadLength];
                if (!ReadExactly(stream, payload, payload.Length)) return EfyvParseResult.Malformed;
                if (FastCrc32.Compute(payload) != expectedChecksum) return EfyvParseResult.Malformed;
            }

            MapFileData parsed = ParsePayload(payload);
            if (parsed == null) return EfyvParseResult.Malformed;
            data = parsed;
            return EfyvParseResult.Valid;
        }

        // Returns null on any structural violation. The reader is strict:
        // the payload must be consumed EXACTLY (no trailing bytes), string
        // lengths are bounded, and the shared TryValidate contract must hold.
        private static MapFileData ParsePayload(byte[] payload)
        {
            using (var buffer = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(buffer, Encoding.UTF8, true))
            {
                try
                {
                    var data = new MapFileData
                    {
                        Width = reader.ReadInt32(),
                        Height = reader.ReadInt32()
                    };
                    if (data.Width <= 0 || data.Width > BackendConfig.MapFile.MaxMapDimension) return null;
                    if (data.Height <= 0 || data.Height > BackendConfig.MapFile.MaxMapDimension) return null;
                    data.TilesetName = ReadBoundedString(reader, buffer);
                    if (data.TilesetName == null) return null;

                    long cellCount = (long)data.Width * data.Height;
                    if (buffer.Length - buffer.Position <
                        cellCount * BackendConfig.MapFile.BytesPerTile) return null;
                    data.Tiles = new short[cellCount];
                    for (long index = 0; index < cellCount; index++) data.Tiles[index] = reader.ReadInt16();

                    int propCount = reader.ReadInt32();
                    if (propCount < 0 || propCount > BackendConfig.MapFile.MaxMapProps) return null;
                    data.Props = new MapPropRecord[propCount];
                    for (int index = 0; index < propCount; index++)
                    {
                        string assetKey = ReadBoundedString(reader, buffer);
                        if (assetKey == null) return null;
                        data.Props[index] = new MapPropRecord
                        {
                            AssetKey = assetKey,
                            X = reader.ReadInt32(),
                            Y = reader.ReadInt32(),
                            Scale = reader.ReadSingle()
                        };
                    }

                    if (buffer.Position != buffer.Length) return null;
                    return FastMapExporter.TryValidate(data) ? data : null;
                }
                catch (EndOfStreamException)
                {
                    return null;
                }
            }
        }

        private static string ReadBoundedString(BinaryReader reader, MemoryStream buffer)
        {
            int byteCount = reader.ReadInt32();
            if (byteCount < 0 || byteCount > buffer.Length - buffer.Position) return null;
            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount) return null;
            return Encoding.UTF8.GetString(bytes);
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

        private static uint ReadUInt32LittleEndian(byte[] source, int offset)
        {
            return source[offset] |
                ((uint)source[offset + 1] << 8) |
                ((uint)source[offset + 2] << 16) |
                ((uint)source[offset + 3] << 24);
        }
    }
}
