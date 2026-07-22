# Entities

[Up to runtime core](../README.md)

The gameplay object hierarchy and its packed global iteration lists.

## Hierarchy

- [`GameEntity.cs`](GameEntity.cs): Unity component caching, pool identity, spawn state, and the scene-placed registry. Entities dropped directly into a scene (never pool-spawned) register as pending in `Awake` (pool factory clones and prefab assets are excluded); `SpawnManager.Update` and the map switch promote them via `OnSpawn` into the per-type packed lists so they tick, are targetable, and are cleaned on map switch. `PlayerController` opts out via `TracksAsScenePlaced`.
- [`Faction.cs`](Faction.cs): the combat sides. `Player` is the zero value so unowned weapons and projectiles retain the established player-side default.
- [`LivingEntity.cs`](LivingEntity.cs): health, authored stats, directional sprites, and damage. `TakeDamage` clamps negative amounts to zero — damage never heals; only `Heal` restores health. It plays the current facing's imported animation as a runtime flipbook over `EntityFacingImportData.Frames`, advanced by the central update loops (`Enemy.Tick` / `PlayerController.Update` via `TickFlipbook`) with no per-frame allocations and honoring imported per-frame durations (`0` = inherit animation fps), the clamped loop range, and ping-pong. `ApplyDirectionalSprite` resolves this flipbook, preserves progress across a facing change when the same clip keeps playing, and restarts it on a state change; a facing with no imported frames falls back to the hand-authored static directional sprite. `PlayAnimation` selects idle/walk/attack only when an authored animation carries the matching `Name`, otherwise the first clip plays; movement drives walk/idle, while attack has no automatic return transition. The current facing and flipbook frame's `Hurtbox` record (`Game.Hitbox.HurtboxType`) drives a cached hand-placed `BoxCollider2D`, re-synced when the facing or frame changes; frames without a `Hurtbox` inherit the last bounds, and missing colliders or imported hitboxes are safe no-ops. Authored effect descriptors drive the `SpriteRenderer`: `OnSpawn` resets color and fires matching effects, positive post-clamp damage fires `OnDamaged`, `tint` persists, and `flash` is centrally timed and restores the tint. Effect matching currently scans every imported animation for the current facing rather than only the playing clip. `particleHook` descriptors are stored but not interpreted because the runtime has no particle pipeline. Applying source data also stores imported sub-element attachment records (`AuthoredAttachments`, with `CountAttachmentsForFrame`); the atlas contains flattened attachment pixels, and the runtime does not render separate dynamic sub-element sprites.
- [`Enemy.cs`](Enemy.cs) and [`BossEnemy.cs`](BossEnemy.cs): scaling, targeting, packed enemy membership, and phases. Enemies stop chasing and stop contact-attacking a dead player (custom non-player targets keep working).
- [`PlayerController.cs`](PlayerController.cs): input, invulnerability, experience, session currency, timed buffs, and projectile loop. `Initialize` folds the persisted meta-progression multipliers (`SaveManager.GetCombinedStatsForToon`: `MaxHealth`, `MoveSpeed`) into the base stats — non-finite or non-positive slots fall back to the neutral 1x; `ReinitializeForToon` re-runs the fold for a selected toon. `ApplyTimedBuff` registers centrally ticked buffs that revert on expiry (re-application refreshes, never stacks). `Die` is idempotent: it latches `IsDead`, despawns, then raises the static `OnPlayerDied` event exactly once so managers can react to game over; `OnSpawn` clears the dead state and the buff list. Session coin addition saturates at `int.MaxValue`.
- [`Projectile.cs`](Projectile.cs): normalized movement, lifetime, piercing, and packed projectile membership. Damage follows the firing weapon's `OwnerFaction` (player-owned hits enemies, enemy-owned hits the player); `OnSpawn` resets the pierce counter and the per-collider component-lookup memo for pool reuse.

## Browse

- [Environment](Environment/README.md): props and interaction behavior.
- [Implementations](Implementations/README.md): concrete enemies and theme data.
- [Items](Items/README.md): merchant purchase models.

Damage or despawn can mutate a packed list. Loops that may trigger either operation must iterate from the tail toward zero.
