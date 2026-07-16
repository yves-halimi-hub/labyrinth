# Runtime Data Assets

[Up to runtime core](../README.md)

Unity `ScriptableObject` types that combine compact backend schemas with references Unity must serialize.

## Files

- [`SchemaBackedAssetData.cs`](SchemaBackedAssetData.cs): copies the fixed schema block to and from a serialized `int[]` without exposing its storage.
- [`AssetDataHierarchy.cs`](AssetDataHierarchy.cs): generic art assets and the `DesignableAsset` marker.
- [`EntityData.cs`](EntityData.cs): atlas, animation, directional sprite, and hitbox import records.
- [`LegacyAchievementDatabase.cs`](LegacyAchievementDatabase.cs): visual achievement definitions backed by hashed compact data.

Names synchronize their deterministic hash into `AssetSchema.AssetIdHash`. Directional imports retain prior frames when metadata-only updates omit a new frame array.
