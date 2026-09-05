using System.Text;
using System.Text.Json;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Host;

/// <summary>
/// Monitors the local clipboard for changes and sends them to the admin via the relay.
/// Also receives clipboard data from the admin and sets it locally.
/// Uses polling (every 500ms) since WinForms clipboard change notifications
/// require a message pump on the same thread.
/// </summary>
public sealed class ClipboardSyncService : IDisposable
{
    private string _lastClipboardText = "";
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private Stream? _stream;

    /// <summary>
    /// Start monitoring clipboard changes and sending them over the stream.
    /// </summary>
    public void Start(Stream stream, CancellationToken ct)
    {
        _stream = stream;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _monitorTask = MonitorClipboardAsync(_cts.Token);
    }

    /// <summary>
    /// Handle incoming clipboard data from the remote side.
    /// </summary>
    public void HandleIncomingClipboard(byte[] payload)
    {
        try
        {
            var text = Encoding.UTF8.GetString(payload);
            if (!string.IsNullOrEmpty(text))
            {
                _lastClipboardText = text; // Prevent echo
                SetClipboardText(text);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Clipboard] Error setting clipboard: {ex.Message}");
        }
    }

    private async Task MonitorClipboardAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, ct);

                var currentText = GetClipboardText();
                if (currentText is not null && currentText != _lastClipboardText)
                {
                    _lastClipboardText = currentText;
                    if (_stream is not null)
                    {
                        var bytes = Encoding.UTF8.GetBytes(currentText);
                        await MessageFraming.WriteFrameAsync(_stream, MessageType.ClipboardSync, bytes, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Clipboard] Monitor error: {ex.Message}");
            }
        }
    }

    private static string? GetClipboardText()
    {
        string? text = null;
        // Clipboard operations must run on an STA thread
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Forms.Clipboard.ContainsText())
                {
                    text = System.Windows.Forms.Clipboard.GetText();
                }
            }
            catch { /* clipboard may be locked by another app */ }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(1000); // timeout 1s
        return text;
    }

    private static void SetClipboardText(string text)
    {
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Clipboard.SetText(text);
            }
            catch { /* clipboard may be locked */ }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(1000);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
