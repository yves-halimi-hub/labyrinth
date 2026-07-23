using System;
using EFYV.Runtime.Media;

namespace EFYVBackend.Core.IO
{
    // Compatibility facade retained for save/map/import callers. CRC ownership
    // now lives in the generic runtime-media package.
    public static class FastCrc32
    {
        public static uint Compute(ReadOnlySpan<byte> data) => Crc32.Compute(data);

        public static uint Update(uint crc, ReadOnlySpan<byte> data) => RuntimeMediaKernel.UpdateCrc32(crc, data);
    }
}
