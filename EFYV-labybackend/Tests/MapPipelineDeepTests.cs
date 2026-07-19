// batch3.6 agent (item #5): the maps + tileset wire contract.
// - FastMapExporter/FastMapImporter: the {EFYM, version, CRC32} envelope,
//   payload round trips, the shared TryValidate gate, atomic publish, and a
//   corruption/truncation matrix that must all land on Malformed.
// - TryValidateTilesetManifest: the shared manifest validator matrix, the
//   documentVersion-5 "tileset" block writer (omit-when-absent), and the
//   FastImporter read-back of tile-sheet documents.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using EFYVBackend.Core.Export;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        private static MapFileData BuildMapData(
            int width,
            int height,
            string tilesetName = "DungeonTiles",
            int propCount = 3,
            uint seed = 0x51DEC0DEu)
        {
            var random = new EFYVBackend.Core.Math.FastRandomState(seed);
            var data = new MapFileData
            {
                Width = width,
                Height = height,
                TilesetName = tilesetName,
                Tiles = new short[width * height],
                Props = new MapPropRecord[propCount]
            };
            for (int index = 0; index < data.Tiles.Length; index++)
            {
                // Mix blanks, palette ids, and out-of-palette ids: all legal shorts.
                data.Tiles[index] = (short)(random.Range(0, 12) - 1);
            }
            for (int index = 0; index < propCount; index++)
            {
                data.Props[index] = new MapPropRecord
                {
                    AssetKey = "Prop_" + index,
                    X = random.Range(-64, 64),
                    Y = random.Range(-64, 64),
                    Scale = 0.5f + (index * 0.25f)
                };
            }
            return data;
        }

        private static void AssertMapDataEqual(MapFileData expected, MapFileData actual)
        {
            AssertEqual(expected.Width, actual.Width);
            AssertEqual(expected.Height, actual.Height);
            AssertEqual(expected.TilesetName ?? string.Empty, actual.TilesetName);
            AssertEqual(expected.Tiles.Length, actual.Tiles.Length);
            for (int index = 0; index < expected.Tiles.Length; index++)
                AssertEqual(expected.Tiles[index], actual.Tiles[index]);
            AssertEqual(expected.Props.Length, actual.Props.Length);
            for (int index = 0; index < expected.Props.Length; index++)
            {
                AssertEqual(expected.Props[index].AssetKey, actual.Props[index].AssetKey);
                AssertEqual(expected.Props[index].X, actual.Props[index].X);
                AssertEqual(expected.Props[index].Y, actual.Props[index].Y);
                AssertEqual(expected.Props[index].Scale, actual.Props[index].Scale);
            }
        }

        private static void TestMapFileRoundTripAndEnvelope()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                // Deterministic round trip with mixed tile ids and props.
                MapFileData data = BuildMapData(7, 5);
                string path = Path.Combine(root, "Crypt.efyvmap");
                FastMapExporter.Export(path, data);
                Assert(File.Exists(path));

                AssertEqual(EfyvParseResult.Valid, FastMapImporter.TryParse(path, out MapFileData parsed));
                AssertMapDataEqual(data, parsed);

                // Envelope bytes: little-endian "EFYM" magic, version, and a
                // CRC that recomputes over exactly the payload region.
                byte[] bytes = File.ReadAllBytes(path);
                AssertEqual((byte)'E', bytes[0]);
                AssertEqual((byte)'F', bytes[1]);
                AssertEqual((byte)'Y', bytes[2]);
                AssertEqual((byte)'M', bytes[3]);
                AssertEqual((uint)BackendConfig.MapFile.FormatVersion, BitConverter.ToUInt32(bytes, BackendConfig.MapFile.VersionOffset));
                uint storedCrc = BitConverter.ToUInt32(bytes, BackendConfig.MapFile.ChecksumOffset);
                AssertEqual(storedCrc, FastCrc32.Compute(
                    new ReadOnlySpan<byte>(bytes, BackendConfig.MapFile.HeaderSizeBytes, bytes.Length - BackendConfig.MapFile.HeaderSizeBytes)));

                // Payload head: width, height, tileset-name length + UTF-8 bytes.
                int offset = BackendConfig.MapFile.HeaderSizeBytes;
                AssertEqual(data.Width, BitConverter.ToInt32(bytes, offset));
                AssertEqual(data.Height, BitConverter.ToInt32(bytes, offset + sizeof(int)));
                int nameLength = BitConverter.ToInt32(bytes, offset + (2 * sizeof(int)));
                AssertEqual(Encoding.UTF8.GetByteCount(data.TilesetName), nameLength);
                AssertEqual(data.TilesetName, Encoding.UTF8.GetString(bytes, offset + (3 * sizeof(int)), nameLength));
                // First tile id sits right after the name (little-endian int16).
                int firstTileOffset = offset + (3 * sizeof(int)) + nameLength;
                AssertEqual(data.Tiles[0], BitConverter.ToInt16(bytes, firstTileOffset));

                // Total size is exact: header + fixed fields + name + tiles + props.
                long expectedLength = BackendConfig.MapFile.HeaderSizeBytes +
                    (3 * sizeof(int)) + nameLength +
                    ((long)data.Tiles.Length * BackendConfig.MapFile.BytesPerTile) +
                    sizeof(int);
                foreach (MapPropRecord prop in data.Props)
                {
                    expectedLength += sizeof(int) + Encoding.UTF8.GetByteCount(prop.AssetKey) +
                        sizeof(int) + sizeof(int) + sizeof(float);
                }
                AssertEqual(expectedLength, new FileInfo(path).Length);

                // Empty tileset reference and zero props: both legal; null
                // tileset name normalizes to empty on the wire.
                MapFileData minimal = BuildMapData(1, 1, null, 0);
                minimal.Tiles[0] = BackendConfig.MapFile.BlankTileId;
                string minimalPath = Path.Combine(root, "Minimal.efyvmap");
                FastMapExporter.Export(minimalPath, minimal);
                AssertEqual(EfyvParseResult.Valid, FastMapImporter.TryParse(minimalPath, out MapFileData minimalParsed));
                AssertEqual(string.Empty, minimalParsed.TilesetName);
                AssertEqual(BackendConfig.MapFile.BlankTileId, minimalParsed.Tiles[0]);
                AssertEqual(0, minimalParsed.Props.Length);

                // Republish over an existing file replaces it atomically and
                // leaves no dotted temporaries behind.
                MapFileData replacement = BuildMapData(2, 3, "Other", 1, 0xBEEFu);
                FastMapExporter.Export(path, replacement);
                AssertEqual(EfyvParseResult.Valid, FastMapImporter.TryParse(path, out MapFileData replacedParsed));
                AssertMapDataEqual(replacement, replacedParsed);
                foreach (string stray in Directory.GetFiles(root))
                {
                    Assert(!Path.GetFileName(stray).StartsWith(
                        BackendConfig.Exporter.TemporaryNamePrefix,
                        StringComparison.Ordinal));
                }

                // Missing file is Missing, not Malformed; so are null/blank paths.
                AssertEqual(EfyvParseResult.Missing, FastMapImporter.TryParse(Path.Combine(root, "absent.efyvmap"), out _));
                AssertEqual(EfyvParseResult.Missing, FastMapImporter.TryParse(null, out _));
                AssertEqual(EfyvParseResult.Missing, FastMapImporter.TryParse("  ", out _));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void TestMapFileValidationMatrix()
        {
            // The shared TryValidate gate: the exporter throws on exactly the
            // shapes the reader would refuse.
            Assert(!FastMapExporter.TryValidate(null));
            Assert(FastMapExporter.TryValidate(BuildMapData(4, 4)));

            MapFileData zeroWidth = BuildMapData(4, 4);
            zeroWidth.Width = 0;
            Assert(!FastMapExporter.TryValidate(zeroWidth));

            MapFileData hugeWidth = BuildMapData(4, 4);
            hugeWidth.Width = BackendConfig.MapFile.MaxMapDimension + 1;
            Assert(!FastMapExporter.TryValidate(hugeWidth));

            MapFileData tileMismatch = BuildMapData(4, 4);
            tileMismatch.Tiles = new short[15];
            Assert(!FastMapExporter.TryValidate(tileMismatch));

            MapFileData nullTiles = BuildMapData(4, 4);
            nullTiles.Tiles = null;
            Assert(!FastMapExporter.TryValidate(nullTiles));

            MapFileData nullProps = BuildMapData(4, 4);
            nullProps.Props = null;
            Assert(!FastMapExporter.TryValidate(nullProps));

            MapFileData tooManyProps = BuildMapData(2, 2, "T", 0);
            tooManyProps.Props = new MapPropRecord[BackendConfig.MapFile.MaxMapProps + 1];
            for (int index = 0; index < tooManyProps.Props.Length; index++)
                tooManyProps.Props[index] = new MapPropRecord { AssetKey = "K", Scale = 1f };
            Assert(!FastMapExporter.TryValidate(tooManyProps));

            foreach (string badStem in new[] { "..", "a/b", "a\\b", "CON", "bad.", "bad ", "", null, "x?y" })
            {
                MapFileData badTileset = BuildMapData(2, 2, "Fine", 0);
                badTileset.TilesetName = badStem;
                // Empty/null tileset names are legal (no tileset); the rest are not.
                AssertEqual(string.IsNullOrEmpty(badStem), FastMapExporter.TryValidate(badTileset));

                if (string.IsNullOrEmpty(badStem)) continue;
                MapFileData badProp = BuildMapData(2, 2, "Fine", 1);
                badProp.Props[0].AssetKey = badStem;
                Assert(!FastMapExporter.TryValidate(badProp));
            }

            MapFileData nanScale = BuildMapData(2, 2, "Fine", 1);
            nanScale.Props[0].Scale = float.NaN;
            Assert(!FastMapExporter.TryValidate(nanScale));
            nanScale.Props[0].Scale = float.PositiveInfinity;
            Assert(!FastMapExporter.TryValidate(nanScale));

            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "Reject.efyvmap");
                AssertThrows<ArgumentException>(() => FastMapExporter.Export(path, nullTiles));
                AssertThrows<ArgumentException>(() => FastMapExporter.Export(path, null));
                AssertThrows<ArgumentException>(() => FastMapExporter.Export(null, BuildMapData(2, 2)));
                AssertThrows<ArgumentException>(() => FastMapExporter.Export("   ", BuildMapData(2, 2)));
                Assert(!File.Exists(path));
                // Traversal in the path NORMALIZES before the containment
                // check (the FastSaveEngine rule): the write lands at the
                // resolved location, never outside its own directory. The
                // designer-facing stem policy is enforced upstream (MapId is
                // a validated safe stem).
                string traversal = Path.Combine(root, "sub", "..", "normalized.efyvmap");
                FastMapExporter.Export(traversal, BuildMapData(2, 2));
                Assert(File.Exists(Path.Combine(root, "normalized.efyvmap")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void TestMapFileCorruptionMatrix()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                MapFileData data = BuildMapData(6, 4, "Tiles", 2);
                string path = Path.Combine(root, "Victim.efyvmap");
                FastMapExporter.Export(path, data);
                byte[] pristine = File.ReadAllBytes(path);
                string mutated = Path.Combine(root, "Mutated.efyvmap");

                void WriteMutant(Action<byte[]> mutate, int? truncateTo = null)
                {
                    byte[] copy = (byte[])pristine.Clone();
                    mutate?.Invoke(copy);
                    if (truncateTo.HasValue)
                    {
                        byte[] shorter = new byte[truncateTo.Value];
                        Array.Copy(copy, shorter, shorter.Length);
                        File.WriteAllBytes(mutated, shorter);
                    }
                    else
                    {
                        File.WriteAllBytes(mutated, copy);
                    }
                }

                // Flipped magic, wrong version, flipped CRC, flipped payload byte.
                WriteMutant(bytes => bytes[0] ^= 0xFF);
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                WriteMutant(bytes => bytes[BackendConfig.MapFile.VersionOffset] = 99);
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                WriteMutant(bytes => bytes[BackendConfig.MapFile.ChecksumOffset + 1] ^= 0x40);
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                WriteMutant(bytes => bytes[bytes.Length - 1] ^= 0x01);
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));

                // Every truncation boundary: empty, header-only, mid-payload.
                WriteMutant(null, 0);
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                WriteMutant(null, BackendConfig.MapFile.HeaderSizeBytes - 1);
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                WriteMutant(null, BackendConfig.MapFile.HeaderSizeBytes);
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                WriteMutant(null, pristine.Length - 3);
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));

                // Trailing garbage after a CRC-valid payload region: the CRC
                // covers the enlarged region, so recompute it - the reader
                // must still refuse on exact-consumption grounds.
                {
                    byte[] enlarged = new byte[pristine.Length + 4];
                    Array.Copy(pristine, enlarged, pristine.Length);
                    var payload = new byte[enlarged.Length - BackendConfig.MapFile.HeaderSizeBytes];
                    Array.Copy(enlarged, BackendConfig.MapFile.HeaderSizeBytes, payload, 0, payload.Length);
                    FastMapExporter.WriteUInt32LittleEndian(
                        enlarged,
                        BackendConfig.MapFile.ChecksumOffset,
                        FastCrc32.Compute(payload));
                    File.WriteAllBytes(mutated, enlarged);
                    AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                }

                // CRC-valid payloads with structural lies: negative prop
                // count, negative string length, oversized declared string.
                void WriteForgedPayload(Action<byte[]> forge)
                {
                    byte[] copy = (byte[])pristine.Clone();
                    forge(copy);
                    var payload = new byte[copy.Length - BackendConfig.MapFile.HeaderSizeBytes];
                    Array.Copy(copy, BackendConfig.MapFile.HeaderSizeBytes, payload, 0, payload.Length);
                    FastMapExporter.WriteUInt32LittleEndian(
                        copy,
                        BackendConfig.MapFile.ChecksumOffset,
                        FastCrc32.Compute(payload));
                    File.WriteAllBytes(mutated, copy);
                }

                int nameLengthOffset = BackendConfig.MapFile.HeaderSizeBytes + (2 * sizeof(int));
                WriteForgedPayload(bytes => FastMapExporter.WriteUInt32LittleEndian(
                    bytes, nameLengthOffset, unchecked((uint)-5)));
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                WriteForgedPayload(bytes => FastMapExporter.WriteUInt32LittleEndian(
                    bytes, nameLengthOffset, 0x7FFFFFFFu));
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));
                // Oversized declared dimensions (the tile payload cannot cover them).
                WriteForgedPayload(bytes => FastMapExporter.WriteUInt32LittleEndian(
                    bytes, BackendConfig.MapFile.HeaderSizeBytes, (uint)(BackendConfig.MapFile.MaxMapDimension + 1)));
                AssertEqual(EfyvParseResult.Malformed, FastMapImporter.TryParse(mutated, out _));

                // The pristine file still parses after all that (no shared state).
                AssertEqual(EfyvParseResult.Valid, FastMapImporter.TryParse(path, out MapFileData reparsed));
                AssertMapDataEqual(data, reparsed);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static TilesetManifestJson TilesetManifest(int tileSize, params string[] tiles)
        {
            return new TilesetManifestJson
            {
                tileSize = tileSize,
                tiles = new List<string>(tiles)
            };
        }

        private static void TestTilesetManifestValidatorMatrix()
        {
            // Standalone manifest (no atlas riding along).
            Assert(FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16, "Grass", "Dirt"), null, out TilesetManifestError error));
            AssertEqual(TilesetManifestError.None, error);

            Assert(!FastExporter.TryValidateTilesetManifest(
                TilesetManifest(0, "Grass"), null, out error));
            AssertEqual(TilesetManifestError.TileSize, error);

            Assert(!FastExporter.TryValidateTilesetManifest(
                new TilesetManifestJson { tileSize = 16, tiles = null }, null, out error));
            AssertEqual(TilesetManifestError.TilesMissing, error);

            Assert(!FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16), null, out error));
            AssertEqual(TilesetManifestError.TileCount, error);

            var overCap = new List<string>();
            for (int index = 0; index <= BackendConfig.Exporter.MaxTilesPerTileset; index++)
                overCap.Add("T" + index);
            Assert(!FastExporter.TryValidateTilesetManifest(
                new TilesetManifestJson { tileSize = 16, tiles = overCap }, null, out error));
            AssertEqual(TilesetManifestError.TileCount, error);

            Assert(!FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16, "Grass", "  "), null, out error));
            AssertEqual(TilesetManifestError.TileName, error);
            Assert(!FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16, (string)null), null, out error));
            AssertEqual(TilesetManifestError.TileName, error);
            Assert(!FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16, new string('n', BackendConfig.Exporter.MaxTileNameLength + 1)), null, out error));
            AssertEqual(TilesetManifestError.TileName, error);
            Assert(FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16, new string('n', BackendConfig.Exporter.MaxTileNameLength)), null, out error));
            AssertEqual(TilesetManifestError.None, error);

            // With a sibling atlas: frames must be exactly tileSize-square
            // and the sheet must have capacity for every tile.
            var atlas = new AtlasMetadataJson
            {
                formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                frameWidth = 16,
                frameHeight = 16,
                atlasWidth = 32,
                atlasHeight = 32,
                animations = new List<AnimationMetadataJson>
                {
                    new AnimationMetadataJson { name = "Tiles", fps = 1, startFrame = 0, frameCount = 3 }
                }
            };
            Assert(FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16, "A", "B", "C"), atlas, out error));
            AssertEqual(TilesetManifestError.None, error);
            // Exactly at capacity is legal.
            Assert(FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16, "A", "B", "C", "D"), atlas, out error));
            AssertEqual(TilesetManifestError.None, error);
            Assert(!FastExporter.TryValidateTilesetManifest(
                TilesetManifest(16, "A", "B", "C", "D", "E"), atlas, out error));
            AssertEqual(TilesetManifestError.AtlasCapacity, error);
            Assert(!FastExporter.TryValidateTilesetManifest(
                TilesetManifest(8, "A"), atlas, out error));
            AssertEqual(TilesetManifestError.AtlasFrameMismatch, error);
        }

        private static void TestTilesetWireWriterAndRoundTrip()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var properties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldAssetName] = "DungeonTiles"
                };
                var atlas = new AtlasMetadataJson
                {
                    formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                    frameWidth = 2,
                    frameHeight = 2,
                    atlasWidth = 4,
                    atlasHeight = 2,
                    animations = new List<AnimationMetadataJson>
                    {
                        new AnimationMetadataJson { name = "Tiles", fps = 1, startFrame = 0, frameCount = 2 }
                    }
                };
                var pixels = new uint[4 * 2];

                // A manifest failing the shared gate never publishes anything.
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(
                    root,
                    "GameAssetData",
                    properties,
                    new List<HitboxJson>(),
                    pixels,
                    4,
                    2,
                    atlas,
                    "GameAssetData",
                    null,
                    TilesetManifest(3, "A")));
                AssertEqual(0, Directory.GetFiles(root).Length);

                // Valid tileset publish: documentVersion 5 with the ordered
                // {tileSize, tiles} block after the atlas.
                FastExporter.PushToUnityLiveHook(
                    root,
                    "GameAssetData",
                    properties,
                    new List<HitboxJson>(),
                    pixels,
                    4,
                    2,
                    atlas,
                    "GameAssetData",
                    null,
                    TilesetManifest(2, "Grass", "Wall"));
                string documentPath = Path.Combine(root, "DungeonTiles" + BackendConfig.Exporter.EfyvExtension);
                Assert(File.Exists(documentPath));

                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(documentPath)))
                {
                    AssertEqual(
                        BackendConfig.Exporter.CurrentDocumentVersion,
                        document.RootElement.GetProperty(BackendConfig.Exporter.FieldDocumentVersion).GetInt32());
                    JsonElement tileset = document.RootElement.GetProperty(BackendConfig.Exporter.FieldTileset);
                    AssertEqual(2, tileset.GetProperty(BackendConfig.Exporter.FieldTileSize).GetInt32());
                    JsonElement tiles = tileset.GetProperty(BackendConfig.Exporter.FieldTiles);
                    AssertEqual(2, tiles.GetArrayLength());
                    AssertEqual("Grass", tiles[0].GetString());
                    AssertEqual("Wall", tiles[1].GetString());
                }

                // FastImporter reads the block back; list index = tile id.
                AssertEqual(EfyvParseResult.Valid, FastImporter.TryParse(documentPath, out EFYVJsonFormat parsed));
                Assert(parsed.tileset.HasValue);
                AssertEqual(2, parsed.tileset.Value.tileSize);
                AssertEqual("Grass", parsed.tileset.Value.tiles[0]);
                AssertEqual("Wall", parsed.tileset.Value.tiles[1]);
                AssertEqual(BackendConfig.Exporter.CurrentDocumentVersion, parsed.EffectiveDocumentVersion);

                // A null manifest OMITS the member entirely: non-tileset
                // documents carry no "tileset" key at all.
                var plainProperties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldAssetName] = "PlainAsset"
                };
                FastExporter.PushToUnityLiveHook(
                    root,
                    "GameAssetData",
                    plainProperties,
                    new List<HitboxJson>(),
                    pixels,
                    4,
                    2,
                    atlas,
                    "GameAssetData",
                    null,
                    null);
                string plainPath = Path.Combine(root, "PlainAsset" + BackendConfig.Exporter.EfyvExtension);
                string plainJson = File.ReadAllText(plainPath);
                Assert(!plainJson.Contains(
                    "\"" + BackendConfig.Exporter.FieldTileset + "\"",
                    StringComparison.Ordinal));
                AssertEqual(EfyvParseResult.Valid, FastImporter.TryParse(plainPath, out EFYVJsonFormat plainParsed));
                Assert(!plainParsed.tileset.HasValue);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
