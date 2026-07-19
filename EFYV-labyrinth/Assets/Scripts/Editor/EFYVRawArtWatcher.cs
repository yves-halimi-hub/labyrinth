using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;
using LabyMakeConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYV.Editor
{
    // Live transport reliability (#12): a debounced EditorApplication.update
    // poller over Assets/RawArt. Without it, LabyMake publishes only become
    // visible when Unity regains window focus - the "live" loop required
    // alt-tabbing after every stroke. A poller (rather than FileSystemWatcher)
    // keeps all work on the editor main thread, so it is safe in Play Mode.
    [InitializeOnLoad]
    internal static class EFYVRawArtWatcher
    {
        private static readonly RawArtChangeTracker Tracker =
            new RawArtChangeTracker(GameConfig.RawArtWatcher.DebounceSeconds);
        private static readonly List<string> PendingImports = new List<string>();
        private static double nextPollTime;

        // Overridable for headless tests; production always watches Assets/RawArt.
        internal static string WatchRoot = Path.Combine(
            LabyMakeConfig.Export.DirAssets,
            LabyMakeConfig.Export.DirRawArt);

        static EFYVRawArtWatcher()
        {
            EditorApplication.update += Poll;
        }

        internal static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPollTime) return;
            nextPollTime = now + GameConfig.RawArtWatcher.PollIntervalSeconds;
            if (!Directory.Exists(WatchRoot)) return;

            PendingImports.Clear();
            if (!Tracker.Update(now, SnapshotWatchedFiles(), PendingImports)) return;

            // Works during Play Mode as well: importing refreshed art is exactly
            // the point of the live loop.
            foreach (string path in PendingImports)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.Default);
            }
            Debug.Log(string.Format(
                GameConfig.RawArtWatcher.LogImported,
                PendingImports.Count,
                WatchRoot));
        }

        private static IEnumerable<(string Path, long Stamp)> SnapshotWatchedFiles()
        {
            foreach (string path in Directory.GetFiles(WatchRoot))
            {
                if (!path.EndsWith(GameConfig.Importer.ExtensionEFYV, StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(GameConfig.Importer.ExtensionPNG, StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return (path, File.GetLastWriteTimeUtc(path).Ticks);
            }
        }
    }

    // Pure debounce/coalescing state machine - no Unity or filesystem calls, so
    // the contract is fully unit-testable headlessly:
    // - the first snapshot only records a baseline (Unity already imported
    //   whatever was on disk when the editor started);
    // - a new or re-stamped file marks the watch dirty and (re)opens the quiet
    //   window; repeated churn keeps pushing the window out (trailing debounce);
    // - deleted files leave the pending set (nothing to import);
    // - once the window has been quiet for debounceSeconds, all pending paths
    //   are emitted once, sorted, and the tracker returns to idle.
    internal sealed class RawArtChangeTracker
    {
        private readonly double debounceSeconds;
        private readonly Dictionary<string, long> knownStamps =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> removedPaths = new List<string>();
        private double lastChangeTime;
        private bool baselined;

        public RawArtChangeTracker(double debounceSeconds)
        {
            if (double.IsNaN(debounceSeconds) || debounceSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(debounceSeconds));
            this.debounceSeconds = debounceSeconds;
        }

        public int PendingCount => pending.Count;

        public bool Update(
            double nowSeconds,
            IEnumerable<(string Path, long Stamp)> files,
            List<string> changedPaths)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (changedPaths == null) throw new ArgumentNullException(nameof(changedPaths));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string path, long stamp) in files)
            {
                if (path == null) continue;
                seen.Add(path);
                if (knownStamps.TryGetValue(path, out long knownStamp) && knownStamp == stamp) continue;

                knownStamps[path] = stamp;
                if (!baselined) continue;
                pending.Add(path);
                lastChangeTime = nowSeconds;
            }

            removedPaths.Clear();
            foreach (KeyValuePair<string, long> known in knownStamps)
            {
                if (!seen.Contains(known.Key)) removedPaths.Add(known.Key);
            }
            foreach (string removed in removedPaths)
            {
                knownStamps.Remove(removed);
                pending.Remove(removed);
            }

            if (!baselined)
            {
                baselined = true;
                return false;
            }

            if (pending.Count == 0 || nowSeconds - lastChangeTime < debounceSeconds) return false;

            changedPaths.AddRange(pending);
            changedPaths.Sort(StringComparer.OrdinalIgnoreCase);
            pending.Clear();
            return true;
        }
    }
}
