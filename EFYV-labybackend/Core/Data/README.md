# Data

[Core README](../README.md)

The storage and configuration authority shared by backend, editor, and game.

## Important files

- [EFYV-LabyrinthConfig.cs](EFYV-LabyrinthConfig.cs) contains `Shared`, `Game`,
  `LabyMake`, and `Backend` constant groups, designer asset registrations, field
  metadata, file names, numeric limits, and pixel-format constants.
- [FastSchemas.cs](FastSchemas.cs) defines `FastSchemaBlock`, all slot enums,
  `PlayerMetaSchema`, default stat initialization, and fixed toon storage.
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

