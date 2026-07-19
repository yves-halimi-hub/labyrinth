# IO

[Core README](../README.md) | [Export](../Export/README.md) | [Data layout](../Data/README.md)

Filesystem boundaries for EFYV metadata, profile persistence, and safe exporter
paths.

## Important files

- [FastImporter.cs](FastImporter.cs) streams an existing metadata file through
  `System.Text.Json` into `EFYVJsonFormat`. `TryParse` returns the tri-state
  `EfyvParseResult` (Missing / Malformed / Valid); `ParseEfyvFile` is the thin
  historical wrapper over the same read path.
- [FastSaveEngine.cs](FastSaveEngine.cs) persists the unmanaged
  `PlayerMetaSchema` image inside a versioned envelope: a
  `{magic, version, CRC32-of-payload}` little-endian header
  (`EFYVLabyrinthConfig.Backend.Save`) followed by the raw struct bytes.
  Writes stage to a dotted temporary sibling and land via
  `File.Replace`/`File.Move` through the bounded retry.
- [FastMapFile.cs](FastMapFile.cs) (item #5) is the versioned binary map
  container (`.efyvmap`): `FastMapExporter` writes `MapFileData` (dimensions,
  tileset reference, row-major int16 tile ids, prop placements) behind the
  same `{magic "EFYM", version, CRC32-of-payload}` little-endian envelope the
  save engine uses, staged to a dotted temporary and published through the
  bounded retry; `FastMapImporter.TryParse` is the tri-state reader
  (`EfyvParseResult`), strict about caps
  (`EFYVLabyrinthConfig.Backend.MapFile`), safe-stem asset keys, finite
  scales, and EXACT payload consumption (trailing bytes are `Malformed`).
  The shared `FastMapExporter.TryValidate` gate is what the writer throws on
  and the reader classifies with.
- [FastCrc32.cs](FastCrc32.cs) is the ONE shared CRC-32 table (PNG polynomial)
  in the backend, used by the save envelope and by the
  `FastPngEncoder`/`FastPngDecoder` chunk checksums.
- [FastIoRetry.cs](FastIoRetry.cs) is the bounded `IOException` retry (3
  attempts, 20-50ms backoff, `EFYVLabyrinthConfig.Backend.IO.PublishRetry*`)
  wrapped around every atomic publish/replace call site.
- [SafePathPolicy.cs](SafePathPolicy.cs) rejects unsafe stems and resolves a single
  file directly inside a normalized root directory.

## Behavior and safety

- A missing importer path returns a default `EFYVJsonFormat` (`TryParse`:
  `Missing`); malformed JSON throws from `ParseEfyvFile` and classifies as
  `Malformed` from `TryParse`; I/O failures propagate from both. Parsing never
  rewrites the source.
- Saving is atomic: a crash mid-write can never truncate the live save file.
  Loading rejects anything that is not exactly header + payload with a matching
  magic, version, and payload CRC - truncated, oversized, legacy header-less,
  corrupted, or version-mismatched files return `false` and reset the output to
  `PlayerMetaSchema.Default()`.
- Save destinations are routed through `SafePathPolicy.GetContainedPath`, so a
  crafted file name cannot escape its directory.
- `IsSafeFileStem` caps stems at 128 characters
  (`EFYVLabyrinthConfig.Backend.IO.MaxFileStemLength`; headroom for facing
  suffixes, extensions, and dotted temporary names under the common
  255-character filesystem component limit) and rejects the full
  Windows-invalid character set
  plus ASCII control characters on every platform, so a stem accepted on Linux
  cannot fail or alias once the export lands in a Windows Unity project.

