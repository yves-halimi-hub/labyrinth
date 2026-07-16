using System;
using System.Threading;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Persistence
{
    public enum AutosaveState
    {
        Idle,
        Scheduled,
        Saving,
        Succeeded,
        Failed,
        Cancelled
    }

    public sealed class AutosaveSnapshot
    {
        public long RequestId { get; }
        public AutosaveState State { get; }
        public string ProjectName { get; }
        public string Path { get; }
        public DateTimeOffset ChangedAt { get; }
        public DateTimeOffset? LastSavedAt { get; }
        public Exception Exception { get; }

        internal AutosaveSnapshot(
            long requestId,
            AutosaveState state,
            string projectName,
            string path,
            DateTimeOffset changedAt,
            DateTimeOffset? lastSavedAt,
            Exception exception)
        {
            RequestId = requestId;
            State = state;
            ProjectName = projectName;
            Path = path;
            ChangedAt = changedAt;
            LastSavedAt = lastSavedAt;
            Exception = exception;
        }
    }

    public sealed class AutosaveController : IDisposable
    {
        private readonly object gate = new object();
        private readonly ProjectPersistenceService persistence;
        private readonly IDebounceScheduler scheduler;
        private readonly TimeSpan debounceDelay;
        private readonly SynchronizationContext eventContext;

        private CancellationTokenSource pendingCancellation;
        private Task pendingTask = Task.CompletedTask;
        private long requestId;
        private bool disposed;
        private DateTimeOffset? lastSavedAt;
        private AutosaveSnapshot current;

        public event Action<AutosaveSnapshot> StateChanged;

        public AutosaveSnapshot Current
        {
            get { lock (gate) return current; }
        }

        public AutosaveController(ProjectPersistenceService persistence)
            : this(
                persistence,
                new TaskDebounceScheduler(),
                TimeSpan.FromMilliseconds(Config.Persistence.DefaultAutosaveDebounceMilliseconds),
                SynchronizationContext.Current)
        {
        }

        public AutosaveController(
            ProjectPersistenceService persistence,
            IDebounceScheduler scheduler,
            TimeSpan debounceDelay,
            SynchronizationContext eventContext = null)
        {
            this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            if (debounceDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounceDelay));
            this.debounceDelay = debounceDelay;
            this.eventContext = eventContext;
            current = CreateSnapshot(AutosaveState.Idle, null, null, null);
        }

        public long Schedule(string projectName, EFYVProject project)
        {
            ThrowIfDisposed();
            if (project == null) throw new ArgumentNullException(nameof(project));
            return Schedule(projectName, () => ProjectPersistenceSnapshot.Capture(project));
        }

        public long Schedule(string projectName, Func<ProjectPersistenceSnapshot> snapshotFactory)
        {
            ThrowIfDisposed();
            if (snapshotFactory == null) throw new ArgumentNullException(nameof(snapshotFactory));
            return Schedule(projectName, snapshotFactory, debounceDelay);
        }

        public Task SaveNowAsync(string projectName, EFYVProject project)
        {
            ThrowIfDisposed();
            if (project == null) throw new ArgumentNullException(nameof(project));
            Schedule(projectName, () => ProjectPersistenceSnapshot.Capture(project), TimeSpan.Zero);
            lock (gate) return pendingTask;
        }

        public Task FlushAsync()
        {
            ThrowIfDisposed();
            lock (gate) return pendingTask;
        }

        public void Cancel()
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
            Transition(cancelledRequest, AutosaveState.Cancelled, current?.ProjectName, current?.Path, null);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Cancel();
        }

        private long Schedule(
            string projectName,
            Func<ProjectPersistenceSnapshot> snapshotFactory,
            TimeSpan delay)
        {
            string path = persistence.GetAutosavePath(projectName);
            var cancellation = new CancellationTokenSource();
            CancellationTokenSource previous;
            long scheduledRequest;
            lock (gate)
            {
                previous = pendingCancellation;
                pendingCancellation = cancellation;
                scheduledRequest = ++requestId;
                pendingTask = RunAsync(
                    scheduledRequest,
                    projectName,
                    path,
                    snapshotFactory,
                    delay,
                    cancellation);
            }

            previous?.Cancel();
            Transition(scheduledRequest, AutosaveState.Scheduled, projectName, path, null);
            return scheduledRequest;
        }

        private async Task RunAsync(
            long scheduledRequest,
            string projectName,
            string path,
            Func<ProjectPersistenceSnapshot> snapshotFactory,
            TimeSpan delay,
            CancellationTokenSource cancellationSource)
        {
            CancellationToken cancellationToken = cancellationSource.Token;
            try
            {
                await Task.Yield();
                await scheduler.Delay(delay, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrent(scheduledRequest)) return;

                ProjectPersistenceSnapshot snapshot = await CaptureAsync(snapshotFactory).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrent(scheduledRequest)) return;

                Transition(scheduledRequest, AutosaveState.Saving, projectName, path, null);
                await Task.Run(
                    () => persistence.SaveAutosave(projectName, snapshot, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                if (!IsCurrent(scheduledRequest)) return;
                lock (gate) lastSavedAt = scheduler.UtcNow;
                Transition(scheduledRequest, AutosaveState.Succeeded, projectName, path, null);
            }
            catch (OperationCanceledException)
            {
                if (IsCurrent(scheduledRequest))
                    Transition(scheduledRequest, AutosaveState.Cancelled, projectName, path, null);
            }
            catch (Exception exception)
            {
                if (IsCurrent(scheduledRequest))
                    Transition(scheduledRequest, AutosaveState.Failed, projectName, path, exception);
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

        private Task<ProjectPersistenceSnapshot> CaptureAsync(
            Func<ProjectPersistenceSnapshot> snapshotFactory)
        {
            if (eventContext == null) return Task.FromResult(snapshotFactory());

            var completion = new TaskCompletionSource<ProjectPersistenceSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            eventContext.Post(
                state =>
                {
                    try
                    {
                        completion.TrySetResult(((Func<ProjectPersistenceSnapshot>)state)());
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                },
                snapshotFactory);
            return completion.Task;
        }

        private bool IsCurrent(long scheduledRequest)
        {
            lock (gate) return scheduledRequest == requestId;
        }

        private void Transition(
            long id,
            AutosaveState state,
            string projectName,
            string path,
            Exception exception)
        {
            AutosaveSnapshot snapshot;
            lock (gate)
            {
                if (id != requestId) return;
                current = CreateSnapshot(state, projectName, path, exception);
                snapshot = current;
            }

            Action<AutosaveSnapshot> handler = StateChanged;
            if (handler == null) return;
            if (eventContext == null) handler(snapshot);
            else eventContext.Post(value => handler((AutosaveSnapshot)value), snapshot);
        }

        private AutosaveSnapshot CreateSnapshot(
            AutosaveState state,
            string projectName,
            string path,
            Exception exception)
        {
            return new AutosaveSnapshot(
                requestId,
                state,
                projectName,
                path,
                scheduler.UtcNow,
                lastSavedAt,
                exception);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(AutosaveController));
        }
    }
}
