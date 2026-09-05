using System.Net.WebSockets;

namespace RemoteAdminTool.Shared;

/// <summary>
/// Adapts a WebSocket into a Stream so that MessageFraming (which reads/writes
/// length-prefixed frames on a Stream) works transparently over WebSocket connections.
///
/// Thread-safe for concurrent writes/reads using SemaphoreSlim locks.
/// Supports both async and synchronous fallback operations cleanly.
/// </summary>
public sealed class WebSocketStreamAdapter : Stream
{
    private readonly WebSocket _ws;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _readLock = new(1, 1);
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
        await _readLock.WaitAsync(ct);
        try
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

            while (_readOffset >= _readBuffer.Length)
            {
                if (_ws.State != WebSocketState.Open && _ws.State != WebSocketState.CloseSent)
                {
                    return 0; // signal EOF
                }

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

                if (ms.Length > 0)
                {
                    _readBuffer = ms.ToArray();
                    _readOffset = 0;
                    break;
                }
            }

            var copyLen = Math.Min(_readBuffer.Length, buffer.Length);
            _readBuffer.AsMemory(0, copyLen).CopyTo(buffer);
            _readOffset = copyLen;
            return copyLen;
        }
        finally
        {
            _readLock.Release();
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        await WriteAsync(buffer.AsMemory(offset, count), ct);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        if (_ws.State != WebSocketState.Open) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, ct);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    public override void WriteByte(byte value)
    {
        Write(new[] { value }, 0, 1);
    }

    public override int ReadByte()
    {
        var buf = new byte[1];
        var read = Read(buf, 0, 1);
        return read == 0 ? -1 : buf[0];
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    // ---- Not supported (stream is not seekable) ----
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writeLock.Dispose();
            _readLock.Dispose();
        }
        base.Dispose(disposing);
    }
}
