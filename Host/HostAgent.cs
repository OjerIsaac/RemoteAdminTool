using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Host;

/// <summary>
/// Host agent that connects OUTBOUND to the relay server and registers itself.
/// When the relay brokers a session (admin wants to view this machine), the agent
/// begins sending screen frames and receiving input events through the relay.
///
/// This replaces the old HostServer that listened on a port - outbound connections
/// solve NAT traversal since only the relay needs to be publicly reachable.
/// </summary>
public sealed class HostAgent
{
    private readonly string _relayUrl;
    private readonly string _machineId;
    private readonly string _machineName;
    private readonly string _authToken;
    private readonly string _licenseKey;
    private readonly bool _unattendedMode;
    private readonly bool _allowRemoteSilentMode;
    private readonly IScreenCapture _capture = ScreenCaptureFactory.Create();
    private readonly InputInjector _input = new();

    private bool _silentMode; // runtime toggle - only effective when _allowRemoteSilentMode is true
    private ClientWebSocket? _ws;
    private WebSocketStreamAdapter? _stream;

    // Shell session (one persistent process per remote session)
    private Process? _shellProcess;
    private CancellationTokenSource? _shellCts;
    private string _shellType = "powershell"; // "powershell" or "cmd"

    // Stats polling
    private CancellationTokenSource? _statsCts;

    // File transfer service
    private readonly FileTransferService _fileTransfer = new();

    public event EventHandler<bool>? SessionStateChanged;
    public event EventHandler<string>? ConnectionStateChanged;

    public HostAgent(string relayUrl, string machineId, string machineName,
        string authToken, string licenseKey, bool unattendedMode = false,
        bool allowRemoteSilentMode = true)
    {
        _relayUrl = relayUrl;
        _machineId = machineId;
        _machineName = machineName;
        _authToken = authToken;
        _licenseKey = licenseKey;
        _unattendedMode = unattendedMode;
        _allowRemoteSilentMode = allowRemoteSilentMode;
    }

    /// <summary>
    /// Main loop: connect to relay, register, wait for sessions, reconnect on failure.
    /// Runs for the lifetime of the application.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionStateChanged?.Invoke(this, "Connecting to relay...");
                await ConnectAndRunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HostAgent] Connection lost: {ex.Message}");
                ConnectionStateChanged?.Invoke(this, $"Disconnected: {ex.Message}");
                SessionStateChanged?.Invoke(this, false);
            }

            // Reconnect with backoff
            if (!ct.IsCancellationRequested)
            {
                ConnectionStateChanged?.Invoke(this, "Reconnecting in 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(_relayUrl), ct);
        _stream = new WebSocketStreamAdapter(_ws);

        ConnectionStateChanged?.Invoke(this, "Connected to relay");

        // Register with the relay
        var buildNumber = Environment.OSVersion.Version.Build;
        var majorVer    = Environment.OSVersion.Version.Major;
        // Windows 11 starts at build 22000; Windows 10 is major 10 below that
        var winLabel    = (majorVer >= 10 && buildNumber >= 22000) ? "Windows 11" : $"Windows {majorVer}";
        var osVersion   = $"{winLabel} (Build {buildNumber})";
        var register = new HostRegisterMessage(
            _machineId,
            _machineName,
            osVersion,
            "1.0.0",
            _authToken,
            _licenseKey
        );
        await MessageFraming.WriteFrameAsync(_stream, MessageType.HostRegister,
            JsonSerializer.SerializeToUtf8Bytes(register), ct);

        // Wait for ack
        var (ackType, ackPayload) = await MessageFraming.ReadFrameAsync(_stream, ct);
        if (ackType != MessageType.HostRegisterAck)
        {
            throw new InvalidDataException("Expected HostRegisterAck from relay");
        }

        var ack = JsonSerializer.Deserialize<HostRegisterAckMessage>(ackPayload);
        if (!ack.Accepted)
        {
            throw new InvalidOperationException($"Registration rejected: {ack.RejectReason}");
        }

        ConnectionStateChanged?.Invoke(this, $"Registered as {_machineName}");
        Console.WriteLine($"[HostAgent] Registered with relay as {_machineName} ({_machineId})");

        // Idle loop - wait for sessions from the relay
        await IdleLoopAsync(ct);
    }

    /// <summary>
    /// Waits for the relay to signal a session (admin wants to connect).
    /// When SessionStarted arrives, runs the frame-send + input-receive loops.
    /// </summary>
    private async Task IdleLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
        {
            var (type, payload) = await MessageFraming.ReadFrameAsync(_stream!, ct);

            switch (type)
            {
                case MessageType.SessionStarted:
                    // Ask the local user for consent (unless unattended or silent mode)
                    bool silentNow = _unattendedMode || _silentMode;
                    if (!ConsentDialog.RequestConsent(silentNow))
                    {
                        Console.WriteLine("[HostAgent] Session DENIED by local user");
                        await MessageFraming.WriteFrameAsync(_stream!, MessageType.Disconnect,
                            Array.Empty<byte>(), ct);
                        break;
                    }
                    Console.WriteLine("[HostAgent] Session started - admin connected");
                    SessionStateChanged?.Invoke(this, true);
                    await RunSessionAsync(ct);
                    SessionStateChanged?.Invoke(this, false);
                    Console.WriteLine("[HostAgent] Session ended - back to idle");
                    break;

                case MessageType.Heartbeat:
                    // Reply with heartbeat
                    await MessageFraming.WriteFrameAsync(_stream!, MessageType.Heartbeat,
                        Array.Empty<byte>(), ct);
                    break;

                case MessageType.Disconnect:
                    return;
            }
        }
    }

    /// <summary>
    /// Active session: send screen frames to the relay (which forwards to admin),
    /// and receive input events from the relay (which came from admin).
    /// </summary>
    private async Task RunSessionAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await SendMonitorListAsync(cts.Token);
        await SendDeviceInfoAsync(cts.Token);
        await SendSystemInventoryAsync(cts.Token);

        var sendTask = FrameSendLoopAsync(cts.Token);
        var receiveTask = InputReceiveLoopAsync(cts.Token);

        var finished = await Task.WhenAny(sendTask, receiveTask);
        cts.Cancel();

        // Clean up shell + stats when session ends
        StopShell();
        StopStats();

        try { await Task.WhenAll(sendTask, receiveTask); }
        catch { /* expected once cancelled */ }
    }

    private async Task SendMonitorListAsync(CancellationToken ct)
    {
        if (_stream is null) return;

        var monitors = _capture.GetMonitors();
        var monitorData = monitors.Select(m => new MonitorInfoData(
            m.Index, m.Name, m.Width, m.Height, m.IsPrimary)).ToArray();

        var msg = new MonitorListMessage(monitorData);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.MonitorList,
            JsonSerializer.SerializeToUtf8Bytes(msg), ct);
    }

    private async Task SendDeviceInfoAsync(CancellationToken ct)
    {
        if (_stream is null) return;
        try
        {
            // CPU model from registry
            var cpuModel = "Unknown";
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                cpuModel = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown";
            }
            catch { }

            // IP addresses (non-loopback IPv4)
            var ips = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToArray();

            // System uptime
            long uptimeSec = Environment.TickCount64 / 1000;

            // System drive free/total
            var sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var driveInfo = new DriveInfo(sysRoot);
            long driveFreeGb  = driveInfo.IsReady ? driveInfo.AvailableFreeSpace / 1_073_741_824L : 0;
            long driveTotalGb = driveInfo.IsReady ? driveInfo.TotalSize           / 1_073_741_824L : 0;

            // OS build
            var osBuild = Environment.OSVersion.Version.Build.ToString();

            // Get total RAM
            long totalRamMb = GetTotalPhysicalMemory() / 1_048_576;

            var msg = new DeviceInfoMessage(
                MachineName:     Environment.MachineName,
                OsVersion:       Environment.OSVersion.VersionString,
                OsBuild:         osBuild,
                CpuModel:        cpuModel,
                CpuCores:        Environment.ProcessorCount,
                RamTotalMb:      totalRamMb,
                IpAddresses:     ips,
                Username:        Environment.UserName,
                Domain:          Environment.UserDomainName,
                UptimeSeconds:   uptimeSec,
                Timezone:        TimeZoneInfo.Local.DisplayName,
                SystemDrive:     sysRoot,
                SystemDriveFreeGb:  driveFreeGb,
                SystemDriveTotalGb: driveTotalGb
            );

            await MessageFraming.WriteFrameAsync(_stream, MessageType.DeviceInfo,
                JsonSerializer.SerializeToUtf8Bytes(msg), ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeviceInfo] Failed to collect: {ex.Message}");
        }
    }

    private async Task SendSystemInventoryAsync(CancellationToken ct)
    {
        if (_stream is null) return;
        try
        {
            var inventory = SystemInventoryCollector.Collect();
            await MessageFraming.WriteFrameAsync(_stream, MessageType.SystemInventory,
                JsonSerializer.SerializeToUtf8Bytes(inventory), ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SystemInventory] Failed to collect: {ex.Message}");
        }
    }

    private async Task HandleDirectoryListAsync(string path, CancellationToken ct)
    {
        if (_stream is null) return;
        try
        {
            // Resolve special folder shortcuts
            if (path.Equals("%DESKTOP%", StringComparison.OrdinalIgnoreCase))
                path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            else if (path.Equals("%DOWNLOADS%", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            else if (path.Equals("%DOCUMENTS%", StringComparison.OrdinalIgnoreCase))
                path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            else if (path.Equals("%TEMP%", StringComparison.OrdinalIgnoreCase))
                path = Path.GetTempPath();

            // Special case: empty/root path -> list all active drives with volume info
            DirectoryEntryData[] entries;
            if (string.IsNullOrWhiteSpace(path) || path == "\\" || path == "/")
            {
                entries = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d =>
                    {
                        var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;
                        var freeGb = d.AvailableFreeSpace / 1_073_741_824L;
                        var totalGb = d.TotalSize / 1_073_741_824L;
                        var displayName = $"{d.Name} [{label}] ({freeGb} GB free of {totalGb} GB)";
                        return new DirectoryEntryData(displayName, d.Name, true, d.TotalSize, DateTime.MinValue);
                    })
                    .ToArray();
                path = "\\";
            }
            else
            {
                var dirInfo = new DirectoryInfo(path);
                var list    = new List<DirectoryEntryData>();

                // Subdirectories
                foreach (var dir in dirInfo.EnumerateDirectories())
                {
                    try { list.Add(new DirectoryEntryData(dir.Name, dir.FullName, true, 0, dir.LastWriteTime)); }
                    catch { }
                }
                // Files
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    try { list.Add(new DirectoryEntryData(file.Name, file.FullName, false, file.Length, file.LastWriteTime)); }
                    catch { }
                }
                entries = list.ToArray();
            }

            var response = new DirectoryListResponseMessage(path, entries, null);
            await MessageFraming.WriteFrameAsync(_stream, MessageType.DirectoryListResponse,
                JsonSerializer.SerializeToUtf8Bytes(response), ct);
        }
        catch (Exception ex)
        {
            var response = new DirectoryListResponseMessage(path, Array.Empty<DirectoryEntryData>(), ex.Message);
            await MessageFraming.WriteFrameAsync(_stream, MessageType.DirectoryListResponse,
                JsonSerializer.SerializeToUtf8Bytes(response), ct);
        }
    }

    private static void HandleFileDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
            Console.WriteLine($"[FileDelete] Deleted: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileDelete] Failed: {ex.Message}");
        }
    }

    private async Task FrameSendLoopAsync(CancellationToken ct)
    {
        const int targetFps = 15;
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / targetFps);
        var heartbeatInterval = TimeSpan.FromSeconds(5);
        var lastHeartbeat = DateTime.UtcNow;

        // Adaptive quality: start at 60, reduce if frames take too long to send
        long jpegQuality = 60;
        const long minQuality = 20;
        const long maxQuality = 80;

        while (!ct.IsCancellationRequested && _stream is not null)
        {
            var sendStart = DateTime.UtcNow;

            var frameBytes = _capture.CaptureFrameAsJpeg(quality: jpegQuality);

            // DXGI returns empty when screen hasn't changed - skip sending
            if (frameBytes.Length > 0)
            {
                await MessageFraming.WriteFrameAsync(_stream, MessageType.FrameData, frameBytes, ct);
            }

            var sendTime = DateTime.UtcNow - sendStart;

            // Adaptive quality: if sending takes more than half the frame interval,
            // reduce quality to save bandwidth. If fast, increase quality.
            if (sendTime > frameInterval * 0.5 && jpegQuality > minQuality)
            {
                jpegQuality = Math.Max(minQuality, jpegQuality - 5);
            }
            else if (sendTime < frameInterval * 0.2 && jpegQuality < maxQuality)
            {
                jpegQuality = Math.Min(maxQuality, jpegQuality + 2);
            }

            // Send heartbeat every 5 seconds alongside frames
            if (DateTime.UtcNow - lastHeartbeat > heartbeatInterval)
            {
                await MessageFraming.WriteFrameAsync(_stream, MessageType.Heartbeat,
                    Array.Empty<byte>(), ct);
                lastHeartbeat = DateTime.UtcNow;
            }

            await Task.Delay(frameInterval, ct);
        }
    }

    private async Task InputReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _stream is not null)
        {
            var (type, payload) = await MessageFraming.ReadFrameAsync(_stream, ct);

            switch (type)
            {
                case MessageType.MouseMove:
                    var move = JsonSerializer.Deserialize<MouseMoveMessage>(payload);
                    _input.MoveMouse(move.X, move.Y);
                    break;

                case MessageType.MouseButton:
                    var btn = JsonSerializer.Deserialize<MouseButtonMessage>(payload);
                    _input.ClickMouse(btn.Button, btn.Action, btn.X, btn.Y);
                    break;

                case MessageType.MouseWheel:
                    var wheel = JsonSerializer.Deserialize<MouseWheelMessage>(payload);
                    _input.ScrollMouse(wheel.DeltaY);
                    break;

                case MessageType.KeyEvent:
                    var key = JsonSerializer.Deserialize<KeyEventMessage>(payload);
                    _input.SendKey(key.VirtualKeyCode, key.Action);
                    break;

                case MessageType.MonitorSelect:
                    var monSelect = JsonSerializer.Deserialize<MonitorSelectMessage>(payload);
                    _capture.SelectMonitor(monSelect.MonitorIndex);
                    Console.WriteLine($"[HostAgent] Monitor switched to index {monSelect.MonitorIndex}");
                    break;

                case MessageType.ShellInput:
                    var shellInput = JsonSerializer.Deserialize<ShellInputMessage>(payload);
                    await HandleShellInputAsync(shellInput.Input, ct);
                    break;

                case MessageType.StatsRequest:
                    var statsReq = JsonSerializer.Deserialize<StatsRequestMessage>(payload);
                    if (statsReq.Enable) StartStats(ct);
                    else StopStats();
                    break;

                case MessageType.DeviceInfoRequest:
                    await SendDeviceInfoAsync(ct);
                    await SendSystemInventoryAsync(ct);
                    break;

                case MessageType.KillProcess:
                    var killReq = JsonSerializer.Deserialize<KillProcessMessage>(payload);
                    try
                    {
                        var proc = Process.GetProcessById(killReq.Pid);
                        proc.Kill(entireProcessTree: true);
                        Console.WriteLine($"[HostAgent] Terminated process {killReq.Pid} ({proc.ProcessName})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[HostAgent] Failed to kill process {killReq.Pid}: {ex.Message}");
                    }
                    break;

                case MessageType.SetSilentMode:
                    var silentReq = JsonSerializer.Deserialize<SetSilentModeMessage>(payload);
                    if (_allowRemoteSilentMode)
                    {
                        _silentMode = silentReq.Silent;
                        Console.WriteLine($"[HostAgent] Silent mode set to {_silentMode} by remote admin");
                    }
                    break;

                case MessageType.DirectoryList:
                    var dirReq = JsonSerializer.Deserialize<DirectoryListMessage>(payload);
                    await HandleDirectoryListAsync(dirReq.Path, ct);
                    break;

                case MessageType.FileDelete:
                    var delReq = JsonSerializer.Deserialize<FileDeleteMessage>(payload);
                    HandleFileDelete(delReq.Path);
                    break;

                case MessageType.FileTransferRequest:
                    var ftReq = JsonSerializer.Deserialize<FileTransferRequestMessage>(payload);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // If DestinationPath exists as a file on THIS machine, viewer wants to DOWNLOAD it
                            if (!string.IsNullOrEmpty(ftReq.DestinationPath) && File.Exists(ftReq.DestinationPath))
                            {
                                Console.WriteLine($"[FileTransfer] Sending file to viewer: {ftReq.DestinationPath}");
                                await _fileTransfer.SendFileAsync(_stream!, ftReq.DestinationPath, null, ct: ct);
                            }
                            else
                            {
                                // Viewer is uploading a file TO the host
                                var saveDir = ftReq.DestinationPath ?? Path.GetTempPath();
                                await _fileTransfer.ReceiveFileAsync(_stream!, ftReq, saveDir, ct: ct);
                                Console.WriteLine($"[FileTransfer] Received file: {ftReq.FileName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[FileTransfer] Error: {ex.Message}");
                        }
                    }, ct);
                    break;

                case MessageType.Heartbeat:
                    break;

                case MessageType.Disconnect:
                case MessageType.DisconnectFromHost:
                    return;
            }
        }
    }

    // -------------------------------------------------------------------------
    // REMOTE SHELL
    // -------------------------------------------------------------------------

    private async Task HandleShellInputAsync(string input, CancellationToken ct)
    {
        if (_stream is null) return;

        // Special signal: Viewer requests elevating host to administrator
        if (input == "##ELEVATE_HOST")
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    Console.WriteLine("[HostAgent] Elevated instance launched, exiting current non-elevated host...");
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                await SendShellOutputAsync($"[Elevation Error] Could not elevate host: {ex.Message}\n", isError: true, ct);
            }
            return;
        }

        // Special signal: Viewer wants to switch shell type
        if (input.StartsWith("##SHELL_TYPE:"))
        {
            _shellType = input["##SHELL_TYPE:".Length..].Trim().ToLower();
            Console.WriteLine($"[Shell] Switching shell type to: {_shellType}");
            StopShell();
            await StartShellAsync(ct);
            return;
        }

        // Start shell process on first command
        if (_shellProcess is null || _shellProcess.HasExited)
            await StartShellAsync(ct);

        try
        {
            await _shellProcess!.StandardInput.WriteLineAsync(input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Shell] Write error: {ex.Message}");
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private async Task StartShellAsync(CancellationToken ct)
    {
        if (_stream is null) return;
        StopShell();

        _shellCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var shellCt = _shellCts.Token;

        string fileName, arguments;
        if (_shellType == "cmd")
        {
            fileName  = "cmd.exe";
            arguments = "/Q"; // quiet mode
        }
        else
        {
            fileName  = "powershell.exe";
            arguments = "-NoLogo -NoProfile -NonInteractive";
        }

        var psi = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        _shellProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _shellProcess.OutputDataReceived += async (_, e) =>
        {
            if (e.Data is null) return;
            await SendShellOutputAsync(e.Data, isError: false, shellCt);
        };

        _shellProcess.ErrorDataReceived += async (_, e) =>
        {
            if (e.Data is null) return;
            await SendShellOutputAsync(e.Data, isError: true, shellCt);
        };

        _shellProcess.Exited += async (_, _) =>
        {
            if (_stream is not null)
                await MessageFraming.WriteFrameAsync(_stream, MessageType.ShellEnded,
                    Array.Empty<byte>(), CancellationToken.None);
        };

        _shellProcess.Start();
        _shellProcess.BeginOutputReadLine();
        _shellProcess.BeginErrorReadLine();

        await MessageFraming.WriteFrameAsync(_stream, MessageType.ShellStarted,
            Array.Empty<byte>(), ct);

        bool isAdmin = IsAdministrator();
        var shellTitle = _shellType == "cmd" ? "Command Prompt (CMD)" : "PowerShell";
        var banner = isAdmin
            ? $"[{shellTitle} - ADMINISTRATOR / ELEVATED PRIVILEGES ACTIVE]\n"
            : $"[{shellTitle} - STANDARD USER / NON-ELEVATED]\n";

        await SendShellOutputAsync(banner, isError: !isAdmin, shellCt);

        Console.WriteLine($"[Shell] {fileName} process started (IsAdmin={isAdmin})");
    }

    private async Task SendShellOutputAsync(string text, bool isError, CancellationToken ct)
    {
        if (_stream is null) return;
        try
        {
            var msg = new ShellOutputMessage(text, isError);
            await MessageFraming.WriteFrameAsync(_stream, MessageType.ShellOutput,
                JsonSerializer.SerializeToUtf8Bytes(msg), ct);
        }
        catch { /* stream closed */ }
    }

    private void StopShell()
    {
        _shellCts?.Cancel();
        _shellCts?.Dispose();
        _shellCts = null;

        try { _shellProcess?.Kill(entireProcessTree: true); } catch { }
        _shellProcess?.Dispose();
        _shellProcess = null;
    }

    // -------------------------------------------------------------------------
    // SYSTEM MONITOR
    // -------------------------------------------------------------------------

    private void StartStats(CancellationToken sessionCt)
    {
        StopStats();
        _statsCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCt);
        _ = Task.Run(async () =>
        {
            await SendDeviceInfoAsync(_statsCts.Token);
            await SendSystemInventoryAsync(_statsCts.Token);
            await StatsLoopAsync(_statsCts.Token);
        }, _statsCts.Token);
    }

    private void StopStats()
    {
        _statsCts?.Cancel();
        _statsCts?.Dispose();
        _statsCts = null;
    }

    private async Task StatsLoopAsync(CancellationToken ct)
    {
        using var sampler = new SystemStatsSampler();
        // Warm-up delay for initial sample delta
        await Task.Delay(1000, ct);

        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var stats = sampler.Sample();
                await MessageFraming.WriteFrameAsync(_stream, MessageType.SystemStats,
                    JsonSerializer.SerializeToUtf8Bytes(stats), ct);

                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stats] Error: {ex.Message}");
        }
    }

    // ---- P/Invoke: get total installed physical RAM ----

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private static long GetTotalPhysicalMemory()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 0;
    }
}
