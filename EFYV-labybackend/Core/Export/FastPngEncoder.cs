using System.IO;
using System.Threading;
using EFYV.Runtime.Media;

namespace EFYVBackend.Core.Export
{
    // Compatibility facade retained for game/editor callers. The implementation
    // is owned by the shared EFYV.Runtime.Media package.
    internal static class FastPngEncoder
    {
        public static void Write<T>(Stream stream, T[] pixels, int width, int height) where T : unmanaged
        {
            PngEncoder.Write(stream, pixels, width, height);
        }

        public static void Write<T>(Stream stream, T[] pixels, int width, int height, bool compressed) where T : unmanaged
        {
            PngEncoder.Write(stream, pixels, width, height, compressed);
        }

        public static void Write<T>(Stream stream, T[] pixels, int width, int height, bool compressed, CancellationToken cancellationToken) where T : unmanaged
        {
            PngEncoder.Write(stream, pixels, width, height, compressed, cancellationToken);
        }
    }
}
