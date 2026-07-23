using EFYV.Labyrinth.Artifacts;
using EFYV.Runtime.Media;
using Labymake.Engine;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
EngineRuntimeOptions runtimeOptions = EngineRuntimeOptions.FromEnvironment();

if (Environment.GetEnvironmentVariable("EFYV_RUNTIME_KERNEL_NATIVE") == "1" && !RuntimeMediaKernel.TryEnableNativeV1())
    Console.Error.WriteLine("EFYV runtime kernel v1 was requested but is unavailable; using the managed compatibility implementation.");

builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = HttpProtocols.Http2);
});
builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = LabyrinthArtifactLimits.MaxSnapshotBytes + LabyrinthArtifactLimits.StreamChunkBytes;
    options.MaxSendMessageSize = LabyrinthArtifactLimits.MaxBundleBytes + LabyrinthArtifactLimits.StreamChunkBytes;
});
builder.Services.AddGrpcHealthChecks(options =>
{
    options.Services.Map("", _ => true);
    options.Services.Map("efyv.labymake.v1.LabyMakeEngine", _ => true);
    options.Services.Map("efyv.labymake.v2.LabyMakeArtifactTransfer", _ => true);
}).AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());
builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddSingleton(new WorkAdmission(
    LabyrinthArtifactLimits.MaxConcurrentExports,
    LabyrinthArtifactLimits.MaxQueuedExports));

var app = builder.Build();
app.MapGrpcService<LabyMakeService>();
app.MapGrpcService<LabyMakeTransferService>();
app.MapGrpcHealthChecksService();
app.Run();

public partial class Program { }
