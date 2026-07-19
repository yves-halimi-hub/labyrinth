using System;
using System.IO;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.IO
{
    public static class SafePathPolicy
    {
        // Longest stem accepted anywhere in the system; the rationale lives with the
        // constant in EFYV-LabyrinthConfig.cs (Backend.IO.MaxFileStemLength).
        private const int MaxFileStemLength = BackendConfig.IO.MaxFileStemLength;

        // Path.GetInvalidFileNameChars() only rejects '/' and NUL on non-Windows
        // hosts, which would let a stem such as "a:b" or "a*b" through on Linux and
        // then fail (or alias another file) once the export lands in a Windows Unity
        // project. The stem policy therefore rejects the full Windows-invalid set on
        // every platform.
        private static readonly char[] WindowsInvalidFileNameChars =
        {
            '"', '<', '>', '|', ':', '*', '?', '\\', '/'
        };

        public static bool IsSafeFileStem(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value == BackendConfig.Exporter.CurrentDirectoryName ||
                value == BackendConfig.Exporter.ParentDirectoryName)
            {
                return false;
            }
            if (value.Length > MaxFileStemLength) return false;
            if (value.EndsWith(BackendConfig.Exporter.CurrentDirectoryName, StringComparison.Ordinal) ||
                value.EndsWith(BackendConfig.Exporter.TrailingSpace, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)) return false;
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (value.IndexOfAny(WindowsInvalidFileNameChars) >= 0) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < ' ') return false;
            }

            int extensionIndex = value.IndexOf('.');
            string baseName = (extensionIndex < 0 ? value : value.Substring(0, extensionIndex)).ToUpperInvariant();
            return baseName != BackendConfig.Exporter.ReservedCon &&
                baseName != BackendConfig.Exporter.ReservedPrn &&
                baseName != BackendConfig.Exporter.ReservedAux &&
                baseName != BackendConfig.Exporter.ReservedNul &&
                !IsNumberedReservedName(baseName, BackendConfig.Exporter.ReservedComPrefix) &&
                !IsNumberedReservedName(baseName, BackendConfig.Exporter.ReservedLptPrefix);
        }

        public static string GetContainedPath(string rootDirectory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException(null, nameof(rootDirectory));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException(null, nameof(fileName));

            string root = NormalizeRootDirectory(Path.GetFullPath(rootDirectory));
            string path = Path.GetFullPath(Path.Combine(root, fileName));
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(path), root, comparison))
                throw new ArgumentException(null, nameof(fileName));
            return path;
        }

        private static bool IsNumberedReservedName(string baseName, string prefix)
        {
            return baseName.Length == prefix.Length + BackendConfig.Exporter.ReservedDeviceSuffixLength &&
                baseName.StartsWith(prefix, StringComparison.Ordinal) &&
                baseName[prefix.Length] >= BackendConfig.Exporter.ReservedDeviceMinSuffix &&
                baseName[prefix.Length] <= BackendConfig.Exporter.ReservedDeviceMaxSuffix;
        }

        private static string NormalizeRootDirectory(string path)
        {
            string pathRoot = Path.GetPathRoot(path);
            if (path.Length <= pathRoot.Length) return path;
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
