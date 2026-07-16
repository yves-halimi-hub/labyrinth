# LabyMake Core

[Repository](../README.md)

`Core` is the UI-independent authoring library. It depends on shared backend source but does not reference Unity or a desktop framework. The primary host boundary is [`DesignerSession`](Logic/DesignerSession.cs), which composes models, tools, validation, history, persistence, preview, autosave, and live export.

## Runtime flow

1. [`AssetSchemaService`](Logic/AssetSchemaService.cs) loads the shared base and concrete asset definitions; [`ToolbarAPI`](Logic/ToolbarAPI.cs) exposes categories and typed inspector fields.
2. A host creates an [`EFYVProject`](Models/EFYVProject.cs) and routes pointer input or structural commands through `DesignerSession`.
3. [`ProjectValidator`](Logic/ProjectValidator.cs) produces structured issues for the designer and stricter export-path checks for live publication.
4. [`ProjectPersistenceService`](Persistence/ProjectPersistenceService.cs) saves editable state, while immutable [`ProjectSnapshot`](Models/ProjectSnapshot.cs) instances isolate preview and export work.
5. [`ExportEngine`](Export/ExportEngine.cs) publishes the validated atlas/metadata contract consumed by Unity.

## Directories

- [`Models/`](Models/README.md): project, animation, frame, layer, sub-element, and snapshot data.
- [`Logic/`](Logic/README.md): orchestration and application services.
- [`Tools/`](Tools/README.md): pointer and procedural authoring operations.
- [`Persistence/`](Persistence/README.md): `.efyvmake` and autosave lifecycle.
- [`Export/`](Export/README.md): immutable atlas export and publication rollback.
- [`IO/`](IO/README.md): shared safe-path adapter.

## Core invariants

- Mutable designer objects stay on the authoring side; background preview/export operations consume snapshots.
- A complete pointer gesture is one history transaction and rolls back on cancellation or tool failure.
- Pixel storage is contiguous RGBA32; frame flattening applies visibility and finite, clamped layer opacity.
- Schema, path, canvas, hitbox, animation, layer, and atlas constraints are validated before persistence or export allocations.
- Configuration and binary contracts come from [`EFYV-LabyrinthConfig.cs`](../../EFYV-labybackend/Core/Data/EFYV-LabyrinthConfig.cs).

