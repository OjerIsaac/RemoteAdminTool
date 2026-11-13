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

        // ---- Resolve LicenseKey & HostToken (supports standalone single-file EXE + sidecar override + auto-update registry) ----
        var (licenseKey, authToken) = await HostCredentials.ResolveAsync();

        if (string.IsNullOrEmpty(licenseKey) || string.IsNullOrEmpty(authToken))
        {
            Log.Error("Host credentials (LicenseKey/HostToken) are missing in {Dir}.", AppContext.BaseDirectory);
            System.Windows.Forms.MessageBox.Show(
                "Host license configuration is missing or invalid.\n\nPlease contact your administrator.",
                "RemoteAdmin Host - License Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            return;
        }

        // ---- Register in HKCU Run so Host auto-starts and reconnects when PC reboots ----
        EnsureStartupRegistration();

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
        var allowRemoteSilentMode = !bool.TryParse(config["AllowRemoteSilentMode"], out var arsp) || arsp; // default: true

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

static void EnsureStartupRegistration()
{
    try
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (runKey is null) return;

        var quotedPath = $"\"{exePath}\"";
        var existing = runKey.GetValue("RemoteAdminHost") as string;
        if (!string.Equals(existing, quotedPath, StringComparison.OrdinalIgnoreCase))
        {
            runKey.SetValue("RemoteAdminHost", quotedPath, Microsoft.Win32.RegistryValueKind.String);
            Log.Information("Registered Host for auto-start on boot: {Path}", quotedPath);
        }
    }
    catch (Exception ex)
    {
        Log.Warning("Could not register auto-start in HKCU Run: {Message}", ex.Message);
    }
}

namespace RemoteAdminTool.Host
{
    internal static class HostCredentials
    {
        private const string RegistrySubKey = @"Software\RemoteAdminTool\Host";

        public static async Task<(string LicenseKeyHash, string HostToken)> ResolveAsync()
        {
            // 1. Optional sidecar files (for Visual Studio F5 debugging or manual override)
            var keyFile   = Path.Combine(AppContext.BaseDirectory, "host.key");
            var tokenFile = Path.Combine(AppContext.BaseDirectory, "host.token");

            var fileKey   = File.Exists(keyFile)   ? (await File.ReadAllTextAsync(keyFile)).Trim()   : string.Empty;
            var fileToken = File.Exists(tokenFile) ? (await File.ReadAllTextAsync(tokenFile)).Trim() : string.Empty;

            if (!string.IsNullOrEmpty(fileKey) && !string.IsNullOrEmpty(fileToken))
            {
                SaveToRegistry(fileKey, fileToken);
                return (fileKey, fileToken);
            }

            // 2. Check Registry if this EXE was just replaced by AutoUpdater (so it keeps its original Sub-Admin tenant)
            var (regKey, regToken, preserveOnUpdate) = ReadFromRegistry();
            if (preserveOnUpdate && !string.IsNullOrEmpty(regKey) && !string.IsNullOrEmpty(regToken))
            {
                SaveToRegistry(regKey, regToken, preserveOnUpdate: false);
                return (regKey, regToken);
            }

            // 3. Compiled-in constants inside standalone Host.exe (built by BuildHostForSubAdmin.bat or DevBuildHost.bat)
            if (!string.IsNullOrEmpty(HostConstants.DefaultLicenseKeyHash) &&
                !string.IsNullOrEmpty(HostConstants.DefaultHostToken))
            {
                SaveToRegistry(HostConstants.DefaultLicenseKeyHash, HostConstants.DefaultHostToken);
                return (HostConstants.DefaultLicenseKeyHash, HostConstants.DefaultHostToken);
            }

            // 4. Fallback to Registry if running a generic build without baked-in constants
            if (!string.IsNullOrEmpty(regKey) && !string.IsNullOrEmpty(regToken))
            {
                return (regKey, regToken);
            }

            return (string.Empty, string.Empty);
        }

        public static void MarkPreserveOnUpdate()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true);
                key?.SetValue("PreserveOnUpdate", "1", Microsoft.Win32.RegistryValueKind.String);
            }
            catch
            {
            }
        }

        private static void SaveToRegistry(string licenseKeyHash, string hostToken, bool preserveOnUpdate = false)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true);
                if (key is null) return;
                key.SetValue("LicenseKeyHash", licenseKeyHash, Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("HostToken", hostToken, Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("PreserveOnUpdate", preserveOnUpdate ? "1" : "0", Microsoft.Win32.RegistryValueKind.String);
            }
            catch
            {
            }
        }

        private static (string LicenseKeyHash, string HostToken, bool PreserveOnUpdate) ReadFromRegistry()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistrySubKey, writable: false);
                if (key is null) return (string.Empty, string.Empty, false);
                var lic      = (key.GetValue("LicenseKeyHash") as string)?.Trim() ?? string.Empty;
                var tok      = (key.GetValue("HostToken") as string)?.Trim()      ?? string.Empty;
                var preserve = (key.GetValue("PreserveOnUpdate") as string) == "1";
                return (lic, tok, preserve);
            }
            catch
            {
                return (string.Empty, string.Empty, false);
            }
        }
    }
}
