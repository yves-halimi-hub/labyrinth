using System.IO;
using System.Text.Json;
using EFYVBackend.Core.Models;

namespace EFYVBackend.Core.IO
{
    public static class FastImporter
    {
        // ULTRA-PERFORMANCE JSON PARSER
        // Reads raw UTF-8 bytes directly from the hard drive into the data models.
        // Bypasses Unity's reflection-heavy JsonUtility and eliminates massive string allocations.
        public static EFYVJsonFormat ParseEfyvFile(string path)
        {
            if (!File.Exists(path)) return default;
            
            // Using FileStream allows System.Text.Json to deserialize sequentially as it reads the disk
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.IO.DefaultFileStreamBufferSize, FileOptions.SequentialScan))
            {
                return JsonSerializer.Deserialize<EFYVJsonFormat>(fs);
            }
        }
    }
}
