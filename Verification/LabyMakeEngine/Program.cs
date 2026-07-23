using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EFYV.Labyrinth.Artifacts;
using Efyv.Labymake.V2;
using Google.Protobuf;
using Grpc.Core;
using Labymake.Engine;

int assertions = 0;
await Run("canonical deterministic artifact", TestCanonicalArtifact);
await Run("malformed and unsafe snapshots", TestInvalidSnapshots);
await Run("cancellation reaches parse and export", TestCancellation);
await Run("bounded admission rejects overflow", TestAdmission);
await Run("streaming request contract", TestStreamingRequest);
await Run("CI contract mode startup policy", TestContractModeStartupPolicy);
Console.WriteLine($"PASS 6 groups, {assertions} assertions");

async Task Run(string name, Func<Task> body)
{
    try { await body(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { Console.Error.WriteLine("FAIL " + name + ": " + exception); Environment.ExitCode = 1; }
}

Task TestCanonicalArtifact()
{
    byte[] source = Snapshot("maker-hero");
    LabyMakeSnapshot snapshot = LabyMakeSnapshotParser.Parse(source);
    LabyMakeArtifactBundle first = LabyMakeArtifactExporter.Export(snapshot);
    LabyMakeArtifactBundle second = LabyMakeArtifactExporter.Export(LabyMakeSnapshotParser.Parse(source));
    EqualSequence(first.Bundle, second.Bundle);
    Equal(first.Sha256, second.Sha256);
    Check(first.ArtifactReference == "sha256:" + first.Sha256);
    Check(first.Filename == "maker-hero-unity-handoff.zip");
    Check(first.Png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));

    using var zip = new ZipArchive(new MemoryStream(first.Bundle), ZipArchiveMode.Read);
    EqualSequence(new[] { "maker-hero.png", "maker-hero.efyvlaby", "maker-hero.efyvmake", "handoff.json" },
        zip.Entries.Select(entry => entry.FullName).ToArray());
    using JsonDocument metadata = JsonDocument.Parse(first.Metadata);
    JsonElement root = metadata.RootElement;
    Check(root.GetProperty("documentVersion").GetInt32() == LabyrinthArtifactLimits.DocumentVersion);
    Check(root.GetProperty("atlas").GetProperty("animations")[0].GetProperty("frameDurationsMs")[0].GetInt32() == 83);
    Check(root.GetProperty("properties").GetProperty("entityName").GetString() == "maker-hero");
    return Task.CompletedTask;
}

Task TestInvalidSnapshots()
{
    byte[] duplicate = Encoding.UTF8.GetBytes("{\"canvasWidth\":1,\"canvasWidth\":1}");
    Check(!EngineOperations.TryParse(duplicate, default, out _, out string duplicateMessage));
    Check(duplicateMessage.Contains("Duplicate", StringComparison.Ordinal));

    LabyMakeSnapshot unsafeSnapshot = LabyMakeSnapshotParser.Parse(Snapshot("../escape"));
    Throws<ArgumentException>(() => LabyMakeArtifactExporter.Export(unsafeSnapshot));

    string json = Encoding.UTF8.GetString(Snapshot("duration"));
    byte[] badDuration = Encoding.UTF8.GetBytes(json.Replace("\"durationMs\":83", "\"durationMs\":60001", StringComparison.Ordinal));
    Check(!EngineOperations.TryParse(badDuration, default, out _, out string durationMessage));
    Check(durationMessage.Contains("duration", StringComparison.OrdinalIgnoreCase));
    return Task.CompletedTask;
}

Task TestCancellation()
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Throws<OperationCanceledException>(() => LabyMakeSnapshotParser.Parse(Snapshot("cancelled"), cancellation.Token));
    LabyMakeSnapshot snapshot = LabyMakeSnapshotParser.Parse(Snapshot("cancelled"));
    Throws<OperationCanceledException>(() => LabyMakeArtifactExporter.Export(snapshot, cancellation.Token));
    return Task.CompletedTask;
}

async Task TestAdmission()
{
    var admission = new WorkAdmission(1, 1);
    IDisposable first = await admission.EnterAsync(default);
    Task<IDisposable> secondTask = admission.EnterAsync(default).AsTask();
    for (int spin = 0; spin < 100 && admission.Queued != 1; spin++) await Task.Yield();
    Check(admission.Queued == 1);
    await ThrowsAsync<AdmissionRejectedException>(async () => (await admission.EnterAsync(default)).Dispose());
    first.Dispose();
    using IDisposable second = await secondTask;
    Check(admission.Queued == 0);
}

async Task TestStreamingRequest()
{
    byte[] source = Snapshot("streamed");
    var requests = new List<ExportRequest>
    {
        new() { Start = new ExportStart { ContractVersion = LabyrinthArtifactLimits.ContractVersion, TotalSize = (ulong)source.Length } },
    };
    for (int offset = 0; offset < source.Length; offset += 17)
        requests.Add(new ExportRequest { Chunk = ByteString.CopyFrom(source, offset, Math.Min(17, source.Length - offset)) });
    ReadOnlyMemory<byte> parsed = await LabyMakeTransferService.ReadSnapshotAsync(new MemoryStreamReader<ExportRequest>(requests), default);
    EqualSequence(source, parsed.ToArray());

    var missingStart = new[] { new ExportRequest { Chunk = ByteString.CopyFromUtf8("bad") } };
    await ThrowsAsync<RpcException>(() => LabyMakeTransferService.ReadSnapshotAsync(new MemoryStreamReader<ExportRequest>(missingStart), default));
}

Task TestContractModeStartupPolicy()
{
    const string variable = EngineRuntimeOptions.ContractTestModeEnvironmentVariable;
    string? previous = Environment.GetEnvironmentVariable(variable);
    try
    {
        Environment.SetEnvironmentVariable(variable, "1");
        Check(EngineRuntimeOptions.FromEnvironment().ContractTestMode);

        Environment.SetEnvironmentVariable(variable, "true");
        Check(!EngineRuntimeOptions.FromEnvironment().ContractTestMode);

        Environment.SetEnvironmentVariable(variable, null);
        Check(!EngineRuntimeOptions.FromEnvironment().ContractTestMode);
    }
    finally
    {
        Environment.SetEnvironmentVariable(variable, previous);
    }
    return Task.CompletedTask;
}

static byte[] Snapshot(string entityName)
{
    string pixels = Convert.ToBase64String(new byte[] { 255, 0, 0, 255, 0, 128, 255, 128 });
    return JsonSerializer.SerializeToUtf8Bytes(new
    {
        name = "Maker Project",
        canvasWidth = 2,
        canvasHeight = 1,
        targetAssetType = "HeroData",
        assetProperties = new { entityName, displayName = "Maker Hero", level = 7 },
        hitboxes = Array.Empty<object>(),
        animations = new[]
        {
            new
            {
                stateName = "Idle",
                fps = 12,
                loopStart = 0,
                loopEnd = 0,
                pingPong = false,
                frames = new[]
                {
                    new { durationMs = 83, layers = new[] { new { isVisible = true, opacity = 1f, rgbaBytes = pixels } } },
                },
            },
        },
    });
}

void Check(bool value)
{
    assertions++;
    if (!value) throw new InvalidOperationException("Assertion failed.");
}

void Equal<T>(T expected, T actual) where T : notnull
{
    assertions++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}

void EqualSequence<T>(T[] expected, T[] actual)
{
    assertions++;
    if (!expected.AsSpan().SequenceEqual(actual)) throw new InvalidOperationException("Sequences differ.");
}

void Throws<T>(Action action) where T : Exception
{
    assertions++;
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
}

async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    assertions++;
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
}

sealed class MemoryStreamReader<T>(IEnumerable<T> values) : IAsyncStreamReader<T>
{
    private readonly IEnumerator<T> enumerator = values.GetEnumerator();
    public T Current { get; private set; } = default!;
    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool moved = enumerator.MoveNext();
        if (moved) Current = enumerator.Current;
        return Task.FromResult(moved);
    }
}
