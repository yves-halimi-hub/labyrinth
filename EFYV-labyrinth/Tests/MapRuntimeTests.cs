// batch3.6 agent (item #5): maps + tileset pipeline, game side.
// - EFYVMapImporter: .efyvmap (FastMapExporter envelope) -> MapAssetData,
//   per-cause rejections, tileset asset linking, update-in-place.
// - EFYVPixelArtImporter: the documentVersion-5 tileset manifest ->
//   TilesetAssetData with sliced sprites in tile-id order; invalid manifests
//   rejected through the shared backend gate.
// - MapViewportController.LoadMapData: imported maps replace the procedural
//   TODO stub (grid rebuild on dimension change, tilePalette fed from the
//   imported tileset), with procedural noise kept as the EXPLICIT fallback -
//   which is what DoorProp/MapManager switching drives per target map id.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EFYV.Core.Data;
using EFYV.Core.Managers;
using EFYV.Editor;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using UnityEditor;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

internal static partial class Program
{
    private static MapFileData RuntimeMapData(
        int width,
        int height,
        string tilesetName,
        params (int x, int y, short id)[] cells)
    {
        var data = new MapFileData
        {
            Width = width,
            Height = height,
            TilesetName = tilesetName,
            Tiles = new short[width * height],
            Props = new MapPropRecord[]
            {
                new MapPropRecord { AssetKey = "Torch", X = 4, Y = 5, Scale = 1.25f }
            }
        };
        for (int index = 0; index < data.Tiles.Length; index++)
            data.Tiles[index] = Config.Backend.MapFile.BlankTileId;
        foreach ((int x, int y, short id) in cells)
            data.Tiles[(y * width) + x] = id;
        return data;
    }

    private static void TestMapImporterEndToEnd()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "efyv-map-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            Type importerType = typeof(EFYVMapImporter);

            // Happy path: envelope-valid file becomes a MapAssetData at
            // "<stem>_Map.asset" with tiles/props copied and the id = stem.
            MapFileData data = RuntimeMapData(4, 3, "DungeonTiles", (0, 0, 2), (3, 2, 0));
            string mapPath = Path.Combine(tempRoot, "Crypt" + Config.Game.MapImporter.ExtensionMap);
            FastMapExporter.Export(mapPath, data);
            InvokeStatic(importerType, "ImportMapFile", mapPath);

            string assetPath = Path.GetDirectoryName(mapPath) + Config.Game.Importer.PathSeparator +
                "Crypt" + Config.Game.MapImporter.ExtensionAsset;
            MapAssetData asset = AssetDatabase.LoadAssetAtPath<MapAssetData>(assetPath);
            Check(asset != null, "Map import produced no asset.");
            Equal("Crypt", asset.mapId);
            Equal(4, asset.width);
            Equal(3, asset.height);
            Equal(12, asset.tiles.Length);
            Equal(2, asset.tiles[0]);
            Equal(0, asset.tiles[(2 * 4) + 3]);
            Equal((int)Config.Backend.MapFile.BlankTileId, asset.tiles[1]);
            Equal("DungeonTiles", asset.tilesetName);
            Equal(1, asset.props.Length);
            Equal("Torch", asset.props[0].assetKey);
            Equal(4, asset.props[0].x);
            Equal(5, asset.props[0].y);
            Near(1.25f, asset.props[0].scale);
            Check(asset.HasLoadableTiles());
            Check(EditorUtility.DirtyObjects.Contains(asset));
            // The tileset asset does not exist yet: link warning, null link.
            Same(null, asset.tileset);
            Check(Debug.Messages.Contains(string.Format(
                Config.Game.MapImporter.LogWarningMissingTilesetSprites,
                "Crypt",
                "DungeonTiles")), "Missing tileset sprites warning not logged.");
            Check(Debug.Messages.Contains(string.Format(
                Config.Game.MapImporter.LogImported, "Crypt", 4, 3, 1, mapPath)));

            // With the tileset asset present, re-import links it directly.
            var tilesetAsset = ScriptableObject.CreateInstance<TilesetAssetData>();
            tilesetAsset.tileSprites = new[] { new Sprite { name = "t0" } };
            string tilesetAssetPath = Path.GetDirectoryName(mapPath) + Config.Game.Importer.PathSeparator +
                "DungeonTiles" + Config.Game.Importer.ExtensionAsset;
            AssetDatabase.CreateAsset(tilesetAsset, tilesetAssetPath);
            InvokeStatic(importerType, "ImportMapFile", mapPath);
            MapAssetData relinked = AssetDatabase.LoadAssetAtPath<MapAssetData>(assetPath);
            Same(asset, relinked); // updated in place, not recreated
            Same(tilesetAsset, relinked.tileset);

            // Update-in-place: a republished file overwrites the payload.
            MapFileData updated = RuntimeMapData(2, 2, "", (1, 1, 7));
            FastMapExporter.Export(mapPath, updated);
            InvokeStatic(importerType, "ImportMapFile", mapPath);
            Equal(2, asset.width);
            Equal(2, asset.height);
            Equal(7, asset.tiles[3]);
            Same(null, asset.tileset);

            // Malformed file: per-cause error, asset untouched.
            File.WriteAllBytes(mapPath, new byte[] { 1, 2, 3 });
            int dirtyBefore = EditorUtility.DirtyObjects.Count;
            InvokeStatic(importerType, "ImportMapFile", mapPath);
            Equal(string.Format(Config.Game.MapImporter.LogErrorMalformed, mapPath), Debug.Messages[^1]);
            Equal(dirtyBefore, EditorUtility.DirtyObjects.Count);
            Equal(2, asset.width);

            // Vanished file: Missing, not Malformed.
            string missingPath = Path.Combine(tempRoot, "Gone" + Config.Game.MapImporter.ExtensionMap);
            InvokeStatic(importerType, "ImportMapFile", missingPath);
            Equal(string.Format(Config.Game.MapImporter.LogErrorMissingFile, missingPath), Debug.Messages[^1]);

            // Unsafe stems are rejected before any parse.
            string reservedPath = Path.Combine(tempRoot, "CON" + Config.Game.MapImporter.ExtensionMap);
            InvokeStatic(importerType, "ImportMapFile", reservedPath);
            Equal(string.Format(Config.Game.MapImporter.LogErrorUnsafeStem, reservedPath, "CON"), Debug.Messages[^1]);

            // Existing asset of another type at the destination: rejected.
            MapFileData clashData = RuntimeMapData(2, 2, "", (0, 0, 1));
            string clashPath = Path.Combine(tempRoot, "Clash" + Config.Game.MapImporter.ExtensionMap);
            FastMapExporter.Export(clashPath, clashData);
            string clashAssetPath = Path.GetDirectoryName(clashPath) + Config.Game.Importer.PathSeparator +
                "Clash" + Config.Game.MapImporter.ExtensionAsset;
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameAssetData>(), clashAssetPath);
            InvokeStatic(importerType, "ImportMapFile", clashPath);
            Equal(string.Format(
                Config.Game.MapImporter.LogErrorExistingAssetTypeMismatch, clashPath, clashAssetPath),
                Debug.Messages[^1]);
            Check(AssetDatabase.LoadAssetAtPath<GameAssetData>(clashAssetPath) != null);

            // The batch postprocessor routes only .efyvmap paths here.
            MapFileData batchData = RuntimeMapData(2, 2, "", (0, 1, 3));
            string batchPath = Path.Combine(tempRoot, "Batch" + Config.Game.MapImporter.ExtensionMap);
            FastMapExporter.Export(batchPath, batchData);
            InvokeStatic(importerType, "OnPostprocessAllAssets",
                new[] { batchPath, Path.Combine(tempRoot, "ignored.png") },
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
            Check(AssetDatabase.LoadAssetAtPath<MapAssetData>(
                Path.GetDirectoryName(batchPath) + Config.Game.Importer.PathSeparator +
                "Batch" + Config.Game.MapImporter.ExtensionAsset) != null);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void TestTilesetImportEndToEnd()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "efyv-tileset-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            Type importerType = typeof(EFYVPixelArtImporter);

            // Publish a real tileset .efyvlaby through the backend exporter:
            // 3 tiles of 2x2 -> 2x2 grid atlas, manifest {tileSize, tiles}.
            var atlas = new AtlasMetadataJson
            {
                formatVersion = Config.Backend.Exporter.CurrentFormatVersion,
                frameWidth = 2,
                frameHeight = 2,
                atlasWidth = 4,
                atlasHeight = 4,
                animations = new List<AnimationMetadataJson>
                {
                    new AnimationMetadataJson
                    {
                        name = Config.LabyMake.Export.TilesetAnimationName,
                        fps = Config.LabyMake.Export.TilesetAnimationFps,
                        startFrame = 0,
                        frameCount = 3
                    }
                }
            };
            var manifest = new TilesetManifestJson
            {
                tileSize = 2,
                tiles = new List<string> { "Grass", "Dirt", "Wall" }
            };
            var properties = new Dictionary<string, object>
            {
                [Config.Shared.AssetNameField] = "DungeonTiles"
            };
            EFYVBackend.Core.Export.FastExporter.PushToUnityLiveHook(
                tempRoot,
                Config.Shared.GameAssetAssetType,
                properties,
                new List<HitboxJson>(),
                new uint[4 * 4],
                4,
                4,
                atlas,
                Config.Shared.GameAssetAssetType,
                null,
                manifest);
            string documentPath = Path.Combine(
                tempRoot,
                "DungeonTiles" + Config.Game.Importer.ExtensionEFYV);
            Check(File.Exists(documentPath));

            // Register the sliced sprites the texture pipeline would produce
            // (slice order == atlas frame order == tile-id order).
            string pngPath = Path.Combine(tempRoot, "DungeonTiles" + Config.Game.Importer.ExtensionPNG);
            var slice0 = new Sprite { name = "DungeonTiles_00000000" };
            var slice1 = new Sprite { name = "DungeonTiles_00000001" };
            var slice2 = new Sprite { name = "DungeonTiles_00000002" };
            AssetDatabase.SetAllAssetsAtPath(pngPath, slice0, slice1, slice2);

            InvokeStatic(importerType, "ImportEFYVAsset", documentPath);
            string assetPath = Path.GetDirectoryName(documentPath) + Config.Game.Importer.PathSeparator +
                "DungeonTiles" + Config.Game.Importer.ExtensionAsset;
            var tilesetAsset = AssetDatabase.LoadAssetAtPath<TilesetAssetData>(assetPath);
            Check(tilesetAsset != null, "Tileset import produced no TilesetAssetData.");
            Equal("DungeonTiles", tilesetAsset.assetName);
            Equal(2, tilesetAsset.tileSize);
            Equal(3, tilesetAsset.tileNames.Length);
            Equal("Grass", tilesetAsset.tileNames[0]);
            Equal("Dirt", tilesetAsset.tileNames[1]);
            Equal("Wall", tilesetAsset.tileNames[2]);
            Equal(3, tilesetAsset.tileSprites.Length);
            Same(slice0, tilesetAsset.tileSprites[0]);
            Same(slice1, tilesetAsset.tileSprites[1]);
            Same(slice2, tilesetAsset.tileSprites[2]);

            // Re-import updates the same asset instance in place.
            InvokeStatic(importerType, "ImportEFYVAsset", documentPath);
            Same(tilesetAsset, AssetDatabase.LoadAssetAtPath<TilesetAssetData>(assetPath));

            // An invalid manifest (atlas frame mismatch) is rejected through
            // the shared gate with the per-cause message; no asset appears.
            var badFormat = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = Config.Shared.GameAssetAssetType,
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Shared.AssetNameField] = JsonValue("\"BadTiles\"")
                },
                hitboxes = new List<HitboxJson>(),
                atlas = atlas,
                tileset = new TilesetManifestJson
                {
                    tileSize = 3,
                    tiles = new List<string> { "Mismatch" }
                }
            };
            string badPath = DataEditorWriteEfyvFile(tempRoot, "BadTiles", badFormat);
            InvokeStatic(importerType, "ImportEFYVAsset", badPath);
            Equal(string.Format(
                Config.Game.Importer.LogErrorInvalidTileset,
                badPath,
                EFYVBackend.Core.Export.TilesetManifestError.AtlasFrameMismatch),
                Debug.Messages[^1]);
            Same(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(badPath) + Config.Game.Importer.PathSeparator +
                "BadTiles" + Config.Game.Importer.ExtensionAsset));

            // A pre-existing NON-tileset asset at the tileset's path is a
            // type mismatch (TilesetAssetData expected).
            var plainFormat = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = Config.Shared.GameAssetAssetType,
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Shared.AssetNameField] = JsonValue("\"OccupiedTiles\"")
                },
                hitboxes = new List<HitboxJson>(),
                tileset = new TilesetManifestJson
                {
                    tileSize = 2,
                    tiles = new List<string> { "Solo" }
                }
            };
            string occupiedPath = DataEditorWriteEfyvFile(tempRoot, "OccupiedTiles", plainFormat);
            string occupiedAssetPath = Path.GetDirectoryName(occupiedPath) + Config.Game.Importer.PathSeparator +
                "OccupiedTiles" + Config.Game.Importer.ExtensionAsset;
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameAssetData>(), occupiedAssetPath);
            InvokeStatic(importerType, "ImportEFYVAsset", occupiedPath);
            Equal(string.Format(
                Config.Game.Importer.LogErrorExistingAssetTypeMismatch,
                occupiedPath,
                nameof(GameAssetData),
                nameof(TilesetAssetData)),
                Debug.Messages[^1]);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void TestMapViewportImportedMapsAndSwitching()
    {
        EFYVBackend.Core.Math.FastRandom.SetSeed(0xFEEDFACEu);

        // Imported assets: one wired into the serialized slot, one only
        // "loaded" (found through the FindObjectsOfTypeAll fallback).
        var tileset = ScriptableObject.CreateInstance<TilesetAssetData>();
        tileset.tileSize = 2;
        tileset.tileNames = new[] { "Grass", "Wall" };
        tileset.tileSprites = new[]
        {
            new Sprite { name = "grass" },
            new Sprite { name = "wall" }
        };

        var wired = ScriptableObject.CreateInstance<MapAssetData>();
        wired.mapId = "Overworld";
        wired.width = 3;
        wired.height = 2;
        wired.tiles = new[] { 1, 0, 1, -1, 0, -1 };
        wired.props = Array.Empty<MapPropPlacement>();
        wired.tileset = tileset;

        var discovered = ScriptableObject.CreateInstance<MapAssetData>();
        discovered.mapId = "Catacombs";
        discovered.width = 2;
        discovered.height = 2;
        discovered.tiles = new[] { 0, 0, 1, 1 };
        discovered.props = Array.Empty<MapPropPlacement>();

        // A broken candidate with the right id must be SKIPPED, not loaded.
        var broken = ScriptableObject.CreateInstance<MapAssetData>();
        broken.mapId = "Overworld";
        broken.width = 9;
        broken.height = 9;
        broken.tiles = new int[3];

        var camera = CreateComponent<Camera>();
        var viewport = CreateComponent<MapViewportController>(invokeAwake: true);
        viewport.mainCamera = camera;
        viewport.targetToFollow = new GameObject("map-target").transform;
        viewport.tilePalette = new[] { new Sprite { name = "fallback-0" } };
        var tilePrefab = new GameObject("map-tile-prefab");
        tilePrefab.AddComponent<SpriteRenderer>();
        viewport.tilePrefab = tilePrefab;
        viewport.importedMaps = new[] { broken, wired };

        // Resolution order: serialized slots (skipping unloadable entries),
        // then loaded assets, then null.
        Same(wired, viewport.FindImportedMap("Overworld"));
        Same(discovered, viewport.FindImportedMap("Catacombs"));
        Same(null, viewport.FindImportedMap("Absent"));
        Same(null, viewport.FindImportedMap(null));
        Same(null, viewport.FindImportedMap(""));

        // Direct load before Start: the controller builds a matching grid,
        // copies the tiles, and feeds tilePalette from the imported tileset.
        viewport.LoadMapData("Overworld");
        var grid = GetField<FastGridMap>(viewport, "backendMap");
        Equal(3, grid.Width);
        Equal(2, grid.Height);
        Equal((short)1, grid.GetTile(0, 0));
        Equal((short)0, grid.GetTile(1, 0));
        Equal((short)(-1), grid.GetTile(0, 1));
        Same(tileset.tileSprites, viewport.tilePalette);
        Check(Debug.Messages.Contains(string.Format(
            Config.Game.Map.LogImportedMapLoaded, "Overworld", 3, 2)));

        // Switching to another imported map rebuilds the grid at its size
        // and keeps the palette (the discovered map has no tileset link).
        viewport.LoadMapData("Catacombs");
        grid = GetField<FastGridMap>(viewport, "backendMap");
        Equal(2, grid.Width);
        Equal((short)1, grid.GetTile(0, 1));
        Same(tileset.tileSprites, viewport.tilePalette);

        // An unknown id falls back to the EXPLICIT procedural path: the grid
        // keeps its dimensions and every cell lands inside the palette range.
        viewport.LoadMapData("NoSuchMap");
        grid = GetField<FastGridMap>(viewport, "backendMap");
        Equal(2, grid.Width);
        Check(Debug.Messages.Contains(string.Format(
            Config.Game.Map.LogNoImportedMapFallback, "NoSuchMap")));
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Check(grid.GetTile(x, y) >= 0 && grid.GetTile(x, y) < viewport.tilePalette.Length);
            }
        }

        // Start() loads the DEFAULT map id: with an imported "Default"
        // present the whole boot path lands on imported data (this is the
        // same LoadMapData route MapManager.SwitchMap drives per door).
        var defaultMap = ScriptableObject.CreateInstance<MapAssetData>();
        defaultMap.mapId = Config.Game.Map.DefaultMapId;
        defaultMap.width = 4;
        defaultMap.height = 4;
        defaultMap.tiles = new int[16];
        for (int index = 0; index < 16; index++) defaultMap.tiles[index] = index % 2;
        defaultMap.tileset = tileset;
        Invoke(viewport, "Start");
        grid = GetField<FastGridMap>(viewport, "backendMap");
        Equal(4, grid.Width);
        Equal((short)1, grid.GetTile(1, 0));
        Same(tileset.tileSprites, viewport.tilePalette);
    }
}
