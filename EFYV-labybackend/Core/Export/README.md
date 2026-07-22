# Export

[Core README](../README.md) | [IO contracts](../IO/README.md) | [Shared models](../Models/README.md)

Publishes pixel atlases and matching EFYV metadata for the editor-to-game live
asset path.

## Important files

- [FastExporter.cs](FastExporter.cs) validates names, dimensions, atlas metadata,
  animation ranges, and pixel element size; packs frames; writes temporary files;
  flushes them; then publishes the final PNG and metadata paths. It also provides
  the atlas grid helpers: `ComputeAtlasLayout` (near-square columns/rows,
  columns >= rows), `GetAtlasFrameOrigin` (row-major frame-index to pixel origin),
  and `ExtractFrameFromAtlas` (the exact inverse of `PackFramesToAtlas`).
  `PushMetadataOnlyToUnityLiveHook` publishes only the `.efyvlaby`
  (same validation and identity/atomic-publish machinery, no pixel payload and
  no PNG write) for LabyMake's live fast path when an edit changed no exported
  pixels; the declared `atlasWidth`/`atlasHeight` still pin the metadata to the
  sheet already on disk.
- [FastPngEncoder.cs](FastPngEncoder.cs) is an internal deterministic RGBA PNG
  writer using CRCs, Adler-32, and pooled scratch storage. IDAT deflates through
  `ZLibStream` by default; a `compressed: false` overload keeps the byte-stable
  stored-block representation for deterministic-output consumers.
- [FastPngDecoder.cs](FastPngDecoder.cs) is the public inverse reader: it parses
  signature/IHDR/IDAT/IEND with CRC validation, inflates, un-filters all five PNG
  filter types, and returns packed RGBA32 pixels. Only 8-bit truecolor-with-alpha
  (color type 6), non-interlaced data is accepted; anything else raises
  `ArgumentException` with a cause-specific message.
- Both PNG codecs delegate chunk CRCs to the shared
  [`Core/IO/FastCrc32`](../IO/FastCrc32.cs) table — neither carries a private
  CRC table copy.
- [SafePathPolicy.cs](../IO/SafePathPolicy.cs) provides file-stem and containment
  enforcement used for all exporter-created names.

## Contracts and limits

- Export pixels must have one 32-bit unmanaged element per atlas pixel and the
  checked length must equal `width * height`.
- Atlas metadata validation is single-sourced in
  `FastExporter.TryValidateAtlasMetadata` (per-cause `AtlasMetadataError`),
  shared by this exporter, the LabyMake export engine/validator, and the Unity
  importer. It also enforces the Unity texture caps
  (`EFYVLabyrinthConfig.LabyMake.Export.MaxAtlasDimension` /
  `MaxAtlasPixelCount`). Frame sizes divide the atlas; animations are named,
  positive-rate, ordered, non-overlapping, and within frame capacity. Gaps are
  allowed.
- Atlas animations may carry optional timing/playback members:
  `frameDurationsMs` (exactly `frameCount` entries; `0` = inherit `fps`,
  positive entries are milliseconds capped by
  `Backend.Exporter.MaxFrameDurationMs`), `loopStart`/`loopEnd`
  (animation-local, `start <= end < frameCount`), and `pingPong`. All four are
  omitted when default — `fps` and the full frame range remain the fallback
  for every reader — and are validated by the same shared validator
  (`AnimationFrameDurations` / `AnimationLoopRange` causes).
- Atlas animations may also carry an optional `effects` array: each
  descriptor is `{name, effectType, trigger}` plus optional
  `{colorRgba, durationMs, strength}`. `effectType` is one of
  `Backend.Exporter.EffectType*` (`flash`/`tint`/`particleHook`; particleHook
  requires a non-empty `name`), `trigger` is a non-empty seam tag
  (`Shared.EffectTriggerOnSpawn`/`OnDamaged` or custom), counts are capped by
  `MaxEffectsPerAnimation`, and value ranges by
  `Min/MaxEffectDurationMs` and `Min/MaxEffectStrength` (shared-validator
  cause `AnimationEffects`). The array is omitted when empty so effect-free
  documents stay byte-identical.
- Documents may carry an optional top-level `attachments` array:
  frame-indexed sub-element attachment records
  (`{frameIndex, subElement, x, y, zOrder}` plus `flipX`/`flipY` written only
  when true), validated by the single-sourced
  `FastExporter.TryValidateAttachments` (safe-stem `subElement`,
  non-negative `frameIndex`, `zOrder` within
  `Min/MaxAttachmentZOrder`, per-frame count capped by
  `MaxAttachmentsPerFrame`; reports the first offending index) shared with
  the Unity importer. The array is omitted when null/empty so
  attachment-free documents stay byte-identical. The designer flattens the
  attachment pixels into the atlas at export time. The current Unity consumer
  stores these records but does not render separate dynamic sub-element sprites.
- Documents may carry an optional top-level `tileset` manifest block:
  `{tileSize, tiles}` where the tile at list index i is FastGridMap
  short tile id i. It is validated by the single-sourced
  `FastExporter.TryValidateTilesetManifest` (per-cause
  `TilesetManifestError`; positive `tileSize`, a non-empty bounded tile-name
  list, and — when an atlas block rides alongside — exactly
  `tileSize`-square frames with capacity for every declared tile), shared
  with the Unity importer. The member is omitted when absent so non-tileset
  documents stay byte-identical.
- Written metadata starts with the top-level `documentVersion`
  (`Backend.Exporter.CurrentDocumentVersion`, currently 5 — version 2 added
  the optional atlas timing fields, version 3 the optional per-animation
  effect descriptors, version 4 the optional top-level attachment records,
  version 5 the optional tileset manifest block;
  version-absent legacy files read as `LegacyDocumentVersion`).
  Importers accept the whole `[MinSupportedDocumentVersion ..
  CurrentDocumentVersion]` range rather than pinning one value. Metadata can
  also carry `baseAssetType`, the registered base of a custom asset type, for
  importer factory fallback.
- Entity name, then asset name, determines the output stem; an export without
  either identity property is rejected (no fallback stem is minted). Reserved
  device names, separators, traversal, and unsafe trailing characters are
  rejected.
- Publishes go through the bounded IO retry (`Core/IO/FastIoRetry.cs`), so a
  destination briefly held by Unity does not fail the swap.
- Failures before publication remove temporary files and preserve prior outputs.
  The final PNG and metadata moves are separate filesystem operations, so they do
  not form one cross-file transaction if the second move itself fails.
- `PackFramesToAtlas` copies complete rows and permits a larger source buffer, but
  rejects any destination rectangle outside the atlas. `ExtractFrameFromAtlas`
  mirrors those guards for the source rectangle and permits a larger destination
  buffer whose tail is left untouched.
- Encoder round trip: pixels written by `FastPngEncoder` (either mode) decode back
  bit-exactly through `FastPngDecoder`.

