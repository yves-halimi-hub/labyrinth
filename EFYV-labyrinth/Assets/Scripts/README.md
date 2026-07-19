# Unity Scripts

[Up to Assets](../README.md) | [Game repository](../../README.md)

The assembly boundary is expressed by directory ownership and enforced by
assembly definitions:

- [Core](Core/README.md): player-build runtime code, compiled into `EFYV.Game` ([`EFYV.Game.asmdef`](EFYV.Game.asmdef), unsafe allowed, references `EFYVBackend.Core`).
- [Editor](Editor/README.md): UnityEditor-only import, live-refresh, and gizmo code, compiled into `EFYV.Game.Editor` ([`Editor/EFYV.Game.Editor.asmdef`](Editor/EFYV.Game.Editor.asmdef), editor platform only).

Runtime code must not reference `UnityEditor`. Editor code may bridge runtime assets to Unity's import database.
