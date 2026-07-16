# Persistence

[Core](../README.md) | [Repository](../../README.md) | [Models](../Models/README.md) | [IO](../IO/README.md)

This directory owns editable project storage and autosave scheduling. Runtime `.efyvlaby` publication belongs to [Export](../Export/README.md).

## Project files

[`ProjectPersistenceService`](ProjectPersistenceService.cs) reads and writes versioned `.efyvmake` JSON. Internal document DTOs represent animations, frames, layers, and hitboxes; layer RGBA bytes serialize as compact base64. `.efyvmake.autosave` uses the same document contract.

Before allocation or commit, document validation enforces:

- a known exact schema type and bounded canvas/atlas dimensions;
- animation, frame, and layer count limits with positive FPS;
- exact `width * height * 4` layer payloads and finite opacity;
- unique, finite, nonnegative, in-canvas hitboxes;
- configured maximum project file size.

`ProjectPersistenceSnapshot.Capture` deep-copies the document graph before background save work. Saves write a sibling temporary file, flush it to disk, check cancellation and size again, then call `ExportEngine.AtomicReplace`. A canceled or failed pre-commit save leaves the prior destination intact and cleans the temporary file.

## Autosave

[`AutosaveController`](AutosaveController.cs) exposes idle, scheduled, saving, succeeded, failed, and cancelled snapshots. Scheduling a newer request cancels the older one. The snapshot factory runs only after the debounce window, avoiding repeated project cloning during rapid edits; an optional synchronization context keeps capture on the host thread. `SaveNowAsync` uses the same pipeline with zero delay.

[`DesignerSession`](../Logic/DesignerSession.cs) schedules autosave for dirty changes, deletes autosave after a fully current manual save, and can reload either the committed project or preferred autosave.

