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
            ThrowIfDisposed();
            if (projectAccessor == null) throw new ArgumentNullException(nameof(projectAccessor));
            lock (gate)
            {
                if (!isWatching) return requestId;
            }
            return Schedule(projectAccessor, debounceDelay);
        }

        public Task ExportNowAsync(EFYVProject project)
        {
            ThrowIfDisposed();
            if (project == null) throw new ArgumentNullException(nameof(project));
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
                ExportResult result = await Task.Run(
                    () => exportEngine.Export(capture.Snapshot, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                if (!IsCurrentRequest(scheduledRequest)) return;
                lock (gate) lastSyncedAt = scheduler.UtcNow;
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
                validation.IsValid ? ProjectSnapshot.Capture(project) : null);
        }

        private sealed class LiveDebugCapture
        {
            public ProjectValidationResult Validation { get; }
            public ProjectSnapshot Snapshot { get; }

            public LiveDebugCapture(ProjectValidationResult validation, ProjectSnapshot snapshot)
            {
                Validation = validation;
                Snapshot = snapshot;
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
