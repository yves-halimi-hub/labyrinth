"""Static validation of the openable Unity project (batch2/unity-project).

Unity itself is not available in CI, so this suite pins the project layout
invariants that make `EFYV-labyrinth` open cleanly in Unity 6000.6.0b4:

* pinned editor version and required ProjectSettings values,
* Packages/manifest.json -> local backend UPM package cross-reference,
* assembly definition graph (names, references, unsafe flags, editor scoping),
* .meta coverage and GUID uniqueness for every Unity-visible file,
* Assets/Scenes/Labyrinth.unity referential integrity: every local fileID and
  every script GUID referenced by the scene resolves, and the bootstrap /
  spawner wiring points at the intended scripts,
* EditorBuildSettings <-> scene meta GUID agreement,
* the BCL-compat package really carries the assemblies the backend needs,
* legacy input axes match the names the shared config makes PlayerController
  read.

GUID convention: hand-authored metas use md5("efyv:" + repo-relative path),
so stable IDs can be regenerated without Unity. Unity preserves these GUIDs
when it rewrites metas.
"""

import hashlib
import json
import pathlib
import re
import unittest

TESTS_DIR = pathlib.Path(__file__).resolve().parent
GAME_ROOT = TESTS_DIR.parent
REPO_ROOT = GAME_ROOT.parent
BACKEND_CORE = REPO_ROOT / "EFYV-labybackend" / "Core"
RUNTIME_MEDIA = REPO_ROOT / "Shared" / "EFYV.Runtime.Media"
BCL_PACKAGE = GAME_ROOT / "Packages" / "com.efyv.bclcompat"
SCENE_PATH = GAME_ROOT / "Assets" / "Scenes" / "Labyrinth.unity"
CONFIG_PATH = BACKEND_CORE / "Data" / "EFYV-LabyrinthConfig.cs"

META_ROOTS = (
    GAME_ROOT / "Assets",
    BACKEND_CORE,
    RUNTIME_MEDIA,
    BCL_PACKAGE,
)

GUID_RE = re.compile(r"^guid: ([0-9a-f]{32})$", re.MULTILINE)
SCENE_DOC_RE = re.compile(r"^--- !u!(\d+) &(\d+)$", re.MULTILINE)
LOCAL_REF_RE = re.compile(r"\{fileID: (-?\d+)\}")
SCRIPT_REF_RE = re.compile(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32}), type: 3\}")
GUID_REF_RE = re.compile(r"\{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d\}")

BUILTIN_GUIDS = {
    "0000000000000000e000000000000000",  # unity_builtin_resources
    "0000000000000000f000000000000000",  # unity_builtin_extra
}

# Unity package assemblies referenced by asmdefs but never vendored into the
# repo (they resolve from Library/PackageCache, which is gitignored). The 2D
# Sprite editor assembly carries ISpriteEditorDataProvider, which the Unity 6.6
# migration adopted for atlas slicing in EFYVPixelArtImporter.
EXTERNAL_PACKAGE_ASSEMBLIES = {
    "Unity.2D.Sprite.Editor",
}


def deterministic_guid(repo_relative_path):
    return hashlib.md5(("efyv:" + repo_relative_path).encode("utf-8")).hexdigest()


def read_meta_guid(meta_path):
    match = GUID_RE.search(meta_path.read_text(encoding="utf-8"))
    if match is None:
        raise AssertionError("meta without guid: " + str(meta_path))
    return match.group(1)


def iter_unity_visible_paths():
    def hidden(path):
        # Unity ignores folders/files ending with ~ or starting with a dot.
        return any(
            part.endswith("~") or part.startswith(".")
            for part in path.relative_to(REPO_ROOT).parts)

    for root in META_ROOTS:
        yield root
        for path in sorted(root.rglob("*")):
            if path.name.endswith(".meta") or hidden(path):
                continue
            yield path


def collect_guid_map():
    """guid -> repo-relative path (forward slashes) for every authored meta."""
    guid_map = {}
    for path in iter_unity_visible_paths():
        meta = path.with_name(path.name + ".meta")
        if meta.exists():
            rel = path.relative_to(REPO_ROOT).as_posix()
            guid_map[read_meta_guid(meta)] = rel
    return guid_map


def config_const(name):
    pattern = re.compile(
        r"public const string " + re.escape(name) + r' = "([^"]*)";')
    match = pattern.search(CONFIG_PATH.read_text(encoding="utf-8"))
    if match is None:
        raise AssertionError(name + " not found in shared config")
    return match.group(1)


class ProjectSettingsTests(unittest.TestCase):
    def test_editor_version_is_pinned(self):
        text = (GAME_ROOT / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8")
        self.assertIn("m_EditorVersion: 6000.6.0b4", text)

    def test_player_settings_identity_and_flags(self):
        text = (GAME_ROOT / "ProjectSettings" / "ProjectSettings.asset").read_text(encoding="utf-8")
        self.assertIn("companyName: EFYV", text)
        self.assertIn("productName: EFYV Labyrinth", text)
        self.assertIn("allowUnsafeCode: 1", text)
        # Legacy Input Manager: PlayerController uses Input.GetAxisRaw.
        self.assertIn("activeInputHandler: 0", text)

    def test_editor_build_settings_lists_scene_with_matching_guid(self):
        text = (GAME_ROOT / "ProjectSettings" / "EditorBuildSettings.asset").read_text(encoding="utf-8")
        self.assertIn("path: Assets/Scenes/Labyrinth.unity", text)
        self.assertIn("enabled: 1", text)
        scene_guid = read_meta_guid(SCENE_PATH.with_name(SCENE_PATH.name + ".meta"))
        self.assertIn("guid: " + scene_guid, text)

    def test_editor_settings_force_text_and_2d(self):
        text = (GAME_ROOT / "ProjectSettings" / "EditorSettings.asset").read_text(encoding="utf-8")
        self.assertIn("m_SerializationMode: 2", text)
        self.assertIn("m_DefaultBehaviorMode: 1", text)

    def test_required_settings_assets_exist(self):
        for name in (
            "ProjectSettings.asset",
            "EditorBuildSettings.asset",
            "EditorSettings.asset",
            "TagManager.asset",
            "InputManager.asset",
            "Physics2DSettings.asset",
            "QualitySettings.asset",
            "GraphicsSettings.asset",
        ):
            self.assertTrue(
                (GAME_ROOT / "ProjectSettings" / name).exists(),
                name + " missing")

    def test_input_axes_match_shared_config_names(self):
        text = (GAME_ROOT / "ProjectSettings" / "InputManager.asset").read_text(encoding="utf-8")
        axes = set(re.findall(r"m_Name: (\S+)", text))
        self.assertIn(config_const("InputHorizontal"), axes)
        self.assertIn(config_const("InputVertical"), axes)


class PackageGraphTests(unittest.TestCase):
    def test_manifest_references_backend_core_package(self):
        manifest = json.loads((GAME_ROOT / "Packages" / "manifest.json").read_text(encoding="utf-8"))
        dependency = manifest["dependencies"]["com.efyv.labybackend"]
        self.assertEqual("file:../../EFYV-labybackend/Core", dependency)
        target = (GAME_ROOT / "Packages" / "../../EFYV-labybackend/Core").resolve()
        self.assertTrue(target.is_dir())
        package = json.loads((target / "package.json").read_text(encoding="utf-8"))
        self.assertEqual("com.efyv.labybackend", package["name"])

        media_dependency = manifest["dependencies"]["com.efyv.runtime.media"]
        self.assertEqual("file:../../Shared/EFYV.Runtime.Media", media_dependency)
        media_target = (GAME_ROOT / "Packages" / "../../Shared/EFYV.Runtime.Media").resolve()
        self.assertEqual("com.efyv.runtime.media", json.loads(
            (media_target / "package.json").read_text(encoding="utf-8"))["name"])

    def test_backend_package_root_excludes_tests(self):
        # The package root is Core on purpose: the sibling Tests directory and
        # its bin/obj DLLs must never be imported by Unity (duplicate types).
        self.assertFalse((BACKEND_CORE / "Tests").exists())
        self.assertTrue((BACKEND_CORE.parent / "Tests").is_dir())

    def test_bcl_compat_package_contents(self):
        package = json.loads((BCL_PACKAGE / "package.json").read_text(encoding="utf-8"))
        self.assertEqual("com.efyv.bclcompat", package["name"])
        for dll in (
            "System.Text.Json.dll",
            "System.Text.Encodings.Web.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "Microsoft.Bcl.AsyncInterfaces.dll",
            "EFYV.ZLibCompat.dll",
        ):
            path = BCL_PACKAGE / dll
            self.assertTrue(path.exists(), dll + " missing")
            self.assertEqual(b"MZ", path.read_bytes()[:2], dll + " is not a PE image")
            self.assertTrue(path.with_name(dll + ".meta").exists(), dll + ".meta missing")

    def test_backend_uses_system_text_json_hence_compat_package(self):
        uses = any(
            "System.Text.Json" in path.read_text(encoding="utf-8", errors="ignore")
            for path in BACKEND_CORE.rglob("*.cs"))
        self.assertTrue(uses, "backend no longer uses System.Text.Json; the compat package can shrink")

    def test_zlib_shim_source_ships_next_to_its_dll(self):
        source_dir = BCL_PACKAGE / "ZLibCompatSource~"
        self.assertTrue((source_dir / "ZLibStream.cs").exists())
        self.assertTrue((source_dir / "EFYV.ZLibCompat.csproj").exists())
        # Hidden from Unity (trailing ~): the .cs must NOT gain a .meta, or Unity
        # would compile a duplicate System.IO.Compression.ZLibStream.
        self.assertFalse((source_dir / "ZLibStream.cs.meta").exists())
        text = (source_dir / "ZLibStream.cs").read_text(encoding="utf-8")
        self.assertIn("namespace System.IO.Compression", text)
        self.assertIn("class ZLibStream", text)

    def test_backend_csc_rsp_raises_language_version(self):
        # HitboxData declares a parameterless struct constructor (C# 10); Unity
        # compiles C# 9 by default, so the backend assembly carries a csc.rsp.
        rsp = (BACKEND_CORE / "csc.rsp").read_text(encoding="utf-8")
        self.assertIn("-langversion:10", rsp)


class AssemblyDefinitionTests(unittest.TestCase):
    def load_all(self):
        asmdefs = {}
        for root in (GAME_ROOT / "Assets", BACKEND_CORE, RUNTIME_MEDIA):
            for path in root.rglob("*.asmdef"):
                data = json.loads(path.read_text(encoding="utf-8"))
                asmdefs[data["name"]] = (path, data)
        return asmdefs

    def test_expected_assemblies_exist(self):
        asmdefs = self.load_all()
        self.assertEqual(
            {"EFYV.Runtime.Media", "EFYVBackend.Core", "EFYV.Game", "EFYV.Game.Editor"},
            set(asmdefs))

    def test_references_resolve_and_scope_is_correct(self):
        asmdefs = self.load_all()
        for name, (path, data) in asmdefs.items():
            for reference in data.get("references", []):
                if reference in EXTERNAL_PACKAGE_ASSEMBLIES:
                    continue
                self.assertIn(reference, asmdefs, name + " references missing assembly " + reference)
        _, game = asmdefs["EFYV.Game"]
        self.assertIn("EFYVBackend.Core", game["references"])
        _, editor = asmdefs["EFYV.Game.Editor"]
        self.assertEqual(["Editor"], editor["includePlatforms"])
        self.assertIn("EFYV.Game", editor["references"])
        _, backend = asmdefs["EFYVBackend.Core"]
        self.assertTrue(backend.get("noEngineReferences"), "backend must stay engine-neutral")
        _, media = asmdefs["EFYV.Runtime.Media"]
        self.assertTrue(media.get("noEngineReferences"), "media must stay engine-neutral")

    def test_unsafe_code_flags_match_sources(self):
        asmdefs = self.load_all()
        unsafe_re = re.compile(r"\bunsafe\b")

        def sources_use_unsafe(directory, exclude=None):
            for path in directory.rglob("*.cs"):
                if exclude is not None and exclude in path.parts:
                    continue
                if unsafe_re.search(path.read_text(encoding="utf-8", errors="ignore")):
                    return True
            return False

        backend_path, backend = asmdefs["EFYVBackend.Core"]
        if sources_use_unsafe(backend_path.parent):
            self.assertTrue(backend["allowUnsafeCode"], "backend sources use unsafe")
        media_path, media = asmdefs["EFYV.Runtime.Media"]
        if sources_use_unsafe(media_path.parent):
            self.assertTrue(media["allowUnsafeCode"], "media sources use unsafe")
        game_path, game = asmdefs["EFYV.Game"]
        if sources_use_unsafe(game_path.parent, exclude="Editor"):
            self.assertTrue(game["allowUnsafeCode"], "game sources use unsafe")

    def test_editor_scripts_live_under_editor_assembly(self):
        editor_dir = GAME_ROOT / "Assets" / "Scripts" / "Editor"
        for path in (GAME_ROOT / "Assets" / "Scripts").rglob("*.cs"):
            text = path.read_text(encoding="utf-8", errors="ignore")
            if re.search(r"^using UnityEditor;", text, re.MULTILINE):
                self.assertIn(
                    editor_dir, path.parents,
                    str(path) + " uses UnityEditor outside the editor assembly")


class MetaCoverageTests(unittest.TestCase):
    def test_every_unity_visible_path_has_meta_and_vice_versa(self):
        for path in iter_unity_visible_paths():
            if path in META_ROOTS:
                continue
            meta = path.with_name(path.name + ".meta")
            self.assertTrue(meta.exists(), "missing meta for " + str(path))
        for root in META_ROOTS:
            for meta in root.rglob("*.meta"):
                target = meta.with_name(meta.name[: -len(".meta")])
                self.assertTrue(target.exists(), "orphan meta " + str(meta))

    def test_guids_are_unique_and_well_formed(self):
        seen = {}
        for path in iter_unity_visible_paths():
            meta = path.with_name(path.name + ".meta")
            if not meta.exists():
                continue
            guid = read_meta_guid(meta)
            self.assertNotIn(guid, seen, "guid collision: " + str(path) + " vs " + str(seen.get(guid)))
            seen[guid] = path
        self.assertGreater(len(seen), 100)

    def test_hand_authored_guids_follow_deterministic_scheme(self):
        # Spot-check the scheme on the assets other files cross-reference, so a
        # regenerated meta cannot silently break the scene or build settings.
        for rel in (
            "EFYV-labyrinth/Assets/Scenes/Labyrinth.unity",
            "EFYV-labyrinth/Assets/Scripts/Core/GameBootstrap.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/SpawnManager.cs",
        ):
            path = REPO_ROOT / rel
            meta = path.with_name(path.name + ".meta")
            self.assertEqual(deterministic_guid(rel), read_meta_guid(meta), rel)


class SceneIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text = SCENE_PATH.read_text(encoding="utf-8")
        cls.anchors = {int(anchor) for _, anchor in SCENE_DOC_RE.findall(cls.text)}
        cls.guid_map = collect_guid_map()

    def test_scene_has_expected_top_level_objects(self):
        class_ids = {int(class_id) for class_id, _ in SCENE_DOC_RE.findall(self.text)}
        # GameObject, Transform, Camera, MonoBehaviour, SpriteRenderer,
        # Rigidbody2D, BoxCollider2D, CircleCollider2D, SceneRoots.
        for expected in (1, 4, 20, 114, 212, 50, 61, 58, 1660057539):
            self.assertIn(expected, class_ids)

    def test_every_local_file_id_reference_resolves(self):
        for value in LOCAL_REF_RE.findall(self.text):
            file_id = int(value)
            if file_id == 0:
                continue
            self.assertIn(file_id, self.anchors, "dangling fileID " + value)

    def test_every_script_guid_resolves_to_a_cs_meta(self):
        script_guids = SCRIPT_REF_RE.findall(self.text)
        self.assertGreaterEqual(len(script_guids), 12)
        for guid in script_guids:
            self.assertIn(guid, self.guid_map, "scene references unknown script guid " + guid)
            self.assertTrue(self.guid_map[guid].endswith(".cs"), self.guid_map[guid])

    def test_every_external_guid_is_builtin_or_authored(self):
        for guid in GUID_REF_RE.findall(self.text):
            if guid in BUILTIN_GUIDS:
                continue
            self.assertIn(guid, self.guid_map, "unknown external guid " + guid)

    def test_expected_behaviours_are_wired(self):
        expected_scripts = {
            "EFYV-labyrinth/Assets/Scripts/Core/GameBootstrap.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Entities/PlayerController.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Entities/Implementations/Monster.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/PoolManager.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/SpawnManager.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/MapManager.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/DropManager.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/AIDirector.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/UpgradeManager.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/SaveManager.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/AchievementManager.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Managers/MapViewportController.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Controllers/WeaponController.cs",
        }
        wired = {self.guid_map[guid] for guid in SCRIPT_REF_RE.findall(self.text)}
        self.assertEqual(expected_scripts, wired)

    def test_bootstrap_and_spawner_wiring(self):
        # GameBootstrap must reference the player controller, the viewport, and
        # the inactive enemy template; SpawnManager must carry the same enemy
        # template in its prefab array and the player transform.
        self.assertIn("player: {fileID: 400002}", self.text)
        self.assertIn("mapViewport: {fileID: 500002}", self.text)
        self.assertIn("enemyTemplate: {fileID: 600002}", self.text)
        self.assertIn("playerTransform: {fileID: 400001}", self.text)
        self.assertIn("- {fileID: 600002}", self.text)
        self.assertIn("tilePrefab: {fileID: 510000}", self.text)
        self.assertIn("targetToFollow: {fileID: 400001}", self.text)
        self.assertIn("mainCamera: {fileID: 100002}", self.text)
        self.assertIn("spawnManager: {fileID: 300003}", self.text)

    def test_enemy_template_is_inactive_and_player_active(self):
        blocks = re.split(r"^--- ", self.text, flags=re.MULTILINE)
        by_name = {}
        for block in blocks:
            name_match = re.search(r"^  m_Name: (.+)$", block, re.MULTILINE)
            active_match = re.search(r"^  m_IsActive: (\d)$", block, re.MULTILINE)
            if name_match and active_match:
                by_name[name_match.group(1)] = active_match.group(1)
        self.assertEqual("0", by_name["EnemyTemplate"])
        self.assertEqual("1", by_name["Player"])
        self.assertEqual("1", by_name["Main Camera"])
        self.assertEqual("1", by_name["Managers"])
        self.assertEqual("1", by_name["TileTemplate"])

    def test_scene_roots_cover_all_root_transforms(self):
        roots_block = self.text.split("SceneRoots:", 1)[1]
        root_ids = {int(v) for v in LOCAL_REF_RE.findall(roots_block)}
        fathered = set()
        for match in re.finditer(r"m_Father: \{fileID: (\d+)\}", self.text):
            fathered.add(int(match.group(1)))
        transform_anchors = {
            int(anchor)
            for class_id, anchor in SCENE_DOC_RE.findall(self.text)
            if class_id == "4"
        }
        expected_roots = set()
        for match in re.finditer(
            r"^--- !u!4 &(\d+)\nTransform:.*?m_Father: \{fileID: (\d+)\}",
            self.text,
            re.MULTILINE | re.DOTALL,
        ):
            if int(match.group(2)) == 0:
                expected_roots.add(int(match.group(1)))
        self.assertEqual(expected_roots, root_ids)
        self.assertTrue(root_ids.issubset(transform_anchors))


class PrefabIntegrityTests(unittest.TestCase):
    """Item #4 debug-spawn template prefabs: referential integrity, same shape
    as the scene checks. Every local fileID resolves, every script GUID resolves
    to a .cs meta, every external GUID is builtin or authored, and each template
    carries exactly the intended component scripts."""

    PREFAB_DIR = GAME_ROOT / "Assets" / "Prefabs"

    # Each archetype template's expected component scripts (by repo-relative
    # path). The living archetypes carry their concrete entity plus the weapon
    # controller; the prop archetype carries the neutral GenericProp.
    EXPECTED_SCRIPTS = {
        "Enemy.prefab": {
            "EFYV-labyrinth/Assets/Scripts/Core/Entities/Implementations/Monster.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Controllers/WeaponController.cs",
        },
        "Boss.prefab": {
            "EFYV-labyrinth/Assets/Scripts/Core/Entities/Implementations/Boss.cs",
            "EFYV-labyrinth/Assets/Scripts/Core/Controllers/WeaponController.cs",
        },
        "Prop.prefab": {
            "EFYV-labyrinth/Assets/Scripts/Core/Entities/Environment/Implementations/GenericProp.cs",
        },
    }

    @classmethod
    def setUpClass(cls):
        cls.guid_map = collect_guid_map()
        cls.prefabs = sorted(cls.PREFAB_DIR.rglob("*.prefab"))

    def test_expected_template_prefabs_exist(self):
        self.assertEqual(set(self.EXPECTED_SCRIPTS), {p.name for p in self.prefabs})
        for name in self.EXPECTED_SCRIPTS:
            self.assertTrue(
                (self.PREFAB_DIR / "DebugTemplates" / name).is_file(),
                name + " missing under DebugTemplates")

    def test_every_local_file_id_reference_resolves(self):
        for prefab in self.prefabs:
            text = prefab.read_text(encoding="utf-8")
            anchors = {int(anchor) for _, anchor in SCENE_DOC_RE.findall(text)}
            for value in LOCAL_REF_RE.findall(text):
                file_id = int(value)
                if file_id == 0:
                    continue
                self.assertIn(file_id, anchors, prefab.name + ": dangling fileID " + value)

    def test_every_script_guid_resolves_to_a_cs_meta(self):
        for prefab in self.prefabs:
            script_guids = SCRIPT_REF_RE.findall(prefab.read_text(encoding="utf-8"))
            self.assertGreaterEqual(len(script_guids), 1, prefab.name)
            for guid in script_guids:
                self.assertIn(guid, self.guid_map, prefab.name + " references unknown script guid " + guid)
                self.assertTrue(self.guid_map[guid].endswith(".cs"), self.guid_map[guid])

    def test_every_external_guid_is_builtin_or_authored(self):
        for prefab in self.prefabs:
            for guid in GUID_REF_RE.findall(prefab.read_text(encoding="utf-8")):
                if guid in BUILTIN_GUIDS:
                    continue
                self.assertIn(guid, self.guid_map, prefab.name + " references unknown external guid " + guid)

    def test_expected_scripts_are_wired(self):
        for prefab in self.prefabs:
            wired = {self.guid_map[guid] for guid in SCRIPT_REF_RE.findall(prefab.read_text(encoding="utf-8"))}
            self.assertEqual(self.EXPECTED_SCRIPTS[prefab.name], wired, prefab.name)


if __name__ == "__main__":
    unittest.main()
