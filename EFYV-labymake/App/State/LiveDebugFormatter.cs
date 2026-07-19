using System.Globalization;
using EFYVLabyMake.Core.Logic;

namespace EFYVLabyMake.App.State
{
    // Pure, UI-framework-free formatting of a LiveDebugSnapshot for the
    // live-debug panel (item #5). The watching flag is surfaced explicitly so
    // the "OFF by default" state reads unambiguously in the UI.
    public static class LiveDebugFormatter
    {
        public static string FormatStatus(LiveDebugSnapshot snapshot)
        {
            if (snapshot == null) return "Live debug: off";
            string watch = snapshot.IsWatching ? "watch ON" : "watch OFF";
            return "Live debug: " + watch + " — " + DescribeState(snapshot);
        }

        // Short synced/last-sync trailer for a secondary status line; empty when
        // the loop has never completed an export.
        public static string FormatLastSync(LiveDebugSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.LastSyncedAt.HasValue) return "";
            return "Last push " +
                snapshot.LastSyncedAt.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string DescribeState(LiveDebugSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case LiveDebugState.Stopped: return "stopped";
                case LiveDebugState.Watching: return "watching for changes";
                case LiveDebugState.Scheduled: return "change queued…";
                case LiveDebugState.Exporting: return "pushing to game…";
                case LiveDebugState.Succeeded: return "pushed to game";
                case LiveDebugState.ValidationFailed: return "blocked: " + CountIssues(snapshot) + " problem(s)";
                case LiveDebugState.Failed:
                    return "failed: " + (snapshot.Exception?.Message ?? "unknown error");
                case LiveDebugState.Cancelled: return "cancelled";
                default: return snapshot.State.ToString();
            }
        }

        private static string CountIssues(LiveDebugSnapshot snapshot)
        {
            int count = 0;
            if (snapshot.Validation != null)
            {
                foreach (ProjectIssue issue in snapshot.Validation.Issues)
                {
                    if (issue.Severity == ProjectIssueSeverity.Error) count++;
                }
            }
            return count.ToString(CultureInfo.InvariantCulture);
        }
    }
}
