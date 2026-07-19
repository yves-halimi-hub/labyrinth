// batch3/palette agent (item #8): palette and color workflow - the Palette
// model with ordered swatches, the persisted most-recent-first RecentColorRing,
// the .efyvmake palette section (optional - legacy documents restore empty),
// the undoable session palette CRUD + swatch selection surface, the composited
// EyedropperTool, the one-history-entry sparse global color swap, and the
// palette-constraint snap (squared-Euclidean straight-RGBA, ties to the lowest
// index) applied to color tools at gesture start.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    // ------------------------------------------------------------------
    // Palette model, recent ring MRU semantics, and the nearest metric
    // ------------------------------------------------------------------
    private static void TestPaletteModelRecentRingAndNearestMetric()
    {
        // Name contract: non-whitespace, bounded length, enforced by both the
        // constructor and the setter.
        RequireThrows<ArgumentException>(() => new Palette(null));
        RequireThrows<ArgumentException>(() => new Palette(""));
        RequireThrows<ArgumentException>(() => new Palette("   "));
        RequireThrows<ArgumentException>(() =>
            new Palette(new string('p', Config.Palette.MaxNameLength + 1)));
        var palette = new Palette(new string('p', Config.Palette.MaxNameLength));
        Require(palette.Name.Length == Config.Palette.MaxNameLength);
        Require(palette.Colors.Count == 0);
        RequireThrows<ArgumentException>(() => palette.Name = " ");
        palette.Name = "Renamed";
        Require(palette.Name == "Renamed");

        // Nearest metric: exact matches win, null throws, empty reports
        // NotFoundIndex rather than inventing an entry.
        RequireThrows<ArgumentNullException>(() => Palette.FindNearestIndex(null, 0u));
        Require(Palette.FindNearestIndex(new uint[0], 0u) == Config.Common.NotFoundIndex);
        palette.Colors.Add(Pack(255, 0, 0, 255));
        palette.Colors.Add(Pack(0, 0, 255, 255));
        palette.Colors.Add(Pack(0, 255, 0, 255));
        Require(palette.FindNearestIndex(Pack(0, 255, 0, 255)) == 2);
        Require(palette.FindNearestIndex(Pack(250, 5, 0, 255)) == 0);
        Require(palette.FindNearestIndex(Pack(0, 10, 240, 255)) == 1);

        // Tie-break: equidistant entries resolve to the LOWEST index. (110 is
        // exactly 10 away from both 100 and 120 on the red channel.)
        var tied = new List<uint> { Pack(120, 0, 0, 255), Pack(100, 0, 0, 255) };
        Require(Palette.FindNearestIndex(tied, Pack(110, 0, 0, 255)) == 0);

        // Alpha participates in the metric: a translucent red is closer to the
        // transparent red entry than to the opaque one.
        var alphaPalette = new List<uint> { Pack(255, 0, 0, 255), Pack(255, 0, 0, 0) };
        Require(Palette.FindNearestIndex(alphaPalette, Pack(255, 0, 0, 10)) == 1);
        Require(Palette.FindNearestIndex(alphaPalette, Pack(255, 0, 0, 250)) == 0);

        // Randomized agreement with an independent brute-force reference.
        var random = new Random(4321);
        var colors = new List<uint>();
        for (int index = 0; index < 32; index++)
        {
            colors.Add(Pack(
                (byte)random.Next(256),
                (byte)random.Next(256),
                (byte)random.Next(256),
                (byte)random.Next(256)));
        }
        for (int probe = 0; probe < 200; probe++)
        {
            uint query = Pack(
                (byte)random.Next(256),
                (byte)random.Next(256),
                (byte)random.Next(256),
                (byte)random.Next(256));
            Require(Palette.FindNearestIndex(colors, query) ==
                ReferenceNearestIndex(colors, query));
        }

        // Recent ring: bounded MRU with de-duplication.
        RequireThrows<ArgumentOutOfRangeException>(() => new RecentColorRing(0));
        var ring = new RecentColorRing();
        Require(ring.Capacity == Config.Palette.RecentColorCapacity);
        Require(ring.Count == 0);
        RequireThrows<ArgumentOutOfRangeException>(() => { _ = ring[0]; });

        Require(ring.Push(1u));
        Require(ring.Count == 1 && ring[0] == 1u);
        Require(!ring.Push(1u));            // already most recent: unchanged
        Require(ring.Count == 1);
        Require(ring.Push(2u) && ring.Push(3u));
        Require(ring[0] == 3u && ring[1] == 2u && ring[2] == 1u);
        Require(ring.Push(1u));             // re-use moves to the front
        Require(ring.Count == 3);
        Require(ring[0] == 1u && ring[1] == 3u && ring[2] == 2u);

        var bounded = new RecentColorRing(3);
        for (uint value = 0; value < 5; value++) Require(bounded.Push(value));
        Require(bounded.Count == 3);
        Require(bounded[0] == 4u && bounded[1] == 3u && bounded[2] == 2u);
        RequireThrows<ArgumentOutOfRangeException>(() => { _ = bounded[3]; });
        uint[] snapshot = bounded.ToArray();
        Require(snapshot.Length == 3 && snapshot[0] == 4u && snapshot[2] == 2u);
        bounded.Clear();
        Require(bounded.Count == 0 && bounded.ToArray().Length == 0);

        // Capacity fill of the default ring evicts oldest-first.
        var full = new RecentColorRing();
        for (uint value = 0; value < (uint)Config.Palette.RecentColorCapacity + 4u; value++)
            full.Push(value);
        Require(full.Count == Config.Palette.RecentColorCapacity);
        Require(full[0] == (uint)Config.Palette.RecentColorCapacity + 3u);
        Require(full[Config.Palette.RecentColorCapacity - 1] == 4u);
    }

    private static int ReferenceNearestIndex(List<uint> colors, uint query)
    {
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int index = 0; index < colors.Count; index++)
        {
            double distance = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                double delta = (double)((colors[index] >> shift) & 0xFFu) -
                    (double)((query >> shift) & 0xFFu);
                distance += delta * delta;
            }
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------
    // .efyvmake palette section: round trip, legacy documents, corpus
    // ------------------------------------------------------------------
    private static void TestPalettePersistenceRoundTripAndLegacyDocuments()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
            EFYVProject project = CreateValidProject(root, 1);

            var mainPalette = new Palette("Main");
            mainPalette.Colors.Add(Pack(255, 0, 0, 255));
            mainPalette.Colors.Add(Pack(0, 255, 0, 128));
            mainPalette.Colors.Add(Pack(0, 0, 0, 0));
            var altPalette = new Palette("Alt");
            altPalette.Colors.Add(Pack(12, 34, 56, 78));
            project.Palettes.Add(mainPalette);
            project.Palettes.Add(altPalette);
            project.RecentColors.Push(Pack(1, 1, 1, 255));
            project.RecentColors.Push(Pack(2, 2, 2, 255));
            project.RecentColors.Push(Pack(1, 1, 1, 255)); // moves back to front

            string path = persistence.SaveProject("PaletteTrip", project, CancellationToken.None);
            EFYVProject restored = persistence.LoadProject("PaletteTrip");
            Require(restored.Palettes.Count == 2);
            Require(restored.Palettes[0].Name == "Main" && restored.Palettes[1].Name == "Alt");
            Require(restored.Palettes[0].Colors.Count == 3);
            Require(restored.Palettes[0].Colors[0] == Pack(255, 0, 0, 255));
            Require(restored.Palettes[0].Colors[1] == Pack(0, 255, 0, 128));
            Require(restored.Palettes[0].Colors[2] == Pack(0, 0, 0, 0));
            Require(restored.Palettes[1].Colors[0] == Pack(12, 34, 56, 78));
            uint[] recents = restored.RecentColors.ToArray();
            Require(recents.Length == 2);
            Require(recents[0] == Pack(1, 1, 1, 255) && recents[1] == Pack(2, 2, 2, 255));

            // The extension did NOT bump the pinned format version, and the
            // new sections serialize under the camel-cased member names.
            JsonObject saved = JsonNode.Parse(File.ReadAllText(path)).AsObject();
            Require((int)saved["formatVersion"] == Config.Persistence.ProjectFormatVersion);
            Require(saved.ContainsKey("palettes") && saved.ContainsKey("recentColors"));
            Require(saved["palettes"].AsArray().Count == 2);

            // Snapshot isolation: mutations after capture do not leak into the
            // captured document.
            ProjectPersistenceSnapshot snapshot = ProjectPersistenceSnapshot.Capture(project);
            project.Palettes[0].Name = "MutatedAfterCapture";
            project.Palettes[0].Colors.Add(Pack(9, 9, 9, 9));
            project.RecentColors.Push(Pack(7, 7, 7, 7));
            persistence.SaveProject("PaletteSnapshot", snapshot, CancellationToken.None);
            EFYVProject fromSnapshot = persistence.LoadProject("PaletteSnapshot");
            Require(fromSnapshot.Palettes[0].Name == "Main");
            Require(fromSnapshot.Palettes[0].Colors.Count == 3);
            Require(fromSnapshot.RecentColors.ToArray().Length == 2);

            // Legacy document: removing both palette members simulates a file
            // written before item #8; it loads with empty palette state.
            JsonObject legacy = JsonNode.Parse(File.ReadAllText(path)).AsObject();
            legacy.Remove("palettes");
            legacy.Remove("recentColors");
            File.WriteAllText(path, legacy.ToJsonString());
            EFYVProject legacyProject = persistence.LoadProject("PaletteTrip");
            Require(legacyProject.Palettes.Count == 0);
            Require(legacyProject.RecentColors.Count == 0);
            Require(legacyProject.Animations.Count == project.Animations.Count);

            // Save-side rejection: a model holding more than MaxPalettes fails
            // document validation before any bytes hit the destination.
            EFYVProject oversized = CreateValidProject(root, 1);
            for (int index = 0; index <= Config.Palette.MaxPalettes; index++)
                oversized.Palettes.Add(new Palette("P" + index));
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("PaletteOversized", oversized, CancellationToken.None));
            Require(!File.Exists(persistence.GetProjectPath("PaletteOversized")));

            // Load-side malformed corpus: every mutation must throw
            // InvalidDataException, never restore a half-valid palette.
            persistence.SaveProject("PaletteCorpus", project, CancellationToken.None);
            string corpusPath = persistence.GetProjectPath("PaletteCorpus");
            JsonObject baseline = JsonNode.Parse(File.ReadAllText(corpusPath)).AsObject();

            var oversizedColors = new JsonArray();
            for (int index = 0; index <= Config.Palette.MaxSwatchesPerPalette; index++)
                oversizedColors.Add(0);
            var oversizedRecents = new JsonArray();
            for (int index = 0; index <= Config.Palette.RecentColorCapacity; index++)
                oversizedRecents.Add(index);
            var oversizedPalettes = new JsonArray();
            for (int index = 0; index <= Config.Palette.MaxPalettes; index++)
                oversizedPalettes.Add(new JsonObject
                {
                    ["name"] = "P" + index,
                    ["colors"] = new JsonArray()
                });

            Action<JsonObject>[] mutations =
            {
                document => document["palettes"].AsArray()[0] = null,
                document => document["palettes"].AsArray()[0]["name"] = "   ",
                document => document["palettes"].AsArray()[0]["name"] =
                    new string('n', Config.Palette.MaxNameLength + 1),
                document => document["palettes"].AsArray()[0]["colors"] = null,
                document => document["palettes"].AsArray()[0]["colors"] = oversizedColors,
                document => document["palettes"] = oversizedPalettes,
                document => document["recentColors"] = oversizedRecents
            };
            foreach (var mutation in mutations)
            {
                JsonObject malformed = baseline.DeepClone().AsObject();
                mutation(malformed);
                File.WriteAllText(corpusPath, malformed.ToJsonString());
                RequireThrows<InvalidDataException>(() => persistence.LoadProject("PaletteCorpus"));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Session palette CRUD: undoable, index-addressed, swatch selection
    // ------------------------------------------------------------------
    private static void TestSessionPaletteCrudUndoRedoAndRecents()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 1);
            using (DesignerSession session = DesignerSession.Create("PaletteCrud", project, root))
            {
                session.AutosaveEnabled = false;

                // Add / swatch CRUD / rename / remove, every step one command.
                Palette palette = session.AddPalette("Main");
                Require(ReferenceEquals(project.Palettes[0], palette));
                Require(session.Current.IsDirty);
                Require(session.History.Current.UndoCount == 1);

                session.AddSwatch(0, Pack(255, 0, 0, 255));
                session.AddSwatch(0, Pack(0, 255, 0, 255));
                session.AddSwatch(0, Pack(0, 0, 255, 255));
                Require(palette.Colors.Count == 3);
                Require(session.History.Current.UndoCount == 4);

                session.MoveSwatch(0, 2, 0);
                Require(palette.Colors[0] == Pack(0, 0, 255, 255));
                Require(palette.Colors[1] == Pack(255, 0, 0, 255));
                Require(palette.Colors[2] == Pack(0, 255, 0, 255));

                session.RemoveSwatch(0, 1);
                Require(palette.Colors.Count == 2 && palette.Colors[1] == Pack(0, 255, 0, 255));

                session.RenamePalette(0, "Primary");
                Require(palette.Name == "Primary");
                int commandsBeforeNoOps = session.History.Current.UndoCount;
                session.RenamePalette(0, "Primary");     // same name: no command
                session.MoveSwatch(0, 1, 1);             // same index: no command
                Require(session.History.Current.UndoCount == commandsBeforeNoOps);

                // Full undo unwinds to an empty palette list; full redo
                // replays to the exact final state.
                while (session.History.Current.CanUndo) Require(session.Undo());
                Require(project.Palettes.Count == 0);
                while (session.History.Current.CanRedo) Require(session.Redo());
                Require(project.Palettes.Count == 1);
                palette = project.Palettes[0];
                Require(palette.Name == "Primary");
                Require(palette.Colors.Count == 2);
                Require(palette.Colors[0] == Pack(0, 0, 255, 255));
                Require(palette.Colors[1] == Pack(0, 255, 0, 255));

                // Validation and capacity guards.
                RequireThrows<ArgumentException>(() => session.AddPalette("  "));
                RequireThrows<ArgumentOutOfRangeException>(() => session.RemovePalette(1));
                RequireThrows<ArgumentOutOfRangeException>(() => session.RenamePalette(-1, "X"));
                RequireThrows<ArgumentOutOfRangeException>(() => session.AddSwatch(7, 0u));
                RequireThrows<ArgumentOutOfRangeException>(() => session.RemoveSwatch(0, 2));
                RequireThrows<ArgumentOutOfRangeException>(() => session.MoveSwatch(0, 0, 5));

                while (project.Palettes.Count < Config.Palette.MaxPalettes)
                    project.Palettes.Add(new Palette("Filler" + project.Palettes.Count));
                RequireThrows<InvalidOperationException>(() => session.AddPalette("Overflow"));
                while (project.Palettes.Count > 1) project.Palettes.RemoveAt(1);

                while (palette.Colors.Count < Config.Palette.MaxSwatchesPerPalette)
                    palette.Colors.Add(0u);
                RequireThrows<InvalidOperationException>(() => session.AddSwatch(0, 1u));
                while (palette.Colors.Count > 2)
                    palette.Colors.RemoveAt(palette.Colors.Count - 1);

                // Swatch selection applies to the active color tool and feeds
                // the recent ring; stale indices report false without throwing.
                var pencil = new PencilTool();
                session.ActiveTool = pencil;
                uint selected;
                Require(session.TrySelectSwatch(0, 1, out selected));
                Require(selected == Pack(0, 255, 0, 255));
                Require(pencil.CurrentColor.Rgba == selected);
                Require(project.RecentColors.Count == 1 && project.RecentColors[0] == selected);
                Require(!session.TrySelectSwatch(0, 99, out selected));
                Require(!session.TrySelectSwatch(3, 0, out selected));
                Require(!session.TrySelectSwatch(-1, 0, out selected));

                // A non-color active tool still resolves the swatch (for the
                // host UI) but its own state is untouched.
                var eraser = new EraserTool();
                session.ActiveTool = eraser;
                Require(session.TrySelectSwatch(0, 0, out selected));
                Require(selected == Pack(0, 0, 255, 255));
                Require(project.RecentColors[0] == selected);

                // Recent tracking dirty-marks only on actual ring change and
                // never records history.
                int undoCountBeforeRecents = session.History.Current.UndoCount;
                long versionBefore = session.Current.ChangeVersion;
                session.NotifyColorUsed(Pack(9, 9, 9, 255));
                Require(session.Current.ChangeVersion == versionBefore + 1);
                session.NotifyColorUsed(Pack(9, 9, 9, 255)); // repeat: complete no-op
                Require(session.Current.ChangeVersion == versionBefore + 1);
                Require(session.History.Current.UndoCount == undoCountBeforeRecents);

                // Palette operations never touch frames, so - unlike frame or
                // layer CRUD - they leave an un-anchored floating selection alive.
                var select = new RectSelectTool();
                session.ActiveTool = select;
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(3, 3));
                Require(session.Selection != null);
                Require(session.LiftSelection(0, true));
                Require(session.Floating != null);
                session.AddSwatch(0, Pack(50, 60, 70, 255));
                Require(session.Floating != null);
                session.CancelFloating();
                Require(session.Floating == null);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Eyedropper: composited (FlattenLayers-exact) sampling, no history
    // ------------------------------------------------------------------
    private static void TestEyedropperCompositedPickContract()
    {
        // Multi-layer frame with per-pixel alpha, fractional layer opacity, a
        // hidden layer, and a zero-opacity layer: the pick must be bit-exact
        // with FlattenLayers at EVERY pixel (composited semantics - the
        // documented choice over active-layer sampling).
        var frame = new Frame(8, 6);
        Layer baseLayer = frame.Layers[0];
        var midLayer = new Layer("Mid", 8, 6) { Opacity = 0.6f };
        var hiddenLayer = new Layer("Hidden", 8, 6) { IsVisible = false };
        var mutedLayer = new Layer("Muted", 8, 6) { Opacity = 0f };
        frame.Layers.Add(midLayer);
        frame.Layers.Add(hiddenLayer);
        frame.Layers.Add(mutedLayer);

        var random = new Random(777);
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                if (x == 0 && y == 0) continue; // keep one fully transparent pixel
                baseLayer.SetPixel(x, y, Color(
                    (byte)random.Next(256), (byte)random.Next(256),
                    (byte)random.Next(256), (byte)(x < 4 ? 255 : random.Next(256))));
                midLayer.SetPixel(x, y, Color(
                    (byte)random.Next(256), (byte)random.Next(256),
                    (byte)random.Next(256), (byte)random.Next(256)));
                hiddenLayer.SetPixel(x, y, Color(255, 255, 255, 255));
                mutedLayer.SetPixel(x, y, Color(1, 2, 3, 255));
            }
        }

        PixelColor[] flattened = frame.FlattenLayers();
        bool sawCompositeDistinctFromEveryLayer = false;
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                PixelColor picked;
                Require(EyedropperTool.TrySampleComposited(frame, x, y, out picked));
                uint expected = flattened[y * frame.Width + x].Rgba;
                Require(picked.Rgba == expected);
                if (expected != baseLayer.GetPixel(x, y).Rgba &&
                    expected != midLayer.GetPixel(x, y).Rgba)
                    sawCompositeDistinctFromEveryLayer = true;
            }
        }
        // Proof this is genuinely composited: somewhere the blend differs from
        // every individual layer's raw value.
        Require(sawCompositeDistinctFromEveryLayer);

        // Transparent pixel picks the exact transparent dword; out-of-canvas
        // samples are rejected.
        PixelColor probe;
        Require(EyedropperTool.TrySampleComposited(frame, 0, 0, out probe));
        Require(probe.Rgba == Config.Color.TransparentPixelRgba);
        Require(!EyedropperTool.TrySampleComposited(frame, -1, 0, out probe));
        Require(!EyedropperTool.TrySampleComposited(frame, frame.Width, 0, out probe));
        Require(!EyedropperTool.TrySampleComposited(frame, 0, frame.Height, out probe));
        Require(!EyedropperTool.TrySampleComposited(null, 0, 0, out probe));

        // Null layer entries are skipped (ResizeCanvas can produce them);
        // mismatched layer dimensions fault like FlattenLayers does.
        frame.Layers.Add(null);
        PixelColor withNull;
        Require(EyedropperTool.TrySampleComposited(frame, 2, 2, out withNull));
        Require(withNull.Rgba == flattened[2 * frame.Width + 2].Rgba);
        frame.Layers.RemoveAt(frame.Layers.Count - 1);
        frame.Layers.Add(new Layer("Mismatched", frame.Width + 1, frame.Height));
        RequireThrows<InvalidOperationException>(() =>
        {
            PixelColor unused;
            EyedropperTool.TrySampleComposited(frame, 2, 2, out unused);
        });
        frame.Layers.RemoveAt(frame.Layers.Count - 1);

        // Session integration: picking is a normal gesture that samples on
        // down/drag/up, raises ColorPicked once, and records NO history.
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 1);
            using (DesignerSession session = DesignerSession.Create("Eyedropper", project, root))
            {
                session.AutosaveEnabled = false;
                Layer layer = session.CurrentFrame.Layers[0];
                layer.SetPixel(1, 1, Color(10, 20, 30, 255));
                layer.SetPixel(3, 3, Color(40, 50, 60, 255));
                PixelColor[] sessionFlattened = session.CurrentFrame.FlattenLayers();

                var eyedropper = new EyedropperTool();
                int eventCount = 0;
                PixelColor lastEvent = default;
                eyedropper.ColorPicked += picked => { eventCount++; lastEvent = picked; };
                session.ActiveTool = eyedropper;

                Require(session.PointerDown(1, 1));
                Require(eyedropper.PickedColor.Rgba == Pack(10, 20, 30, 255));
                Require(session.PointerDrag(3, 3));
                Require(eyedropper.PickedColor.Rgba == Pack(40, 50, 60, 255));
                Require(!session.PointerUp(0, 5)); // empty diff: returns false
                Require(eventCount == 1);
                Require(lastEvent.Rgba ==
                    sessionFlattened[5 * session.CurrentFrame.Width + 0].Rgba);
                Require(session.History.Current.UndoCount == 0);
                Require(!session.Current.IsDirty);

                // Ending the gesture off-canvas keeps the LAST on-canvas
                // sample as the final pick.
                Require(session.PointerDown(3, 3));
                Require(session.PointerDrag(-9, -9));
                Require(!session.PointerUp(-9, -9));
                Require(eventCount == 2);
                Require(lastEvent.Rgba == Pack(40, 50, 60, 255));

                // A gesture entirely outside the canvas raises no event at all.
                Require(session.PointerDown(-2, -2));
                Require(!session.PointerUp(-2, -2));
                Require(eventCount == 2);
                Require(!eyedropper.HasSample);
                Require(eyedropper.PickedColor.Rgba == Pack(40, 50, 60, 255));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Global color swap: frame/all-frames scopes, ONE sparse history entry
    // ------------------------------------------------------------------
    private static void TestColorSwapScopesSparseDiffsAndFuzz()
    {
        uint colorA = Pack(10, 20, 30, 255);
        uint colorB = Pack(200, 100, 50, 255);
        uint colorC = Pack(5, 6, 7, 8);
        uint colorD = Pack(90, 91, 92, 93);

        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 2);
            var altAnimation = new AnimationState("Alt", 4);
            altAnimation.Frames.Add(new Frame(8, 6, 0));
            project.Animations.Add(altAnimation);

            Frame frame0 = project.Animations[0].Frames[0];
            Frame frame1 = project.Animations[0].Frames[1];
            Frame altFrame = altAnimation.Frames[0];
            var hiddenTop = new Layer("HiddenTop", 8, 6) { IsVisible = false };
            frame0.Layers.Add(hiddenTop);

            // Deterministic seeding of A/B/C across frames and layers,
            // including the HIDDEN layer (swap is a data operation).
            frame0.Layers[0].SetPixel(0, 0, new PixelColor { Rgba = colorA });
            frame0.Layers[0].SetPixel(3, 2, new PixelColor { Rgba = colorA });
            frame0.Layers[0].SetPixel(5, 5, new PixelColor { Rgba = colorB });
            hiddenTop.SetPixel(7, 0, new PixelColor { Rgba = colorA });
            frame1.Layers[0].SetPixel(1, 1, new PixelColor { Rgba = colorA });
            frame1.Layers[0].SetPixel(2, 2, new PixelColor { Rgba = colorB });
            altFrame.Layers[0].SetPixel(4, 4, new PixelColor { Rgba = colorA });

            using (DesignerSession session = DesignerSession.Create("ColorSwap", project, root))
            {
                session.AutosaveEnabled = false;
                Require(session.SelectFrame(0, 0));

                uint[] frame0Layer0Before = SnapshotPixels(frame0.Layers[0]);
                uint[] frame0HiddenBefore = SnapshotPixels(hiddenTop);
                uint[] frame1Before = SnapshotPixels(frame1.Layers[0]);
                uint[] altBefore = SnapshotPixels(altFrame.Layers[0]);

                // Current-frame scope: both layers of frame0 (hidden included),
                // other frames untouched, exactly one history entry.
                Require(session.ReplaceColor(colorA, colorD, false) == 3);
                Require(session.History.Current.UndoCount == 1);
                Require(session.Current.IsDirty);
                Require(frame0.Layers[0].GetPixel(0, 0).Rgba == colorD);
                Require(frame0.Layers[0].GetPixel(3, 2).Rgba == colorD);
                Require(frame0.Layers[0].GetPixel(5, 5).Rgba == colorB);
                Require(hiddenTop.GetPixel(7, 0).Rgba == colorD);
                RequirePixels(frame1.Layers[0], frame1Before);
                RequirePixels(altFrame.Layers[0], altBefore);

                // One undo restores byte-exact state everywhere; redo re-swaps.
                Require(session.Undo());
                RequirePixels(frame0.Layers[0], frame0Layer0Before);
                RequirePixels(hiddenTop, frame0HiddenBefore);
                Require(session.Redo());
                Require(frame0.Layers[0].GetPixel(0, 0).Rgba == colorD);
                Require(session.Undo());

                // All-frames scope: every animation, every frame, still ONE
                // history entry (history count grows by exactly one).
                int undoCountBefore = session.History.Current.UndoCount;
                Require(session.ReplaceColor(colorA, colorD, true) == 5);
                Require(session.History.Current.UndoCount == undoCountBefore + 1);
                Require(frame0.Layers[0].GetPixel(0, 0).Rgba == colorD);
                Require(hiddenTop.GetPixel(7, 0).Rgba == colorD);
                Require(frame1.Layers[0].GetPixel(1, 1).Rgba == colorD);
                Require(altFrame.Layers[0].GetPixel(4, 4).Rgba == colorD);
                Require(frame1.Layers[0].GetPixel(2, 2).Rgba == colorB);
                Require(session.Undo());
                RequirePixels(frame0.Layers[0], frame0Layer0Before);
                RequirePixels(hiddenTop, frame0HiddenBefore);
                RequirePixels(frame1.Layers[0], frame1Before);
                RequirePixels(altFrame.Layers[0], altBefore);

                // No-ops: identical colors and no-match swaps record nothing.
                int idleUndoCount = session.History.Current.UndoCount;
                Require(session.ReplaceColor(colorA, colorA, true) == 0);
                Require(session.ReplaceColor(Pack(123, 45, 67, 89), colorD, true) == 0);
                Require(session.History.Current.UndoCount == idleUndoCount);

                // Replacing TRANSPARENT is legal (exact-match semantics): every
                // untouched pixel of the current frame's two layers matches.
                int transparentCount = 0;
                foreach (uint value in frame0Layer0Before)
                    if (value == SharedConfig.TransparentRgba) transparentCount++;
                foreach (uint value in frame0HiddenBefore)
                    if (value == SharedConfig.TransparentRgba) transparentCount++;
                Require(session.ReplaceColor(SharedConfig.TransparentRgba, colorC, false) ==
                    transparentCount);
                Require(frame0.Layers[0].GetPixel(7, 5).Rgba == colorC);
                Require(session.Undo());
                RequirePixels(frame0.Layers[0], frame0Layer0Before);

                // A frame referenced twice in scope is swapped exactly once
                // (layer de-duplication keeps the count honest).
                altAnimation.Frames.Add(altFrame);
                Require(session.ReplaceColor(colorA, colorD, true) == 5);
                Require(session.Undo());
                altAnimation.Frames.RemoveAt(altAnimation.Frames.Count - 1);

                // An un-anchored floating selection is canceled (lifted pixels
                // restored) BEFORE the swap scans, so the swap sees and
                // replaces the lifted color.
                var select = new RectSelectTool();
                session.ActiveTool = select;
                Require(session.PointerDown(0, 0));
                Require(!session.PointerUp(1, 1));
                Require(session.LiftSelection(0, true));
                Require(frame0.Layers[0].GetPixel(0, 0).Rgba == SharedConfig.TransparentRgba);
                Require(session.ReplaceColor(colorA, colorD, false) == 3);
                Require(session.Floating == null);
                Require(frame0.Layers[0].GetPixel(0, 0).Rgba == colorD);
                Require(session.Undo());
                RequirePixels(frame0.Layers[0], frame0Layer0Before);

                // Swapping mid-gesture is a programming error.
                var pencil = new PencilTool();
                session.ActiveTool = pencil;
                Require(session.PointerDown(1, 1));
                RequireThrows<InvalidOperationException>(() =>
                    session.ReplaceColor(colorA, colorD, false));
                session.CancelGesture();
            }

            // No selected frame: current-frame scope throws, all-frames scope
            // over zero frames reports zero.
            EFYVProject emptyProject = CreatePixelToolsProject(root, 8, 6, 0);
            using (DesignerSession emptySession =
                DesignerSession.Create("ColorSwapEmpty", emptyProject, root))
            {
                emptySession.AutosaveEnabled = false;
                Require(emptySession.CurrentFrame == null);
                RequireThrows<InvalidOperationException>(() =>
                    emptySession.ReplaceColor(colorA, colorD, false));
                Require(emptySession.ReplaceColor(colorA, colorD, true) == 0);
            }

            // Randomized fuzz against an independent scan/replace reference.
            var random = new Random(999);
            uint[] paletteChoices = { colorA, colorB, colorC, SharedConfig.TransparentRgba };
            for (int round = 0; round < 10; round++)
            {
                EFYVProject fuzzProject = CreatePixelToolsProject(root, 6, 5, 2);
                Frame fuzzFrame0 = fuzzProject.Animations[0].Frames[0];
                Frame fuzzFrame1 = fuzzProject.Animations[0].Frames[1];
                fuzzFrame0.Layers.Add(new Layer("Extra", 6, 5));
                var fuzzLayers = new List<Layer>
                {
                    fuzzFrame0.Layers[0], fuzzFrame0.Layers[1], fuzzFrame1.Layers[0]
                };
                foreach (Layer layer in fuzzLayers)
                {
                    for (int index = 0; index < layer.Pixels.Length; index++)
                        layer.Pixels[index].Rgba =
                            paletteChoices[random.Next(paletteChoices.Length)];
                }

                var referenceAfter = new List<uint[]>();
                int expectedCount = 0;
                foreach (Layer layer in fuzzLayers)
                {
                    uint[] reference = SnapshotPixels(layer);
                    for (int index = 0; index < reference.Length; index++)
                    {
                        if (reference[index] != colorA) continue;
                        reference[index] = colorB;
                        expectedCount++;
                    }
                    referenceAfter.Add(reference);
                }
                var originals = new List<uint[]>();
                foreach (Layer layer in fuzzLayers) originals.Add(SnapshotPixels(layer));

                using (DesignerSession fuzzSession =
                    DesignerSession.Create("ColorSwapFuzz" + round, fuzzProject, root))
                {
                    fuzzSession.AutosaveEnabled = false;
                    Require(fuzzSession.ReplaceColor(colorA, colorB, true) == expectedCount);
                    for (int layerIndex = 0; layerIndex < fuzzLayers.Count; layerIndex++)
                        RequirePixels(fuzzLayers[layerIndex], referenceAfter[layerIndex]);
                    if (expectedCount > 0)
                    {
                        Require(fuzzSession.Undo());
                        for (int layerIndex = 0; layerIndex < fuzzLayers.Count; layerIndex++)
                            RequirePixels(fuzzLayers[layerIndex], originals[layerIndex]);
                        Require(fuzzSession.Redo());
                        for (int layerIndex = 0; layerIndex < fuzzLayers.Count; layerIndex++)
                            RequirePixels(fuzzLayers[layerIndex], referenceAfter[layerIndex]);
                    }
                    else
                    {
                        Require(fuzzSession.History.Current.UndoCount == 0);
                    }
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Palette-constraint mode: gesture-start snap for color tools only
    // ------------------------------------------------------------------
    private static void TestPaletteConstraintSnapOnDrawTools()
    {
        uint red = Pack(255, 0, 0, 255);
        uint blue = Pack(0, 0, 255, 255);

        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 1);
            using (DesignerSession session = DesignerSession.Create("Constraint", project, root))
            {
                session.AutosaveEnabled = false;
                session.AddPalette("Snap");
                session.AddSwatch(0, red);
                session.AddSwatch(0, blue);
                Layer layer = session.CurrentFrame.Layers[0];

                // Disabled (the default): the raw color draws unchanged.
                Require(!session.PaletteConstraintEnabled);
                Require(session.ActivePaletteIndex == Config.Palette.DefaultActivePaletteIndex);
                uint nearBlue = Pack(10, 0, 200, 255);
                var pencil = new PencilTool { CurrentColor = new PixelColor { Rgba = nearBlue } };
                session.ActiveTool = pencil;
                Require(session.PointerDown(1, 1));
                Require(session.PointerUp(1, 1));
                Require(pencil.CurrentColor.Rgba == nearBlue);
                Require(layer.GetPixel(1, 1).Rgba == nearBlue);
                Require(session.Undo());

                // Enabled: pointer-down snaps the tool color to the nearest
                // palette entry BEFORE the stroke, and the snap sticks.
                session.PaletteConstraintEnabled = true;
                Require(session.PointerDown(2, 2));
                Require(session.PointerUp(2, 2));
                Require(pencil.CurrentColor.Rgba == blue);
                Require(layer.GetPixel(2, 2).Rgba == blue);

                uint nearRed = Pack(200, 40, 30, 255);
                pencil.CurrentColor = new PixelColor { Rgba = nearRed };
                Require(session.PointerDown(3, 3));
                Require(session.PointerUp(3, 3));
                Require(pencil.CurrentColor.Rgba == red);
                Require(layer.GetPixel(3, 3).Rgba == red);

                // An exact palette color is untouched by the snap.
                pencil.CurrentColor = new PixelColor { Rgba = red };
                Require(session.PointerDown(4, 4));
                Require(session.PointerUp(4, 4));
                Require(pencil.CurrentColor.Rgba == red);

                // The metric tie-break (lowest index) is observable through
                // the snap: 110 is equidistant from entries 120 and 100 but
                // the palette lists 120 first.
                session.AddPalette("Tie");
                session.AddSwatch(1, Pack(120, 0, 0, 255));
                session.AddSwatch(1, Pack(100, 0, 0, 255));
                session.ActivePaletteIndex = 1;
                pencil.CurrentColor = new PixelColor { Rgba = Pack(110, 0, 0, 255) };
                Require(session.PointerDown(5, 1));
                Require(session.PointerUp(5, 1));
                Require(pencil.CurrentColor.Rgba == Pack(120, 0, 0, 255));

                // Stale palette index and empty palettes disable the snap
                // silently; the eraser (no brush color) is never affected.
                session.ActivePaletteIndex = 99;
                pencil.CurrentColor = new PixelColor { Rgba = nearBlue };
                Require(session.PointerDown(6, 1));
                Require(session.PointerUp(6, 1));
                Require(pencil.CurrentColor.Rgba == nearBlue);
                Require(layer.GetPixel(6, 1).Rgba == nearBlue);

                session.AddPalette("Empty");
                session.ActivePaletteIndex = 2;
                pencil.CurrentColor = new PixelColor { Rgba = nearBlue };
                Require(session.PointerDown(7, 1));
                Require(session.PointerUp(7, 1));
                Require(pencil.CurrentColor.Rgba == nearBlue);

                session.ActivePaletteIndex = 0;
                var eraser = new EraserTool();
                session.ActiveTool = eraser;
                Require(session.PointerDown(2, 2));
                Require(session.PointerUp(2, 2));
                Require(layer.GetPixel(2, 2).Rgba == SharedConfig.TransparentRgba);

                // The eyedropper is not a color tool either: with constraint
                // mode on it still reports the RAW composited color.
                var eyedropper = new EyedropperTool();
                session.ActiveTool = eyedropper;
                Require(session.PointerDown(6, 1));
                Require(!session.PointerUp(6, 1));
                Require(eyedropper.PickedColor.Rgba == nearBlue);
            }

            // Fill is a color tool too: the snap applies to its flood color.
            EFYVProject fillProject = CreatePixelToolsProject(root, 4, 3, 1);
            using (DesignerSession fillSession =
                DesignerSession.Create("ConstraintFill", fillProject, root))
            {
                fillSession.AutosaveEnabled = false;
                fillSession.AddPalette("Snap");
                fillSession.AddSwatch(0, red);
                fillSession.AddSwatch(0, blue);
                fillSession.PaletteConstraintEnabled = true;
                var fill = new FillTool
                {
                    CurrentColor = new PixelColor { Rgba = Pack(230, 10, 10, 255) }
                };
                fillSession.ActiveTool = fill;
                Require(fillSession.PointerDown(1, 1));
                Require(fillSession.PointerUp(1, 1));
                Require(fill.CurrentColor.Rgba == red);
                Layer fillLayer = fillSession.CurrentFrame.Layers[0];
                for (int index = 0; index < fillLayer.Pixels.Length; index++)
                    Require(fillLayer.Pixels[index].Rgba == red);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
}
