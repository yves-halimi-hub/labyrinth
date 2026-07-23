using EFYV.Labyrinth.Artifacts;
using Efyv.Labymake.V2;
using Google.Protobuf;
using Grpc.Core;

namespace Labymake.Engine;

public sealed class LabyMakeTransferService(EngineRuntimeOptions options, WorkAdmission admission)
    : LabyMakeArtifactTransfer.LabyMakeArtifactTransferBase
{
    private static readonly byte[] EmptyZip =
        { 80, 75, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public override Task<LimitsReply> GetLimits(LimitsRequest request, ServerCallContext context) =>
        Task.FromResult(new LimitsReply
        {
            ContractVersion = LabyrinthArtifactLimits.ContractVersion,
            MaxSnapshotBytes = LabyrinthArtifactLimits.MaxSnapshotBytes,
            MaxBundleBytes = LabyrinthArtifactLimits.MaxBundleBytes,
            StreamChunkBytes = LabyrinthArtifactLimits.StreamChunkBytes,
            MaxConcurrentExports = LabyrinthArtifactLimits.MaxConcurrentExports,
            MaxQueuedExports = LabyrinthArtifactLimits.MaxQueuedExports,
            ArtifactReferences = true,
        });

    public override async Task Export(
        IAsyncStreamReader<ExportRequest> requestStream,
        IServerStreamWriter<ExportReply> responseStream,
        ServerCallContext context)
    {
        using IDisposable lease = await EngineRpcSupport.EnterAsync(admission, context.CancellationToken).ConfigureAwait(false);
        ReadOnlyMemory<byte> source = await ReadSnapshotAsync(requestStream, context.CancellationToken).ConfigureAwait(false);
        LabyMakeArtifactBundle artifact;
        if (options.ContractTestMode)
        {
            string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(EmptyZip)).ToLowerInvariant();
            artifact = new LabyMakeArtifactBundle("contract", Array.Empty<byte>(), Array.Empty<byte>(), EmptyZip, digest);
        }
        else
        {
            LabyMakeSnapshot snapshot = EngineRpcSupport.ParseOrThrow(source, context.CancellationToken);
            artifact = await EngineRpcSupport.ExportOrThrow(snapshot, context.CancellationToken).ConfigureAwait(false);
        }

        await responseStream.WriteAsync(new ExportReply
        {
            Header = new ExportHeader
            {
                Filename = artifact.Filename,
                ContentType = LabyMakeArtifactBundle.ContentType,
                TotalSize = (ulong)artifact.Bundle.Length,
                ArtifactReference = artifact.ArtifactReference,
                Sha256 = artifact.Sha256,
            },
        }).ConfigureAwait(false);
        for (int offset = 0; offset < artifact.Bundle.Length; offset += LabyrinthArtifactLimits.StreamChunkBytes)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(LabyrinthArtifactLimits.StreamChunkBytes, artifact.Bundle.Length - offset);
            await responseStream.WriteAsync(new ExportReply
            {
                Chunk = ByteString.CopyFrom(artifact.Bundle, offset, count),
            }).ConfigureAwait(false);
        }
    }

    public static async Task<ReadOnlyMemory<byte>> ReadSnapshotAsync(
        IAsyncStreamReader<ExportRequest> requestStream,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        ulong declaredSize = 0;
        bool started = false;
        while (await requestStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            ExportRequest request = requestStream.Current;
            if (!started)
            {
                if (request.PayloadCase != ExportRequest.PayloadOneofCase.Start)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Streaming export must begin with a start message."));
                started = true;
                declaredSize = request.Start.TotalSize;
                if (request.Start.ContractVersion != LabyrinthArtifactLimits.ContractVersion)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Unsupported artifact contract version."));
                if (declaredSize == 0 || declaredSize > LabyrinthArtifactLimits.MaxSnapshotBytes)
                    throw new RpcException(new Status(StatusCode.ResourceExhausted, "Snapshot exceeds the released limit."));
                continue;
            }
            if (request.PayloadCase != ExportRequest.PayloadOneofCase.Chunk || request.Chunk.Length == 0 ||
                request.Chunk.Length > LabyrinthArtifactLimits.StreamChunkBytes)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Streaming export accepts non-empty bounded chunks after start."));
            if ((ulong)output.Length + (ulong)request.Chunk.Length > declaredSize)
                throw new RpcException(new Status(StatusCode.ResourceExhausted, "Snapshot stream exceeds its declared size."));
            request.Chunk.WriteTo(output);
        }
        if (!started || (ulong)output.Length != declaredSize)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Snapshot stream length does not match its declaration."));
        return output.ToArray();
    }
}
