using RemoteAdminTool.Host;

// This app is meant to run over an already-secured network path (Tailscale/WireGuard VPN).
// Auth is a placeholder pre-shared token for now -- swap for something stronger before
// exposing this on anything other than a private VPN interface.

const int ListenPort = 5900;
const string AuthToken = "CHANGE_ME_BEFORE_USE"; // TODO: load from config/env, not hardcoded

var trayContext = new TrayApplicationContext();

var server = new HostServer(ListenPort, AuthToken);
server.SessionStateChanged += (_, isConnected) =>
{
    trayContext.SetConnectedState(isConnected);
};

_ = server.StartAsync(); // fire and forget; runs for the app's lifetime

System.Windows.Forms.Application.Run(trayContext);
