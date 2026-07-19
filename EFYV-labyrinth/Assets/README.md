# Unity Assets

[Up to game repository](../README.md)

Content under this directory is visible to Unity and can enter editor or player compilation. External tools and verification projects intentionally live outside it.

## Browse

- [Scripts](Scripts/README.md): runtime gameplay code and Unity editor integration, compiled into the `EFYV.Game` and `EFYV.Game.Editor` assemblies via the checked-in `.asmdef` files.
- [Scenes](Scenes/): `Labyrinth.unity`, the hand-authored playable scene (camera, manager singletons, player, inactive enemy template, map viewport) listed in `EditorBuildSettings`.
- [Prefabs/DebugTemplates](Prefabs/DebugTemplates/README.md): item #4 scene-independent per-archetype (Enemy/Boss/Prop) template prefabs the debug spawn factory clones.

Every checked-in file and folder here carries a hand-authored `.meta` whose GUID is `md5("efyv:" + repo-relative path)`; `Tests/test_unity_project_assets.py` pins the scheme and the scene's cross-references. Generated `.asset`, imported PNG, and raw-art folders may be created by Unity or the designer workflow, but they are not represented in this source snapshot.
