using System;
using System.Collections.Generic;
using EFYVBackend.Core.Models;

namespace EFYV.Labyrinth.Artifacts
{
    public sealed class LabyMakeSnapshot
    {
        internal LabyMakeSnapshot(
            byte[] sourceBytes,
            string assetType,
            string? baseAssetType,
            int frameWidth,
            int frameHeight,
            uint[][] frames,
            Dictionary<string, object> properties,
            List<HitboxJson> hitboxes,
            AtlasMetadataJson atlas,
            List<AttachmentJson>? attachments,
            TilesetManifestJson? tileset)
        {
            SourceBytes = sourceBytes;
            AssetType = assetType;
            BaseAssetType = baseAssetType;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            Frames = frames;
            Properties = properties;
            Hitboxes = hitboxes;
            Atlas = atlas;
            Attachments = attachments;
            Tileset = tileset;
        }

        public byte[] SourceBytes { get; }
        public string AssetType { get; }
        public string? BaseAssetType { get; }
        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public uint[][] Frames { get; }
        public Dictionary<string, object> Properties { get; }
        public List<HitboxJson> Hitboxes { get; }
        public AtlasMetadataJson Atlas { get; }
        public List<AttachmentJson>? Attachments { get; }
        public TilesetManifestJson? Tileset { get; }
    }

    public sealed class LabyMakeArtifactBundle
    {
        public LabyMakeArtifactBundle(string stem, byte[] png, byte[] metadata, byte[] bundle, string sha256)
        {
            Stem = stem;
            Png = png;
            Metadata = metadata;
            Bundle = bundle;
            Sha256 = sha256;
        }

        public string Stem { get; }
        public byte[] Png { get; }
        public byte[] Metadata { get; }
        public byte[] Bundle { get; }
        public string Sha256 { get; }
        public string ArtifactReference => "sha256:" + Sha256;
        public string Filename => Stem + "-unity-handoff.zip";
        public const string ContentType = "application/zip";
    }
}
