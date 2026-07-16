import json
import os
import re
import struct
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
    row = b"\x00" + bytes(rgba) * width
    image_data = row * height
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + _png_chunk(b"IHDR", header)
        + _png_chunk(b"IDAT", zlib.compress(image_data))
        + _png_chunk(b"IEND", b"")
    )
    with open(path, "wb") as output:
        output.write(png)

def mock_pixel_app_export(export_directory, entity_name):
    _validate_entity_name(entity_name)
    print("--- EFYV Custom Pixel Art App (Mock) ---")
    print(f"Exporting asset '{entity_name}' to {export_directory}...")

    # Ensure directory exists
    os.makedirs(export_directory, exist_ok=True)
    facing = "Down"
    export_basename = f"{entity_name}_{facing}"

    # Publish the image first; metadata is the authoritative completion signal.
    png_path = os.path.join(export_directory, f"{export_basename}.png")
    png_temp_path = png_path + ".tmp"
    _write_solid_rgba_png(png_temp_path, 64, 64, (255, 0, 0, 255))
    os.replace(png_temp_path, png_path)
    print(f"Created Sprite: {png_path}")

    # Match the versioned backend/Unity live-hook contract.
    efyv_data = {
        "assetType": "EnemyData",
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
    efyv_temp_path = efyv_path + ".tmp"
    with open(efyv_temp_path, "w", encoding="utf-8") as f:
        json.dump(efyv_data, f, indent=4)
    os.replace(efyv_temp_path, efyv_path)
        
    print(f"Created Metadata: {efyv_path}")
    print("\nExport complete!")
    print("If Unity is open, the EFYVPixelArtImporter will immediately detect these files")
    print("and automatically generate the EntityData ScriptableObject!")

if __name__ == "__main__":
    # We export directly into the Unity Assets folder to trigger the hot-reload
    unity_raw_art_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "../Assets/RawArt"))
    mock_pixel_app_export(unity_raw_art_path, "BasicBat")
