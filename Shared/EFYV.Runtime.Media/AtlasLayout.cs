using System;

namespace EFYV.Runtime.Media
{
    public static class AtlasLayout
    {
        public static void ComputeSquare(int frameCount, int frameWidth, int frameHeight, out int columns, out int rows, out int width, out int height)
        {
            if (frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
            if (frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));
            columns = (int)System.Math.Ceiling(System.Math.Sqrt(frameCount));
            rows = (frameCount + columns - 1) / columns;
            width = checked(columns * frameWidth);
            height = checked(rows * frameHeight);
        }

        public static void GetOrigin(int frameIndex, int columns, int frameWidth, int frameHeight, out int x, out int y)
        {
            if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            if (frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
            if (frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));
            x = checked((frameIndex % columns) * frameWidth);
            y = checked((frameIndex / columns) * frameHeight);
        }

        public static void PackRgbaFrames(uint[][] frames, int frameWidth, int frameHeight, uint[] atlas, int atlasWidth, int columns)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            if (atlas == null) throw new ArgumentNullException(nameof(atlas));
            int framePixels = checked(frameWidth * frameHeight);
            for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
            {
                uint[] frame = frames[frameIndex];
                if (frame == null || frame.Length != framePixels) throw new ArgumentException("Every frame must match the declared dimensions.", nameof(frames));
                GetOrigin(frameIndex, columns, frameWidth, frameHeight, out int originX, out int originY);
                for (int y = 0; y < frameHeight; y++)
                    Array.Copy(frame, y * frameWidth, atlas, checked((originY + y) * atlasWidth + originX), frameWidth);
            }
        }
    }
}
