# Entities

[Up to runtime core](../README.md)

The gameplay object hierarchy and its packed global iteration lists.

## Hierarchy

- [`GameEntity.cs`](GameEntity.cs): Unity component caching, pool identity, spawn state, and the scene-placed registry. Entities dropped directly into a scene (never pool-spawned) register as pending in `Awake` (pool factory clones and prefab assets are excluded); `SpawnManager.Update` and the map switch promote them via `OnSpawn` into the per-type packed lists so they tick, are targetable, and are cleaned on map switch. `PlayerController` opts out via `TracksAsScenePlaced`.
- [`Faction.cs`](Faction.cs): the combat sides. `Player` is the zero value so unowned weapons and projectiles retain the established player-side default.
- [`LivingEntity.cs`](LivingEntity.cs) owns health, authored stats, directional sprites, and damage:
  - `TakeDamage` clamps negative amounts to zero, so damage never heals; only `Heal` restores health.
  - The current facing plays `EntityFacingImportData.Frames` as an allocation-free flipbook advanced
    by the central enemy/player loops. Imported per-frame durations (`0` inherits animation fps),
    clamped loop ranges, and ping-pong are honored. Facing changes preserve progress when the same
    clip continues; state changes restart it; missing imported frames fall back to the hand-authored
    directional sprite.
  - `PlayAnimation` selects idle/walk/attack by authored `Name`, otherwise the first clip plays.
    Movement drives walk/idle; attack has no automatic return transition.
  - The current frame's `Hurtbox` (`Game.Hitbox.HurtboxType`) drives a cached hand-placed
    `BoxCollider2D`. Facing/frame changes resync it, frames without a hurtbox inherit the last
    bounds, and missing colliders or imported hitboxes are safe no-ops.
  - Authored effects drive the `SpriteRenderer`: spawn resets color and fires matching effects,
    positive post-clamp damage fires `OnDamaged`, `tint` persists, and centrally timed `flash`
    restores the tint. Matching currently scans every imported animation for the facing;
    `particleHook` is stored but not interpreted because no particle pipeline exists.
  - Imported sub-element records remain available through `AuthoredAttachments` and
    `CountAttachmentsForFrame`. Their pixels are already flattened into the atlas, so the runtime
    does not render separate dynamic sub-element sprites.
- [`Enemy.cs`](Enemy.cs) and [`BossEnemy.cs`](BossEnemy.cs): scaling, targeting, packed enemy membership, and phases. Enemies stop chasing and stop contact-attacking a dead player (custom non-player targets keep working).
- [`PlayerController.cs`](PlayerController.cs): input, invulnerability, experience, session currency, timed buffs, and projectile loop. `Initialize` folds the persisted meta-progression multipliers (`SaveManager.GetCombinedStatsForToon`: `MaxHealth`, `MoveSpeed`) into the base stats — non-finite or non-positive slots fall back to the neutral 1x; `ReinitializeForToon` re-runs the fold for a selected toon. `ApplyTimedBuff` registers centrally ticked buffs that revert on expiry (re-application refreshes, never stacks). `Die` is idempotent: it latches `IsDead`, despawns, then raises the static `OnPlayerDied` event exactly once so managers can react to game over; `OnSpawn` clears the dead state and the buff list. Session coin addition saturates at `int.MaxValue`.
- [`Projectile.cs`](Projectile.cs): normalized movement, lifetime, piercing, and packed projectile membership. Damage follows the firing weapon's `OwnerFaction` (player-owned hits enemies, enemy-owned hits the player); `OnSpawn` resets the pierce counter and the per-collider component-lookup memo for pool reuse.

## Browse

- [Environment](Environment/README.md): props and interaction behavior.
- [Implementations](Implementations/README.md): concrete enemies and theme data.
- [Items](Items/README.md): merchant purchase models.

Damage or despawn can mutate a packed list. Loops that may trigger either operation must iterate from the tail toward zero.
