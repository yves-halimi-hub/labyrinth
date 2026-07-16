# Environment Entities

[Up to entities](../README.md)

World objects share [`PropEntity.cs`](PropEntity.cs), which owns blocking state, optional frame animation, asset refresh, and `ActiveAnimatedProps` membership.

- [`InteractableProp.cs`](InteractableProp.cs) forwards trigger or collision contact to `OnInteract` for the player.
- [`NonInteractableProp.cs`](NonInteractableProp.cs) marks passive decorations.
- [Implementations](Implementations/README.md) contains pickups, merchants, doors, chests, and scenery.

Only props with animation frames join the central animation list, and they must leave it during despawn.
