# Entities

[Up to runtime core](../README.md)

The gameplay object hierarchy and its packed global iteration lists.

## Hierarchy

- [`GameEntity.cs`](GameEntity.cs): Unity component caching, pool identity, and spawn state.
- [`LivingEntity.cs`](LivingEntity.cs): health, authored stats, directional sprites, and damage.
- [`Enemy.cs`](Enemy.cs) and [`BossEnemy.cs`](BossEnemy.cs): scaling, targeting, packed enemy membership, and phases.
- [`PlayerController.cs`](PlayerController.cs): input, invulnerability, experience, session currency, and projectile loop.
- [`Projectile.cs`](Projectile.cs): normalized movement, lifetime, piercing, and packed projectile membership.

## Browse

- [Environment](Environment/README.md): props and interaction behavior.
- [Implementations](Implementations/README.md): concrete enemies and theme data.
- [Items](Items/README.md): merchant purchase models.

Damage or despawn can mutate a packed list. Loops that may trigger either operation must iterate from the tail toward zero.
