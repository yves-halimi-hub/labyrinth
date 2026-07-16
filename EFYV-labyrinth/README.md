# EFYV Labyrinth

[EFYV workspace](../README.md) | [Backend](../EFYV-labybackend/README.md) | [Designer](../EFYV-labymake/README.md)

Unity-facing runtime and editor integration for EFYV Labyrinth. This repository consumes the shared backend primitives, imports assets produced by LabyMake, and contains the gameplay bridge code that runs inside Unity.

## Browse

| Area | Purpose |
| --- | --- |
| [Assets](Assets/README.md) | Unity-owned runtime and editor scripts |
| [PixelArtApp](PixelArtApp/README.md) | Standalone mock exporter for the art contract |
| [Tests](Tests/README.md) | Headless game/editor verification and Unity API stubs |
| [Repository automation](../.github/README.md) | Tagged build and Steam deployment automation |

## Data Flow

1. LabyMake publishes a PNG and versioned `.efyvlaby` metadata file.
2. `EFYVPixelArtImporter` configures the texture and creates or refreshes a schema-backed `ScriptableObject`.
3. Runtime entities load the compact schema block and retain Unity-only sprite, atlas, and hitbox references.
4. During Play Mode, the live-debug bridge refreshes scene objects that use the changed asset.

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
