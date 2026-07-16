# Runtime Interfaces

[Up to runtime core](../README.md)

- [`IDamageable.cs`](IDamageable.cs) defines damage and death behavior used by projectiles, enemies, players, and attacks.
- [`IPoolable.cs`](IPoolable.cs) defines initialization plus spawn/despawn lifecycle callbacks.

Implementations must keep callbacks idempotent at their public boundaries because collisions, lifetime expiry, and manager cleanup can converge on the same object.
