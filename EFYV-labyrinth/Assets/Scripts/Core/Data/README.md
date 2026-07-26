# Runtime Data Assets

[Up to runtime core](../README.md)

Unity `ScriptableObject` types that combine compact backend schemas with references Unity must serialize.

## Files

- [`SchemaBackedAssetData.cs`](SchemaBackedAssetData.cs): copies the fixed schema block to and from a serialized `int[]` without exposing its storage. It also stores imported sub-element attachment records (`ImportedAttachments`/`SetImportedAttachments`) on the schema-backed base so every designable asset keeps them through one importer call; the value is null when the document had none. Its string-keyed custom-property store holds runtime-registered designer fields: `.efyvlaby` property keys that the compiled schema manifest does not know are parked by the importer as parallel key/value string arrays (values keep their raw JSON text, strings unquoted; cleared on a reimport without unknown keys) and read through `TryGetCustomProperty`/`TryGetCustomFloat`/`TryGetCustomInt` (ordinal keys, invariant-culture parse-on-read).
- [`AssetDataHierarchy.cs`](AssetDataHierarchy.cs): generic art assets and the `DesignableAsset` marker. `GameAssetData` also carries `ImportedFrames`/`SetImportedFrames` — the full imported frame set (atlas order) from a multi-frame sheet (`sprite` stays frame 0), which animated props play instead of their hand-authored inspector array; null for single-sprite imports.
- [`EntityData.cs`](EntityData.cs) owns atlas, animation, directional-sprite, hitbox, effect, and
  attachment import records:
  - `EntityHitboxGeometry.TryGetLocalBounds` is the single pixel-to-local-unit conversion used by
    both runtime `BoxCollider2D` synchronization and the `EFYVHitboxGizmo` editor overlay. Rectangle
    fields and frame dimensions are divided by 16 pixels-per-unit exactly once; the 32x16-frame /
    `(8,4,16,8)` golden case yields a centered `1x0.5`-unit box.
  - Weapon spatial queries intentionally remain transform-center point tests
    (`item.radius = 0`), so a hurtbox change never changes attack reach.
  - `EntityAnimationMetadata` carries optional per-frame durations (`null` means fps-only and `0`
    inherits `FramesPerSecond`), effective loop bounds, ping-pong, and authored effects. Import
    resolves absent effect options to shared defaults. `LivingEntity` interprets flash and tint;
    `particleHook` is stored by name but not interpreted because no particle pipeline exists.
  - `EntityAttachmentRecord` stores atlas-frame index, sub-element name, designer-canvas pivot,
    z-order, and flips. Attachment pixels are already flattened into the atlas, so records are kept
    for inspection without separate runtime sprites or per-facing variants.
- [`TilesetAssetData.cs`](TilesetAssetData.cs): an imported tileset — a `GameAssetData` whose source `.efyvlaby` carried the tile-ID manifest block. `tileSprites` holds the sliced tile-sheet sprites in tile-ID order (slice i = FastGridMap short tile id i), so it feeds `MapViewportController.tilePalette` directly; `tileSize`/`tileNames` mirror the manifest.
- [`MapAssetData.cs`](MapAssetData.cs): an imported `.efyvmap` — map id (the file stem), dimensions, row-major tile ids (`int[]`; Unity does not serialize `short[]`; values below `Game.Map.MinimumTileId` render blank), `MapPropPlacement` records, the tileset name, and a direct `TilesetAssetData` link resolved at import time. Prop-placement records are stored but are not instantiated by the current runtime. `HasLoadableTiles`/`CopyTilesTo(FastGridMap)` are the viewport's ingestion seams.
- [`LegacyAchievementDatabase.cs`](LegacyAchievementDatabase.cs): visual achievement definitions backed by hashed compact data.

Names synchronize their deterministic hash into `AssetSchema.AssetIdHash`. Directional imports retain prior frames when metadata-only updates omit a new frame array.
