using System.IO;
using System.Text.Json;
using EFYVBackend.Core.Models;

namespace EFYVBackend.Core.IO
{
    // Tri-state parse outcome (#16c): callers that need to react differently to
    // a file that is not there versus one that is present but broken use
    // TryParse; ParseEfyvFile keeps the historical thin contract (default on
    // missing, JsonException on malformed).
    public enum EfyvParseResult
    {
        Missing = 0,
        Malformed = 1,
        Valid = 2
    }

    public static class FastImporter
    {
        // ULTRA-PERFORMANCE JSON PARSER
        // Reads raw UTF-8 bytes directly from the hard drive into the data models.
        // Bypasses Unity's reflection-heavy JsonUtility and eliminates massive string allocations.
        public static EFYVJsonFormat ParseEfyvFile(string path)
        {
            if (!File.Exists(path)) return default;
            return ReadAndDeserialize(path);
        }

        // Distinguishes Missing (no file at all) from Malformed (present but not
        // valid JSON for the contract). I/O failures (locks, permissions) still
        // propagate: they are transient environment errors, not document states.
        public static EfyvParseResult TryParse(string path, out EFYVJsonFormat data)
        {
            data = default;
            if (!File.Exists(path)) return EfyvParseResult.Missing;

            try
            {
                data = ReadAndDeserialize(path);
            }
            catch (JsonException)
            {
                data = default;
                return EfyvParseResult.Malformed;
            }
            return EfyvParseResult.Valid;
        }

        private static EFYVJsonFormat ReadAndDeserialize(string path)
        {
            // Using FileStream allows System.Text.Json to deserialize sequentially as it reads the disk
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.IO.DefaultFileStreamBufferSize, FileOptions.SequentialScan))
            {
                return JsonSerializer.Deserialize<EFYVJsonFormat>(fs);
            }
        }
    }
}
