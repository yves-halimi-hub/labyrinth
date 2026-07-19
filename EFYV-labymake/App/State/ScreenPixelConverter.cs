using System;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

namespace EFYVLabyMake.App.State
{
    // Converts the ViewportController screen buffer (straight RGBA, red in the low
    // byte - the PixelColor layout) into opaque BGRA suitable for an Avalonia
    // WriteableBitmap created with PixelFormats.Bgra8888.
    //
    // Item #31 moved the checkerboard transparency backdrop into Core
    // (ViewportController's Checkerboard overlay pass composites it under the
    // canvas content, making inside-canvas pixels opaque), so this converter
    // no longer replicates the blit mapping: any pixel the core left
    // non-opaque (outside the canvas, or inside it with the checkerboard
    // overlay toggled off) composites over the flat workspace color here.
    public static class ScreenPixelConverter
    {
        private const int ChannelMask = 0xFF;
        private const int GreenShift = Config.Shared.RgbaGreenShift;
        private const int BlueShift = Config.Shared.RgbaBlueShift;
        private const int AlphaShift = Config.Shared.RgbaAlphaShift;
        private const uint OpaqueAlphaBgra = 0xFF000000u;
        private const int MaxChannel = 255;
        private const int BlendRounding = 127;

        public static void ConvertToBgra(
            uint[] sourceRgba,
            uint[] destinationBgra,
            int width,
            int height,
            uint workspaceBgra)
        {
            if (sourceRgba == null) throw new ArgumentNullException(nameof(sourceRgba));
            if (destinationBgra == null) throw new ArgumentNullException(nameof(destinationBgra));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (sourceRgba.Length != checked(width * height)) throw new ArgumentException(nameof(sourceRgba));
            if (destinationBgra.Length != sourceRgba.Length) throw new ArgumentException(nameof(destinationBgra));

            for (int index = 0; index < sourceRgba.Length; index++)
            {
                uint rgba = sourceRgba[index];
                int alpha = (int)((rgba >> AlphaShift) & ChannelMask);
                if (alpha == MaxChannel)
                {
                    destinationBgra[index] = SwizzleOpaque(rgba);
                }
                else if (alpha == 0)
                {
                    destinationBgra[index] = workspaceBgra;
                }
                else
                {
                    destinationBgra[index] = BlendOverBackground(rgba, alpha, workspaceBgra);
                }
            }
        }

        // RGBA (red low byte) -> BGRA (blue low byte), forcing full alpha.
        public static uint SwizzleOpaque(uint rgba)
        {
            uint red = rgba & ChannelMask;
            uint green = (rgba >> GreenShift) & ChannelMask;
            uint blue = (rgba >> BlueShift) & ChannelMask;
            return OpaqueAlphaBgra | (red << BlueShift) | (green << GreenShift) | blue;
        }

        private static uint BlendOverBackground(uint rgba, int alpha, uint backgroundBgra)
        {
            int inverse = MaxChannel - alpha;
            int red = Blend((int)(rgba & ChannelMask), (int)((backgroundBgra >> BlueShift) & ChannelMask), alpha, inverse);
            int green = Blend((int)((rgba >> GreenShift) & ChannelMask), (int)((backgroundBgra >> GreenShift) & ChannelMask), alpha, inverse);
            int blue = Blend((int)((rgba >> BlueShift) & ChannelMask), (int)(backgroundBgra & ChannelMask), alpha, inverse);
            return OpaqueAlphaBgra | ((uint)red << BlueShift) | ((uint)green << GreenShift) | (uint)blue;
        }

        private static int Blend(int source, int background, int alpha, int inverseAlpha)
        {
            return (source * alpha + background * inverseAlpha + BlendRounding) / MaxChannel;
        }
    }
}
