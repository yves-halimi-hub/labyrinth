# Shared Labyrinth Packages

[Back to the Labyrinth system](../README.md)

These packages remove behavioral duplication without moving Labyrinth domain code into EFYV
Platform.

- [EFYV.Runtime.Media](EFYV.Runtime.Media/README.md) owns generic RGBA composition, atlas layout,
  PNG, CRC, and the optional EFYV runtime-kernel adapter.
- [EFYV.Runtime.Kernel.Unity](EFYV.Runtime.Kernel.Unity/README.md) is the generated Unity
  .NET Standard adapter over the unchanged official `Efyv.RuntimeKernel` binding and its
  platform-native plug-in. It lets gameplay use native geometry/spatial batches without a
  second ABI declaration.
- [EFYV.Labyrinth.Artifacts](EFYV.Labyrinth.Artifacts/README.md) owns the bounded, single-parse
  LabyMake snapshot-to-Unity artifact contract.

All are plain .NET projects and/or Unity packages. Media and the Runtime Kernel adapter are
reusable outside Labyrinth; artifacts intentionally depends on LabyBackend's released domain schema.
