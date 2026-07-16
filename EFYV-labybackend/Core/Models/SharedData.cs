using System.Collections.Generic;
using System.Text.Json.Serialization;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Models
{
    // Shared Data Model for Hitboxes
    // Used by LabyMake to let artists draw the box.
    // Used by Labyrinth (Unity Importer) to deserialize the raw coordinates.
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
    }

    // Unified JSON Structure
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = BackendConfig.Serialization.SequentialPack)]
    public struct EFYVJsonFormat
    {
        [JsonPropertyName(BackendConfig.Exporter.FieldAssetType)]
        public string assetType { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldProperties)]
        public Dictionary<string, System.Text.Json.JsonElement> properties { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldHitboxes)]
        public List<HitboxJson> hitboxes { get; set; }

        [JsonPropertyName(BackendConfig.Exporter.FieldAtlas)]
        public AtlasMetadataJson? atlas { get; set; }
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
