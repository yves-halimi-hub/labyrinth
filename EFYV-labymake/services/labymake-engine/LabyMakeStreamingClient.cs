using EFYV.Labyrinth.Artifacts;
using Efyv.Labymake.V2;
using Google.Protobuf;
using Grpc.Core;

namespace Labymake.Engine;

public static class LabyMakeStreamingClient
{
    public static async Task<(ExportHeader Header, byte[] Bundle)> ExportAsync(
        LabyMakeArtifactTransfer.LabyMakeArtifactTransferClient client,
        ReadOnlyMemory<byte> snapshot,
        CancellationToken cancellationToken = default)
    {
        using AsyncDuplexStreamingCall<ExportRequest, ExportReply> call = client.Export(cancellationToken: cancellationToken);
        await call.RequestStream.WriteAsync(new ExportRequest
        {
            Start = new ExportStart
            {
                ContractVersion = LabyrinthArtifactLimits.ContractVersion,
                TotalSize = (ulong)snapshot.Length,
            },
        }).ConfigureAwait(false);
        for (int offset = 0; offset < snapshot.Length; offset += LabyrinthArtifactLimits.StreamChunkBytes)
        {
            int count = Math.Min(LabyrinthArtifactLimits.StreamChunkBytes, snapshot.Length - offset);
            await call.RequestStream.WriteAsync(new ExportRequest
            {
                Chunk = ByteString.CopyFrom(snapshot.Slice(offset, count).Span),
            }).ConfigureAwait(false);
        }
        await call.RequestStream.CompleteAsync().ConfigureAwait(false);

        ExportHeader? header = null;
        using var bundle = new MemoryStream();
        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            ExportReply reply = call.ResponseStream.Current;
            if (reply.PayloadCase == ExportReply.PayloadOneofCase.Header && header == null) header = reply.Header;
            else if (reply.PayloadCase == ExportReply.PayloadOneofCase.Chunk && header != null) reply.Chunk.WriteTo(bundle);
            else throw new InvalidDataException("Malformed streaming export response.");
            if (bundle.Length > LabyrinthArtifactLimits.MaxBundleBytes) throw new InvalidDataException("Streaming export response exceeds the released limit.");
        }
        if (header == null || (ulong)bundle.Length != header.TotalSize) throw new InvalidDataException("Streaming export response length is invalid.");
        return (header, bundle.ToArray());
    }
}
