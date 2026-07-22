# Environment Entities

[Up to entities](../README.md)

World objects share [`PropEntity.cs`](PropEntity.cs), which owns blocking state, optional frame animation, asset refresh, and `ActiveAnimatedProps` membership. When a prop carries designer data (`assetData`), the imported `IsWalkable` schema slot drives the runtime blocking flag on `LoadData`/`RefreshDataFromAsset` (walkable = non-blocking), and `TrapDamage` is read live from the same asset schema block; the serialized inspector bool remains only a fallback for hand-placed props without data, and subclass `Initialize` hardcodes still win because they run afterwards. Applying source data also fills `animationFrames` from the asset's imported multi-frame sheet (`GameAssetData.ImportedFrames`) when present, so a designed prop animates over its published frames instead of the hand-authored inspector array; a single-sprite import leaves the inspector array untouched. Animation speed is sanitized on every write path (property setter and serialized-field sync): zero, negative, or NaN values clamp to `PropEntity.MinimumAnimationSpeed` so the ticker never thrashes a frame per tick and the `OnSpawn` timer randomization range is never inverted.

- [`InteractableProp.cs`](InteractableProp.cs) forwards trigger or collision contact to `OnInteract` for the player.
- [`NonInteractableProp.cs`](NonInteractableProp.cs) marks passive decorations.
- [Implementations](Implementations/README.md) contains pickups, merchants, doors, chests, and scenery.

Only props with animation frames join the central animation list, and they must leave it during despawn.
