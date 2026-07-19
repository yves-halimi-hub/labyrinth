# EFYV LabyMake

[Workspace overview](../README.md) | [Shared backend](../EFYV-labybackend/) | [Unity consumer](../EFYV-labyrinth/)

EFYV LabyMake is the pixel-art authoring component for the EFYV Labyrinth project. The headless core models editable assets, validates their gameplay metadata, provides drawing and procedural tools, persists designer projects, previews animation, and publishes PNG atlas plus `.efyvlaby` metadata pairs for Unity. The desktop editor shell in [`App/`](App/README.md) hosts that core by binding to [`DesignerSession`](Core/Logic/DesignerSession.cs); the core itself stays UI-free.

## Three-component role

1. [`EFYV-labybackend`](../EFYV-labybackend/) owns shared schemas, packed data types, math/memory primitives, PNG/JSON serialization, and the unified [`EFYVLabyrinthConfig`](../EFYV-labybackend/Core/Data/EFYV-LabyrinthConfig.cs).
2. **EFYV-labymake** owns authoring state and workflows. It compiles directly against backend source through the verification project.
3. [`EFYV-labyrinth`](../EFYV-labyrinth/) consumes live output in Unity. Its [`EFYVPixelArtImporter`](../EFYV-labyrinth/Assets/Scripts/Editor/EFYVPixelArtImporter.cs) imports the metadata, sibling PNG, sprites, hitboxes, animation ranges, and directional variants.

There is no compile-time Unity dependency in LabyMake. The file contract is the integration boundary.

## Authoring flow

1. `AssetSchemaService` and `ToolbarAPI` create a typed `EFYVProject` from the shared asset manifest.
2. `DesignerSession` coordinates the selected frame, active tool, sparse undo/redo history, validation, preview, autosave, and live-debug state.
3. A pointer down/drag/up sequence is one reversible edit. Structural and inspector changes are commands as well.
4. Project saves use versioned `.efyvmake` JSON. Autosaves use `.efyvmake.autosave`.
5. Live debug debounces changes, validates the latest project, captures an immutable snapshot, and exports a near-square grid PNG atlas plus authoritative `.efyvlaby` metadata into Unity's `Assets/RawArt` directory.

## Layout

- [`App/`](App/README.md): Avalonia 11 desktop editor shell hosting the core (new-project flow, editable canvas, tool strip, zoom/pan, undo/redo/save, status bar).
- [`Core/`](Core/README.md): production authoring core.
- [`Core/Models/`](Core/Models/README.md): mutable design aggregates and immutable export/preview snapshots.
- [`Core/Logic/`](Core/Logic/README.md): session orchestration, schemas, validation, history, preview, and live debug.
- [`Core/Tools/`](Core/Tools/README.md): pointer tools and procedural map/animation controls.
- [`Core/Persistence/`](Core/Persistence/README.md): project files and debounced autosave.
- [`Core/Export/`](Core/Export/README.md): atlas construction and staged Unity publication.
- [`Core/IO/`](Core/IO/README.md): contained-path policy used by storage code.
- [`Tests/`](Tests/README.md): dependency-free executable verification harness.

## Build and verification

Prerequisites are the .NET 8 SDK and the sibling backend at `../EFYV-labybackend`. Both the verification project and the editor app link all LabyMake and backend sources directly; there is no separately packaged core library.

```powershell
dotnet build Tests/EFYVLabyMake.Verification.csproj --configuration Debug
dotnet run --project Tests/EFYVLabyMake.Verification.csproj --configuration Debug
dotnet run --project App/EFYVLabyMake.App.csproj --configuration Release
```

Run commands from this repository root. The test executable covers model isolation, exact raster behavior, schema and validation matrices, bounded history, persistence corruption, publication rollback, and asynchronous autosave/live-debug lifecycles.

## Boundaries

- Canvas, schema, history, persistence, and atlas limits come from the shared config; do not duplicate them locally.
- `MapTool` currently edits an in-memory backend grid. Map state is not part of `.efyvmake` persistence or `.efyvlaby` export.
- Live debug republishes the PNG for metadata-only edits; semantic dirty scopes and content-hash suppression are not implemented.
- A UI host is responsible for widgets, input routing, dialogs, keyboard bindings, and rendering session state. [`App/`](App/README.md) is that host; `Core/` must never reference it.
