using System;
using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Math
{
    public static class FastProceduralGen
    {
        // Standard Cellular Automata smoothing pass for organic blobs (forests, lakes, caves)
        public static unsafe void SmoothCellularAutomata(short[] grid, short[] buffer, int width, int height, short targetTile, short baseTile, int smoothThreshold = BackendConfig.Procedural.DefaultSmoothThreshold)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (ReferenceEquals(grid, buffer)) throw new ArgumentException(null, nameof(buffer));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            int cellCount = checked(width * height);
            if (grid.Length != cellCount) throw new ArgumentException(null, nameof(grid));
            if (buffer.Length != cellCount) throw new ArgumentException(null, nameof(buffer));

            fixed (short* src = grid)
            fixed (short* dst = buffer)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int neighborCount = 0;
                        for (int ny = y - 1; ny <= y + 1; ny++)
                        {
                            for (int nx = x - 1; nx <= x + 1; nx++)
                            {
                                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                {
                                    if (nx != x || ny != y)
                                    {
                                        if (src[ny * width + nx] == targetTile) neighborCount++;
                                    }
                                }
                                else
                                {
                                    // Map edges count as target to create natural borders
                                    neighborCount++;
                                }
                            }
                        }

                        if (neighborCount > smoothThreshold) 
                            dst[y * width + x] = targetTile;
                        else if (neighborCount < smoothThreshold) 
                            dst[y * width + x] = baseTile; 
                        else 
                            dst[y * width + x] = src[y * width + x];
                    }
                }
                
                // Extremely fast memory copy back to original array
                EFYVBackend.Core.Memory.FastMemory.Copy(buffer, grid);
            }
        }
    }
}
