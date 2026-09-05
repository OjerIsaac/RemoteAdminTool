# RemoteAdminTool

A Windows remote desktop administration tool built in C# — similar to [AnyDesk](https://anydesk.com) or [SimpleHelp](https://simple-help.com). Allows an admin to remotely view and control multiple Windows machines through a central relay server.

## Features

| Feature | Details |
|---------|---------|
| **Remote Desktop** | Real-time screen viewing with mouse and keyboard control |
| **Hub-and-Spoke Architecture** | Central relay server — no port forwarding needed on remote machines |
| **Multi-Node Dashboard** | Admin can see all online hosts and switch between them |
| **DXGI GPU Capture** | GPU-accelerated Desktop Duplication with GDI fallback |
| **Adaptive Quality** | JPEG quality auto-adjusts (20-80) based on network conditions |
| **Multi-Monitor** | Select which monitor to view on remote machines |
| **Clipboard Sync** | Bidirectional clipboard sharing between admin and host |
| **File Transfer** | Send files to remote machines (chunked, SHA256 verified) |
| **Chat** | Real-time text chat between admin and remote user |
| **TLS Encryption** | WSS with auto-generated or custom certificates |
| **Consent Dialog** | Remote user can accept/deny incoming connections |
| **Windows Service** | Host can run as a background Windows Service for unattended access |
| **Structured Logging** | Serilog with rolling log files (14-day retention) |

---

## Architecture

```
                    ┌─────────────────┐
                    │   RelayServer   │
                    │  (ASP.NET Core) │
                    │   Port 5050     │
                    └────┬──────┬─────┘
              WSS/WS │          │ WSS/WS
            (outbound)│          │(outbound)
           ┌──────────┘          └──────────┐
           ▼                                ▼
    ┌──────────────┐                ┌──────────────┐
    │  Host Agent   │                │    Viewer     │
    │  (Remote PC)  │                │(Admin Dashboard)│
    │  Tray icon /  │                │    WPF app    │
    │  Win Service  │                │               │
    └──────────────┘                └──────────────┘
```

All connections are **outbound** — both Host and Viewer connect to the Relay. This solves NAT traversal since only the Relay needs to be publicly reachable.

---

## Solution Structure

```
RemoteAdminTool.sln
│
├── Shared/                          Shared protocol + utilities
│   ├── Protocol.cs                  Message types and data structures
│   ├── MessageFraming.cs            Length-prefixed binary frame reader/writer
│   ├── WebSocketStreamAdapter.cs    WebSocket ↔ Stream adapter
│   └── FileTransferService.cs       Chunked file transfer with checksums
│
├── RelayServer/                     Central hub (ASP.NET Core)
│   ├── Program.cs                   Kestrel setup, TLS, Serilog
│   ├── ConnectionHub.cs             Host registry, admin auth, session brokering
│   ├── SessionRelay.cs              Bidirectional frame/input forwarding
│   ├── CertificateHelper.cs         TLS certificate management
│   └── appsettings.json             Server configuration + auth tokens
│
├── Host/                            Remote machine agent
│   ├── Program.cs                   Dual mode: desktop app or Windows Service
│   ├── HostAgent.cs                 WebSocket client, auto-reconnect
│   ├── HostBackgroundService.cs     Windows Service wrapper
│   ├── ConsentDialog.cs             Accept/deny admin connections
│   ├── IScreenCapture.cs            Screen capture interface
│   ├── ScreenCapture.cs             GDI capture (fallback)
│   ├── DxgiScreenCapture.cs         GPU-accelerated Desktop Duplication
│   ├── ScreenCaptureFactory.cs      Auto-detect best capture method
│   ├── ClipboardSyncService.cs      Bidirectional clipboard sync
│   ├── InputInjector.cs             Mouse/keyboard input injection via SendInput
│   ├── TrayApplicationContext.cs    System tray icon
│   └── appsettings.json             Agent configuration + host token
│
└── Viewer/                          Admin dashboard (WPF)
    ├── ViewerClient.cs              Relay connection, auth, session management
    ├── MainWindow.xaml              Dark-themed UI with sidebar + chat panel
    └── MainWindow.xaml.cs           Event handlers, input forwarding
```

---

## Prerequisites

- **Windows 10/11** (all components are Windows-only)
- **.NET 8 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio 2022** (recommended) or VS Code

---

## Quick Start

### 1. Clone and Build

```powershell
git clone <your-repo-url>
cd RemoteAdminTool
dotnet build
```

Or open `RemoteAdminTool.sln` in Visual Studio and press **Ctrl+Shift+B**.

### 2. Configure Credentials

The project comes with pre-generated tokens in the config files. For production, generate your own:

| Config File | Key | Default Value |
|------------|-----|---------------|
| `RelayServer/appsettings.json` | `Auth:AdminToken` | `f7941a09575e414dbf96b253` |
| `RelayServer/appsettings.json` | `Auth:HostToken` | `d6a6ac2563a8443ea686346c` |
| `Host/appsettings.json` | `HostToken` | `d6a6ac2563a8443ea686346c` |
| `Host/appsettings.json` | `MachineId` | `076e49fcac67` |

> **Important:** The `HostToken` in `Host/appsettings.json` must match `Auth:HostToken` in `RelayServer/appsettings.json`.

### 3. Run Locally (3 terminals)

```powershell
# Terminal 1 — Start the Relay Server
dotnet run --project RelayServer

# Terminal 2 — Start the Host Agent
dotnet run --project Host

# Terminal 3 — Start the Admin Viewer
dotnet run --project Viewer
```

### 4. Run in Visual Studio (recommended)

1. Right-click the **Solution** -- **Configure Startup Projects...**
2. Select **Multiple startup projects**
3. Set all three to **Start** (RelayServer first)
4. Press **F5**

### 5. Connect

In the Viewer window:
1. **Relay URL:** `ws://localhost:5050/relay`
2. **Admin Token:** `f7941a09575e414dbf96b253`
3. Click **Connect to Relay**
4. Your machine should appear in the sidebar
5. Click on it -- Accept the consent dialog on the Host -- You're in!

---

## Configuration Reference

### RelayServer (`RelayServer/appsettings.json`)

```json
{
  "RelayServer": {
    "Port": 5050,
    "TLS": "false",
    "CertPath": "",
    "CertPassword": ""
  },
  "Auth": {
    "AdminToken": "your-admin-token",
    "HostToken": "your-host-token"
  }
}
```

All settings can also be set via environment variables:

| Env Variable | Description | Default |
|-------------|-------------|---------|
| `RAT_ADMIN_TOKEN` | Admin authentication token | *(from appsettings)* |
| `RAT_HOST_TOKEN` | Host registration token | *(from appsettings)* |
| `RAT_PORT` | Relay listen port | `5050` |
| `RAT_TLS` | Enable TLS (`true`/`false`) | `true` |
| `RAT_CERT_PATH` | Path to .pfx certificate | *(auto-generated)* |
| `RAT_CERT_PASSWORD` | Certificate password | `dev-password` |

### Host Agent (`Host/appsettings.json`)

```json
{
  "RelayUrl": "ws://localhost:5050/relay",
  "MachineId": "076e49fcac67",
  "MachineName": "",
  "HostToken": "your-host-token",
  "UnattendedMode": "false"
}
```

| Setting | Description |
|---------|-------------|
| `RelayUrl` | WebSocket URL of the relay server |
| `MachineId` | Unique identifier for this machine (auto-generated if empty) |
| `MachineName` | Display name (defaults to hostname if empty) |
| `HostToken` | Must match `Auth:HostToken` on the relay |
| `UnattendedMode` | Skip consent dialogs (`true` for servers) |

---

## Deployment

### Local Development

Set `TLS` to `false` in `RelayServer/appsettings.json` and use `ws://` URLs.

### Production VM (Relay Server)

```powershell
# Build release
dotnet publish RelayServer -c Release -o ./publish/relay

# Set secure tokens
$env:RAT_ADMIN_TOKEN = "$(New-Guid)"
$env:RAT_HOST_TOKEN = "$(New-Guid)"

# Option A: Run with auto-generated self-signed cert
dotnet ./publish/relay/RelayServer.dll

# Option B: Run with your own certificate (e.g., Let's Encrypt)
$env:RAT_CERT_PATH = "C:\certs\relay.pfx"
$env:RAT_CERT_PASSWORD = "your-cert-password"
dotnet ./publish/relay/RelayServer.dll
```

### Remote Machine (Host Agent)

#### As a Desktop App (with tray icon + consent dialog)

```powershell
dotnet publish Host -c Release -r win-x64 --self-contained -o ./publish/host

# Copy to remote machine, edit appsettings.json with correct relay URL + token
./publish/host/Host.exe
```

#### As a Windows Service (unattended, headless)

```powershell
# Build self-contained
dotnet publish Host -c Release -r win-x64 --self-contained -o C:\RemoteAdminHost

# Install as Windows Service (run as Administrator)
sc.exe create RemoteAdminHost binPath="C:\RemoteAdminHost\Host.exe --service"
sc.exe config RemoteAdminHost start=auto
sc.exe start RemoteAdminHost

# Check status
sc.exe query RemoteAdminHost

# View logs
Get-Content C:\RemoteAdminHost\logs\host-*.log -Tail 50

# Uninstall
sc.exe stop RemoteAdminHost
sc.exe delete RemoteAdminHost
```

---

## Protocol

Communication uses **WebSockets** with length-prefixed binary frames:

```
[4 bytes: payload length][1 byte: MessageType][payload bytes]
```

| Category | Message Types |
|----------|--------------|
| **Relay** | HostRegister, HostRegisterAck, AdminAuth, AdminAuthAck, HostList, ConnectToHost, SessionStarted |
| **Screen** | FrameData (JPEG bytes) |
| **Input** | MouseMove, MouseButton, MouseWheel, KeyEvent |
| **Features** | ClipboardSync, ChatMessage, MonitorList, MonitorSelect |
| **File Transfer** | FileTransferRequest, FileTransferData, FileTransferComplete, FileTransferCancel |
| **Control** | Heartbeat, Disconnect, HostOnline, HostOffline |

See [`Shared/Protocol.cs`](Shared/Protocol.cs) for all message types and data structures.

---

## Screen Capture

The Host agent automatically selects the best capture method:

| Method | Speed | Availability |
|--------|-------|-------------|
| **DXGI Desktop Duplication** | ⚡ Fast (GPU) | Windows 8+, not under RDP |
| **GDI CopyFromScreen** | 🐢 Slower (CPU) | Always works |

DXGI is tried first. If it fails (e.g., running under RDP, locked screen, older drivers), GDI is used as a fallback. The adaptive quality system automatically reduces JPEG quality (down to 20) when the network is slow and increases it (up to 80) when the connection is fast.

---

## Security Notes

- **TLS is on by default** in production. Set `TLS: false` only for local development.
- **Auth tokens** are shared secrets — generate long random tokens for production.
- **Consent dialog** is shown on the Host by default. Set `UnattendedMode: true` only for machines where you need unattended access.
- **Config files with tokens** are in `.gitignore` — they won't be committed to source control.
- The relay server is a **pure pass-through** — it does not store or inspect screen frames or input data.

---

## Logging

Both RelayServer and Host use **Serilog** structured logging:

- **Console output:** Colored, human-readable
- **Log files:** Rolling daily files in `logs/` subdirectory (14-day retention)

```
logs/
├── relay-20260905.log
├── host-20260905.log
└── ...
```

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Relay Server | ASP.NET Core 8, Kestrel, WebSockets |
| Host Agent | .NET 8, WinForms (tray), SharpDX (DXGI), Windows Service |
| Admin Viewer | .NET 8, WPF |
| Shared | .NET 8 Class Library |
| Logging | Serilog |
| Capture | DXGI Desktop Duplication / GDI+ |
| Input | Win32 SendInput API |
