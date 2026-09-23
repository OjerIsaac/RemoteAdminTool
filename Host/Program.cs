using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using RemoteAdminTool.Host;

// ---- Serilog structured logging (file only - no console window in WinExe) ----
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "host-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    // ---- Detect service mode ----
    var isService = args.Contains("--service");

    if (isService)
    {
        // Run as a Windows Service (headless, no tray icon, unattended mode)
        Log.Information("Starting as Windows Service");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "RemoteAdminHost";
        });

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true);
        // Note: RelayUrl and HostToken are compiled constants in HostConstants.cs

        builder.Services.AddHostedService<HostBackgroundService>();
        builder.Services.AddSerilog();

        var host = builder.Build();
        await host.RunAsync();
    }
    else
    {
        // Run as interactive desktop app with tray icon
        Log.Information("Starting as interactive desktop app");

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        // ---- Read LicenseKey from host.key (sits next to Host.exe) ----
        var keyFile    = Path.Combine(AppContext.BaseDirectory, "host.key");
        var licenseKey = string.Empty;
        if (File.Exists(keyFile))
            licenseKey = (await File.ReadAllTextAsync(keyFile)).Trim();

        if (string.IsNullOrEmpty(licenseKey))
        {
            Log.Error("host.key is missing or empty in {Dir}.", AppContext.BaseDirectory);
            System.Windows.Forms.MessageBox.Show(
                "host.key is missing or invalid.\n\nPlease contact your administrator.",
                "RemoteAdmin Host - License Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            return;
        }

        // ---- Read HostToken from host.token (sits next to Host.exe, per-tenant) ----
        var tokenFile  = Path.Combine(AppContext.BaseDirectory, "host.token");
        var authToken  = string.Empty;
        if (File.Exists(tokenFile))
            authToken = (await File.ReadAllTextAsync(tokenFile)).Trim();

        if (string.IsNullOrEmpty(authToken))
        {
            Log.Error("host.token is missing or empty in {Dir}.", AppContext.BaseDirectory);
            System.Windows.Forms.MessageBox.Show(
                "host.token is missing or invalid.\n\nPlease contact your administrator.",
                "RemoteAdmin Host - License Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            return;
        }

        // ---- Auto-update check ----
        var updated = await AutoUpdater.CheckAndUpdateAsync(HostConstants.RelayHttpUrl, HostConstants.AppVersion, authToken);
        if (updated)
        {
            await Log.CloseAndFlushAsync();
            return;
        }

        var relayUrl              = HostConstants.RelayUrl;
        var machineName           = !string.IsNullOrWhiteSpace(config["MachineName"])
                                      ? config["MachineName"]!
                                      : Environment.MachineName;
        var unattendedMode        = !bool.TryParse(config["UnattendedMode"], out var ua) || ua; // default: true
        var allowRemoteSilentMode = bool.TryParse(config["AllowRemoteSilentMode"], out var arsp) && arsp;

        // MachineId is derived from hardware (CPU + mobo serial + MAC) - cannot be spoofed by copying a file
        var machineId = await HardwareFingerprint.ComputeAsync();
        Log.Information("Hardware MachineId: {MachineId}", machineId);

        var trayContext = new TrayApplicationContext();
        var agent = new HostAgent(relayUrl, machineId, machineName, authToken, licenseKey,
            unattendedMode, allowRemoteSilentMode);

        agent.SessionStateChanged += (_, isConnected) =>
        {
            trayContext.SetConnectedState(isConnected);
        };

        agent.ConnectionStateChanged += (_, status) =>
        {
            Log.Information("[HostAgent] {Status}", status);
        };

        _ = agent.RunAsync();

        System.Windows.Forms.Application.Run(trayContext);
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host agent terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
