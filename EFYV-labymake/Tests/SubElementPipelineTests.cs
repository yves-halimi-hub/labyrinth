// batch3.5 agent (item #6): the sub-element pipeline - pivot/default
// transform on SubElement, the .efyvsub v2 format (+ v1 legacy reads), the
// per-frame attachment model with undoable session CRUD and stamp-tool
// attachment gestures, .efyvmake persistence, and the export flow that both
// FLATTENS attachments into the atlas pixels and emits structured metadata.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

internal static partial class Program
{
    // ------------------------------------------------------------------
    // Model: pivot + default transform, attachment record contracts.
    // ------------------------------------------------------------------
    private static void TestSubElementPivotTransformAndAttachmentModel()
    {
        // Default pivot is the CENTER (legacy stamp centering), clamped
        // inside the grid for degenerate sizes.
        var wide = new SubElement("Wide", 4, 3, new uint[12]);
        Require(wide.PivotX == 2 && wide.PivotY == 1);
        var dot = new SubElement("Dot", 1, 1, new uint[1]);
        Require(dot.PivotX == 0 && dot.PivotY == 0);
        Require(dot.DefaultOffsetX == 0 && dot.DefaultOffsetY == 0);
        Require(dot.DefaultZOrder == Config.Attachment.DefaultZOrder);
        Require(!dot.DefaultFlipX && !dot.DefaultFlipY);

        // Pivot setters validate against the element's own grid.
        wide.PivotX = 3;
        wide.PivotY = 0;
        Require(wide.PivotX == 3 && wide.PivotY == 0);
        RequireThrows<ArgumentOutOfRangeException>(() => wide.PivotX = -1);
        RequireThrows<ArgumentOutOfRangeException>(() => wide.PivotX = 4);
        RequireThrows<ArgumentOutOfRangeException>(() => wide.PivotY = 3);
        RequireThrows<ArgumentOutOfRangeException>(() => wide.DefaultZOrder =
            Config.Attachment.MaxZOrder + 1);
        RequireThrows<ArgumentOutOfRangeException>(() => wide.DefaultZOrder =
            Config.Attachment.MinZOrder - 1);
        wide.DefaultZOrder = Config.Attachment.MinZOrder;
        Require(wide.DefaultZOrder == Config.Attachment.MinZOrder);

        // Attachment record: the constructor is the validation gate.
        var attachment = new SubElementAttachment("Torch", 5, -3, 7, true, false);
        Require(attachment.SubElementName == "Torch");
        Require(attachment.X == 5 && attachment.Y == -3 && attachment.ZOrder == 7);
        Require(attachment.FlipX && !attachment.FlipY);
        RequireThrows<ArgumentException>(() => new SubElementAttachment("../up", 0, 0, 0, false, false));
        RequireThrows<ArgumentException>(() => new SubElementAttachment("", 0, 0, 0, false, false));
        RequireThrows<ArgumentException>(() => new SubElementAttachment("CON", 0, 0, 0, false, false));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            new SubElementAttachment("Torch", 0, 0, Config.Attachment.MaxZOrder + 1, false, false));
        RequireThrows<ArgumentOutOfRangeException>(() =>
            attachment.ZOrder = Config.Attachment.MinZOrder - 1);

        // Clones are deep and independent.
        SubElementAttachment clone = attachment.Clone();
        clone.X = 99;
        clone.FlipY = true;
        Require(attachment.X == 5 && !attachment.FlipY);
        Require(clone.SubElementName == "Torch" && clone.ZOrder == 7);

        // Anchor grab tolerance is a per-axis box.
        Require(attachment.IsNearAnchor(5, -3, 0));
        Require(attachment.IsNearAnchor(
            5 + Config.Attachment.GrabRadius, -3 - Config.Attachment.GrabRadius,
            Config.Attachment.GrabRadius));
        Require(!attachment.IsNearAnchor(
            5 + Config.Attachment.GrabRadius + 1, -3, Config.Attachment.GrabRadius));
        Require(!attachment.IsNearAnchor(
            5, -3 + Config.Attachment.GrabRadius + 1, Config.Attachment.GrabRadius));

        // Frame.Clone deep-copies attachment records.
        var frame = new Frame(8, 8);
        frame.Attachments.Add(attachment);
        Frame cloned = frame.Clone();
        Require(cloned.Attachments.Count == 1);
        cloned.Attachments[0].X = 77;
        Require(frame.Attachments[0].X == 5);
    }

    // ------------------------------------------------------------------
    // Asset bank: .efyvsub v2 round trip, v1 legacy reads, corrupt corpus,
    // and the resolver seam.
    // ------------------------------------------------------------------
    private static void TestSubElementBankV2RoundTripAndLegacy()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var bank = new AssetBankManager(root);
            int failures = 0;
            string lastFailedPath = null;
            bank.LoadFailed += (path, exception) =>
            {
                failures++;
                lastFailedPath = path;
                Require(exception != null);
            };

            // Full v2 round trip with a non-default pivot/transform.
            var saved = new SubElement("Emblem", 2, 3, new uint[]
            {
                Pack(1, 0, 0, 255), Pack(2, 0, 0, 255),
                Pack(3, 0, 0, 255), Pack(4, 0, 0, 255),
                Pack(5, 0, 0, 255), Pack(6, 0, 0, 255)
            });
            saved.PivotX = 0;
            saved.PivotY = 2;
            saved.DefaultOffsetX = -3;
            saved.DefaultOffsetY = 7;
            saved.DefaultZOrder = 9;
            saved.DefaultFlipX = true;
            bank.SaveSubElement(saved);

            Require(bank.TryResolveSubElement("Emblem", out SubElement loaded));
            Require(loaded.Name == "Emblem" && loaded.Width == 2 && loaded.Height == 3);
            Require(loaded.PivotX == 0 && loaded.PivotY == 2);
            Require(loaded.DefaultOffsetX == -3 && loaded.DefaultOffsetY == 7);
            Require(loaded.DefaultZOrder == 9);
            Require(loaded.DefaultFlipX && !loaded.DefaultFlipY);
            for (int index = 0; index < 6; index++)
                Require(loaded.Pixels[index] == saved.Pixels[index]);

            // A VERSION-1 legacy file (no transform header) loads with the
            // default pivot/transform - the versioning-consistency fix.
            SubPipeWriteSubElementFile(
                Path.Combine(root, "OldTimer" + Config.Export.ExtensionEfyvSub),
                Config.Persistence.MinSupportedSubElementFormatVersion,
                "OldTimer", 4, 1, false, 0, 0, 0, 0,
                new uint[] { 1u, 2u, 3u, 4u }, 4);
            Require(bank.TryResolveSubElement("OldTimer", out SubElement legacy));
            Require(legacy.Width == 4 && legacy.Height == 1);
            Require(legacy.PivotX == 2 && legacy.PivotY == 0); // center default
            Require(legacy.DefaultOffsetX == 0 && legacy.DefaultOffsetY == 0);
            Require(legacy.DefaultZOrder == Config.Attachment.DefaultZOrder);
            Require(!legacy.DefaultFlipX && !legacy.DefaultFlipY);
            Require(legacy.Pixels[3] == 4u);
            Require(failures == 0);

            // Future versions and corrupt v2 headers are rejected per file
            // (LoadFailed), never aborting the whole scan.
            SubPipeWriteSubElementFile(
                Path.Combine(root, "z1-future" + Config.Export.ExtensionEfyvSub),
                Config.Persistence.SubElementFormatVersion + 1,
                "Future", 1, 1, true, 0, 0, 0, 0, new uint[] { 1u }, 1);
            SubPipeWriteSubElementFile(
                Path.Combine(root, "z2-pivot" + Config.Export.ExtensionEfyvSub),
                Config.Persistence.SubElementFormatVersion,
                "BadPivot", 2, 2, true, 2, 0, 0, 0, new uint[4], 4);
            SubPipeWriteSubElementFile(
                Path.Combine(root, "z3-flags" + Config.Export.ExtensionEfyvSub),
                Config.Persistence.SubElementFormatVersion,
                "BadFlags", 1, 1, true, 0, 0, 0, 0, new uint[1], 1,
                flags: 0x4);
            SubPipeWriteSubElementFile(
                Path.Combine(root, "z4-zorder" + Config.Export.ExtensionEfyvSub),
                Config.Persistence.SubElementFormatVersion,
                "BadZ", 1, 1, true, 0, 0, 0, 0, new uint[1], 1,
                zOrder: Config.Attachment.MaxZOrder + 1);
            List<SubElement> all = bank.LoadAllSubElements();
            Require(all.Count == 2 && failures == 4);
            Require(all[0].Name == "Emblem" && all[1].Name == "OldTimer");

            // Resolver contract: unresolvable names return false; only
            // present-but-broken files raise LoadFailed.
            failures = 0;
            Require(!bank.TryResolveSubElement("Missing", out SubElement missing) && missing == null);
            Require(!bank.TryResolveSubElement("../escape", out _));
            Require(failures == 0);
            // Resolution addresses the FILE STEM; a present-but-broken file
            // reports through LoadFailed and resolves false.
            Require(!bank.TryResolveSubElement("z2-pivot", out _));
            Require(failures == 1 && lastFailedPath.EndsWith(
                "z2-pivot" + Config.Export.ExtensionEfyvSub, StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Session CRUD: undoable add/move/remove, caps, guards, validator.
    // ------------------------------------------------------------------
    private static void TestSessionAttachmentCrudUndoRedo()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("AttachCrud", project, root))
            {
                session.AutosaveEnabled = false;
                Frame frame = session.CurrentFrame;

                SubElementAttachment added = session.AddAttachment("Torch", 3, 4, 5, true, false);
                Require(frame.Attachments.Count == 1 && ReferenceEquals(frame.Attachments[0], added));
                Require(session.Current.IsDirty);
                Require(session.History.Current.UndoCount == 1);
                Require(session.Undo());
                Require(frame.Attachments.Count == 0);
                Require(session.Redo());
                Require(frame.Attachments.Count == 1);

                // Move: undoable, exact restore, and a same-position no-op
                // records nothing.
                int undoCountBefore = session.History.Current.UndoCount;
                session.MoveAttachment(0, 3, 4);
                Require(session.History.Current.UndoCount == undoCountBefore);
                session.MoveAttachment(0, 9, -2);
                Require(frame.Attachments[0].X == 9 && frame.Attachments[0].Y == -2);
                Require(session.Undo());
                Require(frame.Attachments[0].X == 3 && frame.Attachments[0].Y == 4);
                Require(session.Redo());
                Require(frame.Attachments[0].X == 9);

                session.RemoveAttachment(0);
                Require(frame.Attachments.Count == 0);
                Require(session.Undo());
                Require(frame.Attachments.Count == 1 && frame.Attachments[0].ZOrder == 5);

                // Validation gates and index guards.
                RequireThrows<ArgumentException>(() => session.AddAttachment("../up", 0, 0, 0, false, false));
                RequireThrows<ArgumentOutOfRangeException>(() => session.AddAttachment(
                    "Torch", 0, 0, Config.Attachment.MaxZOrder + 1, false, false));
                RequireThrows<ArgumentOutOfRangeException>(() => session.MoveAttachment(5, 0, 0));
                RequireThrows<ArgumentOutOfRangeException>(() => session.RemoveAttachment(-1));

                // Per-frame cap enforced on add.
                while (frame.Attachments.Count < Config.Attachment.MaxPerFrame)
                    frame.Attachments.Add(new SubElementAttachment("Filler", 0, 0, 0, false, false));
                RequireThrows<InvalidOperationException>(() =>
                    session.AddAttachment("Torch", 0, 0, 0, false, false));

                // Validator: the cap is a structural error; so is a null entry.
                var validator = new ProjectValidator(new AssetSchemaService());
                frame.Attachments.Add(new SubElementAttachment("Overflow", 0, 0, 0, false, false));
                Require(ContainsIssue(
                    validator.Validate(project, ProjectValidationScope.Structural),
                    ProjectIssueCode.AttachmentLimitExceeded));
                frame.Attachments.Clear();
                frame.Attachments.Add(null);
                Require(ContainsIssue(
                    validator.Validate(project, ProjectValidationScope.Structural),
                    ProjectIssueCode.InvalidAttachment));
                frame.Attachments.Clear();

                // CRUD refuses to run inside an active pointer gesture.
                var stamp = new StampTool { ActiveSubElement = new SubElement("Torch", 1, 1, new uint[1]) };
                session.ActiveTool = stamp;
                Require(session.PointerDown(2, 2));
                RequireThrows<InvalidOperationException>(() =>
                    session.AddAttachment("Torch", 0, 0, 0, false, false));
                RequireThrows<InvalidOperationException>(() => session.MoveAttachment(0, 1, 1));
                RequireThrows<InvalidOperationException>(() => session.RemoveAttachment(0));
                session.PointerUp(2, 2);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Stamp tool: attachment placement/repositioning gestures commit as ONE
    // undoable FrameEditCommand; the legacy bake mode blits via the pivot.
    // ------------------------------------------------------------------
    private static void TestStampToolAttachmentGesturesAndBake()
    {
        string root = NewTemporaryDirectory();
        try
        {
            EFYVProject project = CreateValidProject(root, 1);
            using (DesignerSession session = DesignerSession.Create("AttachStamp", project, root))
            {
                session.AutosaveEnabled = false;
                Frame frame = session.CurrentFrame;

                var element = new SubElement("Gem", 3, 3, new uint[9]);
                element.DefaultOffsetX = 2;
                element.DefaultOffsetY = 3;
                element.DefaultZOrder = 4;
                element.DefaultFlipY = true;
                var stamp = new StampTool { ActiveSubElement = element };
                Require(stamp.Mode == StampToolMode.PlaceAttachment); // the default
                session.ActiveTool = stamp;

                // Place + drag in one gesture: the new attachment starts at
                // pointer + default offset and follows the drag; the whole
                // gesture is ONE history entry.
                Require(session.PointerDown(5, 5));
                Require(frame.Attachments.Count == 1);
                Require(frame.Attachments[0].X == 7 && frame.Attachments[0].Y == 8);
                Require(frame.Attachments[0].ZOrder == 4);
                Require(!frame.Attachments[0].FlipX && frame.Attachments[0].FlipY);
                Require(session.PointerDrag(6, 7));
                Require(session.PointerUp(6, 7));
                Require(frame.Attachments[0].X == 8 && frame.Attachments[0].Y == 10);
                Require(session.History.Current.UndoCount == 1);
                Require(session.Undo());
                Require(frame.Attachments.Count == 0);
                Require(session.Redo());
                Require(frame.Attachments.Count == 1 && frame.Attachments[0].X == 8);

                // Grab an EXISTING attachment near its anchor and reposition
                // it - no new record, one more history entry, and the grab
                // offset keeps it from jumping under the pointer.
                Require(session.PointerDown(
                    8 + Config.Attachment.GrabRadius,
                    10 - Config.Attachment.GrabRadius));
                Require(frame.Attachments.Count == 1);
                session.PointerDrag(30, 40);
                session.PointerUp(30, 40);
                Require(frame.Attachments.Count == 1);
                Require(frame.Attachments[0].X == 30 - Config.Attachment.GrabRadius);
                Require(frame.Attachments[0].Y == 40 + Config.Attachment.GrabRadius);
                Require(session.History.Current.UndoCount == 2);
                Require(session.Undo());
                Require(frame.Attachments[0].X == 8 && frame.Attachments[0].Y == 10);
                Require(session.Redo());

                // Repositioning works with NO active sub-element; placement
                // away from any anchor is then a silent no-op.
                stamp.ActiveSubElement = null;
                Require(session.PointerDown(frame.Attachments[0].X, frame.Attachments[0].Y));
                session.PointerDrag(12, 13);
                session.PointerUp(12, 13);
                Require(frame.Attachments[0].X == 12 && frame.Attachments[0].Y == 13);
                int entriesSoFar = session.History.Current.UndoCount;
                session.PointerDown(50, 50);
                Require(!session.PointerUp(50, 50)); // no changes committed
                Require(frame.Attachments.Count == 1);
                Require(session.History.Current.UndoCount == entriesSoFar);

                // The per-frame cap turns placement into a no-op (no throw,
                // no history entry) - existing anchors still grab fine.
                stamp.ActiveSubElement = element;
                while (frame.Attachments.Count < Config.Attachment.MaxPerFrame)
                    frame.Attachments.Add(new SubElementAttachment("Filler", 60, 60, 0, false, false));
                session.PointerDown(40, 40);
                Require(!session.PointerUp(40, 40));
                Require(frame.Attachments.Count == Config.Attachment.MaxPerFrame);
                while (frame.Attachments.Count > 1) frame.Attachments.RemoveAt(1);

                // Esc mid-gesture rolls the placement back entirely.
                session.PointerDown(20, 20);
                Require(frame.Attachments.Count == 2);
                session.CancelGesture();
                Require(frame.Attachments.Count == 1);

                // Legacy bake mode: pixels blend at pointer - PIVOT (custom
                // pivot (0,0) anchors the blit's top-left on the pointer).
                var inkPixels = new uint[4];
                for (int index = 0; index < 4; index++) inkPixels[index] = Pack(9, 8, 7, 255);
                var ink = new SubElement("Ink", 2, 2, inkPixels);
                ink.PivotX = 0;
                ink.PivotY = 0;
                stamp.ActiveSubElement = ink;
                stamp.Mode = StampToolMode.BakePixels;
                Require(session.PointerDown(10, 11));
                Require(session.PointerUp(10, 11));
                Require(frame.Layers[0].GetPixel(10, 11).Rgba == Pack(9, 8, 7, 255));
                Require(frame.Layers[0].GetPixel(11, 12).Rgba == Pack(9, 8, 7, 255));
                Require(frame.Layers[0].GetPixel(9, 10).Rgba == 0u);
                Require(frame.Attachments.Count == 1); // bake never places records
                Require(session.Undo());
                Require(frame.Layers[0].GetPixel(10, 11).Rgba == 0u);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Persistence: .efyvmake round trip, legacy documents, corrupt corpus,
    // and the ResizeCanvas shift.
    // ------------------------------------------------------------------
    private static void TestAttachmentPersistenceRoundTripAndResize()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
            EFYVProject project = CreateValidProject(root, 2);
            Frame first = project.Animations[0].Frames[0];
            first.Attachments.Add(new SubElementAttachment("Torch", 3, 4, 5, true, false));
            first.Attachments.Add(new SubElementAttachment("Shield", -2, 60, -7, false, true));

            string path = persistence.SaveProject("AttachDoc", project, CancellationToken.None);
            EFYVProject restored = persistence.LoadProject("AttachDoc");
            List<SubElementAttachment> restoredAttachments =
                restored.Animations[0].Frames[0].Attachments;
            Require(restoredAttachments.Count == 2);
            Require(restoredAttachments[0].SubElementName == "Torch");
            Require(restoredAttachments[0].X == 3 && restoredAttachments[0].Y == 4);
            Require(restoredAttachments[0].ZOrder == 5);
            Require(restoredAttachments[0].FlipX && !restoredAttachments[0].FlipY);
            Require(restoredAttachments[1].SubElementName == "Shield");
            Require(restoredAttachments[1].ZOrder == -7 && restoredAttachments[1].FlipY);
            Require(restored.Animations[0].Frames[1].Attachments.Count == 0);

            // The document stores the section per frame under "attachments".
            JsonObject document = JsonNode.Parse(File.ReadAllText(path)).AsObject();
            JsonArray frameAttachments = document["animations"].AsArray()[0]
                .AsObject()["frames"].AsArray()[0]
                .AsObject()["attachments"].AsArray();
            Require(frameAttachments.Count == 2);
            Require((string)frameAttachments[0].AsObject()["subElementName"] == "Torch");
            Require((int)frameAttachments[0].AsObject()["zOrder"] == 5);
            Require((bool)frameAttachments[0].AsObject()["flipX"]);

            // LEGACY document: a missing attachments member restores empty.
            JsonObject legacy = document.DeepClone().AsObject();
            foreach (JsonNode animationNode in legacy["animations"].AsArray())
            foreach (JsonNode frameNode in animationNode.AsObject()["frames"].AsArray())
                frameNode.AsObject().Remove("attachments");
            File.WriteAllText(path, legacy.ToJsonString());
            EFYVProject legacyProject = persistence.LoadProject("AttachDoc");
            Require(legacyProject.Animations[0].Frames[0].Attachments.Count == 0);

            // Corrupt corpus: every mutation must be rejected on load.
            Action<JsonObject>[] mutations =
            {
                corrupted => SubPipeFirstAttachment(corrupted)["subElementName"] = "../escape",
                corrupted => SubPipeFirstAttachment(corrupted)["subElementName"] = "",
                corrupted => SubPipeFirstAttachment(corrupted)["zOrder"] =
                    Config.Attachment.MaxZOrder + 1,
                corrupted => SubPipeFirstAttachments(corrupted).Add(null),
                corrupted =>
                {
                    JsonArray list = SubPipeFirstAttachments(corrupted);
                    JsonNode template = list[0].DeepClone();
                    while (list.Count <= Config.Attachment.MaxPerFrame)
                        list.Add(template.DeepClone());
                }
            };
            foreach (Action<JsonObject> mutate in mutations)
            {
                JsonObject corrupted = document.DeepClone().AsObject();
                mutate(corrupted);
                File.WriteAllText(path, corrupted.ToJsonString());
                RequireThrows<InvalidDataException>(() => persistence.LoadProject("AttachDoc"));
            }

            // Save-side gate: a null entry in the live model fails the save.
            first.Attachments.Add(null);
            RequireThrows<InvalidDataException>(() =>
                persistence.SaveProject("AttachDoc", project, CancellationToken.None));
            first.Attachments.RemoveAt(first.Attachments.Count - 1);

            // ResizeCanvas shifts attachment anchors with the content and
            // undo restores the original frames exactly.
            using (DesignerSession session = DesignerSession.Create("AttachResize", project, root))
            {
                session.AutosaveEnabled = false;
                int previousWidth = project.CanvasWidth;
                int previousHeight = project.CanvasHeight;
                session.ResizeCanvas(
                    previousWidth + 10, previousHeight + 6, CanvasAnchor.BottomRight);
                Frame resized = project.Animations[0].Frames[0];
                Require(resized.Attachments.Count == 2);
                Require(resized.Attachments[0].X == 13 && resized.Attachments[0].Y == 10);
                Require(resized.Attachments[1].X == 8 && resized.Attachments[1].Y == 66);
                Require(session.Undo());
                Frame back = project.Animations[0].Frames[0];
                Require(back.Attachments[0].X == 3 && back.Attachments[0].Y == 4);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    // ------------------------------------------------------------------
    // Export: attachments flatten INTO the atlas pixels (pivot, flips,
    // z-order, clipping, unresolved-skip) AND ride as structured metadata.
    // ------------------------------------------------------------------
    private static void TestExportAttachmentFlattenAndMetadata()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var bank = new AssetBankManager(Path.Combine(root, Config.Export.AssetBankDirectoryName));

            var dot = new SubElement("Dot", 1, 1, new[] { Pack(255, 0, 0, 255) });
            bank.SaveSubElement(dot);
            var white = new SubElement("Blank", 1, 1, new[] { Pack(255, 255, 255, 255) });
            bank.SaveSubElement(white);
            var wide = new SubElement(
                "Wide", 2, 1, new[] { Pack(0, 255, 0, 255), Pack(0, 0, 255, 255) });
            bank.SaveSubElement(wide); // default pivot (1, 0)

            EFYVProject project = CreateValidProject(root, 2);
            Frame frame0 = project.Animations[0].Frames[0];
            Frame frame1 = project.Animations[0].Frames[1];
            // z-order: the white dot (z -5) is painted first, the red dot
            // (z 5) over it - authored order deliberately reversed.
            frame0.Attachments.Add(new SubElementAttachment("Dot", 30, 30, 5, false, false));
            frame0.Attachments.Add(new SubElementAttachment("Blank", 30, 30, -5, false, false));
            // Pivot placement: "Wide" pivot (1,0) at (10,10) puts green on
            // (9,10) and blue on (10,10).
            frame0.Attachments.Add(new SubElementAttachment("Wide", 10, 10, 0, false, false));
            // FlipX: flipped pivot becomes (0,0), so (20,20) shows blue at
            // (20,20) and green at (21,20).
            frame0.Attachments.Add(new SubElementAttachment("Wide", 20, 20, 1, true, false));
            // Unresolved: metadata only, pixels untouched.
            frame0.Attachments.Add(new SubElementAttachment("Ghost", 40, 40, 0, false, false));
            // Clipping: a pivot landing off-canvas draws nothing and faults
            // nothing.
            frame0.Attachments.Add(new SubElementAttachment("Dot", -50, -50, 0, false, false));
            frame1.Attachments.Add(new SubElementAttachment("Dot", 1, 1, 0, false, false));

            var engine = new ExportEngine(
                new ProjectValidator(new AssetSchemaService()),
                bank);
            ExportResult result = engine.Export(project, CancellationToken.None);

            // --- Flattened pixels (2 frames of 64x64 pack as a 128x64 grid).
            uint[] atlas = EFYVBackend.Core.Export.FastPngDecoder.Read(
                File.ReadAllBytes(result.ImagePath), out int atlasWidth, out int atlasHeight);
            Require(atlasWidth == 128 && atlasHeight == 64);
            Require(atlas[30 * 128 + 30] == Pack(255, 0, 0, 255));      // red over white
            Require(atlas[10 * 128 + 9] == Pack(0, 255, 0, 255));       // green (pivot)
            Require(atlas[10 * 128 + 10] == Pack(0, 0, 255, 255));      // blue (pivot)
            Require(atlas[20 * 128 + 20] == Pack(0, 0, 255, 255));      // blue (flipX)
            Require(atlas[20 * 128 + 21] == Pack(0, 255, 0, 255));      // green (flipX)
            Require(atlas[40 * 128 + 40] == 0u);                        // Ghost skipped
            Require(atlas[1 * 128 + 64 + 1] == Pack(255, 0, 0, 255));   // frame 1 dot

            // --- Structured metadata: global frame indices, authored order,
            // flips only when true.
            JsonObject document = JsonNode.Parse(File.ReadAllText(result.MetadataPath)).AsObject();
            Require((int)document[BackendConfig.Exporter.FieldDocumentVersion] ==
                BackendConfig.Exporter.CurrentDocumentVersion);
            JsonArray records = document[BackendConfig.Exporter.FieldAttachments].AsArray();
            Require(records.Count == 7);
            Require((int)records[0].AsObject()[BackendConfig.Exporter.FieldFrameIndex] == 0);
            Require((string)records[0].AsObject()[BackendConfig.Exporter.FieldSubElement] == "Dot");
            Require((int)records[0].AsObject()[BackendConfig.Exporter.FieldZOrder] == 5);
            Require(records[0].AsObject()[BackendConfig.Exporter.FieldFlipX] == null);
            Require((string)records[1].AsObject()[BackendConfig.Exporter.FieldSubElement] == "Blank");
            Require((bool)records[3].AsObject()[BackendConfig.Exporter.FieldFlipX]);
            Require((string)records[4].AsObject()[BackendConfig.Exporter.FieldSubElement] == "Ghost");
            Require((int)records[6].AsObject()[BackendConfig.Exporter.FieldFrameIndex] == 1);
            Require((int)records[6].AsObject()[BackendConfig.Exporter.FieldX] == 1);

            // The MODEL pixels were never mutated by flattening.
            Require(project.Animations[0].Frames[0].Layers[0].GetPixel(30, 30).Rgba == 0u);

            // --- Attachment-free projects: no "attachments" member at all
            // and (without a resolver) exports still succeed.
            EFYVProject plain = CreateValidProject(root, 1);
            plain.AssetProperties[
                EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.EntityNameField] = "PlainAttach";
            ExportResult plainResult = new ExportEngine(
                new ProjectValidator(new AssetSchemaService())).Export(plain, CancellationToken.None);
            JsonObject plainDocument = JsonNode.Parse(
                File.ReadAllText(plainResult.MetadataPath)).AsObject();
            Require(!plainDocument.ContainsKey(BackendConfig.Exporter.FieldAttachments));

            // --- Resolver-less engines skip flattening but keep metadata.
            EFYVProject unresolved = CreateValidProject(root, 1);
            unresolved.AssetProperties[
                EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.EntityNameField] = "NoResolver";
            unresolved.Animations[0].Frames[0].Attachments.Add(
                new SubElementAttachment("Dot", 5, 5, 0, false, false));
            ExportResult unresolvedResult = new ExportEngine(
                new ProjectValidator(new AssetSchemaService())).Export(unresolved, CancellationToken.None);
            uint[] unresolvedAtlas = EFYVBackend.Core.Export.FastPngDecoder.Read(
                File.ReadAllBytes(unresolvedResult.ImagePath), out _, out _);
            Require(unresolvedAtlas[5 * 64 + 5] == 0u);
            JsonObject unresolvedDocument = JsonNode.Parse(
                File.ReadAllText(unresolvedResult.MetadataPath)).AsObject();
            Require(unresolvedDocument[BackendConfig.Exporter.FieldAttachments].AsArray().Count == 1);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static JsonArray SubPipeFirstAttachments(JsonObject document)
    {
        return document["animations"].AsArray()[0]
            .AsObject()["frames"].AsArray()[0]
            .AsObject()["attachments"].AsArray();
    }

    private static JsonObject SubPipeFirstAttachment(JsonObject document)
    {
        return SubPipeFirstAttachments(document)[0].AsObject();
    }

    private static void SubPipeWriteSubElementFile(
        string path,
        int version,
        string name,
        int width,
        int height,
        bool writeTransformHeader,
        int pivotX,
        int pivotY,
        int offsetX,
        int offsetY,
        uint[] pixels,
        int declaredPixelCount,
        int zOrder = 0,
        byte flags = 0)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(version);
            writer.Write(name);
            writer.Write(width);
            writer.Write(height);
            if (writeTransformHeader)
            {
                writer.Write(pivotX);
                writer.Write(pivotY);
                writer.Write(offsetX);
                writer.Write(offsetY);
                writer.Write(zOrder);
                writer.Write(flags);
            }
            writer.Write(declaredPixelCount);
            foreach (uint pixel in pixels) writer.Write(pixel);
        }
    }
}
