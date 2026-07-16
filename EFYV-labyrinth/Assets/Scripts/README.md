# Unity Scripts

[Up to Assets](../README.md) | [Game repository](../../README.md)

The assembly boundary is expressed by directory ownership:

- [Core](Core/README.md): player-build runtime code.
- [Editor](Editor/README.md): UnityEditor-only import, live-refresh, and gizmo code.

Runtime code must not reference `UnityEditor`. Editor code may bridge runtime assets to Unity's import database.
