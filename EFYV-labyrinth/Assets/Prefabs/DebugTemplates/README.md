# Debug spawn template prefabs

[Up to game](../../../README.md)

Scene-independent, generic per-archetype template prefabs that the
[data-to-prefab factory](../../Scripts/Core/Spawning/README.md) clones. One
template (and therefore one `PoolManager` pool) backs each archetype; the
imported asset is bound onto the pooled clone through `LoadData`.

| Prefab | Component stack | Archetype |
| --- | --- | --- |
| `Enemy.prefab` | `Monster` + `SpriteRenderer` + `BoxCollider2D` (trigger) + `Rigidbody2D` (kinematic) + `WeaponController` | Enemy |
| `Boss.prefab` | `Boss` + `SpriteRenderer` + `BoxCollider2D` (trigger) + `Rigidbody2D` (kinematic) + `WeaponController` | Boss |
| `Prop.prefab` | `GenericProp` + `SpriteRenderer` + `BoxCollider2D` (trigger) | Prop |

The `BoxCollider2D` doubles as the runtime hurtbox driven by imported hitboxes
and the contact trigger; the kinematic `Rigidbody2D` lets 2D trigger callbacks
fire. GUIDs are hand-authored (`md5("efyv:" + repo-relative path)`) and their
referential integrity is pinned by `Tests/test_unity_project_assets.py`
(`PrefabIntegrityTests`).
