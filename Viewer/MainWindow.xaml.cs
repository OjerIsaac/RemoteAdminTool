using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Viewer;

public partial class MainWindow : Window
{
    private ViewerClient? _client;
    private int _remoteWidth;
    private int _remoteHeight;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        _client = new ViewerClient();
        _client.FrameReceived += OnFrameReceived;
        _client.Disconnected += OnDisconnected;

        try
        {
            var ack = await _client.ConnectAsync(HostBox.Text, int.Parse(PortBox.Text), TokenBox.Password);
            if (!ack.Accepted)
            {
                StatusText.Text = $"Rejected: {ack.RejectReason}";
                return;
            }

            _remoteWidth = ack.ScreenWidth;
            _remoteHeight = ack.ScreenHeight;
            StatusText.Text = $"Connected ({_remoteWidth}x{_remoteHeight})";
            RemoteScreenImage.Focus();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connection failed: {ex.Message}";
        }
    }

    private void OnFrameReceived(object? sender, byte[] jpegBytes)
    {
        // Frames arrive on a background thread; marshal to UI thread to update the Image control.
        Dispatcher.Invoke(() =>
        {
            using var ms = new MemoryStream(jpegBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            RemoteScreenImage.Source = bitmap;
        });
    }

    private void OnDisconnected(object? sender, string reason)
    {
        Dispatcher.Invoke(() => StatusText.Text = $"Disconnected: {reason}");
    }

    // ---- Input forwarding ----
    // Maps the local mouse position over the Image control back to remote screen coordinates.

    private (int X, int Y) ToRemoteCoords(MouseEventArgs e)
    {
        var pos = e.GetPosition(RemoteScreenImage);
        var scaleX = _remoteWidth / RemoteScreenImage.ActualWidth;
        var scaleY = _remoteHeight / RemoteScreenImage.ActualHeight;
        return ((int)(pos.X * scaleX), (int)(pos.Y * scaleY));
    }

    private async void RemoteScreenImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_client is null) return;
        var (x, y) = ToRemoteCoords(e);
        await _client.SendMouseMoveAsync(x, y);
    }

    private async void RemoteScreenImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_client is null) return;
        var (x, y) = ToRemoteCoords(e);
        var button = e.ChangedButton switch
        {
            MouseButton.Right => MouseButtonKind.Right,
            MouseButton.Middle => MouseButtonKind.Middle,
            _ => MouseButtonKind.Left
        };
        await _client.SendMouseButtonAsync(button, MouseButtonAction.Down, x, y);
    }

    private async void RemoteScreenImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_client is null) return;
        var (x, y) = ToRemoteCoords(e);
        var button = e.ChangedButton switch
        {
            MouseButton.Right => MouseButtonKind.Right,
            MouseButton.Middle => MouseButtonKind.Middle,
            _ => MouseButtonKind.Left
        };
        await _client.SendMouseButtonAsync(button, MouseButtonAction.Up, x, y);
    }

    private async void RemoteScreenImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_client is null) return;
        var (x, y) = ToRemoteCoords(e);
        await _client.SendMouseWheelAsync(e.Delta, x, y);
    }

    private async void RemoteScreenImage_KeyDown(object sender, KeyEventArgs e)
    {
        if (_client is null) return;
        var vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key);
        await _client.SendKeyAsync(vk, KeyAction.Down);
    }

    private async void RemoteScreenImage_KeyUp(object sender, KeyEventArgs e)
    {
        if (_client is null) return;
        var vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key);
        await _client.SendKeyAsync(vk, KeyAction.Up);
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
