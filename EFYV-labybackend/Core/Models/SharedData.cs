using System.Collections.Generic;
using System.Text.Json.Serialization;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Models
{
    // Shared Data Model for Hitboxes
    // Used by LabyMake to let artists draw the box.
    // Used by Labyrinth (Unity Importer) to deserialize the raw coordinates.
    //
    // DEFAULT-VALUE CONTRACT (explicit, because C# struct semantics make it a trap):
    // - `new HitboxData()` (and CreateDefault below) seeds the semantic defaults:
    //   position 0, UNIT width/height.
    // - `default(HitboxData)`, array elements, and uninitialized fields BYPASS the
    //   parameterless constructor and yield an all-zero block - a ZERO-SIZED box.
    // Callers materializing hitboxes from raw storage must therefore either use
    // CreateDefault()/the constructor or explicitly set all four extents.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct HitboxData
    {
        public EFYVBackend.Core.Data.FastSchemaBlock Block;

        public float X
        {
            get => Block.GetFloat((int)EFYVBackend.Core.Data.HitboxSchema.X);
            set => Block.SetFloat((int)EFYVBackend.Core.Data.HitboxSchema.X, value);
        }
        public float Y
        {
            get => Block.GetFloat((int)EFYVBackend.Core.Data.HitboxSchema.Y);
            set => Block.SetFloat((int)EFYVBackend.Core.Data.HitboxSchema.Y, value);
        }
        public float Width
        {
            get => Block.GetFloat((int)EFYVBackend.Core.Data.HitboxSchema.Width);
            set => Block.SetFloat((int)EFYVBackend.Core.Data.HitboxSchema.Width, value);
        }
        public float Height
        {
            get => Block.GetFloat((int)EFYVBackend.Core.Data.HitboxSchema.Height);
            set => Block.SetFloat((int)EFYVBackend.Core.Data.HitboxSchema.Height, value);
        }

        public HitboxData()
        {
            X = BackendConfig.Models.HitboxDefaultPosition;
            Y = BackendConfig.Models.HitboxDefaultPosition;
            Width = BackendConfig.Models.HitboxDefaultSize;
            Height = BackendConfig.Models.HitboxDefaultSize;
        }

        // Explicit, non-bypassable spelling of the semantic defaults. Prefer this over the
        // parameterless constructor at call sites: it cannot be silently skipped the way the
        // constructor is by default(HitboxData)/arrays, and it survives a future removal of
        // the constructor.
        public static HitboxData CreateDefault()
        {
            return new HitboxData();
        }
    }

    // Unified JSON Structure
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct EFYVJsonFormat
    {
        // Top-level document version (#16a). Nullable so legacy files written
        // before the field existed deserialize as "absent" and are read as
        // BackendConfig.Exporter.LegacyDocumentVersion.
        [JsonPropertyName(BackendConfig.Exporter.FieldDocumentVersion)]
        public int? documentVersion { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldAssetType)]
        public string assetType { get; set; }

        // The registered base type of assetType (#16e); importers fall back to
        // this factory when assetType itself is a custom type they do not know.
        [JsonPropertyName(BackendConfig.Exporter.FieldBaseAssetType)]
        public string baseAssetType { get; set; }

        [JsonIgnore]
        public int EffectiveDocumentVersion =>
            documentVersion ?? BackendConfig.Exporter.LegacyDocumentVersion;

        [JsonPropertyName(BackendConfig.Exporter.FieldProperties)]
        public Dictionary<string, System.Text.Json.JsonElement> properties { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldHitboxes)]
        public List<HitboxJson> hitboxes { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldAtlas)]
        public AtlasMetadataJson? atlas { get; set; }

        // Item #6 OPTIONAL sub-element attachment records (documentVersion 4).
        // Null/absent on legacy documents and whenever no frame carries an
        // attachment (the exporter omits the array entirely, keeping
        // attachment-free documents byte-identical to version-3 output).
        [JsonPropertyName(BackendConfig.Exporter.FieldAttachments)]
        public List<AttachmentJson> attachments { get; set; }

        // Item #5 OPTIONAL tileset manifest block (documentVersion 5).
        // Null/absent on every non-tileset document (the exporter omits the
        // member entirely). Present on tile-sheet exports: it maps designed
        // tiles to FastGridMap short tile ids (list index i = tile id i).
        [JsonPropertyName(BackendConfig.Exporter.FieldTileset)]
        public TilesetManifestJson? tileset { get; set; }
    }

    // Item #5: the tile-ID manifest riding inside a tileset .efyvlaby.
    // tileSize is the square tile edge in pixels (it must equal the sibling
    // atlas block's frameWidth/frameHeight when one is present); tiles holds
    // one display name per designed tile, where list index i is the
    // FastGridMap short tile id painted into map documents.
    public struct TilesetManifestJson
    {
        [JsonPropertyName(BackendConfig.Exporter.FieldTileSize)]
        public int tileSize { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldTiles)]
        public List<string> tiles { get; set; }
    }

    public struct AtlasMetadataJson
    {
        [JsonPropertyName(BackendConfig.Exporter.FieldFormatVersion)]
        public int formatVersion { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldFrameWidth)]
        public int frameWidth { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldFrameHeight)]
        public int frameHeight { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldAtlasWidth)]
        public int atlasWidth { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldAtlasHeight)]
        public int atlasHeight { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldAnimations)]
        public List<AnimationMetadataJson> animations { get; set; }
    }

    public struct AnimationMetadataJson
    {
        [JsonPropertyName(BackendConfig.Exporter.FieldName)]
        public string name { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldFps)]
        public int fps { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldStartFrame)]
        public int startFrame { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldFrameCount)]
        public int frameCount { get; set; }

        // Item #10 OPTIONAL timing/playback fields (documentVersion 2). All are
        // nullable so legacy documents deserialize as "absent" and readers fall
        // back to fps / the full frame range. frameDurationsMs, when present,
        // has exactly frameCount entries; entry 0 is the "inherit fps" sentinel,
        // positive entries are per-frame display milliseconds.
        [JsonPropertyName(BackendConfig.Exporter.FieldFrameDurationsMs)]
        public List<int> frameDurationsMs { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldLoopStart)]
        public int? loopStart { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldLoopEnd)]
        public int? loopEnd { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldPingPong)]
        public bool? pingPong { get; set; }

        // Item #7 OPTIONAL runtime-effect descriptors (documentVersion 3).
        // Null/absent on legacy documents and on animations without effects;
        // the exporter omits the array entirely when it is empty so documents
        // that do not use effects stay byte-identical.
        [JsonPropertyName(BackendConfig.Exporter.FieldEffects)]
        public List<EffectDescriptorJson> effects { get; set; }
    }

    // One authored runtime-effect descriptor (item #7): name + params +
    // trigger tag. effectType is one of the Backend.Exporter.EffectType*
    // strings ("flash"/"tint"/"particleHook"); trigger is the seam tag the
    // runtime fires (Shared.EffectTriggerOnSpawn/OnDamaged or a custom tag).
    // The numeric params are nullable so hand-authored minimal documents
    // deserialize as "absent" and resolve to the Default* config values;
    // designer exports always write all of them.
    public struct EffectDescriptorJson
    {
        [JsonPropertyName(BackendConfig.Exporter.FieldName)]
        public string name { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldEffectType)]
        public string effectType { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldTrigger)]
        public string trigger { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldColorRgba)]
        public uint? colorRgba { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldDurationMs)]
        public int? durationMs { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldStrength)]
        public float? strength { get; set; }
    }

    // One per-frame sub-element attachment record (item #6): frameIndex is
    // the GLOBAL atlas frame index (same addressing as HitboxJson), subElement
    // names the designer-bank sub-element, and x/y is the canvas-space pixel
    // position of that sub-element's PIVOT. zOrder orders attachments within
    // the frame (ascending; ties keep document order). flipX/flipY are
    // nullable so hand-authored minimal documents deserialize as "absent" and
    // resolve to false; the designer exporter omits them when false.
    public struct AttachmentJson
    {
        [JsonPropertyName(BackendConfig.Exporter.FieldFrameIndex)]
        public int frameIndex { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldSubElement)]
        public string subElement { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldX)]
        public int x { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldY)]
        public int y { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldZOrder)]
        public int zOrder { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldFlipX)]
        public bool? flipX { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldFlipY)]
        public bool? flipY { get; set; }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct HitboxJson
    {
        [JsonPropertyName(BackendConfig.Exporter.FieldFrameIndex)]
        public int frameIndex { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldHitboxType)]
        public string hitboxType { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldX)]
        public float x { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldY)]
        public float y { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldWidth)]
        public float width { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldHeight)]
        public float height { get; set; }
    }
}
