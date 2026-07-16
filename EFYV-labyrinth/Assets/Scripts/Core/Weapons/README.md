# Weapons

[Up to runtime core](../README.md)

[`Weapon.cs`](Weapon.cs) defines cooldown ticking, level state, and the `Fire` contract. [`WeaponEvolution.cs`](WeaponEvolution.cs) connects a required power-up ID to an evolved prefab. [`MagicWandWeapon.cs`](MagicWandWeapon.cs) targets the nearest packed-list enemy and fires a pooled projectile.

## Browse

- [Types](Types/README.md): reusable weapon behavior families.
- [Implementations](Implementations/README.md): configured concrete weapons.

Weapons are ticked by `WeaponController`; they do not own independent Unity update loops.
