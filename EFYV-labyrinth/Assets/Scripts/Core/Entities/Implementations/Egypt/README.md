# Egypt Theme Data

[Up to entity implementations](../README.md)

[`EgyptThemeEntitiesData.cs`](EgyptThemeEntitiesData.cs) declares the 17 concrete `DesignableAsset` types used by LabyMake and the Unity importer.

The class name is the serialized `assetType` registration key. The display name comes from shared configuration, and the base class determines whether living, enemy, boss, or generic asset fields are available. Any new type must be registered consistently in backend configuration, LabyMake schemas, and `EFYVPixelArtImporter`.
