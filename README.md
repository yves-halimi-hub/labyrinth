# EFYV Labyrinth System

[EFYV Games](../../README.md) / [Game repositories](../README.md) / Labyrinth

This repository contains the Labyrinth game, its shared runtime foundation, and the declaration-driven
LabyMake EFYV node.

## Choose Where To Start

| I want to understand or change... | Start here |
| --- | --- |
| Shared schemas, memory, math, collections, export, or save primitives | [EFYV LabyBackend](EFYV-labybackend/README.md) |
| The asset designer, drawing tools, history, persistence, jobs, or browser handoff | [LabyMake node](EFYV-labymake/README.md) |
| LabyMake validation and deterministic Unity artifact export | [`labymake-engine`](EFYV-labymake/services/labymake-engine/) |
| Unity gameplay, entities, managers, weapons, pooling, import, or editor integration | [EFYV Labyrinth](EFYV-labyrinth/README.md) |
| Existing game verification | [Backend tests](EFYV-labybackend/Tests/README.md) and [game/editor tests](EFYV-labyrinth/Tests/README.md) |

## System Shape

```text
EFYV-labymake/app/app.efyv --> EFYV Platform --> labymake-engine --> PNG + .efyvlaby + ZIP
                                                        |
                                                        v
                                                EFYV-labyrinth
```

- **LabyBackend** is the dependency foundation. It owns compact data layouts, deterministic algorithms, bounded collections, file safety, binary persistence, and export primitives.
- **LabyMake** is an ordinary EFYV Maker node. EFYV Platform owns editable state, tools, history,
  persistence, jobs, artifacts, and browser-folder handoff; `labymake-engine` owns only bounded,
  stateless domain validation and deterministic export.
- **Labyrinth** is the Unity bridge and game runtime. It imports designer output, creates schema-backed assets, and runs gameplay through pooled entities and centralized update loops.

The Maker communicates with the game only through explicit user-downloaded or browser-folder artifacts.
There is no desktop bridge, guessed host path, live filesystem watcher contract, or runtime type
coupling between the EFYV node and the game.

## Documentation Navigation

Every component uses the same documentation pattern:

1. Its component README explains role, system boundaries, and major areas.
2. A directory README explains only that directory's ownership and immediate children.
3. Leaf READMEs document concrete files, invariants, and local extension points.
4. Every nested README links back to its parent so readers can move through the tree without searching.

## Verify Everything

The backend suite targets `net8.0`; the game suite targets `net10.0`. The new `labymake-engine`
targets `net8.0`. Full verification needs both SDKs. Run from this repository root:

```powershell
dotnet run --project EFYV-labybackend\Tests\EFYVBackend.Verification.csproj -c Release
dotnet build EFYV-labymake\services\labymake-engine\LabyMakeEngine.csproj -c Release
dotnet run --project EFYV-labyrinth\Tests\EFYVGame.Verification.csproj -c Release
```

The game component also has Python contract tests:

```powershell
python -m unittest discover -s EFYV-labyrinth\Tests -p "test_*.py" -v
```

The retired Avalonia/session designer was deleted. The Unity project at `EFYV-labyrinth` opens in
Unity `6000.6.0b4` (pinned in `ProjectSettings/ProjectVersion.txt`).

## Repository Automation

Workflows are defined under [.github/workflows](.github/workflows/README.md): `ci.yml` runs all four verification suites on every push and pull request, and the tagged Steam deployment workflow treats `EFYV-labyrinth` as the Unity project path while running from this repository root.
