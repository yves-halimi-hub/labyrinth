// batch3.5 agent (item #6): sub-element attachment records - the importer
// parses the .efyvlaby top-level "attachments" array onto the imported asset
// (EntityAttachmentRecord on the schema-backed base) and LivingEntity
// minimally STORES them for future dynamic sub-element rendering (which is
// deferred; the attachment pixels are already flattened into the atlas).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EFYV.Core.Data;
using EFYV.Editor;
using EFYVBackend.Core.Models;
using UnityEditor;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

internal static partial class Program
{
    private static List<AttachmentJson> RuntimeAttachmentList()
    {
        return new List<AttachmentJson>
        {
            new AttachmentJson { frameIndex = 0, subElement = "Torch", x = 3, y = 4, zOrder = 5, flipX = true },
            new AttachmentJson { frameIndex = 0, subElement = "Shield", x = -2, y = 9, zOrder = -5 },
            new AttachmentJson { frameIndex = 2, subElement = "Torch", x = 0, y = 0, zOrder = 0, flipY = true }
        };
    }

    // ------------------------------------------------------------------
    // Importer: attachments convert (with flip defaults) and import end to
    // end onto the ScriptableObject; invalid records reject the import.
    // ------------------------------------------------------------------
    private static void TestImporterAttachmentRecordsEndToEnd()
    {
        // Conversion resolves absent flips to false and copies the wire list.
        List<AttachmentJson> wire = RuntimeAttachmentList();
        EntityAttachmentRecord[] converted = (EntityAttachmentRecord[])InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertAttachments", wire);
        Equal(3, converted.Length);
        Equal("Torch", converted[0].SubElementName);
        Equal(3, converted[0].X);
        Equal(4, converted[0].Y);
        Equal(5, converted[0].ZOrder);
        Check(converted[0].FlipX && !converted[0].FlipY);
        Check(!converted[1].FlipX && !converted[1].FlipY);
        Equal(2, converted[2].FrameIndex);
        Check(converted[2].FlipY);
        Equal(null, InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertAttachments", (List<AttachmentJson>)null));
        Equal(null, InvokeStatic(
            typeof(EFYVPixelArtImporter), "ConvertAttachments", new List<AttachmentJson>()));

        string tempRoot = Path.Combine(
            Path.GetTempPath(), "efyv-attach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            // End to end: a documentVersion-4 .efyvlaby with attachments
            // imports and lands the records on the asset's schema-backed base.
            var document = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"AttachImport\"")
                },
                hitboxes = new List<HitboxJson>(),
                atlas = ValidAtlas(),
                attachments = RuntimeAttachmentList()
            };
            string path = Path.Combine(
                tempRoot, "AttachImport" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(path, JsonSerializer.Serialize(document));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", path);
            EnemyData imported = AssetDatabase.LoadAssetAtPath<EnemyData>(
                Path.GetDirectoryName(path) + Config.Game.Importer.PathSeparator +
                "AttachImport" + Config.Game.Importer.ExtensionAsset);
            Check(imported != null, "Attachment .efyvlaby must import.");
            EntityAttachmentRecord[] records = imported.ImportedAttachments;
            Equal(3, records.Length);
            Equal("Torch", records[0].SubElementName);
            Equal(5, records[0].ZOrder);
            Check(records[0].FlipX);
            Equal("Shield", records[1].SubElementName);
            Equal(2, records[2].FrameIndex);

            // A LEGACY document (no attachments member) clears any previous
            // records on reimport instead of leaving stale ones behind.
            var legacy = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"AttachImport\"")
                },
                hitboxes = new List<HitboxJson>(),
                atlas = ValidAtlas()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(legacy));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", path);
            Equal(null, imported.ImportedAttachments);

            // Invalid records reject the whole import through the shared
            // backend gate, naming the offending index.
            var broken = new EFYVJsonFormat
            {
                documentVersion = Config.Backend.Exporter.CurrentDocumentVersion,
                assetType = nameof(EnemyData),
                properties = new Dictionary<string, JsonElement>
                {
                    [Config.Game.Importer.KeyEntityName] = JsonValue("\"BrokenAttach\"")
                },
                hitboxes = new List<HitboxJson>(),
                atlas = ValidAtlas(),
                attachments = new List<AttachmentJson>
                {
                    new AttachmentJson { frameIndex = 0, subElement = "Fine" },
                    new AttachmentJson { frameIndex = -1, subElement = "Bad" }
                }
            };
            string brokenPath = Path.Combine(
                tempRoot, "BrokenAttach" + Config.Game.Importer.ExtensionEFYV);
            File.WriteAllText(brokenPath, JsonSerializer.Serialize(broken));
            InvokeStatic(typeof(EFYVPixelArtImporter), "ImportEFYVAsset", brokenPath);
            Equal(null, AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(
                Path.GetDirectoryName(brokenPath) + Config.Game.Importer.PathSeparator +
                "BrokenAttach" + Config.Game.Importer.ExtensionAsset));
            Equal(string.Format(
                    Config.Game.Importer.LogErrorInvalidAttachments, brokenPath, 1),
                Debug.Messages[^1]);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // ------------------------------------------------------------------
    // LivingEntity: the minimal runtime consumer stores the records and
    // answers per-frame queries; rendering from them is deferred.
    // ------------------------------------------------------------------
    private static void TestLivingEntityStoresAttachments()
    {
        var data = ScriptableObject.CreateInstance<LivingEntityData>();
        var block = new EFYVBackend.Core.Data.FastSchemaBlock();
        block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.MaxHealth, 40f);
        block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.BaseSpeed, 1f);
        data.SetSchemaBlock(block);
        data.SetImportedAttachments(new[]
        {
            new EntityAttachmentRecord
            {
                FrameIndex = 0, SubElementName = "Torch", X = 3, Y = 4, ZOrder = 5, FlipX = true
            },
            new EntityAttachmentRecord { FrameIndex = 0, SubElementName = "Shield" },
            new EntityAttachmentRecord { FrameIndex = 2, SubElementName = "Torch" }
        });

        ProbeLiving live = CreateComponent<ProbeLiving>(addRenderer: true);
        live.Initialize();
        live.LoadData(data);
        Equal(3, live.AuthoredAttachments.Length);
        Equal("Torch", live.AuthoredAttachments[0].SubElementName);
        Check(live.AuthoredAttachments[0].FlipX);
        Equal(2, live.CountAttachmentsForFrame(0));
        Equal(0, live.CountAttachmentsForFrame(1));
        Equal(1, live.CountAttachmentsForFrame(2));

        // Storage survives pooled reuse; only a data swap changes it.
        live.OnSpawn();
        live.TakeDamage(1f);
        Equal(3, live.AuthoredAttachments.Length);
        var bare = ScriptableObject.CreateInstance<LivingEntityData>();
        live.LoadData(bare);
        Equal(null, live.AuthoredAttachments);
        Equal(0, live.CountAttachmentsForFrame(0));
    }
}
