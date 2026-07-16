# Unity Editor Integration

[Up to Scripts](../README.md) | [Game repository](../../../README.md)

Editor-only bridge from LabyMake output to runtime assets.

## Files

- [`EFYVPixelArtImporter.cs`](EFYVPixelArtImporter.cs): validates metadata, configures pixel-art slicing, maps schema properties, creates typed assets, and links directional imports.
- [`EFYVLiveDebugBridge.cs`](EFYVLiveDebugBridge.cs): coalesces Play Mode refreshes and updates matching scene entities/props.
- [`EFYVHitboxGizmo.cs`](EFYVHitboxGizmo.cs): converts authored hitboxes into local bounds and draws frame-specific gizmos.

The metadata filename stem and `entityName` or `assetName` must pass the backend safe-stem policy. A new designer asset type requires both a runtime class and importer factory registration.
