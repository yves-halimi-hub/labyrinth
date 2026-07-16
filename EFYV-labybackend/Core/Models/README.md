# Models

[Core README](../README.md) | [Schemas](../Data/README.md)

Named data views shared across editor, game, exporter, and importer boundaries.

## Important files

- [GameDataStructs.cs](GameDataStructs.cs) contains schema-backed structs for
  player, entity, projectile, weapon, power-up, project, animation, frame, layer,
  viewport, and editor tool state.
- [SharedData.cs](SharedData.cs) contains `HitboxData` plus the JSON DTOs
  `EFYVJsonFormat`, `AtlasMetadataJson`, `AnimationMetadataJson`, and `HitboxJson`.

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
- Construct `HitboxData` when semantic unit-size defaults are required; `default`
  produces a zero-sized box.

