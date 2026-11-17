using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using RemoteAdminTool.Host;

// ---- No-op logger (file logging disabled so no logs/ folder is created on disk) ----
Log.Logger = new LoggerConfiguration().CreateLogger();

// ---- Handle permanent stop / uninstall command-line switch ----
if (args.Contains("--stop") || args.Contains("--uninstall"))
{
    HostRegistrationHelper.RemoveStartupRegistration();
    return;
}

// ---- Single-Instance OS Mutex (prevents duplicate instances when Task Scheduler Watchdog runs) ----
using var singleInstanceMutex = new Mutex(initiallyOwned: false, @"Global\RemoteAdminTool_Host_SingleInstance");
var hasMutex = false;

try
{
    try
    {
        hasMutex = singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(2), false);
    }
    catch (AbandonedMutexException)
    {
        hasMutex = true;
    }

    if (!hasMutex)
    {
        // Another instance of Host.exe is already active
        return;
    }

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

        // ---- Register Watchdog Scheduled Task + HKCU Run so Host auto-restarts if killed & on reboot ----
        HostRegistrationHelper.EnsureStartupRegistration();

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
    if (hasMutex)
    {
        try { singleInstanceMutex.ReleaseMutex(); } catch { }
    }
    await Log.CloseAndFlushAsync();
}

namespace RemoteAdminTool.Host
{
    internal static class HostRegistrationHelper
    {
        public static void EnsureStartupRegistration()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                var quotedPath = $"\"{exePath}\"";

                // 1. Register a Windows Scheduled Task via XML with:
                //    - HighestAvailable (Administrator)
                //    - DisallowStartIfOnBatteries = false (works on laptop battery!)
                //    - StopIfGoingOnBatteries = false
                //    - LogonTrigger + 1-minute TimeTrigger
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                {
                    var escapedPath = System.Security.SecurityElement.Escape(exePath);
                    var taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
    <TimeTrigger>
      <Repetition>
        <Interval>PT1M</Interval>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
      <StartBoundary>2025-01-01T00:00:00</StartBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <RunLevel>HighestAvailable</RunLevel>
      <LogonType>InteractiveToken</LogonType>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{escapedPath}</Command>
    </Exec>
  </Actions>
</Task>";

                    var tempXmlPath = Path.Combine(Path.GetTempPath(), "RemoteAdminHost_Task.xml");
                    File.WriteAllText(tempXmlPath, taskXml, System.Text.Encoding.Unicode);

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = "schtasks.exe",
                        Arguments       = $"/Create /TN \"RemoteAdminHost\" /XML \"{tempXmlPath}\" /F",
                        CreateNoWindow  = true,
                        UseShellExecute = false,
                        WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit(5000);

                    try { File.Delete(tempXmlPath); } catch { }

                    // Start lightweight 5-second watchdog monitor (single-instance via OS mutex).
                    // If Host.exe is ended in Task Manager while RemoteAdminHost task exists,
                    // it runs 'schtasks /Run /TN RemoteAdminHost' to recover within ~5 seconds.
                    // If RemoteAdminHost task is deleted (via Stop Host button or KillHost.bat), the monitor exits immediately.
                    StartWatchdogMonitor();
                }

                // 2. Also keep HKCU Run fallback
                using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (runKey is null) return;

                var existing = runKey.GetValue("RemoteAdminHost") as string;
                if (!string.Equals(existing, quotedPath, StringComparison.OrdinalIgnoreCase))
                {
                    runKey.SetValue("RemoteAdminHost", quotedPath, Microsoft.Win32.RegistryValueKind.String);
                    Log.Information("Registered Host for auto-start on boot: {Path}", quotedPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Could not register auto-start on boot: {Message}", ex.Message);
            }
        }

        private static void StartWatchdogMonitor()
        {
            try
            {
                const string watchdogScript = @"
$m = New-Object System.Threading.Mutex($false, 'Global\RemoteAdminTool_Watchdog_Monitor')
$hasHandle = $false
try {
    try { $hasHandle = $m.WaitOne(0, $false) } catch [System.Threading.AbandonedMutexException] { $hasHandle = $true }
    if (-not $hasHandle) { exit }
    while ($true) {
        Start-Sleep -Seconds 5
        if (-not (Test-Path 'C:\Windows\System32\Tasks\RemoteAdminHost')) { break }
        if (-not (Get-Process -Name 'Host' -ErrorAction SilentlyContinue)) {
            schtasks.exe /Run /TN 'RemoteAdminHost' >$null 2>&1
        }
    }
} finally {
    if ($hasHandle) { try { $m.ReleaseMutex() } catch {} }
}";
                var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(watchdogScript));
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "powershell.exe",
                    Arguments       = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
                    CreateNoWindow  = true,
                    UseShellExecute = true,
                    WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
            }
        }

        public static void RemoveStartupRegistration()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "schtasks.exe",
                    Arguments       = "/Delete /TN \"RemoteAdminHost\" /F",
                    CreateNoWindow  = true,
                    UseShellExecute = false,
                    WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch
            {
            }

            try
            {
                using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                runKey?.DeleteValue("RemoteAdminHost", throwOnMissingValue: false);
            }
            catch
            {
            }
        }
    }

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
