# Math

[Core README](../README.md) | [Memory primitives](../Memory/README.md)

Engine-neutral scalar, raster, deformation, procedural, and random algorithms.

## Important files

- [FastMath.cs](FastMath.cs): clamps, wrapping, squared distance, normalization,
  deterministic hashing, direction selection, and periodic trig approximations.
  `FastCeilToInt` has defined full-range behavior (NaN -> 0, out-of-range clamps
  to `int.MinValue`/`int.MaxValue`); `FastNormalize` rescales by the largest
  component when the squared magnitude would overflow or underflow, so huge and
  tiny finite vectors normalize correctly (exact zero stays zero).
- [FastRandom.cs](FastRandom.cs): `FastRandomState`, an instance xorshift32 PRNG
  struct (isolated deterministic streams for tools/generators), plus the static
  `FastRandom` facade delegating to one shared state with historical seed and
  sequence compatibility. Numeric ranges (64-bit span, no wide-range collapse),
  a Lemire-reduced `RangeUnbiased` (not sequence-compatible with `Range`), and
  uniform rejection sampling inside the unit disk.
- [Algorithms.cs](Algorithms.cs): clipped Bresenham lines, thick square/circle
  brushes, bounded flood fill, and the shape-tool rasterizers over caller-owned
  RGBA canvases: `DrawRectangle` (filled or an inside border band) and
  `DrawEllipse` (filled, or a gap-free morphological outline — an inside pixel
  is on the ring when the thickness-radius square around it is not fully inside,
  checked via the four corners by convexity; degenerate zero-radius boxes
  degrade to their filled bounding box). Off-canvas anchors are legal and cost
  at most one canvas sweep.
- [FastProceduralGen.cs](FastProceduralGen.cs): cellular-automata smoothing with a
  separate work buffer and a selectable `CellularBorderRule` (edge-as-target
  default, edge-as-empty optional), plus two deterministic tile generators over
  caller buffers: `GenerateMazeRecursiveBacktracker` (perfect maze) and
  `GenerateRoomsAndCorridors` (non-overlapping rooms joined by L-corridors).
- [FastDeformation.cs](FastDeformation.cs): frame generation over raw pixel
  buffers — toon walk, multi-wave radial jitter, and the item #10 presets:
  `GenerateBobBreatheFrame` (sine bob translation plus bottom-anchored breathe
  squash; the breathe amplitude is capped by
  `Deformation.MaxBreatheAmplitude` so the scale never reaches zero, and zero
  amplitudes copy verbatim) and `GenerateShakeFlashFrame` (decaying horizontal
  shake over `Deformation.ShakeOscillations` cycles plus a white hit-flash
  that is strongest at `timeT = 0`; alpha is preserved and alpha-0 pixels pass
  through untouched).

## Invariants and performance

- Pointer APIs require valid storage for the declared dimensions. Dimensions and
  finite scalar inputs are validated, but the runtime cannot infer pointer length.
- Source and destination buffers should be distinct where an algorithm performs a
  transform; procedural smoothing and the tile generators explicitly reject an
  aliased work buffer.
- Approximate trig and normalization are chosen for speed and bounded game/editor
  use, not bit-for-bit parity with `System.Math` at every magnitude.
  `Get4WayDirection` resolves special values (signed zero, NaN, infinities) by
  raw bit inspection; the exact semantics are documented at the method and pinned
  by the verification suite as a deliberate contract.
- `FastRandom` owns one static mutable state. Seeding makes a sequence
  deterministic, but concurrent callers require external synchronization or a
  private `FastRandomState` instance (copying the struct forks the stream).
- Jitter-frame generation validates the post-multiplication wave phase and the
  final per-octant offsets, so non-finite parameters throw instead of silently
  wiping the destination frame.

