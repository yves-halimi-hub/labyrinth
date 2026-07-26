# Weapon Types

[Up to weapons](../README.md)

Reusable attack geometries. All radius and aimed damage goes through the base `Weapon` faction helpers, so each family harms only the side opposing its `OwnerFaction`:

- [`AuraWeapon.cs`](AuraWeapon.cs): damage all opposing targets in a fixed radius.
- [`DropWeapon.cs`](DropWeapon.cs): choose all random camera-space impact points, then submit one multi-radius spatial batch.
- [`MeleeWeapon.cs`](MeleeWeapon.cs): one native range query plus managed damage/planar knockback. The knockback step scales by the driving tick's `TickDeltaTime`, never the global clock.
- [`OrbitalWeapon.cs`](OrbitalWeapon.cs): continuously move attack points around the owner. All angles use one native paired-sin/cos batch and all contact points use one spatial batch. Contact damage scales by the tick's `TickDeltaTime` (its `Tick` records it before firing).
- [`ProjectileWeapon.cs`](ProjectileWeapon.cs): fire a pooled projectile toward the nearest opposing target. Preferred wiring is a typed `projectilePrefab` reference through `PoolManager.Spawn` (the MagicWandWeapon pattern); the legacy `projectilePrefabKey` path type-checks the rented entry BEFORE activating it and returns a mis-keyed entry to its pool unharmed.
- [`SplashWeapon.cs`](SplashWeapon.cs): collect random nearby area impacts, then submit one multi-radius spatial batch.

Native hits are grouped by attack point. Each group is consumed in descending captured-item
order, and enemies killed by an earlier group are skipped, retaining packed-list
swap-removal behavior while avoiding managed target-search loops.
