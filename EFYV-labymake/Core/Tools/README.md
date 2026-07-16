# Authoring Tools

[Core](../README.md) | [Repository](../../README.md) | [Models](../Models/README.md) | [Logic](../Logic/README.md)

Tools receive project/frame context and pointer coordinates through [`ITool`](Tool.cs). `ILayerTool` identifies the edited layer for sparse history capture, `IColorTool` exposes brush color, and `ColorLayerTool` centralizes their backend brush state and visible opaque default.

## Pixel and shape tools

- [`PencilTool`](PencilTool.cs) draws backend Bresenham lines with square or circle brushes. Brush diameter is exact for odd/even sizes, centrally capped, and reduced to the target canvas at execution.
- [`FillTool`](FillTool.cs) delegates bounded flood fill to the backend scanline implementation.
- [`StampTool`](StampTool.cs) centers a `SubElement`, clips at canvas edges, and alpha-blends nontransparent source pixels.
- [`TileMakerTool`](TileMakerTool.cs) wraps brush coordinates inside a configured tile, allowing seamless edge painting without out-of-bounds writes.
- [`HitboxTool`](HitboxTool.cs) clamps pointer endpoints to the frame and stores the selected keyed rectangle in world units using shared pixels-per-unit.

## Procedural tools

- [`MovingTool`](MovingTool.cs) configures walk deformation or eight-octant jitter and returns a generated `AnimationState`. Jitter accessors validate octant indices before touching packed storage. `DesignerSession.GenerateAnimation` performs the command-backed add/replace.
- [`MapTool`](MapTool.cs) applies deterministic scatter, prop-tile, noise-fill, and cellular-smoothing operations to a backend `FastGridMap`. Each operation advances a seeded sequence and publishes status, seed, and affected count.

## Tool contract

- Invalid frame/layer coordinates are ignored or clamped according to the tool's interaction semantics.
- A host should send a balanced down/drag/up lifecycle through `DesignerSession`, not invoke mutation tools concurrently.
- The session captures only the active layer for `ILayerTool` and all hitboxes for `HitboxTool`; a tool must mutate only the state declared by that contract.
- Tool loops are bounded by configured brush, tile, jitter, and scatter limits.
- Map edits are currently outside project persistence, undo/redo, and live export.

