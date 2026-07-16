using UnityEngine;
using EFYVBackend.Core.Collections;
using System.Collections.Generic;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    // The Unity bridge for the FastGridMap and FastRingBufferViewport.
    // Handles infinite map scrolling, FOV culling, and camera centering.
    public class MapViewportController : MonoBehaviour
    {
        private EFYVBackend.Core.Models.ViewportData Data = new EFYVBackend.Core.Models.ViewportData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        public Camera mainCamera;
        public Transform targetToFollow; // Usually the Player or the Labymake Cursor

        [Header(GameConfig.DataConfig.HeaderMapSettings)]
        [SerializeField] private float serializedCellSize = GameConfig.Map.DefaultCellSize;
        public float cellSize
        {
            get => Data.CellSize;
            set
            {
                serializedCellSize = SanitizeCellSize(value);
                Data.CellSize = serializedCellSize;
            }
        }
        public Sprite[] tilePalette; // The visual sprites for tile IDs (0 = grass, 1 = dirt, etc)
        public GameObject tilePrefab; // A simple GameObject with a SpriteRenderer

        private void Awake()
        {
            serializedCellSize = SanitizeCellSize(serializedCellSize);
            Data.CellSize = serializedCellSize;
        }

        private void OnValidate()
        {
            serializedCellSize = SanitizeCellSize(serializedCellSize);
            Data.CellSize = serializedCellSize;
        }

        private void Reset()
        {
            serializedCellSize = GameConfig.Map.DefaultCellSize;
            Data.CellSize = serializedCellSize;
        }

        private static float SanitizeCellSize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value <= GameConfig.Runtime.UnitIntervalMin
                ? GameConfig.Map.DefaultCellSize
                : value;
        }

        // Backend Map Data (1D Array)
        private FastGridMap backendMap;
        
        // Backend Ring Buffer Math
        private FastRingBufferViewport ringBuffer;

        // Our persistent grid of Unity SpriteRenderers (No instantiation during gameplay!)
        private SpriteRenderer[,] visualGrid;

        private void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;

            // Apply global GameConfig zoom level
            mainCamera.orthographicSize = GameConfig.Camera.DefaultZoomLevel;

            // Initialize a massive backend map using global config sizes
            backendMap = new FastGridMap(GameConfig.Map.DefaultMapWidth, GameConfig.Map.DefaultMapHeight);
            
            LoadMapData(GameConfig.Map.DefaultMapId);

            // Calculate how many tiles we need to cover the screen FOV (+ Padding)
            // We use the GameConfig multipliers to adjust the buffer size mathematically
            float screenHeight = mainCamera.orthographicSize * GameConfig.Camera.OrthographicExtentMultiplier * GameConfig.Camera.FOVHeightMultiplier;
            float screenWidth = (mainCamera.orthographicSize * GameConfig.Camera.OrthographicExtentMultiplier * mainCamera.aspect) * GameConfig.Camera.FOVWidthMultiplier;
            int cols = CalculateVisualGridDimension(screenWidth, cellSize, GameConfig.Map.PaddingCellsBackend);
            int rows = CalculateVisualGridDimension(screenHeight, cellSize, GameConfig.Map.PaddingCellsBackend);

            ringBuffer = new FastRingBufferViewport(cols, rows);
            visualGrid = new SpriteRenderer[cols, rows];

            // Pre-warm the visual grid ONCE. 
            // These GameObjects will mathematically teleport around the camera forever.
            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    GameObject tileObj = Instantiate(tilePrefab, transform);
                    visualGrid[x, y] = tileObj.GetComponent<SpriteRenderer>();
                }
            }
        }

        internal static int CalculateVisualGridDimension(float fieldOfView, float tileSize, int padding)
        {
            if (fieldOfView <= GameConfig.Runtime.UnitIntervalMin || float.IsNaN(fieldOfView) || float.IsInfinity(fieldOfView))
                throw new System.ArgumentOutOfRangeException(nameof(fieldOfView));
            if (tileSize <= GameConfig.Runtime.UnitIntervalMin || float.IsNaN(tileSize) || float.IsInfinity(tileSize))
                throw new System.ArgumentOutOfRangeException(nameof(tileSize));
            if (padding < GameConfig.Runtime.FirstIndex)
                throw new System.ArgumentOutOfRangeException(nameof(padding));

            return EFYVBackend.Core.Math.FastMath.FastCeilToInt(fieldOfView / tileSize) +
                padding + padding +
                GameConfig.Map.InclusiveBoundsCellCount;
        }

        private void LateUpdate()
        {
            if (targetToFollow == null) return;

            // 1. Center Camera on Target
            Vector3 camPos = targetToFollow.position;
            camPos.z = GameConfig.Camera.CameraZOffset; // Keep camera backed out
            mainCamera.transform.position = camPos;

            // 2. Ask backend for the exact integer bounds of our current FOV
            // Apply FOV size/shape config modifiers
            float fovHeight = mainCamera.orthographicSize * GameConfig.Camera.OrthographicExtentMultiplier * GameConfig.Camera.FOVHeightMultiplier;
            float fovWidth = (mainCamera.orthographicSize * GameConfig.Camera.OrthographicExtentMultiplier * mainCamera.aspect) * GameConfig.Camera.FOVWidthMultiplier;

            backendMap.GetVisibleBounds(camPos.x, camPos.y, fovWidth, fovHeight, cellSize, 
                GameConfig.Map.PaddingCellsBackend,
                out int minX, out int maxX, out int minY, out int maxY);

            // 3. Check if the camera actually moved enough to shift a full grid cell
            if (!ringBuffer.HasViewportShifted(minX, minY)) return; // Fast exit!

            // 4. Update the visual grid using the Ring Buffer wrapping logic
            for (int worldX = minX; worldX <= maxX; worldX++)
            {
                for (int worldY = minY; worldY <= maxY; worldY++)
                {
                    // Backend instantly maps the world coordinate to our persistent array index
                    ringBuffer.GetRingBufferIndex(worldX, worldY, out int ringX, out int ringY);

                    SpriteRenderer renderer = visualGrid[ringX, ringY];

                    // Physically teleport the sprite to the new grid location
                    renderer.transform.position = new Vector3(worldX * cellSize, worldY * cellSize, GameConfig.Map.TileZOffset);

                    // Fetch the map data from the backend 1D array
                    short tileID = backendMap.GetTile(worldX, worldY);
                    
                    // Assign visual
                    if (tileID >= GameConfig.Map.MinimumTileId && tileID < tilePalette.Length)
                        renderer.sprite = tilePalette[tileID];
                    else
                        renderer.sprite = null; // Blank space
                }
            }

            // Save bounds so we only recalculate when the cell boundary changes
            ringBuffer.UpdatePreviousBounds(minX, minY);
        }

        public void LoadMapData(string mapId)
        {
            // TODO: In the future, this will hook into the FastSaveEngine or binary map files generated by Labymake.
            // For now, we simulate a map load by regenerating the grid.
            PopulateFallbackMap(backendMap, tilePalette != null ? tilePalette.Length : GameConfig.Runtime.EmptyCollectionCount);
            
            if (visualGrid != null)
            {
                // Blank out the visual grid so it doesn't flicker old tiles before next LateUpdate
                for (int x = 0; x < visualGrid.GetLength(0); x++)
                {
                    for (int y = 0; y < visualGrid.GetLength(1); y++)
                    {
                        if (visualGrid[x, y] != null) visualGrid[x, y].sprite = null;
                    }
                }
                
                // Force a complete recalculation next frame
                ringBuffer.UpdatePreviousBounds(GameConfig.Map.InvalidBounds, GameConfig.Map.InvalidBounds);
            }
        }

        internal static void PopulateFallbackMap(FastGridMap map, int tileTypeCount)
        {
            if (map == null) throw new System.ArgumentNullException(nameof(map));
            short[] tiles = map.RawData;
            if (tileTypeCount <= GameConfig.Runtime.EmptyCollectionCount)
            {
                System.Array.Clear(tiles, GameConfig.Runtime.FirstIndex, tiles.Length);
                return;
            }

            for (int i = GameConfig.Runtime.FirstIndex; i < tiles.Length; i++)
            {
                tiles[i] = (short)EFYVBackend.Core.Math.FastRandom.Range(
                    GameConfig.Map.RandomTileMinIndex,
                    tileTypeCount);
            }
        }
    }
}
