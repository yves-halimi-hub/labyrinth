using System;
using System.Runtime.CompilerServices;

namespace EFYV.Runtime.Media
{
    public static class RgbaCompositor
    {
        public const int GreenShift = 8;
        public const int BlueShift = 16;
        public const int AlphaShift = 24;
        public const byte TransparentAlpha = 0;
        public const byte OpaqueAlpha = 255;
        public const uint RgbMask = 0x00FFFFFFu;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlendPixel(ref uint destination, uint source)
        {
            byte sourceAlpha = (byte)(source >> AlphaShift);
            if (sourceAlpha == TransparentAlpha) return;
            if (sourceAlpha == OpaqueAlpha) { destination = source; return; }

            byte destinationAlpha = (byte)(destination >> AlphaShift);
            if (destinationAlpha == TransparentAlpha) { destination = source; return; }

            byte sourceR = (byte)source;
            byte sourceG = (byte)(source >> GreenShift);
            byte sourceB = (byte)(source >> BlueShift);
            byte destinationR = (byte)destination;
            byte destinationG = (byte)(destination >> GreenShift);
            byte destinationB = (byte)(destination >> BlueShift);
            const uint fullAlpha = OpaqueAlpha;
            uint halfAlpha = fullAlpha >> 1;
            uint inverseAlpha = fullAlpha - sourceAlpha;

            if (destinationAlpha == OpaqueAlpha)
            {
                uint outputR = ((uint)(sourceR * sourceAlpha) + (uint)(destinationR * inverseAlpha) + halfAlpha) / fullAlpha;
                uint outputG = ((uint)(sourceG * sourceAlpha) + (uint)(destinationG * inverseAlpha) + halfAlpha) / fullAlpha;
                uint outputB = ((uint)(sourceB * sourceAlpha) + (uint)(destinationB * inverseAlpha) + halfAlpha) / fullAlpha;
                destination = outputR | (outputG << GreenShift) | (outputB << BlueShift) | (fullAlpha << AlphaShift);
                return;
            }

            uint alphaNumerator = (sourceAlpha * fullAlpha) + (destinationAlpha * inverseAlpha);
            uint outputAlpha = (alphaNumerator + halfAlpha) / fullAlpha;
            uint channelR = ((uint)(sourceR * sourceAlpha) * fullAlpha + (uint)(destinationR * destinationAlpha) * inverseAlpha + (alphaNumerator >> 1)) / alphaNumerator;
            uint channelG = ((uint)(sourceG * sourceAlpha) * fullAlpha + (uint)(destinationG * destinationAlpha) * inverseAlpha + (alphaNumerator >> 1)) / alphaNumerator;
            uint channelB = ((uint)(sourceB * sourceAlpha) * fullAlpha + (uint)(destinationB * destinationAlpha) * inverseAlpha + (alphaNumerator >> 1)) / alphaNumerator;
            destination = channelR | (channelG << GreenShift) | (channelB << BlueShift) | (outputAlpha << AlphaShift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlendPixel(ref uint destination, uint source, byte opacity)
        {
            if (opacity == TransparentAlpha) return;
            if (opacity != OpaqueAlpha)
            {
                const uint fullAlpha = OpaqueAlpha;
                uint adjustedAlpha = (((source >> AlphaShift) * opacity) + (fullAlpha >> 1)) / fullAlpha;
                source = (source & RgbMask) | (adjustedAlpha << AlphaShift);
            }
            BlendPixel(ref destination, source);
        }

        public static unsafe void BlendBatchManaged(uint* destination, uint* source, int count, byte opacity, byte transparentThreshold)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return;
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (source == null) throw new ArgumentNullException(nameof(source));
            uint[] snapshot = null;
            nuint bytes = checked((nuint)count * sizeof(uint));
            nuint destinationStart = (nuint)destination;
            nuint sourceStart = (nuint)source;
            bool partialOverlap = destination != source &&
                destinationStart < sourceStart + bytes && sourceStart < destinationStart + bytes;
            if (partialOverlap)
            {
                snapshot = new uint[count];
                fixed (uint* snapshotPointer = snapshot)
                    Buffer.MemoryCopy(source, snapshotPointer, bytes, bytes);
            }
            fixed (uint* snapshotPointer = snapshot)
            {
                uint* stableSource = snapshot == null ? source : snapshotPointer;
                for (int i = 0; i < count; i++)
                {
                    if ((byte)(stableSource[i] >> AlphaShift) > transparentThreshold)
                        BlendPixel(ref destination[i], stableSource[i], opacity);
                }
            }
        }
    }
}
