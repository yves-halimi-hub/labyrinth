# Managers

[Up to runtime core](../README.md)

Central orchestration and update surfaces:

| Manager | Ownership |
| --- | --- |
| [`PoolManager.cs`](PoolManager.cs) | Bounded entity/GameObject pools and delayed VFX despawn |
| [`SpawnManager.cs`](SpawnManager.cs) | Difficulty curve, bounded spawning, enemy/prop central ticks |
| [`AIDirector.cs`](AIDirector.cs) | Time-based intensity, health, and speed multipliers |
| [`MapManager.cs`](MapManager.cs) | Map transition sequence and entity cleanup |
| [`MapViewportController.cs`](MapViewportController.cs) | Backend grid, ring-buffer tile visuals, camera following |
| [`MapTransitionCameraEffect.cs`](MapTransitionCameraEffect.cs) | Optional CPU blur bridge |
| [`SaveManager.cs`](SaveManager.cs) | Binary profile load/save and progression arithmetic |
| [`DropManager.cs`](DropManager.cs) | Time-scaled coin and chest drops |
| [`UpgradeManager.cs`](UpgradeManager.cs) | Normal upgrade and special-attack phases |
| [`AchievementManager.cs`](AchievementManager.cs) | Bit-packed legacy achievement state |

Managers that derive from `Singleton<T>` clear only state they own. Spawn loops and delayed work are bounded by shared configuration to prevent a single frame from expanding without limit.
