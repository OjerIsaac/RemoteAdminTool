using System.Collections.ObjectModel;
using System.IO;
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
    private double _monitorPanelWidth; // cached to calculate bar widths

    // View-model collections
    public ObservableCollection<HostInfoMessage> Nodes { get; } = new();
    public ObservableCollection<ChatEntry> ChatEntries { get; } = new();
    public ObservableCollection<TenantViewModel> Tenants { get; } = new();
    public ObservableCollection<ProcessInfoData> ProcessItems { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        NodeList.ItemsSource = Nodes;
        ChatMessages.ItemsSource = ChatEntries;
        TenantList.ItemsSource = Tenants;
        ProcessList.ItemsSource = ProcessItems;

        // Cache monitor panel width when it changes size
        MonitorPanel.SizeChanged += (_, e) => _monitorPanelWidth = e.NewSize.Width;

        // RelayUrl is a compiled constant — set by build.ps1 from .env
        RelayUrlBox.Text = ViewerConstants.RelayUrl;
    }

    // ---- Connection to Relay ----

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
            Nodes.Clear();
            Tenants.Clear();
            ConnectButton.Content = "Connect to Relay";
            StatusText.Text = "Disconnected";
            SetStatusColor("#f38ba8");
            // Hide super-admin-only UI
            TabTenantsButton.Visibility = Visibility.Collapsed;
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
        _client.TenantListReceived += OnTenantListReceived;
        _client.TenantCreated += OnTenantCreated;
        _client.ShellOutputReceived += OnShellOutputReceived;
        _client.ShellStarted += OnShellStarted;
        _client.ShellEnded += OnShellEnded;
        _client.SystemStatsReceived += OnSystemStatsReceived;

        try
        {
            StatusText.Text = "Connecting...";
            SetStatusColor("#f9e2af");

            var licenseKey = LicenseKeyBox.Text.Trim();
            var (accepted, reason, isSuperAdmin) = await _client.ConnectAsync(
                RelayUrlBox.Text, AdminTokenBox.Password, licenseKey);

            if (!accepted)
            {
                StatusText.Text = $"Rejected: {reason}";
                SetStatusColor("#f38ba8");
                await _client.DisposeAsync();
                _client = null;
                return;
            }

            StatusText.Text = isSuperAdmin ? "Connected (Super Admin)" : "Connected (Sub-Admin)";
            SetStatusColor("#a6e3a1");
            ConnectButton.Content = "Connect to Relay";

            // Switch sidebar to logged-in state
            LoginForm.Visibility = Visibility.Collapsed;
            LoggedInBar.Visibility = Visibility.Visible;
            LoggedInLabel.Text = isSuperAdmin ? "Super Admin" : "Sub-Admin";
            LoggedInRole.Text = isSuperAdmin ? "Full access" : "Tenant access";
            if (isSuperAdmin)
            {
                TabTenantsButton.Visibility = Visibility.Visible;
                // Auto-load tenant list
                await _client.RequestTenantListAsync();
            }
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

    // ---- Tab switching ----

    private void TabNodesButton_Click(object sender, RoutedEventArgs e)
    {
        NodePanel.Visibility = Visibility.Visible;
        TenantPanel.Visibility = Visibility.Collapsed;
        TabNodesButton.BorderBrush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#a6e3a1"));
        TabNodesButton.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#a6e3a1"));
        TabTenantsButton.BorderBrush = System.Windows.Media.Brushes.Transparent;
        TabTenantsButton.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6c7086"));
    }

    private async void TabTenantsButton_Click(object sender, RoutedEventArgs e)
    {
        NodePanel.Visibility = Visibility.Collapsed;
        TenantPanel.Visibility = Visibility.Visible;
        TabTenantsButton.BorderBrush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#cba6f7"));
        TabTenantsButton.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#cba6f7"));
        TabNodesButton.BorderBrush = System.Windows.Media.Brushes.Transparent;
        TabNodesButton.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6c7086"));

        // Refresh tenant list
        if (_client is not null)
            await _client.RequestTenantListAsync();
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
                Nodes.Add(new HostInfoMessage(msg.MachineId, msg.MachineName, "", true, DateTime.UtcNow, msg.TenantId));
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

    // ---- Tenant events (super admin only) ----

    private void OnTenantListReceived(object? sender, TenantInfoMessage[] tenants)
    {
        Dispatcher.Invoke(() =>
        {
            Tenants.Clear();
            foreach (var t in tenants)
                Tenants.Add(new TenantViewModel(t));
        });
    }

    private void OnTenantCreated(object? sender, CreateTenantAckMessage ack)
    {
        Dispatcher.Invoke(() =>
        {
            if (!ack.Success)
            {
                MessageBox.Show($"Failed to create tenant: {ack.Error}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Show copyable credentials dialog instead of a plain MessageBox
            TenantCredentialsDialog.ShowNewTenant(this,
                ack.TenantId!, ack.LicenseKey!, ack.AdminToken!, ack.HostToken ?? "");

            // Refresh the tenant list so the new entry appears
            _ = _client?.RequestTenantListAsync();
        });
    }

    private async void CreateTenantButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || !_client.IsSuperAdmin) return;

        var dialog = new CreateTenantDialog();
        if (dialog.ShowDialog() == true)
        {
            await _client.CreateTenantAsync(dialog.DisplayName, dialog.AdminEmail);
        }
    }

    private void TenantList_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Clicking a tenant could filter the node list — future enhancement
        // For now, just switch to Nodes tab showing all hosts (super admin already sees all)
        if (TenantList.SelectedItem is TenantViewModel selectedTenant)
        {
            TabNodesButton_Click(sender, new RoutedEventArgs());
        }
    }

    private async void SuspendTenantButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || !_client.IsSuperAdmin) return;
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tenantId)
        {
            var tenant = Tenants.FirstOrDefault(t => t.TenantId == tenantId);
            var action = tenant?.IsActive == true ? "suspend" : "resume";
            var result = MessageBox.Show(
                $"Are you sure you want to {action} '{tenant?.DisplayName}'?\nTheir hosts will be disconnected from the relay.",
                $"{char.ToUpper(action[0]) + action[1..]} Tenant",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                if (tenant?.IsActive == true)
                    await _client.SuspendTenantAsync(tenantId);
                else
                    await _client.ResumeTenantAsync(tenantId);
                await _client.RequestTenantListAsync();
            }
        }
    }

    private void ViewTenantDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is TenantViewModel vm)
        {
            TenantCredentialsDialog.ShowExisting(this,
                vm.DisplayName, vm.TenantId, vm.LicenseKey, vm.AdminToken, vm.HostToken);
        }
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
            // Restore login form
            LoginForm.Visibility = Visibility.Visible;
            LoggedInBar.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Disconnected: {reason}";
            SetStatusColor("#f38ba8");
            ConnectButton.Content = "Connect to Relay";
            Nodes.Clear();
            Tenants.Clear();
            TabTenantsButton.Visibility = Visibility.Collapsed;
            EndSessionUI();
            _client = null;
        });
    }

    // ---- Logout ----

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        LoginForm.Visibility = Visibility.Visible;
        LoggedInBar.Visibility = Visibility.Collapsed;
        StatusText.Text = "Not connected";
        SetStatusColor("#f38ba8");
        Nodes.Clear();
        Tenants.Clear();
        TabTenantsButton.Visibility = Visibility.Collapsed;
        EndSessionUI();
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
                await _client.SendClipboardAsync(text);
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

    private async void ChatSendButton_Click(object sender, RoutedEventArgs e) => await SendChatMessageAsync();

    private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { await SendChatMessageAsync(); e.Handled = true; }
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
        // Hide console/monitor panels
        ConsolePanel.Visibility = Visibility.Collapsed;
        MonitorPanel.Visibility = Visibility.Collapsed;
        RemoteScreenImage.Visibility = Visibility.Visible;
        ConsoleOutput.Text = string.Empty;
        ProcessItems.Clear();
    }

    // ---- Session tab switching ----

    private void TabScreenBtn_Click(object sender, RoutedEventArgs e)  => SetSessionTab(0);
    private void TabConsoleBtn_Click(object sender, RoutedEventArgs e)
    {
        SetSessionTab(1);
        // Kick off the shell session on first open
        if (_client is not null && ConsoleOutput.Text.Length == 0)
            _ = _client.SendShellInputAsync("Write-Host 'Shell ready'");
    }
    private void TabMonitorBtn_Click(object sender, RoutedEventArgs e)
    {
        SetSessionTab(2);
        _ = _client?.SendStatsRequestAsync(true);
    }

    private void SetSessionTab(int tab)
    {
        RemoteScreenImage.Visibility  = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConsolePanel.Visibility       = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
        MonitorPanel.Visibility       = tab == 2 ? Visibility.Visible : Visibility.Collapsed;

        // Highlight active tab button
        var activeStyle  = "#89b4fa";
        var inactiveBack = "Transparent";
        void Style(System.Windows.Controls.Button btn, bool active)
        {
            btn.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    active ? activeStyle : inactiveBack));
            btn.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    active ? "#1e1e2e" : "#cdd6f4"));
            btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
        Style(TabScreenBtn,  tab == 0);
        Style(TabConsoleBtn, tab == 1);
        Style(TabMonitorBtn, tab == 2);

        // Stop stats when leaving monitor tab
        if (tab != 2) _ = _client?.SendStatsRequestAsync(false);
    }

    // ---- Console handlers ----

    private void OnShellStarted(object? sender, EventArgs e)
        => Dispatcher.Invoke(() => AppendConsoleText("[Shell ready — type commands below]\n", isError: false));

    private void OnShellEnded(object? sender, EventArgs e)
        => Dispatcher.Invoke(() => AppendConsoleText("\n[Shell session ended]\n", isError: true));

    private void OnShellOutputReceived(object? sender, ShellOutputMessage msg)
        => Dispatcher.Invoke(() => AppendConsoleText(msg.Text + "\n", msg.IsError));

    private void AppendConsoleText(string text, bool isError)
    {
        // Simple append — TextBox does not support per-run coloring, so errors get a prefix
        ConsoleOutput.AppendText(isError ? $"[ERR] {text}" : text);
        ConsoleScroller.ScrollToBottom();
    }

    private async void ConsoleRunBtn_Click(object sender, RoutedEventArgs e)
        => await SendConsoleCommandAsync();

    private async void ConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SendConsoleCommandAsync();
    }

    private async Task SendConsoleCommandAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(ConsoleInput.Text)) return;
        var cmd = ConsoleInput.Text.Trim();
        ConsoleInput.Clear();
        AppendConsoleText($"PS> {cmd}\n", isError: false);
        await _client.SendShellInputAsync(cmd);
    }

    // ---- Monitor handlers ----

    private void OnSystemStatsReceived(object? sender, SystemStatsMessage stats)
    {
        Dispatcher.Invoke(() =>
        {
            var panelW = _monitorPanelWidth > 0 ? _monitorPanelWidth - 32 : 400;

            CpuLabel.Text = $"{stats.CpuPercent:F1}%";
            CpuBar.Width  = panelW * (stats.CpuPercent / 100.0);

            double ramPct = stats.RamTotalMb > 0 ? (double)stats.RamUsedMb / stats.RamTotalMb : 0;
            RamLabel.Text = $"{stats.RamUsedMb:N0} / {stats.RamTotalMb:N0} MB";
            RamBar.Width  = panelW * ramPct;

            DiskReadLabel.Text  = $"{stats.DiskReadMbps:F2} MB/s";
            DiskWriteLabel.Text = $"{stats.DiskWriteMbps:F2} MB/s";
            NetSentLabel.Text   = $"{stats.NetSentMbps:F2} MB/s";
            NetRecvLabel.Text   = $"{stats.NetRecvMbps:F2} MB/s";

            ProcessItems.Clear();
            foreach (var p in stats.TopProcesses)
                ProcessItems.Add(p);
        });
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

// ---- Helper types ----

public record ChatEntry(string Sender, string Text);

public record MonitorComboItem(int Index, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// ViewModel wrapping TenantInfoMessage for the Tenants list in the UI.
/// </summary>
public class TenantViewModel
{
    public string TenantId       { get; }
    public string DisplayName    { get; }
    public string AdminEmail     { get; }
    public int    OnlineNodeCount { get; }
    public bool   IsActive       { get; }
    public string LicenseKey     { get; }
    public string AdminToken     { get; }
    public string HostToken      { get; }
    public string StatusLabel => IsActive ? "Active" : "Suspended";

    public TenantViewModel(TenantInfoMessage msg)
    {
        TenantId        = msg.TenantId;
        DisplayName     = msg.DisplayName;
        AdminEmail      = msg.AdminEmail;
        OnlineNodeCount = msg.OnlineNodeCount;
        IsActive        = msg.IsActive;
        LicenseKey      = msg.LicenseKey;
        AdminToken      = msg.AdminToken;
        HostToken       = msg.HostToken;
    }
}
