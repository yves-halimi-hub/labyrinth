# IO

[Core README](../README.md) | [Export](../Export/README.md) | [Data layout](../Data/README.md)

Filesystem boundaries for EFYV metadata, profile persistence, and safe exporter
paths.

## Important files

- [FastImporter.cs](FastImporter.cs) streams an existing metadata file through
  `System.Text.Json` into `EFYVJsonFormat`.
- [FastSaveEngine.cs](FastSaveEngine.cs) writes and reads the exact unmanaged
  `PlayerMetaSchema` memory image.
- [SafePathPolicy.cs](SafePathPolicy.cs) rejects unsafe stems and resolves a single
  file directly inside a normalized root directory.

## Behavior and safety

- A missing importer path returns a default `EFYVJsonFormat`; malformed JSON and
  access failures propagate their exceptions. Parsing never rewrites the source.
- Saving uses `FileMode.Create`, so a prior longer save is truncated. Loading a
  short file returns `false` and resets the output to `PlayerMetaSchema.Default()`.
- The raw save format has no version, checksum, length header, or authenticity
  marker. Full-size arbitrary bytes are accepted and trailing bytes are ignored.
  Add a migration envelope before changing `PlayerMetaSchema` layout.
- `SafePathPolicy` prevents generated filenames from escaping one root directory;
  it is not automatically applied to the general-purpose importer or save path.

