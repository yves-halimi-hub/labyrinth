# Authoring Tools

[Core](../README.md) | [Repository](../../README.md) | [Models](../Models/README.md) | [Logic](../Logic/README.md)

Tools receive project/frame context and pointer coordinates through [`ITool`](Tool.cs). `ILayerTool` identifies the edited layer for sparse history capture, `IColorTool` exposes brush color, and `ColorLayerTool` centralizes their backend brush state and visible opaque default.

## Pixel and shape tools

- [`PencilTool`](PencilTool.cs) draws backend Bresenham lines with square or circle brushes. Brush diameter is exact for odd/even sizes, centrally capped, and reduced to the target canvas at execution. Rasterization goes through the shared `StrokeRenderer` in [`Symmetry.cs`](Symmetry.cs).
- [`EraserTool`](EraserTool.cs) is the first-class eraser: it shares the pencil's brush/stroke machinery but always writes the exact transparent dword and deliberately implements no color interface, so a stroke is a true erase (not a paint with color 0) flowing through the same sparse-diff undo path.
- [`FillTool`](FillTool.cs) delegates bounded flood fill to the backend scanline implementation. Mirror mode seeds an additional fill per mirrored coordinate.
- [`EyedropperTool`](EyedropperTool.cs) samples the **composited** frame color — bit-exact with `Frame.FlattenLayers` at the picked pixel (visibility, layer opacity, and alpha blending applied), the documented choice over active-layer sampling. Down/drag re-sample live; pointer-up raises `ColorPicked` once per gesture that touched the canvas. It mutates nothing and implements neither `ILayerTool` nor `IColorTool`, so picks record no history and the palette-constraint snap never applies to it.
- [`ShapeTools.cs`](ShapeTools.cs): `LineTool`, `RectangleTool`, and `EllipseTool` are gesture-preview shape tools — pointer-down snapshots the active layer, every drag restores it and re-rasterizes the shape from the anchor, and the final pointer-up state commits as ONE sparse `FrameEditCommand`. Thickness is clamped shared brush state; rectangle/ellipse support filled or outline (the backend ellipse ring is the gap-free morphological outline).
- [`Symmetry.cs`](Symmetry.cs): `SymmetryMode` (None/Horizontal/Vertical/Both) plus the internal `SymmetryVariants`/`StrokeRenderer` helpers. Mirroring maps `x -> width-1-x` (and `y` likewise), so an odd canvas's center column/row maps onto itself; pencil, eraser, fill, and the shape tools all honor the mode.
- [`SelectionTools.cs`](SelectionTools.cs): `RectSelectTool` and `LassoSelectTool` implement `ISelectionTool` — a gesture defines a [`SelectionRegion`](../Models/Selection.cs) (lasso uses even-odd pixel-center containment) without touching pixels or history; `DesignerSession` collects the completed region on pointer-up and owns the lift/move/copy/paste lifecycle.
- [`StampTool`](StampTool.cs) (item #6) has two modes. `PlaceAttachment` (the default) records a repositionable per-frame `SubElementAttachment` referencing the active sub-element — pointer-down grabs an existing attachment near its anchor (`Attachment.GrabRadius`, no active element required) or places a new one seeded with the element's default transform, drag repositions it, and the whole gesture commits as ONE undoable command; placement is a silent no-op at the per-frame cap. `BakePixels` is the legacy destructive mode: it blits the sub-element with its top-left at pointer − pivot (a default center pivot reproduces the historical centering), clips at canvas edges, and alpha-blends nontransparent source pixels.
- [`TileMakerTool`](TileMakerTool.cs) wraps brush coordinates inside a configured tile, allowing seamless edge painting without out-of-bounds writes.
- [`HitboxTool`](HitboxTool.cs) clamps pointer endpoints to the frame and stores the selected keyed rectangle in world units using shared pixels-per-unit.

## Procedural tools

- [`MovingTool`](MovingTool.cs) configures one of four movement presets — walk deformation, eight-octant jitter, bob/breathe (vertical sine bob plus bottom-anchored squash), or shake/hit-flash (decaying horizontal shake plus a white impact flash) — and returns a generated `AnimationState` whose frames carry the `Generated` layer name. Jitter accessors validate octant indices before touching packed storage. `DesignerSession.GenerateAnimation` performs the command-backed add or the layer-preserving replace (`AnimationGeneratorAPI.MergeOntoTargetLayer`).
- [`MapTool`](MapTool.cs) applies deterministic scatter, prop-tile, noise-fill, and cellular-smoothing operations to a backend `FastGridMap`. Each operation reseeds a backend `FastRandomState` instance (the same xorshift stream the tool previously duplicated by hand, so seeded sequences are unchanged) and publishes status, seed, and affected count. Prop-tile placement floor-snaps pointer coordinates (negative coordinates align correctly) and clamps the snapped origin onto the target map.

## Tool contract

- Invalid frame/layer coordinates are ignored or clamped according to the tool's interaction semantics.
- A host should send a balanced down/drag/up lifecycle through `DesignerSession`, not invoke mutation tools concurrently.
- The session captures only the active layer for `ILayerTool`, all hitboxes for `HitboxTool`, and the frame's attachment list for `StampTool`; a tool must mutate only the state declared by that contract. Selection tools deliberately implement neither, so a selection gesture never pays for a layer capture and records no history.
- Tool loops are bounded by configured brush, tile, jitter, and scatter limits.
- Map edits are currently outside project persistence, undo/redo, and live export.

