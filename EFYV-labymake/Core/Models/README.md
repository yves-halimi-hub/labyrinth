# Models

[Core](../README.md) | [Repository](../../README.md) | [Logic](../Logic/README.md) | [Persistence](../Persistence/README.md)

The model layer separates mutable designer state from immutable preview/export state. Packed scalar data uses backend schema blocks; owned collections and pixel arrays remain explicit C# objects.

## Mutable authoring graph

- [`EFYVProject`](EFYVProject.cs) holds the target asset type, typed property dictionary, Unity project path, canvas size, deterministic designer seed, and ordered animations.
- [`AnimationState`](AnimationState.cs) owns a name, positive FPS, and ordered frames.
- [`Frame`](Frame.cs) owns fixed dimensions, ordered layers, and keyed backend `HitboxData`. `Clone` deep-copies layers and hitbox membership.
- [`Layer`](Layer.cs) stores contiguous RGBA32 pixels plus visibility and finite opacity clamped to `[0,1]`. Out-of-bounds reads are transparent and writes are ignored.
- [`SubElement`](SubElement.cs) is an owned, cloned pixel region used by the stamp tool and asset bank.

`PixelColor` packs channels as `R | G<<8 | B<<16 | A<<24`, matching backend export. A newly allocated canvas is transparent; authoring tools select their own opaque default brush color.

## Compositing invariants

`Frame.FlattenLayers` clears the destination, walks layers in list order, skips hidden/zero-opacity layers, verifies matching dimensions, and alpha-blends through backend `FastMemory`. The caller-provided overload requires exactly `Width * Height` pixels and enables buffer reuse.

## Immutable snapshots

[`ProjectSnapshot`](ProjectSnapshot.cs) captures project metadata, read-only property values, animation frame ranges, flattened frame pixels, and hitboxes. It also precomputes total frame and hitbox counts. `JsonElement` values are cloned; other unexpected reference values are reduced to strings.

Preview and export consume snapshots so later designer mutations cannot change in-flight work. Persistence uses a separate editable-project snapshot described in [Persistence](../Persistence/README.md).

