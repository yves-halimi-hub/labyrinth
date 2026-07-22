# Memory

[Core README](../README.md) | [Math algorithms](../Math/README.md) | [Pixel constants](../Data/README.md)

Low-allocation operations over unmanaged arrays and packed RGBA buffers.

## Important files

- [FastMemory.cs](FastMemory.cs) implements bulk copy/clear, source-over alpha
  blending, layer flattening, unchecked 2D access, nearest-neighbor scaling, and
  clipped stamp blitting.
- [FastEffects.cs](FastEffects.cs) implements separable box blur with either an
  `ArrayPool<uint>` scratch buffer or a caller-provided scratch pointer. Color
  channels accumulate alpha-premultiplied and are un-premultiplied once when
  packing the destination, so fully transparent neighbors contribute no color
  and sprite edges fade in alpha without darkening toward halo colors; a
  zero-alpha average packs as exactly zero. It also carries these filter
  primitives: `Outline` (1px 8-neighborhood expansion of the alpha>0
  silhouette in a chosen color; silhouette pixels are never recolored),
  `Glow` (silhouette+rim flooded with the glow color, box-blurred by radius —
  0 keeps a hard rim — then the source alpha-composited back on top), and
  `ColorShift` (per-pixel HSV: hue delta wraps degrees, saturation/value
  deltas clamp to [0,1]; alpha preserved, fully transparent pixels copied
  bit-exact). All three tolerate `src == dest`.

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

