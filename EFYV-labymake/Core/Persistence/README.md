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
- configured maximum project file size;
- when the optional palette section is present: bounded palette/swatch/recent counts and valid palette names;
- item #10 timing fields: per-frame `durationMs` in `[0 .. MaxFrameDurationMs]` (0 = inherit FPS) and well-formed playback tags (`loopStart >= 0`, `loopEnd >= -1` — stale-but-well-formed ranges are legal; playback clamps);
- item #7 effect descriptors (optional per-animation `effects` section): bounded count (`LabyMake.Effect.MaxEffectsPerAnimation`), known effect type, non-empty bounded trigger/name (name required for `particleHook`), and `durationMs`/`strength` inside the shared wire caps — restore routes through the `EffectDescriptor` constructor (the single validation gate) and translates its failures into `InvalidDataException`;
- item #6 sub-element attachments (optional per-frame `attachments` section): bounded count (`LabyMake.Attachment.MaxPerFrame`), safe-file-stem `subElementName`, and `zOrder` inside the shared wire bounds — restore routes through the `SubElementAttachment` constructor and translates its failures into `InvalidDataException`;
- item #5 tileset section (optional `tileset`): `tileSize` inside the shared tileset caps, bounded tile count, non-blank bounded tile names, and exact `tileSize² * 4` RGBA payloads per tile — restore routes through the `TilesetTile` constructor;
- item #5 map section (optional `map`): safe-stem `mapId`, dimensions inside `LabyMake.MapDocument.MaxDimension`, an exact `width * height * 2` little-endian int16 `tileBytes` payload, an empty-or-safe-stem `tilesetName`, and a bounded prop list with safe-stem asset keys and finite scales — restore routes through the `MapSection` constructor;
- item #33 directional section (optional `directional`): a canonical `activeFacing` name, NO list for the active facing (its animations ARE the document's main `animations` list, so an older reader opening a directional document still sees the facing the designer last worked on), and a present, bounded list for each of the other three facings — every parked animation is held to the same rules as the main list, and restore routes through the `DirectionalState` constructor. Loading also resyncs the `facing` asset property to the section's active facing, so a hand-edited mismatch cannot survive a load.

The item #8 palette section (`palettes` + `recentColors`, most-recent-first) EXTENDED the document without a format-version bump: both members are optional, a legacy document without them restores to empty palette state, and older readers ignore the extra JSON — so the hard-pinned `formatVersion` equality check keeps accepting every existing `.efyvmake`. The item #10 timing members (`durationMs` per frame; `loopStart`/`loopEnd`/`pingPong` per animation, `loopEnd` nullable so "absent" restores to the -1 full-range sentinel) ride the same pattern: legacy documents restore to inherit-FPS timing and a full-range forward loop. The item #7 per-animation `effects` list rides it too: `null` identifies a pre-effects document and restores to an empty effect list. So does the item #6 per-frame `attachments` list: `null` identifies a pre-attachment document and restores to an empty attachment list. The item #5 `tileset` and `map` sections follow the same rule: `null` identifies a pre-map document and restores to no sections (`EFYVProject.Tileset`/`Map` stay null). The item #33 `directional` section does too: `null` identifies a plain (or pre-directional) document and restores to `EFYVProject.Directional == null`.

`ProjectPersistenceSnapshot.Capture` deep-copies the document graph before background save work. Saves write a sibling temporary file, flush it to disk, check cancellation and size again, then call `ExportEngine.AtomicReplace`. A canceled or failed pre-commit save leaves the prior destination intact and cleans the temporary file.

## Enumerating projects

`ListProjects()` discovers the committed projects in the service directory for an "open existing" browser, returning `ProjectListEntry` (safe project name + last-write UTC) sorted by name (ordinal, ignoring case). It scans only the top level (subdirectories are ignored, never recursed) and holds every candidate to the same `DesignerPathPolicy.IsSafeFileStem` gate the per-name operations enforce, so a listed name always round-trips through `GetProjectPath`. Entries are skipped — not surfaced, never faulted on — when the stem is unsafe (e.g. the empty `.efyvmake` stem), when the file is an autosave sidecar (`.efyvmake.autosave`, whose stem would otherwise collide with its own project), or when a timestamp cannot be read; a missing or unreadable directory lists as empty rather than throwing. The list is a discovery aid only: a returned name can still fail to `LoadProject` if the file is corrupt (the loader stays the validation gate).

## Autosave

[`AutosaveController`](AutosaveController.cs) exposes idle, scheduled, saving, succeeded, failed, and cancelled snapshots. Scheduling a newer request cancels the older one. The snapshot factory runs only after the debounce window, avoiding repeated project cloning during rapid edits; an optional synchronization context keeps capture on the host thread. `SaveNowAsync` uses the same pipeline with zero delay.

[`DesignerSession`](../Logic/DesignerSession.cs) schedules autosave for dirty changes, deletes autosave after a fully current manual save, and can reload either the committed project or preferred autosave.

