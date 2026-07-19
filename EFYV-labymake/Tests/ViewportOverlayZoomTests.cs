using System;
using System.Collections.Generic;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

// batch3.7 agent - item #31: viewport designer overlays + the public
// zoom/pan API. Reference models independently replicate the blit's
// dest->source float mapping, the FastMemory.BlendColor math, and the
// overlay geometry (checker cells, grid boundary scan with the
// exactly-once intersection rule, rect outlines, pivot crosses) so the
// production passes are pinned pixel for pixel.
internal static partial class Program
{
    // --- Group 1: SetZoom/SetPan/ResetView (batch-2 carried gap) ---------------

    private static void TestViewportZoomPanResetApiContract()
    {
        var viewport = new ViewportController();

        // Clamping and non-finite rejection.
        viewport.SetZoom(Config.Viewport.MaxZoom + 5f);
        Require(viewport.ZoomLevel == Config.Viewport.MaxZoom);
        viewport.SetZoom(-3f);
        Require(viewport.ZoomLevel == Config.Viewport.MinZoom);
        viewport.SetZoom(2.5f);
        Require(viewport.ZoomLevel == 2.5f);
        RequireThrows<ArgumentOutOfRangeException>(() => viewport.SetZoom(float.NaN));
        RequireThrows<ArgumentOutOfRangeException>(() => viewport.SetZoom(float.PositiveInfinity));
        RequireThrows<ArgumentOutOfRangeException>(() => viewport.SetZoom(float.NegativeInfinity));
        Require(viewport.ZoomLevel == 2.5f); // rejected calls leave state alone

        // Plain SetZoom never touches offsets; SetPan is absolute, Pan relative.
        viewport.SetPan(11, -7);
        viewport.SetZoom(3f);
        Require(viewport.OffsetX == 11 && viewport.OffsetY == -7);
        viewport.Pan(2, 3);
        Require(viewport.OffsetX == 13 && viewport.OffsetY == -4);

        // Anchored SetZoom is bit-identical to the anchored OnScroll step from
        // the same start state (same canvas-point capture, same offset math).
        var wheel = new ViewportController();
        wheel.SetPan(5, 9);
        wheel.OnScroll(1f, 40, 25);
        var api = new ViewportController();
        api.SetPan(5, 9);
        api.SetZoom(Config.Viewport.DefaultZoomLevel + Config.Viewport.ZoomStep, 40, 25);
        Require(api.ZoomLevel == wheel.ZoomLevel);
        Require(api.OffsetX == wheel.OffsetX && api.OffsetY == wheel.OffsetY);

        // The canvas point under the anchor survives an anchored zoom exactly
        // (integer canvas coordinates scale exactly under the new zoom).
        var anchored = new ViewportController();
        anchored.SetPan(-3, 6);
        anchored.SetZoom(4f);
        anchored.ScreenToCanvas(50, 42, out int beforeX, out int beforeY);
        anchored.SetZoom(8f, 50, 42);
        anchored.ScreenToCanvas(50, 42, out int afterX, out int afterY);
        Require(afterX == beforeX && afterY == beforeY);
        // Anchored overload clamps too.
        anchored.SetZoom(float.MaxValue, 50, 42);
        Require(anchored.ZoomLevel == Config.Viewport.MaxZoom);

        // ResetView restores the construction-time defaults.
        anchored.ResetView();
        Require(anchored.ZoomLevel == Config.Viewport.DefaultZoomLevel);
        Require(anchored.OffsetX == Config.Viewport.DefaultOffsetX);
        Require(anchored.OffsetY == Config.Viewport.DefaultOffsetY);

        // OnScroll still clamps identically after API-driven state changes.
        anchored.SetZoom(Config.Viewport.MinZoom);
        anchored.OnScroll(-1f);
        Require(anchored.ZoomLevel == Config.Viewport.MinZoom);
    }

    // --- Group 2: checkerboard backdrop composited in core ---------------------

    private static void TestOverlayCheckerboardCoreComposite()
    {
        var frame = new Frame(2, 2);
        frame.Layers[0].SetPixel(0, 0, Color(255, 0, 0, 255));  // opaque
        frame.Layers[0].SetPixel(1, 0, Color(0, 255, 0, 128));  // semi-transparent
        var viewport = new ViewportController();
        viewport.SetZoom(2f);
        viewport.SetPan(1, 1);

        var overlays = new ViewportOverlaySettings { Enabled = ViewportOverlayKind.Checkerboard };
        overlays.Checkerboard.CellShift = 1;
        overlays.Checkerboard.LightRgba = Pack(200, 200, 200, 255);
        overlays.Checkerboard.DarkRgba = Pack(90, 90, 90, 255);

        const int screenSize = 8;
        var screen = new uint[screenSize * screenSize];
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);

        uint[] expected = OverlayBlitReference(
            frame, 2f, 1, 1, screenSize, screenSize);
        OverlayCheckerReference(
            expected, 2, 2, 2f, 1, 1, screenSize, screenSize,
            overlays.Checkerboard.LightRgba, overlays.Checkerboard.DarkRgba, 1);
        RequireBuffersEqual(expected, screen);

        // Every inside pixel came out opaque; outside pixels stay transparent
        // (the host paints its own workspace color there).
        for (int y = 0; y < screenSize; y++)
        {
            for (int x = 0; x < screenSize; x++)
            {
                bool inside = x >= 1 && x <= 4 && y >= 1 && y <= 4;
                uint value = screen[y * screenSize + x];
                if (inside) Require((value >> Config.Color.AlphaShift) == 255u);
                else Require(value == 0u);
            }
        }

        // Null settings and ViewportOverlayKind.None are byte-identical to the
        // plain overload (the no-behavior-change contract).
        var plain = new uint[screen.Length];
        viewport.RenderToScreenBuffer(frame, plain, screenSize, screenSize);
        var nullSettings = new uint[screen.Length];
        viewport.RenderToScreenBuffer(frame, null, null, nullSettings, screenSize, screenSize);
        RequireBuffersEqual(plain, nullSettings);
        overlays.Enabled = ViewportOverlayKind.None;
        var noneSettings = new uint[screen.Length];
        viewport.RenderToScreenBuffer(frame, null, overlays, noneSettings, screenSize, screenSize);
        RequireBuffersEqual(plain, noneSettings);
        overlays.Enabled = ViewportOverlayKind.Checkerboard;

        // The checker is SCREEN-anchored: panning moves the art, not the cell
        // phase - a fully transparent canvas pixel shows the cell color that
        // belongs to its screen position regardless of the pan.
        var bare = new Frame(2, 2);
        foreach (int panX in new[] { 1, 2 })
        {
            viewport.SetPan(panX, 1);
            viewport.RenderToScreenBuffer(bare, null, overlays, screen, screenSize, screenSize);
            // Screen pixel (2,2): cell parity (1+1)&1=0 -> light, both pans.
            Require(screen[2 * screenSize + 2] == overlays.Checkerboard.LightRgba);
        }

        // Config gate: only the ENABLED pass validates its struct.
        overlays.Checkerboard.CellShift = -1;
        RequireThrows<ArgumentOutOfRangeException>(() =>
            viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize));
        overlays.Checkerboard.CellShift = 16;
        RequireThrows<ArgumentOutOfRangeException>(() =>
            viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize));
        overlays.Enabled = ViewportOverlayKind.None;
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);
        overlays.Checkerboard.CellShift = 1;
        overlays.Enabled = ViewportOverlayKind.Checkerboard;

        // Onion overload + checkerboard: ghosts blend first, the checker then
        // composites under the whole ghost+current stack.
        var ghost = new Frame(2, 2);
        ghost.Layers[0].SetPixel(1, 1, Color(0, 0, 255, 255));
        var current = new Frame(2, 2);
        var frames = new List<Frame> { ghost, current };
        var onionSettings = new OnionSkinSettings();
        viewport.SetPan(1, 1);
        viewport.RenderToScreenBuffer(
            frames, 1, onionSettings, null, overlays, screen, screenSize, screenSize);

        var composed = new PixelColor[4];
        viewport.ComposeOnionSkin(frames, 1, onionSettings, composed);
        var onionFrame = new Frame(2, 2);
        for (int index = 0; index < composed.Length; index++)
            onionFrame.Layers[0].Pixels[index] = composed[index];
        uint[] onionExpected = OverlayBlitReference(
            onionFrame, 2f, 1, 1, screenSize, screenSize);
        OverlayCheckerReference(
            onionExpected, 2, 2, 2f, 1, 1, screenSize, screenSize,
            overlays.Checkerboard.LightRgba, overlays.Checkerboard.DarkRgba, 1);
        RequireBuffersEqual(onionExpected, screen);
    }

    // --- Group 3: pixel/tile grid boundary lines --------------------------------

    private static void TestOverlayGridBoundaryReference()
    {
        var frame = new Frame(4, 4);
        var viewport = new ViewportController();
        viewport.SetZoom(4f);
        viewport.SetPan(3, 2);
        const int screenSize = 24;
        var screen = new uint[screenSize * screenSize];

        var overlays = new ViewportOverlaySettings { Enabled = ViewportOverlayKind.PixelGrid };
        overlays.PixelGrid.LineRgba = Pack(255, 255, 255, 255);
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);

        // Expected vertical lines at x = 3,7,11,15 and horizontal at
        // y = 2,6,10,14 (the leading canvas edge included), confined to the
        // canvas area; over a transparent canvas an opaque line is exact.
        uint[] expected = new uint[screen.Length];
        OverlayGridReference(
            expected, 4, 4, 4f, 3, 2, screenSize, screenSize, 1,
            overlays.PixelGrid.LineRgba);
        RequireBuffersEqual(expected, screen);
        Require(screen[6 * screenSize + 7] == overlays.PixelGrid.LineRgba);   // boundary pixel
        Require(screen[7 * screenSize + 8] == 0u);                            // cell interior
        Require(screen[6 * screenSize + 19] == 0u);                           // past the canvas

        // Below the zoom threshold the pass draws nothing.
        viewport.SetZoom(overlays.PixelGrid.MinZoom - 0.2f);
        var below = new uint[screen.Length];
        viewport.RenderToScreenBuffer(frame, null, overlays, below, screenSize, screenSize);
        var plainBelow = new uint[screen.Length];
        viewport.RenderToScreenBuffer(frame, plainBelow, screenSize, screenSize);
        RequireBuffersEqual(plainBelow, below);
        // ... and the threshold is inclusive.
        overlays.PixelGrid.MinZoom = 2f;
        viewport.SetZoom(2f);
        viewport.RenderToScreenBuffer(frame, null, overlays, below, screenSize, screenSize);
        Require(below[2 * screenSize + 3] == overlays.PixelGrid.LineRgba);
        overlays.PixelGrid.MinZoom = float.NaN;
        RequireThrows<ArgumentOutOfRangeException>(() =>
            viewport.RenderToScreenBuffer(frame, null, overlays, below, screenSize, screenSize));
        overlays.PixelGrid.MinZoom = Config.Overlay.DefaultPixelGridMinZoom;

        // Tile grid: TileSize-cell boundaries; TileSize 0 = no context = off.
        viewport.SetZoom(4f);
        overlays.Enabled = ViewportOverlayKind.TileGrid;
        overlays.TileGrid.LineRgba = Pack(10, 220, 40, 255);
        overlays.TileGrid.TileSize = 2;
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);
        expected = new uint[screen.Length];
        OverlayGridReference(
            expected, 4, 4, 4f, 3, 2, screenSize, screenSize, 2,
            overlays.TileGrid.LineRgba);
        RequireBuffersEqual(expected, screen);
        Require(screen[6 * screenSize + 11] == overlays.TileGrid.LineRgba);   // tile boundary
        Require(screen[6 * screenSize + 7] == 0u);                            // pixel boundary only
        overlays.TileGrid.TileSize = Config.Overlay.InactiveTileSize;
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);
        var plainAtFour = new uint[screen.Length];
        viewport.RenderToScreenBuffer(frame, plainAtFour, screenSize, screenSize);
        RequireBuffersEqual(plainAtFour, screen);

        // Both grids: the tile line draws after (over) the pixel line.
        overlays.Enabled = ViewportOverlayKind.PixelGrid | ViewportOverlayKind.TileGrid;
        overlays.TileGrid.TileSize = 2;
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);
        Require(screen[7 * screenSize + 11] == overlays.TileGrid.LineRgba);
        Require(screen[7 * screenSize + 7] == overlays.PixelGrid.LineRgba);

        // PixelGrid geometry == TileGrid geometry at cell size 1.
        overlays.Enabled = ViewportOverlayKind.PixelGrid;
        overlays.PixelGrid.MinZoom = 0f;
        overlays.PixelGrid.LineRgba = Pack(1, 2, 3, 200);
        viewport.SetZoom(2.7f);
        viewport.SetPan(-3, 4);
        var viaPixel = new uint[screen.Length];
        viewport.RenderToScreenBuffer(frame, null, overlays, viaPixel, screenSize, screenSize);
        overlays.Enabled = ViewportOverlayKind.TileGrid;
        overlays.TileGrid.TileSize = 1;
        overlays.TileGrid.LineRgba = overlays.PixelGrid.LineRgba;
        var viaTile = new uint[screen.Length];
        viewport.RenderToScreenBuffer(frame, null, overlays, viaTile, screenSize, screenSize);
        RequireBuffersEqual(viaPixel, viaTile);

        // Exactly-once blending at intersections: over an OPAQUE canvas a
        // semi-transparent grid must produce ONE blend step everywhere,
        // including line crossings - pinned by the randomized model below.
        var random = new Random(9137);
        for (int round = 0; round < 120; round++)
        {
            int canvasWidth = 1 + random.Next(9);
            int canvasHeight = 1 + random.Next(9);
            var fuzzFrame = new Frame(canvasWidth, canvasHeight);
            for (int index = 0; index < fuzzFrame.Layers[0].Pixels.Length; index++)
            {
                fuzzFrame.Layers[0].Pixels[index].Rgba = random.Next(2) == 0
                    ? 0u
                    : Pack((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            }
            float zoom = 0.5f + (float)random.NextDouble() * 7.5f;
            int offsetX = random.Next(-10, 11);
            int offsetY = random.Next(-10, 11);
            int cellSize = 1 + random.Next(4);
            uint lineColor = Pack(
                (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256),
                (byte)random.Next(256));

            viewport.SetZoom(zoom);
            viewport.SetPan(offsetX, offsetY);
            overlays.TileGrid.TileSize = cellSize;
            overlays.TileGrid.LineRgba = lineColor;
            const int fuzzScreen = 16;
            var actual = new uint[fuzzScreen * fuzzScreen];
            viewport.RenderToScreenBuffer(fuzzFrame, null, overlays, actual, fuzzScreen, fuzzScreen);

            uint[] model = OverlayBlitReference(
                fuzzFrame, viewport.ZoomLevel, offsetX, offsetY, fuzzScreen, fuzzScreen);
            OverlayGridReference(
                model, canvasWidth, canvasHeight, viewport.ZoomLevel, offsetX, offsetY,
                fuzzScreen, fuzzScreen, cellSize, lineColor);
            RequireBuffersEqual(model, actual);
        }
    }

    // --- Group 4: hitbox rectangles with per-key colors --------------------------

    private static void TestOverlayHitboxRectsAndKeyColors()
    {
        // The per-key color scheme is deterministic FNV-over-RGB, opaque.
        uint hurtboxColor = ViewportController.GetHitboxKeyColor(Config.Hitbox.DefaultKeyHurtbox);
        Require(hurtboxColor ==
            ((uint)EFYVBackend.Core.Math.FastMath.FastHash(Config.Hitbox.DefaultKeyHurtbox) |
                (255u << Config.Color.AlphaShift)));
        uint attackColor = ViewportController.GetHitboxKeyColor("AttackBox");
        Require(attackColor != hurtboxColor);
        Require((attackColor >> Config.Color.AlphaShift) == 255u);

        var frame = new Frame(32, 32);   // default Hurtbox {0,0,1,1} present
        var attack = new EFYVBackend.Core.Models.HitboxData();
        attack.X = 0.25f;
        attack.Y = 0.25f;
        attack.Width = 0.5f;
        attack.Height = 0.5f;
        frame.Hitboxes["AttackBox"] = attack;
        // default(HitboxData) bypasses the semantic constructor: an all-zero
        // box - the overlay skips it rather than painting a degenerate line.
        frame.Hitboxes["ZeroBox"] = default(EFYVBackend.Core.Models.HitboxData);
        var broken = new EFYVBackend.Core.Models.HitboxData();
        broken.X = float.NaN;
        frame.Hitboxes["NanBox"] = broken;

        var viewport = new ViewportController();
        viewport.SetZoom(2f);
        viewport.SetPan(0, 0);
        var overlays = new ViewportOverlaySettings { Enabled = ViewportOverlayKind.Hitboxes };
        Require(overlays.Hitboxes.PixelsPerUnit == Config.Hitbox.PixelsPerUnit);

        const int screenSize = 64;
        var screen = new uint[screenSize * screenSize];
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);

        // Hurtbox: canvas [0,16)x[0,16) -> screen [0..31]^2 outline.
        // AttackBox: canvas [4,12)x[4,12) -> screen [8..23]^2 outline.
        // The outlines do not overlap, so the expected buffer is
        // paint-order independent.
        var expected = new uint[screen.Length];
        OverlayRectOutlineReference(
            expected, screenSize, screenSize, 0f, 0f, 16f, 16f, 2f, 0, 0, hurtboxColor);
        OverlayRectOutlineReference(
            expected, screenSize, screenSize, 4f, 4f, 12f, 12f, 2f, 0, 0, attackColor);
        RequireBuffersEqual(expected, screen);
        Require(screen[0] == hurtboxColor);
        Require(screen[8 * screenSize + 8] == attackColor);
        Require(screen[16 * screenSize + 16] == 0u);   // interior untouched

        // Clipping: a pan that pushes both boxes partially off-screen renders
        // the clipped reference exactly (and never faults).
        viewport.SetPan(-10, -20);
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);
        expected = new uint[screen.Length];
        OverlayRectOutlineReference(
            expected, screenSize, screenSize, 0f, 0f, 16f, 16f, 2f, -10, -20, hurtboxColor);
        OverlayRectOutlineReference(
            expected, screenSize, screenSize, 4f, 4f, 12f, 12f, 2f, -10, -20, attackColor);
        RequireBuffersEqual(expected, screen);

        // A sub-screen-pixel box still draws a visible 1-pixel line (0.1
        // units * 16 ppu * 0.5 zoom = 0.8 screen pixels -> one pixel; the
        // scale factors are powers of two so the reference floats are exact).
        viewport.SetPan(0, 0);
        viewport.SetZoom(0.5f);
        var tiny = new Frame(32, 32);
        var sliver = new EFYVBackend.Core.Models.HitboxData();
        sliver.Width = 0.1f;
        sliver.Height = 0.1f;
        tiny.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = sliver;
        viewport.RenderToScreenBuffer(tiny, null, overlays, screen, screenSize, screenSize);
        expected = new uint[screen.Length];
        OverlayRectOutlineReference(
            expected, screenSize, screenSize, 0f, 0f, 0.1f * 16f, 0.1f * 16f, 0.5f, 0, 0, hurtboxColor);
        RequireBuffersEqual(expected, screen);
        Require(screen[0] == hurtboxColor);

        // Config gate: non-positive/non-finite PixelsPerUnit faults loudly.
        overlays.Hitboxes.PixelsPerUnit = 0f;
        RequireThrows<ArgumentOutOfRangeException>(() =>
            viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize));
        overlays.Hitboxes.PixelsPerUnit = float.NaN;
        RequireThrows<ArgumentOutOfRangeException>(() =>
            viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize));
        overlays.Hitboxes.PixelsPerUnit = Config.Hitbox.PixelsPerUnit;
    }

    // --- Group 5: attachment outlines + pivot markers ----------------------------

    private static void TestOverlayAttachmentOutlinesAndPivotMarkers()
    {
        var pixels = new uint[4 * 6];
        for (int index = 0; index < pixels.Length; index++) pixels[index] = Pack(10, 20, 30, 255);
        var element = new SubElement("gem", 4, 6, pixels);
        element.PivotX = 1;
        element.PivotY = 2;

        var frame = new Frame(16, 16);
        frame.Attachments.Add(new SubElementAttachment("gem", 5, 7, 0, true, false));
        frame.Attachments.Add(new SubElementAttachment("missing", 2, 2, 0, false, false));

        // The outline must frame EXACTLY the pixels the export flatten would
        // blend: flipped pivot (4-1-1, 2) = (2, 2) -> origin (3, 5), bounds
        // canvas [3,7)x[5,11). Pinned against the real flatten.
        var flattened = new PixelColor[16 * 16];
        EFYVLabyMake.Core.Export.ExportEngine.CompositeAttachment(
            flattened, 16, 16, element, new AttachmentSnapshot(frame.Attachments[0]));
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if (flattened[y * 16 + x].Rgba == 0u) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        Require(minX == 3 && maxX == 6 && minY == 5 && maxY == 10);

        var viewport = new ViewportController();
        viewport.SetZoom(2f);
        viewport.SetPan(0, 0);
        var overlays = new ViewportOverlaySettings { Enabled = ViewportOverlayKind.AttachmentOutlines };
        overlays.AttachmentSources = new List<SubElement> { element };

        const int screenSize = 44;
        var screen = new uint[screenSize * screenSize];
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);

        var expected = new uint[screen.Length];
        OverlayRectOutlineReference(
            expected, screenSize, screenSize, 3f, 5f, 7f, 11f, 2f, 0, 0,
            Config.Overlay.AttachmentOutlineRgba);
        RequireBuffersEqual(expected, screen);   // 'missing' resolves nothing -> no second outline

        // No sources at all: outlines silently skip (nothing rendered).
        overlays.AttachmentSources = null;
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);
        RequireBuffersEqual(new uint[screen.Length], screen);
        overlays.AttachmentSources = new List<SubElement> { element };

        // Pivot markers: the explicit host pivot plus EVERY attachment anchor
        // (resolved or not), crosses centered on the addressed canvas pixel.
        overlays.Enabled = ViewportOverlayKind.PivotMarkers;
        overlays.Pivots.HasExplicitPivot = true;
        overlays.Pivots.ExplicitPivotX = 12;
        overlays.Pivots.ExplicitPivotY = 3;
        overlays.Pivots.MarkerRadius = 2;
        overlays.Pivots.MarkerRgba = Pack(255, 255, 255, 128);
        viewport.SetZoom(3f);
        viewport.SetPan(1, 0);
        viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize);

        expected = new uint[screen.Length];
        OverlayPivotCrossReference(
            expected, screenSize, screenSize, 12, 3, 3f, 1, 0, 2, overlays.Pivots.MarkerRgba);
        OverlayPivotCrossReference(
            expected, screenSize, screenSize, 5, 7, 3f, 1, 0, 2, overlays.Pivots.MarkerRgba);
        OverlayPivotCrossReference(
            expected, screenSize, screenSize, 2, 2, 3f, 1, 0, 2, overlays.Pivots.MarkerRgba);
        RequireBuffersEqual(expected, screen);
        // A semi-transparent marker keeps its single-blend alpha everywhere -
        // the arms never double-blend the center pixel.
        Require(screen[10 * screenSize + 38] == overlays.Pivots.MarkerRgba);

        // Radius zero degenerates to a single center pixel; out-of-range
        // radii fault loudly.
        overlays.Pivots.MarkerRadius = 0;
        overlays.Pivots.HasExplicitPivot = false;
        var dotFrame = new Frame(16, 16);
        dotFrame.Attachments.Add(new SubElementAttachment("gem", 4, 4, 0, false, false));
        viewport.RenderToScreenBuffer(dotFrame, null, overlays, screen, screenSize, screenSize);
        expected = new uint[screen.Length];
        OverlayPivotCrossReference(
            expected, screenSize, screenSize, 4, 4, 3f, 1, 0, 0, overlays.Pivots.MarkerRgba);
        RequireBuffersEqual(expected, screen);
        overlays.Pivots.MarkerRadius = -1;
        RequireThrows<ArgumentOutOfRangeException>(() =>
            viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize));
        overlays.Pivots.MarkerRadius = Config.Overlay.MaxPivotMarkerRadius + 1;
        RequireThrows<ArgumentOutOfRangeException>(() =>
            viewport.RenderToScreenBuffer(frame, null, overlays, screen, screenSize, screenSize));
        overlays.Pivots.MarkerRadius = Config.Overlay.DefaultPivotMarkerRadius;
    }

    // --- Group 6: pass composition, state reuse, zero-alloc steady state ---------

    private static void TestOverlayComposeStateAndSteadyState()
    {
        // Onion overload: overlay context comes from the CURRENT frame only.
        var ghost = new Frame(8, 8);
        var onlyGhost = new EFYVBackend.Core.Models.HitboxData();
        onlyGhost.Width = 2f;
        onlyGhost.Height = 2f;
        ghost.Hitboxes["OnlyGhost"] = onlyGhost;
        var current = new Frame(8, 8);
        var onlyCurrent = new EFYVBackend.Core.Models.HitboxData();
        onlyCurrent.Width = 2f;
        onlyCurrent.Height = 1f;
        current.Hitboxes["OnlyCurrent"] = onlyCurrent;
        var frames = new List<Frame> { ghost, current };

        var viewport = new ViewportController();
        viewport.SetZoom(2f);
        viewport.SetPan(0, 0);
        var overlays = new ViewportOverlaySettings
        {
            Enabled = ViewportOverlayKind.Checkerboard | ViewportOverlayKind.Hitboxes
        };
        overlays.Hitboxes.PixelsPerUnit = 2f;

        const int screenSize = 32;
        var screen = new uint[screenSize * screenSize];
        var onion = new OnionSkinSettings();
        viewport.RenderToScreenBuffer(frames, 1, onion, null, overlays, screen, screenSize, screenSize);
        uint ghostColor = ViewportController.GetHitboxKeyColor("OnlyGhost");
        uint currentColor = ViewportController.GetHitboxKeyColor("OnlyCurrent");
        bool sawGhost = false, sawCurrent = false;
        for (int index = 0; index < screen.Length; index++)
        {
            if (screen[index] == ghostColor) sawGhost = true;
            if (screen[index] == currentColor) sawCurrent = true;
        }
        Require(!sawGhost && sawCurrent);

        // The floating buffer composites into the CONTENT, so the backdrop
        // slides under it like any other canvas pixels.
        var floatPixels = new uint[1] { Pack(9, 9, 9, 255) };
        var floating = new FloatingSelection(3, 3, 1, 1, floatPixels, new bool[1] { true });
        overlays.Enabled = ViewportOverlayKind.Checkerboard;
        viewport.RenderToScreenBuffer(current, floating, overlays, screen, screenSize, screenSize);
        Require(screen[6 * screenSize + 6] == Pack(9, 9, 9, 255));

        // Rendering with overlays leaves no residue: a follow-up plain render
        // matches a fresh controller's plain render bit for bit, and repeated
        // overlay renders are deterministic.
        var again = new uint[screen.Length];
        viewport.RenderToScreenBuffer(current, floating, overlays, again, screenSize, screenSize);
        RequireBuffersEqual(screen, again);
        var plainAfter = new uint[screen.Length];
        viewport.RenderToScreenBuffer(current, plainAfter, screenSize, screenSize);
        var fresh = new ViewportController();
        fresh.SetZoom(2f);
        fresh.SetPan(0, 0);
        var plainFresh = new uint[screen.Length];
        fresh.RenderToScreenBuffer(current, plainFresh, screenSize, screenSize);
        RequireBuffersEqual(plainFresh, plainAfter);

        // Zero-alloc steady state: after warmup, the full overlay render
        // (onion + every pass + attachment sources) allocates NOTHING on the
        // rendering thread.
        var pixels = new uint[2 * 2];
        for (int index = 0; index < pixels.Length; index++) pixels[index] = Pack(1, 2, 3, 255);
        var element = new SubElement("part", 2, 2, pixels);
        current.Attachments.Add(new SubElementAttachment("part", 4, 4, 0, false, true));
        current.Attachments.Add(new SubElementAttachment("absent", 1, 1, 0, false, false));
        overlays.Enabled =
            ViewportOverlayKind.Checkerboard | ViewportOverlayKind.PixelGrid |
            ViewportOverlayKind.TileGrid | ViewportOverlayKind.Hitboxes |
            ViewportOverlayKind.AttachmentOutlines | ViewportOverlayKind.PivotMarkers;
        overlays.PixelGrid.MinZoom = 0f;
        overlays.TileGrid.TileSize = 2;
        overlays.AttachmentSources = new List<SubElement> { element };
        overlays.Pivots.HasExplicitPivot = true;
        overlays.Pivots.ExplicitPivotX = 2;
        overlays.Pivots.ExplicitPivotY = 2;

        for (int warmup = 0; warmup < 16; warmup++)
        {
            viewport.RenderToScreenBuffer(
                frames, 1, onion, floating, overlays, screen, screenSize, screenSize);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int steady = 0; steady < 8; steady++)
        {
            viewport.RenderToScreenBuffer(
                frames, 1, onion, floating, overlays, screen, screenSize, screenSize);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0L);
    }

    // --- Item #31 reference models ------------------------------------------------

    private static void RequireBuffersEqual(uint[] expected, uint[] actual)
    {
        Require(expected.Length == actual.Length);
        for (int index = 0; index < expected.Length; index++)
            Require(expected[index] == actual[index]);
    }

    // Independent replication of the ScaleBlitNearestNeighbor dest->source
    // mapping over the frame's flattened pixels.
    private static uint[] OverlayBlitReference(
        Frame frame, float zoom, int offsetX, int offsetY, int screenWidth, int screenHeight)
    {
        PixelColor[] flattened = frame.FlattenLayers();
        var screen = new uint[screenWidth * screenHeight];
        float invZoom = 1f / zoom;
        for (int y = 0; y < screenHeight; y++)
        {
            float sourceY = (y - offsetY) * invZoom;
            int sourceRow = (int)sourceY;
            bool rowInside = sourceY >= 0f && (uint)sourceRow < (uint)frame.Height;
            for (int x = 0; x < screenWidth; x++)
            {
                float sourceX = (x - offsetX) * invZoom;
                int sourceColumn = (int)sourceX;
                bool inside = rowInside && sourceX >= 0f && (uint)sourceColumn < (uint)frame.Width;
                screen[y * screenWidth + x] = inside
                    ? flattened[sourceRow * frame.Width + sourceColumn].Rgba
                    : 0u;
            }
        }
        return screen;
    }

    // Independent replication of FastMemory.BlendColor (straight RGBA,
    // red in the low byte): src blended over dest.
    private static uint OverlayReferenceBlend(uint destRgba, uint srcRgba)
    {
        uint srcA = (srcRgba >> 24) & 0xFFu;
        if (srcA == 0u) return destRgba;
        if (srcA == 255u) return srcRgba;
        uint destA = (destRgba >> 24) & 0xFFu;
        if (destA == 0u) return srcRgba;

        uint srcR = srcRgba & 0xFFu, srcG = (srcRgba >> 8) & 0xFFu, srcB = (srcRgba >> 16) & 0xFFu;
        uint destR = destRgba & 0xFFu, destG = (destRgba >> 8) & 0xFFu, destB = (destRgba >> 16) & 0xFFu;
        uint invA = 255u - srcA;
        if (destA == 255u)
        {
            uint red = (srcR * srcA + destR * invA + 127u) / 255u;
            uint green = (srcG * srcA + destG * invA + 127u) / 255u;
            uint blue = (srcB * srcA + destB * invA + 127u) / 255u;
            return red | (green << 8) | (blue << 16) | (255u << 24);
        }
        uint alphaNumerator = srcA * 255u + destA * invA;
        uint outAlpha = (alphaNumerator + 127u) / 255u;
        uint outR = (srcR * srcA * 255u + destR * destA * invA + (alphaNumerator >> 1)) / alphaNumerator;
        uint outG = (srcG * srcA * 255u + destG * destA * invA + (alphaNumerator >> 1)) / alphaNumerator;
        uint outB = (srcB * srcA * 255u + destB * destA * invA + (alphaNumerator >> 1)) / alphaNumerator;
        return outR | (outG << 8) | (outB << 16) | (outAlpha << 24);
    }

    // Checkerboard reference: inside the canvas area the existing screen
    // content re-composites over the screen-anchored cell color.
    private static void OverlayCheckerReference(
        uint[] screen, int canvasWidth, int canvasHeight, float zoom, int offsetX, int offsetY,
        int screenWidth, int screenHeight, uint lightRgba, uint darkRgba, int cellShift)
    {
        float invZoom = 1f / zoom;
        for (int y = 0; y < screenHeight; y++)
        {
            float sourceY = (y - offsetY) * invZoom;
            if (!(sourceY >= 0f && (uint)(int)sourceY < (uint)canvasHeight)) continue;
            for (int x = 0; x < screenWidth; x++)
            {
                float sourceX = (x - offsetX) * invZoom;
                if (!(sourceX >= 0f && (uint)(int)sourceX < (uint)canvasWidth)) continue;
                uint cell = (((x >> cellShift) + (y >> cellShift)) & 1) == 0 ? lightRgba : darkRgba;
                screen[y * screenWidth + x] = OverlayReferenceBlend(cell, screen[y * screenWidth + x]);
            }
        }
    }

    private static int OverlayCellReference(
        int screenCoordinate, int offset, float invZoom, int canvasExtent, int cellSize)
    {
        float source = (screenCoordinate - offset) * invZoom;
        int sourceIndex = (int)source;
        if (!(source >= 0f && (uint)sourceIndex < (uint)canvasExtent)) return -1;
        return sourceIndex / cellSize;
    }

    // Grid reference: vertical boundary columns first, then horizontal
    // boundary rows skipping pixels a vertical line already claimed - the
    // production pass's exactly-once blend rule.
    private static void OverlayGridReference(
        uint[] screen, int canvasWidth, int canvasHeight, float zoom, int offsetX, int offsetY,
        int screenWidth, int screenHeight, int cellSize, uint lineRgba)
    {
        float invZoom = 1f / zoom;
        for (int x = 0; x < screenWidth; x++)
        {
            int cell = OverlayCellReference(x, offsetX, invZoom, canvasWidth, cellSize);
            if (cell < 0) continue;
            int previous = x == 0 ? -1 : OverlayCellReference(x - 1, offsetX, invZoom, canvasWidth, cellSize);
            if (previous == cell) continue;
            for (int y = 0; y < screenHeight; y++)
            {
                float sourceY = (y - offsetY) * invZoom;
                if (!(sourceY >= 0f && (uint)(int)sourceY < (uint)canvasHeight)) continue;
                screen[y * screenWidth + x] = OverlayReferenceBlend(screen[y * screenWidth + x], lineRgba);
            }
        }
        for (int y = 0; y < screenHeight; y++)
        {
            int cell = OverlayCellReference(y, offsetY, invZoom, canvasHeight, cellSize);
            if (cell < 0) continue;
            int previous = y == 0 ? -1 : OverlayCellReference(y - 1, offsetY, invZoom, canvasHeight, cellSize);
            if (previous == cell) continue;
            for (int x = 0; x < screenWidth; x++)
            {
                float sourceX = (x - offsetX) * invZoom;
                if (!(sourceX >= 0f && (uint)(int)sourceX < (uint)canvasWidth)) continue;
                int columnCell = OverlayCellReference(x, offsetX, invZoom, canvasWidth, cellSize);
                int columnPrevious = x == 0 ? -1 : OverlayCellReference(x - 1, offsetX, invZoom, canvasWidth, cellSize);
                if (columnCell >= 0 && columnPrevious != columnCell) continue;
                screen[y * screenWidth + x] = OverlayReferenceBlend(screen[y * screenWidth + x], lineRgba);
            }
        }
    }

    private static int OverlayFloor(float value)
    {
        int truncated = (int)value;
        return value < truncated ? truncated - 1 : truncated;
    }

    private static void OverlayHorizontalSpanReference(
        uint[] screen, int screenWidth, int screenHeight, int y, int fromX, int toX, uint colorRgba)
    {
        if ((uint)y >= (uint)screenHeight) return;
        if (fromX < 0) fromX = 0;
        if (toX >= screenWidth) toX = screenWidth - 1;
        for (int x = fromX; x <= toX; x++)
            screen[y * screenWidth + x] = OverlayReferenceBlend(screen[y * screenWidth + x], colorRgba);
    }

    private static void OverlayVerticalSpanReference(
        uint[] screen, int screenWidth, int screenHeight, int x, int fromY, int toY, uint colorRgba)
    {
        if ((uint)x >= (uint)screenWidth) return;
        if (fromY < 0) fromY = 0;
        if (toY >= screenHeight) toY = screenHeight - 1;
        for (int y = fromY; y <= toY; y++)
            screen[y * screenWidth + x] = OverlayReferenceBlend(screen[y * screenWidth + x], colorRgba);
    }

    // Rect-outline reference: canvas rect [left,rightExclusive) x
    // [top,bottomExclusive) -> 1-pixel outline over the covered screen
    // region, minimum one line, every pixel blended once.
    private static void OverlayRectOutlineReference(
        uint[] screen, int screenWidth, int screenHeight,
        float canvasLeft, float canvasTop, float canvasRightExclusive, float canvasBottomExclusive,
        float zoom, int offsetX, int offsetY, uint colorRgba)
    {
        int left = offsetX + OverlayFloor(canvasLeft * zoom);
        int top = offsetY + OverlayFloor(canvasTop * zoom);
        int right = offsetX + OverlayFloor(canvasRightExclusive * zoom) - 1;
        int bottom = offsetY + OverlayFloor(canvasBottomExclusive * zoom) - 1;
        if (right < left) right = left;
        if (bottom < top) bottom = top;

        OverlayHorizontalSpanReference(screen, screenWidth, screenHeight, top, left, right, colorRgba);
        if (bottom != top)
            OverlayHorizontalSpanReference(screen, screenWidth, screenHeight, bottom, left, right, colorRgba);
        if (bottom - top > 1)
        {
            OverlayVerticalSpanReference(screen, screenWidth, screenHeight, left, top + 1, bottom - 1, colorRgba);
            if (right != left)
                OverlayVerticalSpanReference(screen, screenWidth, screenHeight, right, top + 1, bottom - 1, colorRgba);
        }
    }

    // Pivot-cross reference: crosshair on the canvas pixel CENTER, vertical
    // arm skipping the center pixel (exactly-once blending).
    private static void OverlayPivotCrossReference(
        uint[] screen, int screenWidth, int screenHeight,
        int canvasX, int canvasY, float zoom, int offsetX, int offsetY, int radius, uint colorRgba)
    {
        int screenX = offsetX + OverlayFloor((canvasX + 0.5f) * zoom);
        int screenY = offsetY + OverlayFloor((canvasY + 0.5f) * zoom);
        OverlayHorizontalSpanReference(
            screen, screenWidth, screenHeight, screenY, screenX - radius, screenX + radius, colorRgba);
        if (radius > 0)
        {
            OverlayVerticalSpanReference(
                screen, screenWidth, screenHeight, screenX, screenY - radius, screenY - 1, colorRgba);
            OverlayVerticalSpanReference(
                screen, screenWidth, screenHeight, screenX, screenY + 1, screenY + radius, colorRgba);
        }
    }
}
