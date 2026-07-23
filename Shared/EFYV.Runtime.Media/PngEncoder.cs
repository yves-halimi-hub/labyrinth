using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace EFYV.Runtime.Media
{
    public static class PngEncoder
    {
        public static unsafe void Write<T>(Stream stream, T[] pixels, int width, int height) where T : unmanaged
        {
            Write(stream, pixels, width, height, true, CancellationToken.None);
        }

        public static unsafe void Write<T>(Stream stream, T[] pixels, int width, int height, bool compressed) where T : unmanaged
        {
            Write(stream, pixels, width, height, compressed, CancellationToken.None);
        }

        public static unsafe void Write<T>(Stream stream, T[] pixels, int width, int height, bool compressed, CancellationToken cancellationToken) where T : unmanaged
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException(null, nameof(stream));
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (sizeof(T) != sizeof(uint)) throw new ArgumentException(null, nameof(pixels));
            if (pixels.Length != checked(width * height)) throw new ArgumentException(null, nameof(pixels));

            cancellationToken.ThrowIfCancellationRequested();
            stream.Write(PngContract.Signature, 0, PngContract.Signature.Length);
            WriteIhdr(stream, width, height);
            fixed (T* pixelPointer = pixels)
            {
                if (compressed) WriteIdatCompressed(stream, (uint*)pixelPointer, width, height, cancellationToken);
                else WriteIdatStored(stream, (uint*)pixelPointer, width, height, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            WriteChunk(stream, PngContract.IendChunkType, Array.Empty<byte>());
        }

        private static void WriteIhdr(Stream stream, int width, int height)
        {
            byte[] data = new byte[PngContract.IhdrLength];
            WriteUInt32BigEndian(data, 0, (uint)width);
            WriteUInt32BigEndian(data, 4, (uint)height);
            data[8] = PngContract.RgbaBitDepth;
            data[9] = PngContract.RgbaColorType;
            data[10] = PngContract.CompressionMethod;
            data[11] = PngContract.FilterMethod;
            data[12] = PngContract.InterlaceMethod;
            WriteChunk(stream, PngContract.IhdrChunkType, data);
        }

        private static unsafe void WriteIdatCompressed(Stream stream, uint* pixels, int width, int height, CancellationToken cancellationToken)
        {
            int rowLength = checked(width * PngContract.RgbaChannelCount + PngContract.ScanlineFilterLength);
            byte[] rowBuffer = ArrayPool<byte>.Shared.Rent(rowLength);
            try
            {
                using (var compressed = new MemoryStream())
                {
                    using (var deflater = new ZLibStream(compressed, CompressionLevel.Fastest, true))
                    {
                        int pixelIndex = 0;
                        for (int y = 0; y < height; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            rowBuffer[0] = PngContract.ScanlineFilterNone;
                            int rowIndex = PngContract.ScanlineFilterLength;
                            for (int x = 0; x < width; x++)
                            {
                                uint packed = pixels[pixelIndex++];
                                rowBuffer[rowIndex] = (byte)packed;
                                rowBuffer[rowIndex + 1] = (byte)(packed >> PngContract.GreenShift);
                                rowBuffer[rowIndex + 2] = (byte)(packed >> PngContract.BlueShift);
                                rowBuffer[rowIndex + 3] = (byte)(packed >> PngContract.AlphaShift);
                                rowIndex += PngContract.RgbaChannelCount;
                            }
                            deflater.Write(rowBuffer, 0, rowLength);
                        }
                    }
                    if (compressed.Length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
                    WriteChunk(stream, PngContract.IdatChunkType, compressed.GetBuffer(), (int)compressed.Length);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rowBuffer);
            }
        }

        private static unsafe void WriteIdatStored(Stream stream, uint* pixels, int width, int height, CancellationToken cancellationToken)
        {
            long rowLength = checked((long)width * PngContract.RgbaChannelCount + PngContract.ScanlineFilterLength);
            long rawLength = checked(rowLength * height);
            long blockCount = (rawLength + PngContract.StoredBlockMaxLength - 1L) / PngContract.StoredBlockMaxLength;
            long idatLength = checked(PngContract.ZlibHeaderLength + rawLength + blockCount * PngContract.StoredBlockHeaderLength + PngContract.AdlerLength);
            if (idatLength > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));

            WriteUInt32BigEndian(stream, (uint)idatLength);
            stream.Write(PngContract.IdatChunkType, 0, PngContract.IdatChunkType.Length);
            uint crc = RuntimeMediaKernel.UpdateCrc32(PngContract.InitialCrc, PngContract.IdatChunkType);
            byte[] zlibHeader = { PngContract.ZlibCompressionMethodAndInfo, PngContract.ZlibNoCompressionFlags };
            WriteWithCrc(stream, zlibHeader, zlibHeader.Length, ref crc);

            byte[] blockBuffer = ArrayPool<byte>.Shared.Rent(PngContract.StoredBlockMaxLength);
            uint adlerA = PngContract.AdlerInitialA;
            uint adlerB = PngContract.AdlerInitialB;
            int pixelIndex = 0;
            int rowPixelIndex = 0;
            int channel = 0;
            bool filterPending = true;
            int adlerModuloByteCount = 0;
            long rawRemaining = rawLength;
            byte[] blockHeader = new byte[PngContract.StoredBlockHeaderLength];
            try
            {
                while (rawRemaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int blockLength = (int)System.Math.Min(PngContract.StoredBlockMaxLength, rawRemaining);
                    bool finalBlock = rawRemaining == blockLength;
                    blockHeader[0] = finalBlock ? PngContract.StoredBlockFinal : PngContract.StoredBlockContinues;
                    blockHeader[1] = (byte)blockLength;
                    blockHeader[2] = (byte)(blockLength >> 8);
                    ushort inverseLength = (ushort)~blockLength;
                    blockHeader[3] = (byte)inverseLength;
                    blockHeader[4] = (byte)(inverseLength >> 8);
                    WriteWithCrc(stream, blockHeader, blockHeader.Length, ref crc);

                    FillRawBlock(pixels, width, blockBuffer, blockLength, ref pixelIndex, ref rowPixelIndex,
                        ref channel, ref filterPending, ref adlerA, ref adlerB, ref adlerModuloByteCount);
                    WriteWithCrc(stream, blockBuffer, blockLength, ref crc);
                    rawRemaining -= blockLength;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(blockBuffer);
            }

            adlerA %= PngContract.AdlerModulus;
            adlerB %= PngContract.AdlerModulus;
            uint adler = (adlerB << 16) | adlerA;
            byte[] adlerBytes = new byte[PngContract.AdlerLength];
            WriteUInt32BigEndian(adlerBytes, 0, adler);
            WriteWithCrc(stream, adlerBytes, adlerBytes.Length, ref crc);
            WriteUInt32BigEndian(stream, crc ^ PngContract.FinalCrcMask);
        }

        private static unsafe void FillRawBlock(
            uint* pixels, int width, byte[] destination, int count, ref int pixelIndex,
            ref int rowPixelIndex, ref int channel, ref bool filterPending,
            ref uint adlerA, ref uint adlerB, ref int adlerModuloByteCount)
        {
            for (int i = 0; i < count; i++)
            {
                byte value;
                if (filterPending)
                {
                    value = PngContract.ScanlineFilterNone;
                    filterPending = false;
                }
                else
                {
                    uint packed = pixels[pixelIndex];
                    if (channel == 0) value = (byte)packed;
                    else if (channel == 1) value = (byte)(packed >> PngContract.GreenShift);
                    else if (channel == 2) value = (byte)(packed >> PngContract.BlueShift);
                    else value = (byte)(packed >> PngContract.AlphaShift);
                    channel++;
                    if (channel == PngContract.RgbaChannelCount)
                    {
                        channel = 0;
                        pixelIndex++;
                        rowPixelIndex++;
                        if (rowPixelIndex == width) { rowPixelIndex = 0; filterPending = true; }
                    }
                }
                destination[i] = value;
                adlerA += value;
                adlerB += adlerA;
                adlerModuloByteCount++;
                if (adlerModuloByteCount == PngContract.AdlerModuloBlockLength)
                {
                    adlerA %= PngContract.AdlerModulus;
                    adlerB %= PngContract.AdlerModulus;
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
            if (type == null || type.Length != PngContract.ChunkTypeLength) throw new ArgumentException(null, nameof(type));
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteUInt32BigEndian(stream, (uint)count);
            stream.Write(type, 0, type.Length);
            if (count > 0) stream.Write(data, 0, count);
            uint crc = RuntimeMediaKernel.UpdateCrc32(PngContract.InitialCrc, type);
            crc = RuntimeMediaKernel.UpdateCrc32(crc, new ReadOnlySpan<byte>(data, 0, count));
            WriteUInt32BigEndian(stream, crc ^ PngContract.FinalCrcMask);
        }

        private static void WriteWithCrc(Stream stream, byte[] data, int count, ref uint crc)
        {
            stream.Write(data, 0, count);
            crc = RuntimeMediaKernel.UpdateCrc32(crc, new ReadOnlySpan<byte>(data, 0, count));
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
