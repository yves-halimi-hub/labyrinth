# Math

[Core README](../README.md) | [Memory primitives](../Memory/README.md)

Engine-neutral scalar, raster, deformation, procedural, and random algorithms.

## Important files

- [FastMath.cs](FastMath.cs): clamps, wrapping, squared distance, normalization,
  deterministic hashing, direction selection, and periodic trig approximations.
- [FastRandom.cs](FastRandom.cs): seedable xorshift32 generator, numeric ranges,
  and uniform rejection sampling inside the unit disk.
- [Algorithms.cs](Algorithms.cs): clipped Bresenham lines, thick square/circle
  brushes, and bounded flood fill over caller-owned RGBA canvases.
- [FastProceduralGen.cs](FastProceduralGen.cs): cellular-automata smoothing with a
  separate work buffer and edge-as-target behavior.
- [FastDeformation.cs](FastDeformation.cs): walk and multi-wave jitter frame
  generation over raw pixel buffers.

## Invariants and performance

- Pointer APIs require valid storage for the declared dimensions. Dimensions and
  finite scalar inputs are validated, but the runtime cannot infer pointer length.
- Source and destination buffers should be distinct where an algorithm performs a
  transform; procedural smoothing explicitly rejects an aliased work buffer.
- Approximate trig and normalization are chosen for speed and bounded game/editor
  use, not bit-for-bit parity with `System.Math` at every magnitude.
- `FastRandom` owns one static mutable state. Seeding makes a sequence
  deterministic, but concurrent callers require external synchronization or
  separate generator state.

