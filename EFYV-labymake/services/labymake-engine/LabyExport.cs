using System.IO.Compression;
using System.Text.Json;

static class LabyExport
{
    const int MaxDimension = 4096;
    const int MaxFrames = 4096;
    const long MaxDecodedFrameBytes = 64L * 1024 * 1024;
    const long MaxAtlasPixels = 16L * 1024 * 1024;

    internal sealed record AnimationInfo(string Name, int Fps, int Start, int Count, int[] Durations, int LoopStart, int LoopEnd, bool PingPong, JsonElement Source);
    internal sealed record SnapshotInfo(string Stem, string AssetType, int Width, int Height, byte[][] Frames, AnimationInfo[] Animations, JsonElement Properties, JsonElement Root);

    public static SnapshotInfo Parse(ReadOnlyMemory<byte> bytes)
    {
        RejectDuplicateProperties(bytes.Span);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Snapshot must be an object.");
        var width = RequiredInt(root, "canvasWidth", 1, MaxDimension);
        var height = RequiredInt(root, "canvasHeight", 1, MaxDimension);
        var assetType = OptionalString(root, "targetAssetType") ?? "EFYVSprite";
        var properties = root.TryGetProperty("assetProperties", out var rawProperties) && rawProperties.ValueKind == JsonValueKind.Object
            ? rawProperties.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
        var identity = PropertyString(properties, "entityName") ?? PropertyString(properties, "assetName") ?? OptionalString(root, "name") ?? "labymake-project";
        var stem = SafeStem(identity);
        if (!root.TryGetProperty("animations", out var animations) || animations.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("animations must be an array.");

        var frames = new List<byte[]>();
        var animationInfo = new List<AnimationInfo>();
        foreach (var animation in animations.EnumerateArray())
        {
            if (animation.ValueKind != JsonValueKind.Object) throw new InvalidDataException("animation must be an object.");
            var name = OptionalString(animation, "stateName") ?? throw new InvalidDataException("animation stateName is required.");
            var fps = RequiredInt(animation, "fps", 1, 240);
            if (!animation.TryGetProperty("frames", out var rawFrames) || rawFrames.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("animation frames must be an array.");
            var start = frames.Count;
            var durations = new List<int>();
            foreach (var frame in rawFrames.EnumerateArray())
            {
                var frameBytes = checked((long)width * height * 4);
                if (checked((frames.Count + 1L) * frameBytes) > MaxDecodedFrameBytes)
                    throw new InvalidDataException("Decoded frame data exceeds the 64 MB aggregate limit.");
                frames.Add(FlattenFrame(frame, width, height));
                durations.Add(frame.TryGetProperty("durationMs", out var duration) && duration.TryGetInt32(out var durationValue) ? Math.Clamp(durationValue, 0, 60_000) : 0);
                if (frames.Count > MaxFrames) throw new InvalidDataException("Snapshot contains too many frames.");
            }
            if (frames.Count == start) throw new InvalidDataException("Each animation needs at least one frame.");
            var count = frames.Count - start;
            var loopStart = animation.TryGetProperty("loopStart", out var rawStart) && rawStart.TryGetInt32(out var startValue) ? Math.Clamp(startValue, 0, count - 1) : 0;
            var loopEnd = animation.TryGetProperty("loopEnd", out var rawEnd) && rawEnd.TryGetInt32(out var endValue) ? Math.Clamp(endValue, loopStart, count - 1) : count - 1;
            var pingPong = animation.TryGetProperty("pingPong", out var rawPingPong) && rawPingPong.ValueKind == JsonValueKind.True;
            animationInfo.Add(new AnimationInfo(name, fps, start, count, durations.ToArray(), loopStart, loopEnd, pingPong, animation.Clone()));
        }
        if (frames.Count == 0) throw new InvalidDataException("Snapshot needs at least one frame.");
        return new SnapshotInfo(stem, assetType, width, height, frames.ToArray(), animationInfo.ToArray(), properties, root.Clone());
    }

    public static byte[] CreateBundle(ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
    {
        var snapshot = Parse(source);
        cancellationToken.ThrowIfCancellationRequested();
        var columns = (int)Math.Ceiling(Math.Sqrt(snapshot.Frames.Length));
        var rows = (snapshot.Frames.Length + columns - 1) / columns;
        var atlasWidth = checked(snapshot.Width * columns);
        var atlasHeight = checked(snapshot.Height * rows);
        if (atlasWidth > 8192 || atlasHeight > 8192 || (long)atlasWidth * atlasHeight > MaxAtlasPixels)
            throw new InvalidDataException("Atlas exceeds the bounded export dimensions.");
        var atlas = new byte[checked(atlasWidth * atlasHeight * 4)];
        for (var index = 0; index < snapshot.Frames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originX = index % columns * snapshot.Width;
            var originY = index / columns * snapshot.Height;
            for (var y = 0; y < snapshot.Height; y++)
                Buffer.BlockCopy(snapshot.Frames[index], y * snapshot.Width * 4, atlas, ((originY + y) * atlasWidth + originX) * 4, snapshot.Width * 4);
        }

        var png = Png(atlas, atlasWidth, atlasHeight);
        var metadata = Metadata(snapshot, atlasWidth, atlasHeight);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, snapshot.Stem + ".png", png);
            Entry(zip, snapshot.Stem + ".efyvlaby", metadata);
            Entry(zip, snapshot.Stem + ".efyvmake", source.ToArray());
            Entry(zip, "handoff.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                destination = "browser-folder",
                files = new[] { snapshot.Stem + ".png", snapshot.Stem + ".efyvlaby" }
            }));
        }
        return output.ToArray();
    }

    static byte[] FlattenFrame(JsonElement frame, int width, int height)
    {
        if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("frame layers must be an array.");
        var destination = new byte[checked(width * height * 4)];
        var layerCount = 0;
        foreach (var layer in layers.EnumerateArray())
        {
            if (++layerCount > 256) throw new InvalidDataException("frame contains too many layers.");
            if (layer.ValueKind != JsonValueKind.Object) throw new InvalidDataException("layer must be an object.");
            if (layer.TryGetProperty("isVisible", out var visible) && visible.ValueKind == JsonValueKind.False) continue;
            if (!layer.TryGetProperty("rgbaBytes", out var encoded) || encoded.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("layer rgbaBytes is required.");
            byte[] pixels;
            try { pixels = Convert.FromBase64String(encoded.GetString()!); }
            catch (FormatException exception) { throw new InvalidDataException("layer rgbaBytes is invalid.", exception); }
            if (pixels.Length != destination.Length) throw new InvalidDataException("layer dimensions do not match the canvas.");
            var opacity = layer.TryGetProperty("opacity", out var rawOpacity) && rawOpacity.TryGetSingle(out var value) ? Math.Clamp(value, 0f, 1f) : 1f;
            Blend(destination, pixels, opacity);
        }
        return destination;
    }

    static void Blend(byte[] destination, byte[] source, float opacity)
    {
        for (var index = 0; index < destination.Length; index += 4)
        {
            var sourceAlpha = source[index + 3] / 255f * opacity;
            if (sourceAlpha <= 0) continue;
            var destinationAlpha = destination[index + 3] / 255f;
            var outputAlpha = sourceAlpha + destinationAlpha * (1 - sourceAlpha);
            for (var channel = 0; channel < 3; channel++)
            {
                var value = outputAlpha <= 0 ? 0 : (source[index + channel] * sourceAlpha + destination[index + channel] * destinationAlpha * (1 - sourceAlpha)) / outputAlpha;
                destination[index + channel] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
            }
            destination[index + 3] = (byte)Math.Clamp((int)Math.Round(outputAlpha * 255), 0, 255);
        }
    }

    static byte[] Metadata(SnapshotInfo snapshot, int atlasWidth, int atlasHeight)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("documentVersion", 5);
            writer.WriteString("assetType", snapshot.AssetType);
            if (snapshot.Root.TryGetProperty("baseAssetType", out var baseType) && baseType.ValueKind == JsonValueKind.String)
                writer.WriteString("baseAssetType", baseType.GetString());
            writer.WritePropertyName("properties");
            snapshot.Properties.WriteTo(writer);
            writer.WritePropertyName("hitboxes");
            if (snapshot.Root.TryGetProperty("hitboxes", out var hitboxes) && hitboxes.ValueKind == JsonValueKind.Array) hitboxes.WriteTo(writer);
            else { writer.WriteStartArray(); writer.WriteEndArray(); }
            writer.WriteStartObject("atlas");
            writer.WriteNumber("formatVersion", 1);
            writer.WriteNumber("frameWidth", snapshot.Width);
            writer.WriteNumber("frameHeight", snapshot.Height);
            writer.WriteNumber("atlasWidth", atlasWidth);
            writer.WriteNumber("atlasHeight", atlasHeight);
            writer.WriteStartArray("animations");
            foreach (var animation in snapshot.Animations)
            {
                writer.WriteStartObject();
                writer.WriteString("name", animation.Name);
                writer.WriteNumber("fps", animation.Fps);
                writer.WriteNumber("startFrame", animation.Start);
                writer.WriteNumber("frameCount", animation.Count);
                writer.WriteStartArray("frameDurationsMs");
                foreach (var duration in animation.Durations) writer.WriteNumberValue(duration);
                writer.WriteEndArray();
                writer.WriteNumber("loopStart", animation.LoopStart);
                writer.WriteNumber("loopEnd", animation.LoopEnd);
                writer.WriteBoolean("pingPong", animation.PingPong);
                if (animation.Source.TryGetProperty("effects", out var effects) && effects.ValueKind == JsonValueKind.Array)
                {
                    writer.WritePropertyName("effects");
                    effects.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            if (snapshot.Root.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
            {
                writer.WritePropertyName("attachments");
                attachments.WriteTo(writer);
            }
            if (snapshot.Root.TryGetProperty("tileset", out var tileset) && tileset.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("tileset");
                tileset.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return output.ToArray();
    }

    static byte[] Png(byte[] rgba, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BigEndian(ihdr, 0, (uint)width);
        BigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(output, "IHDR", ihdr);
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(zlib);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    static void Chunk(Stream output, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var length = new byte[4]; BigEndian(length, 0, (uint)data.Length); output.Write(length);
        output.Write(typeBytes); output.Write(data);
        var crc = Crc(typeBytes.Concat(data).ToArray());
        var crcBytes = new byte[4]; BigEndian(crcBytes, 0, crc); output.Write(crcBytes);
    }

    static uint Crc(byte[] bytes)
    {
        uint crc = 0xffffffff;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xffffffff;
    }

    static void BigEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value;
    }

    static void Entry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open(); stream.Write(bytes);
    }

    static int RequiredInt(JsonElement root, string name, int minimum, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var number) || number < minimum || number > maximum)
            throw new InvalidDataException($"{name} must be from {minimum} through {maximum}.");
        return number;
    }

    static string? OptionalString(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static string? PropertyString(JsonElement properties, string name) => OptionalString(properties, name);

    static void RejectDuplicateProperties(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var objects = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) objects.Push(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var current = objects.Peek();
                var name = reader.GetString() ?? throw new InvalidDataException("JSON property name is invalid.");
                if (current is null || !current.Add(name)) throw new InvalidDataException($"Duplicate JSON property {name}.");
            }
        }
    }

    static string SafeStem(string value)
    {
        var safe = new string(value.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_').Take(80).ToArray());
        return safe.Length == 0 ? "labymake-project" : safe;
    }
}
