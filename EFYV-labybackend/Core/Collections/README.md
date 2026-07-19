# Collections

[Core README](../README.md)

Allocation-conscious containers and coordinate structures used by runtime and
editor hot paths.

## Important types

- [FastPool.cs](FastPool.cs): fixed-capacity, lazy object pool with optional
  prewarming and reference-identity ownership checks.
- [FastPoolRegistry.cs](FastPoolRegistry.cs): static integer-keyed registry, scoped
  separately for each generic `T`.
- [FastSwapList.cs](FastSwapList.cs): O(1) swap-and-pop removal for
  `IFastListTrackable` objects.
- [FastGridMap.cs](FastGridMap.cs): checked flat `short[]` tile map plus tracked
  map props, viewport-bound calculations, and bulk editing primitives
  (`TrySetTile`, `FillRect`, `CopyRegion` with memmove overlap semantics,
  scanline `FloodFillTiles`, and overlap-preserving `Resize`).
- [FastRingBufferViewport.cs](FastRingBufferViewport.cs): positive modulo mapping
  and previous-bound change detection.

## Invariants and performance

- A pool never grows past capacity. `Rent` returns `null` ONLY when exhausted; a
  factory returning `null` or a duplicate reference is a programming error and
  throws instead of being conflated with exhaustion. Returned objects must
  originate from that exact pool and cannot be returned twice.
- Pools are mutable and unsynchronized; callers provide any required thread
  ownership. The registry's key map itself is lock-guarded, so concurrent
  registration/lookup cannot corrupt it (first registration wins) - but the
  pools it hands out remain single-threaded.
- Swap-list order is intentionally unstable. Every member's `ActiveListIndex`
  must equal its current slot; removal resets it to the configured unregistered
  sentinel.
- Grid reads outside the map return the empty tile; `SetTile` writes are silently
  ignored for compatibility while `TrySetTile` reports them. `GetVisibleBounds`
  rejects NaN fov values. Direct `RawData` access bypasses those policies.
- A viewport wholly outside the map can produce an empty range (`min > max`);
  consumers must handle that without indexing.

