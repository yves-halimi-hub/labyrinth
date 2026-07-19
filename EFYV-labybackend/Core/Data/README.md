# Data

[Core README](../README.md)

The storage and configuration authority shared by backend, editor, and game.

## Important files

- [EFYV-LabyrinthConfig.cs](EFYV-LabyrinthConfig.cs) contains `Shared`, `Game`,
  `LabyMake`, and `Backend` constant groups, designer asset registrations, field
  metadata, file names, numeric limits, and pixel-format constants. `Shared`
  also owns the wire-format authority `AssetSchemaFieldManifest`: the single
  field-name-to-`AssetSchema`-slot table consumed by both the LabyMake designer
  schema and the Unity importer, plus the `.efyvlaby` `documentVersion`
  constants (`Backend.Exporter`) and the save envelope constants
  (`Backend.Save`).
- [FastSchemas.cs](FastSchemas.cs) defines `FastSchemaBlock`, all slot enums,
  `PlayerMetaSchema`, default stat initialization, and fixed toon storage.
  `PlayerMetaSchema` static-asserts at type initialization that its real size
  (and `FastSchemaBlock`'s) matches the config constants its toon-block stride
  math depends on.
- [Model wrappers](../Models/README.md) expose named properties over these slots.

## Binary invariants

- `FastSchemaBlock` is exactly 64 `int` slots, or 256 bytes. Floats preserve their
  bit pattern in the same slots.
- Schema enum values are direct unchecked array offsets. Every value must remain
  unique where fields share a block and stay below `FastSchemaBlock.MaxSize`.
- Persisted structs use sequential layout with the configured pack value. Field
  order, enum offsets, block size, and `PlayerMetaSchema.MaxToons` are save-format
  compatibility boundaries.
- `PlayerMetaSchema.Default()` initializes legacy stats, achievements, and coin
  state; a zeroed struct is not equivalent to that semantic default.
- String-bearing model properties retain the string in a managed field and write
  a deterministic hash to their schema slot. The hash is not reversible text.

