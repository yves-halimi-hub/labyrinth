"""Deep byte-level and contract tests for PixelArtApp/MockExporter.py.

These extend Tests/test_mock_exporter.py (left untouched) with:

- independent byte-for-byte PNG reference checks (signature, chunk offsets,
  CRCs, zlib/adler32 framing, decoded pixel plane),
- the full .efyvlaby JSON text/type contract mirrored from the backend
  exporter (EFYV-labybackend/Core/Export/FastExporter.cs field order plus
  the ValidateMetadata atlas-geometry rules),
- publication ordering, byte determinism, stale-temp handling, and export
  directory behavior,
- adversarial and Windows-edge entity names beyond the base suite
  (SafePathPolicy.IsSafeFileStem is the reference policy).

Former divergences from the backend exporter (zero/negative dimensions,
RGBA component count, deterministic truncating temp names) are FIXED in
MockExporter.py; the tests below assert the fixed behavior.
"""

import contextlib
import importlib.util
import io
import json
import os
import pathlib
import re
import struct
import tempfile
import unittest
import uuid
import zlib
from unittest import mock


MODULE_PATH = pathlib.Path(__file__).resolve().parents[1] / "PixelArtApp" / "MockExporter.py"
SPEC = importlib.util.spec_from_file_location("efyv_mock_exporter_deep", MODULE_PATH)
EXPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORTER)


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
IEND_CRC = 0xAE426082  # CRC-32 of the bytes b"IEND"; a fixed PNG-spec constant.
EXPORT_COLOR = (255, 0, 0, 255)
EXPORT_SIZE = 64


def reference_chunk(chunk_type, payload):
    """Independent PNG chunk builder (does not reuse EXPORTER._png_chunk)."""
    crc = zlib.crc32(payload, zlib.crc32(chunk_type)) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + chunk_type + payload + struct.pack(">I", crc)


def walk_png(data):
    """Strict offset-accurate chunk walk; returns [(offset, type, payload)].

    Raises AssertionError on a bad signature, truncated chunk, CRC mismatch,
    or trailing bytes after the final chunk.
    """
    if data[:8] != PNG_SIGNATURE:
        raise AssertionError("bad PNG signature")
    chunks = []
    offset = 8
    while offset < len(data):
        if offset + 12 > len(data):
            raise AssertionError("truncated chunk header")
        (length,) = struct.unpack(">I", data[offset:offset + 4])
        chunk_type = data[offset + 4:offset + 8]
        payload = data[offset + 8:offset + 8 + length]
        if len(payload) != length:
            raise AssertionError("truncated chunk payload")
        (crc,) = struct.unpack(">I", data[offset + 8 + length:offset + 12 + length])
        expected = zlib.crc32(payload, zlib.crc32(chunk_type)) & 0xFFFFFFFF
        if crc != expected:
            raise AssertionError("bad chunk CRC")
        chunks.append((offset, chunk_type, payload))
        offset += 12 + length
    if offset != len(data):
        raise AssertionError("trailing bytes after final chunk")
    return chunks


def quiet_export(directory, name):
    """Run the exporter with stdout captured; returns the captured text."""
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        EXPORTER.mock_pixel_app_export(directory, name)
    return output.getvalue()


def exported_paths(root, name):
    base = f"{name}_Down"
    return pathlib.Path(root) / f"{base}.png", pathlib.Path(root) / f"{base}.efyvlaby"


def expected_metadata(name):
    """The exact document contract (field order mirrors FastExporter.WriteJson)."""
    return {
        "documentVersion": 5,
        "assetType": "EnemyData",
        "baseAssetType": "EnemyData",
        "properties": {
            "entityName": name,
            "maxHealth": 25.0,
            "baseSpeed": 1.5,
            "damageToPlayer": 5.0,
            "experienceValue": 10.0,
            "facing": "Down",
        },
        "hitboxes": [
            {
                "frameIndex": 0,
                "hitboxType": "Hurtbox",
                "x": 0.0,
                "y": 0.0,
                "width": 1.0,
                "height": 1.0,
            }
        ],
        "atlas": {
            "formatVersion": 1,
            "frameWidth": 64,
            "frameHeight": 64,
            "atlasWidth": 64,
            "atlasHeight": 64,
            "animations": [
                {"name": "Idle", "fps": 12, "startFrame": 0, "frameCount": 1}
            ],
        },
    }


class PngByteStructureDeepTests(unittest.TestCase):
    def test_exported_png_matches_independent_reference_bytes(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-refpng-") as root:
            quiet_export(root, "RefBytes")
            png_path, _ = exported_paths(root, "RefBytes")
            row = b"\x00" + bytes(EXPORT_COLOR) * EXPORT_SIZE
            expected = (
                PNG_SIGNATURE
                + reference_chunk(b"IHDR", struct.pack(">IIBBBBB", EXPORT_SIZE, EXPORT_SIZE, 8, 6, 0, 0, 0))
                + reference_chunk(b"IDAT", zlib.compress(row * EXPORT_SIZE))
                + reference_chunk(b"IEND", b"")
            )
            self.assertEqual(expected, png_path.read_bytes())

    def test_exported_png_chunk_layout_offsets_and_crcs(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-layout-") as root:
            quiet_export(root, "Layout")
            png_path, _ = exported_paths(root, "Layout")
            data = png_path.read_bytes()

            chunks = walk_png(data)  # verifies every CRC and exact EOF
            self.assertEqual([b"IHDR", b"IDAT", b"IEND"], [kind for _, kind, _ in chunks])

            # IHDR must start immediately after the 8-byte signature.
            ihdr_offset, _, ihdr = chunks[0]
            self.assertEqual(8, ihdr_offset)
            self.assertEqual(13, struct.unpack(">I", data[8:12])[0])
            self.assertEqual(b"IHDR", data[12:16])
            self.assertEqual(13, len(ihdr))
            width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", ihdr)
            self.assertEqual((EXPORT_SIZE, EXPORT_SIZE, 8, 6, 0, 0, 0),
                             (width, height, depth, color, compression, filtering, interlace))

            # IHDR chunk is 12 + 13 bytes, so IDAT begins at offset 33.
            self.assertEqual(33, chunks[1][0])

            # The file must end with an empty IEND carrying the spec CRC.
            self.assertEqual(struct.pack(">I", 0) + b"IEND" + struct.pack(">I", IEND_CRC), data[-12:])
            self.assertEqual(len(data), chunks[2][0] + 12)

            # Decoded pixel plane: filter byte 0 then solid red, row by row.
            pixels = zlib.decompress(chunks[1][2])
            self.assertEqual(EXPORT_SIZE * (1 + 4 * EXPORT_SIZE), len(pixels))
            expected_row = b"\x00" + bytes(EXPORT_COLOR) * EXPORT_SIZE
            for row_index in range(EXPORT_SIZE):
                row = pixels[row_index * len(expected_row):(row_index + 1) * len(expected_row)]
                self.assertEqual(expected_row, row, f"row {row_index}")

    def test_idat_zlib_stream_has_valid_header_and_adler32(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-zlib-") as root:
            quiet_export(root, "ZlibFrame")
            png_path, _ = exported_paths(root, "ZlibFrame")
            chunks = walk_png(png_path.read_bytes())
            idat = chunks[1][2]

            # zlib header: CM=8 (deflate), and CMF/FLG checksum divisible by 31.
            self.assertEqual(8, idat[0] & 0x0F)
            self.assertEqual(0, ((idat[0] << 8) | idat[1]) % 31)

            # Trailer: big-endian Adler-32 of the decompressed stream.
            decompressed = zlib.decompress(idat)
            self.assertEqual(struct.pack(">I", zlib.adler32(decompressed)), idat[-4:])

    def test_png_helper_boundary_sizes_and_extreme_colors(self):
        cases = (
            (1, 1, (0, 0, 0, 0)),
            (256, 1, (255, 255, 255, 255)),
            (1, 256, (1, 2, 3, 4)),
            (5, 7, (9, 8, 7, 6)),
        )
        with tempfile.TemporaryDirectory(prefix="efyv-deep-bounds-") as root:
            for width, height, rgba in cases:
                with self.subTest(width=width, height=height, rgba=rgba):
                    path = pathlib.Path(root) / f"b{width}x{height}.png"
                    EXPORTER._write_solid_rgba_png(path, width, height, rgba)
                    chunks = walk_png(path.read_bytes())
                    self.assertEqual((width, height), struct.unpack(">II", chunks[0][2][:8]))
                    pixels = zlib.decompress(chunks[1][2])
                    self.assertEqual((b"\x00" + bytes(rgba) * width) * height, pixels)

    def test_png_helper_rejects_zero_and_negative_dimensions(self):
        # FIXED (was pinned as a BUG): _write_solid_rgba_png now mirrors the
        # backend FastPngEncoder, which throws ArgumentOutOfRangeException
        # for width/height <= 0. ValueError is raised before any file is
        # created, replacing the old zero-dimension PNG output and the old
        # struct.error surface for negatives.
        with tempfile.TemporaryDirectory(prefix="efyv-deep-baddim-") as root:
            for width, height in ((0, 2), (3, 0), (0, 0), (-1, 4), (4, -1)):
                with self.subTest(width=width, height=height):
                    path = pathlib.Path(root) / f"d{width}x{height}.png"
                    with self.assertRaises(ValueError):
                        EXPORTER._write_solid_rgba_png(path, width, height, EXPORT_COLOR)
                    self.assertFalse(path.exists())

    def test_png_helper_rejects_wrong_rgba_component_count(self):
        # FIXED (was pinned as a BUG): the backend encoder enforces 4-byte
        # pixels (sizeof(T) == sizeof(uint)); _write_solid_rgba_png now
        # rejects any other component count with ValueError instead of
        # silently writing a pixel stream that contradicts the RGBA IHDR.
        with tempfile.TemporaryDirectory(prefix="efyv-deep-rgbcount-") as root:
            for rgba in ((), (9,), (9, 8), (9, 8, 7), (9, 8, 7, 6, 5)):
                with self.subTest(rgba=rgba):
                    path = pathlib.Path(root) / "count.png"
                    with self.assertRaises(ValueError):
                        EXPORTER._write_solid_rgba_png(path, 2, 1, rgba)
                    self.assertFalse(path.exists())

    def test_png_helper_refuses_to_overwrite_an_existing_file(self):
        # The helper opens with exclusive-create semantics (mode "xb"),
        # mirroring the backend's FileMode.CreateNew for temporary outputs.
        with tempfile.TemporaryDirectory(prefix="efyv-deep-exclusive-") as root:
            path = pathlib.Path(root) / "occupied.png"
            path.write_bytes(b"occupied")
            with self.assertRaises(FileExistsError):
                EXPORTER._write_solid_rgba_png(path, 2, 2, EXPORT_COLOR)
            self.assertEqual(b"occupied", path.read_bytes())

    def test_png_helper_rejects_out_of_range_color_components(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-range-") as root:
            for rgba in ((256, 0, 0, 0), (0, 0, 0, -1)):
                with self.subTest(rgba=rgba):
                    path = pathlib.Path(root) / "range.png"
                    with self.assertRaises(ValueError):
                        EXPORTER._write_solid_rgba_png(path, 2, 2, rgba)
                    self.assertFalse(path.exists())


class MetadataContractDeepTests(unittest.TestCase):
    def test_metadata_full_serialized_text_matches_reference_document(self):
        # Byte-for-byte text contract (modulo platform newlines): pins key
        # order to the backend writer sequence (documentVersion, assetType,
        # baseAssetType, properties, hitboxes, atlas), the 4-space indent,
        # and every value.
        with tempfile.TemporaryDirectory(prefix="efyv-deep-jsontext-") as root:
            quiet_export(root, "TextPin")
            _, metadata_path = exported_paths(root, "TextPin")
            self.assertEqual(
                json.dumps(expected_metadata("TextPin"), indent=4),
                metadata_path.read_text(encoding="utf-8"),
            )

    def test_metadata_field_types_are_numerically_faithful(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-types-") as root:
            quiet_export(root, "TypeProbe")
            _, metadata_path = exported_paths(root, "TypeProbe")
            document = json.loads(metadata_path.read_text(encoding="utf-8"))

            self.assertEqual(
                ["documentVersion", "assetType", "baseAssetType", "properties", "hitboxes", "atlas"],
                list(document),
            )
            self.assertIs(type(document["documentVersion"]), int)
            self.assertIs(type(document["baseAssetType"]), str)
            properties = document["properties"]
            self.assertEqual(
                ["entityName", "maxHealth", "baseSpeed", "damageToPlayer", "experienceValue", "facing"],
                list(properties),
            )
            self.assertIs(type(properties["entityName"]), str)
            for field in ("maxHealth", "baseSpeed", "damageToPlayer", "experienceValue"):
                self.assertIs(type(properties[field]), float, field)
            self.assertIs(type(properties["facing"]), str)

            self.assertEqual(1, len(document["hitboxes"]))
            hitbox = document["hitboxes"][0]
            self.assertEqual(["frameIndex", "hitboxType", "x", "y", "width", "height"], list(hitbox))
            self.assertIs(type(hitbox["frameIndex"]), int)
            self.assertIs(type(hitbox["hitboxType"]), str)
            for field in ("x", "y", "width", "height"):
                self.assertIs(type(hitbox[field]), float, field)

            atlas = document["atlas"]
            self.assertEqual(
                ["formatVersion", "frameWidth", "frameHeight", "atlasWidth", "atlasHeight", "animations"],
                list(atlas),
            )
            for field in ("formatVersion", "frameWidth", "frameHeight", "atlasWidth", "atlasHeight"):
                self.assertIs(type(atlas[field]), int, field)
            self.assertIs(type(atlas["animations"]), list)
            animation = atlas["animations"][0]
            self.assertEqual(["name", "fps", "startFrame", "frameCount"], list(animation))
            self.assertIs(type(animation["name"]), str)
            for field in ("fps", "startFrame", "frameCount"):
                self.assertIs(type(animation[field]), int, field)

    def test_metadata_bytes_are_ascii_utf8_without_bom(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-encoding-") as root:
            quiet_export(root, "AsciiProbe01")
            _, metadata_path = exported_paths(root, "AsciiProbe01")
            raw = metadata_path.read_bytes()
            self.assertEqual(b"{", raw[:1])  # implies no UTF-8 BOM
            self.assertEqual(b"}", raw[-1:])  # no trailing newline
            self.assertTrue(all(byte < 0x80 for byte in raw))
            json.loads(raw.decode("utf-8", errors="strict"))

    def test_unicode_entity_name_round_trips_and_is_ascii_escaped(self):
        name = "Bät_Ω"  # Bät_Ω: legal filename, exercises escaping
        with tempfile.TemporaryDirectory(prefix="efyv-deep-unicode-") as root:
            quiet_export(root, name)
            png_path, metadata_path = exported_paths(root, name)
            self.assertTrue(png_path.is_file())
            self.assertTrue(metadata_path.is_file())
            walk_png(png_path.read_bytes())

            raw = metadata_path.read_bytes()
            # json.dump defaults to ensure_ascii=True, so the file stays
            # pure ASCII even for non-ASCII names (the backend Utf8JsonWriter
            # also escapes non-ASCII by default) -- and must round-trip.
            self.assertTrue(all(byte < 0x80 for byte in raw))
            document = json.loads(raw.decode("ascii"))
            self.assertEqual(name, document["properties"]["entityName"])

    def test_atlas_geometry_satisfies_backend_validation_rules(self):
        # Mirror of FastExporter.ValidateMetadata: the mock's document must
        # stay importable by the strict backend/Unity contract.
        with tempfile.TemporaryDirectory(prefix="efyv-deep-atlas-") as root:
            quiet_export(root, "AtlasRules")
            png_path, metadata_path = exported_paths(root, "AtlasRules")
            document = json.loads(metadata_path.read_text(encoding="utf-8"))
            atlas = document["atlas"]

            self.assertEqual(1, atlas["formatVersion"])  # CurrentFormatVersion
            self.assertGreater(atlas["frameWidth"], 0)
            self.assertGreater(atlas["frameHeight"], 0)
            self.assertEqual(0, atlas["atlasWidth"] % atlas["frameWidth"])
            self.assertEqual(0, atlas["atlasHeight"] % atlas["frameHeight"])

            capacity = (atlas["atlasWidth"] // atlas["frameWidth"]) * (atlas["atlasHeight"] // atlas["frameHeight"])
            previous_end = 0
            for animation in atlas["animations"]:
                self.assertTrue(animation["name"].strip())
                self.assertGreater(animation["fps"], 0)
                self.assertGreaterEqual(animation["startFrame"], 0)
                self.assertGreater(animation["frameCount"], 0)
                self.assertGreaterEqual(animation["startFrame"], previous_end)
                self.assertLessEqual(animation["startFrame"] + animation["frameCount"], capacity)
                previous_end = animation["startFrame"] + animation["frameCount"]

            # Cross-file consistency: declared atlas dims == actual PNG IHDR.
            chunks = walk_png(png_path.read_bytes())
            self.assertEqual(
                (atlas["atlasWidth"], atlas["atlasHeight"]),
                struct.unpack(">II", chunks[0][2][:8]),
            )

            # Hitbox geometry stays inside the unit frame used by the importer.
            for hitbox in document["hitboxes"]:
                self.assertGreaterEqual(hitbox["frameIndex"], 0)
                self.assertLess(hitbox["frameIndex"], capacity)
                self.assertGreaterEqual(hitbox["x"], 0.0)
                self.assertGreaterEqual(hitbox["y"], 0.0)
                self.assertGreater(hitbox["width"], 0.0)
                self.assertGreater(hitbox["height"], 0.0)


class ExportBehaviorDeepTests(unittest.TestCase):
    def test_exports_are_byte_identical_across_directories_and_runs(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-det-a-") as first_root, \
                tempfile.TemporaryDirectory(prefix="efyv-deep-det-b-") as second_root:
            quiet_export(first_root, "SameBytes")
            quiet_export(second_root, "SameBytes")
            first_png, first_metadata = exported_paths(first_root, "SameBytes")
            second_png, second_metadata = exported_paths(second_root, "SameBytes")
            self.assertEqual(first_png.read_bytes(), second_png.read_bytes())
            self.assertEqual(first_metadata.read_bytes(), second_metadata.read_bytes())

            # Re-export over existing outputs must also be byte-stable.
            before_png = first_png.read_bytes()
            before_metadata = first_metadata.read_bytes()
            quiet_export(first_root, "SameBytes")
            self.assertEqual(before_png, first_png.read_bytes())
            self.assertEqual(before_metadata, first_metadata.read_bytes())

    def test_png_is_published_before_metadata(self):
        # Contract from the module comment ("metadata is the authoritative
        # completion signal") and from FastExporter: the PNG must land first
        # so a watcher triggered by the metadata file always sees the image.
        with tempfile.TemporaryDirectory(prefix="efyv-deep-order-") as root:
            publishes = []
            real_replace = os.replace

            def recording_replace(source, destination):
                publishes.append((os.fspath(source), os.fspath(destination)))
                real_replace(source, destination)

            with mock.patch("os.replace", side_effect=recording_replace):
                quiet_export(root, "OrderProbe")

            self.assertEqual(2, len(publishes))
            self.assertTrue(publishes[0][0].endswith(".png.tmp"))
            self.assertTrue(publishes[0][1].endswith("OrderProbe_Down.png"))
            self.assertTrue(publishes[1][0].endswith(".efyvlaby.tmp"))
            self.assertTrue(publishes[1][1].endswith("OrderProbe_Down.efyvlaby"))

    def test_temp_names_are_guid_style_and_unique_per_export(self):
        # FIXED (was pinned as a BUG divergence): temp names now follow the
        # backend FastExporter convention ".<stem>.<guid N><final ext>.tmp"
        # instead of the old deterministic truncating "<final>.tmp", so two
        # concurrent exports of the same entity cannot share a temp file.
        temp_pattern = re.compile(r"^\.Guid_Down\.[0-9a-f]{32}\.(?:png|efyvlaby)\.tmp$")
        with tempfile.TemporaryDirectory(prefix="efyv-deep-guidtmp-") as root:
            publishes = []
            real_replace = os.replace

            def recording_replace(source, destination):
                publishes.append((os.fspath(source), os.fspath(destination)))
                real_replace(source, destination)

            with mock.patch("os.replace", side_effect=recording_replace):
                quiet_export(root, "Guid")
                quiet_export(root, "Guid")

            self.assertEqual(4, len(publishes))
            for source, _ in publishes:
                self.assertRegex(os.path.basename(source), temp_pattern)
            # Both files of one export share one GUID stem; separate exports
            # must not reuse it.
            first_stems = {os.path.basename(s).split(".")[2] for s, _ in publishes[:2]}
            second_stems = {os.path.basename(s).split(".")[2] for s, _ in publishes[2:]}
            self.assertEqual(1, len(first_stems))
            self.assertEqual(1, len(second_stems))
            self.assertNotEqual(first_stems, second_stems)
            self.assertEqual([], list(pathlib.Path(root).glob("*.tmp")))

    def test_stale_junk_temp_files_are_left_untouched(self):
        # With GUID temp names the exporter never consumes another writer's
        # stale "<final>.tmp" junk; the final outputs are valid regardless.
        with tempfile.TemporaryDirectory(prefix="efyv-deep-stale-") as root:
            png_path, metadata_path = exported_paths(root, "Stale")
            stale_png = pathlib.Path(f"{png_path}.tmp")
            stale_metadata = pathlib.Path(f"{metadata_path}.tmp")
            stale_png.write_bytes(b"\xde\xad\xbe\xef")
            stale_metadata.write_bytes(b"not json")

            quiet_export(root, "Stale")

            self.assertEqual(b"\xde\xad\xbe\xef", stale_png.read_bytes())
            self.assertEqual(b"not json", stale_metadata.read_bytes())
            walk_png(png_path.read_bytes())
            document = json.loads(metadata_path.read_text(encoding="utf-8"))
            self.assertEqual("Stale", document["properties"]["entityName"])

    def test_colliding_temp_file_fails_exclusively_and_is_cleaned_up(self):
        # Exclusive-create semantics: if the exporter's own temp path already
        # exists (forced here by pinning the GUID), the export fails with
        # FileExistsError instead of silently truncating, and the backend-
        # style DeleteIfPresent cleanup removes the temp path.
        pinned = uuid.UUID(int=0x1234567890ABCDEF1234567890ABCDEF)
        with tempfile.TemporaryDirectory(prefix="efyv-deep-collide-tmp-") as root:
            blocker = pathlib.Path(root) / f".Blocked_Down.{pinned.hex}.png.tmp"
            blocker.write_bytes(b"occupied")
            output = io.StringIO()
            with mock.patch.object(EXPORTER.uuid, "uuid4", return_value=pinned):
                with contextlib.redirect_stdout(output):
                    with self.assertRaises(FileExistsError):
                        EXPORTER.mock_pixel_app_export(root, "Blocked")
            png_path, metadata_path = exported_paths(root, "Blocked")
            self.assertFalse(png_path.exists())
            self.assertFalse(metadata_path.exists())
            self.assertEqual([], list(pathlib.Path(root).glob("*.tmp")))

    def test_nested_export_directory_is_created(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-nested-") as root:
            nested = os.path.join(root, "a", "b", "c")
            quiet_export(nested, "NestProbe")
            png_path, metadata_path = exported_paths(nested, "NestProbe")
            self.assertTrue(png_path.is_file())
            self.assertTrue(metadata_path.is_file())

    def test_export_directory_colliding_with_file_raises_fileexistserror(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-collide-") as root:
            blocked = pathlib.Path(root) / "blocked"
            blocked.write_bytes(b"occupied")
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                with self.assertRaises(FileExistsError):
                    EXPORTER.mock_pixel_app_export(str(blocked), "CollideProbe")
            self.assertEqual(b"occupied", blocked.read_bytes())
            self.assertEqual([], list(pathlib.Path(root).glob("*.tmp")))
            self.assertEqual([blocked], list(pathlib.Path(root).iterdir()))

    def test_invalid_name_fails_before_any_output_or_directory_creation(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-silent-") as root:
            target = pathlib.Path(root) / "never_created"
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                with self.assertRaises(ValueError):
                    EXPORTER.mock_pixel_app_export(str(target), "bad|name")
            self.assertEqual("", output.getvalue())  # validation precedes any print
            self.assertFalse(target.exists())

    def test_stdout_announces_exact_artifact_paths_in_order(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-stdout-") as root:
            output = quiet_export(root, "EchoProbe")
            png_path = os.path.join(root, "EchoProbe_Down.png")
            metadata_path = os.path.join(root, "EchoProbe_Down.efyvlaby")
            self.assertIn("--- EFYV Custom Pixel Art App (Mock) ---", output)
            sprite_index = output.index(f"Created Sprite: {png_path}")
            metadata_index = output.index(f"Created Metadata: {metadata_path}")
            complete_index = output.index("Export complete!")
            self.assertLess(sprite_index, metadata_index)
            self.assertLess(metadata_index, complete_index)


class NameValidationDeepTests(unittest.TestCase):
    def test_rejects_non_string_inputs(self):
        for name in (5, 1.5, True, b"BasicBat", ["BasicBat"], object()):
            with self.subTest(name=name):
                with self.assertRaises(ValueError):
                    EXPORTER._validate_entity_name(name)

    def test_rejects_remaining_invalid_characters_and_unicode_whitespace(self):
        # Complements the base suite (which covers : * ? NUL-byte and \n).
        invalid = (
            "bad<name", "bad>name", 'bad"name', "bad|name",
            "bad\tname", "bad\rname", "bad\x1fname", "\t", " ",
        )
        for name in invalid:
            with self.subTest(name=name):
                with self.assertRaises(ValueError):
                    EXPORTER._validate_entity_name(name)

    def test_rejects_reserved_device_names_with_multiple_extensions_and_mixed_case(self):
        invalid = ("CoN.tar.gz", "Com5.a.b", "lPt3.x", "NUL.x.y.z", "Prn.é", "aUx.1")
        for name in invalid:
            with self.subTest(name=name):
                with self.assertRaises(ValueError):
                    EXPORTER._validate_entity_name(name)

    def test_accepts_windows_safe_edge_case_stems(self):
        # All of these are also accepted by the backend reference policy
        # (SafePathPolicy.IsSafeFileStem): leading dots, non-reserved
        # near-miss device names, DEL (0x7F, not a Windows-invalid char),
        # non-ASCII letters, a superscript digit after COM (not 1-9), and a
        # long stem (neither layer enforces a length limit).
        accepted = (
            ".hidden", "..name", "COM0", "COM10", "COM1x", "LPT0", "LPT10",
            "CONSOLE", "CONtext", "AUXILIARY", "NULL", "PRNfile",
            "na\x7fme", "éclair", "COM¹", "internal space",
            "a" * 300,
        )
        for name in accepted:
            with self.subTest(name=name):
                EXPORTER._validate_entity_name(name)  # must not raise

    def test_edge_case_stems_export_real_files(self):
        with tempfile.TemporaryDirectory(prefix="efyv-deep-edge-") as root:
            for name in (".hidden", "COM10"):
                with self.subTest(name=name):
                    quiet_export(root, name)
                    png_path, metadata_path = exported_paths(root, name)
                    self.assertTrue(png_path.is_file())
                    self.assertTrue(metadata_path.is_file())
                    walk_png(png_path.read_bytes())
                    document = json.loads(metadata_path.read_text(encoding="utf-8"))
                    self.assertEqual(name, document["properties"]["entityName"])
            self.assertEqual([], list(pathlib.Path(root).glob("*.tmp")))


if __name__ == "__main__":
    unittest.main()
