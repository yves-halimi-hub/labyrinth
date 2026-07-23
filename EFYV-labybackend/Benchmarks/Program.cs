using System.Diagnostics;
using System.Text.Json;
using EFYV.Labyrinth.Artifacts;
using EFYV.Runtime.Media;
using EFYVBackend.Core.Memory;

bool verify = args.Contains("--verify", StringComparer.Ordinal);
long checksum = 0;
var results = new List<Result>();

const int side = 256;
int pixelCount = side * side;
var source = new uint[pixelCount];
var destination = new uint[pixelCount];
var scratch = new uint[pixelCount];
var random = new Random(712367);
for (int index = 0; index < source.Length; index++)
{
    source[index] = (uint)random.NextInt64(0, 1L << 32);
    destination[index] = (uint)random.NextInt64(0, 1L << 32);
}

results.Add(Measure("rgba-blend-65k", 50, Blend));
results.Add(Measure("box-blur-256x256-r4", 15, Blur));
results.Add(Measure("png-256x256", 12, EncodePng));

byte[] snapshotBytes = CreateSnapshot(64, 64, 4);
LabyMakeSnapshot snapshot = LabyMakeSnapshotParser.Parse(snapshotBytes);
results.Add(Measure("artifact-export-4x64x64", 8, ExportArtifact));

foreach (Result result in results)
    Console.WriteLine($"{result.Name,-28} {result.Elapsed.TotalMilliseconds / result.Iterations,9:F3} ms/op  {result.AllocatedBytes / (double)result.Iterations,12:F0} B/op  {result.Iterations / result.Elapsed.TotalSeconds,10:F1} op/s");
Console.WriteLine("checksum=" + checksum);

if (verify)
{
    Require(checksum != 0, "benchmark operations were optimized away");
    Require(results.All(result => result.Elapsed > TimeSpan.Zero), "timer failed");
    Require(results.First(result => result.Name.StartsWith("rgba", StringComparison.Ordinal)).AllocatedBytes / resultIterations("rgba-blend-65k") < 1024 * 1024,
        "blend allocation regression exceeds the broad verification ceiling");
    Require(results.All(result => result.AllocatedBytes >= 0), "allocation counter failed");
    Console.WriteLine("PASS benchmark verification");
}

unsafe void Blend()
{
    fixed (uint* destinationPointer = destination)
    fixed (uint* sourcePointer = source)
        RuntimeMediaKernel.BlendRgbaBatch(destinationPointer, sourcePointer, pixelCount, 211, 3);
    checksum ^= destination[(int)(checksum & (pixelCount - 1))];
}

unsafe void Blur()
{
    fixed (uint* sourcePointer = source)
    fixed (uint* destinationPointer = destination)
    fixed (uint* scratchPointer = scratch)
        FastEffects.BoxBlur(sourcePointer, destinationPointer, scratchPointer, side, side, 4);
    checksum ^= destination[(int)((checksum + 17) & (pixelCount - 1))];
}

void EncodePng()
{
    using var output = new MemoryStream();
    PngEncoder.Write(output, source, side, side, compressed: true);
    checksum ^= output.Length;
}

void ExportArtifact()
{
    LabyMakeArtifactBundle bundle = LabyMakeArtifactExporter.Export(snapshot);
    checksum ^= bundle.Bundle.Length;
}

Result Measure(string name, int iterations, Action operation)
{
    operation();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetAllocatedBytesForCurrentThread();
    Stopwatch watch = Stopwatch.StartNew();
    for (int iteration = 0; iteration < iterations; iteration++) operation();
    watch.Stop();
    return new Result(name, iterations, watch.Elapsed, GC.GetAllocatedBytesForCurrentThread() - before);
}

int resultIterations(string name) => results.Single(result => result.Name == name).Iterations;

static byte[] CreateSnapshot(int width, int height, int frameCount)
{
    var bytes = new byte[checked(width * height * 4)];
    for (int index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index * 31 + 17);
    string encoded = Convert.ToBase64String(bytes);
    return JsonSerializer.SerializeToUtf8Bytes(new
    {
        canvasWidth = width,
        canvasHeight = height,
        targetAssetType = "BenchmarkData",
        assetProperties = new { entityName = "benchmark-artifact" },
        animations = new[]
        {
            new
            {
                stateName = "Idle",
                fps = 12,
                frames = Enumerable.Range(0, frameCount).Select(_ => new
                {
                    layers = new[] { new { isVisible = true, opacity = 1f, rgbaBytes = encoded } },
                }),
            },
        },
    });
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct Result(string Name, int Iterations, TimeSpan Elapsed, long AllocatedBytes);
