# Tests

[Backend README](../README.md) | [Production core](../Core/README.md)

Dependency-free, outside-in verification for every backend source area. The
console project links `../Core/**/*.cs` directly, enables unsafe code, runs a fixed
set of deterministic groups, prints each result, and exits nonzero on any failure.
Nothing in this directory is referenced by the game or editor runtime.

## Files

- [EFYVBackend.Verification.csproj](EFYVBackend.Verification.csproj): .NET 8
  executable and source-link boundary.
- [Program.cs](Program.cs): runner, baseline integration tests, PNG parser/CRC
  oracle, and common assertions.
- [SchemaCollectionTests.cs](SchemaCollectionTests.cs): reflection coverage for
  schema-backed models plus randomized pool, swap-list, grid, and ring-buffer
  state models.
- [MathMemoryTests.cs](MathMemoryTests.cs): independent numeric and pixel reference
  models, pointer guard regions, deterministic random checks, blur, procedural,
  deformation, and physics tests, plus the rectangle/ellipse shape-rasterizer
  reference models (randomized clip/thickness/filled sweeps, the gap-free
  outline-ring invariant, degenerate boxes, and guard contracts).
- [ImportSaveExportAdversarialTests.cs](ImportSaveExportAdversarialTests.cs):
  malformed and locked files, save-memory canaries, the versioned save
  envelope's rejection matrix (legacy raw dumps, trailing bytes, wrong
  magic/version, CRC-flagged payload corruption), traversal attempts, metadata
  guards, export preservation (including the no-identity rejection), atlas
  fuzzing, and PNG determinism.
- [CollectionsMemoryDeepTests.cs](CollectionsMemoryDeepTests.cs): exact LIFO pool
  identity and factory fault injection, registry contracts and per-type isolation,
  tampered swap-list indices, grid and ring-buffer extremes, bit-exact blend and
  layer-threshold models, blur-overload/blit edge cases, grid bulk-editing
  primitives (TrySetTile/FillRect/CopyRegion/FloodFillTiles/Resize) against
  reference models, and registry locking under concurrent registration.
- [MathPhysicsDeepTests.cs](MathPhysicsDeepTests.cs): float special-value and
  facing-corner contracts, bit-exact normalize and random-range replay models,
  flood-fill and thick-brush adversarial cases, walk/jitter deformation reference
  sweeps, cellular threshold extremes, translation special-value propagation,
  instance `FastRandomState` stream/isolation/Lemire-unbiased-range replays, and
  maze/rooms generator determinism and structural (spanning-tree, connectivity)
  checks.
- [ExportIoDeepTests.cs](ExportIoDeepTests.cs): byte-level PNG reference encoder
  for the stored mode (zlib stored blocks, Adler-32, CRCs) plus decode-and-compare
  structural verification of the compressed default, FastPngDecoder round-trip
  property tests and an adversarial malformed-PNG corpus (all five scanline
  filters, CRC/truncation/header rejections), near-square atlas layout and
  frame-extraction inverses, generic pixel types and forward-only streams,
  exporter naming and culture invariance, JSON escaping fidelity, importer edge
  documents, the pinned save envelope byte layout (header plus payload), file
  sharing error paths, a safe-path-policy reference model, generic atlas
  packing, the shared atlas-metadata validator's per-cause matrix, the
  tri-state `FastImporter.TryParse` contract, the bounded publish retry, the
  shared CRC-32 reference model, and the `.efyvlaby`
  documentVersion/baseAssetType contract.
- [DataModelsDeepTests.cs](DataModelsDeepTests.cs): bit-exact schema-block
  reference model with canaries, save-struct byte layout pins, property-to-slot
  mapping for every model wrapper, bool/string-hash edge semantics, shared JSON
  wire contracts, and cross-constant config invariants (including the item #10
  documentVersion range semantics and the per-frame duration wire cap).
- [AnimationTimingDeepTests.cs](AnimationTimingDeepTests.cs): the item #10 atlas
  timing/playback metadata contract (frameDurationsMs/loopStart/loopEnd/pingPong
  validation matrix, exporter round trip with optional members omitted when
  default, legacy documents parsing as absent) and full reference-model sweeps
  for the bob/breathe and shake/hit-flash deformation presets (guards,
  zero-amplitude verbatim copies, impact-frame flash semantics, alpha
  preservation).
- [SubElementAttachmentDeepTests.cs](SubElementAttachmentDeepTests.cs): the
  item #6 wire surface — the shared `TryValidateAttachments` gate (safe-stem
  names, frame/zOrder bounds, the per-frame cap with exact offending-index
  reporting), the documentVersion-4 writer (attachments after atlas, pinned
  per-entry member order, flips written only when true, omission when
  null/empty with byte identity to the pre-attachment overload), the parse
  round trip, and exporter rejection of invalid records.
- [EffectsFiltersDeepTests.cs](EffectsFiltersDeepTests.cs): the item #7 surface —
  outline (8-neighborhood silhouette expansion against a naive reference,
  silhouette pixels never recolored, aliasing safety), glow (hard-rim radius 0,
  blur-spread halo alpha decay, source-over-halo compositing), HSV color shift
  (exact primary rotations/clamps/wraps plus a bit-exact mirrored-reference
  fuzz, alpha and transparent-pixel preservation, non-finite guards), the
  effect-descriptor wire contract (validation matrix onto `AnimationEffects`,
  export/parse round trip, per-descriptor field order, omission-when-empty
  byte stability), and the single-sourced PNG CRC (ISO-HDLC check vector,
  encoder chunk CRCs validated via `FastCrc32`, decoder round trip, and the
  structural no-private-table pin on both codecs).
- [MapPipelineDeepTests.cs](MapPipelineDeepTests.cs): the item #5 wire surface -
  the .efyvmap container (envelope bytes, payload layout, deterministic round
  trips, exact-size accounting, atomic republish with no stray temporaries,
  the shared `FastMapExporter.TryValidate` gate matrix, and a
  corruption/truncation/forged-payload matrix that must all classify as
  Malformed while missing files stay Missing), the shared
  `TryValidateTilesetManifest` matrix (standalone and against a sibling atlas
  block), and the documentVersion-5 tileset block writer (ordered
  {tileSize, tiles} after the atlas, omission when absent keeping non-tileset
  documents byte-free of the member, FastImporter read-back).

## Run

From the repository root:

```powershell
dotnet run --project Tests\EFYVBackend.Verification.csproj -c Debug
dotnet run --project Tests\EFYVBackend.Verification.csproj -c Release
```

Tests use fixed seeds and bounded loops. Temporary files are isolated under the
system temp directory and removed in `finally` blocks. Large assertion counts are
intentional reference-model checks, not runtime instrumentation.
