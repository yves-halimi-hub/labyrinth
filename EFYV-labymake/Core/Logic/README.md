# Application Logic

[Core](../README.md) | [Repository](../../README.md) | [Models](../Models/README.md) | [Tools](../Tools/README.md)

This directory contains the host-facing application services. [`DesignerSession`](DesignerSession.cs) is the central facade; the remaining types are independently injectable/testable collaborators.

## Session and history

- `DesignerSession` owns the active project, selected animation/frame, active tool, dirty version, validation result, preview, autosave, live-debug controller, and bounded command history.
- Pointer down captures the frame and active tool. Drag/up continue against that stable pair, so changing UI selection mid-gesture does not redirect the edit.
- [`DesignerCommands`](DesignerCommands.cs) stores changed pixel indices or hitbox deltas rather than whole frames. Exceptions and `CancelGesture` restore the captured state.
- [`CommandManager`](CommandManager.cs) bounds undo/redo by command count and estimated bytes, normalizes invalid estimates, and evicts oldest commands before accounting can overflow.
- Animation, frame, layer, property, opacity, visibility, FPS, reorder, and generated-animation changes are recorded commands.

## Schema and validation

- [`AssetSchemaService`](AssetSchemaService.cs) turns the shared config manifest into immutable field/type definitions and supports exact-type manifest registration without reflection.
- [`ToolbarAPI`](ToolbarAPI.cs) creates directional/non-directional categories, exposes inspector metadata, normalizes typed values, and enforces ranges, choices, and read-only fields.
- [`ProjectValidator`](ProjectValidator.cs) returns structured issue codes and locations. It checks schema properties, identities/facing, finite values, boss phase thresholds, canvas/atlas limits, animation/frame/layer structure, opacity, hitboxes, and export paths.

## Preview and live debug

- [`PreviewController`](PreviewController.cs) plays immutable composite frames with exact tick accumulation, seek/loop controls, and overflow-safe large elapsed times.
- [`LiveDebugController`](LiveDebugController.cs) debounces change notifications, validates after the delay, captures on the supplied synchronization context, and exports snapshots in background work.
- [`DebounceScheduler`](DebounceScheduler.cs) abstracts time/delay so autosave and live-debug state machines are deterministic in tests.
- Both controllers publish immutable state snapshots rather than exposing mutable worker state.

## Supporting services

- [`AnimationGeneratorAPI`](AnimationGeneratorAPI.cs) builds walk and eight-octant jitter animations while copying base-frame hitboxes.
- [`AssetBankManager`](AssetBankManager.cs) atomically stores bounded `.efyvsub` records, skips corrupt entries with a `LoadFailed` event, and extracts flattened canvas regions.
- [`ViewportController`](ViewportController.cs) maps screen/canvas coordinates and renders a flattened frame through backend nearest-neighbor scaling.

