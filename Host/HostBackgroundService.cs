using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RemoteAdminTool.Host;

/// <summary>
/// Windows Service wrapper for the Host agent.
/// Allows the host to run as a background Windows Service for unattended access.
///
/// Usage:
///   Install:   sc create RemoteAdminHost binPath="path\to\Host.exe --service"
///   Start:     sc start RemoteAdminHost
///   Stop:      sc stop RemoteAdminHost
///   Uninstall: sc delete RemoteAdminHost
///
/// When running as a service, consent dialogs are disabled (unattended mode is forced).
/// The tray icon is not shown in service mode (no interactive desktop).
/// </summary>
public sealed class HostBackgroundService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<HostBackgroundService> _logger;

    public HostBackgroundService(IConfiguration config, ILogger<HostBackgroundService> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // RelayUrl is a compiled constant; LicenseKey and HostToken are read from sidecar files
        var relayUrl   = HostConstants.RelayUrl;
        var keyFile    = Path.Combine(AppContext.BaseDirectory, "host.key");
        var tokenFile  = Path.Combine(AppContext.BaseDirectory, "host.token");
        var licenseKey = File.Exists(keyFile)   ? (await File.ReadAllTextAsync(keyFile)).Trim()   : string.Empty;
        var authToken  = File.Exists(tokenFile) ? (await File.ReadAllTextAsync(tokenFile)).Trim() : string.Empty;

        if (string.IsNullOrEmpty(licenseKey))
        {
            _logger.LogCritical("host.key is missing or empty in {Dir}.", AppContext.BaseDirectory);
            return;
        }

        if (string.IsNullOrEmpty(authToken))
        {
            _logger.LogCritical("host.token is missing or empty in {Dir}.", AppContext.BaseDirectory);
            return;
        }

        var machineName            = _config["MachineName"] ?? Environment.MachineName;
        var allowRemoteSilentMode  = bool.TryParse(_config["AllowRemoteSilentMode"], out var arsp) && arsp;

        // MachineId derived from hardware - cannot be spoofed by copying appsettings.json
        var machineId = await HardwareFingerprint.ComputeAsync();

        _logger.LogInformation("Starting Host agent as Windows Service");
        _logger.LogInformation("Machine: {MachineName} ({MachineId})", machineName, machineId);
        _logger.LogInformation("Relay: {RelayUrl}", relayUrl);

        // Force unattended mode when running as a service (no interactive desktop)
        var agent = new HostAgent(relayUrl, machineId, machineName, authToken, licenseKey,
            unattendedMode: true, allowRemoteSilentMode: allowRemoteSilentMode);

        agent.ConnectionStateChanged += (_, status) =>
        {
            _logger.LogInformation("[HostAgent] {Status}", status);
        };

        agent.SessionStateChanged += (_, isConnected) =>
        {
            if (isConnected)
                _logger.LogInformation("Admin session started");
            else
                _logger.LogInformation("Admin session ended");
        };

        await agent.RunAsync(stoppingToken);
    }
}
