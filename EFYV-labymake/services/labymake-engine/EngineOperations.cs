using System.IO;
using EFYV.Labyrinth.Artifacts;

namespace Labymake.Engine;

public sealed record EngineRuntimeOptions(bool ContractTestMode)
{
    public const string ContractTestModeEnvironmentVariable = "EFYV_CONTRACT_TEST_MODE";

    public static EngineRuntimeOptions FromEnvironment() => new(
        string.Equals(
            Environment.GetEnvironmentVariable(ContractTestModeEnvironmentVariable),
            "1",
            StringComparison.Ordinal));
}

public static class EngineOperations
{
    public static bool TryParse(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken,
        out LabyMakeSnapshot? snapshot,
        out string diagnostic)
    {
        try
        {
            snapshot = LabyMakeSnapshotParser.Parse(source, cancellationToken);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException or OverflowException or ArgumentException)
        {
            snapshot = null;
            diagnostic = exception.Message[..Math.Min(exception.Message.Length, 1000)];
            return false;
        }
    }

    public static Task<LabyMakeArtifactBundle> ExportAsync(LabyMakeSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.Run(() => LabyMakeArtifactExporter.Export(snapshot, cancellationToken), cancellationToken);
}

public sealed class WorkAdmission
{
    private readonly SemaphoreSlim _slots;
    private readonly int _maxQueued;
    private int _queued;

    public WorkAdmission(int concurrency, int maxQueued)
    {
        if (concurrency <= 0) throw new ArgumentOutOfRangeException(nameof(concurrency));
        if (maxQueued < 0) throw new ArgumentOutOfRangeException(nameof(maxQueued));
        _slots = new SemaphoreSlim(concurrency, concurrency);
        _maxQueued = maxQueued;
    }

    public int Queued => Volatile.Read(ref _queued);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        if (_slots.Wait(0)) return new Lease(_slots);
        int queued = Interlocked.Increment(ref _queued);
        if (queued > _maxQueued)
        {
            Interlocked.Decrement(ref _queued);
            throw new AdmissionRejectedException();
        }
        try
        {
            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(_slots);
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
        }
    }

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private SemaphoreSlim? _slots = slots;
        public void Dispose() => Interlocked.Exchange(ref _slots, null)?.Release();
    }
}

public sealed class AdmissionRejectedException : Exception
{
    public AdmissionRejectedException() : base("The bounded LabyMake work queue is full.") { }
}
