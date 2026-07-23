using System.IO;
using EFYV.Labyrinth.Artifacts;
using Efyv.Labymake.V1;
using Google.Protobuf;
using Grpc.Core;

namespace Labymake.Engine;

public sealed class LabyMakeService(EngineRuntimeOptions options, WorkAdmission admission)
    : LabyMakeEngine.LabyMakeEngineBase
{
    private static readonly byte[] EmptyZip =
        { 80, 75, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public override async Task<ValidationReply> Validate(SnapshotRequest request, ServerCallContext context)
    {
        if (options.ContractTestMode) return new ValidationReply { Valid = true };
        using IDisposable lease = await EnterAsync(context.CancellationToken).ConfigureAwait(false);
        bool valid = EngineOperations.TryParse(request.SnapshotJson.Memory, context.CancellationToken, out _, out string message);
        var reply = new ValidationReply { Valid = valid };
        if (!valid) reply.Diagnostics.Add(new Diagnostic { Code = "snapshot-json", Message = message });
        return reply;
    }

    public override async Task<ExportReply> Export(SnapshotRequest request, ServerCallContext context)
    {
        if (options.ContractTestMode)
            return new ExportReply
            {
                Filename = "contract.zip",
                ContentType = LabyMakeArtifactBundle.ContentType,
                Bundle = ByteString.CopyFrom(EmptyZip),
            };

        using IDisposable lease = await EnterAsync(context.CancellationToken).ConfigureAwait(false);
        LabyMakeSnapshot snapshot = EngineRpcSupport.ParseOrThrow(request.SnapshotJson.Memory, context.CancellationToken);
        LabyMakeArtifactBundle artifact = await EngineRpcSupport.ExportOrThrow(snapshot, context.CancellationToken).ConfigureAwait(false);
        return new ExportReply
        {
            Filename = artifact.Filename,
            ContentType = LabyMakeArtifactBundle.ContentType,
            Bundle = ByteString.CopyFrom(artifact.Bundle),
            ArtifactReference = artifact.ArtifactReference,
            Sha256 = artifact.Sha256,
        };
    }

    private async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        try { return await admission.EnterAsync(cancellationToken).ConfigureAwait(false); }
        catch (AdmissionRejectedException exception)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, exception.Message));
        }
    }
}

internal static class EngineRpcSupport
{
    internal static LabyMakeSnapshot ParseOrThrow(ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
    {
        if (EngineOperations.TryParse(source, cancellationToken, out LabyMakeSnapshot? snapshot, out string message)) return snapshot!;
        throw new RpcException(new Status(StatusCode.InvalidArgument, message));
    }

    internal static async Task<LabyMakeArtifactBundle> ExportOrThrow(LabyMakeSnapshot snapshot, CancellationToken cancellationToken)
    {
        try { return await EngineOperations.ExportAsync(snapshot, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }

    internal static async ValueTask<IDisposable> EnterAsync(WorkAdmission admission, CancellationToken cancellationToken)
    {
        try { return await admission.EnterAsync(cancellationToken).ConfigureAwait(false); }
        catch (AdmissionRejectedException exception)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, exception.Message));
        }
    }
}
