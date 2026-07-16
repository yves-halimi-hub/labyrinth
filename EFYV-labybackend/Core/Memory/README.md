# Memory

[Core README](../README.md) | [Math algorithms](../Math/README.md) | [Pixel constants](../Data/README.md)

Low-allocation operations over unmanaged arrays and packed RGBA buffers.

## Important files

- [FastMemory.cs](FastMemory.cs) implements bulk copy/clear, source-over alpha
  blending, layer flattening, unchecked 2D access, nearest-neighbor scaling, and
  clipped stamp blitting.
- [FastEffects.cs](FastEffects.cs) implements separable box blur with either an
  `ArrayPool<uint>` scratch buffer or a caller-provided scratch pointer.

## Safety and representation

- Packed RGBA uses red at bits 0-7, green at 8-15, blue at 16-23, and alpha at
  24-31. Changing these shifts breaks export and blend compatibility.
- Public pointer operations validate nulls, dimensions, and finite parameters,
  but callers must guarantee that the pointed storage covers every addressed
  pixel.
- `Read2DArrayUnsafe` and `Write2DArrayUnsafe` intentionally perform no bounds
  checks. Use them only after proving `0 <= x < width` and a valid row range.
- `FastMemory.Copy` requires destination capacity at least equal to source length.
  Blits preserve their inputs and clip destination coordinates.
- The caller-provided blur scratch buffer cannot alias source or destination and
  must hold `width * height` pixels. The pooled overload returns scratch in a
  `finally` block.

