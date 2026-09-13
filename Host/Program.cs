using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using RemoteAdminTool.Host;

// ---- Serilog structured logging (file only — no console window in WinExe) ----
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
        {
            licenseKey = (await File.ReadAllTextAsync(keyFile)).Trim();
        }

        if (string.IsNullOrEmpty(licenseKey))
        {
            // No valid license — show a tray error and exit.
            // The relay would reject this anyway, but we fail fast here for clarity.
            Log.Error("host.key is missing or empty in {Dir}. Host cannot start without a valid license key.", AppContext.BaseDirectory);
            System.Windows.Forms.MessageBox.Show(
                "host.key is missing or invalid.\n\nPlease contact your administrator.",
                "RemoteAdmin Host — License Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            return;
        }

        // ---- Auto-update check ----
        var updated = await AutoUpdater.CheckAndUpdateAsync(HostConstants.RelayHttpUrl, HostConstants.AppVersion);
        if (updated)
        {
            // A replacement process has been launched — exit this one cleanly
            await Log.CloseAndFlushAsync();
            return;
        }

        // RelayUrl, HostToken are compiled constants — not readable as plain text
        var relayUrl   = HostConstants.RelayUrl;
        var authToken  = HostConstants.HostToken;
        var machineName = config["MachineName"] ?? Environment.MachineName;
        var unattendedMode = bool.TryParse(config["UnattendedMode"], out var ua) && ua;

        var machineId = config["MachineId"];
        if (string.IsNullOrEmpty(machineId))
        {
            machineId = Guid.NewGuid().ToString("N")[..12];
            Log.Warning("Generated new Machine ID: {MachineId}. Add to appsettings.json for persistence.", machineId);
        }

        var trayContext = new TrayApplicationContext();
        var agent = new HostAgent(relayUrl, machineId, machineName, authToken, licenseKey, unattendedMode);

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
