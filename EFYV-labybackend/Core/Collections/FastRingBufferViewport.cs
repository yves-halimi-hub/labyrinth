using System;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Collections
{
    // PERFORMANCE: Mathematical 2D Ring Buffer
    // Solves infinite scrolling without allocating or despawning a single object.
    // As the camera moves, objects that fall outside the FOV mathematically "wrap around"
    // to the opposite side of the screen, creating the illusion of infinite objects.
    public class FastRingBufferViewport
    {
        public int ViewportCols { get; }
        public int ViewportRows { get; }

        private int prevMinX = BackendConfig.Collections.UninitializedViewportCoordinate;
        private int prevMinY = BackendConfig.Collections.UninitializedViewportCoordinate;

        public FastRingBufferViewport(int cols, int rows)
        {
            if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            ViewportCols = cols;
            ViewportRows = rows;
        }

        // Returns true if the camera shifted enough to warrant a visual update
        public bool HasViewportShifted(int currentMinX, int currentMinY)
        {
            return currentMinX != prevMinX || currentMinY != prevMinY;
        }

        // Maps a world grid coordinate to a persistent 2D array index for the Unity GameObjects
        // This is a pure mathematical wrapping function: O(1)
        public void GetRingBufferIndex(int worldX, int worldY, out int ringX, out int ringY)
        {
            // Pure modulo wrap-around logic
            ringX = worldX % ViewportCols;
            if (ringX < 0) ringX += ViewportCols; // Handle negative wrapping

            ringY = worldY % ViewportRows;
            if (ringY < 0) ringY += ViewportRows;
        }

        public void UpdatePreviousBounds(int minX, int minY)
        {
            prevMinX = minX;
            prevMinY = minY;
        }
        
    }
}
