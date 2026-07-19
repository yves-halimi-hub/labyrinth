using System;
using System.IO;

namespace System.IO.Compression
{
    // Minimal zlib (RFC 1950) stream for runtimes whose BCL predates .NET 6's
    // System.IO.Compression.ZLibStream (Unity's Mono profile). Only the surface
    // EFYVBackend.Core uses is implemented:
    //   * new ZLibStream(stream, CompressionLevel, leaveOpen) + Write + Dispose
    //     -> writes the 2-byte zlib header, a deflate body, and the big-endian
    //        Adler-32 checksum of the uncompressed payload.
    //   * new ZLibStream(stream, CompressionMode.Decompress, leaveOpen) + Read
    //     -> validates the zlib header (method 8, header checksum, no preset
    //        dictionary) and inflates the deflate body. The Adler-32 trailer is
    //        not re-verified: DeflateStream buffers past the deflate body, so
    //        the trailer bytes are not reliably addressable here, and the PNG
    //        pipeline already validates chunk CRCs one layer above.
    // Malformed input surfaces as InvalidDataException, matching the real type.
    public sealed class ZLibStream : Stream
    {
        private const byte HeaderCmf = 0x78;       // deflate, 32K window
        private const byte HeaderFlg = 0x9C;       // check bits, no dict
        private const int HeaderLength = 2;
        private const int DeflateMethod = 8;
        private const int PresetDictionaryFlag = 0x20;
        private const int HeaderChecksumModulus = 31;
        private const uint AdlerModulus = 65521;
        private const int AdlerBatchLength = 5552; // max bytes before mod is required

        private readonly Stream baseStream;
        private readonly bool leaveOpen;
        private readonly bool compressing;
        private readonly CompressionLevel compressionLevel;
        private DeflateStream deflate;
        private bool headerHandled;
        private bool disposed;
        private uint adlerA = 1;
        private uint adlerB;

        public ZLibStream(Stream stream, CompressionMode mode)
            : this(stream, mode, false)
        {
        }

        public ZLibStream(Stream stream, CompressionMode mode, bool leaveOpen)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (mode != CompressionMode.Compress && mode != CompressionMode.Decompress)
                throw new ArgumentException("Unsupported compression mode.", nameof(mode));
            baseStream = stream;
            this.leaveOpen = leaveOpen;
            compressing = mode == CompressionMode.Compress;
            compressionLevel = CompressionLevel.Optimal;
        }

        public ZLibStream(Stream stream, CompressionLevel compressionLevel)
            : this(stream, compressionLevel, false)
        {
        }

        public ZLibStream(Stream stream, CompressionLevel compressionLevel, bool leaveOpen)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            baseStream = stream;
            this.leaveOpen = leaveOpen;
            compressing = true;
            this.compressionLevel = compressionLevel;
        }

        public override bool CanRead => !compressing && !disposed && baseStream.CanRead;
        public override bool CanWrite => compressing && !disposed && baseStream.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (compressing) throw new InvalidOperationException("The stream was opened for compression.");
            ThrowIfDisposed();
            EnsureInflaterReady();
            return deflate.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!compressing) throw new InvalidOperationException("The stream was opened for decompression.");
            ThrowIfDisposed();
            EnsureDeflaterReady();
            deflate.Write(buffer, offset, count);
            UpdateAdler(buffer, offset, count);
        }

        public override void Flush()
        {
            ThrowIfDisposed();
            deflate?.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposed || !disposing)
            {
                base.Dispose(disposing);
                return;
            }

            try
            {
                if (compressing)
                {
                    EnsureDeflaterReady(); // zero-write streams still emit a valid empty zlib body
                    deflate.Dispose();     // terminates the deflate bit stream
                    WriteAdlerTrailer();
                }
                else
                {
                    deflate?.Dispose();
                }
            }
            finally
            {
                disposed = true;
                if (!leaveOpen) baseStream.Dispose();
                base.Dispose(true);
            }
        }

        private void EnsureDeflaterReady()
        {
            if (headerHandled) return;
            baseStream.WriteByte(HeaderCmf);
            baseStream.WriteByte(HeaderFlg);
            deflate = new DeflateStream(baseStream, compressionLevel, leaveOpen: true);
            headerHandled = true;
        }

        private void EnsureInflaterReady()
        {
            if (headerHandled) return;
            int cmf = baseStream.ReadByte();
            int flg = baseStream.ReadByte();
            if (cmf < 0 || flg < 0)
                throw new InvalidDataException("The zlib stream is truncated before the 2-byte header.");
            if ((cmf & 0x0F) != DeflateMethod)
                throw new InvalidDataException("The zlib stream does not use the deflate compression method.");
            if (((cmf << 8) | flg) % HeaderChecksumModulus != 0)
                throw new InvalidDataException("The zlib header checksum is invalid.");
            if ((flg & PresetDictionaryFlag) != 0)
                throw new InvalidDataException("Preset dictionaries are not supported.");
            deflate = new DeflateStream(baseStream, CompressionMode.Decompress, leaveOpen: true);
            headerHandled = true;
        }

        private void UpdateAdler(byte[] buffer, int offset, int count)
        {
            int index = offset;
            int remaining = count;
            while (remaining > 0)
            {
                int batch = remaining < AdlerBatchLength ? remaining : AdlerBatchLength;
                for (int i = 0; i < batch; i++)
                {
                    adlerA += buffer[index + i];
                    adlerB += adlerA;
                }
                adlerA %= AdlerModulus;
                adlerB %= AdlerModulus;
                index += batch;
                remaining -= batch;
            }
        }

        private void WriteAdlerTrailer()
        {
            uint adler = (adlerB << 16) | adlerA;
            baseStream.WriteByte((byte)(adler >> 24));
            baseStream.WriteByte((byte)(adler >> 16));
            baseStream.WriteByte((byte)(adler >> 8));
            baseStream.WriteByte((byte)adler);
            baseStream.Flush();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ZLibStream));
        }
    }
}
