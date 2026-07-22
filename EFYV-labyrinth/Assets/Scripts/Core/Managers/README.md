# Managers

[Up to runtime core](../README.md)

Central orchestration and update surfaces:

| Manager | Ownership |
| --- | --- |
| [`PoolManager.cs`](PoolManager.cs) | Bounded entity/GameObject pools, prewarming (instance + static `TryPrewarm`/`TryPrewarmGameObject` hooks), and delayed VFX despawn. `DespawnGameObject` is idempotent (a not-currently-rented object is ignored) and validates the pool key before any deactivation. A `Spawn`/`SpawnByKey` overload takes a pre-spawn hook that runs on the rented instance after placement but before `OnSpawn`, so the data-to-prefab factory can bind imported data first (props register for animation, enemies scale spawn stats) |
| [`SpawnManager.cs`](SpawnManager.cs) | Difficulty curve, bounded spawning, enemy/prop central ticks, scene-placed entity promotion, survival-time achievement feed; `Start` prewarms the enemy prefab pools |
| [`AIDirector.cs`](AIDirector.cs) | Time-based intensity, health, and speed multipliers |
| [`MapManager.cs`](MapManager.cs) | Map transition sequence and entity cleanup (pooled AND scene-placed) |
| [`MapViewportController.cs`](MapViewportController.cs) | Backend grid, ring-buffer tile visuals, camera following. `LoadMapData(mapId)` loads imported map data: a matching `MapAssetData` (serialized `importedMaps` slots first, then any loaded asset; unloadable entries skipped) rebuilds the grid at the imported dimensions, copies its tiles, and feeds `tilePalette` from the linked tileset's sliced sprites; procedural noise is the explicit fallback when no imported map matches. `DoorProp`/`MapManager.SwitchMap` therefore drive distinct imported maps per target id |
| [`MapTransitionCameraEffect.cs`](MapTransitionCameraEffect.cs) | Optional CPU blur bridge |
| [`SaveManager.cs`](SaveManager.cs) | Binary profile load/save and progression arithmetic |
| [`DropManager.cs`](DropManager.cs) | Time-scaled coin, chest, and XP gem drops; `Awake` prewarms the drop pools; `DropLoot` doubles as the kill-notification seam for achievements |
| [`UpgradeManager.cs`](UpgradeManager.cs) | Normal upgrade and special-attack phases, plus the runtime upgrade application path |
| [`AchievementManager.cs`](AchievementManager.cs) | Bit-packed legacy achievement state, startup definition re-sync for player builds, and the event-driven kill/survival triggers |

Managers that derive from `Singleton<T>` clear only state they own. Spawn loops and delayed work are bounded by shared configuration to prevent a single frame from expanding without limit.

## Game over

`SpawnManager`, `AIDirector`, and `MapManager` subscribe to `PlayerController.OnPlayerDied` (subscribed in `Awake`, unsubscribed in `OnDestroy` — the event is static). After the broadcast: spawning, the survival timer, and the difficulty coupling freeze; the director reports neutral 1x multipliers; map transitions are ignored. The central enemy/prop ticking keeps running so the world stays alive around the corpse. The runtime has no clean restart/reset path.

## Upgrade loop flow

Player level-ups (`PlayerController.LevelUp`) and chests (`UpgradeManager.OpenChest`) both call into `TriggerUpgradeSelection`. While the player has a free weapon slot or any weapon below max level, NORMAL upgrades are offered — an empty inventory never flips the run into the special-attack phase. If a UI has subscribed to `OnNormalUpgradesRequested`, it presents the choices and applies the picks itself (`WeaponController.TryAddWeapon` / `Weapon.Upgrade` are public). With no subscriber, `ApplyNormalUpgrades` applies them directly: it fills free slots with instances from the serialized `normalWeaponPool` prefab array, then raises the lowest-level weapon. Only once every slot is full and every weapon is maxed does the run enter the special-attack phase (`OnSpecialAttacksRequested`).
