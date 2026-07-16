# EFYV Labyrinth System

[EFYV Games](../../README.md) / [Game repositories](../README.md) / Labyrinth

This monorepo contains the three components that build the Labyrinth game, designer, and shared runtime foundation. Their existing directory names are preserved so source links and project references remain stable.

## Choose Where To Start

| I want to understand or change... | Start here |
| --- | --- |
| Shared schemas, memory, math, collections, export, or save primitives | [EFYV LabyBackend](EFYV-labybackend/README.md) |
| The asset designer, drawing tools, history, validation, persistence, export, autosave, or live debug | [EFYV LabyMake](EFYV-labymake/README.md) |
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

Dependency flow should remain one-way: the designer and game may consume backend contracts; the backend must not depend on either consumer. The designer communicates with the game through published artifacts and live-debug notifications rather than runtime type coupling.

## Documentation Navigation

Every component uses the same documentation pattern:

1. Its component README explains role, system boundaries, and major areas.
2. A directory README explains only that directory's ownership and immediate children.
3. Leaf READMEs document concrete files, invariants, and local extension points.
4. Every nested README links back to its parent so readers can move through the tree without searching.

## Verify Everything

Run from this repository root:

```powershell
dotnet run --project EFYV-labybackend\Tests\EFYVBackend.Verification.csproj -c Release
dotnet run --project EFYV-labymake\Tests\EFYVLabyMake.Verification.csproj -c Release
dotnet run --project EFYV-labyrinth\Tests\EFYVGame.Verification.csproj -c Release
```

The game component also has Python contract tests:

```powershell
python -m unittest discover -s EFYV-labyrinth\Tests -p "test_*.py" -v
```

## Repository Automation

Tagged Unity builds and Steam publication are defined under [.github/workflows](.github/workflows/README.md). The workflow treats `EFYV-labyrinth` as the Unity project path while running from this repository root.
