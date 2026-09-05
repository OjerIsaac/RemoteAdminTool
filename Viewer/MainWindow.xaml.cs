using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Viewer;

public partial class MainWindow : Window
{
    private ViewerClient? _client;
    private string? _activeSessionMachineId;
    private int _remoteWidth;
    private int _remoteHeight;
    private bool _chatVisible;

    public ObservableCollection<HostInfoMessage> Nodes { get; } = new();
    public ObservableCollection<ChatEntry> ChatEntries { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        NodeList.ItemsSource = Nodes;
        ChatMessages.ItemsSource = ChatEntries;
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
            SetStatusColor("#f38ba8");
            return;
        }

        _client = new ViewerClient();
        _client.HostListReceived += OnHostListReceived;
        _client.HostCameOnline += OnHostCameOnline;
        _client.HostWentOffline += OnHostWentOffline;
        _client.SessionStarted += OnSessionStarted;
        _client.FrameReceived += OnFrameReceived;
        _client.ClipboardReceived += OnClipboardReceived;
        _client.MonitorListReceived += OnMonitorListReceived;
        _client.ChatReceived += OnChatReceived;
        _client.Disconnected += OnDisconnected;

        try
        {
            StatusText.Text = "Connecting...";
            SetStatusColor("#f9e2af");

            var (accepted, reason) = await _client.ConnectAsync(RelayUrlBox.Text, AdminTokenBox.Password);

            if (!accepted)
            {
                StatusText.Text = $"Rejected: {reason}";
                SetStatusColor("#f38ba8");
                await _client.DisposeAsync();
                _client = null;
                return;
            }

            StatusText.Text = "Connected to relay";
            SetStatusColor("#a6e3a1");
            ConnectButton.Content = "Disconnect from Relay";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
            SetStatusColor("#f38ba8");
            if (_client is not null) await _client.DisposeAsync();
            _client = null;
        }
    }

    private void SetStatusColor(string hex)
    {
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
    }

    // ---- Host list events ----

    private void OnHostListReceived(object? sender, HostInfoMessage[] hosts)
    {
        Dispatcher.Invoke(() =>
        {
            Nodes.Clear();
            foreach (var host in hosts) Nodes.Add(host);
        });
    }

    private void OnHostCameOnline(object? sender, HostOnlineMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            var exists = false;
            foreach (var node in Nodes)
                if (node.MachineId == msg.MachineId) { exists = true; break; }
            if (!exists)
                Nodes.Add(new HostInfoMessage(msg.MachineId, msg.MachineName, "", true, DateTime.UtcNow));
        });
    }

    private void OnHostWentOffline(object? sender, HostOfflineMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            for (int i = Nodes.Count - 1; i >= 0; i--)
                if (Nodes[i].MachineId == msg.MachineId) { Nodes.RemoveAt(i); break; }

            if (_activeSessionMachineId == msg.MachineId)
                EndSessionUI();
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
            SetStatusColor("#f38ba8");
            ConnectButton.Content = "Connect to Relay";
            Nodes.Clear();
            EndSessionUI();
            _client = null;
        });
    }

    // ---- Clipboard ----

    private void OnClipboardReceived(object? sender, string text)
    {
        Dispatcher.Invoke(() =>
        {
            try { Clipboard.SetText(text); }
            catch { /* clipboard may be locked */ }
        });
    }

    private async void ClipboardSyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        try
        {
            var text = Clipboard.GetText();
            if (!string.IsNullOrEmpty(text))
            {
                await _client.SendClipboardAsync(text);
            }
        }
        catch { /* clipboard may be locked */ }
    }

    // ---- Monitor selection ----

    private void OnMonitorListReceived(object? sender, MonitorInfoData[] monitors)
    {
        Dispatcher.Invoke(() =>
        {
            MonitorSelector.Items.Clear();
            foreach (var mon in monitors)
            {
                var label = $"{mon.Name} ({mon.Width}x{mon.Height}){(mon.IsPrimary ? " ★" : "")}";
                MonitorSelector.Items.Add(new MonitorComboItem(mon.Index, label));
            }
            if (MonitorSelector.Items.Count > 0)
            {
                MonitorSelector.SelectedIndex = 0;
                MonitorSelector.Visibility = Visibility.Visible;
            }
        });
    }

    private async void MonitorSelector_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_client is null || MonitorSelector.SelectedItem is not MonitorComboItem item) return;
        await _client.SelectMonitorAsync(item.Index);
    }

    // ---- Chat ----

    private void OnChatReceived(object? sender, ChatMessageData msg)
    {
        Dispatcher.Invoke(() =>
        {
            ChatEntries.Add(new ChatEntry(msg.SenderName, msg.Text));
            ChatMessages.ScrollIntoView(ChatEntries[^1]);
        });
    }

    private void ChatToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _chatVisible = !_chatVisible;
        ChatColumn.Width = _chatVisible ? new GridLength(280) : new GridLength(0);
        ChatPanel.Visibility = _chatVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ChatSendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendChatMessageAsync();
    }

    private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SendChatMessageAsync();
            e.Handled = true;
        }
    }

    private async Task SendChatMessageAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(ChatInput.Text)) return;
        var text = ChatInput.Text.Trim();
        ChatInput.Clear();

        ChatEntries.Add(new ChatEntry("Admin", text));
        ChatMessages.ScrollIntoView(ChatEntries[^1]);

        await _client.SendChatAsync("Admin", text);
    }

    // ---- File Transfer ----

    private async void FileTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Select file to send to remote machine",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            var stream = _client.GetStream();
            if (stream is null) return;

            var transferService = new FileTransferService();
            try
            {
                StatusText.Text = $"Sending {Path.GetFileName(dialog.FileName)}...";
                SetStatusColor("#f9e2af");

                await transferService.SendFileAsync(stream, dialog.FileName, null);

                StatusText.Text = "File sent successfully!";
                SetStatusColor("#a6e3a1");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Transfer failed: {ex.Message}";
                SetStatusColor("#f38ba8");
            }
        }
    }

    // ---- Node list interaction ----

    private async void NodeList_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_client is null || NodeList.SelectedItem is not HostInfoMessage selectedHost) return;
        await _client.ConnectToHostAsync(selectedHost.MachineId);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null)
            await _client.RequestHostListAsync();
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
        MonitorSelector.Visibility = Visibility.Collapsed;
        MonitorSelector.Items.Clear();
    }

    // ---- Input forwarding ----

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
            await _client.DisposeAsync();
    }
}

// Helper types for UI bindings
public record ChatEntry(string Sender, string Text);
public record MonitorComboItem(int Index, string Label)
{
    public override string ToString() => Label;
}
