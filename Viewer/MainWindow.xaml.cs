using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    private double _monitorPanelWidth;

    // Auto-reconnect
    private string? _savedRelayUrl;
    private string? _savedAdminToken;
    private string? _savedLicenseKey;
    private bool _autoReconnecting;

    // Terminal history
    private readonly List<string> _consoleHistory = new();
    private int _historyIndex = -1;

    // File manager
    private string _currentRemotePath = "\\";

    // View-model collections
    public ObservableCollection<HostInfoMessage> Nodes { get; } = new();
    public ObservableCollection<ChatEntry> ChatEntries { get; } = new();
    public ObservableCollection<TenantViewModel> Tenants { get; } = new();
    public ObservableCollection<ProcessInfoData> ProcessItems { get; } = new();
    public ObservableCollection<RemoteFileEntry> RemoteFiles { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        NodeList.ItemsSource    = Nodes;
        ChatMessages.ItemsSource = ChatEntries;
        TenantList.ItemsSource  = Tenants;
        ProcessList.ItemsSource = ProcessItems;
        RemoteFileList.ItemsSource = RemoteFiles;

        MonitorPanel.SizeChanged += (_, e) => _monitorPanelWidth = e.NewSize.Width;
        RelayUrlBox.Text = ViewerConstants.RelayUrl;
    }

    // ---- Connection ----

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
            Nodes.Clear(); Tenants.Clear();
            ConnectButton.Content = "Connect to Relay";
            StatusText.Text = "Disconnected";
            SetStatusColor("#f38ba8");
            TabTenantsButton.Visibility = Visibility.Collapsed;
            return;
        }

        await ConnectInternalAsync(RelayUrlBox.Text, AdminTokenBox.Password, LicenseKeyBox.Text.Trim());
    }

    private async Task ConnectInternalAsync(string relayUrl, string adminToken, string licenseKey)
    {
        _savedRelayUrl   = relayUrl;
        _savedAdminToken = adminToken;
        _savedLicenseKey = licenseKey;

        _client = new ViewerClient();
        _client.HostListReceived    += OnHostListReceived;
        _client.HostCameOnline      += OnHostCameOnline;
        _client.HostWentOffline     += OnHostWentOffline;
        _client.SessionStarted      += OnSessionStarted;
        _client.FrameReceived       += OnFrameReceived;
        _client.ClipboardReceived   += OnClipboardReceived;
        _client.MonitorListReceived += OnMonitorListReceived;
        _client.ChatReceived        += OnChatReceived;
        _client.Disconnected        += OnDisconnected;
        _client.TenantListReceived  += OnTenantListReceived;
        _client.TenantCreated       += OnTenantCreated;
        _client.ShellOutputReceived += OnShellOutputReceived;
        _client.ShellStarted        += OnShellStarted;
        _client.ShellEnded          += OnShellEnded;
        _client.SystemStatsReceived += OnSystemStatsReceived;
        _client.DeviceInfoReceived  += OnDeviceInfoReceived;
        _client.DirectoryListReceived += OnDirectoryListReceived;

        try
        {
            StatusText.Text = "Connecting...";
            SetStatusColor("#f9e2af");

            var (accepted, reason, isSuperAdmin) = await _client.ConnectAsync(relayUrl, adminToken, licenseKey);

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

            LoginForm.Visibility  = Visibility.Collapsed;
            LoggedInBar.Visibility = Visibility.Visible;
            LoggedInLabel.Text = isSuperAdmin ? "Super Admin" : "Sub-Admin";
            LoggedInRole.Text  = isSuperAdmin ? "Full access" : "Tenant access";
            _autoReconnecting  = false;

            if (isSuperAdmin)
            {
                TabTenantsButton.Visibility = Visibility.Visible;
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
        StatusText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(hex));
    }

    // ---- Auto-reconnect ----

    private void OnDisconnected(object? sender, string reason)
    {
        Dispatcher.Invoke(async () =>
        {
            EndSessionUI();

            if (!string.IsNullOrEmpty(_savedAdminToken) && !_autoReconnecting)
            {
                _autoReconnecting = true;
                StatusText.Text = "Reconnecting...";
                SetStatusColor("#f9e2af");

                await Task.Delay(4000); // brief wait before retry

                int attempts = 0;
                while (_autoReconnecting && attempts < 20)
                {
                    attempts++;
                    try
                    {
                        StatusText.Text = $"Reconnecting... ({attempts})";
                        await ConnectInternalAsync(_savedRelayUrl!, _savedAdminToken!, _savedLicenseKey ?? "");
                        if (_client is not null) return; // success
                    }
                    catch { }
                    await Task.Delay(5000);
                }

                // Give up after 20 attempts
                _autoReconnecting = false;
                LoginForm.Visibility   = Visibility.Visible;
                LoggedInBar.Visibility = Visibility.Collapsed;
                StatusText.Text = $"Disconnected: {reason}";
                SetStatusColor("#f38ba8");
                Nodes.Clear(); Tenants.Clear();
                TabTenantsButton.Visibility = Visibility.Collapsed;
                _client = null;
            }
            else
            {
                LoginForm.Visibility   = Visibility.Visible;
                LoggedInBar.Visibility = Visibility.Collapsed;
                StatusText.Text = $"Disconnected: {reason}";
                SetStatusColor("#f38ba8");
                Nodes.Clear(); Tenants.Clear();
                TabTenantsButton.Visibility = Visibility.Collapsed;
                _client = null;
            }
        });
    }

    // ---- Sidebar tab switching ----

    private void TabNodesButton_Click(object sender, RoutedEventArgs e)
    {
        NodePanel.Visibility   = Visibility.Visible;
        TenantPanel.Visibility = Visibility.Collapsed;
        TabNodesButton.BorderBrush  = ColorBrush("#a6e3a1");
        TabNodesButton.Foreground   = ColorBrush("#a6e3a1");
        TabTenantsButton.BorderBrush = Brushes.Transparent;
        TabTenantsButton.Foreground  = ColorBrush("#6c7086");
    }

    private async void TabTenantsButton_Click(object sender, RoutedEventArgs e)
    {
        NodePanel.Visibility   = Visibility.Collapsed;
        TenantPanel.Visibility = Visibility.Visible;
        TabTenantsButton.BorderBrush = ColorBrush("#cba6f7");
        TabTenantsButton.Foreground  = ColorBrush("#cba6f7");
        TabNodesButton.BorderBrush   = Brushes.Transparent;
        TabNodesButton.Foreground    = ColorBrush("#6c7086");
        if (_client is not null) await _client.RequestTenantListAsync();
    }

    private static SolidColorBrush ColorBrush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

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
            if (Nodes.All(n => n.MachineId != msg.MachineId))
                Nodes.Add(new HostInfoMessage(msg.MachineId, msg.MachineName, "", true, DateTime.UtcNow, msg.TenantId));
        });
    }

    private void OnHostWentOffline(object? sender, HostOfflineMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            for (int i = Nodes.Count - 1; i >= 0; i--)
                if (Nodes[i].MachineId == msg.MachineId) { Nodes.RemoveAt(i); break; }
            if (_activeSessionMachineId == msg.MachineId) EndSessionUI();
        });
    }

    // ---- Tenant events ----

    private void OnTenantListReceived(object? sender, TenantInfoMessage[] tenants)
    {
        Dispatcher.Invoke(() =>
        {
            Tenants.Clear();
            foreach (var t in tenants) Tenants.Add(new TenantViewModel(t));
        });
    }

    private void OnTenantCreated(object? sender, CreateTenantAckMessage ack)
    {
        Dispatcher.Invoke(() =>
        {
            if (!ack.Success)
            {
                MessageBox.Show($"Failed to create tenant: {ack.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            TenantCredentialsDialog.ShowNewTenant(this, ack.TenantId!, ack.LicenseKey!, ack.AdminToken!, ack.HostToken ?? "");
            _ = _client?.RequestTenantListAsync();
        });
    }

    private async void CreateTenantButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || !_client.IsSuperAdmin) return;
        var dialog = new CreateTenantDialog();
        if (dialog.ShowDialog() == true)
            await _client.CreateTenantAsync(dialog.DisplayName, dialog.AdminEmail);
    }

    private void TenantList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TenantList.SelectedItem is TenantViewModel)
            TabNodesButton_Click(sender, new RoutedEventArgs());
    }

    private async void SuspendTenantButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || !_client.IsSuperAdmin) return;
        if (sender is Button btn && btn.Tag is string tenantId)
        {
            var tenant = Tenants.FirstOrDefault(t => t.TenantId == tenantId);
            var action = tenant?.IsActive == true ? "suspend" : "resume";
            var result = MessageBox.Show(
                $"Are you sure you want to {action} '{tenant?.DisplayName}'?",
                $"{char.ToUpper(action[0]) + action[1..]} Tenant",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                if (tenant?.IsActive == true) await _client.SuspendTenantAsync(tenantId);
                else await _client.ResumeTenantAsync(tenantId);
                await _client.RequestTenantListAsync();
            }
        }
    }

    private void ViewTenantDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TenantViewModel vm)
            TenantCredentialsDialog.ShowExisting(this, vm.DisplayName, vm.TenantId, vm.LicenseKey, vm.AdminToken, vm.HostToken);
    }

    // ---- Session events ----

    private void OnSessionStarted(object? sender, SessionStartedMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            _activeSessionMachineId = msg.MachineId;
            _remoteWidth  = msg.ScreenWidth;
            _remoteHeight = msg.ScreenHeight;
            SessionHostName.Text = $"  {msg.MachineName}";
            ConnectingOverlay.Visibility      = Visibility.Collapsed;  // hide spinner
            SessionToolbar.Visibility         = Visibility.Visible;
            NoSessionPlaceholder.Visibility   = Visibility.Collapsed;
            RemoteScreenImage.Focus();
            SetSessionTab(0);
            // Auto-browse remote files root on connection
            _currentRemotePath = "\\";
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
                _remoteWidth  = bitmap.PixelWidth;
                _remoteHeight = bitmap.PixelHeight;
            }
        });
    }

    // ---- Logout ----

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _autoReconnecting = false;
        _savedAdminToken  = null;
        if (_client is not null) { await _client.DisposeAsync(); _client = null; }
        LoginForm.Visibility   = Visibility.Visible;
        LoggedInBar.Visibility = Visibility.Collapsed;
        StatusText.Text = "Not connected";
        SetStatusColor("#f38ba8");
        Nodes.Clear(); Tenants.Clear();
        TabTenantsButton.Visibility = Visibility.Collapsed;
        EndSessionUI();
    }

    // ---- Clipboard ----

    private void OnClipboardReceived(object? sender, string text)
        => Dispatcher.Invoke(() => { try { Clipboard.SetText(text); } catch { } });

    private async void ClipboardSyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        try { var text = Clipboard.GetText(); if (!string.IsNullOrEmpty(text)) await _client.SendClipboardAsync(text); }
        catch { }
    }

    // ---- Monitor selector ----

    private void OnMonitorListReceived(object? sender, MonitorInfoData[] monitors)
    {
        Dispatcher.Invoke(() =>
        {
            MonitorSelector.Items.Clear();
            foreach (var mon in monitors)
            {
                var label = $"{mon.Name} ({mon.Width}x{mon.Height}){(mon.IsPrimary ? " *" : "")}";
                MonitorSelector.Items.Add(new MonitorComboItem(mon.Index, label));
            }
            if (MonitorSelector.Items.Count > 0) { MonitorSelector.SelectedIndex = 0; MonitorSelector.Visibility = Visibility.Visible; }
        });
    }

    private async void MonitorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_client is null || MonitorSelector.SelectedItem is not MonitorComboItem item) return;
        await _client.SelectMonitorAsync(item.Index);
    }

    // ---- Chat ----

    private void OnChatReceived(object? sender, ChatMessageData msg)
        => Dispatcher.Invoke(() => { ChatEntries.Add(new ChatEntry(msg.SenderName, msg.Text)); ChatMessages.ScrollIntoView(ChatEntries[^1]); });

    private void ChatToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _chatVisible = !_chatVisible;
        ChatColumn.Width = _chatVisible ? new GridLength(280) : new GridLength(0);
        ChatPanel.Visibility = _chatVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ChatSendButton_Click(object sender, RoutedEventArgs e) => await SendChatMessageAsync();
    private async void ChatInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { await SendChatMessageAsync(); e.Handled = true; } }

    private async Task SendChatMessageAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(ChatInput.Text)) return;
        var text = ChatInput.Text.Trim(); ChatInput.Clear();
        ChatEntries.Add(new ChatEntry("Admin", text));
        ChatMessages.ScrollIntoView(ChatEntries[^1]);
        await _client.SendChatAsync("Admin", text);
    }

    // ---- Node list ----

    private async void NodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_client is null || NodeList.SelectedItem is not HostInfoMessage selectedHost) return;
        // Show connecting overlay immediately so the user knows something is happening
        ConnectingLabel.Text      = $"Connecting to {selectedHost.MachineName}...";
        ConnectingOverlay.Visibility = Visibility.Visible;
        await _client.ConnectToHostAsync(selectedHost.MachineId);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null) await _client.RequestHostListAsync();
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
        ConnectingOverlay.Visibility    = Visibility.Collapsed;  // never get stuck
        SessionToolbar.Visibility       = Visibility.Collapsed;
        NoSessionPlaceholder.Visibility = Visibility.Visible;
        MonitorSelector.Visibility = Visibility.Collapsed;
        MonitorSelector.Items.Clear();
        // Hide all session tabs
        ScreenTab.Visibility    = Visibility.Collapsed;
        ConsolePanel.Visibility = Visibility.Collapsed;
        CodePanel.Visibility    = Visibility.Collapsed;
        FilesPanel.Visibility   = Visibility.Collapsed;
        DevicePanel.Visibility  = Visibility.Collapsed;
        MonitorPanel.Visibility = Visibility.Collapsed;
        RemoteScreenImage.Visibility = Visibility.Visible;
        ScreenTab.Visibility    = Visibility.Visible;
        ConsoleOutput.Document.Blocks.Clear();
        CodeOutput.Document.Blocks.Clear();
        ProcessItems.Clear();
        RemoteFiles.Clear();
        SilentModeCheckBox.IsChecked = false;
    }

    // ---- Session tab switching ----

    private void TabScreenBtn_Click(object sender, RoutedEventArgs e)  => SetSessionTab(0);
    private void TabConsoleBtn_Click(object sender, RoutedEventArgs e)
    {
        SetSessionTab(1);
        if (_client is not null && new TextRange(ConsoleOutput.Document.ContentStart, ConsoleOutput.Document.ContentEnd).Text.Length < 5)
            _ = _client.SendShellInputAsync("echo Shell ready");
    }
    private void TabCodeBtn_Click(object sender, RoutedEventArgs e)    => SetSessionTab(2);
    private void TabFilesBtn_Click(object sender, RoutedEventArgs e)
    {
        SetSessionTab(3);
        _ = _client?.RequestDirectoryAsync(_currentRemotePath);
    }
    private void TabDeviceBtn_Click(object sender, RoutedEventArgs e)  => SetSessionTab(4);
    private void TabMonitorBtn_Click(object sender, RoutedEventArgs e)
    {
        SetSessionTab(5);
        _ = _client?.SendStatsRequestAsync(true);
    }

    private void SetSessionTab(int tab)
    {
        ScreenTab.Visibility    = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConsolePanel.Visibility = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
        CodePanel.Visibility    = tab == 2 ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility   = tab == 3 ? Visibility.Visible : Visibility.Collapsed;
        DevicePanel.Visibility  = tab == 4 ? Visibility.Visible : Visibility.Collapsed;
        MonitorPanel.Visibility = tab == 5 ? Visibility.Visible : Visibility.Collapsed;

        // Stop stats when leaving monitor tab
        if (tab != 5) _ = _client?.SendStatsRequestAsync(false);

        // Update tab button highlight
        var buttons = new[] { TabScreenBtn, TabConsoleBtn, TabCodeBtn, TabFilesBtn, TabDeviceBtn, TabMonitorBtn };
        for (int i = 0; i < buttons.Length; i++)
        {
            bool active = i == tab;
            buttons[i].Background  = active ? ColorBrush("#89b4fa") : ColorBrush("Transparent");
            buttons[i].Foreground  = active ? ColorBrush("#1e1e2e") : ColorBrush("#cdd6f4");
            buttons[i].FontWeight  = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        // Update shell prompt label for cmd vs powershell
        if (tab == 1)
        {
            var isCmdShell = ShellTypeCombo.SelectedIndex == 1;
            var promptBlock = FindName("ConsolePromptLabel") as TextBlock;
            // prompt label text is in XAML as "PS>" - updated dynamically below
        }
    }

    // ---- Terminal (RichTextBox) ----

    private void OnShellStarted(object? sender, EventArgs e)
        => Dispatcher.Invoke(() => AppendTerminalText("[Shell ready]\n", isError: false));

    private void OnShellEnded(object? sender, EventArgs e)
        => Dispatcher.Invoke(() => AppendTerminalText("\n[Shell ended]\n", isError: true));

    private void OnShellOutputReceived(object? sender, ShellOutputMessage msg)
        => Dispatcher.Invoke(() =>
        {
            // Route output to whichever panel is showing (terminal or code)
            if (CodePanel.IsVisible)
                AppendRichText(CodeOutput, msg.Text + "\n", msg.IsError ? "#f38ba8" : "#a6e3a1");
            else
                AppendTerminalText(msg.Text + "\n", msg.IsError);
        });

    private void AppendTerminalText(string text, bool isError)
    {
        AppendRichText(ConsoleOutput, text, isError ? "#f38ba8" : "#cdd6f4");
        ConsoleScroller.ScrollToBottom();
    }

    private static void AppendRichText(RichTextBox rtb, string text, string colorHex)
    {
        var para = new Paragraph(new Run(text))
        {
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
            Margin     = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 13
        };
        rtb.Document.Blocks.Add(para);
    }

    private async void ConsoleRunBtn_Click(object sender, RoutedEventArgs e) => await SendConsoleCommandAsync();

    private async void ConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SendConsoleCommandAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_consoleHistory.Count > 0)
            {
                _historyIndex = Math.Min(_historyIndex + 1, _consoleHistory.Count - 1);
                ConsoleInput.Text = _consoleHistory[_consoleHistory.Count - 1 - _historyIndex];
                ConsoleInput.CaretIndex = ConsoleInput.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                ConsoleInput.Text = _consoleHistory[_consoleHistory.Count - 1 - _historyIndex];
                ConsoleInput.CaretIndex = ConsoleInput.Text.Length;
            }
            else
            {
                _historyIndex = -1;
                ConsoleInput.Clear();
            }
            e.Handled = true;
        }
    }

    private async Task SendConsoleCommandAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(ConsoleInput.Text)) return;
        var cmd = ConsoleInput.Text.Trim();
        ConsoleInput.Clear();
        _historyIndex = -1;
        _consoleHistory.Add(cmd);

        var isCmd = ShellTypeCombo.SelectedIndex == 1;
        var prompt = isCmd ? "CMD> " : "PS> ";
        AppendTerminalText($"{prompt}{cmd}\n", isError: false);
        await _client.SendShellInputAsync(cmd);
    }

    private void ConsoleClearBtn_Click(object sender, RoutedEventArgs e)
        => ConsoleOutput.Document.Blocks.Clear();

    private async void ShellTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Tell host to restart with the new shell type
        if (_client is null) return;
        var shellType = ShellTypeCombo.SelectedIndex == 1 ? "cmd" : "powershell";
        // We signal shell type change by sending a special restart command
        // The host will pick up the new shell on next startup
        await _client.SendShellInputAsync($"##SHELL_TYPE:{shellType}");
    }

    // ---- Code Editor ----

    private async void CodeRunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || string.IsNullOrWhiteSpace(CodeEditor.Text)) return;

        CodeOutput.Document.Blocks.Clear();
        AppendRichText(CodeOutput, "Running script...\n", "#6c7086");

        var script = CodeEditor.Text;
        var isPs   = CodeLanguageCombo.SelectedIndex == 0;

        if (isPs)
        {
            // Wrap multi-line PS in a scriptblock and send as one block
            var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    await _client.SendShellInputAsync(trimmed);
            }
        }
        else
        {
            // Batch: write to temp file and execute
            var tempName = $"_rat_script_{Guid.NewGuid():N}.bat";
            var tempPath = Path.Combine(Path.GetTempPath(), tempName);
            await _client.SendShellInputAsync($"$env:TEMP\\{tempName} | Out-Null; Set-Content '{tempPath}' @'\n{script}\n'@; cmd /c '{tempPath}'; Remove-Item '{tempPath}' -EA SilentlyContinue");
        }
    }

    private void CodeClearOutputBtn_Click(object sender, RoutedEventArgs e)
        => CodeOutput.Document.Blocks.Clear();

    // ---- File Manager ----

    private void OnDirectoryListReceived(object? sender, DirectoryListResponseMessage resp)
    {
        Dispatcher.Invoke(() =>
        {
            RemoteFiles.Clear();
            RemotePathBox.Text = resp.Path;
            _currentRemotePath = resp.Path;

            if (!string.IsNullOrEmpty(resp.Error))
            {
                MessageBox.Show($"Cannot open directory:\n{resp.Error}", "File Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Add ".." up entry unless we're at root
            if (resp.Path != "\\")
                RemoteFiles.Add(new RemoteFileEntry { Name = "..", FullPath = "..", IsDirectory = true, TypeLabel = "Folder", SizeLabel = "", LastModified = "" });

            foreach (var entry in resp.Entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name))
            {
                RemoteFiles.Add(new RemoteFileEntry
                {
                    Name         = entry.Name,
                    FullPath     = entry.FullPath,
                    IsDirectory  = entry.IsDirectory,
                    TypeLabel    = entry.IsDirectory ? "Folder" : Path.GetExtension(entry.Name).TrimStart('.').ToUpper(),
                    SizeLabel    = entry.IsDirectory ? "" : FormatSize(entry.SizeBytes),
                    LastModified = entry.LastModified == DateTime.MinValue ? "" : entry.LastModified.ToString("yyyy-MM-dd HH:mm")
                });
            }
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1_048_576) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1_073_741_824) return $"{bytes / 1_048_576.0:F1} MB";
        return $"{bytes / 1_073_741_824.0:F1} GB";
    }

    private void RemoteFileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RemoteFileList.SelectedItem is not RemoteFileEntry entry) return;
        if (!entry.IsDirectory) return;

        if (entry.Name == "..")
        {
            // Navigate up
            var parent = Path.GetDirectoryName(_currentRemotePath.TrimEnd('\\'));
            _currentRemotePath = string.IsNullOrEmpty(parent) ? "\\" : parent;
        }
        else
        {
            _currentRemotePath = entry.FullPath;
        }
        _ = _client?.RequestDirectoryAsync(_currentRemotePath);
    }

    private void RemotePathGoBtn_Click(object sender, RoutedEventArgs e)
        => _ = _client?.RequestDirectoryAsync(RemotePathBox.Text);

    private void RemotePathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = _client?.RequestDirectoryAsync(RemotePathBox.Text);
    }

    private void RemotePathUpBtn_Click(object sender, RoutedEventArgs e)
    {
        var parent = Path.GetDirectoryName(_currentRemotePath.TrimEnd('\\'));
        _currentRemotePath = string.IsNullOrEmpty(parent) ? "\\" : parent;
        _ = _client?.RequestDirectoryAsync(_currentRemotePath);
    }

    private async void UploadToHostBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        var dialog = new OpenFileDialog { Title = "Select file to upload to host", Multiselect = false };
        if (dialog.ShowDialog() != true) return;

        TransferProgressBar.Visibility = Visibility.Visible;
        TransferStatusText.Text = $"Uploading {Path.GetFileName(dialog.FileName)}...";

        try
        {
            var progress = new Progress<double>(p => Dispatcher.Invoke(() => TransferProgress.Value = p * 100));
            await _client.SendFileToHostAsync(dialog.FileName, _currentRemotePath, progress);
            TransferStatusText.Text = "Upload complete.";
            // Refresh listing
            _ = _client.RequestDirectoryAsync(_currentRemotePath);
        }
        catch (Exception ex)
        {
            TransferStatusText.Text = $"Upload failed: {ex.Message}";
        }
        finally
        {
            await Task.Delay(3000);
            TransferProgressBar.Visibility = Visibility.Collapsed;
            TransferProgress.Value = 0;
        }
    }

    private async void DownloadFromHostBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || RemoteFileList.SelectedItem is not RemoteFileEntry entry || entry.IsDirectory) return;

        var dialog = new WinForms.FolderBrowserDialog { Description = "Choose save location" };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        TransferProgressBar.Visibility = Visibility.Visible;
        TransferStatusText.Text = $"Downloading {entry.Name}...";

        try
        {
            var progress = new Progress<double>(p => Dispatcher.Invoke(() => TransferProgress.Value = p * 100));
            var savedPath = await _client.DownloadFileFromHostAsync(entry.FullPath, dialog.SelectedPath, progress);
            TransferStatusText.Text = $"Saved to {savedPath}";
        }
        catch (Exception ex)
        {
            TransferStatusText.Text = $"Download failed: {ex.Message}";
        }
        finally
        {
            await Task.Delay(3000);
            TransferProgressBar.Visibility = Visibility.Collapsed;
            TransferProgress.Value = 0;
        }
    }

    private async void DeleteRemoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || RemoteFileList.SelectedItem is not RemoteFileEntry entry) return;
        var result = MessageBox.Show($"Delete '{entry.Name}' on the remote machine?", "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        await _client.DeleteRemotePathAsync(entry.FullPath);
        await Task.Delay(500);
        _ = _client.RequestDirectoryAsync(_currentRemotePath);
    }

    // ---- Device Info ----

    private void OnDeviceInfoReceived(object? sender, DeviceInfoMessage info)
    {
        Dispatcher.Invoke(() =>
        {
            DevMachineName.Text  = info.MachineName;
            DevUsername.Text     = $"{info.Domain}\\{info.Username}";
            DevOsVersion.Text    = info.OsVersion;
            DevOsBuild.Text      = $"Build {info.OsBuild}  |  {info.Timezone}";
            DevUptime.Text       = FormatUptime(info.UptimeSeconds);
            DevCpu.Text          = info.CpuModel;
            DevCores.Text        = $"{info.CpuCores} cores";
            DevRam.Text          = $"{info.RamTotalMb / 1024.0:F1} GB";
            DevSystemDrive.Text  = info.SystemDrive;
            DevDriveFree.Text    = $"{info.SystemDriveFreeGb} GB free";
            DevDriveTotal.Text   = $"{info.SystemDriveTotalGb} GB total";
            DevIpAddresses.Text  = string.Join("  |  ", info.IpAddresses);
        });
    }

    private static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    // ---- Silent mode ----

    private async void SilentModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        await _client.SetSilentModeAsync(SilentModeCheckBox.IsChecked == true);
    }

    // ---- Monitor stats ----

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
            foreach (var p in stats.TopProcesses) ProcessItems.Add(p);
        });
    }

    // ---- Cursor hiding on remote screen ----

    private void RemoteScreenImage_MouseEnter(object sender, MouseEventArgs e)
    {
        RemoteScreenImage.Cursor = Cursors.None;
        CursorDot.Visibility = Visibility.Visible;
    }

    private void RemoteScreenImage_MouseLeave(object sender, MouseEventArgs e)
    {
        RemoteScreenImage.Cursor = Cursors.Arrow;
        CursorDot.Visibility = Visibility.Collapsed;
    }

    // ---- Input forwarding ----

    private (int X, int Y) ToRemoteCoords(MouseEventArgs e)
    {
        var pos = e.GetPosition(RemoteScreenImage);
        if (RemoteScreenImage.ActualWidth <= 0 || _remoteWidth <= 0) return (0, 0);
        var scaleX = _remoteWidth  / RemoteScreenImage.ActualWidth;
        var scaleY = _remoteHeight / RemoteScreenImage.ActualHeight;
        return ((int)(pos.X * scaleX), (int)(pos.Y * scaleY));
    }

    private async void RemoteScreenImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;
        var (x, y) = ToRemoteCoords(e);
        await _client.SendMouseMoveAsync(x, y);

        // Move crosshair dot
        var pos = e.GetPosition(CursorOverlay);
        Canvas.SetLeft(CursorDot, pos.X - 5);
        Canvas.SetTop(CursorDot,  pos.Y - 5);
    }

    private async void RemoteScreenImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;
        var (x, y) = ToRemoteCoords(e);
        var button = e.ChangedButton switch { MouseButton.Right => MouseButtonKind.Right, MouseButton.Middle => MouseButtonKind.Middle, _ => MouseButtonKind.Left };
        await _client.SendMouseButtonAsync(button, MouseButtonAction.Down, x, y);
    }

    private async void RemoteScreenImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;
        var (x, y) = ToRemoteCoords(e);
        var button = e.ChangedButton switch { MouseButton.Right => MouseButtonKind.Right, MouseButton.Middle => MouseButtonKind.Middle, _ => MouseButtonKind.Left };
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
        await _client.SendKeyAsync(KeyInterop.VirtualKeyFromKey(e.Key), KeyAction.Down);
    }

    private async void RemoteScreenImage_KeyUp(object sender, KeyEventArgs e)
    {
        if (_client is null || _activeSessionMachineId is null) return;
        await _client.SendKeyAsync(KeyInterop.VirtualKeyFromKey(e.Key), KeyAction.Up);
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _autoReconnecting = false;
        if (_client is not null) await _client.DisposeAsync();
    }
}

// ---- Helper types ----

public record ChatEntry(string Sender, string Text);

public record MonitorComboItem(int Index, string Label)
{
    public override string ToString() => Label;
}

public class RemoteFileEntry
{
    public string Name         { get; set; } = "";
    public string FullPath     { get; set; } = "";
    public bool   IsDirectory  { get; set; }
    public string TypeLabel    { get; set; } = "";
    public string SizeLabel    { get; set; } = "";
    public string LastModified { get; set; } = "";
}

public class TenantViewModel
{
    public string TenantId        { get; }
    public string DisplayName     { get; }
    public string AdminEmail      { get; }
    public int    OnlineNodeCount { get; }
    public bool   IsActive        { get; }
    public string LicenseKey      { get; }
    public string AdminToken      { get; }
    public string HostToken       { get; }
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
