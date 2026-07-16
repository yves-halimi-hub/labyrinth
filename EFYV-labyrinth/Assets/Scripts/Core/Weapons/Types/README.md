# Weapon Types

[Up to weapons](../README.md)

Reusable attack geometries:

- [`AuraWeapon.cs`](AuraWeapon.cs): damage all enemies in a fixed radius.
- [`DropWeapon.cs`](DropWeapon.cs): choose random camera-space impact points.
- [`MeleeWeapon.cs`](MeleeWeapon.cs): range damage plus planar knockback.
- [`OrbitalWeapon.cs`](OrbitalWeapon.cs): continuously move attack points around the owner.
- [`ProjectileWeapon.cs`](ProjectileWeapon.cs): fire a pooled projectile toward a packed-list target.
- [`SplashWeapon.cs`](SplashWeapon.cs): random nearby area impacts.

Any loop that deals lethal damage iterates enemies in descending order so swap-removal cannot skip an element.
