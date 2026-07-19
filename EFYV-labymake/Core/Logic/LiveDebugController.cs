using System;
using System.Threading;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public enum LiveDebugState
    {
        Stopped,
        Watching,
        Scheduled,
        Exporting,
        Succeeded,
        ValidationFailed,
        Failed,
        Cancelled
    }

    public sealed class LiveDebugSnapshot
    {
        public long RequestId { get; }
        public LiveDebugState State { get; }
        public bool IsWatching { get; }
        public bool IsPending { get; }
        public DateTimeOffset ChangedAt { get; }
        public DateTimeOffset? LastSyncedAt { get; }
        public ProjectValidationResult Validation { get; }
        public ExportResult Export { get; }
        public Exception Exception { get; }

        internal LiveDebugSnapshot(
            long requestId,
            LiveDebugState state,
            bool isWatching,
            DateTimeOffset changedAt,
            DateTimeOffset? lastSyncedAt,
            ProjectValidationResult validation,
            ExportResult export,
            Exception exception)
        {
            RequestId = requestId;
            State = state;
            IsWatching = isWatching;
            IsPending = state == LiveDebugState.Scheduled || state == LiveDebugState.Exporting;
            ChangedAt = changedAt;
            LastSyncedAt = lastSyncedAt;
            Validation = validation;
            Export = export;
            Exception = exception;
        }
    }

    public sealed class LiveDebugController : IDisposable
    {
        private readonly object gate = new object();
        private readonly ExportEngine exportEngine;
        private readonly ProjectValidator validator;
        private readonly IDebounceScheduler scheduler;
        private readonly SynchronizationContext eventContext;
        private readonly TimeSpan debounceDelay;

        private CancellationTokenSource pendingCancellation;
        private Task pendingTask = Task.CompletedTask;
        private long requestId;
        private bool isWatching;
        private bool isDisposed;
        private DateTimeOffset? lastSyncedAt;
        private LiveDebugSnapshot current;
        // Item #27: the union of the edit scopes reported since the last
        // successful publish. When it changed no exported pixels or atlas
        // layout, the next publish takes the metadata-only fast path (no PNG
        // re-encode). Accumulated across superseded debounce requests so a
        // pixel edit followed by a hitbox nudge inside one debounce window still
        // republishes the PNG. Untagged notifications contribute Everything.
        private DesignerDirtyScope pendingScope = DesignerDirtyScope.None;

        public event Action<LiveDebugSnapshot> StateChanged;

        public LiveDebugSnapshot Current
        {
            get { lock (gate) return current; }
        }

        public LiveDebugController(ExportEngine exportEngine, ProjectValidator validator)
            : this(
                exportEngine,
                validator,
                new TaskDebounceScheduler(),
                TimeSpan.FromMilliseconds(Config.LiveDebug.DefaultDebounceMilliseconds),
                SynchronizationContext.Current)
        {
        }

        public LiveDebugController(
            ExportEngine exportEngine,
            ProjectValidator validator,
            IDebounceScheduler scheduler,
            TimeSpan debounceDelay,
            SynchronizationContext eventContext = null)
        {
            if (exportEngine == null) throw new ArgumentNullException(nameof(exportEngine));
            if (validator == null) throw new ArgumentNullException(nameof(validator));
            if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));
            if (debounceDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounceDelay));

            this.exportEngine = exportEngine;
            this.validator = validator;
            this.scheduler = scheduler;
            this.debounceDelay = debounceDelay;
            this.eventContext = eventContext;
            current = CreateSnapshot(LiveDebugState.Stopped, null, null, null);
        }

        public void StartWatching()
        {
            ThrowIfDisposed();
            lock (gate) isWatching = true;
            Transition(LiveDebugState.Watching, null, null, null);
        }

        public void StopWatching(bool cancelPending = true)
        {
            ThrowIfDisposed();
            CancellationTokenSource cancellation;
            ProjectValidationResult validation;
            ExportResult export;
            Exception exception;
            long stoppedRequest;
            lock (gate)
            {
                isWatching = false;
                stoppedRequest = ++requestId;
                cancellation = pendingCancellation;
                pendingCancellation = null;
                validation = current?.Validation;
                export = current?.Export;
                exception = current?.Exception;
            }
            if (cancelPending) cancellation?.Cancel();
            Transition(stoppedRequest, LiveDebugState.Stopped, validation, export, exception);
        }

        public long NotifyProjectChanged(EFYVProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            return NotifyProjectChanged(() => project);
        }

        public long NotifyProjectChanged(Func<EFYVProject> projectAccessor)
        {
            // Untagged notifications keep the full-publish behavior (item #27):
            // the metadata-only fast path can only ever be reached via a scope
            // that a caller deliberately narrowed.
            return NotifyProjectChanged(projectAccessor, DesignerDirtyScope.Everything);
        }

        // Item #27: the scope is the union of what changed since the caller last
        // notified. It accumulates here across debounced/superseded requests
        // until a publish succeeds.
        public long NotifyProjectChanged(Func<EFYVProject> projectAccessor, DesignerDirtyScope scope)
        {
            ThrowIfDisposed();
            if (projectAccessor == null) throw new ArgumentNullException(nameof(projectAccessor));
            lock (gate)
            {
                if (!isWatching) return requestId;
                pendingScope |= scope;
            }
            return Schedule(projectAccessor, debounceDelay);
        }

        public Task ExportNowAsync(EFYVProject project)
        {
            ThrowIfDisposed();
            if (project == null) throw new ArgumentNullException(nameof(project));
            // An explicit "export now" always writes a full publish.
            lock (gate) pendingScope |= DesignerDirtyScope.Everything;
            Schedule(() => project, TimeSpan.Zero);
            lock (gate) return pendingTask;
        }

        public Task FlushAsync()
        {
            ThrowIfDisposed();
            lock (gate) return pendingTask;
        }

        public void CancelPending()
        {
            CancellationTokenSource cancellation;
            long cancelledRequest;
            lock (gate)
            {
                cancellation = pendingCancellation;
                if (cancellation == null) return;
                pendingCancellation = null;
                cancelledRequest = ++requestId;
            }
            cancellation.Cancel();
            Transition(cancelledRequest, LiveDebugState.Cancelled, current?.Validation, null, null);
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            CancelPending();
        }

        private long Schedule(Func<EFYVProject> projectAccessor, TimeSpan delay)
        {
            CancellationTokenSource previous;
            var currentCancellation = new CancellationTokenSource();
            long currentRequest;
            lock (gate)
            {
                previous = pendingCancellation;
                currentRequest = ++requestId;
                pendingCancellation = currentCancellation;
                pendingTask = RunExportAsync(
                    currentRequest,
                    projectAccessor,
                    delay,
                    currentCancellation);
            }

            previous?.Cancel();
            Transition(currentRequest, LiveDebugState.Scheduled, null, null, null);
            return currentRequest;
        }

        private async Task RunExportAsync(
            long scheduledRequest,
            Func<EFYVProject> projectAccessor,
            TimeSpan delay,
            CancellationTokenSource cancellationSource)
        {
            CancellationToken cancellationToken = cancellationSource.Token;
            try
            {
                await Task.Yield();
                await scheduler.Delay(delay, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentRequest(scheduledRequest)) return;

                LiveDebugCapture capture = await CaptureAsync(projectAccessor).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentRequest(scheduledRequest)) return;
                if (!capture.Validation.IsValid)
                {
                    Transition(
                        scheduledRequest,
                        LiveDebugState.ValidationFailed,
                        capture.Validation,
                        null,
                        null);
                    return;
                }

                Transition(scheduledRequest, LiveDebugState.Exporting, capture.Validation, null, null);
                // Item #27: capture the accumulated scope as late as possible
                // (a newer notification arriving after this supersedes the
                // request, so it never reaches Succeeded with a stale scope) and
                // take the metadata-only publish path when no exported pixels or
                // atlas layout changed.
                DesignerDirtyScope capturedScope;
                lock (gate) capturedScope = pendingScope;
                bool metadataOnly = capturedScope.IsMetadataOnly();
                // Item #33: a directional capture carries one snapshot per
                // facing (active facing last); the reported result is the
                // final - active - facing's publish.
                ExportResult result = await Task.Run(
                    () =>
                    {
                        ExportResult lastResult = null;
                        foreach (ProjectSnapshot snapshot in capture.Snapshots)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            lastResult = exportEngine.Export(snapshot, cancellationToken, metadataOnly);
                        }
                        return lastResult;
                    },
                    cancellationToken).ConfigureAwait(false);

                if (!IsCurrentRequest(scheduledRequest)) return;
                // Clear only the bits this publish covered; anything reported
                // after the capture above belongs to the next batch.
                lock (gate)
                {
                    lastSyncedAt = scheduler.UtcNow;
                    pendingScope &= ~capturedScope;
                }
                Transition(scheduledRequest, LiveDebugState.Succeeded, capture.Validation, result, null);
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentRequest(scheduledRequest))
                    Transition(scheduledRequest, LiveDebugState.Cancelled, null, null, null);
            }
            catch (Exception exception)
            {
                if (IsCurrentRequest(scheduledRequest))
                    Transition(scheduledRequest, LiveDebugState.Failed, null, null, exception);
            }
            finally
            {
                lock (gate)
                {
                    if (scheduledRequest == requestId && ReferenceEquals(pendingCancellation, cancellationSource))
                        pendingCancellation = null;
                }
                cancellationSource.Dispose();
            }
        }

        private Task<LiveDebugCapture> CaptureAsync(Func<EFYVProject> projectAccessor)
        {
            if (eventContext == null) return Task.FromResult(Capture(projectAccessor));

            var completion = new TaskCompletionSource<LiveDebugCapture>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            eventContext.Post(
                state =>
                {
                    try { completion.TrySetResult(Capture((Func<EFYVProject>)state)); }
                    catch (Exception exception) { completion.TrySetException(exception); }
                },
                projectAccessor);
            return completion.Task;
        }

        private LiveDebugCapture Capture(Func<EFYVProject> projectAccessor)
        {
            EFYVProject project = projectAccessor();
            ProjectValidationResult validation = validator.Validate(project, ProjectValidationScope.Export);
            return new LiveDebugCapture(
                validation,
                validation.IsValid ? CaptureSnapshots(project) : null);
        }

        // One snapshot for a plain project; one per facing (inactive facings
        // in catalog order, the ACTIVE facing last) for a directional project,
        // so a single live push refreshes all four suffixed pairs (item #33).
        private static ProjectSnapshot[] CaptureSnapshots(EFYVProject project)
        {
            if (project.Directional == null)
                return new[] { ProjectSnapshot.Capture(project) };

            var snapshots = new ProjectSnapshot[Config.Entity.DirectionalVariantCount];
            int index = Config.Common.FirstIndex;
            string activeFacing = project.Directional.ActiveFacing;
            foreach (string facing in Config.Schema.FacingChoices)
            {
                if (string.Equals(facing, activeFacing, StringComparison.Ordinal)) continue;
                snapshots[index++] = ProjectSnapshot.CaptureFacing(project, facing);
            }
            snapshots[index] = ProjectSnapshot.CaptureFacing(project, activeFacing);
            return snapshots;
        }

        private sealed class LiveDebugCapture
        {
            public ProjectValidationResult Validation { get; }
            public ProjectSnapshot[] Snapshots { get; }

            public LiveDebugCapture(ProjectValidationResult validation, ProjectSnapshot[] snapshots)
            {
                Validation = validation;
                Snapshots = snapshots;
            }
        }

        private bool IsCurrentRequest(long scheduledRequest)
        {
            lock (gate) return scheduledRequest == requestId;
        }

        private void Transition(
            LiveDebugState state,
            ProjectValidationResult validation,
            ExportResult export,
            Exception exception)
        {
            long id;
            lock (gate) id = requestId;
            Transition(id, state, validation, export, exception);
        }

        private void Transition(
            long id,
            LiveDebugState state,
            ProjectValidationResult validation,
            ExportResult export,
            Exception exception)
        {
            LiveDebugSnapshot snapshot;
            lock (gate)
            {
                if (id != requestId) return;
                current = CreateSnapshot(state, validation, export, exception);
                snapshot = current;
            }
            Publish(snapshot);
        }

        private LiveDebugSnapshot CreateSnapshot(
            LiveDebugState state,
            ProjectValidationResult validation,
            ExportResult export,
            Exception exception)
        {
            return new LiveDebugSnapshot(
                requestId,
                state,
                isWatching,
                scheduler.UtcNow,
                lastSyncedAt,
                validation,
                export,
                exception);
        }

        private void Publish(LiveDebugSnapshot snapshot)
        {
            Action<LiveDebugSnapshot> handler = StateChanged;
            if (handler == null) return;
            if (eventContext == null) handler(snapshot);
            else eventContext.Post(state => handler((LiveDebugSnapshot)state), snapshot);
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(LiveDebugController));
        }
    }
}
