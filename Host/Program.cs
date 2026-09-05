using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using RemoteAdminTool.Host;

// ---- Serilog structured logging ----
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
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
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables(prefix: "RAT_");

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
            .AddEnvironmentVariables(prefix: "RAT_")
            .Build();

        var relayUrl = config["RelayUrl"] ?? "wss://localhost:5050/relay";
        var authToken = config["HostToken"] ?? "host-secret-change-me";
        var machineName = config["MachineName"] ?? Environment.MachineName;
        var unattendedMode = bool.TryParse(config["UnattendedMode"], out var ua) && ua;

        var machineId = config["MachineId"];
        if (string.IsNullOrEmpty(machineId))
        {
            machineId = Guid.NewGuid().ToString("N")[..12];
            Log.Warning("Generated new Machine ID: {MachineId}. Add to appsettings.json for persistence.", machineId);
        }

        var trayContext = new TrayApplicationContext();
        var agent = new HostAgent(relayUrl, machineId, machineName, authToken, unattendedMode);

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
