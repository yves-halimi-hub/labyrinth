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
  map props and viewport-bound calculations.
- [FastRingBufferViewport.cs](FastRingBufferViewport.cs): positive modulo mapping
  and previous-bound change detection.

## Invariants and performance

- A pool never grows past capacity. `Rent` returns `null` when exhausted; returned
  objects must originate from that exact pool and cannot be returned twice.
- Pool and registry classes are mutable and unsynchronized. Coordinate access is
  O(1), but callers provide any required thread ownership.
- Swap-list order is intentionally unstable. Every member's `ActiveListIndex`
  must equal its current slot; removal resets it to the configured unregistered
  sentinel.
- Grid reads outside the map return the empty tile and writes are ignored. Direct
  `RawData` access bypasses that policy.
- A viewport wholly outside the map can produce an empty range (`min > max`);
  consumers must handle that without indexing.

