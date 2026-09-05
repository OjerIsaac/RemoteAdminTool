using System.Net.WebSockets;

namespace RemoteAdminTool.Shared;

/// <summary>
/// Adapts a WebSocket into a Stream so that MessageFraming (which reads/writes
/// length-prefixed frames on a Stream) works transparently over WebSocket connections.
///
/// This collects incoming WebSocket messages into an internal buffer and exposes
/// them as a continuous byte stream for ReadAsync. WriteAsync sends raw bytes as
/// binary WebSocket messages.
/// </summary>
public sealed class WebSocketStreamAdapter : Stream
{
    private readonly WebSocket _ws;
    private byte[] _readBuffer = Array.Empty<byte>();
    private int _readOffset;

    public WebSocketStreamAdapter(WebSocket ws)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        return await ReadAsync(buffer.AsMemory(offset, count), ct);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        // If there's leftover data in _readBuffer from a previous WebSocket message, serve from that first.
        if (_readOffset < _readBuffer.Length)
        {
            var available = _readBuffer.Length - _readOffset;
            var toCopy = Math.Min(available, buffer.Length);
            _readBuffer.AsMemory(_readOffset, toCopy).CopyTo(buffer);
            _readOffset += toCopy;
            return toCopy;
        }

        // Need a new WebSocket message.
        // Use a reasonably large receive buffer; WebSocket messages from the relay
        // can be up to several hundred KB (screen frames).
        var recvBuf = new byte[256 * 1024];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(new ArraySegment<byte>(recvBuf), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return 0; // signal EOF
            }
            ms.Write(recvBuf, 0, result.Count);
        }
        while (!result.EndOfMessage);

        _readBuffer = ms.ToArray();
        _readOffset = 0;

        var copyLen = Math.Min(_readBuffer.Length, buffer.Length);
        _readBuffer.AsMemory(0, copyLen).CopyTo(buffer);
        _readOffset = copyLen;
        return copyLen;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        await WriteAsync(buffer.AsMemory(offset, count), ct);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        if (_ws.State == WebSocketState.Open)
        {
            await _ws.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, ct);
        }
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    // ---- Not supported (stream is not seekable) ----
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use ReadAsync.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use WriteAsync.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Don't dispose the WebSocket here — the caller owns it.
        }
        base.Dispose(disposing);
    }
}
