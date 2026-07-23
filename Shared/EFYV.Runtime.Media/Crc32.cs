using System;

namespace EFYV.Runtime.Media
{
    public static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            return Update(PngContract.InitialCrc, data) ^ PngContract.FinalCrcMask;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
                crc = Table[(crc ^ data[i]) & PngContract.CrcIndexMask] ^ (crc >> 8);
            return crc;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[PngContract.CrcTableSize];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < PngContract.CrcBitsPerByte; bit++)
                    value = (value & 1u) != 0 ? PngContract.CrcPolynomial ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }
    }
}
