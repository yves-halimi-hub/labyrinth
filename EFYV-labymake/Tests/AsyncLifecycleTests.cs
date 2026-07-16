using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Logic;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

internal static partial class Program
{
    private static async Task TestLiveDebugAdversarialStateMachine()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            var schema = new AssetSchemaService();
            var validator = new ProjectValidator(schema);
            var engine = new ExportEngine(validator);
            var scheduler = new ManualScheduler();
            EFYVProject project = CreateValidProject(root, 1);

            RequireThrows<ArgumentNullException>(() =>
                new LiveDebugController(null, validator, scheduler, TimeSpan.Zero));
            RequireThrows<ArgumentNullException>(() =>
                new LiveDebugController(engine, null, scheduler, TimeSpan.Zero));
            RequireThrows<ArgumentNullException>(() =>
                new LiveDebugController(engine, validator, null, TimeSpan.Zero));
            RequireThrows<ArgumentOutOfRangeException>(() =>
                new LiveDebugController(engine, validator, scheduler, TimeSpan.FromTicks(-1)));

            var live = new LiveDebugController(
                engine,
                validator,
                scheduler,
                TimeSpan.FromSeconds(1),
                null);
            int eventCount = 0;
            live.StateChanged += snapshot =>
            {
                Interlocked.Increment(ref eventCount);
                Require(snapshot.RequestId >= 0);
            };

            long stoppedId = live.NotifyProjectChanged(project);
            Require(stoppedId == 0 && live.Current.State == LiveDebugState.Stopped);
            live.StartWatching();
            Require(live.Current.State == LiveDebugState.Watching && live.Current.IsWatching);

            int staleCaptures = 0;
            int latestCaptures = 0;
            long first = live.NotifyProjectChanged(() =>
            {
                Interlocked.Increment(ref staleCaptures);
                return project;
            });
            long second = live.NotifyProjectChanged(() =>
            {
                Interlocked.Increment(ref latestCaptures);
                return project;
            });
            Require(second > first && live.Current.State == LiveDebugState.Scheduled);
            AsyncReleaseWhenPending(scheduler, 2);
            await live.FlushAsync();
            Require(staleCaptures == 0 && latestCaptures == 1);
            Require(live.Current.State == LiveDebugState.Succeeded);
            Require(live.Current.Export != null && live.Current.LastSyncedAt.HasValue);

            EFYVProject invalid = CreateValidProject(root, 1);
            invalid.Animations.Clear();
            live.NotifyProjectChanged(invalid);
            AsyncReleaseWhenPending(scheduler, 1);
            await live.FlushAsync();
            Require(live.Current.State == LiveDebugState.ValidationFailed);
            Require(live.Current.Validation != null && !live.Current.Validation.IsValid);
            Require(live.Current.Export == null && live.Current.Exception == null);

            live.NotifyProjectChanged(() => throw new InvalidOperationException("capture failed"));
            AsyncReleaseWhenPending(scheduler, 1);
            await live.FlushAsync();
            Require(live.Current.State == LiveDebugState.Failed);
            Require(live.Current.Exception is InvalidOperationException);

            live.NotifyProjectChanged(project);
            AsyncWaitForPending(scheduler, 1);
            live.CancelPending();
            scheduler.ReleaseAll();
            await live.FlushAsync();
            Require(live.Current.State == LiveDebugState.Cancelled && !live.Current.IsPending);

            live.NotifyProjectChanged(project);
            AsyncWaitForPending(scheduler, 1);
            live.StopWatching(false);
            scheduler.ReleaseAll();
            await live.FlushAsync();
            Require(live.Current.State == LiveDebugState.Stopped && !live.Current.IsWatching);

            await live.ExportNowAsync(project);
            Require(live.Current.State == LiveDebugState.Succeeded && !live.Current.IsWatching);
            Require(eventCount >= 10);

            live.Dispose();
            live.Dispose();
            RequireThrows<ObjectDisposedException>(() => live.StartWatching());
            RequireThrows<ObjectDisposedException>(() => live.NotifyProjectChanged(project));
            RequireThrows<ObjectDisposedException>(() => live.ExportNowAsync(project));
            RequireThrows<ObjectDisposedException>(() => live.FlushAsync());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task TestAutosaveAdversarialStateMachine()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
            var scheduler = new ManualScheduler();
            EFYVProject project = CreateValidProject(root, 1);

            RequireThrows<ArgumentNullException>(() =>
                new AutosaveController(null, scheduler, TimeSpan.Zero));
            RequireThrows<ArgumentNullException>(() =>
                new AutosaveController(persistence, null, TimeSpan.Zero));
            RequireThrows<ArgumentOutOfRangeException>(() =>
                new AutosaveController(persistence, scheduler, TimeSpan.FromTicks(-1)));

            var autosave = new AutosaveController(
                persistence,
                scheduler,
                TimeSpan.FromSeconds(1),
                null);
            int events = 0;
            autosave.StateChanged += snapshot =>
            {
                Interlocked.Increment(ref events);
                Require(snapshot.RequestId >= 0);
            };

            int staleCaptures = 0;
            int latestCaptures = 0;
            long first = autosave.Schedule("Coalesce", () =>
            {
                Interlocked.Increment(ref staleCaptures);
                return ProjectPersistenceSnapshot.Capture(project);
            });
            long second = autosave.Schedule("Coalesce", () =>
            {
                Interlocked.Increment(ref latestCaptures);
                return ProjectPersistenceSnapshot.Capture(project);
            });
            Require(second > first && autosave.Current.State == AutosaveState.Scheduled);
            AsyncReleaseWhenPending(scheduler, 2);
            await autosave.FlushAsync();
            Require(staleCaptures == 0 && latestCaptures == 1);
            Require(autosave.Current.State == AutosaveState.Succeeded);
            Require(autosave.Current.LastSavedAt.HasValue && persistence.AutosaveExists("Coalesce"));

            autosave.Schedule("FactoryFailure", () => throw new InvalidDataException("capture failed"));
            AsyncReleaseWhenPending(scheduler, 1);
            await autosave.FlushAsync();
            Require(autosave.Current.State == AutosaveState.Failed);
            Require(autosave.Current.Exception is InvalidDataException);
            Require(!persistence.AutosaveExists("FactoryFailure"));

            autosave.Schedule("Cancelled", project);
            AsyncWaitForPending(scheduler, 1);
            autosave.Cancel();
            scheduler.ReleaseAll();
            await autosave.FlushAsync();
            Require(autosave.Current.State == AutosaveState.Cancelled);
            Require(!persistence.AutosaveExists("Cancelled"));

            await autosave.SaveNowAsync("Immediate", project);
            Require(autosave.Current.State == AutosaveState.Succeeded);
            Require(persistence.AutosaveExists("Immediate"));
            Require(events >= 8);

            RequireThrows<ArgumentException>(() => autosave.Schedule("../escape", project));
            RequireThrows<ArgumentNullException>(() => autosave.Schedule("Null", (EFYVProject)null));
            RequireThrows<ArgumentNullException>(() =>
                autosave.Schedule("Null", (Func<ProjectPersistenceSnapshot>)null));
            RequireThrows<ArgumentNullException>(() => autosave.SaveNowAsync("Null", null));

            autosave.Dispose();
            autosave.Dispose();
            RequireThrows<ObjectDisposedException>(() => autosave.Schedule("Disposed", project));
            RequireThrows<ObjectDisposedException>(() => autosave.SaveNowAsync("Disposed", project));
            RequireThrows<ObjectDisposedException>(() => autosave.FlushAsync());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task TestDesignerSessionSaveReloadLifecycle()
    {
        string root = NewTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Config.Export.DirAssets));
            EFYVProject project = CreateValidProject(root, 1);
            var session = DesignerSession.Create("Lifecycle", project, root);
            session.AutosaveEnabled = false;
            try
            {
                session.ActiveTool = new PencilTool { CurrentColor = Color(1, 2, 3, 255) };
                Require(session.PointerDown(2, 2));
                Require(session.PointerUp(2, 2));
                Require(session.Current.IsDirty && session.Current.ChangeVersion == 1);
                await session.SaveAsync();
                Require(!session.Current.IsDirty && session.Current.LastSavedAt.HasValue);
                Require(File.Exists(Path.Combine(root, "Lifecycle" + Config.Persistence.ProjectExtension)));

                session.ActiveTool = new PencilTool { CurrentColor = Color(9, 8, 7, 255) };
                Require(session.PointerDown(2, 2));
                Require(session.PointerUp(2, 2));
                Require(session.Project.Animations[0].Frames[0].Layers[0].GetPixel(2, 2).R == 9);
                session.ReloadFromDisk(false);
                Require(session.Project.Animations[0].Frames[0].Layers[0].GetPixel(2, 2).R == 1);
                Require(!session.Current.IsDirty && session.Current.ChangeVersion == 0);
                Require(!session.History.CanUndo && !session.History.CanRedo);

                var persistence = new ProjectPersistenceService(root, new AssetSchemaService());
                EFYVProject autosaveProject = CreateValidProject(root, 1);
                autosaveProject.Animations[0].Frames[0].Layers[0].SetPixel(2, 2, Color(44, 55, 66, 255));
                persistence.SaveAutosave("Lifecycle", autosaveProject, CancellationToken.None);
                session.ReloadFromDisk(true);
                Require(session.Project.Animations[0].Frames[0].Layers[0].GetPixel(2, 2).R == 44);
                Require(!session.Current.IsDirty && session.Current.Validation.IsValid);

                session.ActiveTool = new PencilTool { CurrentColor = Color(77, 0, 0, 255) };
                Require(session.PointerDown(1, 1));
                Require(session.PointerUp(1, 1));
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    bool cancelled = false;
                    try
                    {
                        await session.SaveAsync(cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    Require(cancelled);
                }
                Require(session.Current.IsDirty);
                Require(session.Current.PersistenceException is OperationCanceledException);
            }
            finally
            {
                session.Dispose();
            }

            RequireThrows<ObjectDisposedException>(() => session.SelectFrame(0, 0));
            RequireThrows<ObjectDisposedException>(() => session.Undo());
            RequireThrows<ObjectDisposedException>(() => session.ReloadFromDisk(false));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void AsyncWaitForPending(ManualScheduler scheduler, int minimum)
    {
        Require(SpinWait.SpinUntil(() => scheduler.PendingCount >= minimum, 2000));
    }

    private static void AsyncReleaseWhenPending(ManualScheduler scheduler, int minimum)
    {
        AsyncWaitForPending(scheduler, minimum);
        scheduler.ReleaseAll();
    }
}
