import contextlib
import importlib.util
import io
import json
import pathlib
import struct
import tempfile
import unittest
import zlib


MODULE_PATH = pathlib.Path(__file__).resolve().parents[1] / "PixelArtApp" / "MockExporter.py"
SPEC = importlib.util.spec_from_file_location("efyv_mock_exporter", MODULE_PATH)
EXPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORTER)


def parse_png(path):
    payload = pathlib.Path(path).read_bytes()
    if payload[:8] != b"\x89PNG\r\n\x1a\n":
        raise AssertionError("invalid PNG signature")

    offset = 8
    chunks = []
    while offset < len(payload):
        length = struct.unpack(">I", payload[offset:offset + 4])[0]
        chunk_type = payload[offset + 4:offset + 8]
        data = payload[offset + 8:offset + 8 + length]
        checksum = struct.unpack(">I", payload[offset + 8 + length:offset + 12 + length])[0]
        expected = zlib.crc32(data, zlib.crc32(chunk_type)) & 0xFFFFFFFF
        if checksum != expected:
            raise AssertionError("invalid PNG chunk checksum")
        chunks.append((chunk_type, data))
        offset += 12 + length

    if offset != len(payload):
        raise AssertionError("trailing or truncated PNG bytes")
    return chunks


class MockExporterTests(unittest.TestCase):
    def test_png_chunk_has_big_endian_length_and_crc(self):
        for size in (0, 1, 255, 4096):
            data = bytes((index * 31) & 0xFF for index in range(size))
            chunk = EXPORTER._png_chunk(b"TEST", data)
            self.assertEqual(size, struct.unpack(">I", chunk[:4])[0])
            self.assertEqual(b"TEST", chunk[4:8])
            self.assertEqual(data, chunk[8:-4])
            self.assertEqual(
                zlib.crc32(data, zlib.crc32(b"TEST")) & 0xFFFFFFFF,
                struct.unpack(">I", chunk[-4:])[0],
            )

    def test_solid_png_pixels_dimensions_and_input_integrity(self):
        with tempfile.TemporaryDirectory(prefix="efyv-mock-png-") as root:
            for width, height in ((1, 1), (2, 3), (64, 64), (127, 5)):
                rgba = [17, 34, 51, 68]
                original = list(rgba)
                path = pathlib.Path(root) / f"{width}x{height}.png"
                EXPORTER._write_solid_rgba_png(path, width, height, rgba)
                self.assertEqual(original, rgba)

                chunks = parse_png(path)
                self.assertEqual([b"IHDR", b"IDAT", b"IEND"], [kind for kind, _ in chunks])
                self.assertEqual(
                    (width, height, 8, 6, 0, 0, 0),
                    struct.unpack(">IIBBBBB", chunks[0][1]),
                )
                pixels = zlib.decompress(chunks[1][1])
                expected_row = b"\x00" + bytes(rgba) * width
                self.assertEqual(expected_row * height, pixels)
                self.assertEqual(b"", chunks[2][1])

    def test_export_contract_is_repeatable_and_leaves_no_temps(self):
        with tempfile.TemporaryDirectory(prefix="efyv-mock-export-") as root:
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                EXPORTER.mock_pixel_app_export(root, "Basic Bat_01.v2")
                EXPORTER.mock_pixel_app_export(root, "Basic Bat_01.v2")

            png = pathlib.Path(root) / "Basic Bat_01.v2_Down.png"
            metadata = pathlib.Path(root) / "Basic Bat_01.v2_Down.efyvlaby"
            self.assertTrue(png.is_file())
            self.assertTrue(metadata.is_file())
            self.assertFalse(list(pathlib.Path(root).glob("*.tmp")))
            parse_png(png)

            document = json.loads(metadata.read_text(encoding="utf-8"))
            self.assertEqual("EnemyData", document["assetType"])
            self.assertEqual("Basic Bat_01.v2", document["properties"]["entityName"])
            self.assertEqual("Down", document["properties"]["facing"])
            self.assertEqual(1, document["atlas"]["formatVersion"])
            self.assertEqual(64, document["atlas"]["frameWidth"])
            self.assertEqual(1, document["atlas"]["animations"][0]["frameCount"])
            self.assertEqual(1, len(document["hitboxes"]))
            self.assertIn("Export complete!", output.getvalue())

    def test_adversarial_names_cannot_escape_or_target_devices(self):
        invalid_names = (
            None, "", "   ", ".", "..", "../escape", "..\\escape",
            "a/b", "a\\b", "CON", "con.txt", "PRN", "AUX.data", "NUL",
            "COM1", "com9.log", "LPT1", "lpt9.txt", "trailing.", "trailing ",
            "bad:name", "bad*name", "bad?name", "bad\x00name", "bad\nname",
        )
        with tempfile.TemporaryDirectory(prefix="efyv-mock-attacks-") as root:
            parent_before = set(pathlib.Path(root).parent.iterdir())
            for name in invalid_names:
                with self.subTest(name=name):
                    with self.assertRaises(ValueError):
                        EXPORTER.mock_pixel_app_export(root, name)
                    self.assertEqual([], list(pathlib.Path(root).iterdir()))
            self.assertEqual(parent_before, set(pathlib.Path(root).parent.iterdir()))


if __name__ == "__main__":
    unittest.main()
