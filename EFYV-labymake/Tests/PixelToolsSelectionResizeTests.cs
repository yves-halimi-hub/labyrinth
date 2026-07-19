// batch3/pixel-tools agent (item #9): first-class eraser, rect/lasso selection
// with the session floating buffer (lift/move/copy/paste/anchor as ONE sparse
// command), line/rect/ellipse gesture-preview shape tools, mirror symmetry on
// the drawing tools, and the command-backed ResizeCanvas 9-anchor model.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

internal static partial class Program
{
    // ------------------------------------------------------------------
    // Eraser: true transparency through the sparse-diff undo path
    // ------------------------------------------------------------------
    private static void TestPixelToolsEraserTrueTransparency()
    {
        // The eraser is deliberately NOT a color tool: it cannot be configured
        // to paint, and it never reads the brush color another tool selected.
        var eraser = new EraserTool();
        Require(!typeof(IColorTool).IsAssignableFrom(typeof(EraserTool)));
        Require(eraser is ILayerTool);

        // Brush size clamps like the pencil's.
        eraser.BrushSize = 0;
        Require(eraser.BrushSize == Config.Tool.Eraser.DefaultBrushSize);
        eraser.BrushSize = int.MaxValue;
        Require(eraser.BrushSize == Config.Tool.MaxBrushSize);
        eraser.BrushSize = 3;
        Require(eraser.BrushSize == 3);

        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 1);
            using (DesignerSession session = DesignerSession.Create("EraserTest", project, root))
            {
                session.AutosaveEnabled = false;
                Layer layer = session.CurrentFrame.Layers[0];

                // Opaque, semi-transparent, and low-alpha pixels all erase to
                // the exact zero dword (not just alpha zero).
                for (int y = 0; y < layer.Height; y++)
                {
                    for (int x = 0; x < layer.Width; x++)
                    {
                        layer.SetPixel(x, y, Color(
                            (byte)(x * 30 + 1),
                            (byte)(y * 40 + 2),
                            77,
                            (byte)(x == 0 ? 3 : 255)));
                    }
                }
                uint[] painted = SnapshotPixels(layer);

                eraser.BrushSize = 3;
                session.ActiveTool = eraser;
                Require(session.PointerDown(2, 2));
                Require(session.PointerDrag(5, 2));
                Require(session.PointerUp(5, 2));
                Require(session.History.Current.UndoCount == 1);

                // The 3-wide stroke from (2,2) to (5,2) is exactly transparent;
                // everything outside the stroke is untouched.
                int erased = 0;
                for (int y = 0; y < layer.Height; y++)
                {
                    for (int x = 0; x < layer.Width; x++)
                    {
                        uint value = layer.GetPixel(x, y).Rgba;
                        if (value == SharedConfig.TransparentRgba) erased++;
                        else Require(value == painted[y * layer.Width + x]);
                    }
                }
                Require(erased > 0);
                Require(layer.GetPixel(2, 2).Rgba == SharedConfig.TransparentRgba);
                Require(layer.GetPixel(5, 2).Rgba == SharedConfig.TransparentRgba);
                Require(layer.GetPixel(0, 0).Rgba == painted[0]);

                // One undo restores the exact pre-stroke bytes; redo re-erases.
                Require(session.Undo());
                RequirePixels(layer, painted);
                Require(session.Redo());
                Require(layer.GetPixel(2, 2).Rgba == SharedConfig.TransparentRgba);
                Require(session.Undo());
                RequirePixels(layer, painted);

                // An out-of-bounds tap is ignored by the tool, so the gesture
                // diff is empty and records no history.
                Require(session.PointerDown(-1, -1));
                Require(!session.PointerUp(-1, -1));
                Require(session.History.Current.UndoCount == 0);
                RequirePixels(layer, painted);

                // A single serpentine gesture erases the whole canvas to exact
                // zero and still commits as ONE undoable command.
                eraser.BrushSize = 1;
                Require(session.PointerDown(0, 0));
                for (int y = 0; y < layer.Height; y++)
                {
                    int edgeX = (y % 2 == 0) ? layer.Width - 1 : 0;
                    Require(session.PointerDrag(edgeX, y));
                    if (y + 1 < layer.Height) Require(session.PointerDrag(edgeX, y + 1));
                }
                Require(session.PointerUp(0, layer.Height - 1));
                for (int index = 0; index < layer.Pixels.Length; index++)
                    Require(layer.Pixels[index].Rgba == SharedConfig.TransparentRgba);
                Require(session.History.Current.UndoCount == 1);
                Require(session.Undo());
                RequirePixels(layer, painted);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Mirror symmetry applied by pencil, eraser, fill, and shape tools
    // ------------------------------------------------------------------
    private static void TestPixelToolsSymmetryMirrorModes()
    {
        // Pencil taps: Horizontal mirrors x -> W-1-x, Vertical mirrors
        // y -> H-1-y, Both produces all four copies.
        RequireExactPixels(
            PencilTapWithSymmetry(8, 6, 1, 2, SymmetryMode.None),
            new[] { (1, 2) });
        RequireExactPixels(
            PencilTapWithSymmetry(8, 6, 1, 2, SymmetryMode.Horizontal),
            new[] { (1, 2), (6, 2) });
        RequireExactPixels(
            PencilTapWithSymmetry(8, 6, 1, 2, SymmetryMode.Vertical),
            new[] { (1, 2), (1, 3) });
        RequireExactPixels(
            PencilTapWithSymmetry(8, 6, 1, 2, SymmetryMode.Both),
            new[] { (1, 2), (6, 2), (1, 3), (6, 3) });

        // The center pixel of an odd canvas maps onto itself: drawing it twice
        // is idempotent and produces exactly one pixel.
        RequireExactPixels(
            PencilTapWithSymmetry(5, 5, 2, 2, SymmetryMode.Both),
            new[] { (2, 2) });

        // A mirrored drag mirrors the whole segment.
        {
            var frame = new Frame(8, 6);
            var pencil = new PencilTool
            {
                CurrentColor = Color(10, 20, 30, 255),
                Symmetry = SymmetryMode.Horizontal
            };
            pencil.OnPointerDown(null, frame, 1, 1);
            pencil.OnPointerDrag(null, frame, 2, 1);
            pencil.OnPointerUp(null, frame, 2, 1);
            RequireExactPixels(frame.Layers[0], new[] { (1, 1), (2, 1), (5, 1), (6, 1) });
        }

        // Eraser symmetry: four mirrored corner taps erase all four corners of
        // a fully painted layer to the exact zero dword.
        {
            var frame = new Frame(8, 6);
            Layer layer = frame.Layers[0];
            for (int index = 0; index < layer.Pixels.Length; index++)
                layer.Pixels[index] = Color(200, 100, 50, 255);
            var eraser = new EraserTool { Symmetry = SymmetryMode.Both };
            eraser.OnPointerDown(null, frame, 0, 0);
            eraser.OnPointerUp(null, frame, 0, 0);
            int transparent = 0;
            for (int y = 0; y < layer.Height; y++)
            {
                for (int x = 0; x < layer.Width; x++)
                {
                    if (layer.GetPixel(x, y).Rgba == SharedConfig.TransparentRgba)
                    {
                        transparent++;
                        Require((x == 0 || x == 7) && (y == 0 || y == 5));
                    }
                }
            }
            Require(transparent == 4);
        }

        // Fill symmetry: an opaque wall at x=3 splits a 7x5 canvas; a single
        // mirrored fill seeds BOTH separated regions and leaves the wall alone.
        {
            var frame = new Frame(7, 5);
            Layer layer = frame.Layers[0];
            PixelColor wall = Color(1, 1, 1, 255);
            for (int y = 0; y < 5; y++) layer.SetPixel(3, y, wall);
            var fill = new FillTool
            {
                CurrentColor = Color(0, 200, 0, 255),
                Symmetry = SymmetryMode.Horizontal
            };
            fill.OnPointerDown(null, frame, 1, 2);
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 7; x++)
                {
                    uint expected = x == 3 ? wall.Rgba : Pack(0, 200, 0, 255);
                    Require(layer.GetPixel(x, y).Rgba == expected);
                }
            }
        }

        // Shape symmetry: a filled 2x2 rectangle under Both produces the four
        // quadrant copies (16 pixels total on an 8x8 canvas).
        {
            var frame = new Frame(8, 8);
            var rect = new RectangleTool
            {
                CurrentColor = Color(9, 9, 9, 255),
                Filled = true,
                Symmetry = SymmetryMode.Both
            };
            rect.OnPointerDown(null, frame, 1, 1);
            rect.OnPointerUp(null, frame, 2, 2);
            var expected = new List<(int, int)>();
            foreach (int startX in new[] { 1, 5 })
            {
                foreach (int startY in new[] { 1, 5 })
                {
                    for (int y = startY; y < startY + 2; y++)
                        for (int x = startX; x < startX + 2; x++)
                            expected.Add((x, y));
                }
            }
            RequireExactPixels(frame.Layers[0], expected.ToArray());
        }
    }

    // ------------------------------------------------------------------
    // Shape tools: live gesture preview, single-command commit, thickness
    // ------------------------------------------------------------------
    private static void TestPixelToolsShapeGesturePreviewAndCommit()
    {
        // Thickness clamps into [DefaultThickness, MaxThickness].
        var clampLine = new LineTool();
        clampLine.Thickness = 0;
        Require(clampLine.Thickness == Config.Tool.Shape.DefaultThickness);
        clampLine.Thickness = int.MaxValue;
        Require(clampLine.Thickness == Config.Tool.Shape.MaxThickness);

        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 1);
            using (DesignerSession session = DesignerSession.Create("ShapeTest", project, root))
            {
                session.AutosaveEnabled = false;
                Layer layer = session.CurrentFrame.Layers[0];
                var line = new LineTool { CurrentColor = Color(255, 0, 0, 255) };
                session.ActiveTool = line;

                // Preview: every drag restores the pre-gesture pixels and
                // re-rasterizes from the anchor, so strokes never accumulate.
                Require(session.PointerDown(1, 1));
                RequireExactPixels(layer, new[] { (1, 1) });
                Require(session.PointerDrag(4, 1));
                RequireExactPixels(layer, new[] { (1, 1), (2, 1), (3, 1), (4, 1) });
                Require(session.PointerDrag(1, 3));
                RequireExactPixels(layer, new[] { (1, 1), (1, 2), (1, 3) });
                Require(session.PointerUp(1, 3));

                // The whole preview interaction committed as ONE command.
                Require(session.History.Current.UndoCount == 1);
                Require(session.Undo());
                RequireExactPixels(layer, new (int, int)[0]);
                Require(session.Redo());
                RequireExactPixels(layer, new[] { (1, 1), (1, 2), (1, 3) });
                Require(session.Undo());

                // Rectangle outline: the border band of the anchor box; the
                // pointer coordinates clamp onto the canvas.
                var rect = new RectangleTool { CurrentColor = Color(0, 255, 0, 255) };
                session.ActiveTool = rect;
                Require(session.PointerDown(1, 1));
                Require(session.PointerDrag(100, 100));
                Require(session.PointerUp(100, 100));
                Require(session.History.Current.UndoCount == 1);
                var border = new List<(int, int)>();
                for (int y = 1; y <= 5; y++)
                {
                    for (int x = 1; x <= 7; x++)
                    {
                        if (x == 1 || x == 7 || y == 1 || y == 5) border.Add((x, y));
                    }
                }
                RequireExactPixels(layer, border.ToArray());
                Require(session.Undo());

                // Filled rectangle covers the whole box.
                rect.Filled = true;
                Require(session.PointerDown(2, 2));
                Require(session.PointerDrag(5, 4));
                Require(session.PointerUp(5, 4));
                var full = new List<(int, int)>();
                for (int y = 2; y <= 4; y++)
                    for (int x = 2; x <= 5; x++) full.Add((x, y));
                RequireExactPixels(layer, full.ToArray());
                Require(session.Undo());
                Require(session.History.Current.UndoCount == 0);
            }

            // Thick outline band: 7x7 box with thickness 2 leaves a 3x3 hole.
            {
                var frame = new Frame(7, 7);
                var rect = new RectangleTool
                {
                    CurrentColor = Color(3, 3, 3, 255),
                    Thickness = 2
                };
                rect.OnPointerDown(null, frame, 0, 0);
                rect.OnPointerUp(null, frame, 6, 6);
                Layer layer = frame.Layers[0];
                for (int y = 0; y < 7; y++)
                {
                    for (int x = 0; x < 7; x++)
                    {
                        bool hole = x >= 2 && x <= 4 && y >= 2 && y <= 4;
                        uint expected = hole ? SharedConfig.TransparentRgba : Pack(3, 3, 3, 255);
                        Require(layer.GetPixel(x, y).Rgba == expected);
                    }
                }
            }

            // Ellipse: the filled ellipse inscribed in the full 9x7 canvas
            // covers the full center row/column and leaves the corners empty;
            // the 1-pixel outline is a subset that keeps the rim.
            {
                var frame = new Frame(9, 7);
                var ellipse = new EllipseTool
                {
                    CurrentColor = Color(8, 8, 8, 255),
                    Filled = true
                };
                ellipse.OnPointerDown(null, frame, 0, 0);
                ellipse.OnPointerUp(null, frame, 8, 6);
                Layer filled = frame.Layers[0];
                for (int x = 0; x < 9; x++) Require(!filled.GetPixel(x, 3).IsTransparent);
                for (int y = 0; y < 7; y++) Require(!filled.GetPixel(4, y).IsTransparent);
                Require(filled.GetPixel(0, 0).IsTransparent);
                Require(filled.GetPixel(8, 0).IsTransparent);
                Require(filled.GetPixel(0, 6).IsTransparent);
                Require(filled.GetPixel(8, 6).IsTransparent);

                var outlineFrame = new Frame(9, 7);
                var outline = new EllipseTool { CurrentColor = Color(8, 8, 8, 255) };
                outline.OnPointerDown(null, outlineFrame, 0, 0);
                outline.OnPointerUp(null, outlineFrame, 8, 6);
                Layer ring = outlineFrame.Layers[0];
                Require(ring.GetPixel(4, 3).IsTransparent);
                Require(!ring.GetPixel(0, 3).IsTransparent);
                Require(!ring.GetPixel(8, 3).IsTransparent);
                Require(!ring.GetPixel(4, 0).IsTransparent);
                Require(!ring.GetPixel(4, 6).IsTransparent);
                for (int y = 0; y < 7; y++)
                {
                    for (int x = 0; x < 9; x++)
                    {
                        if (!ring.GetPixel(x, y).IsTransparent)
                            Require(!filled.GetPixel(x, y).IsTransparent);
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
    // SelectionRegion geometry factories (rect clamp + lasso even-odd)
    // ------------------------------------------------------------------
    private static void TestPixelToolsSelectionRegionGeometry()
    {
        // Rectangle: extreme anchor coordinates clamp onto the canvas without
        // overflowing, in any corner order.
        SelectionRegion full = SelectionRegion.FromRectangle(
            6, 5, int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
        Require(full != null);
        Require(full.X == 0 && full.Y == 0 && full.Width == 6 && full.Height == 5);
        Require(full.SelectedCount == 30);
        SelectionRegion swapped = SelectionRegion.FromRectangle(6, 5, 4, 3, 1, 1);
        Require(swapped.X == 1 && swapped.Y == 1 && swapped.Width == 4 && swapped.Height == 3);
        Require(swapped.SelectedCount == 12);
        Require(swapped.Contains(1, 1) && swapped.Contains(4, 3));
        Require(!swapped.Contains(0, 1) && !swapped.Contains(5, 3) && !swapped.Contains(1, 0));

        // Entirely off-canvas or zero-sized canvases select nothing.
        Require(SelectionRegion.FromRectangle(6, 5, -10, -10, -2, -2) == null);
        Require(SelectionRegion.FromRectangle(6, 5, 6, 0, 9, 4) == null);
        Require(SelectionRegion.FromRectangle(0, 5, 0, 0, 3, 3) == null);

        // Lasso triangle (0,0)-(6,0)-(0,6): a pixel center (x+.5, y+.5) is
        // inside exactly when x+y < 5, giving the 15-pixel staircase.
        SelectionRegion triangle = SelectionRegion.FromPolygon(
            7, 7, new[] { 0, 6, 0 }, new[] { 0, 0, 6 });
        Require(triangle != null && triangle.SelectedCount == 15);
        for (int y = 0; y < 7; y++)
        {
            for (int x = 0; x < 7; x++)
                Require(triangle.Contains(x, y) == (x + y < 5));
        }

        // Randomized polygons against an independent even-odd reference model.
        var random = new Random(0x53E1EC7);
        for (int iteration = 0; iteration < 40; iteration++)
        {
            int pointCount = 3 + random.Next(6);
            var xs = new int[pointCount];
            var ys = new int[pointCount];
            for (int index = 0; index < pointCount; index++)
            {
                xs[index] = random.Next(-3, 12);
                ys[index] = random.Next(-3, 11);
            }

            SelectionRegion region = SelectionRegion.FromPolygon(9, 8, xs, ys);
            int expectedCount = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 9; x++)
                {
                    bool inside = ReferenceEvenOddInside(xs, ys, x + 0.5, y + 0.5);
                    if (inside) expectedCount++;
                    Require((region != null && region.Contains(x, y)) == inside);
                }
            }
            Require((region == null ? 0 : region.SelectedCount) == expectedCount);
        }

        // Degenerate and adversarial polygon inputs.
        Require(SelectionRegion.FromPolygon(9, 8, new[] { 1, 3, 5 }, new[] { 1, 1, 1 }) == null);
        Require(SelectionRegion.FromPolygon(9, 8, new[] { 1, 3 }, new[] { 1, 1 }) == null);
        RequireThrows<ArgumentNullException>(
            () => SelectionRegion.FromPolygon(9, 8, null, new[] { 1 }));
        RequireThrows<ArgumentNullException>(
            () => SelectionRegion.FromPolygon(9, 8, new[] { 1 }, null));
        RequireThrows<ArgumentException>(
            () => SelectionRegion.FromPolygon(9, 8, new[] { 1, 2, 3 }, new[] { 1, 2 }));
        var tooManyX = new int[Config.Tool.Selection.MaxLassoPoints + 1];
        var tooManyY = new int[Config.Tool.Selection.MaxLassoPoints + 1];
        RequireThrows<ArgumentOutOfRangeException>(
            () => SelectionRegion.FromPolygon(9, 8, tooManyX, tooManyY));

        // The lasso tool caps its recorded points and drops consecutive
        // duplicates, and the selection tools report a completed region
        // exactly once.
        var lasso = new LassoSelectTool();
        var lassoFrame = new Frame(9, 8);
        lasso.OnPointerDown(null, lassoFrame, 0, 0);
        for (int repeat = 0; repeat < 5; repeat++) lasso.OnPointerDrag(null, lassoFrame, 6, 0);
        lasso.OnPointerUp(null, lassoFrame, 0, 6);
        SelectionRegion lassoRegion = lasso.TakeCompletedRegion();
        Require(lassoRegion != null && lassoRegion.SelectedCount == 15);
        Require(lasso.TakeCompletedRegion() == null);
    }

    // ------------------------------------------------------------------
    // Session floating buffer: lift, move, anchor as one command
    // ------------------------------------------------------------------
    private static void TestPixelToolsSelectionLiftMoveAnchorHistory()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 1);
            using (DesignerSession session = DesignerSession.Create("FloatTest", project, root))
            {
                session.AutosaveEnabled = false;
                Layer layer = session.CurrentFrame.Layers[0];
                for (int y = 1; y <= 2; y++)
                    for (int x = 1; x <= 3; x++)
                        layer.SetPixel(x, y, Color((byte)(x * 50), (byte)(y * 90), 5, 255));
                uint[] original = SnapshotPixels(layer);

                // A selection gesture defines the region without touching
                // pixels or history; PointerUp reports "no pixel changes".
                var rectSelect = new RectSelectTool();
                session.ActiveTool = rectSelect;
                Require(session.PointerDown(1, 1));
                Require(session.PointerDrag(2, 2));
                Require(!session.PointerUp(3, 2));
                Require(session.History.Current.UndoCount == 0);
                Require(session.Selection != null && session.Selection.SelectedCount == 6);
                Require(session.Floating == null);
                RequirePixels(layer, original);

                // Dragging from inside the selection lifts it (cut) and moves
                // it; pointer-up keeps the buffer hovering.
                Require(session.PointerDown(2, 1));
                Require(session.Floating != null && session.Selection == null);
                Require(session.Floating.OffsetX == 1 && session.Floating.OffsetY == 1);
                for (int y = 1; y <= 2; y++)
                    for (int x = 1; x <= 3; x++)
                        Require(layer.GetPixel(x, y).Rgba == SharedConfig.TransparentRgba);
                Require(session.PointerDrag(4, 3));
                Require(session.Floating.OffsetX == 3 && session.Floating.OffsetY == 3);
                Require(session.PointerUp(4, 3));
                Require(session.Floating != null);
                Require(session.History.Current.UndoCount == 0);

                // The viewport composites the hovering buffer over the frame
                // without mutating the layer.
                var viewport = new ViewportController();
                var screen = new uint[8 * 6];
                viewport.RenderToScreenBuffer(session.CurrentFrame, session.Floating, screen, 8, 6);
                Require(screen[3 * 8 + 3] == original[1 * 8 + 1]);
                Require(screen[1 * 8 + 1] == SharedConfig.TransparentRgba);
                Require(layer.GetPixel(3, 3).Rgba == SharedConfig.TransparentRgba);

                // Clicking outside the buffer anchors the whole lift-move
                // interaction as exactly ONE undoable command.
                Require(session.PointerDown(7, 0));
                Require(session.Floating == null);
                Require(session.History.Current.UndoCount == 1);
                var moved = new uint[original.Length];
                for (int y = 1; y <= 2; y++)
                    for (int x = 1; x <= 3; x++)
                        moved[(y + 2) * 8 + (x + 2)] = original[y * 8 + x];
                RequirePixels(layer, moved);

                Require(session.Undo());
                RequirePixels(layer, original);
                Require(session.Redo());
                RequirePixels(layer, moved);
                Require(session.Undo());
                RequirePixels(layer, original);

                // Esc cancels a lift: the cut pixels return, nothing recorded.
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(3, 2));
                Require(session.PointerDown(2, 2));
                Require(session.Floating != null);
                Require(session.MoveFloating(2, 0));
                session.CancelGesture();
                Require(session.Floating == null);
                RequirePixels(layer, original);
                Require(session.History.Current.UndoCount == 0);

                // Copy-lift (removeSource: false) leaves the source intact and
                // anchors a duplicate elsewhere.
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(3, 2));
                Require(session.LiftSelection(0, false));
                RequirePixels(layer, original);
                Require(session.MoveFloating(3, 2));
                Require(session.AnchorFloating());
                Require(session.History.Current.UndoCount == 1);
                var duplicated = (uint[])original.Clone();
                for (int y = 1; y <= 2; y++)
                    for (int x = 1; x <= 3; x++)
                        duplicated[(y + 2) * 8 + (x + 3)] = original[y * 8 + x];
                RequirePixels(layer, duplicated);
                Require(session.Undo());
                RequirePixels(layer, original);

                // Moving a floating buffer partially or fully off-canvas is
                // legal; off-canvas pixels are simply not committed.
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(3, 2));
                Require(session.LiftSelection(0, true));
                Require(session.MoveFloating(-2, -1));
                Require(session.AnchorFloating());
                var shifted = new uint[original.Length];
                for (int y = 1; y <= 2; y++)
                {
                    for (int x = 1; x <= 3; x++)
                    {
                        int destX = x - 2;
                        int destY = y - 1;
                        if (destX >= 0 && destY >= 0)
                            shifted[destY * 8 + destX] = original[y * 8 + x];
                    }
                }
                RequirePixels(layer, shifted);
                Require(session.Undo());
                RequirePixels(layer, original);

                // Guards: no lift without a selection, no copy of a bad layer.
                Require(!session.LiftSelection(0, true));
                Require(!session.CopySelection(0));
                Require(!session.MoveFloating(1, 1));
                Require(!session.AnchorFloating());
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Clipboard reuse/isolation + transient state dropped by other edits
    // ------------------------------------------------------------------
    private static void TestPixelToolsClipboardAndTransientDrops()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 8, 6, 2);
            using (DesignerSession session = DesignerSession.Create("ClipTest", project, root))
            {
                session.AutosaveEnabled = false;
                Layer layer = session.CurrentFrame.Layers[0];
                for (int y = 1; y <= 2; y++)
                    for (int x = 1; x <= 2; x++)
                        layer.SetPixel(x, y, Color((byte)(x * 60), (byte)(y * 70), 9, 255));
                uint[] original = SnapshotPixels(layer);

                var rectSelect = new RectSelectTool();
                session.ActiveTool = rectSelect;
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(2, 2));

                // Copy mutates nothing and keeps the selection alive.
                Require(!session.HasClipboard);
                Require(session.CopySelection(0));
                Require(session.HasClipboard);
                Require(session.Selection != null);
                RequirePixels(layer, original);

                // Later source edits do not retro-edit the clipboard.
                layer.SetPixel(1, 1, Color(255, 255, 255, 255));
                uint[] scribbled = SnapshotPixels(layer);
                session.ClearSelection();
                Require(session.Selection == null);

                // Paste floats a clone at the copied location; anchoring
                // elsewhere writes the ORIGINAL copied bytes.
                Require(session.PasteClipboard(0));
                Require(session.Floating != null);
                Require(session.Floating.OffsetX == 1 && session.Floating.OffsetY == 1);
                Require(session.MoveFloating(4, 2));
                Require(session.AnchorFloating());
                Require(session.History.Current.UndoCount == 1);
                var pasted = (uint[])scribbled.Clone();
                for (int y = 1; y <= 2; y++)
                    for (int x = 1; x <= 2; x++)
                        pasted[(y + 2) * 8 + (x + 4)] = original[y * 8 + x];
                RequirePixels(layer, pasted);

                // The clipboard is reusable: a second paste floats the same
                // original bytes again (clone isolation from the first paste).
                Require(session.HasClipboard);
                Require(session.PasteClipboard(0));
                Require(session.Floating.Pixels[0] == original[1 * 8 + 1]);
                session.CancelFloating();
                RequirePixels(layer, pasted);
                Require(session.Undo());
                RequirePixels(layer, scribbled);

                // Structural session mutations CANCEL an un-anchored lift:
                // the cut pixels come back before the structure changes.
                Require(session.PointerDown(4, 3));
                Require(!session.PointerUp(6, 4));
                Require(session.LiftSelection(0, true));
                Require(session.Floating != null);
                session.AddFrame();
                Require(session.Floating == null);
                RequirePixels(layer, scribbled);
                Require(session.Undo());

                // Selecting another frame clears the pending region.
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(2, 2));
                Require(session.Selection != null);
                Require(session.SelectFrame(0, 1));
                Require(session.Selection == null);
                Require(session.SelectFrame(0, 0));

                // A property-style layer edit also drops the lift first, then
                // applies and records only its own change.
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(2, 2));
                Require(session.LiftSelection(0, true));
                int undoBefore = session.History.Current.UndoCount;
                session.SetLayerOpacity(0, 0.5f);
                Require(session.Floating == null);
                Require(layer.Opacity == 0.5f);
                RequirePixels(layer, scribbled);
                Require(session.History.Current.UndoCount == undoBefore + 1);
                Require(session.Undo());
                Require(layer.Opacity == 1f);
                RequirePixels(layer, scribbled);

                // Undo/redo themselves drop any pending lift safely.
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(2, 2));
                Require(session.LiftSelection(0, true));
                session.Redo();
                Require(session.Floating == null);
                RequirePixels(layer, scribbled);
                session.Undo();
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // ResizeCanvas: 9-anchor content placement reference model
    // ------------------------------------------------------------------
    private static void TestPixelToolsResizeCanvasAnchorModel()
    {
        var anchors = new[]
        {
            CanvasAnchor.TopLeft, CanvasAnchor.TopCenter, CanvasAnchor.TopRight,
            CanvasAnchor.MiddleLeft, CanvasAnchor.MiddleCenter, CanvasAnchor.MiddleRight,
            CanvasAnchor.BottomLeft, CanvasAnchor.BottomCenter, CanvasAnchor.BottomRight
        };
        var targets = new[] { (7, 6), (2, 2), (5, 2), (4, 3) };

        foreach ((int newWidth, int newHeight) in targets)
        {
            foreach (CanvasAnchor anchor in anchors)
            {
                string root = NewTemporaryDirectory();
                try
                {
                    const int oldWidth = 4;
                    const int oldHeight = 3;
                    EFYVProject project = CreatePixelToolsProject(root, oldWidth, oldHeight, 2);
                    var second = new AnimationState("Walk", 6);
                    second.Frames.Add(new Frame(oldWidth, oldHeight, 0));
                    project.Animations.Add(second);

                    // Unique opaque pixel per coordinate, in every frame of
                    // every animation, plus layer metadata to preserve.
                    var sourceLayers = new List<Layer>();
                    foreach (AnimationState animation in project.Animations)
                    {
                        foreach (Frame frame in animation.Frames)
                        {
                            Layer layer = frame.Layers[0];
                            for (int y = 0; y < oldHeight; y++)
                                for (int x = 0; x < oldWidth; x++)
                                    layer.SetPixel(x, y, Color((byte)(x * 40 + 1), (byte)(y * 40 + 1), 7, 255));
                            layer.Name = "Content";
                            layer.Opacity = 0.25f;
                            layer.IsVisible = false;
                            sourceLayers.Add(layer);
                        }
                    }
                    Frame hitboxFrame = project.Animations[0].Frames[0];
                    hitboxFrame.Hitboxes[Config.Hitbox.DefaultKeyHurtbox] = new HitboxData
                    {
                        X = 0.125f,
                        Y = 0.0625f,
                        Width = 0.125f,
                        Height = 0.0625f
                    };

                    using (DesignerSession session = DesignerSession.Create("ResizeTest", project, root))
                    {
                        session.AutosaveEnabled = false;
                        session.ResizeCanvas(newWidth, newHeight, anchor);

                        Require(project.CanvasWidth == newWidth && project.CanvasHeight == newHeight);
                        int shiftX = AnchorShift(HorizontalAlignmentOf(anchor), oldWidth, newWidth);
                        int shiftY = AnchorShift(VerticalAlignmentOf(anchor), oldHeight, newHeight);

                        int layerCursor = 0;
                        foreach (AnimationState animation in project.Animations)
                        {
                            foreach (Frame frame in animation.Frames)
                            {
                                Require(frame.Width == newWidth && frame.Height == newHeight);
                                Layer resized = frame.Layers[0];
                                Layer source = sourceLayers[layerCursor++];
                                Require(resized.Name == "Content");
                                Require(resized.Opacity == 0.25f);
                                Require(!resized.IsVisible);
                                var expected = new uint[newWidth * newHeight];
                                for (int y = 0; y < oldHeight; y++)
                                {
                                    for (int x = 0; x < oldWidth; x++)
                                    {
                                        int destX = x + shiftX;
                                        int destY = y + shiftY;
                                        if (destX >= 0 && destX < newWidth && destY >= 0 && destY < newHeight)
                                            expected[destY * newWidth + destX] = source.GetPixel(x, y).Rgba;
                                    }
                                }
                                RequirePixels(resized, expected);
                            }
                        }

                        // Hitboxes translate with the content and clamp onto
                        // the new canvas in world units.
                        float unitsPerPixel = 1f / Config.Hitbox.PixelsPerUnit;
                        float maxX = newWidth * unitsPerPixel;
                        float maxY = newHeight * unitsPerPixel;
                        float left = Math.Clamp(0.125f + shiftX * unitsPerPixel, 0f, maxX);
                        float right = Math.Clamp(0.25f + shiftX * unitsPerPixel, left, maxX);
                        float top = Math.Clamp(0.0625f + shiftY * unitsPerPixel, 0f, maxY);
                        float bottom = Math.Clamp(0.125f + shiftY * unitsPerPixel, top, maxY);
                        HitboxData resizedBox =
                            project.Animations[0].Frames[0].Hitboxes[Config.Hitbox.DefaultKeyHurtbox];
                        Require(resizedBox.X == left && resizedBox.Width == right - left);
                        Require(resizedBox.Y == top && resizedBox.Height == bottom - top);

                        // The snapshot layer (used by preview/export) reflects
                        // the new geometry immediately.
                        ProjectSnapshot snapshot = ProjectSnapshot.Capture(project);
                        Require(snapshot.CanvasWidth == newWidth && snapshot.CanvasHeight == newHeight);
                    }
                }
                finally
                {
                    DeleteDirectory(root);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // ResizeCanvas: single-command history, guards, transient drops,
    // and the persisted document round trip
    // ------------------------------------------------------------------
    private static void TestPixelToolsResizeCanvasGuardsAndHistory()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreatePixelToolsProject(root, 4, 3, 2);
            using (DesignerSession session = DesignerSession.Create("ResizeHist", project, root))
            {
                session.AutosaveEnabled = false;
                Layer layer = session.CurrentFrame.Layers[0];
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 4; x++)
                        layer.SetPixel(x, y, Color((byte)(x + 1), (byte)(y + 1), 3, 255));
                uint[] original = SnapshotPixels(layer);
                var originalFrames = new List<Frame>(project.Animations[0].Frames);

                // The whole multi-frame rebuild is ONE undoable command, and
                // undo restores the ORIGINAL frame objects untouched.
                session.ResizeCanvas(6, 5, CanvasAnchor.MiddleCenter);
                Require(session.History.Current.UndoCount == 1);
                Require(session.CurrentFrame.Width == 6 && session.CurrentFrame.Height == 5);
                Require(session.Undo());
                Require(project.CanvasWidth == 4 && project.CanvasHeight == 3);
                for (int index = 0; index < originalFrames.Count; index++)
                    Require(ReferenceEquals(project.Animations[0].Frames[index], originalFrames[index]));
                RequirePixels(project.Animations[0].Frames[0].Layers[0], original);
                Require(session.Redo());
                Require(project.CanvasWidth == 6 && project.CanvasHeight == 5);
                Require(session.CurrentFrame.Width == 6);
                Require(session.Undo());

                // A no-op resize records nothing.
                session.ResizeCanvas(4, 3, CanvasAnchor.TopLeft);
                Require(session.History.Current.UndoCount == 0);

                // Dimension guards.
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ResizeCanvas(0, 3, CanvasAnchor.TopLeft));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ResizeCanvas(4, 0, CanvasAnchor.TopLeft));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ResizeCanvas(Config.Persistence.MaxCanvasDimension + 1, 3, CanvasAnchor.TopLeft));
                RequireThrows<ArgumentOutOfRangeException>(
                    () => session.ResizeCanvas(4, Config.Persistence.MaxCanvasDimension + 1, CanvasAnchor.TopLeft));

                // Resizing mid-gesture is refused outright.
                var pencil = new PencilTool { CurrentColor = Color(9, 9, 9, 255) };
                session.ActiveTool = pencil;
                Require(session.PointerDown(0, 0));
                RequireThrows<InvalidOperationException>(
                    () => session.ResizeCanvas(6, 5, CanvasAnchor.TopLeft));
                session.CancelGesture();
                RequirePixels(project.Animations[0].Frames[0].Layers[0], original);

                // A pending lift is canceled (pixels restored) BEFORE the
                // rebuild, so the resized frames carry the pre-lift content.
                var rectSelect = new RectSelectTool();
                session.ActiveTool = rectSelect;
                Require(session.PointerDown(1, 1));
                Require(!session.PointerUp(2, 2));
                Require(session.LiftSelection(0, true));
                session.ResizeCanvas(6, 5, CanvasAnchor.TopLeft);
                Require(session.Floating == null && session.Selection == null);
                Layer resizedLayer = project.Animations[0].Frames[0].Layers[0];
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 4; x++)
                        Require(resizedLayer.GetPixel(x, y).Rgba == original[y * 4 + x]);

                // The resized project round-trips through the persistence
                // document with the new canvas and anchored pixels intact.
                var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
                persistence.SaveProject("ResizedRoundTrip", project, CancellationToken.None);
                EFYVProject restored = persistence.LoadProject("ResizedRoundTrip");
                Require(restored.CanvasWidth == 6 && restored.CanvasHeight == 5);
                Frame restoredFrame = restored.Animations[0].Frames[0];
                Require(restoredFrame.Width == 6 && restoredFrame.Height == 5);
                RequirePixels(restoredFrame.Layers[0], SnapshotPixels(resizedLayer));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Shared fixtures and reference helpers
    // ------------------------------------------------------------------
    private static EFYVProject CreatePixelToolsProject(
        string unityRoot,
        int width,
        int height,
        int frameCount)
    {
        var schema = new AssetSchemaService();
        var toolbar = new ToolbarAPI(schema);
        EFYVProject project = toolbar.CreateNewProject(
            SharedConfig.EnemyDisplayName + Config.Entity.SuffixDown);
        project.UnityProjectPath = unityRoot;
        project.AssetProperties[SharedConfig.EntityNameField] = "PixelToolsEnemy";
        project.CanvasWidth = width;
        project.CanvasHeight = height;
        var animation = new AnimationState("Idle", 4);
        for (int index = 0; index < frameCount; index++)
            animation.Frames.Add(new Frame(width, height, index));
        project.Animations.Add(animation);
        return project;
    }

    private static Layer PencilTapWithSymmetry(
        int width,
        int height,
        int x,
        int y,
        SymmetryMode mode)
    {
        var frame = new Frame(width, height);
        var pencil = new PencilTool
        {
            CurrentColor = Color(10, 20, 30, 255),
            Symmetry = mode
        };
        pencil.OnPointerDown(null, frame, x, y);
        pencil.OnPointerUp(null, frame, x, y);
        return frame.Layers[0];
    }

    private static uint[] SnapshotPixels(Layer layer)
    {
        var pixels = new uint[layer.Pixels.Length];
        for (int index = 0; index < pixels.Length; index++)
            pixels[index] = layer.Pixels[index].Rgba;
        return pixels;
    }

    private static void RequirePixels(Layer layer, uint[] expected)
    {
        Require(layer.Pixels.Length == expected.Length);
        for (int index = 0; index < expected.Length; index++)
            Require(layer.Pixels[index].Rgba == expected[index]);
    }

    private static void RequireExactPixels(Layer layer, (int X, int Y)[] expected)
    {
        var set = new HashSet<(int, int)>(expected);
        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
                Require(!layer.GetPixel(x, y).IsTransparent == set.Contains((x, y)));
        }
    }

    // Independent even-odd (crossing) point-in-polygon reference, written
    // against the classic ray-cast formulation rather than the production
    // helper's loop shape.
    private static bool ReferenceEvenOddInside(int[] xs, int[] ys, double probeX, double probeY)
    {
        bool inside = false;
        for (int current = 0; current < xs.Length; current++)
        {
            int previous = (current + xs.Length - 1) % xs.Length;
            double x0 = xs[previous];
            double y0 = ys[previous];
            double x1 = xs[current];
            double y1 = ys[current];
            if ((y1 > probeY) == (y0 > probeY)) continue;
            double intersectX = x1 + (probeY - y1) * (x0 - x1) / (y0 - y1);
            if (probeX < intersectX) inside = !inside;
        }
        return inside;
    }

    private static int HorizontalAlignmentOf(CanvasAnchor anchor)
    {
        switch (anchor)
        {
            case CanvasAnchor.TopLeft:
            case CanvasAnchor.MiddleLeft:
            case CanvasAnchor.BottomLeft:
                return 0;
            case CanvasAnchor.TopCenter:
            case CanvasAnchor.MiddleCenter:
            case CanvasAnchor.BottomCenter:
                return 1;
            default:
                return 2;
        }
    }

    private static int VerticalAlignmentOf(CanvasAnchor anchor)
    {
        switch (anchor)
        {
            case CanvasAnchor.TopLeft:
            case CanvasAnchor.TopCenter:
            case CanvasAnchor.TopRight:
                return 0;
            case CanvasAnchor.MiddleLeft:
            case CanvasAnchor.MiddleCenter:
            case CanvasAnchor.MiddleRight:
                return 1;
            default:
                return 2;
        }
    }

    private static int AnchorShift(int alignment, int oldSize, int newSize)
    {
        if (alignment == 0) return 0;
        if (alignment == 1) return (newSize - oldSize) / 2;
        return newSize - oldSize;
    }
}
