# Weapons

[Up to runtime core](../README.md)

[`Weapon.cs`](Weapon.cs) defines cooldown ticking, level state, the `Fire` contract, and the shared faction-aware combat helpers: `OwnerFaction` (stamped by the equipping `WeaponController`; defaults to the player's side), `TryGetTargetPosition` (player-owned weapons aim at the nearest packed-list enemy, enemy-owned weapons aim at the living player), `DamageTargetsInRadius` (allocation-free planar radius damage against the opposing side only), and `TickDeltaTime` (the driving tick's deltaTime, recorded before `Fire` so time-scaled effects never read the global `Time.deltaTime`). [`WeaponEvolution.cs`](WeaponEvolution.cs) connects a required power-up ID to an evolved prefab. [`MagicWandWeapon.cs`](MagicWandWeapon.cs) resolves its target through the shared faction helpers and fires a pooled projectile carrying its owner's faction.

## Browse

- [Types](Types/README.md): reusable weapon behavior families.
- [Implementations](Implementations/README.md): configured concrete weapons.

Weapons are ticked by `WeaponController`; they do not own independent Unity update loops. A dead player is never a valid target: enemy-owned weapons no-op after game over.
