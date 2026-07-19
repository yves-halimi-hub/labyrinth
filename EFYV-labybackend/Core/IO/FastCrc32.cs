using System;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.IO
{
    // Shared CRC-32 (ISO-HDLC, the PNG polynomial): the ONE table in the
    // backend. Consumed by the FastSaveEngine header and by
    // FastPngEncoder/FastPngDecoder chunk checksums (their former private
    // table copies were folded onto this class - batch-2 deferred nit).
    // The polynomial/table constants are single-sourced from the PNG section
    // of the config because they are the same standard CRC-32.
    public static class FastCrc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            return Update(BackendConfig.Exporter.Png.InitialCrc, data) ^
                BackendConfig.Exporter.Png.FinalCrcMask;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                crc = Table[(crc ^ data[i]) & BackendConfig.Exporter.Png.CrcIndexMask] ^ (crc >> 8);
            }
            return crc;
        }

        private static uint[] CreateTable()
        {
            uint[] table = new uint[BackendConfig.Exporter.Png.CrcTableSize];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < BackendConfig.Exporter.Png.CrcBitsPerByte; bit++)
                {
                    value = (value & 1u) != 0
                        ? BackendConfig.Exporter.Png.CrcPolynomial ^ (value >> 1)
                        : value >> 1;
                }
                table[i] = value;
            }
            return table;
        }
    }
}
