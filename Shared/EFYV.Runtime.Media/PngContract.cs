namespace EFYV.Runtime.Media
{
    public static class PngContract
    {
        public const int IhdrLength = 13;
        public const int ChunkTypeLength = 4;
        public const int StoredBlockMaxLength = ushort.MaxValue;
        public const int StoredBlockHeaderLength = 5;
        public const int ZlibHeaderLength = 2;
        public const int AdlerLength = 4;
        public const int ScanlineFilterLength = 1;
        public const int RgbaChannelCount = 4;
        public const byte RgbaBitDepth = 8;
        public const byte RgbaColorType = 6;
        public const byte CompressionMethod = 0;
        public const byte FilterMethod = 0;
        public const byte InterlaceMethod = 0;
        public const byte ScanlineFilterNone = 0;
        public const byte ZlibCompressionMethodAndInfo = 0x78;
        public const byte ZlibNoCompressionFlags = 0x01;
        public const byte StoredBlockFinal = 1;
        public const byte StoredBlockContinues = 0;
        public const uint AdlerModulus = 65521u;
        public const int AdlerModuloBlockLength = 5552;
        public const uint AdlerInitialA = 1u;
        public const uint AdlerInitialB = 0u;
        public const uint InitialCrc = 0xFFFFFFFFu;
        public const uint FinalCrcMask = 0xFFFFFFFFu;
        public const uint CrcPolynomial = 0xEDB88320u;
        public const int CrcTableSize = 256;
        public const int CrcBitsPerByte = 8;
        public const uint CrcIndexMask = 0xFFu;
        public const int GreenShift = 8;
        public const int BlueShift = 16;
        public const int AlphaShift = 24;

        public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        public static readonly byte[] IhdrChunkType = { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        public static readonly byte[] IdatChunkType = { (byte)'I', (byte)'D', (byte)'A', (byte)'T' };
        public static readonly byte[] IendChunkType = { (byte)'I', (byte)'E', (byte)'N', (byte)'D' };
    }
}
