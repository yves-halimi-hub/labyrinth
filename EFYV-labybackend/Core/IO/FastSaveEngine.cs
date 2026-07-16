using System.IO;
using System;
using EFYVBackend.Core.Data; // Hooking into the unified schemas
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.IO
{
    public static class FastSaveEngine
    {
        // ULTRA-PERFORMANCE BINARY SAVE
        // Dumps the exact struct layout from RAM straight onto the Hard Drive.
        // Zero allocations, zero parsing, perfectly instant.
        public static unsafe void SaveGame(string path, ref PlayerMetaSchema data)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BackendConfig.IO.DefaultFileStreamBufferSize, FileOptions.SequentialScan))
            {
                int size = sizeof(PlayerMetaSchema);
                
                // Pin the struct in memory and write the raw bytes to the stream
                fixed (PlayerMetaSchema* ptr = &data)
                {
                    ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(ptr, size);
                    fs.Write(span);
                }
            }
        }

        // ULTRA-PERFORMANCE BINARY LOAD
        public static unsafe bool LoadGame(string path, out PlayerMetaSchema data)
        {
            data = PlayerMetaSchema.Default();
            if (!File.Exists(path)) return false;

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BackendConfig.IO.DefaultFileStreamBufferSize, FileOptions.SequentialScan))
            {
                int size = sizeof(PlayerMetaSchema);
                
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
                }
            }
            return true;
        }
    }
}
