using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using EFYVBackend.Core.IO;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Export
{
    internal static class FastPngEncoder
    {

        public static unsafe void Write<T>(Stream stream, T[] pixels, int width, int height) where T : unmanaged
        {
            Write(stream, pixels, width, height, true);
        }

        public static unsafe void Write<T>(Stream stream, T[] pixels, int width, int height, bool compressed) where T : unmanaged
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException(null, nameof(stream));
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (sizeof(T) != sizeof(uint)) throw new ArgumentException(null, nameof(pixels));
            if (pixels.Length != checked(width * height)) throw new ArgumentException(null, nameof(pixels));

            stream.Write(BackendConfig.Exporter.Png.Signature, 0, BackendConfig.Exporter.Png.Signature.Length);
            WriteIhdr(stream, width, height);
            fixed (T* pixelPointer = pixels)
            {
                if (compressed) WriteIdatCompressed(stream, (uint*)pixelPointer, width, height);
                else WriteIdatStored(stream, (uint*)pixelPointer, width, height);
            }
            WriteChunk(stream, BackendConfig.Exporter.Png.IendChunkType, Array.Empty<byte>());
        }

        private static void WriteIhdr(Stream stream, int width, int height)
        {
            byte[] data = new byte[BackendConfig.Exporter.Png.IhdrLength];
            WriteUInt32BigEndian(data, 0, (uint)width);
            WriteUInt32BigEndian(data, 4, (uint)height);
            data[8] = BackendConfig.Exporter.Png.RgbaBitDepth;
            data[9] = BackendConfig.Exporter.Png.RgbaColorType;
            data[10] = BackendConfig.Exporter.Png.CompressionMethod;
            data[11] = BackendConfig.Exporter.Png.FilterMethod;
            data[12] = BackendConfig.Exporter.Png.InterlaceMethod;
            WriteChunk(stream, BackendConfig.Exporter.Png.IhdrChunkType, data);
        }

        private static unsafe void WriteIdatCompressed(Stream stream, uint* pixels, int width, int height)
        {
            int rowLength = checked(
                width * BackendConfig.Exporter.Png.RgbaChannelCount +
                BackendConfig.Exporter.Png.ScanlineFilterLength);
            byte[] rowBuffer = ArrayPool<byte>.Shared.Rent(rowLength);
            try
            {
                using (MemoryStream compressed = new MemoryStream())
                {
                    using (ZLibStream deflater = new ZLibStream(compressed, CompressionLevel.Fastest, true))
                    {
                        int pixelIndex = 0;
                        for (int y = 0; y < height; y++)
                        {
                            rowBuffer[0] = BackendConfig.Exporter.Png.ScanlineFilterNone;
                            int rowIndex = BackendConfig.Exporter.Png.ScanlineFilterLength;
                            for (int x = 0; x < width; x++)
                            {
                                uint packed = pixels[pixelIndex++];
                                rowBuffer[rowIndex] = (byte)packed;
                                rowBuffer[rowIndex + 1] = (byte)(packed >> BackendConfig.Pixel.GreenShift);
                                rowBuffer[rowIndex + 2] = (byte)(packed >> BackendConfig.Pixel.BlueShift);
                                rowBuffer[rowIndex + 3] = (byte)(packed >> BackendConfig.Pixel.AlphaShift);
                                rowIndex += BackendConfig.Exporter.Png.RgbaChannelCount;
                            }
                            deflater.Write(rowBuffer, 0, rowLength);
                        }
                    }
                    if (compressed.Length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
                    WriteChunk(
                        stream,
                        BackendConfig.Exporter.Png.IdatChunkType,
                        compressed.GetBuffer(),
                        (int)compressed.Length);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rowBuffer);
            }
        }

        private static unsafe void WriteIdatStored(Stream stream, uint* pixels, int width, int height)
        {
            long rowLength = checked((long)width * BackendConfig.Exporter.Png.RgbaChannelCount + BackendConfig.Exporter.Png.ScanlineFilterLength);
            long rawLength = checked(rowLength * height);
            long blockCount = (rawLength + BackendConfig.Exporter.Png.StoredBlockMaxLength - 1L) / BackendConfig.Exporter.Png.StoredBlockMaxLength;
            long idatLength = checked(
                BackendConfig.Exporter.Png.ZlibHeaderLength + rawLength +
                blockCount * BackendConfig.Exporter.Png.StoredBlockHeaderLength +
                BackendConfig.Exporter.Png.AdlerLength);
            if (idatLength > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));

            WriteUInt32BigEndian(stream, (uint)idatLength);
            stream.Write(BackendConfig.Exporter.Png.IdatChunkType, 0, BackendConfig.Exporter.Png.IdatChunkType.Length);
            uint crc = UpdateCrc(
                BackendConfig.Exporter.Png.InitialCrc,
                BackendConfig.Exporter.Png.IdatChunkType,
                0,
                BackendConfig.Exporter.Png.IdatChunkType.Length);

            byte[] zlibHeader = {
                BackendConfig.Exporter.Png.ZlibCompressionMethodAndInfo,
                BackendConfig.Exporter.Png.ZlibNoCompressionFlags
            };
            WriteWithCrc(stream, zlibHeader, zlibHeader.Length, ref crc);

            byte[] blockBuffer = ArrayPool<byte>.Shared.Rent(BackendConfig.Exporter.Png.StoredBlockMaxLength);
            uint adlerA = BackendConfig.Exporter.Png.AdlerInitialA;
            uint adlerB = BackendConfig.Exporter.Png.AdlerInitialB;
            int pixelIndex = 0;
            int rowPixelIndex = 0;
            int channel = 0;
            bool filterPending = true;
            int adlerModuloByteCount = 0;
            long rawRemaining = rawLength;
            byte[] blockHeader = new byte[BackendConfig.Exporter.Png.StoredBlockHeaderLength];
            try
            {
                while (rawRemaining > 0)
                {
                    int blockLength = (int)System.Math.Min(BackendConfig.Exporter.Png.StoredBlockMaxLength, rawRemaining);
                    bool finalBlock = rawRemaining == blockLength;
                    blockHeader[0] = finalBlock ? BackendConfig.Exporter.Png.StoredBlockFinal : BackendConfig.Exporter.Png.StoredBlockContinues;
                    blockHeader[1] = (byte)blockLength;
                    blockHeader[2] = (byte)(blockLength >> 8);
                    ushort inverseLength = (ushort)~blockLength;
                    blockHeader[3] = (byte)inverseLength;
                    blockHeader[4] = (byte)(inverseLength >> 8);
                    WriteWithCrc(stream, blockHeader, blockHeader.Length, ref crc);

                    FillRawBlock(
                        pixels,
                        width,
                        blockBuffer,
                        blockLength,
                        ref pixelIndex,
                        ref rowPixelIndex,
                        ref channel,
                        ref filterPending,
                        ref adlerA,
                        ref adlerB,
                        ref adlerModuloByteCount);
                    WriteWithCrc(stream, blockBuffer, blockLength, ref crc);
                    rawRemaining -= blockLength;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(blockBuffer);
            }

            adlerA %= BackendConfig.Exporter.Png.AdlerModulus;
            adlerB %= BackendConfig.Exporter.Png.AdlerModulus;
            uint adler = (adlerB << 16) | adlerA;
            byte[] adlerBytes = new byte[BackendConfig.Exporter.Png.AdlerLength];
            WriteUInt32BigEndian(adlerBytes, 0, adler);
            WriteWithCrc(stream, adlerBytes, adlerBytes.Length, ref crc);
            WriteUInt32BigEndian(stream, crc ^ BackendConfig.Exporter.Png.FinalCrcMask);
        }

        private static unsafe void FillRawBlock(
            uint* pixels,
            int width,
            byte[] destination,
            int count,
            ref int pixelIndex,
            ref int rowPixelIndex,
            ref int channel,
            ref bool filterPending,
            ref uint adlerA,
            ref uint adlerB,
            ref int adlerModuloByteCount)
        {
            for (int i = 0; i < count; i++)
            {
                byte value;
                if (filterPending)
                {
                    value = BackendConfig.Exporter.Png.ScanlineFilterNone;
                    filterPending = false;
                }
                else
                {
                    uint packed = pixels[pixelIndex];
                    if (channel == 0) value = (byte)packed;
                    else if (channel == 1) value = (byte)(packed >> BackendConfig.Pixel.GreenShift);
                    else if (channel == 2) value = (byte)(packed >> BackendConfig.Pixel.BlueShift);
                    else value = (byte)(packed >> BackendConfig.Pixel.AlphaShift);

                    channel++;
                    if (channel == BackendConfig.Exporter.Png.RgbaChannelCount)
                    {
                        channel = 0;
                        pixelIndex++;
                        rowPixelIndex++;
                        if (rowPixelIndex == width)
                        {
                            rowPixelIndex = 0;
                            filterPending = true;
                        }
                    }
                }

                destination[i] = value;
                adlerA += value;
                adlerB += adlerA;
                adlerModuloByteCount++;
                if (adlerModuloByteCount == BackendConfig.Exporter.Png.AdlerModuloBlockLength)
                {
                    adlerA %= BackendConfig.Exporter.Png.AdlerModulus;
                    adlerB %= BackendConfig.Exporter.Png.AdlerModulus;
                    adlerModuloByteCount = 0;
                }
            }
        }

        private static void WriteChunk(Stream stream, byte[] type, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteChunk(stream, type, data, data.Length);
        }

        private static void WriteChunk(Stream stream, byte[] type, byte[] data, int count)
        {
            if (type == null || type.Length != BackendConfig.Exporter.Png.ChunkTypeLength) throw new ArgumentException(null, nameof(type));
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteUInt32BigEndian(stream, (uint)count);
            stream.Write(type, 0, type.Length);
            if (count > 0) stream.Write(data, 0, count);

            uint crc = UpdateCrc(BackendConfig.Exporter.Png.InitialCrc, type, 0, type.Length);
            crc = UpdateCrc(crc, data, 0, count);
            WriteUInt32BigEndian(stream, crc ^ BackendConfig.Exporter.Png.FinalCrcMask);
        }

        private static void WriteWithCrc(Stream stream, byte[] data, int count, ref uint crc)
        {
            stream.Write(data, 0, count);
            crc = UpdateCrc(crc, data, 0, count);
        }

        // Single-sourced CRC-32 (batch-2 deferred nit): the encoder no longer
        // carries a private table copy - it delegates to the shared
        // Core/IO/FastCrc32 used by the save-engine header and the decoder.
        private static uint UpdateCrc(uint crc, byte[] data, int offset, int count)
        {
            return FastCrc32.Update(crc, new ReadOnlySpan<byte>(data, offset, count));
        }

        private static void WriteUInt32BigEndian(Stream stream, uint value)
        {
            byte[] bytes = new byte[4];
            WriteUInt32BigEndian(bytes, 0, value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteUInt32BigEndian(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }
    }
}
