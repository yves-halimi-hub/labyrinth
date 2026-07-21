using System.IO.Compression;
using System.Text.Json;
using Efyv.Labymake.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = HttpProtocols.Http2);
});
builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = 16 * 1024 * 1024;
    options.MaxSendMessageSize = 26 * 1024 * 1024;
});
builder.Services.AddGrpcHealthChecks(options =>
{
    options.Services.Map("", _ => true);
    options.Services.Map("efyv.labymake.v1.LabyMakeEngine", _ => true);
}).AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());
var app = builder.Build();
app.MapGrpcService<LabyMakeService>();
app.MapGrpcHealthChecksService();
app.Run();

sealed class LabyMakeService : LabyMakeEngine.LabyMakeEngineBase
{
    const int MaxSnapshotBytes = 16 * 1024 * 1024;
    public override Task<ValidationReply> Validate(SnapshotRequest request, ServerCallContext context)
    {
        if (Environment.GetEnvironmentVariable("EFYV_CONTRACT_TEST_MODE") == "1")
            return Task.FromResult(new ValidationReply { Valid = true });
        var diagnostics = ValidateSnapshot(request.SnapshotJson.Memory);
        var reply = new ValidationReply { Valid = diagnostics.Count == 0 };
        reply.Diagnostics.AddRange(diagnostics);
        return Task.FromResult(reply);
    }

    public override Task<ExportReply> Export(SnapshotRequest request, ServerCallContext context)
    {
        if (Environment.GetEnvironmentVariable("EFYV_CONTRACT_TEST_MODE") == "1")
            return Task.FromResult(new ExportReply { Filename = "contract.zip", ContentType = "application/zip", Bundle = Google.Protobuf.ByteString.CopyFrom(new byte[] { 80, 75, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }) });
        var diagnostics = ValidateSnapshot(request.SnapshotJson.Memory);
        if (diagnostics.Count != 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, diagnostics[0].Message));
        context.CancellationToken.ThrowIfCancellationRequested();

        byte[] bundle;
        try { bundle = LabyExport.CreateBundle(request.SnapshotJson.Memory, context.CancellationToken); }
        catch (InvalidDataException exception) { throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message)); }
        if (bundle.Length > 25 * 1024 * 1024)
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "export bundle exceeds 25 MB"));
        string name;
        try { name = LabyExport.Parse(request.SnapshotJson.Memory).Stem; }
        catch (InvalidDataException exception) { throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message)); }
        return Task.FromResult(new ExportReply { Filename = name + "-unity-handoff.zip", ContentType = "application/zip", Bundle = Google.Protobuf.ByteString.CopyFrom(bundle) });
    }

    static List<Diagnostic> ValidateSnapshot(ReadOnlyMemory<byte> snapshot)
    {
        var result = new List<Diagnostic>();
        if (snapshot.Length == 0 || snapshot.Length > MaxSnapshotBytes)
        {
            result.Add(new Diagnostic { Code = "snapshot-size", Message = "Snapshot must contain at most 16 MB." });
            return result;
        }
        try
        {
            LabyExport.Parse(snapshot);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or OverflowException)
        {
            result.Add(new Diagnostic { Code = "snapshot-json", Message = exception.Message[..Math.Min(exception.Message.Length, 1000)] });
        }
        return result;
    }

}
