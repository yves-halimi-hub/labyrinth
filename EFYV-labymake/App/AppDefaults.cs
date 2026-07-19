using System;
using System.IO;

namespace EFYVLabyMake.App
{
    // Shell-local defaults. Wire-format and canvas limits come from the shared
    // EFYVLabyrinthConfig; everything here is presentation-only. If any value ever
    // needs to be shared with Core or the game it must move into the shared config
    // (owned by the pipeline-contract work in this batch - see the deferred notes).
    public static class AppDefaults
    {
        public const string WindowTitle = "EFYV LabyMake";
        public const double WindowWidth = 1280;
        public const double WindowHeight = 800;

        public const string ToolPencil = "Pencil";
        public const string ToolEraser = "Eraser";
        public const string ToolEyedropper = "Eyedropper";
        public const string ToolFill = "Fill";
        public const string ToolLine = "Line";
        public const string ToolRect = "Rect";
        public const string ToolEllipse = "Ellipse";
        public const string ToolSelectRect = "Select";
        public const string ToolSelectLasso = "Lasso";
        public const string ToolStamp = "Stamp";
        public const string ToolTileMaker = "TileMaker";
        public const string ToolHitbox = "Hitbox";
        public const string ToolMoving = "Moving";

        // Straight RGBA with red in the low byte, matching PixelColor.
        public const uint DefaultColorRgba = 0xFF000000u;
        public static readonly uint[] SwatchesRgba =
        {
            0xFF000000u, // black
            0xFFFFFFFFu, // white
            0xFF0000FFu, // red
            0xFF00FF00u, // green
            0xFFFF0000u, // blue
            0xFF00FFFFu, // yellow
            0xFFFF00FFu, // magenta
            0xFFFFFF00u  // cyan
        };

        // Outside-canvas workspace color, a BGRA screen value (an opaque gray,
        // so channel order does not actually matter). The checkerboard
        // transparency backdrop moved into core with item #31
        // (EFYVLabyrinthConfig.LabyMake.Overlay + the ViewportController
        // Checkerboard overlay pass) so every host renders the same backdrop.
        public const uint WorkspaceBgra = 0xFF202020u;

        public const int ErrorFlashMilliseconds = 4000;
        public const int NoticeFlashMilliseconds = 3000;
        public const string InitialAnimationName = "Idle";

        // Editor panel set (item #3): default names generated for new
        // layers/animations/palettes, the left dock panel width, and the
        // preview player's UI-timer interval (~60 fps ticks; the core preview
        // controller applies the actual per-frame durations).
        public const string LayerNamePrefix = "Layer ";
        public const string NewAnimationName = "NewAnim";
        public const string NewPaletteName = "Palette";
        public const double PanelDockWidth = 264;
        public const double PanelListHeight = 220;
        public const double PreviewBoxSize = 200;
        public const int PreviewTimerMilliseconds = 16;
        public const int PreviewReloadDebounceMilliseconds = 250;

        // Asset bank panel (item #6): presentation-only sizing for the
        // sub-element thumbnail list docked at the window's right edge.
        public const double BankPanelWidth = 210;
        public const double BankThumbnailBoxSize = 48;

        // Timeline strip (item #10): presentation-only sizing.
        public const double TimelineMaxFramesWidth = 640;
        public const int FrameDurationIncrementMs = 10;

        // Map/tileset panel (item #5): presentation-only sizing plus the
        // shell-generated default tile name prefix and the map-size input
        // range (the hard cap lives in the shared config; the input range is
        // sized for hand-authored maps).
        public const double MapPanelWidth = 220;
        public const string TileNamePrefix = "Tile";
        public const int MinMapDimensionInput = 1;
        public const int DefaultMapDimensionInput = 32;
        public const int MaxMapDimensionInput = 256;

        public const int MinCanvasInput = 1;
        public const int DefaultBrushSizeInput = 1;
        public const int MaxBrushSizeInput = 64;

        public static string DefaultProjectDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "EFYVLabyMake",
                "Projects");
        }
    }
}
