# Export

[Core](../README.md) | [Repository](../../README.md) | [Models](../Models/README.md) | [Logic](../Logic/README.md)

[`ExportEngine`](ExportEngine.cs) converts a validated project snapshot into the live Unity file pair. It delegates PNG and JSON encoding to the backend [`FastExporter`](../../../EFYV-labybackend/Core/Export/FastExporter.cs); Unity consumes the result through [`EFYVPixelArtImporter`](../../../EFYV-labyrinth/Assets/Scripts/Editor/EFYVPixelArtImporter.cs).

## Pipeline

1. `Export(EFYVProject, ...)` runs `ProjectValidator` with `ProjectValidationScope.Export`, then captures an immutable `ProjectSnapshot`.
2. Frames are flattened in animation order and packed left-to-right into one horizontal RGBA atlas.
3. Hitboxes receive global frame indices. Atlas metadata records format version, dimensions, and each animation's name, FPS, start frame, and frame count.
4. Backend export writes identity-named staged files outside `Assets`.
5. LabyMake publishes to `<UnityProject>/Assets/RawArt`, adding `_Up`, `_Down`, `_Left`, or `_Right` to directional file stems.

## Publication contract

- The image is published first; `.efyvlaby` metadata is authoritative and published last.
- Existing destination files move to backup paths in the staging directory. If metadata publication fails, `PublishPair` restores the previous image and metadata state.
- Temporary and backup files are deleted after completion. The staging directory is outside Unity's import tree.
- `AtomicReplace` also supplies the single-file replace primitive used by [persistence](../Persistence/README.md) and the asset bank.

`ExportResult` reports final paths, frame/hitbox counts, and atlas dimensions. Export does not persist editable layers; the PNG contains composited frames, while `.efyvlaby` contains runtime metadata.

