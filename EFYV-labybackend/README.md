# EFYV Laby Backend

[Workspace documentation](../README.md) | [LabyMake editor](../EFYV-labymake/) | [Labyrinth game](../EFYV-labyrinth/)

EFYV Laby Backend is the engine-independent C# core shared by the EFYV Labyrinth
toolchain. It defines the data contract used by the LabyMake authoring application
and the Labyrinth Unity game, plus allocation-conscious collections, pixel
operations, export/import, persistence, math, and deterministic verification.

This component is source-first: there is no root library project. The local and
sibling verification projects compile `Core/**/*.cs` directly. Production
integrations must preserve the `EFYVBackend.Core.*` namespaces, unsafe-code
setting, schema offsets, and serialized field names.

## Dependencies and consumers

- Requires the .NET 8 SDK for the verification project.
- Uses only the .NET base class library, including `System.Text.Json`; there are no
  third-party packages.
- Unsafe code is required for fixed schema blocks, raw buffers, pixel operations,
  PNG encoding, and binary saves.
- [EFYV-labymake](../EFYV-labymake/) consumes the shared schemas, models, drawing,
  persistence, and export contracts.
- [EFYV-labyrinth](../EFYV-labyrinth/) consumes the same schemas, models,
  collections, math, persistence, and importer/export metadata contracts.

## Layout

- [Core](Core/README.md): production source organized by responsibility.
- [Tests](Tests/README.md): dependency-free outside-in verification executable.
- [Central configuration](Core/Data/EFYV-LabyrinthConfig.cs): shared constants and
  designer/game/backend registration data.
- [Schema definitions](Core/Data/FastSchemas.cs): fixed-layout storage contract.

## Build and test

From this directory:

```powershell
dotnet build Tests\EFYVBackend.Verification.csproj -c Debug
dotnet run --project Tests\EFYVBackend.Verification.csproj -c Debug
dotnet run --project Tests\EFYVBackend.Verification.csproj -c Release
```

The test project links every production source file, so either build compiles the
entire backend. Tests remain outside `Core` and impose no runtime cost on the game
or editor.

## Compatibility rules

- Schema enum values are storage offsets. Changing or reusing an offset is a data
  migration, not a refactor.
- Packed pixel values are RGBA bytes in a `uint`, with red in the low byte and
  alpha in the high byte.
- The current save format is a raw `PlayerMetaSchema` memory image. It has no
  version, magic value, or checksum and therefore accepts arbitrary full-length
  data and ignores trailing bytes.
- Atlas export validates each output before publication, but PNG and metadata are
  two separately published files; a failure during the second publication has no
  cross-file transaction rollback.
