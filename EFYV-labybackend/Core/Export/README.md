# Export

[Core README](../README.md) | [IO contracts](../IO/README.md) | [Shared models](../Models/README.md)

Publishes pixel atlases and matching EFYV metadata for the editor-to-game live
asset path.

## Important files

- [FastExporter.cs](FastExporter.cs) validates names, dimensions, atlas metadata,
  animation ranges, and pixel element size; packs frames; writes temporary files;
  flushes them; then publishes the final PNG and metadata paths.
- [FastPngEncoder.cs](FastPngEncoder.cs) is an internal deterministic RGBA PNG
  writer using stored DEFLATE blocks, CRCs, Adler-32, and pooled scratch storage.
- [SafePathPolicy.cs](../IO/SafePathPolicy.cs) provides file-stem and containment
  enforcement used for all exporter-created names.

## Contracts and limits

- Export pixels must have one 32-bit unmanaged element per atlas pixel and the
  checked length must equal `width * height`.
- Optional metadata must match atlas dimensions and format version. Frame sizes
  divide the atlas; animations are named, positive-rate, ordered,
  non-overlapping, and within frame capacity. Gaps are allowed.
- Entity name, then asset name, then `<asset-type>_Export` determines the output
  stem. Reserved device names, separators, traversal, and unsafe trailing
  characters are rejected.
- Failures before publication remove temporary files and preserve prior outputs.
  The final PNG and metadata moves are separate filesystem operations, so they do
  not form one cross-file transaction if the second move itself fails.
- `PackFramesToAtlas` copies complete rows and permits a larger source buffer, but
  rejects any destination rectangle outside the atlas.

