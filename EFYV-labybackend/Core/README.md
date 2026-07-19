# Core

[Backend README](../README.md)

`Core` contains all production code in this repository. It is independent of
Unity APIs and can be compiled into the editor, game, or a plain .NET process.

`Core` also doubles as a local Unity (UPM) package consumed by the game
project: [`package.json`](package.json) names it `com.efyv.labybackend` and
[`EFYVBackend.Core.asmdef`](EFYVBackend.Core.asmdef) compiles everything here
into the engine-neutral `EFYVBackend.Core` assembly (unsafe allowed, no engine
references). [`csc.rsp`](csc.rsp) raises this assembly to C# 10 inside Unity
(the parameterless `HitboxData` constructor requires it; Unity defaults to
C# 9). The package root is `Core` — not the repository root — so the sibling
`Tests` directory and its `bin`/`obj` output are never imported by Unity. The
`*.meta` files alongside sources carry the stable GUIDs Unity needs; keep them
when moving or renaming files.

## Areas

- [Collections](Collections/README.md): bounded pools, swap removal, flat maps,
  and ring-buffer coordinate mapping.
- [Data](Data/README.md): central configuration, fixed schema blocks, offsets, and
  binary profile layout.
- [Export](Export/README.md): validated atlas packing and PNG/JSON publication.
- [IO](IO/README.md): metadata import, raw profile saves, and contained paths.
- [Math](Math/README.md): drawing, deformation, random, procedural, and scalar
  helpers.
- [Memory](Memory/README.md): unsafe copy, blend, blit, scaling, and blur routines.
- [Models](Models/README.md): schema-backed views and shared JSON DTOs.
- [Physics](Physics/README.md): engine-neutral translation calculation.

## Design constraints

- Hot paths use flat arrays, structs, integer-packed data, pointer loops, and
  caller-owned buffers to avoid per-frame allocation.
- Public entry points validate dimensions and checked size arithmetic where they
  can; explicitly named unsafe accessors still require caller-proven bounds.
- [EFYVLabyrinthConfig](Data/EFYV-LabyrinthConfig.cs) owns shared numeric and text
  constants. Do not scatter replacements through consumers.
- [FastSchemaBlock](Data/FastSchemas.cs) is the common 64-slot storage unit used
  by [schema-backed models](Models/GameDataStructs.cs).

