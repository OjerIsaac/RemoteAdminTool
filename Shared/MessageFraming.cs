namespace RemoteAdminTool.Shared;

/// <summary>
/// Reads/writes length-prefixed frames: [4-byte length][1-byte MessageType][payload].
/// Both Host and Viewer use this so they speak the same wire format.
/// Uses Stream (not NetworkStream) so it works over raw TCP, SslStream, or WebSocket adapters.
/// </summary>
public static class MessageFraming
{
    public static async Task WriteFrameAsync(Stream stream, MessageType type, byte[] payload, CancellationToken ct = default)
    {
        var frame = new byte[4 + 1 + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), payload.Length + 1); // +1 for the type byte
        frame[4] = (byte)type;
        if (payload.Length > 0)
        {
            payload.CopyTo(frame, 5);
        }
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<(MessageType Type, byte[] Payload)> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var lengthBuf = await ReadExactAsync(stream, 4, ct);
        var totalLength = BitConverter.ToInt32(lengthBuf, 0);
        if (totalLength < 1)
        {
            throw new InvalidDataException("Invalid frame length received.");
        }

        var body = await ReadExactAsync(stream, totalLength, ct);
        var type = (MessageType)body[0];
        var payload = body.Length > 1 ? body[1..] : Array.Empty<byte>();
        return (type, payload);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0)
            {
                throw new IOException("Connection closed while reading frame.");
            }
            offset += read;
        }
        return buffer;
    }
}
