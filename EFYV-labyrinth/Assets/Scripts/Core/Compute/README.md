# Gameplay compute bridge

[Back to gameplay core](../README.md)

[`RuntimeGameplayCompute.cs`](RuntimeGameplayCompute.cs) is the only Unity-facing
adapter for the official `Efyv.RuntimeKernel` geometry and spatial batches. It
snapshots the packed enemy list into caller-owned blittable arrays, invokes one
native spatial batch for a weapon group, and maps result indexes back to Unity
objects. Unity callbacks, health changes, despawns, knockback, visuals, and pool
operations remain managed and run only after the native call returns.

## Spatial trick and ordering contract

- Enemy centers are point items (`item.radius = 0`). Weapon ranges intentionally
  preserve the established transform-center containment rule; imported hurtbox
  dimensions do not silently enlarge weapon reach.
- A one-based snapshot index is the batch-local stable id. It preserves the old
  first-packed-enemy tie break for nearest queries, while hit `ItemIndex` maps
  directly back to the captured object even if later damage swap-removes live
  enemies.
- At up to 64 items the Runtime Kernel uses its cache-friendly direct scan.
  Larger radius batches use its caller-owned open-addressed uniform grid. The
  bridge grows scratch and result arrays geometrically, so steady-state ticks
  allocate nothing.
- Native radius hits are grouped by query and emitted in item order. Weapon code
  consumes each group backwards to retain the former descending packed-list
  mutation order.

`NormalizeRadians` applies the geometry API's documented `[-pi, pi]` periodic
input contract. `SinCosRadians` then forwards one normalized angle span to
`Kernel.SinCosGeometry`, giving every orbit/spawn calculation the Runtime
Kernel's shared quadrant reduction and paired sin/cos polynomials instead of
independent managed approximations.
