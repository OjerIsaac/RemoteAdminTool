using Microsoft.Extensions.Configuration;
using RemoteAdminTool.Host;

// ---- Load configuration ----
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "RAT_")
    .Build();

var relayUrl = config["RelayUrl"] ?? "ws://localhost:5050/relay";
var authToken = config["HostToken"] ?? "host-secret-change-me";
var machineName = config["MachineName"] ?? Environment.MachineName;

// Generate a stable machine ID on first run (persisted in appsettings.json)
var machineId = config["MachineId"];
if (string.IsNullOrEmpty(machineId))
{
    machineId = Guid.NewGuid().ToString("N")[..12]; // short 12-char ID
    Console.WriteLine($"Generated new Machine ID: {machineId}");
    Console.WriteLine("Add this to your appsettings.json to keep it stable across restarts.");
}

// ---- Set up tray icon ----
var trayContext = new TrayApplicationContext();

// ---- Set up host agent ----
var agent = new HostAgent(relayUrl, machineId, machineName, authToken);

agent.SessionStateChanged += (_, isConnected) =>
{
    trayContext.SetConnectedState(isConnected);
};

agent.ConnectionStateChanged += (_, status) =>
{
    Console.WriteLine($"[Host] {status}");
};

// Fire and forget — runs for the app's lifetime with auto-reconnect
_ = agent.RunAsync();

System.Windows.Forms.Application.Run(trayContext);
