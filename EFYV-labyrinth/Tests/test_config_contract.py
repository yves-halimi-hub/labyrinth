"""Cross-language contract guard for PixelArtApp/MockExporter.py.

MockExporter.py mirrors the wire contract owned by the backend config
(EFYV-labybackend/Core/Data/EFYV-LabyrinthConfig.cs) but cannot reference
it at runtime. This suite extracts the relevant constants from the C#
source by parsing `public const` declarations (with alias resolution and
class-scope tracking) and asserts the mock's exported artifacts use the
same values.

Scope is deliberately limited to stable contract identity: JSON field
names, file extensions, asset-type/facing strings, filename suffix/stem
rules, temp-file extension, and the atlas format version. Atlas layout
math (frame packing, near-square grids, dimensions) is NOT pinned here --
the backend layout strategy may evolve independently of field naming.
"""

import contextlib
import importlib.util
import io
import json
import os
import pathlib
import re
import tempfile
import unittest
from unittest import mock


MODULE_PATH = pathlib.Path(__file__).resolve().parents[1] / "PixelArtApp" / "MockExporter.py"
SPEC = importlib.util.spec_from_file_location("efyv_mock_exporter_contract", MODULE_PATH)
EXPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORTER)

CONFIG_PATH = (
    pathlib.Path(__file__).resolve().parents[2]
    / "EFYV-labybackend" / "Core" / "Data" / "EFYV-LabyrinthConfig.cs"
)

_CLASS_DECLARATION = re.compile(r"^(?:public\s+|internal\s+)?static\s+class\s+(\w+)")
_CONST_DECLARATION = re.compile(r"^public\s+const\s+[\w.\[\]<>]+\s+(\w+)\s*=\s*(.+);$")
_STRING_LITERAL = re.compile(r'"(?:\\.|[^"\\])*"')
_CHAR_LITERAL = re.compile(r"'(?:\\.|[^'\\])'")
_INTEGER_LITERAL = re.compile(r"^-?\d+$")


def _parse_constants(text):
    """Return {fully.qualified.name: raw value text} for every public const.

    Tracks nested static-class scope by counting braces (string and char
    literals are blanked first so braces inside them cannot desync the
    scope stack). Non-class braces (initializers, properties) push an
    anonymous scope entry that never contributes to qualified names.
    """
    constants = {}
    scope = []
    pending_classes = []
    for raw_line in text.splitlines():
        line = raw_line.strip()
        class_match = _CLASS_DECLARATION.match(line)
        if class_match:
            pending_classes.append(class_match.group(1))
        const_match = _CONST_DECLARATION.match(line)
        if const_match:
            qualified_parts = [name for name in scope if name]
            qualified_parts.append(const_match.group(1))
            constants[".".join(qualified_parts)] = const_match.group(2).strip()
        blanked = _CHAR_LITERAL.sub("''", _STRING_LITERAL.sub('""', line))
        for character in blanked:
            if character == "{":
                scope.append(pending_classes.pop(0) if pending_classes else None)
            elif character == "}" and scope:
                scope.pop()
    return constants


def _resolve(constants, value):
    """Resolve a raw C# const value to a Python str/int, following aliases."""
    value = value.strip()
    if value.startswith('"') and value.endswith('"') and len(value) >= 2:
        literal = value[1:-1]
        if "\\" in literal:
            raise AssertionError(f"escaped string literals are out of scope: {value}")
        return literal
    if _INTEGER_LITERAL.match(value):
        return int(value)
    reference = value
    if reference in constants:
        return _resolve(constants, constants[reference])
    prefixed = "EFYVLabyrinthConfig." + reference
    if prefixed in constants:
        return _resolve(constants, constants[prefixed])
    raise AssertionError(f"cannot resolve backend constant reference: {value}")


class CrossLanguageContractTests(unittest.TestCase):
    """Field names, extensions, versions, and stem rules must match C#."""

    @classmethod
    def setUpClass(cls):
        cls.constants = _parse_constants(CONFIG_PATH.read_text(encoding="utf-8"))
        cls._export_dir = tempfile.TemporaryDirectory(prefix="efyv-contract-")
        cls.addClassCleanup(cls._export_dir.cleanup)
        with contextlib.redirect_stdout(io.StringIO()):
            EXPORTER.mock_pixel_app_export(cls._export_dir.name, "ContractProbe")
        cls.document = None
        for entry in pathlib.Path(cls._export_dir.name).iterdir():
            if entry.suffix == ".efyvlaby":
                cls.document = json.loads(entry.read_text(encoding="utf-8"))
        if cls.document is None:
            raise AssertionError("export produced no .efyvlaby document")

    @classmethod
    def shared(cls, name):
        return _resolve(cls.constants, cls.constants[f"EFYVLabyrinthConfig.Shared.{name}"])

    @classmethod
    def exporter(cls, name):
        return _resolve(
            cls.constants, cls.constants[f"EFYVLabyrinthConfig.Backend.Exporter.{name}"]
        )

    def test_parser_extracts_known_backend_constants(self):
        # Guards the regex extraction itself: if the config file layout or
        # naming drifts so far the parser finds nothing, fail loudly here
        # instead of vacuously passing the contract tests.
        self.assertEqual("entityName", self.shared("EntityNameField"))
        self.assertEqual(".efyvlaby", self.shared("EfyvExtension"))
        self.assertEqual("properties", self.exporter("FieldProperties"))
        self.assertEqual(1, self.exporter("CurrentFormatVersion"))
        # Alias resolution: Exporter.FieldEntityName aliases Shared.EntityNameField.
        self.assertEqual(self.shared("EntityNameField"), self.exporter("FieldEntityName"))

    def test_exported_filenames_use_shared_extensions_and_facing_suffix(self):
        suffix = self.shared("FacingFileSuffixDown")
        png_extension = self.shared("PngExtension")
        efyv_extension = self.shared("EfyvExtension")
        root = pathlib.Path(self._export_dir.name)
        self.assertTrue((root / f"ContractProbe{suffix}{png_extension}").is_file())
        self.assertTrue((root / f"ContractProbe{suffix}{efyv_extension}").is_file())

    def test_top_level_field_names_match_backend_exporter(self):
        self.assertEqual(
            [
                self.exporter("FieldDocumentVersion"),
                self.shared("AssetTypeField"),
                self.exporter("FieldBaseAssetType"),
                self.exporter("FieldProperties"),
                self.exporter("FieldHitboxes"),
                self.exporter("FieldAtlas"),
            ],
            list(self.document),
        )

    def test_document_version_and_base_asset_type_match_backend(self):
        # The mock emits the backend's CURRENT document version and the base
        # archetype of its asset type (EnemyData is a base archetype, so it
        # names itself - mirroring ExportEngine.ResolveBaseAssetType).
        self.assertEqual(
            self.exporter("CurrentDocumentVersion"),
            self.document[self.exporter("FieldDocumentVersion")],
        )
        self.assertEqual(
            self.shared("EnemyAssetType"),
            self.document[self.exporter("FieldBaseAssetType")],
        )

    def test_property_field_names_and_identity_values_match_backend(self):
        asset_type = self.document[self.shared("AssetTypeField")]
        self.assertEqual(self.shared("EnemyAssetType"), asset_type)
        properties = self.document[self.exporter("FieldProperties")]
        for field in (
            self.shared("EntityNameField"),
            self.shared("MaxHealthField"),
            self.shared("BaseSpeedField"),
            self.shared("DamageToPlayerField"),
            self.shared("ExperienceValueField"),
            self.shared("FacingField"),
        ):
            self.assertIn(field, properties)
        self.assertEqual("ContractProbe", properties[self.shared("EntityNameField")])
        self.assertEqual(self.shared("FacingDown"), properties[self.shared("FacingField")])

    def test_hitbox_field_names_match_backend_exporter(self):
        hitboxes = self.document[self.exporter("FieldHitboxes")]
        self.assertEqual(1, len(hitboxes))
        self.assertEqual(
            [
                self.exporter("FieldFrameIndex"),
                self.exporter("FieldHitboxType"),
                self.exporter("FieldX"),
                self.exporter("FieldY"),
                self.exporter("FieldWidth"),
                self.exporter("FieldHeight"),
            ],
            list(hitboxes[0]),
        )

    def test_atlas_field_names_and_format_version_match_backend(self):
        atlas = self.document[self.exporter("FieldAtlas")]
        self.assertEqual(
            [
                self.exporter("FieldFormatVersion"),
                self.exporter("FieldFrameWidth"),
                self.exporter("FieldFrameHeight"),
                self.exporter("FieldAtlasWidth"),
                self.exporter("FieldAtlasHeight"),
                self.exporter("FieldAnimations"),
            ],
            list(atlas),
        )
        self.assertEqual(self.exporter("CurrentFormatVersion"), atlas[self.exporter("FieldFormatVersion")])
        animations = atlas[self.exporter("FieldAnimations")]
        self.assertGreaterEqual(len(animations), 1)
        for animation in animations:
            self.assertEqual(
                [
                    self.exporter("FieldName"),
                    self.exporter("FieldFps"),
                    self.exporter("FieldStartFrame"),
                    self.exporter("FieldFrameCount"),
                ],
                list(animation),
            )

    def test_temporary_files_use_backend_temporary_extension_and_prefix(self):
        temporary_extension = self.exporter("TemporaryExtension")
        temporary_prefix = self.exporter("TemporaryNamePrefix")
        publishes = []
        real_replace = os.replace

        def recording_replace(source, destination):
            publishes.append(os.fspath(source))
            real_replace(source, destination)

        with tempfile.TemporaryDirectory(prefix="efyv-contract-tmp-") as root:
            with mock.patch("os.replace", side_effect=recording_replace):
                with contextlib.redirect_stdout(io.StringIO()):
                    EXPORTER.mock_pixel_app_export(root, "TempProbe")
        self.assertEqual(2, len(publishes))
        for source in publishes:
            basename = os.path.basename(source)
            self.assertTrue(basename.endswith(temporary_extension), basename)
            self.assertTrue(basename.startswith(temporary_prefix), basename)


if __name__ == "__main__":
    unittest.main()
