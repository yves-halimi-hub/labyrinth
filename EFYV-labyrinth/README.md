# EFYV Labyrinth

[EFYV workspace](../README.md) | [Backend](../EFYV-labybackend/README.md) | [LabyMake node](../EFYV-labymake/README.md)

Unity-facing runtime and editor integration for EFYV Labyrinth. This repository consumes the shared backend primitives, imports assets produced by LabyMake, and contains the gameplay bridge code that runs inside Unity.

## Browse

| Area | Purpose |
| --- | --- |
| [Assets](Assets/README.md) | Unity-owned runtime and editor scripts, plus the playable scene |
| [Packages](Packages/) | Unity package manifest, local backend package reference, and the BCL-compat embedded package |
| [ProjectSettings](ProjectSettings/) | Pinned editor version (6000.3.20f1) and project configuration |
| [PixelArtApp](PixelArtApp/README.md) | Standalone mock exporter for the art contract |
| [Tests](Tests/README.md) | Headless game/editor verification and Unity API stubs |
| [Repository automation](../.github/README.md) | Tagged build and Steam deployment automation |

## Opening the project in Unity

This directory is a complete Unity project targeting **Unity 6.3 LTS (6000.3.20f1)**.

1. Install Unity `6000.3.20f1` through Unity Hub (no extra modules required to open it).
2. In Unity Hub choose *Add project from disk* and select this `EFYV-labyrinth` directory (the repo checkout must keep its siblings: the backend is consumed as a local package via `Packages/manifest.json` -> `file:../../EFYV-labybackend/Core`).
3. Open `Assets/Scenes/Labyrinth.unity` and press Play. `GameBootstrap` generates deterministic placeholder sprites, a tile palette, and a schema-backed stats block for the inactive `EnemyTemplate`, so the scene is playable without any binary art: WASD/arrows move the player, `SpawnManager` clones the template through `PoolManager`, and enemies chase and damage the player.

Layout notes:

- Scripts compile into three assemblies: `EFYVBackend.Core` (the backend local package, engine-neutral, unsafe allowed), `EFYV.Game` (`Assets/Scripts`), and `EFYV.Game.Editor` (`Assets/Scripts/Editor`, editor-only).
- `Packages/com.efyv.bclcompat` embeds `System.Text.Json` and its dependency closure because the backend uses it and Unity's scripting profile does not ship it. See that package's README before adding or removing assemblies.
- `EFYV-labybackend/Core/csc.rsp` raises only the backend assembly to C# 10 (`HitboxData` declares a parameterless struct constructor); Unity's default language level is C# 9.
- Hand-authored `.meta` GUIDs follow `md5("efyv:" + repo-relative path)`; `Tests/test_unity_project_assets.py` statically validates the project (metas, GUID cross-references, scene wiring, settings) on every test run.

## Data Flow

1. The declaration-driven LabyMake node exports a PNG and versioned `.efyvlaby` metadata file through an explicit browser download or folder handoff.
2. `EFYVPixelArtImporter` configures the texture and creates or refreshes a schema-backed `ScriptableObject`.
3. Runtime entities load the compact schema block and retain Unity-only sprite, atlas, and hitbox references.
4. During Play Mode, the game-owned refresh bridge can reload files after the user explicitly hands them to the Unity project; the Maker node never writes a host path in the background.

## Runtime Rules

- Per-object Unity `Update` calls are avoided for enemies, projectiles, and animated props; managers iterate packed lists.
- Pool ownership is represented by `prefabPoolKey`; spawned objects must pair `OnSpawn` with `OnDespawn`.
- Numeric runtime data lives in backend `FastSchemaBlock` wrappers. Unity assets hold serialized block bytes and visual references.
- Editor-only code stays under `Assets/Scripts/Editor`; external verification stays outside `Assets`.

## Verification

```powershell
dotnet run --project Tests\EFYVGame.Verification.csproj -c Release
python -m unittest discover -s Tests -p "test_*.py" -v
```

The headless suite validates C# behavior without entering a player build. Real Unity serialization, physics, GPU rendering, and AssetDatabase callbacks still require a complete Unity project and Unity Test Framework execution.
