# IO Policy

[Core](../README.md) | [Repository](../../README.md) | [Persistence](../Persistence/README.md)

[`DesignerPathPolicy`](DesignerPathPolicy.cs) is LabyMake's adapter over the backend [`SafePathPolicy`](../../../EFYV-labybackend/Core/IO/SafePathPolicy.cs). Keeping the implementation shared ensures the authoring tool and backend exporter apply the same filename rules.

## API

- `IsSafeFileStem` rejects empty names, traversal components, separators, invalid filename characters, trailing dots/spaces, and Windows reserved device stems such as `CON`, `COM1`, or `LPT1`.
- `GetContainedPath` resolves a path and requires its immediate parent directory to be the supplied root.

[`ProjectPersistenceService`](../Persistence/ProjectPersistenceService.cs) validates project names before appending project/autosave extensions. [`AssetBankManager`](../Logic/AssetBankManager.cs) applies the same policy to reusable sub-elements. Callers should validate the logical stem and then resolve the complete filename inside the owned directory.

