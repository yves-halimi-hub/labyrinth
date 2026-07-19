using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EFYV.Core.Data;
using EFYV.Editor;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Models;
using UnityEditor;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

// batch3.8 agent: item #33b - runtime-extensible asset fields on the game
// side. SchemaBackedAssetData's string-keyed custom-property store (raw JSON
// text values + typed parse-on-read accessors) and the importer path that
// parks unknown .efyvlaby properties keys there (logging them, never
// dropping them - batch-2 behavior) and clears stale entries on reimport.
internal static partial class Program
{
    private static void TestCustomPropertyStoreAccessors()
    {
        var asset = ScriptableObject.CreateInstance<GameAssetData>();
        Equal(0, asset.CustomPropertyCount);
        Check(!asset.TryGetCustomProperty("anything", out string missing));
        Equal(null, missing);
        Check(!asset.TryGetCustomProperty(null, out _));

        asset.SetCustomProperties(
            new[] { "sparkleFactor", "comboCount", "glowLabel", "zzFlag" },
            new[] { "3.25", "7", "purple", "true" });
        Equal(4, asset.CustomPropertyCount);

        Check(asset.TryGetCustomProperty("glowLabel", out string label));
        Equal("purple", label);
        Check(asset.TryGetCustomProperty("zzFlag", out string flag));
        Equal("true", flag);
        Check(!asset.TryGetCustomProperty("GLOWLABEL", out _), "Lookup must be ordinal.");
        Check(!asset.TryGetCustomProperty("absent", out _));

        // Typed accessors parse on read with invariant culture.
        Check(asset.TryGetCustomFloat("sparkleFactor", out float sparkle));
        Near(3.25f, sparkle);
        Check(asset.TryGetCustomFloat("comboCount", out float comboAsFloat));
        Near(7f, comboAsFloat);
        Check(asset.TryGetCustomInt("comboCount", out int combo));
        Equal(7, combo);
        Check(!asset.TryGetCustomInt("sparkleFactor", out _), "Int accessor must reject float text.");
        Check(!asset.TryGetCustomFloat("glowLabel", out _), "Float accessor must reject words.");
        Check(!asset.TryGetCustomInt("absent", out _));

        // Mismatched arrays are rejected before anything is stored.
        Throws<ArgumentException>(() => asset.SetCustomProperties(new[] { "a", "b" }, new[] { "1" }));
        Equal(4, asset.CustomPropertyCount, "Failed store must not clobber existing entries.");

        // Null clears the store (the importer's no-unknown-keys path).
        asset.SetCustomProperties(null, null);
        Equal(0, asset.CustomPropertyCount);
        Check(!asset.TryGetCustomProperty("sparkleFactor", out _));
        asset.SetCustomProperties(new[] { "solo" }, new[] { "1" });
        asset.SetCustomProperties(new[] { "solo" }, null);
        Equal(0, asset.CustomPropertyCount, "A null side must clear, not throw.");
    }

    private static void TestImporterCustomPropertiesEndToEnd()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(), "efyv-custom-fields-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            // Unknown keys of every JSON shape ride into the custom store as
            // raw text (strings unquoted), while known manifest keys still map
            // into the schema block and identity/facing keys stay untouched.
            var format = new EFYVJsonFormat
            {
                assetType = nameof(GameAssetData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Shared.AssetNameField] = JsonValue("\"CustomProbe\""),
                    [Config.Shared.TrapDamageField] = JsonValue("42.5"),
                    ["sparkleFactor"] = JsonValue("3.25"),
                    ["comboCount"] = JsonValue("7"),
                    ["glowLabel"] = JsonValue("\"purple\""),
                    ["zzFlag"] = JsonValue("true")
                }
            };
            string path = Path.Combine(tempRoot, "CustomProbe" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(path, JsonSerializer.Serialize(format));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", path);

            string assetPath = Path.GetDirectoryName(path) + Config.Game.Importer.PathSeparator +
                "CustomProbe" + Config.Game.Importer.ExtensionAsset;
            GameAssetData imported = AssetDatabase.LoadAssetAtPath<GameAssetData>(assetPath);
            Check(imported != null, "Custom-fields import failed.");
            Near(42.5f, imported.GetSchemaBlock().GetFloat((int)AssetSchema.TrapDamage));

            Equal(4, imported.CustomPropertyCount);
            Check(imported.TryGetCustomFloat("sparkleFactor", out float sparkle));
            Near(3.25f, sparkle);
            Check(imported.TryGetCustomInt("comboCount", out int combo));
            Equal(7, combo);
            Check(imported.TryGetCustomProperty("glowLabel", out string label));
            Equal("purple", label, "String values must land unquoted.");
            Check(imported.TryGetCustomProperty("zzFlag", out string flag));
            Equal("true", flag, "Non-string values keep their raw JSON text.");
            Check(!imported.TryGetCustomProperty(Config.Shared.AssetNameField, out _),
                "Identity keys must not leak into the custom store.");
            Check(!imported.TryGetCustomProperty(Config.Shared.TrapDamageField, out _),
                "Manifest keys must not leak into the custom store.");

            // The unknown keys are still LOGGED (batch-2 logs-not-drops
            // behavior is unchanged by the parking step).
            string expectedWarning = string.Format(
                Config.Game.Importer.LogWarningUnknownSchemaKeys,
                path,
                "sparkleFactor, comboCount, glowLabel, zzFlag");
            Check(Debug.Messages.Contains(expectedWarning),
                "Unknown keys must still be logged: " + expectedWarning);

            // A reimport WITHOUT unknown keys clears the stale entries.
            format.properties = new Dictionary<string, JsonElement>
            {
                [Config.Shared.AssetNameField] = JsonValue("\"CustomProbe\""),
                [Config.Shared.TrapDamageField] = JsonValue("41.5")
            };
            File.WriteAllText(path, JsonSerializer.Serialize(format));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", path);
            GameAssetData reimported = AssetDatabase.LoadAssetAtPath<GameAssetData>(assetPath);
            Check(reimported != null);
            Near(41.5f, reimported.GetSchemaBlock().GetFloat((int)AssetSchema.TrapDamage));
            Equal(0, reimported.CustomPropertyCount, "Reimport must clear stale custom entries.");
            Check(!reimported.TryGetCustomProperty("sparkleFactor", out _));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
