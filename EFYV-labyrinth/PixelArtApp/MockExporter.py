import json
import os
import re
import struct
import uuid
import zlib


_RESERVED_DEVICE_NAME = re.compile(r"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", re.IGNORECASE)


def _validate_entity_name(entity_name):
    if not isinstance(entity_name, str) or not entity_name.strip():
        raise ValueError("entity_name must be a non-empty safe file stem")
    if entity_name in (".", "..") or entity_name.endswith((".", " ")):
        raise ValueError("entity_name must be a safe file stem")
    if any(ord(character) < 32 or character in '<>:"/\\|?*' for character in entity_name):
        raise ValueError("entity_name contains invalid filename characters")
    if _RESERVED_DEVICE_NAME.match(entity_name):
        raise ValueError("entity_name is a reserved device name")


def _png_chunk(chunk_type, payload):
    checksum = zlib.crc32(chunk_type)
    checksum = zlib.crc32(payload, checksum)
    return struct.pack(">I", len(payload)) + chunk_type + payload + struct.pack(">I", checksum)


def _write_solid_rgba_png(path, width, height, rgba):
    # Mirror FastPngEncoder.Write: non-positive dimensions and non-RGBA pixel
    # layouts are rejected before anything touches the filesystem.
    if width <= 0:
        raise ValueError("width must be positive")
    if height <= 0:
        raise ValueError("height must be positive")
    pixel = bytes(rgba)
    if len(pixel) != 4:
        raise ValueError("rgba must have exactly 4 components")
    row = b"\x00" + pixel * width
    image_data = row * height
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + _png_chunk(b"IHDR", header)
        + _png_chunk(b"IDAT", zlib.compress(image_data))
        + _png_chunk(b"IEND", b"")
    )
    # Exclusive create, mirroring the backend's FileMode.CreateNew.
    with open(path, "xb") as output:
        output.write(png)

def mock_pixel_app_export(export_directory, entity_name):
    _validate_entity_name(entity_name)
    print("--- EFYV Custom Pixel Art App (Mock) ---")
    print(f"Exporting asset '{entity_name}' to {export_directory}...")

    # Ensure directory exists
    os.makedirs(export_directory, exist_ok=True)
    facing = "Down"
    export_basename = f"{entity_name}_{facing}"

    # Mirror the backend FastExporter temp-name convention:
    # "." + stem + "." + <guid N> + <final extension> + ".tmp", created
    # exclusively so concurrent exports cannot corrupt each other's file,
    # published via os.replace, and removed if a failure leaves them behind.
    temporary_stem = f".{export_basename}.{uuid.uuid4().hex}"
    png_path = os.path.join(export_directory, f"{export_basename}.png")
    png_temp_path = os.path.join(export_directory, f"{temporary_stem}.png.tmp")

    # Match the versioned backend/Unity live-hook contract. Key order mirrors
    # FastExporter.WriteJson: documentVersion, assetType, baseAssetType,
    # properties, hitboxes, atlas. documentVersion tracks the backend's
    # CurrentDocumentVersion; baseAssetType is the registered base of the
    # asset type (EnemyData is a base archetype, so it names itself), letting
    # the importer fall back for custom types it has no class for.
    # Item #5 note: the mock does NOT mirror the .efyvmap binary container
    # or the optional "tileset" manifest block - the cross-language contract
    # guard pins only the .efyvlaby JSON identity this mock emits (see the
    # test_config_contract.py scope note). documentVersion 5 == the backend's
    # CurrentDocumentVersion after the tileset block landed.
    efyv_data = {
        "documentVersion": 5,
        "assetType": "EnemyData",
        "baseAssetType": "EnemyData",
        "properties": {
            "entityName": entity_name,
            "maxHealth": 25.0,
            "baseSpeed": 1.5,
            "damageToPlayer": 5.0,
            "experienceValue": 10.0,
            "facing": facing,
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

    efyv_path = os.path.join(export_directory, f"{export_basename}.efyvlaby")
    efyv_temp_path = os.path.join(export_directory, f"{temporary_stem}.efyvlaby.tmp")
    try:
        # Publish the image first; metadata is the authoritative completion signal.
        _write_solid_rgba_png(png_temp_path, 64, 64, (255, 0, 0, 255))
        os.replace(png_temp_path, png_path)
        print(f"Created Sprite: {png_path}")

        with open(efyv_temp_path, "x", encoding="utf-8") as f:
            json.dump(efyv_data, f, indent=4)
        os.replace(efyv_temp_path, efyv_path)
    finally:
        # Mirror the backend's DeleteIfPresent cleanup.
        for leftover in (png_temp_path, efyv_temp_path):
            try:
                os.remove(leftover)
            except OSError:
                pass

    print(f"Created Metadata: {efyv_path}")
    print("\nExport complete!")
    print("If Unity is open, the EFYVPixelArtImporter will immediately detect these files")
    print("and automatically generate the EntityData ScriptableObject!")

if __name__ == "__main__":
    # We export directly into the Unity Assets folder to trigger the hot-reload
    unity_raw_art_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "../Assets/RawArt"))
    mock_pixel_app_export(unity_raw_art_path, "BasicBat")
