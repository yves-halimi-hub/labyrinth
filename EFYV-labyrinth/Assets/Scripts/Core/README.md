# Runtime Core

[Up to Scripts](../README.md) | [Game repository](../../../README.md)

Unity-facing gameplay code built on the backend's compact schemas, math, collections, memory, and persistence primitives.

## Browse

| Area | Responsibility |
| --- | --- |
| [`GameBootstrap.cs`](GameBootstrap.cs) | Scene-load composition for `Assets/Scenes/Labyrinth.unity`: deterministic placeholder sprites, tile palette, and a schema-backed enemy-template `EnemyData` (all guarded so authored data always wins) |
| [Controllers](Controllers/README.md) | Equipped weapons and power-up inventory |
| [Data](Data/README.md) | Schema-backed Unity assets and imported art metadata |
| [Entities](Entities/README.md) | Players, enemies, projectiles, and world props |
| [Interfaces](Interfaces/README.md) | Damage and pooling contracts |
| [Items](Items/README.md) | Compact runtime item values |
| [Managers](Managers/README.md) | Central loops, pools, maps, progression, and saves |
| [Spawning](Spawning/README.md) | Item #4: the data-to-prefab factory + debug spawn-palette state machine |
| [Utils](Utils/README.md) | Singleton, vector, and transform bridges |
| [Weapons](Weapons/README.md) | Weapon lifecycle, archetypes, and implementations |

Keep hot-path state in packed structures, avoid scene searches in repeated loops, and preserve descending iteration wherever callbacks can remove packed-list members.
