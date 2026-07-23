# EFYV Runtime Media

[Back to shared packages](../README.md)

This engine-neutral package owns the released RGBA packing/blending, atlas-layout,
PNG and ISO-HDLC CRC behavior shared by LabyBackend and LabyMake. Managed code is
always available. `RuntimeMediaKernel.TryEnableNativeV1()` may opt into the external
`efyv_runtime_kernel` library implementing the canonical
`EFYV-runtime-kernel/include/efyv/runtime_kernel.h`; any
missing library, ABI mismatch, symbol failure, or operation error falls back to the
bit-compatible managed batch implementation.

Cancellation and ownership stay on the managed side; the adapter calls the canonical
`efyv_runtime_v1_rgba_blend_batch` and `efyv_runtime_v1_crc32` operations. It never
publishes a competing C header or exposes
Labyrinth models or Unity objects across the ABI.
