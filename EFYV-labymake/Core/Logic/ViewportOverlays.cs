using System;
using System.Collections.Generic;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    // Item #31 viewport designer overlays: the optional, host-agnostic overlay
    // passes ViewportController composites into the screen buffer after the
    // canvas blit. The overlay set is a flags enum plus one small config
    // struct per overlay, bundled in a host-owned ViewportOverlaySettings the
    // host reuses across renders (the render path itself is zero-alloc in
    // steady state - no per-call allocations happen in any pass).
    //
    // Pass order (fixed): Checkerboard (under the content, inside the canvas
    // area only) -> PixelGrid -> TileGrid -> Hitboxes -> AttachmentOutlines ->
    // PivotMarkers. Later passes blend over earlier ones, so at a coincident
    // pixel/tile boundary the tile line reads on top.
    [Flags]
    public enum ViewportOverlayKind
    {
        None = 0,
        Checkerboard = 1,
        PixelGrid = 2,
        TileGrid = 4,
        Hitboxes = 8,
        AttachmentOutlines = 16,
        PivotMarkers = 32
    }

    // Checkerboard transparency backdrop: screen-anchored square cells of
    // edge 1 << CellShift (panning moves the art over a fixed backdrop, the
    // exact behavior the Avalonia shell had before this moved into core).
    // The canvas content alpha-blends over the cells, so pixels inside the
    // canvas area come out opaque when the cell colors are opaque.
    public struct CheckerboardOverlayConfig
    {
        public uint LightRgba;
        public uint DarkRgba;
        public int CellShift;

        public static CheckerboardOverlayConfig CreateDefault()
        {
            return new CheckerboardOverlayConfig
            {
                LightRgba = Config.Overlay.CheckerLightRgba,
                DarkRgba = Config.Overlay.CheckerDarkRgba,
                CellShift = Config.Overlay.DefaultCheckerCellShift
            };
        }
    }

    // Pixel grid: 1-screen-pixel lines wherever the nearest-neighbor source
    // column/row changes (the exact same float mapping the blit uses, so
    // lines can never drift off the scaled pixel edges). Draws nothing while
    // ZoomLevel < MinZoom.
    public struct PixelGridOverlayConfig
    {
        public uint LineRgba;
        public float MinZoom;

        public static PixelGridOverlayConfig CreateDefault()
        {
            return new PixelGridOverlayConfig
            {
                LineRgba = Config.Overlay.PixelGridLineRgba,
                MinZoom = Config.Overlay.DefaultPixelGridMinZoom
            };
        }
    }

    // Tile grid: TileSize-cell boundaries. TileSize is per-render CONTEXT the
    // host stamps before rendering (the tileset's TileSize, or the map cell
    // size in map mode); Config.Overlay.InactiveTileSize (0) means no
    // tileset/map context is active and the pass draws nothing even when its
    // flag is set.
    public struct TileGridOverlayConfig
    {
        public uint LineRgba;
        public int TileSize;

        public static TileGridOverlayConfig CreateDefault()
        {
            return new TileGridOverlayConfig
            {
                LineRgba = Config.Overlay.TileGridLineRgba,
                TileSize = Config.Overlay.InactiveTileSize
            };
        }
    }

    // Hitbox rectangles: every Frame.Hitboxes entry scaled by PixelsPerUnit
    // into canvas pixels and outlined in a deterministic per-key color
    // (ViewportController.GetHitboxKeyColor). Boxes with non-positive or
    // non-finite extents are skipped (default(HitboxData) is a zero box).
    public struct HitboxOverlayConfig
    {
        public float PixelsPerUnit;

        public static HitboxOverlayConfig CreateDefault()
        {
            return new HitboxOverlayConfig
            {
                PixelsPerUnit = Config.Hitbox.PixelsPerUnit
            };
        }
    }

    // Attachment outlines: the placed bounds of every frame attachment whose
    // sub-element resolves out of ViewportOverlaySettings.AttachmentSources,
    // using the exact pivot/flip placement math the export flatten uses
    // (ExportEngine.CompositeAttachment). Unresolved names draw no outline.
    public struct AttachmentOverlayConfig
    {
        public uint OutlineRgba;

        public static AttachmentOverlayConfig CreateDefault()
        {
            return new AttachmentOverlayConfig
            {
                OutlineRgba = Config.Overlay.AttachmentOutlineRgba
            };
        }
    }

    // Pivot markers: a crosshair (arms MarkerRadius screen pixels long) on
    // the CENTER of the addressed canvas pixel for (a) the optional explicit
    // host-supplied pivot (e.g. the selected bank sub-element's pivot while
    // authoring it) and (b) every attachment anchor - the canvas point the
    // placed sub-element's pivot lands on (item #6). Frames carry no pivot of
    // their own in the current model, so there is no frame-level marker.
    public struct PivotOverlayConfig
    {
        public uint MarkerRgba;
        public int MarkerRadius;
        public bool HasExplicitPivot;
        public int ExplicitPivotX;
        public int ExplicitPivotY;

        public static PivotOverlayConfig CreateDefault()
        {
            return new PivotOverlayConfig
            {
                MarkerRgba = Config.Overlay.PivotMarkerRgba,
                MarkerRadius = Config.Overlay.DefaultPivotMarkerRadius,
                HasExplicitPivot = false,
                ExplicitPivotX = Config.Canvas.MinCoordinate,
                ExplicitPivotY = Config.Canvas.MinCoordinate
            };
        }
    }

    // The host-owned overlay bundle: which passes run plus each pass's
    // config. Construction seeds every config with its shared-config default
    // so enabling a flag "just works"; hosts mutate the public fields in
    // place (and re-stamp the per-render context - TileGrid.TileSize and
    // AttachmentSources - before each render) rather than reallocating.
    public sealed class ViewportOverlaySettings
    {
        public ViewportOverlayKind Enabled { get; set; }

        public CheckerboardOverlayConfig Checkerboard;
        public PixelGridOverlayConfig PixelGrid;
        public TileGridOverlayConfig TileGrid;
        public HitboxOverlayConfig Hitboxes;
        public AttachmentOverlayConfig Attachments;
        public PivotOverlayConfig Pivots;

        // Resolution source for attachment outlines: the host's loaded bank
        // sub-elements (matched to SubElementAttachment.SubElementName by
        // ordinal name). Null or missing entries silently skip the outline;
        // pivot markers still mark the anchor.
        public IReadOnlyList<SubElement> AttachmentSources { get; set; }

        public ViewportOverlaySettings()
        {
            Enabled = ViewportOverlayKind.None;
            Checkerboard = CheckerboardOverlayConfig.CreateDefault();
            PixelGrid = PixelGridOverlayConfig.CreateDefault();
            TileGrid = TileGridOverlayConfig.CreateDefault();
            Hitboxes = HitboxOverlayConfig.CreateDefault();
            Attachments = AttachmentOverlayConfig.CreateDefault();
            Pivots = PivotOverlayConfig.CreateDefault();
            AttachmentSources = null;
        }
    }
}
