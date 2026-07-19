using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EFYVLabyMake.Core.Models;

namespace EFYVLabyMake.App.UI
{
    // Shared straight-RGBA (red in the low byte, the PixelColor layout) ->
    // unpremultiplied BGRA WriteableBitmap builder used by the thumbnail lists
    // (asset bank, tileset tiles, palette swatches, layer previews) and the
    // preview panel canvas. Nearest-neighbor so the pixels stay crisp when
    // scaled into their box.
    public static class BitmapFactory
    {
        public static unsafe WriteableBitmap FromRgba(uint[] pixels, int width, int height)
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Bgra8888,
                AlphaFormat.Unpremul);
            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                uint* destination = (uint*)buffer.Address;
                int rowPixels = buffer.RowBytes / sizeof(uint);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        uint rgba = pixels[y * width + x];
                        byte red = (byte)rgba;
                        byte green = (byte)(rgba >> 8);
                        byte blue = (byte)(rgba >> 16);
                        byte alpha = (byte)(rgba >> 24);
                        destination[y * rowPixels + x] =
                            blue | ((uint)green << 8) | ((uint)red << 16) | ((uint)alpha << 24);
                    }
                }
            }
            return bitmap;
        }

        public static WriteableBitmap FromPixelColors(PixelColor[] pixels, int width, int height)
        {
            var rgba = new uint[width * height];
            for (int index = 0; index < rgba.Length; index++) rgba[index] = pixels[index].Rgba;
            return FromRgba(rgba, width, height);
        }

        public static Image FromRgbaImage(uint[] pixels, int width, int height)
        {
            var image = new Image
            {
                Source = FromRgba(pixels, width, height),
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
            return image;
        }
    }
}
