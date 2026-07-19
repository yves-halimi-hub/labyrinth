# Controllers

[Up to runtime core](../README.md)

[`WeaponController.cs`](WeaponController.cs) owns equipped weapons, active power-ups, ticking, capacity checks, and weapon evolution.

Important invariants:

- Power-ups are value types; after mutation, write the value back into `activePowerUps`.
- Evolution replaces the weapon at its existing list index and consumes exactly one matching power-up use.
- Weapon capacity is initialized by the owning player or enemy before additions are accepted.
- `Initialize` resolves the controller's `OwnerFaction` from its host (an `Enemy` component means `Faction.Enemy`, anything else fights enemies) and stamps that faction onto every weapon it holds, adds, or evolves — weapons never damage their own side.
- `HasFreeWeaponSlot` reports remaining capacity; `UpgradeManager` relies on it to keep offering new weapons to players with empty slots instead of flipping into the special-attack phase.
