// batch3.5 agent (item #6): the .efyvlaby sub-element attachment wire
// contract - the shared TryValidateAttachments gate, the documentVersion-4
// writer (omit-when-empty, flip-when-true), byte identity for attachment-free
// documents, and the AttachmentJson round trip.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EFYVBackend.Core.Export;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        private static AttachmentJson AttachmentEntry(
            int frameIndex,
            string subElement,
            int x = 0,
            int y = 0,
            int zOrder = 0,
            bool? flipX = null,
            bool? flipY = null)
        {
            return new AttachmentJson
            {
                frameIndex = frameIndex,
                subElement = subElement,
                x = x,
                y = y,
                zOrder = zOrder,
                flipX = flipX,
                flipY = flipY
            };
        }

        private static void TestAttachmentSharedValidator()
        {
            // Null (absent member) is legal; so is an empty list.
            Assert(FastExporter.TryValidateAttachments(null, out int invalidIndex));
            AssertEqual(-1, invalidIndex);
            Assert(FastExporter.TryValidateAttachments(new List<AttachmentJson>(), out invalidIndex));
            AssertEqual(-1, invalidIndex);

            // A fully-populated valid list, including both zOrder bounds.
            var valid = new List<AttachmentJson>
            {
                AttachmentEntry(0, "Torch", 3, 4, BackendConfig.Exporter.MinAttachmentZOrder),
                AttachmentEntry(0, "Torch", -9, -9, BackendConfig.Exporter.MaxAttachmentZOrder, true, true),
                AttachmentEntry(int.MaxValue, "Shield_2", 0, 0, 0, false, null)
            };
            Assert(FastExporter.TryValidateAttachments(valid, out invalidIndex));
            AssertEqual(-1, invalidIndex);

            // Per-entry failures report the exact offending index.
            var badName = new List<AttachmentJson>
            {
                AttachmentEntry(0, "Fine"),
                AttachmentEntry(0, "../escape")
            };
            Assert(!FastExporter.TryValidateAttachments(badName, out invalidIndex));
            AssertEqual(1, invalidIndex);
            // Note: an INTERIOR dot ("a.b") is a legal stem under
            // SafePathPolicy; only trailing dots/spaces and reserved device
            // base names are rejected.
            foreach (string unsafeName in new[] { null, "", "  ", "CON", "lpt1.log", "name." })
            {
                Assert(!FastExporter.TryValidateAttachments(
                    new List<AttachmentJson> { AttachmentEntry(0, unsafeName) },
                    out invalidIndex));
                AssertEqual(0, invalidIndex);
            }
            Assert(!FastExporter.TryValidateAttachments(
                new List<AttachmentJson> { AttachmentEntry(-1, "Torch") },
                out invalidIndex));
            AssertEqual(0, invalidIndex);
            Assert(!FastExporter.TryValidateAttachments(
                new List<AttachmentJson>
                {
                    AttachmentEntry(0, "Torch", 0, 0, BackendConfig.Exporter.MinAttachmentZOrder - 1)
                },
                out invalidIndex));
            AssertEqual(0, invalidIndex);
            Assert(!FastExporter.TryValidateAttachments(
                new List<AttachmentJson>
                {
                    AttachmentEntry(0, "Torch", 0, 0, BackendConfig.Exporter.MaxAttachmentZOrder + 1)
                },
                out invalidIndex));
            AssertEqual(0, invalidIndex);

            // Per-frame cap: exactly MaxAttachmentsPerFrame on one frame is
            // legal; one more fails AT the overflowing entry. Other frames
            // keep their own budgets.
            var capped = new List<AttachmentJson>();
            for (int index = 0; index < BackendConfig.Exporter.MaxAttachmentsPerFrame; index++)
                capped.Add(AttachmentEntry(7, "Torch", index));
            capped.Add(AttachmentEntry(8, "Torch"));
            Assert(FastExporter.TryValidateAttachments(capped, out invalidIndex));
            capped.Insert(3, AttachmentEntry(7, "Torch", 999));
            Assert(!FastExporter.TryValidateAttachments(capped, out invalidIndex));
            AssertEqual(BackendConfig.Exporter.MaxAttachmentsPerFrame, invalidIndex);
        }

        private static void TestAttachmentWireWriterAndRoundTrip()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "EFYVAttachWire-" + Guid.NewGuid().ToString("N"));
            try
            {
                var properties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = "AttachProbe"
                };
                var metadata = new AtlasMetadataJson
                {
                    formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                    frameWidth = 2,
                    frameHeight = 2,
                    atlasWidth = 4,
                    atlasHeight = 2,
                    animations = new List<AnimationMetadataJson>
                    {
                        new AnimationMetadataJson { name = "Idle", fps = 8, startFrame = 0, frameCount = 2 }
                    }
                };
                var pixels = new PackedRgba[8];
                var attachments = new List<AttachmentJson>
                {
                    AttachmentEntry(0, "Torch", 1, -2, 5),
                    AttachmentEntry(1, "Shield", 0, 3, -5, true),
                    AttachmentEntry(1, "Torch", 2, 2, 0, null, true)
                };

                FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", properties, new List<HitboxJson>(),
                    pixels, 4, 2, metadata, "EnemyData", attachments);
                string jsonPath = Path.Combine(root, "AttachProbe" + BackendConfig.Exporter.EfyvExtension);

                // Model round trip: values, absent-flip resolution left to
                // consumers (null on the wire model), documentVersion 4.
                EFYVJsonFormat imported = FastImporter.ParseEfyvFile(jsonPath);
                AssertEqual(BackendConfig.Exporter.CurrentDocumentVersion, imported.EffectiveDocumentVersion);
                AssertEqual(3, imported.attachments.Count);
                AssertEqual(0, imported.attachments[0].frameIndex);
                AssertEqual("Torch", imported.attachments[0].subElement);
                AssertEqual(1, imported.attachments[0].x);
                AssertEqual(-2, imported.attachments[0].y);
                AssertEqual(5, imported.attachments[0].zOrder);
                Assert(!imported.attachments[0].flipX.HasValue);
                Assert(!imported.attachments[0].flipY.HasValue);
                AssertEqual(true, imported.attachments[1].flipX);
                Assert(!imported.attachments[1].flipY.HasValue);
                Assert(!imported.attachments[2].flipX.HasValue);
                AssertEqual(true, imported.attachments[2].flipY);

                // Raw JSON: attachments follows atlas; per-entry member order
                // is pinned; false flips are OMITTED, true flips written.
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(jsonPath)))
                {
                    var topNames = new List<string>();
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                        topNames.Add(property.Name);
                    AssertEqual(BackendConfig.Exporter.FieldAtlas, topNames[topNames.Count - 2]);
                    AssertEqual(BackendConfig.Exporter.FieldAttachments, topNames[topNames.Count - 1]);

                    JsonElement entries = document.RootElement.GetProperty(
                        BackendConfig.Exporter.FieldAttachments);
                    var firstNames = new List<string>();
                    foreach (JsonProperty property in entries[0].EnumerateObject())
                        firstNames.Add(property.Name);
                    AssertSequenceEqual(
                        new[]
                        {
                            BackendConfig.Exporter.FieldFrameIndex,
                            BackendConfig.Exporter.FieldSubElement,
                            BackendConfig.Exporter.FieldX,
                            BackendConfig.Exporter.FieldY,
                            BackendConfig.Exporter.FieldZOrder
                        },
                        firstNames.ToArray());
                    var secondNames = new List<string>();
                    foreach (JsonProperty property in entries[1].EnumerateObject())
                        secondNames.Add(property.Name);
                    AssertEqual(BackendConfig.Exporter.FieldFlipX, secondNames[secondNames.Count - 1]);
                    var thirdNames = new List<string>();
                    foreach (JsonProperty property in entries[2].EnumerateObject())
                        thirdNames.Add(property.Name);
                    AssertEqual(BackendConfig.Exporter.FieldFlipY, thirdNames[thirdNames.Count - 1]);
                }

                // Byte identity: null and EMPTY attachment lists write the
                // exact same document as the pre-attachment overload.
                var noneProperties = new Dictionary<string, object>
                {
                    [BackendConfig.Exporter.FieldEntityName] = "NoAttach"
                };
                FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", noneProperties, new List<HitboxJson>(),
                    pixels, 4, 2, metadata, "EnemyData");
                byte[] withoutOverload = File.ReadAllBytes(
                    Path.Combine(root, "NoAttach" + BackendConfig.Exporter.EfyvExtension));
                FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", noneProperties, new List<HitboxJson>(),
                    pixels, 4, 2, metadata, "EnemyData", new List<AttachmentJson>());
                byte[] withEmptyList = File.ReadAllBytes(
                    Path.Combine(root, "NoAttach" + BackendConfig.Exporter.EfyvExtension));
                AssertSequenceEqual(withoutOverload, withEmptyList);
                EFYVJsonFormat none = FastImporter.ParseEfyvFile(
                    Path.Combine(root, "NoAttach" + BackendConfig.Exporter.EfyvExtension));
                AssertEqual(null, none.attachments);

                // The exporter REJECTS invalid records through the shared gate.
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", properties, new List<HitboxJson>(),
                    pixels, 4, 2, metadata, "EnemyData",
                    new List<AttachmentJson> { AttachmentEntry(-1, "Torch") }));
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", properties, new List<HitboxJson>(),
                    pixels, 4, 2, metadata, "EnemyData",
                    new List<AttachmentJson> { AttachmentEntry(0, "../up") }));

                // Reflection serialization of the wire struct round-trips.
                string serialized = JsonSerializer.Serialize(attachments[1]);
                AttachmentJson reparsed = JsonSerializer.Deserialize<AttachmentJson>(serialized);
                AssertEqual("Shield", reparsed.subElement);
                AssertEqual(-5, reparsed.zOrder);
                AssertEqual(true, reparsed.flipX);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
