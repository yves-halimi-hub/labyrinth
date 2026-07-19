# Spawning

[Up to Core](../README.md)

Item #4: the "the game debugger knows what to spawn" loop - turning a freshly
imported `.efyvlaby` asset into a live, pooled game object without hand-building
a prefab per asset.

| File | Responsibility |
| --- | --- |
| [`SpawnArchetype.cs`](SpawnArchetype.cs) | The three generic template archetypes (`Enemy` / `Boss` / `Prop`) every imported asset maps onto |
| [`DataToPrefabFactory.cs`](DataToPrefabFactory.cs) | Resolves an imported `SchemaBackedAssetData`'s archetype (by data type, custom types falling back to their base; unknown shapes rejected), rents the matching template through `PoolManager`, and binds the asset via `LivingEntity`/`PropEntity` `LoadData` |
| [`SpawnPaletteModel.cs`](SpawnPaletteModel.cs) | The debug window's testable list / refresh / selection state machine (dedup, selection preservation, auto-offer of the newest import); no editor or GUI dependency |

Archetype resolution walks the data type: `BossData` -> Boss (checked before
`EnemyData`, which it extends), any other living-entity data -> Enemy, any
`GameAssetData` (props, tilesets, custom prop types) -> Prop. A plain
`EntityData` or an unrecognized `SchemaBackedAssetData` has no archetype and is
rejected cleanly.

The factory binds through the pooled spawn **before** `OnSpawn` (a
`PoolManager.Spawn` overload takes a pre-spawn hook), so the clone drives the
item #13 flipbook animation and item #14 hurtbox collider (living entities) and
registers in the central animation loop (props) - the same order the scene
bootstrap uses.

The scene-independent archetype template prefabs live under
[`Assets/Prefabs/DebugTemplates`](../../../Prefabs/DebugTemplates); the Play-Mode
editor window that drives this loop is
[`EFYVSpawnPaletteWindow`](../../Editor/README.md).
