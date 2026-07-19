# Export

[Core](../README.md) | [Repository](../../README.md) | [Models](../Models/README.md) | [Logic](../Logic/README.md)

[`ExportEngine`](ExportEngine.cs) converts a validated project snapshot into the live Unity file pair. It delegates PNG and JSON encoding to the backend [`FastExporter`](../../../EFYV-labybackend/Core/Export/FastExporter.cs); Unity consumes the result through [`EFYVPixelArtImporter`](../../../EFYV-labyrinth/Assets/Scripts/Editor/EFYVPixelArtImporter.cs).

## Pipeline

1. `Export(EFYVProject, ...)` runs `ProjectValidator` with `ProjectValidationScope.Export`, then captures an immutable `ProjectSnapshot`. The snapshot overload enforces the shared atlas caps itself (it has no validator) and both paths reject snapshots without an `entityName`/`assetName` identity.
2. Frames are flattened in animation order and packed row-major into a near-square RGBA grid atlas (`FastExporter.ComputeAtlasLayout`); the Unity importer slices the same row-major grid, and unused trailing cells stay transparent. Item #6: before packing, each frame's sub-element ATTACHMENTS are flattened onto its pixels — ascending `zOrder` (ties keep authored order), flips applied, the element's pivot landing on the attachment position, off-canvas parts clipped, blended with the exact `FastMemory.BlendColor` math the legacy bake-pixels stamp uses. Resolution goes through the engine's optional [`ISubElementResolver`](../Logic/ISubElementResolver.cs) (`DesignerSession.Create` wires the host's `AssetBankManager`); an unresolvable name — or a resolver-less engine — skips that attachment's pixels but STILL emits its metadata record, so a missing bank file never breaks the live-debug loop (best-effort by design). The snapshot's own pixel buffers are never mutated.
3. Hitboxes receive global frame indices. The written `.efyvlaby` starts with the top-level `documentVersion`, carries `baseAssetType` (the registered base of the project's asset type, for importer factory fallback), and its atlas metadata records format version, dimensions, and each animation's name, FPS, start frame, and frame count. Item #10: each animation additionally carries `frameDurationsMs` (raw model values, `0` = inherit FPS) when any frame overrides its duration, plus `loopStart`/`loopEnd`/`pingPong` when they differ from the full-range forward defaults — the loop range is written CLAMPED via the snapshot's effective accessors, and feature-free exports stay byte-identical to pre-item-#10 output. Item #7: each animation with authored effect descriptors carries an `effects` array (every descriptor field populated: `name`, `effectType`, `trigger`, `colorRgba`, `durationMs`, `strength`); the array is omitted when the animation has none, keeping effect-free exports byte-identical. Item #6 (documentVersion 4): attachments ALSO export as a structured top-level `attachments` array (same frame-indexed shape as `hitboxes`: `frameIndex`, `subElement`, `x`, `y`, `zOrder`, plus `flipX`/`flipY` only when true), omitted entirely when no frame has any — the flattened pixels keep the game simple today, the records enable future dynamic sub-element rendering (the game importer stores them; rendering is deferred).
4. Backend export writes identity-named staged files outside `Assets`.
5. LabyMake publishes to `<UnityProject>/Assets/RawArt`, adding `_Up`, `_Down`, `_Left`, or `_Right` to directional file stems. Publishes retry transient `IOException`s through the backend `FastIoRetry` (Unity briefly holding a destination no longer drops a live push).

## Linked directional projects (item #33)

- For a linked 4-direction project (`EFYVProject.Directional != null`), ONE `Export(EFYVProject, ...)` call publishes all four facings — one suffixed `.png`/`.efyvlaby` pair per facing via `ProjectSnapshot.CaptureFacing` (that facing's animation set + the shared properties with the `facing` key overridden, which is exactly what drives the existing suffix convention in step 5). Inactive facings publish first in catalog order, the ACTIVE facing last, and the returned `ExportResult` is the active facing's pair. Plain projects export exactly as before.
- `ExportAllFacings(EFYVProject, ...)` returns the per-facing results (active facing LAST); it throws `InvalidOperationException` for non-directional projects. Validation runs once up front at export scope, where `DirectionalFacingIncomplete` blocks the publish while any facing is empty. Each facing's pair publishes atomically on its own; cancellation between facings leaves already-published pairs in place.
- Live debug captures one snapshot per facing under the same rules, so a single debounced push refreshes all four pairs and reports the active facing's result.

## Maps and tilesets (item #5)

- `ExportTileset(EFYVProject, ...)` publishes the project's [`TilesetSection`](../Models/Tileset.cs) as a tile-sheet `.efyvlaby` + PNG: the tiles pack into a near-square grid (one TileSize-square frame per tile, row-major — TILE-ID order), the atlas block declares ONE animation (`LabyMake.Export.TilesetAnimationName`, fps `TilesetAnimationFps`) covering exactly tileCount frames so Unity slices one sprite per tile, and the tile-ID manifest `{tileSize, tiles}` rides as the documentVersion-5 `tileset` block. Identity/path/manifest enforcement and the atomic publish are `FastExporter`'s (the shared `TryValidateTilesetManifest` gate); an empty or absent tileset refuses to export.
- `ExportMap(EFYVProject)` publishes the project's [`MapSection`](../Models/MapSection.cs) as `<MapId>.efyvmap` in `Assets/RawArt` through the backend `FastMapExporter` (the `{EFYM, version, CRC32}` envelope with staged atomic publication); returns the published path. Unity ingests it through `EFYVMapImporter`.

## Live fast path (item #27)

The engine keeps a per-instance `LivePublishCache` (the live-debug loop holds one engine), so a debounced live cycle avoids needless work two ways:

- **Content-hash suppression.** Before publishing each staged artifact, its bytes are hashed with the shared backend `FastCrc32` (signature = CRC-32 + length) and compared to what this engine last published to that path. A byte-identical artifact whose destination still exists is NOT re-published, so the downstream Unity re-import only fires on a real change. The PNG and `.efyvlaby` suppress INDEPENDENTLY: a hitbox nudge rewrites the metadata but leaves the byte-identical PNG untouched. An externally deleted destination is always re-published (the cache checks `File.Exists`); the hash is recorded only after a successful publish.
- **Metadata-only publish.** `Export(snapshot, ct, preferMetadataOnly: true)` — driven by `LiveDebugController` when the accumulated edit scope since the last publish changed no exported pixels or atlas layout — publishes ONLY the `.efyvlaby`, never packing or re-encoding the atlas. It still writes an atlas block whose dimensions pin the existing sheet. The hint is honoured only when the sibling PNG already exists; otherwise it falls back to a full publish, so a first-ever (or post-deletion) export can never leave the sheet missing. The backend does the PNG-less write through `FastExporter.PushMetadataOnlyToUnityLiveHook`.

Both paths converge on the same on-disk result; the metadata-only path just skips the pixel work up front, and content hashing catches an identical rewrite even on the full path.

## Publication contract

- The image is published first; `.efyvlaby` metadata is authoritative and published last. When content-hash suppression leaves only one artifact to publish, `PublishSingle` does the single atomic swap (the suppressed one is provably already current on disk).
- Existing destination files move to backup paths in the staging directory. If metadata publication fails, `PublishPair` restores the previous image and metadata state.
- Temporary and backup files are deleted after completion. The staging directory is outside Unity's import tree.
- `AtomicReplace` also supplies the single-file replace primitive used by [persistence](../Persistence/README.md) and the asset bank.

`ExportResult` reports final paths, frame/hitbox counts, atlas dimensions, and — item #27 — `ImageWritten`/`MetadataWritten` flags telling which artifact this publish actually rewrote (both false on a fully-suppressed live republish; `ImageWritten` false on a metadata-only publish). Export does not persist editable layers; the PNG contains composited frames, while `.efyvlaby` contains runtime metadata.

