# Game And Editor Verification

[Up to game repository](../README.md)

External tests compile all backend and game/editor C# sources without placing test code under Unity's `Assets` directory.

## Files

- [`EFYVGame.Verification.csproj`](EFYVGame.Verification.csproj): dependency-free console verification project.
- [`UnityStubs.cs`](UnityStubs.cs): controlled UnityEngine and UnityEditor API surface for headless tests.
- [`GameDataEditorTests.cs`](GameDataEditorTests.cs): schema assets, importer, hitboxes, and live refresh.
- [`GameRuntimeTests.cs`](GameRuntimeTests.cs): entities, projectiles, weapons, pools, vectors, and viewports.
- [`GameManagerTests.cs`](GameManagerTests.cs): saves, progression, maps, props, achievements, and manager states.
- [`test_mock_exporter.py`](test_mock_exporter.py): PNG, metadata, repeatability, and path-attack tests.

```powershell
dotnet run --project EFYVGame.Verification.csproj -c Debug
dotnet run --project EFYVGame.Verification.csproj -c Release
python -m unittest discover -s . -p "test_*.py" -v
```

The stubs deliberately model only APIs used by this source tree. Passing here does not replace tests inside the real Unity engine for serialization, rendering, physics, or AssetDatabase behavior.
