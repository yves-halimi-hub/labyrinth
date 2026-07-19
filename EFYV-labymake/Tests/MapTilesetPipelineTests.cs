// batch3.6 agent (item #5): maps + tileset pipeline, designer side.
// - TilesetSection/TilesetTile and MapSection model gates.
// - Undoable session tileset CRUD (create/add/add-from-frame/rename/remove).
// - Undoable map editing: single-cell writes, rect/flood bulk fills, MapTool
//   gestures (tile diff + appended props as ONE history entry), and the
//   Begin/Paint/End map paint stroke batching with Esc cancellation.
// - .efyvmake tileset/map sections: round trip, snapshot isolation, legacy
//   documents, and a malformed-document corpus.
// - ExportTileset (tile-sheet .efyvlaby with the documentVersion-5 manifest)
//   and ExportMap (.efyvmap through the FastMapExporter envelope), both read
//   back through the backend importers.
// - MapRenderer reference model (tile blit / placeholder / blank cells).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using EFYVBackend.Core.IO;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    private static uint[] SolidTilePixels(int tileSize, uint rgba)
    {
        var pixels = new uint[tileSize * tileSize];
        for (int index = 0; index < pixels.Length; index++) pixels[index] = rgba;
        return pixels;
    }

    // ------------------------------------------------------------------
    // Models: TilesetTile/TilesetSection/MapSection gates + MapRenderer
    // ------------------------------------------------------------------
    private static void TestMapTilesetModelsAndRenderer()
    {
        // Tile size bounds come from the shared tileset caps.
        RequireThrows<ArgumentOutOfRangeException>(() =>
            new TilesetSection(Config.Tileset.MinTileSize - 1));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            new TilesetSection(Config.Tileset.MaxTileSize + 1));
        RequireThrows<ArgumentOutOfRangeException>(() => new TilesetTile("T", 0));

        // Name gate: non-blank, bounded; pixels must cover the tile exactly.
        RequireThrows<ArgumentException>(() => new TilesetTile(null, Config.Tileset.MinTileSize));
        RequireThrows<ArgumentException>(() => new TilesetTile("  ", Config.Tileset.MinTileSize));
        RequireThrows<ArgumentException>(() => new TilesetTile(
            new string('x', Config.Tileset.MaxTileNameLength + 1), Config.Tileset.MinTileSize));
        RequireThrows<ArgumentException>(() => new TilesetTile(
            "Short", Config.Tileset.MinTileSize, new uint[3]));

        var tile = new TilesetTile("Grass", Config.Tileset.MinTileSize);
        Require(tile.Pixels.Length == Config.Tileset.MinTileSize * Config.Tileset.MinTileSize);
        foreach (uint pixel in tile.Pixels) Require(pixel == Config.Color.TransparentPixelRgba);
        RequireThrows<ArgumentException>(() => tile.Name = " ");
        tile.Name = "Grass2";
        Require(tile.Name == "Grass2");

        // Constructor copies the pixel buffer; Clone is fully isolated.
        uint[] source = SolidTilePixels(Config.Tileset.MinTileSize, Pack(1, 2, 3, 255));
        var owned = new TilesetTile("Owned", Config.Tileset.MinTileSize, source);
        source[0] = 0;
        Require(owned.Pixels[0] == Pack(1, 2, 3, 255));
        TilesetTile clone = owned.Clone();
        clone.Pixels[0] = 7;
        Require(owned.Pixels[0] == Pack(1, 2, 3, 255));

        // Map section: id/dimension/tileset-reference gates; blank initial fill.
        RequireThrows<ArgumentException>(() => new MapSection("..", 4, 4, ""));
        RequireThrows<ArgumentException>(() => new MapSection("CON", 4, 4, ""));
        RequireThrows<ArgumentOutOfRangeException>(() => new MapSection("Ok", 0, 4, ""));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            new MapSection("Ok", Config.MapDocument.MaxDimension + 1, 4, ""));
        RequireThrows<ArgumentException>(() => new MapSection("Ok", 4, 4, "a/b"));

        var map = new MapSection("Dungeon", 3, 2, null);
        Require(map.MapId == "Dungeon");
        Require(map.TilesetName == "");
        Require(map.Grid.Width == 3 && map.Grid.Height == 2);
        foreach (short blankCell in map.Grid.RawData)
            Require(blankCell == Config.MapDocument.BlankTileId);

        // MapRenderer reference model: blank stays transparent, in-palette
        // ids blit tile pixels, out-of-palette ids get the deterministic
        // opaque placeholder; without a tileset the default tile size rules.
        var tileset = new TilesetSection(Config.Tileset.MinTileSize);
        tileset.Tiles.Add(new TilesetTile("Red", tileset.TileSize,
            SolidTilePixels(tileset.TileSize, Pack(255, 0, 0, 255))));
        map.Grid.SetTile(0, 0, 0);   // tile 0 -> red pixels
        map.Grid.SetTile(1, 0, 9);   // out of palette -> placeholder
        // (2,0) and row 1 stay blank.

        MapRenderer.GetSurfaceSize(map, tileset, out int surfaceWidth, out int surfaceHeight);
        Require(surfaceWidth == 3 * tileset.TileSize && surfaceHeight == 2 * tileset.TileSize);
        var surface = new uint[surfaceWidth * surfaceHeight];
        RequireThrows<ArgumentException>(() => MapRenderer.Render(map, tileset, new uint[1]));
        MapRenderer.Render(map, tileset, surface);
        int cell = tileset.TileSize;
        uint placeholder = MapRenderer.GetPlaceholderColor(9);
        Require((placeholder >> Config.Color.AlphaShift) == 0xFFu);
        for (int y = 0; y < surfaceHeight; y++)
        {
            for (int x = 0; x < surfaceWidth; x++)
            {
                uint expected;
                if (y < cell && x < cell) expected = Pack(255, 0, 0, 255);
                else if (y < cell && x < 2 * cell) expected = placeholder;
                else expected = Config.Color.TransparentPixelRgba;
                Require(surface[(y * surfaceWidth) + x] == expected);
            }
        }
        Require(MapRenderer.GetCellSize(null) == Config.Tileset.DefaultTileSize);
        Require(MapRenderer.GetPlaceholderColor(9) == placeholder); // deterministic
    }

    // ------------------------------------------------------------------
    // Session tileset CRUD: one history entry each, undo/redo, dirty
    // ------------------------------------------------------------------
    private static void TestSessionTilesetCrudUndoRedo()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("Tileset", project, root))
            {
                session.AutosaveEnabled = false;
                Require(project.Tileset == null);

                TilesetSection tileset = session.CreateTileset(16);
                Require(ReferenceEquals(project.Tileset, tileset));
                RequireThrows<InvalidOperationException>(() => session.CreateTileset(16));
                Require(session.Undo() && project.Tileset == null);
                Require(session.Redo() && ReferenceEquals(project.Tileset, tileset));

                // Blank tile + captured tile. The capture takes the flattened
                // top-left TileSize square of the current frame.
                session.SelectFrame(0, 0);
                Frame frame = session.CurrentFrame;
                frame.Layers[0].SetPixel(0, 0, Color(10, 20, 30, 255));
                frame.Layers[0].SetPixel(15, 15, Color(40, 50, 60, 255));
                frame.Layers[0].SetPixel(16, 16, Color(70, 80, 90, 255)); // outside 16x16

                TilesetTile blank = session.AddTilesetTile("Blank");
                TilesetTile captured = session.AddTilesetTileFromCurrentFrame("Captured");
                Require(tileset.Tiles.Count == 2);
                Require(ReferenceEquals(tileset.Tiles[0], blank));
                Require(ReferenceEquals(tileset.Tiles[1], captured));
                Require(blank.Pixels[0] == 0u);
                Require(captured.Pixels[0] == Pack(10, 20, 30, 255));
                Require(captured.Pixels[(15 * 16) + 15] == Pack(40, 50, 60, 255));

                // Rename + remove, all undoable.
                session.RenameTilesetTile(0, "Renamed");
                Require(tileset.Tiles[0].Name == "Renamed");
                Require(session.Undo() && tileset.Tiles[0].Name == "Blank");
                Require(session.Redo() && tileset.Tiles[0].Name == "Renamed");
                RequireThrows<ArgumentException>(() => session.RenameTilesetTile(0, " "));
                RequireThrows<ArgumentOutOfRangeException>(() => session.RenameTilesetTile(9, "X"));

                session.RemoveTilesetTile(0);
                Require(tileset.Tiles.Count == 1 && ReferenceEquals(tileset.Tiles[0], captured));
                Require(session.Undo() && tileset.Tiles.Count == 2);
                Require(ReferenceEquals(tileset.Tiles[0], blank));
                Require(session.Redo() && tileset.Tiles.Count == 1);

                // RemoveTileset restores the exact section on undo.
                session.RemoveTileset();
                Require(project.Tileset == null);
                RequireThrows<InvalidOperationException>(() => session.RemoveTileset());
                RequireThrows<InvalidOperationException>(() => session.AddTilesetTile("NoSection"));
                Require(session.Undo() && ReferenceEquals(project.Tileset, tileset));
                Require(session.Current.IsDirty);

                // The tile cap is enforced up front.
                while (tileset.Tiles.Count < Config.Tileset.MaxTiles)
                    tileset.Tiles.Add(new TilesetTile("Filler", tileset.TileSize));
                RequireThrows<InvalidOperationException>(() => session.AddTilesetTile("Overflow"));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Session map editing: cells, bulk ops, paint strokes, history
    // ------------------------------------------------------------------
    private static void TestSessionMapEditingUndoRedo()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("MapEdit", project, root))
            {
                session.AutosaveEnabled = false;
                RequireThrows<InvalidOperationException>(() => session.SetMapTile(0, 0, 1));

                MapSection map = session.CreateMapSection("Cavern", 8, 6, "");
                Require(ReferenceEquals(project.Map, map));
                RequireThrows<InvalidOperationException>(() => session.CreateMapSection("Other", 2, 2, ""));

                // Single-cell command: bounds report false, no-ops record nothing.
                int undoBase = session.History.Current.UndoCount;
                Require(!session.SetMapTile(-1, 0, 1));
                Require(!session.SetMapTile(8, 0, 1));
                Require(session.History.Current.UndoCount == undoBase);
                RequireThrows<ArgumentOutOfRangeException>(() => session.SetMapTile(0, 0, -2));
                Require(session.SetMapTile(2, 1, 3));
                Require(map.Grid.GetTile(2, 1) == 3);
                Require(session.History.Current.UndoCount == undoBase + 1);
                Require(session.SetMapTile(2, 1, 3)); // unchanged: no new entry
                Require(session.History.Current.UndoCount == undoBase + 1);
                Require(session.Undo() && map.Grid.GetTile(2, 1) == Config.MapDocument.BlankTileId);
                Require(session.Redo() && map.Grid.GetTile(2, 1) == 3);

                // Rect fill: one entry, exact sparse undo (the already-3 cell
                // keeps its value class in the diff).
                int changed = session.FillMapRect(1, 1, 3, 2, 3);
                Require(changed == 6);
                Require(session.History.Current.UndoCount == undoBase + 2);
                Require(session.Undo());
                Require(map.Grid.GetTile(2, 1) == 3); // pre-fill value restored
                Require(map.Grid.GetTile(1, 1) == Config.MapDocument.BlankTileId);
                Require(session.Redo());

                // Flood fill: contiguous blank region from (0,0) - the filled
                // rect blocks it; ONE history entry, exact undo.
                long beforeFlood = CountMapCells(map, Config.MapDocument.BlankTileId);
                int flooded = session.FloodFillMapTiles(0, 0, 7);
                Require(flooded > 0);
                Require(CountMapCells(map, 7) == flooded);
                Require(session.Undo());
                Require(CountMapCells(map, Config.MapDocument.BlankTileId) == beforeFlood);
                Require(session.Redo());
                Require(CountMapCells(map, 7) == flooded);
                // Flooding with the same id is a no-op and records nothing.
                int floodEntries = session.History.Current.UndoCount;
                Require(session.FloodFillMapTiles(0, 0, 7) == 0);
                Require(session.History.Current.UndoCount == floodEntries);

                // Paint stroke: many cells, ONE entry; revisits keep the first
                // before-value; blank stroke commits nothing.
                Require(session.BeginMapPaint(5));
                Require(!session.BeginMapPaint(5)); // no nesting
                Require(session.PaintMapCell(0, 5));
                Require(session.PaintMapCell(1, 5));
                Require(session.PaintMapCell(1, 5));
                Require(!session.PaintMapCell(99, 99));
                RequireThrows<InvalidOperationException>(() => session.SetMapTile(0, 0, 1));
                RequireThrows<InvalidOperationException>(() => session.RemoveMapSection());
                int strokeBase = session.History.Current.UndoCount;
                Require(session.EndMapPaint());
                Require(!session.EndMapPaint()); // already closed
                Require(session.History.Current.UndoCount == strokeBase + 1);
                Require(map.Grid.GetTile(0, 5) == 5 && map.Grid.GetTile(1, 5) == 5);
                Require(session.Undo());
                // The flood above painted these cells 7; the stroke's undo
                // restores exactly that pre-stroke value.
                Require(map.Grid.GetTile(0, 5) == 7);
                Require(session.Redo() && map.Grid.GetTile(0, 5) == 5);

                // Esc cancels the stroke and restores every touched cell.
                Require(session.BeginMapPaint(9));
                Require(session.PaintMapCell(4, 4));
                Require(map.Grid.GetTile(4, 4) == 9);
                session.CancelGesture();
                Require(!session.MapPaintActive);
                Require(map.Grid.GetTile(4, 4) == 7);
                Require(session.History.Current.UndoCount == strokeBase + 1);

                // A stroke that changes nothing publishes but records nothing.
                Require(session.BeginMapPaint(5));
                Require(session.PaintMapCell(0, 5));
                Require(!session.EndMapPaint());
                Require(session.History.Current.UndoCount == strokeBase + 1);

                // Undo during an open stroke cancels it first (no corruption):
                // the touched cell reverts to its flooded value 7 (via the
                // cancel), and the popped entry is the COMMITTED stroke above.
                Require(session.BeginMapPaint(11));
                Require(session.PaintMapCell(7, 0));
                Require(session.Undo());
                Require(!session.MapPaintActive);
                Require(map.Grid.GetTile(7, 0) == 7);
                Require(map.Grid.GetTile(0, 5) == 7);

                // Tileset reference set/undo.
                session.SetMapTilesetName("DungeonTiles");
                Require(map.TilesetName == "DungeonTiles");
                RequireThrows<ArgumentException>(() => session.SetMapTilesetName("a/b"));
                Require(session.Undo() && map.TilesetName == "");

                // Remove/undo the whole section.
                session.RemoveMapSection();
                Require(project.Map == null);
                Require(session.Undo() && ReferenceEquals(project.Map, map));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static long CountMapCells(MapSection map, short tileId)
    {
        long count = 0;
        foreach (short cell in map.Grid.RawData)
        {
            if (cell == tileId) count++;
        }
        return count;
    }

    // ------------------------------------------------------------------
    // MapTool gestures through the session: history + prop capture
    // ------------------------------------------------------------------
    private static void TestSessionMapToolGestureHistory()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            project.DesignerSeed = 777u;
            using (DesignerSession session = DesignerSession.Create("MapTool", project, root))
            {
                session.AutosaveEnabled = false;
                var tool = new MapTool();
                RequireThrows<InvalidOperationException>(() => session.ApplyMapTool(tool, 0, 0));
                MapSection map = session.CreateMapSection("Arena", 10, 10, "");

                // Noise fill: deterministic under the project seed - replaying
                // the same op on a fresh grid with the same seed and operation
                // index must produce identical tiles.
                tool.Mode = Config.Tool.Map.ModeNoiseFill;
                tool.TargetTileId = 4;
                tool.FillProbability = 0.5f;
                MapOperationResult noise = session.ApplyMapTool(tool, 0, 0);
                Require(noise.Status == MapOperationStatus.Succeeded);
                Require(session.History.Current.UndoCount == 2); // create + noise
                short[] noisedTiles = (short[])map.Grid.RawData.Clone();
                Require(CountMapCells(map, 4) > 0);

                var referenceGrid = new EFYVBackend.Core.Collections.FastGridMap(10, 10);
                referenceGrid.FillRect(0, 0, 10, 10, Config.MapDocument.BlankTileId);
                var referenceTool = new MapTool
                {
                    Mode = Config.Tool.Map.ModeNoiseFill,
                    TargetTileId = 4,
                    FillProbability = 0.5f,
                    TargetMap = referenceGrid,
                    Seed = 777u
                };
                referenceTool.Execute(null, 0, 0);
                for (int index = 0; index < noisedTiles.Length; index++)
                    Require(noisedTiles[index] == referenceGrid.RawData[index]);

                // Undo restores the pre-noise blank grid exactly.
                Require(session.Undo());
                Require(CountMapCells(map, Config.MapDocument.BlankTileId) == 100);
                Require(session.Redo());
                for (int index = 0; index < noisedTiles.Length; index++)
                    Require(map.Grid.RawData[index] == noisedTiles[index]);

                // Scatter appends props; undo removes exactly those instances
                // and redo re-adds them in order.
                tool.Mode = Config.Tool.Map.ModeScatter;
                tool.SelectedAsset = "Rock";
                tool.ScatterDensity = 5;
                MapOperationResult scatter = session.ApplyMapTool(tool, 5, 5);
                Require(scatter.Status == MapOperationStatus.Succeeded);
                Require(map.Grid.Props.Count == 5);
                for (int index = 0; index < map.Grid.Props.Count; index++)
                    Require(map.Grid.Props[index].AssetKey == "Rock");
                var scatteredProps = new List<EFYVBackend.Core.Collections.FastGridMap.MapPropData>();
                for (int index = 0; index < map.Grid.Props.Count; index++)
                    scatteredProps.Add(map.Grid.Props[index]);

                Require(session.Undo());
                Require(map.Grid.Props.Count == 0);
                Require(session.Redo());
                Require(map.Grid.Props.Count == 5);
                for (int index = 0; index < 5; index++)
                    Require(ReferenceEquals(map.Grid.Props[index], scatteredProps[index]));

                // A failed operation (missing selected asset) records nothing.
                tool.SelectedAsset = null;
                int entries = session.History.Current.UndoCount;
                MapOperationResult missing = session.ApplyMapTool(tool, 1, 1);
                Require(missing.Status == MapOperationStatus.MissingSelectedAsset);
                Require(session.History.Current.UndoCount == entries);

                // The session syncs the tool onto the section grid and seed.
                Require(ReferenceEquals(tool.TargetMap, map.Grid));
                Require(tool.Seed == project.DesignerSeed);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // .efyvmake sections: round trip, isolation, legacy, malformed corpus
    // ------------------------------------------------------------------
    private static void TestMapTilesetPersistenceRoundTrip()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
            EFYVProject project = CreateValidProject(root, 1);

            var tileset = new TilesetSection(8);
            tileset.Tiles.Add(new TilesetTile("Grass", 8, SolidTilePixels(8, Pack(0, 200, 0, 255))));
            tileset.Tiles.Add(new TilesetTile("Wall", 8, SolidTilePixels(8, Pack(90, 90, 90, 255))));
            project.Tileset = tileset;

            var map = new MapSection("Overworld", 5, 4, "TilesetStem");
            map.Grid.SetTile(0, 0, 1);
            map.Grid.SetTile(4, 3, 0);
            map.Grid.SetTile(2, 2, -1);
            map.Grid.Props.Add(new EFYVBackend.Core.Collections.FastGridMap.MapPropData
            {
                AssetKey = "Torch",
                X = 12,
                Y = -3,
                Scale = 1.5f
            });
            project.Map = map;

            string path = persistence.SaveProject("MapTrip", project, CancellationToken.None);
            EFYVProject restored = persistence.LoadProject("MapTrip");
            Require(restored.Tileset != null && restored.Tileset.TileSize == 8);
            Require(restored.Tileset.Tiles.Count == 2);
            Require(restored.Tileset.Tiles[0].Name == "Grass");
            Require(restored.Tileset.Tiles[1].Name == "Wall");
            foreach (uint pixel in restored.Tileset.Tiles[0].Pixels)
                Require(pixel == Pack(0, 200, 0, 255));
            Require(restored.Map != null && restored.Map.MapId == "Overworld");
            Require(restored.Map.TilesetName == "TilesetStem");
            Require(restored.Map.Grid.Width == 5 && restored.Map.Grid.Height == 4);
            Require(restored.Map.Grid.GetTile(0, 0) == 1);
            Require(restored.Map.Grid.GetTile(4, 3) == 0);
            Require(restored.Map.Grid.GetTile(2, 2) == -1);
            Require(restored.Map.Grid.GetTile(1, 1) == Config.MapDocument.BlankTileId);
            Require(restored.Map.Grid.Props.Count == 1);
            Require(restored.Map.Grid.Props[0].AssetKey == "Torch");
            Require(restored.Map.Grid.Props[0].X == 12 && restored.Map.Grid.Props[0].Y == -3);
            Require(restored.Map.Grid.Props[0].Scale == 1.5f);

            // No format-version bump; the sections serialize under camelCase.
            JsonObject saved = JsonNode.Parse(File.ReadAllText(path)).AsObject();
            Require((int)saved["formatVersion"] == Config.Persistence.ProjectFormatVersion);
            Require(saved.ContainsKey("tileset") && saved.ContainsKey("map"));

            // Snapshot isolation: post-capture mutations do not leak.
            ProjectPersistenceSnapshot snapshot = ProjectPersistenceSnapshot.Capture(project);
            map.Grid.SetTile(0, 0, 9);
            tileset.Tiles[0].Pixels[0] = 123u;
            persistence.SaveProject("MapSnapshot", snapshot, CancellationToken.None);
            EFYVProject fromSnapshot = persistence.LoadProject("MapSnapshot");
            Require(fromSnapshot.Map.Grid.GetTile(0, 0) == 1);
            Require(fromSnapshot.Tileset.Tiles[0].Pixels[0] == Pack(0, 200, 0, 255));

            // Legacy document (no sections) restores to null sections.
            JsonObject legacy = JsonNode.Parse(File.ReadAllText(path)).AsObject();
            legacy.Remove("tileset");
            legacy.Remove("map");
            File.WriteAllText(path, legacy.ToJsonString());
            EFYVProject legacyProject = persistence.LoadProject("MapTrip");
            Require(legacyProject.Tileset == null && legacyProject.Map == null);

            // Malformed corpus: every mutation must be rejected on load.
            string corpusPath = persistence.SaveProject("MapCorpus", project, CancellationToken.None);
            string baselineJson = File.ReadAllText(corpusPath);
            var corpus = new Action<JsonObject>[]
            {
                document => document["tileset"]["tileSize"] = Config.Tileset.MinTileSize - 1,
                document => document["tileset"]["tileSize"] = Config.Tileset.MaxTileSize + 1,
                document => document["tileset"]["tiles"] = null,
                document => document["tileset"]["tiles"][0]["name"] = "  ",
                document => document["tileset"]["tiles"][0]["name"] =
                    new string('n', Config.Tileset.MaxTileNameLength + 1),
                document => document["tileset"]["tiles"][0]["rgbaBytes"] =
                    Convert.ToBase64String(new byte[4]),
                document => document["tileset"]["tiles"][0]["rgbaBytes"] = null,
                document => document["map"]["mapId"] = "..",
                document => document["map"]["mapId"] = "a/b",
                document => document["map"]["width"] = 0,
                document => document["map"]["width"] = Config.MapDocument.MaxDimension + 1,
                document => document["map"]["tileBytes"] = Convert.ToBase64String(new byte[2]),
                document => document["map"]["tileBytes"] = null,
                document => document["map"]["tilesetName"] = "bad stem?",
                document => document["map"]["props"] = null,
                document => document["map"]["props"][0]["assetKey"] = "CON"
            };
            foreach (Action<JsonObject> mutate in corpus)
            {
                JsonObject malformed = JsonNode.Parse(baselineJson).AsObject();
                mutate(malformed);
                File.WriteAllText(corpusPath, malformed.ToJsonString());
                RequireThrows<InvalidDataException>(() => persistence.LoadProject("MapCorpus"));
            }

            // Save-side gate: an over-cap prop list never hits the disk.
            File.WriteAllText(corpusPath, baselineJson);
            EFYVProject oversized = persistence.LoadProject("MapCorpus");
            for (int index = 0; index <= Config.MapDocument.MaxProps; index++)
            {
                oversized.Map.Grid.Props.Add(new EFYVBackend.Core.Collections.FastGridMap.MapPropData
                {
                    AssetKey = "P",
                    Scale = 1f
                });
            }
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("MapOversized", oversized, CancellationToken.None));
            Require(!File.Exists(persistence.GetProjectPath("MapOversized")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Tileset export end to end: tile sheet + manifest + read-back
    // ------------------------------------------------------------------
    private static void TestTilesetExportEndToEnd()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            var toolbar = new ToolbarAPI(schema);
            EFYVProject project = toolbar.CreateNewProject(SharedConfig.GameAssetDisplayName);
            project.UnityProjectPath = root;
            project.AssetProperties[SharedConfig.AssetNameField] = "DungeonTiles";

            var engine = new ExportEngine(new ProjectValidator(schema));
            RequireThrows<InvalidOperationException>(() =>
                engine.ExportTileset(project, CancellationToken.None));

            var tileset = new TilesetSection(8);
            tileset.Tiles.Add(new TilesetTile("Grass", 8, SolidTilePixels(8, Pack(0, 255, 0, 255))));
            tileset.Tiles.Add(new TilesetTile("Dirt", 8, SolidTilePixels(8, Pack(120, 80, 40, 255))));
            tileset.Tiles.Add(new TilesetTile("Wall", 8, SolidTilePixels(8, Pack(90, 90, 90, 255))));
            project.Tileset = tileset;

            ExportResult result = engine.ExportTileset(project, CancellationToken.None);
            Require(File.Exists(result.MetadataPath) && File.Exists(result.ImagePath));
            Require(result.FrameCount == 3 && result.HitboxCount == 0);
            // 3 tiles -> 2x2 near-square grid of 8px frames.
            Require(result.AtlasWidth == 16 && result.AtlasHeight == 16);

            // The manifest survives the backend reader; index = tile id.
            Require(FastImporter.TryParse(result.MetadataPath, out var parsed) == EfyvParseResult.Valid);
            Require(parsed.EffectiveDocumentVersion == BackendConfig.Exporter.CurrentDocumentVersion);
            Require(parsed.tileset.HasValue);
            Require(parsed.tileset.Value.tileSize == 8);
            Require(parsed.tileset.Value.tiles.Count == 3);
            Require(parsed.tileset.Value.tiles[0] == "Grass");
            Require(parsed.tileset.Value.tiles[1] == "Dirt");
            Require(parsed.tileset.Value.tiles[2] == "Wall");
            Require(parsed.atlas.HasValue);
            Require(parsed.atlas.Value.frameWidth == 8 && parsed.atlas.Value.frameHeight == 8);
            Require(parsed.atlas.Value.animations.Count == 1);
            Require(parsed.atlas.Value.animations[0].name == Config.Export.TilesetAnimationName);
            Require(parsed.atlas.Value.animations[0].fps == Config.Export.TilesetAnimationFps);
            Require(parsed.atlas.Value.animations[0].startFrame == 0);
            Require(parsed.atlas.Value.animations[0].frameCount == 3);

            // The tile-sheet pixels land row-major in tile-id order.
            byte[] png = File.ReadAllBytes(result.ImagePath);
            var decoded = EFYVBackend.Core.Export.FastPngDecoder.Read(png, out int pngWidth, out int pngHeight);
            Require(pngWidth == 16 && pngHeight == 16);
            Require(decoded[0] == Pack(0, 255, 0, 255));        // tile 0 at (0,0)
            Require(decoded[8] == Pack(120, 80, 40, 255));      // tile 1 at (8,0)
            Require(decoded[8 * 16] == Pack(90, 90, 90, 255));  // tile 2 at (0,8)
            Require(decoded[(8 * 16) + 8] == 0u);               // capacity slot: transparent

            // An empty tileset (created, no tiles) refuses to export.
            var emptyProject = toolbar.CreateNewProject(SharedConfig.GameAssetDisplayName);
            emptyProject.UnityProjectPath = root;
            emptyProject.AssetProperties[SharedConfig.AssetNameField] = "EmptyTiles";
            emptyProject.Tileset = new TilesetSection(8);
            RequireThrows<InvalidOperationException>(() =>
                engine.ExportTileset(emptyProject, CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Map export end to end: .efyvmap through the shared envelope
    // ------------------------------------------------------------------
    private static void TestMapExportEndToEnd()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            EFYVProject project = CreateValidProject(root, 1);
            var engine = new ExportEngine(new ProjectValidator(new AssetSchemaService()));
            RequireThrows<InvalidOperationException>(() => engine.ExportMap(project));

            var map = new MapSection("Catacombs", 6, 3, "DungeonTiles");
            map.Grid.SetTile(0, 0, 2);
            map.Grid.SetTile(5, 2, 0);
            map.Grid.Props.Add(new EFYVBackend.Core.Collections.FastGridMap.MapPropData
            {
                AssetKey = "Sarcophagus",
                X = 40,
                Y = 16,
                Scale = 2f
            });
            project.Map = map;

            string published = engine.ExportMap(project);
            Require(published.EndsWith(
                "Catacombs" + BackendConfig.MapFile.Extension,
                StringComparison.Ordinal));
            Require(published.Contains(
                Path.Combine(Config.Export.DirAssets, Config.Export.DirRawArt),
                StringComparison.Ordinal));
            Require(File.Exists(published));

            Require(FastMapImporter.TryParse(published, out MapFileData parsed) == EfyvParseResult.Valid);
            Require(parsed.Width == 6 && parsed.Height == 3);
            Require(parsed.TilesetName == "DungeonTiles");
            Require(parsed.Tiles[0] == 2);
            Require(parsed.Tiles[(2 * 6) + 5] == 0);
            Require(parsed.Tiles[1] == Config.MapDocument.BlankTileId);
            Require(parsed.Props.Length == 1);
            Require(parsed.Props[0].AssetKey == "Sarcophagus");
            Require(parsed.Props[0].X == 40 && parsed.Props[0].Y == 16 && parsed.Props[0].Scale == 2f);

            // Republish replaces atomically (same path, new content).
            map.Grid.SetTile(1, 1, 4);
            string republished = engine.ExportMap(project);
            Require(republished == published);
            Require(FastMapImporter.TryParse(published, out MapFileData reparsed) == EfyvParseResult.Valid);
            Require(reparsed.Tiles[(1 * 6) + 1] == 4);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
}
