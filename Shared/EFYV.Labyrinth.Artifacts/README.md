# EFYV Labyrinth Artifacts

[Back to shared packages](../README.md)

This domain package is the only LabyMake snapshot-to-Unity handoff translator.
It parses each snapshot once, flattens RGBA layers through
[`EFYV.Runtime.Media`](../EFYV.Runtime.Media/), constructs Labyrinth metadata,
and delegates PNG/metadata production to the same `FastExporter` contract used
by the game backend. Effects, attachments, tilesets, filenames, optional fields,
pixel rounding, atlas limits, and document versions therefore have one behavior.

The package remains Labyrinth-owned: generic Maker persistence, jobs and artifact
retention belong to EFYV Platform, while Unity import and gameplay stay in the
game project.

The released limits are centralized in `LabyrinthArtifactLimits`: 16 MiB request snapshots,
64 MiB aggregate decoded frames, 25 MiB bundles, 64 KiB streaming chunks, two active operations,
and eight queued operations. `LabyMakeSnapshotParser` rejects duplicates and malformed bounds while
honoring cancellation; `LabyMakeArtifactExporter` writes deterministic ZIP entries and a SHA-256
artifact identity.

The [v2 transfer protocol](Protocol/README.md) is colocated with the domain package so other clients
can generate bindings without importing the web-service host.
