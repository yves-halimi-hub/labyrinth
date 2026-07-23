# EFYV Laby Backend

[Workspace documentation](../README.md) | [LabyMake node](../EFYV-labymake/README.md) | [LabyMake engine](../EFYV-labymake/services/labymake-engine/) | [Labyrinth game](../EFYV-labyrinth/)

EFYV Laby Backend is the engine-independent C# core shared by the EFYV Labyrinth
toolchain. It defines the data contract used by the LabyMake authoring application
and the Labyrinth Unity game, plus allocation-conscious collections, pixel
operations, export/import, persistence, math, and deterministic verification.

This component is source-first. `Core/EFYVBackend.Core.csproj` provides the plain .NET
reference used by the artifact service; sibling verification projects still compile
`Core/**/*.cs` directly, and the Unity
game consumes `Core` as a local UPM package (`com.efyv.labybackend`, see
[Core/README.md](Core/README.md)) compiled into the `EFYVBackend.Core`
assembly. Production integrations must preserve the `EFYVBackend.Core.*`
namespaces, unsafe-code setting, schema offsets, and serialized field names.

## Dependencies and consumers

- Requires the .NET 8 SDK for the verification project.
- Uses only the .NET base class library, including `System.Text.Json`; there are no
  third-party packages.
- Unsafe code is required for fixed schema blocks, raw buffers, pixel operations,
  PNG encoding, and binary saves.
- The declaration-driven [LabyMake node](../EFYV-labymake/README.md) and its narrow
  [domain engine](../EFYV-labymake/services/labymake-engine/) publish the bounded artifact contract consumed by
  the game. Maker document/session persistence belongs to the EFYV platform, not this library.
- [EFYV-labyrinth](../EFYV-labyrinth/) consumes the same schemas, models,
  collections, math, persistence, and importer/export metadata contracts.

## Layout

- [Core](Core/README.md): production source organized by responsibility.
- [Tests](Tests/README.md): dependency-free outside-in verification executable.
- [Benchmarks](Benchmarks/README.md): warmed throughput and allocation harness for media and export paths.
- [Central configuration](Core/Data/EFYV-LabyrinthConfig.cs): shared constants and
  designer/game/backend registration data.
- [Schema definitions](Core/Data/FastSchemas.cs): fixed-layout storage contract.

## Build and test

From this directory:

```powershell
dotnet build Tests\EFYVBackend.Verification.csproj -c Debug
dotnet run --project Tests\EFYVBackend.Verification.csproj -c Debug
dotnet run --project Tests\EFYVBackend.Verification.csproj -c Release
dotnet run --project Benchmarks\EFYVBackend.Benchmarks.csproj -c Release -- --verify
```

The test project links every production source file, so either build compiles the
entire backend. Tests remain outside `Core` and impose no runtime cost on the game
or editor.

## Compatibility rules

- Schema enum values are storage offsets. Changing or reusing an offset changes
  persisted data and requires explicit conversion; it is not a source-only refactor.
- Packed pixel values are RGBA bytes in a `uint`, with red in the low byte and
  alpha in the high byte.
- The current save format wraps the raw `PlayerMetaSchema` memory image in a
  versioned envelope containing magic bytes and a payload CRC. Loading requires
  an exact-length, matching-version envelope and rejects corrupted or trailing data.
- Atlas export validates each output before publication, but PNG and metadata are
  two separately published files; a failure during the second publication has no
  cross-file transaction rollback.
