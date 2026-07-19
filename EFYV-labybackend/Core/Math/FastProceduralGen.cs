using System;
using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Math
{
    // How cells beyond the map edge are counted by the cellular-automata smoothing pass.
    public enum CellularBorderRule
    {
        // Off-map neighbors count as the target tile ("treat as wall"): blobs grow natural
        // borders that hug the map edge. This is the historical behavior and the default.
        TreatAsTarget = 0,
        // Off-map neighbors count as nothing ("treat as empty"): edge cells see fewer
        // neighbors, so blobs erode away from the map edge instead of sticking to it.
        TreatAsEmpty = 1
    }

    public static class FastProceduralGen
    {
        // Local generator tuning (kept file-local; EFYV-LabyrinthConfig.cs is owned by the
        // config batch - see the batch notes - so these deliberately do not live there yet).
        private const int MazeCellStride = 2;                 // tile distance between carved maze cells
        private const int MazeDirectionCount = 4;             // N/E/S/W
        private const int RoomMinSpan = 3;                    // smallest room edge, in tiles
        private const int RoomMaxSpanDivisor = 4;             // room edges cap at dimension / divisor
        private const int RoomBorderMargin = 1;               // walls kept between a room and the map edge

        // Standard Cellular Automata smoothing pass for organic blobs (forests, lakes, caves).
        // Compatibility overload: map edges count as target (CellularBorderRule.TreatAsTarget).
        public static unsafe void SmoothCellularAutomata(short[] grid, short[] buffer, int width, int height, short targetTile, short baseTile, int smoothThreshold = BackendConfig.Procedural.DefaultSmoothThreshold)
        {
            SmoothCellularAutomata(grid, buffer, width, height, targetTile, baseTile, smoothThreshold, CellularBorderRule.TreatAsTarget);
        }

        public static unsafe void SmoothCellularAutomata(short[] grid, short[] buffer, int width, int height, short targetTile, short baseTile, int smoothThreshold, CellularBorderRule borderRule)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (ReferenceEquals(grid, buffer)) throw new ArgumentException(null, nameof(buffer));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (borderRule != CellularBorderRule.TreatAsTarget && borderRule != CellularBorderRule.TreatAsEmpty)
                throw new ArgumentOutOfRangeException(nameof(borderRule));
            int cellCount = checked(width * height);
            if (grid.Length != cellCount) throw new ArgumentException(null, nameof(grid));
            if (buffer.Length != cellCount) throw new ArgumentException(null, nameof(buffer));

            bool borderCountsAsTarget = borderRule == CellularBorderRule.TreatAsTarget;
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
                                else if (borderCountsAsTarget)
                                {
                                    // Map edges count as target to create natural borders.
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

        // ---------------------------------------------------------
        // PERFECT MAZE (recursive backtracker, iterative form)
        // Carves a fully connected, loop-free maze into caller-owned tile storage.
        // Maze cells live on odd tile coordinates (1,1), (3,1), ... so walls are
        // exactly one tile thick; an even trailing row/column simply stays wall.
        // `buffer` is used as the per-cell DFS bookkeeping (no allocations): each
        // visited cell records the direction back toward its predecessor, which
        // replaces the explicit backtracking stack. Fully deterministic from `seed`.
        // ---------------------------------------------------------
        public static void GenerateMazeRecursiveBacktracker(short[] tiles, short[] buffer, int width, int height, uint seed, short wallId, short floorId)
        {
            ValidateGeneratorBuffers(tiles, buffer, width, height);

            for (int index = 0; index < tiles.Length; index++) tiles[index] = wallId;

            int cellsX = (width - 1) / MazeCellStride;
            int cellsY = (height - 1) / MazeCellStride;
            if (cellsX <= 0 || cellsY <= 0) return; // Too small to hold a single carved cell.

            // Per-cell back-direction bookkeeping (indexed by cell coordinates, which always
            // fit inside the tile-sized buffer): 0 = unvisited, 1..4 = direction (N/E/S/W)
            // that leads back toward the predecessor, 5 = visited root.
            const short Unvisited = 0;
            const short BackDirectionBase = 1;
            const short Root = BackDirectionBase + MazeDirectionCount;
            for (int index = 0; index < cellsX * cellsY; index++) buffer[index] = Unvisited;

            // Direction deltas in cell space, ordered N, E, S, W. Opposite(d) = (d + 2) % 4.
            Span<int> deltaX = stackalloc int[MazeDirectionCount] { 0, 1, 0, -1 };
            Span<int> deltaY = stackalloc int[MazeDirectionCount] { -1, 0, 1, 0 };
            Span<int> candidates = stackalloc int[MazeDirectionCount];

            FastRandomState random = new FastRandomState(seed);
            int currentX = 0;
            int currentY = 0;
            buffer[0] = Root;
            tiles[TileIndexOfCell(0, 0, width)] = floorId;

            while (true)
            {
                int candidateCount = 0;
                for (int direction = 0; direction < MazeDirectionCount; direction++)
                {
                    int nextX = currentX + deltaX[direction];
                    int nextY = currentY + deltaY[direction];
                    if ((uint)nextX >= (uint)cellsX || (uint)nextY >= (uint)cellsY) continue;
                    if (buffer[nextY * cellsX + nextX] != Unvisited) continue;
                    candidates[candidateCount] = direction;
                    candidateCount++;
                }

                if (candidateCount > 0)
                {
                    int direction = candidates[random.Range(0, candidateCount)];
                    int nextX = currentX + deltaX[direction];
                    int nextY = currentY + deltaY[direction];
                    // Carve the wall between the two cells, then the destination cell.
                    int wallTileX = currentX * MazeCellStride + 1 + deltaX[direction];
                    int wallTileY = currentY * MazeCellStride + 1 + deltaY[direction];
                    tiles[wallTileY * width + wallTileX] = floorId;
                    tiles[TileIndexOfCell(nextX, nextY, width)] = floorId;
                    buffer[nextY * cellsX + nextX] =
                        (short)(BackDirectionBase + ((direction + MazeDirectionCount / 2) % MazeDirectionCount));
                    currentX = nextX;
                    currentY = nextY;
                }
                else
                {
                    short marker = buffer[currentY * cellsX + currentX];
                    if (marker == Root) break; // Backtracked to the start with nothing left to visit.
                    int backDirection = marker - BackDirectionBase;
                    currentX += deltaX[backDirection];
                    currentY += deltaY[backDirection];
                }
            }
        }

        // ---------------------------------------------------------
        // ROOMS AND CORRIDORS (roguelike dungeon)
        // Attempts `roomAttempts` random room placements; rooms never overlap other
        // rooms (one-tile margin), rejected attempts still consume a fixed number of
        // random draws so the layout is fully deterministic from `seed`. Each accepted
        // room is connected to the previous one with an L-shaped corridor. `buffer`
        // tracks rooms-only floor so corridors never block later room placement.
        // Returns the number of rooms carved.
        // ---------------------------------------------------------
        public static int GenerateRoomsAndCorridors(short[] tiles, short[] buffer, int width, int height, uint seed, short wallId, short floorId, int roomAttempts)
        {
            ValidateGeneratorBuffers(tiles, buffer, width, height);
            if (roomAttempts < 0) throw new ArgumentOutOfRangeException(nameof(roomAttempts));

            for (int index = 0; index < tiles.Length; index++)
            {
                tiles[index] = wallId;
                buffer[index] = wallId;
            }

            int maxRoomWidth = FastMath.FastClamp(width / RoomMaxSpanDivisor, RoomMinSpan, width - 2 * RoomBorderMargin);
            int maxRoomHeight = FastMath.FastClamp(height / RoomMaxSpanDivisor, RoomMinSpan, height - 2 * RoomBorderMargin);
            bool roomsFit = width >= RoomMinSpan + 2 * RoomBorderMargin && height >= RoomMinSpan + 2 * RoomBorderMargin;

            FastRandomState random = new FastRandomState(seed);
            int placedRooms = 0;
            int previousCenterX = 0;
            int previousCenterY = 0;
            for (int attempt = 0; attempt < roomAttempts; attempt++)
            {
                // Fixed draw pattern per attempt (4 draws), accepted or not: determinism does
                // not depend on earlier acceptance decisions beyond the shared stream.
                int roomWidth = random.Range(RoomMinSpan, maxRoomWidth + 1);
                int roomHeight = random.Range(RoomMinSpan, maxRoomHeight + 1);
                int roomX = random.Range(RoomBorderMargin, FastMath.FastMax(RoomBorderMargin + 1, width - RoomBorderMargin - roomWidth + 1));
                int roomY = random.Range(RoomBorderMargin, FastMath.FastMax(RoomBorderMargin + 1, height - RoomBorderMargin - roomHeight + 1));
                if (!roomsFit) continue;
                if (roomX + roomWidth > width - RoomBorderMargin || roomY + roomHeight > height - RoomBorderMargin) continue;
                if (RoomsOverlaps(buffer, width, height, roomX, roomY, roomWidth, roomHeight, floorId)) continue;

                for (int y = roomY; y < roomY + roomHeight; y++)
                {
                    int rowStart = y * width;
                    for (int x = roomX; x < roomX + roomWidth; x++)
                    {
                        tiles[rowStart + x] = floorId;
                        buffer[rowStart + x] = floorId;
                    }
                }

                int centerX = roomX + roomWidth / 2;
                int centerY = roomY + roomHeight / 2;
                if (placedRooms > 0)
                {
                    // One extra draw decides the L-corridor bend order; corridors carve into
                    // `tiles` only, so they never block later room placement.
                    bool horizontalFirst = random.Range(0, 2) == 0;
                    if (horizontalFirst)
                    {
                        CarveHorizontalCorridor(tiles, width, previousCenterX, centerX, previousCenterY, floorId);
                        CarveVerticalCorridor(tiles, width, previousCenterY, centerY, centerX, floorId);
                    }
                    else
                    {
                        CarveVerticalCorridor(tiles, width, previousCenterY, centerY, previousCenterX, floorId);
                        CarveHorizontalCorridor(tiles, width, previousCenterX, centerX, centerY, floorId);
                    }
                }
                previousCenterX = centerX;
                previousCenterY = centerY;
                placedRooms++;
            }
            return placedRooms;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TileIndexOfCell(int cellX, int cellY, int width)
        {
            return (cellY * MazeCellStride + 1) * width + (cellX * MazeCellStride + 1);
        }

        private static void ValidateGeneratorBuffers(short[] tiles, short[] buffer, int width, int height)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (ReferenceEquals(tiles, buffer)) throw new ArgumentException(null, nameof(buffer));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            int cellCount = checked(width * height);
            if (tiles.Length != cellCount) throw new ArgumentException(null, nameof(tiles));
            if (buffer.Length != cellCount) throw new ArgumentException(null, nameof(buffer));
        }

        private static bool RoomsOverlaps(short[] roomsOnly, int width, int height, int roomX, int roomY, int roomWidth, int roomHeight, short floorId)
        {
            int scanMinX = FastMath.FastMax(0, roomX - RoomBorderMargin);
            int scanMinY = FastMath.FastMax(0, roomY - RoomBorderMargin);
            int scanMaxX = FastMath.FastMin(width - 1, roomX + roomWidth - 1 + RoomBorderMargin);
            int scanMaxY = FastMath.FastMin(height - 1, roomY + roomHeight - 1 + RoomBorderMargin);
            for (int y = scanMinY; y <= scanMaxY; y++)
            {
                int rowStart = y * width;
                for (int x = scanMinX; x <= scanMaxX; x++)
                {
                    if (roomsOnly[rowStart + x] == floorId) return true;
                }
            }
            return false;
        }

        private static void CarveHorizontalCorridor(short[] tiles, int width, int fromX, int toX, int y, short floorId)
        {
            int minX = FastMath.FastMin(fromX, toX);
            int maxX = FastMath.FastMax(fromX, toX);
            int rowStart = y * width;
            for (int x = minX; x <= maxX; x++) tiles[rowStart + x] = floorId;
        }

        private static void CarveVerticalCorridor(short[] tiles, int width, int fromY, int toY, int x, short floorId)
        {
            int minY = FastMath.FastMin(fromY, toY);
            int maxY = FastMath.FastMax(fromY, toY);
            for (int y = minY; y <= maxY; y++) tiles[y * width + x] = floorId;
        }
    }
}
