using System;
using System.IO;
using System.IO.Compression;
using EFYVBackend.Core.IO;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Export
{
    public static class FastPngDecoder
    {
        // The scanline filter identifiers defined by the PNG specification. Only the
        // filter METHOD byte lives in EFYV-LabyrinthConfig (the encoder writes filter
        // None exclusively); the decoder must understand all five per-row types.
        private const byte FilterNone = 0;
        private const byte FilterSub = 1;
        private const byte FilterUp = 2;
        private const byte FilterAverage = 3;
        private const byte FilterPaeth = 4;

        private const int ChunkLengthFieldLength = 4;
        private const int ChunkCrcFieldLength = 4;

        public static uint[] Read(Stream stream, out int width, out int height)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("The PNG stream must be readable.", nameof(stream));
            using (MemoryStream buffered = new MemoryStream())
            {
                stream.CopyTo(buffered);
                return Read(buffered.GetBuffer(), (int)buffered.Length, out width, out height);
            }
        }

        public static uint[] Read(byte[] pngBytes, out int width, out int height)
        {
            if (pngBytes == null) throw new ArgumentNullException(nameof(pngBytes));
            return Read(pngBytes, pngBytes.Length, out width, out height);
        }

        private static uint[] Read(byte[] bytes, int length, out int width, out int height)
        {
            byte[] signature = BackendConfig.Exporter.Png.Signature;
            if (length < signature.Length) throw new ArgumentException("The data is shorter than a PNG signature.", nameof(bytes));
            for (int i = 0; i < signature.Length; i++)
            {
                if (bytes[i] != signature[i]) throw new ArgumentException("The data does not start with the PNG signature.", nameof(bytes));
            }

            int offset = signature.Length;
            bool sawHeader = false;
            bool sawEnd = false;
            width = 0;
            height = 0;
            using (MemoryStream idat = new MemoryStream())
            {
                while (offset < length)
                {
                    ReadChunkHeader(bytes, length, offset, out int dataLength, out byte[] chunkType);
                    int dataOffset = offset + ChunkLengthFieldLength + BackendConfig.Exporter.Png.ChunkTypeLength;
                    ValidateChunkCrc(bytes, offset, dataLength);

                    if (IsChunkType(chunkType, BackendConfig.Exporter.Png.IhdrChunkType))
                    {
                        if (sawHeader) throw new ArgumentException("The PNG contains more than one IHDR chunk.", nameof(bytes));
                        ReadHeader(bytes, dataOffset, dataLength, out width, out height);
                        sawHeader = true;
                    }
                    else if (!sawHeader)
                    {
                        throw new ArgumentException("The first PNG chunk must be IHDR.", nameof(bytes));
                    }
                    else if (IsChunkType(chunkType, BackendConfig.Exporter.Png.IdatChunkType))
                    {
                        idat.Write(bytes, dataOffset, dataLength);
                    }
                    else if (IsChunkType(chunkType, BackendConfig.Exporter.Png.IendChunkType))
                    {
                        if (dataLength != 0) throw new ArgumentException("The PNG IEND chunk must be empty.", nameof(bytes));
                        sawEnd = true;
                        offset = dataOffset + ChunkCrcFieldLength;
                        break;
                    }
                    // Other ancillary chunks are CRC-validated and skipped.

                    offset = dataOffset + dataLength + ChunkCrcFieldLength;
                }

                if (!sawHeader) throw new ArgumentException("The PNG contains no IHDR chunk.", nameof(bytes));
                if (!sawEnd) throw new ArgumentException("The PNG is truncated before its IEND chunk.", nameof(bytes));
                if (offset != length) throw new ArgumentException("The PNG has trailing data after its IEND chunk.", nameof(bytes));
                if (idat.Length == 0) throw new ArgumentException("The PNG contains no IDAT data.", nameof(bytes));

                byte[] raw = Inflate(idat, width, height);
                Unfilter(raw, width, height);
                return PackPixels(raw, width, height);
            }
        }

        private static void ReadChunkHeader(byte[] bytes, int length, int offset, out int dataLength, out byte[] chunkType)
        {
            int headerLength = ChunkLengthFieldLength + BackendConfig.Exporter.Png.ChunkTypeLength;
            if (offset + headerLength > length) throw new ArgumentException("A PNG chunk header is truncated.", nameof(bytes));
            uint declaredLength = ReadUInt32BigEndian(bytes, offset);
            if (declaredLength > int.MaxValue) throw new ArgumentException("A PNG chunk declares an unsupported length.", nameof(bytes));
            dataLength = (int)declaredLength;
            if (offset + headerLength + (long)dataLength + ChunkCrcFieldLength > length)
                throw new ArgumentException("A PNG chunk is truncated.", nameof(bytes));

            chunkType = new byte[BackendConfig.Exporter.Png.ChunkTypeLength];
            Buffer.BlockCopy(bytes, offset + ChunkLengthFieldLength, chunkType, 0, chunkType.Length);
        }

        private static void ValidateChunkCrc(byte[] bytes, int chunkOffset, int dataLength)
        {
            int typeOffset = chunkOffset + ChunkLengthFieldLength;
            int crcOffset = typeOffset + BackendConfig.Exporter.Png.ChunkTypeLength + dataLength;
            uint crc = UpdateCrc(
                BackendConfig.Exporter.Png.InitialCrc,
                bytes,
                typeOffset,
                BackendConfig.Exporter.Png.ChunkTypeLength + dataLength);
            crc ^= BackendConfig.Exporter.Png.FinalCrcMask;
            if (crc != ReadUInt32BigEndian(bytes, crcOffset))
                throw new ArgumentException("A PNG chunk failed CRC validation.", nameof(bytes));
        }

        private static void ReadHeader(byte[] bytes, int dataOffset, int dataLength, out int width, out int height)
        {
            if (dataLength != BackendConfig.Exporter.Png.IhdrLength)
                throw new ArgumentException("The PNG IHDR chunk has an invalid length.", nameof(bytes));

            uint declaredWidth = ReadUInt32BigEndian(bytes, dataOffset);
            uint declaredHeight = ReadUInt32BigEndian(bytes, dataOffset + 4);
            if (declaredWidth == 0 || declaredWidth > int.MaxValue)
                throw new ArgumentException("The PNG width is out of range.", nameof(bytes));
            if (declaredHeight == 0 || declaredHeight > int.MaxValue)
                throw new ArgumentException("The PNG height is out of range.", nameof(bytes));
            width = (int)declaredWidth;
            height = (int)declaredHeight;
            if ((long)width * height > int.MaxValue)
                throw new ArgumentException("The PNG pixel count is unsupported.", nameof(bytes));
            if (RowLength(width) * (long)height > int.MaxValue)
                throw new ArgumentException("The PNG scanline data size is unsupported.", nameof(bytes));

            if (bytes[dataOffset + 8] != BackendConfig.Exporter.Png.RgbaBitDepth)
                throw new ArgumentException("Only 8-bit-per-channel PNG data is supported.", nameof(bytes));
            if (bytes[dataOffset + 9] != BackendConfig.Exporter.Png.RgbaColorType)
                throw new ArgumentException("Only truecolor-with-alpha (color type 6) PNG data is supported.", nameof(bytes));
            if (bytes[dataOffset + 10] != BackendConfig.Exporter.Png.CompressionMethod)
                throw new ArgumentException("Only PNG compression method 0 is supported.", nameof(bytes));
            if (bytes[dataOffset + 11] != BackendConfig.Exporter.Png.FilterMethod)
                throw new ArgumentException("Only PNG filter method 0 is supported.", nameof(bytes));
            if (bytes[dataOffset + 12] != BackendConfig.Exporter.Png.InterlaceMethod)
                throw new ArgumentException("Interlaced PNG data is not supported.", nameof(bytes));
        }

        private static byte[] Inflate(MemoryStream idat, int width, int height)
        {
            int expectedLength = (int)(RowLength(width) * height);
            byte[] raw = new byte[expectedLength];
            idat.Position = 0;
            try
            {
                using (ZLibStream inflater = new ZLibStream(idat, CompressionMode.Decompress, true))
                {
                    int produced = 0;
                    while (produced < expectedLength)
                    {
                        int read = inflater.Read(raw, produced, expectedLength - produced);
                        if (read == BackendConfig.IO.EndOfStreamReadCount)
                            throw new ArgumentException("The PNG IDAT data inflates to fewer bytes than the image requires.", nameof(idat));
                        produced += read;
                    }
                    byte[] probe = new byte[1];
                    if (inflater.Read(probe, 0, probe.Length) != BackendConfig.IO.EndOfStreamReadCount)
                        throw new ArgumentException("The PNG IDAT data inflates to more bytes than the image requires.", nameof(idat));
                }
            }
            catch (InvalidDataException exception)
            {
                throw new ArgumentException("The PNG IDAT data is not a valid zlib stream.", nameof(idat), exception);
            }
            return raw;
        }

        private static void Unfilter(byte[] raw, int width, int height)
        {
            int bytesPerPixel = BackendConfig.Exporter.Png.RgbaChannelCount;
            int rowLength = (int)RowLength(width);
            int stride = width * bytesPerPixel;
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * rowLength + BackendConfig.Exporter.Png.ScanlineFilterLength;
                int previousRowStart = rowStart - rowLength;
                byte filter = raw[rowStart - BackendConfig.Exporter.Png.ScanlineFilterLength];
                switch (filter)
                {
                    case FilterNone:
                        break;
                    case FilterSub:
                        for (int i = bytesPerPixel; i < stride; i++)
                        {
                            raw[rowStart + i] = (byte)(raw[rowStart + i] + raw[rowStart + i - bytesPerPixel]);
                        }
                        break;
                    case FilterUp:
                        if (y > 0)
                        {
                            for (int i = 0; i < stride; i++)
                            {
                                raw[rowStart + i] = (byte)(raw[rowStart + i] + raw[previousRowStart + i]);
                            }
                        }
                        break;
                    case FilterAverage:
                        for (int i = 0; i < stride; i++)
                        {
                            int left = i >= bytesPerPixel ? raw[rowStart + i - bytesPerPixel] : 0;
                            int above = y > 0 ? raw[previousRowStart + i] : 0;
                            raw[rowStart + i] = (byte)(raw[rowStart + i] + ((left + above) >> 1));
                        }
                        break;
                    case FilterPaeth:
                        for (int i = 0; i < stride; i++)
                        {
                            int left = i >= bytesPerPixel ? raw[rowStart + i - bytesPerPixel] : 0;
                            int above = y > 0 ? raw[previousRowStart + i] : 0;
                            int aboveLeft = y > 0 && i >= bytesPerPixel ? raw[previousRowStart + i - bytesPerPixel] : 0;
                            raw[rowStart + i] = (byte)(raw[rowStart + i] + PaethPredictor(left, above, aboveLeft));
                        }
                        break;
                    default:
                        throw new ArgumentException("A PNG scanline declares an unknown filter type.", nameof(raw));
                }
            }
        }

        private static int PaethPredictor(int left, int above, int aboveLeft)
        {
            int estimate = left + above - aboveLeft;
            int distanceLeft = System.Math.Abs(estimate - left);
            int distanceAbove = System.Math.Abs(estimate - above);
            int distanceAboveLeft = System.Math.Abs(estimate - aboveLeft);
            if (distanceLeft <= distanceAbove && distanceLeft <= distanceAboveLeft) return left;
            if (distanceAbove <= distanceAboveLeft) return above;
            return aboveLeft;
        }

        private static uint[] PackPixels(byte[] raw, int width, int height)
        {
            uint[] pixels = new uint[checked(width * height)];
            int rowLength = (int)RowLength(width);
            int pixelIndex = 0;
            for (int y = 0; y < height; y++)
            {
                int rawIndex = y * rowLength + BackendConfig.Exporter.Png.ScanlineFilterLength;
                for (int x = 0; x < width; x++)
                {
                    pixels[pixelIndex++] = raw[rawIndex] |
                        ((uint)raw[rawIndex + 1] << BackendConfig.Pixel.GreenShift) |
                        ((uint)raw[rawIndex + 2] << BackendConfig.Pixel.BlueShift) |
                        ((uint)raw[rawIndex + 3] << BackendConfig.Pixel.AlphaShift);
                    rawIndex += BackendConfig.Exporter.Png.RgbaChannelCount;
                }
            }
            return pixels;
        }

        private static long RowLength(int width)
        {
            return (long)width * BackendConfig.Exporter.Png.RgbaChannelCount +
                BackendConfig.Exporter.Png.ScanlineFilterLength;
        }

        private static bool IsChunkType(byte[] chunkType, byte[] expected)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (chunkType[i] != expected[i]) return false;
            }
            return true;
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) |
                ((uint)bytes[offset + 1] << 16) |
                ((uint)bytes[offset + 2] << 8) |
                bytes[offset + 3];
        }

        // Single-sourced CRC-32 (batch-2 deferred nit): delegates to the shared
        // Core/IO/FastCrc32 instead of a private table copy.
        private static uint UpdateCrc(uint crc, byte[] data, int offset, int count)
        {
            return FastCrc32.Update(crc, new ReadOnlySpan<byte>(data, offset, count));
        }
    }
}
