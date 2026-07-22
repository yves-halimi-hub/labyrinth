# Models

[Core README](../README.md) | [Schemas](../Data/README.md)

Named data views shared across editor, game, exporter, and importer boundaries.

## Important files

- [GameDataStructs.cs](GameDataStructs.cs) contains schema-backed structs for
  player, entity, projectile, weapon, power-up, project, animation, frame, layer,
  viewport, and editor tool state.
- [SharedData.cs](SharedData.cs) contains `HitboxData` plus the JSON DTOs
  `EFYVJsonFormat`, `AtlasMetadataJson`, `AnimationMetadataJson`,
  `EffectDescriptorJson` (one authored runtime-effect descriptor —
  name + params + trigger tag, numeric params nullable with shared defaults),
  `AttachmentJson` (one frame-indexed sub-element attachment record —
  safe-stem `subElement` name, canvas-space pivot position, `zOrder`, and
  nullable flips that resolve to false when absent), `TilesetManifestJson`
  (the optional documentVersion-5 tile-ID manifest —
  `{tileSize, tiles}` where list index i is FastGridMap short tile id i;
  `EFYVJsonFormat.tileset` is null/absent on every non-tileset document), and
  `HitboxJson`.

## Invariants

- Numeric and boolean properties are views over a contained `FastSchemaBlock`;
  their enum offset is the storage contract.
- Boolean wrappers encode the configured integer true/false values. The wrappers
  generally do not clamp domain values, so owning systems enforce gameplay and
  editor ranges.
- String properties keep managed text in a private field and update a deterministic
  hash slot. Copying a struct copies its block and references; changing one copy's
  property does not update another copy.
- JSON field names are fixed by configuration attributes and are consumed across
  repositories. Additive unknown JSON fields are ignored by the current importer,
  while malformed field types can fail deserialization.
- `EFYVJsonFormat` carries the top-level `documentVersion` (nullable; absent
  legacy files read as `Backend.Exporter.LegacyDocumentVersion` via
  `EffectiveDocumentVersion`) and the optional `baseAssetType` used for importer
  factory fallback on custom asset types.
- Construct `HitboxData` (or call `HitboxData.CreateDefault()`, the non-bypassable
  spelling) when semantic unit-size defaults are required; `default(HitboxData)`,
  array elements, and uninitialized fields bypass the parameterless constructor
  and produce a zero-sized box. This bypass is an explicit documented contract on
  the struct.
- `MovingToolData`'s jitter amplitude/frequency accessors validate the octant
  index and throw `ArgumentOutOfRangeException` out of range; an invalid index
  can no longer alias sibling schema slots.

