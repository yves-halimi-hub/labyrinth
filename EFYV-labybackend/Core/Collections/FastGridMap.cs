using System;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTile(int x, int y, short tileID)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
            gridData[y * Width + x] = tileID;
        }

        // Extremely fast Viewport Culling calculations.
        // Takes the camera coordinates and instantly spits out the exact 2D bounds of the screen.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetVisibleBounds(float cameraX, float cameraY, float fovWidth, float fovHeight, float cellSize, int padding,
            out int minX, out int maxX, out int minY, out int maxY)
        {
            if (fovWidth < 0f) throw new ArgumentOutOfRangeException(nameof(fovWidth));
            if (fovHeight < 0f) throw new ArgumentOutOfRangeException(nameof(fovHeight));
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
