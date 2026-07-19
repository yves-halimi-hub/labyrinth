using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Collections
{
    // PERFORMANCE: Ultra-fast 1D Flat Array for 2D Map Data
    // Capable of storing massive maps (e.g., 10,000 x 10,000) entirely in RAM
    // without the devastating overhead of Unity GameObjects or MonoBehaviours.
    public class FastGridMap
    {
        private readonly short[] gridData;
        public int Width { get; }
        public int Height { get; }
        
        // Direct access for unmanaged C-style bulk operations
        public short[] RawData => gridData;

        // Represents a prop placed on the map
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
        public sealed class MapPropData : IFastListTrackable
        {
            public string AssetKey;
            public int X;
            public int Y;
            public float Scale;
            public int ActiveListIndex { get; set; } = BackendConfig.Collections.UnregisteredListIndex;
        }

        public FastSwapList<MapPropData> Props { get; }

        public FastGridMap(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            int cellCount = checked(width * height);

            Width = width;
            Height = height;
            gridData = new short[cellCount];
            Props = new FastSwapList<MapPropData>(BackendConfig.Collections.MapPropsCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short GetTile(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return BackendConfig.Collections.EmptyTileId;
            return gridData[y * Width + x];
        }

        // Compatibility overload: silently ignores out-of-bounds writes (see TrySetTile).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTile(int x, int y, short tileID)
        {
            TrySetTile(x, y, tileID);
        }

        // Bounds-reporting write: returns false (and writes nothing) outside the map, so callers
        // like editor tools can surface lost strokes instead of dropping them silently.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrySetTile(int x, int y, short tileID)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
            gridData[y * Width + x] = tileID;
            return true;
        }

        // Fills the intersection of the given rectangle with the map. Negative sizes are rejected;
        // a zero-area or fully outside rectangle writes nothing. Returns the number of cells written.
        public int FillRect(int x, int y, int rectWidth, int rectHeight, short tileID)
        {
            if (rectWidth < 0) throw new ArgumentOutOfRangeException(nameof(rectWidth));
            if (rectHeight < 0) throw new ArgumentOutOfRangeException(nameof(rectHeight));

            // Clamp in 64-bit so x + rectWidth cannot overflow.
            int minX = x < 0 ? 0 : x;
            int minY = y < 0 ? 0 : y;
            long exclusiveMaxX = (long)x + rectWidth;
            long exclusiveMaxY = (long)y + rectHeight;
            int maxX = exclusiveMaxX > Width ? Width : (int)exclusiveMaxX;
            int maxY = exclusiveMaxY > Height ? Height : (int)exclusiveMaxY;
            if (minX >= maxX || minY >= maxY) return 0;

            for (int row = minY; row < maxY; row++)
            {
                int rowStart = row * Width;
                for (int column = minX; column < maxX; column++)
                {
                    gridData[rowStart + column] = tileID;
                }
            }
            return (maxX - minX) * (maxY - minY);
        }

        // Copies a rectangular tile region to another position on the SAME map, memmove-style:
        // overlapping source and destination copy correctly. Only cell pairs where BOTH the source
        // and the destination fall inside the map are copied (everything else is skipped, matching
        // the silently-clamping tile-write policy). Returns the number of cells copied.
        public int CopyRegion(int sourceX, int sourceY, int destinationX, int destinationY, int regionWidth, int regionHeight)
        {
            if (regionWidth < 0) throw new ArgumentOutOfRangeException(nameof(regionWidth));
            if (regionHeight < 0) throw new ArgumentOutOfRangeException(nameof(regionHeight));
            if (regionWidth == 0 || regionHeight == 0) return 0;
            if (sourceX == destinationX && sourceY == destinationY) return 0;

            // Iterate away from the destination overlap so already-copied cells are never re-read.
            bool forwardX = destinationX <= sourceX;
            bool forwardY = destinationY <= sourceY;
            int copied = 0;
            for (int rowStep = 0; rowStep < regionHeight; rowStep++)
            {
                int offsetY = forwardY ? rowStep : regionHeight - 1 - rowStep;
                int fromY = sourceY + offsetY;
                int toY = destinationY + offsetY;
                if ((uint)fromY >= (uint)Height || (uint)toY >= (uint)Height) continue;
                for (int columnStep = 0; columnStep < regionWidth; columnStep++)
                {
                    int offsetX = forwardX ? columnStep : regionWidth - 1 - columnStep;
                    int fromX = sourceX + offsetX;
                    int toX = destinationX + offsetX;
                    if ((uint)fromX >= (uint)Width || (uint)toX >= (uint)Width) continue;
                    gridData[toY * Width + toX] = gridData[fromY * Width + fromX];
                    copied++;
                }
            }
            return copied;
        }

        // Scanline flood fill over 4-connected equal tile ids (same algorithm family as
        // Algorithms.FloodFill). An out-of-bounds start or a start already holding the
        // replacement id is a no-op. Returns the number of cells changed.
        public int FloodFillTiles(int startX, int startY, short replacementTileID)
        {
            if ((uint)startX >= (uint)Width || (uint)startY >= (uint)Height) return 0;
            short targetTileID = gridData[startY * Width + startX];
            if (targetTileID == replacementTileID) return 0;

            int initialStackSize = Height < BackendConfig.Math.StepPositive
                ? BackendConfig.Math.StepPositive
                : checked(Height << BackendConfig.Procedural.FloodStackHeightShift);
            int[] stackX = ArrayPool<int>.Shared.Rent(initialStackSize);
            int[] stackY = ArrayPool<int>.Shared.Rent(initialStackSize);
            int stackPointer = 0;
            int changed = 0;
            try
            {
                PushFillPoint(ref stackX, ref stackY, ref stackPointer, startX, startY);
                while (stackPointer > 0)
                {
                    stackPointer--;
                    int cx = stackX[stackPointer];
                    int cy = stackY[stackPointer];
                    int rowStart = cy * Width;
                    if (gridData[rowStart + cx] != targetTileID) continue;

                    int leftX = cx;
                    while (leftX >= 0 && gridData[rowStart + leftX] == targetTileID) leftX--;
                    leftX++;
                    int rightX = cx;
                    while (rightX < Width && gridData[rowStart + rightX] == targetTileID) rightX++;
                    rightX--;

                    for (int x = leftX; x <= rightX; x++)
                    {
                        gridData[rowStart + x] = replacementTileID;
                        changed++;
                    }

                    bool spanAbove = false;
                    bool spanBelow = false;
                    for (int x = leftX; x <= rightX; x++)
                    {
                        if (cy > 0)
                        {
                            bool matches = gridData[rowStart - Width + x] == targetTileID;
                            if (!spanAbove && matches)
                            {
                                PushFillPoint(ref stackX, ref stackY, ref stackPointer, x, cy - 1);
                                spanAbove = true;
                            }
                            else if (spanAbove && !matches) spanAbove = false;
                        }
                        if (cy < Height - 1)
                        {
                            bool matches = gridData[rowStart + Width + x] == targetTileID;
                            if (!spanBelow && matches)
                            {
                                PushFillPoint(ref stackX, ref stackY, ref stackPointer, x, cy + 1);
                                spanBelow = true;
                            }
                            else if (spanBelow && !matches) spanBelow = false;
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(stackX);
                ArrayPool<int>.Shared.Return(stackY);
            }
            return changed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PushFillPoint(ref int[] stackX, ref int[] stackY, ref int count, int x, int y)
        {
            if (count == stackX.Length)
            {
                int newSize = checked(stackX.Length * BackendConfig.Procedural.StackGrowthMultiplier);
                int[] newStackX = ArrayPool<int>.Shared.Rent(newSize);
                int[] newStackY = ArrayPool<int>.Shared.Rent(newSize);
                Array.Copy(stackX, newStackX, count);
                Array.Copy(stackY, newStackY, count);
                ArrayPool<int>.Shared.Return(stackX);
                ArrayPool<int>.Shared.Return(stackY);
                stackX = newStackX;
                stackY = newStackY;
            }
            stackX[count] = x;
            stackY[count] = y;
            count++;
        }

        // Returns a NEW map of the requested size with all overlapping tiles preserved (a shrink is
        // a crop, a grow pads with the empty tile). Props are duplicated as fresh instances so the
        // original map's swap-list tracking stays valid; prop coordinates are copied verbatim.
        public FastGridMap Resize(int newWidth, int newHeight)
        {
            FastGridMap resized = new FastGridMap(newWidth, newHeight);
            int overlapWidth = newWidth < Width ? newWidth : Width;
            int overlapHeight = newHeight < Height ? newHeight : Height;
            for (int y = 0; y < overlapHeight; y++)
            {
                Array.Copy(gridData, y * Width, resized.gridData, y * newWidth, overlapWidth);
            }
            for (int index = 0; index < Props.Count; index++)
            {
                MapPropData original = Props[index];
                resized.Props.Add(new MapPropData
                {
                    AssetKey = original.AssetKey,
                    X = original.X,
                    Y = original.Y,
                    Scale = original.Scale
                });
            }
            return resized;
        }

        // Extremely fast Viewport Culling calculations.
        // Takes the camera coordinates and instantly spits out the exact 2D bounds of the screen.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetVisibleBounds(float cameraX, float cameraY, float fovWidth, float fovHeight, float cellSize, int padding,
            out int minX, out int maxX, out int minY, out int maxY)
        {
            // The negated ">= 0" form also rejects NaN (a NaN fov used to slip through "< 0"
            // and wrap the truncated casts into a garbage empty range).
            if (!(fovWidth >= 0f)) throw new ArgumentOutOfRangeException(nameof(fovWidth));
            if (!(fovHeight >= 0f)) throw new ArgumentOutOfRangeException(nameof(fovHeight));
            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize)) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));

            float halfWidth = fovWidth * BackendConfig.Collections.ViewportHalfExtent;
            float halfHeight = fovHeight * BackendConfig.Collections.ViewportHalfExtent;
            
            // Multiply by inverse cell size (faster than division)
            float invCellSize = BackendConfig.Collections.ReciprocalNumerator / cellSize;
            
            // We pad by N cells in every direction to prevent visual pop-in at screen edges
            minX = (int)((cameraX - halfWidth) * invCellSize) - padding;
            maxX = (int)((cameraX + halfWidth) * invCellSize) + padding;
            minY = (int)((cameraY - halfHeight) * invCellSize) - padding;
            maxY = (int)((cameraY + halfHeight) * invCellSize) + padding;

            // Clamp mathematically to prevent memory out-of-bounds
            if (minX < 0) minX = 0;
            if (maxX >= Width) maxX = Width - 1;
            if (minY < 0) minY = 0;
            if (maxY >= Height) maxY = Height - 1;
        }
    }
}
