// Item #3.8 (editor panels batch): closes the batch-2 "App State layer tests"
// deferral by covering the App's UI-framework-free view-state helpers that the
// panels are built on - the ScreenPixelConverter swizzle/blend mapping (checked
// against a backend ScaleBlitNearestNeighbor blit of the same canvas), the
// inspector PropertyFieldEditor, the problems ProblemFormatter, and the live
// debug / preview status formatters - plus the one sanctioned Core addition,
// ProjectPersistenceService.ListProjects, against an adversarial directory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EFYVLabyMake.App.State;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

internal static partial class Program
{
    private static void TestAppStateScreenPixelConverterAndBlit()
    {
        // Straight RGBA (red in the low byte) -> opaque BGRA swizzle.
        uint rgba = 0x78563412u; // R=0x12 G=0x34 B=0x56 A=0x78
        Require(ScreenPixelConverter.SwizzleOpaque(rgba) == 0xFF123456u);

        // ConvertToBgra: opaque pixel swizzles, fully transparent maps to the
        // workspace color, partial alpha blends (opaque result, distinct from
        // both endpoints).
        const uint workspace = 0xFF202020u;
        uint[] source =
        {
            0xFF0000FFu, // opaque red   (R=0xFF)
            0x00123456u, // transparent
            0x80FF0000u  // half-alpha blue (B=0xFF, A=0x80)
        };
        var destination = new uint[source.Length];
        ScreenPixelConverter.ConvertToBgra(source, destination, source.Length, 1, workspace);
        Require(destination[0] == ScreenPixelConverter.SwizzleOpaque(source[0]));
        Require(destination[1] == workspace);
        Require((destination[2] >> 24) == 0xFFu);            // forced opaque
        Require(destination[2] != workspace);
        Require(destination[2] != ScreenPixelConverter.SwizzleOpaque(source[2]));

        // A 1:1 nearest-neighbor blit preserves the raw RGBA values, so
        // converting the blitted screen buffer must equal converting the source
        // directly - the two viewport stages compose (blit then swizzle).
        const int width = 4;
        const int height = 3;
        var canvas = new uint[width * height];
        for (int index = 0; index < canvas.Length; index++)
            canvas[index] = 0xFF000000u | (uint)((index * 37) & 0x00FFFFFF);
        var blitted = new uint[width * height];
        unsafe
        {
            fixed (uint* sourcePointer = canvas)
            fixed (uint* destinationPointer = blitted)
            {
                EFYVBackend.Core.Memory.FastMemory.ScaleBlitNearestNeighbor(
                    sourcePointer, width, height,
                    destinationPointer, width, height,
                    1f, 0, 0);
            }
        }
        var blittedBgra = new uint[width * height];
        var directBgra = new uint[width * height];
        ScreenPixelConverter.ConvertToBgra(blitted, blittedBgra, width, height, workspace);
        ScreenPixelConverter.ConvertToBgra(canvas, directBgra, width, height, workspace);
        for (int index = 0; index < blittedBgra.Length; index++)
        {
            Require(blittedBgra[index] == directBgra[index]);
            Require(blittedBgra[index] == ScreenPixelConverter.SwizzleOpaque(canvas[index]));
        }
    }

    private static void TestAppStatePropertyFieldEditor()
    {
        Require(PropertyFieldEditor.FormatValue(3.5f, SchemaValueKind.Float) == "3.5");
        Require(PropertyFieldEditor.FormatValue(5, SchemaValueKind.Integer) == "5");
        Require(PropertyFieldEditor.FormatValue("hi", SchemaValueKind.Text) == "hi");
        Require(PropertyFieldEditor.FormatValue(null, SchemaValueKind.Text) == "");

        object value;
        Require(PropertyFieldEditor.TryParse("2.5", SchemaValueKind.Float, out value) &&
            value is float && (float)value == 2.5f);
        Require(!PropertyFieldEditor.TryParse("abc", SchemaValueKind.Float, out value));
        Require(!PropertyFieldEditor.TryParse("NaN", SchemaValueKind.Float, out value));
        Require(PropertyFieldEditor.TryParse("7", SchemaValueKind.Integer, out value) &&
            value is int && (int)value == 7);
        Require(!PropertyFieldEditor.TryParse("7.5", SchemaValueKind.Integer, out value));
        Require(PropertyFieldEditor.TryParse("", SchemaValueKind.Text, out value) &&
            (string)value == "");

        Require(PropertyFieldEditor.DescribeStatus(PropertyEditStatus.Success, SchemaValueKind.Text) == "");
        Require(PropertyFieldEditor.DescribeStatus(
            PropertyEditStatus.OutOfRange, SchemaValueKind.Float).Length > 0);
        Require(PropertyFieldEditor.DescribeStatus(
            PropertyEditStatus.ReadOnly, SchemaValueKind.Text).Length > 0);
        Require(PropertyFieldEditor.DescribeStatus(
            PropertyEditStatus.InvalidValue, SchemaValueKind.Integer)
            .IndexOf("whole-number", StringComparison.Ordinal) >= 0);
    }

    private static void TestAppStateProblemFormatter()
    {
        Require(ProblemFormatter.FormatSeverity(ProjectIssueSeverity.Error) == "Error");
        Require(ProblemFormatter.FormatSeverity(ProjectIssueSeverity.Warning) == "Warning");

        var located = new ProjectIssue(
            ProjectIssueSeverity.Error,
            ProjectIssueCode.MissingProperty,
            "hp",
            SchemaValueKind.Float,
            animationIndex: 2,
            frameIndex: 3,
            layerIndex: 1);
        Require(ProblemFormatter.HasFocusLocation(located));
        Require(ProblemFormatter.FormatLocation(located) == "Anim 3 · Frame 4 · Layer 2");
        Require(ProblemFormatter.Describe(located).IndexOf("(hp)", StringComparison.Ordinal) >= 0);
        Require(ProblemFormatter.FormatLine(located).IndexOf(
            "Anim 3", StringComparison.Ordinal) >= 0);

        var locationless = new ProjectIssue(
            ProjectIssueSeverity.Warning,
            ProjectIssueCode.UnknownProperty,
            "mystery");
        Require(!ProblemFormatter.HasFocusLocation(locationless));
        Require(ProblemFormatter.FormatLocation(locationless) == "");
        Require(ProblemFormatter.FormatLine(locationless) == ProblemFormatter.Describe(locationless));
    }

    private static void TestAppStateLiveDebugFormatter()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var stopped = new LiveDebugSnapshot(
            1, LiveDebugState.Stopped, false, now, null, null, null, null);
        string stoppedText = LiveDebugFormatter.FormatStatus(stopped);
        Require(stoppedText.IndexOf("watch OFF", StringComparison.Ordinal) >= 0);
        Require(stoppedText.IndexOf("stopped", StringComparison.Ordinal) >= 0);
        Require(LiveDebugFormatter.FormatLastSync(stopped) == "");

        var watching = new LiveDebugSnapshot(
            2, LiveDebugState.Watching, true, now, now, null, null, null);
        Require(LiveDebugFormatter.FormatStatus(watching).IndexOf(
            "watch ON", StringComparison.Ordinal) >= 0);
        Require(LiveDebugFormatter.FormatLastSync(watching).IndexOf(
            "Last push", StringComparison.Ordinal) >= 0);

        var twoErrors = new ProjectValidationResult(new List<ProjectIssue>
        {
            new ProjectIssue(ProjectIssueSeverity.Error, ProjectIssueCode.EmptyIdentityName),
            new ProjectIssue(ProjectIssueSeverity.Error, ProjectIssueCode.MissingAnimations)
        });
        var blocked = new LiveDebugSnapshot(
            3, LiveDebugState.ValidationFailed, true, now, null, twoErrors, null, null);
        string blockedText = LiveDebugFormatter.FormatStatus(blocked);
        Require(blockedText.IndexOf("blocked", StringComparison.Ordinal) >= 0);
        Require(blockedText.IndexOf("2", StringComparison.Ordinal) >= 0);

        var failed = new LiveDebugSnapshot(
            4, LiveDebugState.Failed, true, now, null, null, null, new InvalidOperationException("boom"));
        Require(LiveDebugFormatter.FormatStatus(failed).IndexOf("boom", StringComparison.Ordinal) >= 0);
        Require(LiveDebugFormatter.FormatStatus(null).Length > 0);
    }

    private static void TestAppStatePreviewStatusFormatter()
    {
        var empty = new PreviewStateSnapshot(
            PreviewPlaybackState.Empty, 0, 0, 0, 0, false, false, 0, 0, 0);
        Require(PreviewStatusFormatter.FormatStatus(empty) == "No frames to preview");

        var stopped = new PreviewStateSnapshot(
            PreviewPlaybackState.Stopped, 0, 0, 3, 12, true, false, 0, 2, 0);
        string stoppedText = PreviewStatusFormatter.FormatStatus(stopped);
        Require(stoppedText.IndexOf("Stopped", StringComparison.Ordinal) >= 0);
        Require(stoppedText.IndexOf("Frame 1/3", StringComparison.Ordinal) >= 0);
        Require(stoppedText.IndexOf("12 fps", StringComparison.Ordinal) >= 0);
        Require(stoppedText.IndexOf("loop", StringComparison.Ordinal) >= 0);

        var playingPingPong = new PreviewStateSnapshot(
            PreviewPlaybackState.Playing, 0, 1, 4, 24, true, true, 0, 3, 0);
        string playingText = PreviewStatusFormatter.FormatStatus(playingPingPong);
        Require(playingText.IndexOf("Playing", StringComparison.Ordinal) >= 0);
        Require(playingText.IndexOf("ping-pong", StringComparison.Ordinal) >= 0);
    }

    private static void TestPersistenceListProjectsAndAdversarialDirs()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var schema = new EFYVLabyMake.Core.Logic.AssetSchemaService();
            var persistence = new ProjectPersistenceService(root, schema);

            // Committed projects (valid documents through the save gate).
            persistence.SaveProject("Alpha", CreateValidProject(root, 1), CancellationToken.None);
            persistence.SaveProject("beta", CreateValidProject(root, 1), CancellationToken.None);
            persistence.SaveProject("Gamma", CreateValidProject(root, 2), CancellationToken.None);
            // A safe stem that itself contains ".autosave" - it must be LISTED
            // (its own sidecar is what the autosave guard must exclude).
            persistence.SaveProject("Weird.autosave", CreateValidProject(root, 1), CancellationToken.None);

            // Adversarial siblings that must all be skipped.
            persistence.SaveAutosave("Alpha", CreateValidProject(root, 1), CancellationToken.None);
            File.WriteAllText(Path.Combine(root, "notes.txt"), "not a project");
            File.WriteAllText(Path.Combine(root, Config.Persistence.ProjectExtension), "empty stem");
            File.WriteAllText(
                Path.Combine(root, new string('x', 140) + Config.Persistence.ProjectExtension),
                "oversized stem");
            string subdirectory = Path.Combine(root, "sub");
            Directory.CreateDirectory(subdirectory);
            File.WriteAllText(
                Path.Combine(subdirectory, "Inside" + Config.Persistence.ProjectExtension),
                "nested, not recursed");

            IReadOnlyList<ProjectListEntry> listed = persistence.ListProjects();
            var names = new List<string>();
            foreach (ProjectListEntry entry in listed) names.Add(entry.Name);

            Require(names.Count == 4);
            Require(names[0] == "Alpha");            // sorted ordinal-ignore-case
            Require(names[1] == "beta");
            Require(names[2] == "Gamma");
            Require(names[3] == "Weird.autosave");

            // Every listed name round-trips through the per-name path gate, and
            // carries a real (non-default) timestamp.
            foreach (ProjectListEntry entry in listed)
            {
                Require(persistence.GetProjectPath(entry.Name).Length > 0);
                Require(entry.LastWriteUtc > DateTime.MinValue);
            }

            // A fresh empty directory lists as empty; a deleted directory lists
            // as empty rather than throwing.
            string emptyRoot = NewTemporaryDirectory();
            try
            {
                Require(new ProjectPersistenceService(emptyRoot, schema).ListProjects().Count == 0);
            }
            finally
            {
                DeleteDirectory(emptyRoot);
            }

            var ephemeral = new ProjectPersistenceService(NewTemporaryDirectory(), schema);
            DeleteDirectory(ephemeral.ProjectDirectory);
            Require(ephemeral.ListProjects().Count == 0);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
}
