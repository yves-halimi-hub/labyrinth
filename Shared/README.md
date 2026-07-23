# Shared Labyrinth Packages

[Back to the Labyrinth system](../README.md)

These packages remove behavioral duplication without moving Labyrinth domain code into EFYV
Platform.

- [EFYV.Runtime.Media](EFYV.Runtime.Media/README.md) owns generic RGBA composition, atlas layout,
  PNG, CRC, and the optional EFYV runtime-kernel adapter.
- [EFYV.Labyrinth.Artifacts](EFYV.Labyrinth.Artifacts/README.md) owns the bounded, single-parse
  LabyMake snapshot-to-Unity artifact contract.

Both are plain .NET projects and Unity packages. Media is reusable outside Labyrinth; artifacts
intentionally depends on LabyBackend's released domain schema.
