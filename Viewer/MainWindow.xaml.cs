using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Viewer;

public partial class MainWindow : Window
{
    private ViewerClient? _client;
    private string? _activeSessionMachineId;
    private int _remoteWidth;
    private int _remoteHeight;

    // Observable collection for the node list in the sidebar
    public ObservableCollection<HostInfoMessage> Nodes { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        NodeList.ItemsSource = Nodes;
    }

    // ---- Connection to Relay ----

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
            Nodes.Clear();
            ConnectButton.Content = "Connect to Relay";
            StatusText.Text = "Disconnected";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f38ba8"));
            return;
        }

        _client = new ViewerClient();
        _client.HostListReceived += OnHostListReceived;
        _client.HostCameOnline += OnHostCameOnline;
        _client.HostWentOffline += OnHostWentOffline;
        _client.SessionStarted += OnSessionStarted;
        _client.FrameReceived += OnFrameReceived;
        _client.Disconnected += OnDisconnected;

        try
        {
            StatusText.Text = "Connecting...";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f9e2af"));

            var (accepted, reason) = await _client.ConnectAsync(RelayUrlBox.Text, AdminTokenBox.Password);

            if (!accepted)
            {
                StatusText.Text = $"Rejected: {reason}";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f38ba8"));
                await _client.DisposeAsync();
                _client = null;
                return;
            }

            StatusText.Text = "Connected to relay";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#a6e3a1"));
            ConnectButton.Content = "Disconnect from Relay";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f38ba8"));
            if (_client is not null) await _client.DisposeAsync();
            _client = null;
        }
    }

    // ---- Host list events ----

    private void OnHostListReceived(object? sender, HostInfoMessage[] hosts)
    {
        Dispatcher.Invoke(() =>
        {
            Nodes.Clear();
            foreach (var host in hosts)
            {
                Nodes.Add(host);
            }
        });
    }

    private void OnHostCameOnline(object? sender, HostOnlineMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            // Add if not already in the list
            var exists = false;
            foreach (var node in Nodes)
            {
                if (node.MachineId == msg.MachineId) { exists = true; break; }
            }
            if (!exists)
            {
                Nodes.Add(new HostInfoMessage(msg.MachineId, msg.MachineName, "", true, DateTime.UtcNow));
            }
        });
    }

    private void OnHostWentOffline(object? sender, HostOfflineMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            for (int i = Nodes.Count - 1; i >= 0; i--)
            {
                if (Nodes[i].MachineId == msg.MachineId)
                {
                    Nodes.RemoveAt(i);
                    break;
                }
            }

            // If the active session host went offline, clear the screen
            if (_activeSessionMachineId == msg.MachineId)
            {
                EndSessionUI();
            }
        });
    }

    // ---- Session events ----

    private void OnSessionStarted(object? sender, SessionStartedMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            _activeSessionMachineId = msg.MachineId;
            _remoteWidth = msg.ScreenWidth;
            _remoteHeight = msg.ScreenHeight;

            SessionHostName.Text = $"Connected to: {msg.MachineName} ({msg.MachineId})";
            SessionToolbar.Visibility = Visibility.Visible;
            NoSessionPlaceholder.Visibility = Visibility.Collapsed;
            RemoteScreenImage.Focus();
        });
    }

    private void OnFrameReceived(object? sender, byte[] jpegBytes)
    {
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

            // Update remote dimensions from the actual frame if not set
            if (_remoteWidth == 0 || _remoteHeight == 0)
            {
                _remoteWidth = bitmap.PixelWidth;
                _remoteHeight = bitmap.PixelHeight;
            }
        });
    }

    private void OnDisconnected(object? sender, string reason)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"Disconnected: {reason}";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f38ba8"));
            ConnectButton.Content = "Connect to Relay";
            Nodes.Clear();
            EndSessionUI();
            _client = null;
        });
    }

    // ---- Node list interaction ----

    private async void NodeList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_client is null || NodeList.SelectedItem is not HostInfoMessage selectedHost)
            return;

        await _client.ConnectToHostAsync(selectedHost.MachineId);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null)
        {
            await _client.RequestHostListAsync();
        }
    }

    private async void DisconnectSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null && _activeSessionMachineId is not null)
        {
            await _client.DisconnectFromHostAsync(_activeSessionMachineId);
            EndSessionUI();
        }
    }

    private void EndSessionUI()
    {
        _activeSessionMachineId = null;
        RemoteScreenImage.Source = null;
        SessionToolbar.Visibility = Visibility.Collapsed;
        NoSessionPlaceholder.Visibility = Visibility.Visible;
    }

    // ---- Input forwarding ----
    // Maps the local mouse position over the Image control back to remote screen coordinates.

    private (int X, int Y) ToRemoteCoords(MouseEventArgs e)
    {
        var pos = e.GetPosition(RemoteScreenImage);
        if (RemoteScreenImage.ActualWidth <= 0 || RemoteScreenImage.ActualHeight <= 0
            || _remoteWidth <= 0 || _remoteHeight <= 0)
            return (0, 0);

        var scaleX = _remoteWidth / RemoteScreenImage.ActualWidth;
        var scaleY = _remoteHeight / RemoteScreenImage.ActualHeight;
        return ((int)(pos.X * scaleX), (int)(pos.Y * scaleY));
    }

    private async void RemoteScreenImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;
        var (x, y) = ToRemoteCoords(e);
        await _client.SendMouseMoveAsync(x, y);
    }

    private async void RemoteScreenImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;
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
        if (_client is null || _activeSessionMachineId is null) return;
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
        if (_client is null || _activeSessionMachineId is null) return;
        var (x, y) = ToRemoteCoords(e);
        await _client.SendMouseWheelAsync(e.Delta, x, y);
    }

    private async void RemoteScreenImage_KeyDown(object sender, KeyEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;
        var vk = KeyInterop.VirtualKeyFromKey(e.Key);
        await _client.SendKeyAsync(vk, KeyAction.Down);
    }

    private async void RemoteScreenImage_KeyUp(object sender, KeyEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;
        var vk = KeyInterop.VirtualKeyFromKey(e.Key);
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
