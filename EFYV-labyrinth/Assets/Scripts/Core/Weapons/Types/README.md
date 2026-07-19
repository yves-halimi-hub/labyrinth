# Weapon Types

[Up to weapons](../README.md)

Reusable attack geometries. All radius and aimed damage goes through the base `Weapon` faction helpers, so each family harms only the side opposing its `OwnerFaction`:

- [`AuraWeapon.cs`](AuraWeapon.cs): damage all opposing targets in a fixed radius.
- [`DropWeapon.cs`](DropWeapon.cs): choose random camera-space impact points.
- [`MeleeWeapon.cs`](MeleeWeapon.cs): range damage plus planar knockback. The knockback step scales by the driving tick's `TickDeltaTime`, never the global clock.
- [`OrbitalWeapon.cs`](OrbitalWeapon.cs): continuously move attack points around the owner. Contact damage scales by the tick's `TickDeltaTime` (its `Tick` records it before firing).
- [`ProjectileWeapon.cs`](ProjectileWeapon.cs): fire a pooled projectile toward the nearest opposing target. Preferred wiring is a typed `projectilePrefab` reference through `PoolManager.Spawn` (the MagicWandWeapon pattern); the legacy `projectilePrefabKey` path type-checks the rented entry BEFORE activating it and returns a mis-keyed entry to its pool unharmed.
- [`SplashWeapon.cs`](SplashWeapon.cs): random nearby area impacts.

Any loop that deals lethal damage iterates enemies in descending order so swap-removal cannot skip an element.
