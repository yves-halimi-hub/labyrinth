# EFYV Labyrinth System

[EFYV Games](../../README.md) / [Game repositories](../README.md) / Labyrinth

This monorepo contains the three components that build the Labyrinth game, designer, and shared runtime foundation. Their existing directory names are preserved so source links and project references remain stable.

## Choose Where To Start

| I want to understand or change... | Start here |
| --- | --- |
| Shared schemas, memory, math, collections, export, or save primitives | [EFYV LabyBackend](EFYV-labybackend/README.md) |
| The asset designer, drawing tools, history, validation, persistence, export, autosave, or live debug | [EFYV LabyMake](EFYV-labymake/README.md) |
| The desktop editor app (run it: `dotnet run --project EFYV-labymake\App\EFYVLabyMake.App.csproj -c Release`) | [EFYV LabyMake App](EFYV-labymake/App/README.md) |
| Unity gameplay, entities, managers, weapons, pooling, import, or editor integration | [EFYV Labyrinth](EFYV-labyrinth/README.md) |
| Cross-component verification | [Backend tests](EFYV-labybackend/Tests/README.md), [designer tests](EFYV-labymake/Tests/README.md), and [game/editor tests](EFYV-labyrinth/Tests/README.md) |

## System Shape

```text
EFYV-labymake  -- PNG + .efyvlaby -->  EFYV-labyrinth
       \                                  /
        +---- EFYV-labybackend contracts -+
```

- **LabyBackend** is the dependency foundation. It owns compact data layouts, deterministic algorithms, bounded collections, file safety, binary persistence, and export primitives.
- **LabyMake** is the headless designer core. It owns editable project state, tools, undo/redo, validation, preview, persistence, publishing, autosave, and live-debug transport.
- **Labyrinth** is the Unity bridge and game runtime. It imports designer output, creates schema-backed assets, and runs gameplay through pooled entities and centralized update loops.

Dependency flow should remain one-way: the designer and game may consume backend contracts; the backend must not depend on either consumer. The designer communicates with the game only through published artifacts: live debug drops the PNG and `.efyvlaby` files into the game's `Assets/RawArt` directory, where Unity's asset import runs `EFYVPixelArtImporter` and the Play Mode bridge refreshes affected scene objects. There is no runtime type coupling or notification channel.

## Documentation Navigation

Every component uses the same documentation pattern:

1. Its component README explains role, system boundaries, and major areas.
2. A directory README explains only that directory's ownership and immediate children.
3. Leaf READMEs document concrete files, invariants, and local extension points.
4. Every nested README links back to its parent so readers can move through the tree without searching.

## Verify Everything

The backend and designer suites target `net8.0`; the game suite targets `net10.0`, so full verification needs both the .NET 8 and .NET 10 SDKs. Run from this repository root:

```powershell
dotnet run --project EFYV-labybackend\Tests\EFYVBackend.Verification.csproj -c Release
dotnet run --project EFYV-labymake\Tests\EFYVLabyMake.Verification.csproj -c Release
dotnet run --project EFYV-labyrinth\Tests\EFYVGame.Verification.csproj -c Release
```

The game component also has Python contract tests:

```powershell
python -m unittest discover -s EFYV-labyrinth\Tests -p "test_*.py" -v
```

The desktop editor app builds warning-free with `dotnet build EFYV-labymake\App\EFYVLabyMake.App.csproj -c Release`, and the Unity project at `EFYV-labyrinth` opens in Unity `6000.6.0b4` (pinned in `ProjectSettings/ProjectVersion.txt`).

## Repository Automation

Workflows are defined under [.github/workflows](.github/workflows/README.md): `ci.yml` runs all four verification suites on every push and pull request, and the tagged Steam deployment workflow treats `EFYV-labyrinth` as the Unity project path while running from this repository root.
