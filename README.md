# RemoteAdminTool

A **multi-tenant Windows remote desktop administration platform** built in C# — similar to AnyDesk or SimpleHelp, but with a SaaS-style business model baked in. A **Super Admin (platform owner)** can manage multiple **Sub-Admins** (resellers/clients), each with their own pool of end-user machines.

---

## Features

| Feature | Details |
|---------|---------|
| **Remote Desktop** | Real-time screen viewing with mouse and keyboard control |
| **Multi-Tenant Architecture** | Super admin → Sub-admins → End-user nodes (3-tier) |
| **Hub-and-Spoke Relay** | Central relay server — no port forwarding needed on remote machines |
| **Multi-Node Dashboard** | Admin sees all online nodes; super admin sees all tenants' nodes |
| **Tenant Management** | Create, suspend, and resume sub-admin tenants from the Viewer UI |
| **DXGI GPU Capture** | GPU-accelerated Desktop Duplication with GDI fallback |
| **Adaptive Quality** | JPEG quality auto-adjusts (20–80) based on network conditions |
| **Multi-Monitor** | Select which monitor to view on remote machines |
| **Clipboard Sync** | Bidirectional clipboard sharing between admin and host |
| **File Transfer** | Send files to remote machines (chunked, SHA256 verified) |
| **Chat** | Real-time text chat between admin and remote user |
| **TLS Encryption** | WSS with auto-generated or custom certificates |
| **Consent Dialog** | Remote user can accept/deny incoming connections |
| **Windows Service** | Host can run as a background Windows Service for unattended access |
| **MySQL Persistence** | Tenant records stored durably in MySQL on the relay server |
| **Structured Logging** | Serilog with rolling log files (14-day retention) |

---

## Business Model Overview

```
YOU  (Super Admin / Platform Owner)
  │
  │  Create sub-admin tenants via Viewer UI
  │  After payment confirmed → generate LicenseKey + AdminToken
  │  Build + distribute Host.exe per sub-admin
  │
  ├──► Sub-Admin A (e.g. IT company)
  │       AdminToken + LicenseKey to log into Viewer
  │       Host.exe (with Sub-Admin A's LicenseKey baked in)
  │           ├── End User PC 1  (runs Host.exe)
  │           ├── End User PC 2  (runs Host.exe)
  │           └── End User PC 3  (runs Host.exe)
  │
  └──► Sub-Admin B (e.g. another IT company)
          AdminToken + LicenseKey to log into Viewer
          Host.exe (with Sub-Admin B's LicenseKey baked in)
              ├── End User PC 1  (runs Host.exe)
              └── End User PC 2  (runs Host.exe)
```

- **Sub-admins only see their own nodes** — fully isolated
- **You (super admin) see everything** — all tenants, all nodes, real-time
- **Future:** billing per node per sub-admin

---

## Architecture

```
                    ┌──────────────────────────────────┐
                    │         RelayServer               │
                    │      (ASP.NET Core + MySQL)       │
                    │  Multi-tenant ConnectionHub       │
                    │  TenantStore (in-memory + MySQL)  │
                    │         Port 5050                 │
                    └────────┬──────────┬───────────────┘
                 WS/WSS      │          │      WS/WSS
              (outbound)     │          │   (outbound)
           ┌────────────────┘          └──────────────────┐
           ▼                                              ▼
  ┌─────────────────┐                        ┌────────────────────┐
  │   Host Agent    │                        │       Viewer        │
  │  (Remote PC)    │                        │  (Admin Dashboard)  │
  │  LicenseKey     │                        │  Super Admin:       │
  │  baked in →     │                        │    all tenants      │
  │  identifies     │                        │  Sub-Admin:         │
  │  the tenant     │                        │    own nodes only   │
  └─────────────────┘                        └────────────────────┘
```

All connections are **outbound** from both Host and Viewer to the Relay. Only the Relay needs to be publicly reachable. No port forwarding required on remote machines.

---

## Solution Structure

```
RemoteAdminTool.sln
│
├── Shared/                          Shared protocol + utilities
│   ├── Protocol.cs                  All message types (including multi-tenant messages)
│   ├── MessageFraming.cs            Length-prefixed binary frame reader/writer
│   ├── WebSocketStreamAdapter.cs    Thread-safe WebSocket ↔ Stream adapter
│   └── FileTransferService.cs       Chunked file transfer with SHA256 checksums
│
├── RelayServer/                     Central hub (ASP.NET Core)
│   ├── Program.cs                   Kestrel, TLS, Serilog, EF Core + MySQL setup
│   ├── ConnectionHub.cs             Multi-tenant host registry, admin auth, session relay
│   ├── TenantStore.cs               In-memory cached tenant service (CRUD via MySQL)
│   ├── RelayDbContext.cs            EF Core DbContext for MySQL
│   ├── Tenant.cs                    EF Core entity model for sub-admin tenants
│   ├── SessionRelay.cs              Bidirectional frame/input forwarding
│   ├── CertificateHelper.cs         TLS certificate management
│   └── appsettings.json             SuperAdminToken, HostToken, MySQL connection
│
├── Host/                            Remote machine agent
│   ├── Program.cs                   Dual mode: desktop app or Windows Service
│   ├── HostAgent.cs                 WebSocket client, LicenseKey auth, auto-reconnect
│   ├── HostBackgroundService.cs     Windows Service wrapper
│   ├── ConsentDialog.cs             Accept/deny admin connections
│   ├── DxgiScreenCapture.cs         GPU-accelerated Desktop Duplication
│   ├── ScreenCapture.cs             GDI fallback capture
│   ├── ScreenCaptureFactory.cs      Auto-detect best capture method
│   ├── ClipboardSyncService.cs      Bidirectional clipboard sync
│   ├── InputInjector.cs             Mouse/keyboard injection via Win32 SendInput
│   ├── TrayApplicationContext.cs    System tray icon
│   └── appsettings.json             RelayUrl, HostToken, LicenseKey (tenant identifier)
│
└── Viewer/                          Admin dashboard (WPF)
    ├── ViewerClient.cs              Relay auth (super admin + sub-admin), all tenant commands
    ├── MainWindow.xaml              Dark-themed UI: Nodes tab + Tenants tab
    ├── MainWindow.xaml.cs           Event handlers, input forwarding, tenant management
    └── CreateTenantDialog.xaml      Modal dialog for creating new sub-admin tenants
```

---

## Prerequisites

- **Windows 10/11** (all components are Windows-only)
- **.NET 8 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio 2022** (recommended) or VS Code
- **MySQL** — for the relay server (XAMPP locally, managed MySQL in production)

---

## Quick Start (Local Development)

### 1. Clone and Build

```powershell
git clone <your-repo-url>
cd RemoteAdminTool
dotnet build
```

Or open `RemoteAdminTool.sln` in Visual Studio → **Ctrl+Shift+B**.

### 2. Local Dev with PostgreSQL

If you don't have PostgreSQL locally, the easiest way is **[Postgres.app](https://postgresapp.com/)** (Mac) or install via `winget install PostgreSQL.PostgreSQL` (Windows).

Then create the database:
```sql
CREATE DATABASE remote_admin;
```

The relay server auto-creates the `Tenants` table via `EnsureCreatedAsync()` on startup — no migrations needed.

### 3. Credentials at a Glance

| File | Key | Default Value |
|------|-----|---------------|
| `RelayServer/appsettings.json` | `Auth:SuperAdminToken` | `f7941a09575e414dbf96b253` |
| `RelayServer/appsettings.json` | `Auth:HostToken` | `d6a6ac2563a8443ea686346c` |
| `RelayServer/appsettings.json` | `ConnectionStrings:DefaultConnection` | `Host=localhost;Database=remote_admin;Username=postgres;Password=postgres;` |
| `Host/appsettings.json` | `HostToken` | `d6a6ac2563a8443ea686346c` |
| `Host/appsettings.json` | `LicenseKey` | *(set per sub-admin after creating tenant)* |

> `HostToken` in `Host/appsettings.json` **must match** `Auth:HostToken` in `RelayServer/appsettings.json`.

### 4. Run Locally (3 Terminals)

```powershell
# Terminal 1 — Relay Server
dotnet run --project RelayServer

# Terminal 2 — Host Agent (this machine)
dotnet run --project Host

# Terminal 3 — Admin Viewer
dotnet run --project Viewer
```

**Or in Visual Studio:** Right-click Solution → **Configure Startup Projects** → **Multiple startup projects** → set all three to **Start** → press **F5**.

### 5. Connect as Super Admin

In the Viewer:
1. **Relay URL:** `ws://localhost:5050/relay`
2. **Admin Token:** `f7941a09575e414dbf96b253`
3. **License Key:** *(leave blank — super admin has no license key)*
4. Click **Connect to Relay**
5. Status shows **"Connected (Super Admin)"** and the **Tenants** tab appears

---

## Multi-Tenant Workflow

### Creating a Sub-Admin (After Payment)

1. Open the Viewer as **Super Admin**
2. Click the **Tenants** tab → **+ New**
3. Enter the sub-admin's **Display Name** and **Email** → **Create Tenant**
4. A dialog shows:
   - `LicenseKey` — baked into the Host EXE for their end users
   - `AdminToken` — given to the sub-admin to log into the Viewer
5. **Save both values** — they are generated once and stored in PostgreSQL

### Building the Host EXE for a Sub-Admin's End Users

On **your laptop**, do this for each new sub-admin:

**Step 1** — Update `Host/appsettings.json` with the sub-admin's credentials:
```json
{
  "RelayUrl": "ws://YOUR_LIVE_RELAY_IP:5050/relay",
  "MachineId": "",
  "MachineName": "",
  "HostToken": "d6a6ac2563a8443ea686346c",
  "LicenseKey": "<PASTE_TENANT_LICENSE_KEY_HERE>",
  "UnattendedMode": "false"
}
```

**Step 2** — Publish a self-contained single-file EXE:
```powershell
dotnet publish Host/Host.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist/SubAdminName-Host
```

**Step 3** — Zip and send `dist/SubAdminName-Host/Host.exe` to the sub-admin.  
They distribute this one EXE to each of their end users. End users just run it — no config needed.

> ⚠️ Each sub-admin needs their **own separate build** with their unique `LicenseKey`. Never share the same EXE between sub-admins.

### Building the Viewer EXE (for Sub-Admins to Log In)

Build **once** and share with all sub-admins — they each log in with their own credentials:

```powershell
dotnet publish Viewer/Viewer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist/Viewer
```

Tell each sub-admin:
> "Open Viewer.exe, set Relay URL to `ws://yourserver.com:5050/relay`, enter your **Admin Token** and **License Key**, click Connect."

### What Each Party Gets

| Role | EXE | Viewer Credentials |
|------|-----|--------------------|
| **You (Super Admin)** | Viewer.exe | Super Admin Token · Leave License Key blank |
| **Sub-Admin** | Viewer.exe + Host.exe (for their users) | Their AdminToken · Their LicenseKey |
| **End User** | Host.exe only | Nothing — runs silently in tray |

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
    "SuperAdminToken": "your-super-admin-token",
    "HostToken": "your-host-token"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=remote_admin;Username=postgres;Password=postgres;"
  }
}
```

| Environment Variable | Description | Default |
|---------------------|-------------|---------|
| `RAT_SUPER_ADMIN_TOKEN` | Super admin (platform owner) token | *(from appsettings)* |
| `RAT_HOST_TOKEN` | Host registration token | *(from appsettings)* |
| `RAT_POSTGRES_CONNECTION` | PostgreSQL connection string | *(from appsettings)* |
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
  "LicenseKey": "sub-admin-license-key",
  "UnattendedMode": "false"
}
```

| Setting | Description |
|---------|-------------|
| `RelayUrl` | WebSocket URL of the relay server |
| `MachineId` | Unique ID for this machine (auto-generated if empty) |
| `MachineName` | Display name in the Viewer (defaults to hostname) |
| `HostToken` | Must match `Auth:HostToken` on the relay |
| `LicenseKey` | Identifies which sub-admin tenant this host belongs to |
| `UnattendedMode` | Skip consent dialogs (`true` for servers/unattended machines) |

---

## Deployment (Render — Recommended)

[Render](https://render.com) is the recommended hosting platform. It provides a managed **Web Service** (for the relay) and managed **PostgreSQL** — no server config, no SSL cert setup, no systemd. TLS is handled automatically at Render's edge.

### 1. Create a PostgreSQL Database on Render

1. Go to [render.com](https://render.com) → **New → PostgreSQL**
2. Name it `remote-admin-db`, choose a region, pick a plan (**Starter $7/mo** minimum — free tier has a 90-day expiry)
3. Once created, copy the **Internal Database URL** (used by the web service on Render) and the **External Database URL** (for local dev access if needed)

### 2. Deploy the Relay as a Web Service

1. Push your code to GitHub
2. Go to Render → **New → Web Service** → connect your repo
3. Configure:
   - **Runtime:** Docker *or* use the native .NET build pack
   - **Build Command:** `dotnet publish RelayServer/RelayServer.csproj -c Release -o ./publish`
   - **Start Command:** `dotnet ./publish/RelayServer.dll`
   - **Plan:** Starter ($7/mo) — **never use the free tier** (it sleeps after inactivity, killing all connected Host agents)
4. Under **Environment Variables**, add:

| Key | Value |
|-----|-------|
| `DATABASE_URL` | *(paste the Internal Database URL from step 1 — Render may set this automatically when you link the DB)* |
| `RAT_SUPER_ADMIN_TOKEN` | *(generate: `openssl rand -hex 24`)* |
| `RAT_HOST_TOKEN` | *(generate: `openssl rand -hex 24`)* |
| `RAT_TLS` | `false` *(Render terminates TLS at their edge — Kestrel sees plain HTTP)* |
| `RAT_PORT` | `10000` *(Render expects apps to listen on port 10000 by default)* |

5. Click **Deploy** — Render builds and starts the relay. Your relay URL will be:
   ```
   wss://your-service-name.onrender.com/relay
   ```

> ✅ The app calls `app.UseForwardedHeaders()` so WebSocket upgrade requests work correctly even though Render terminates TLS before Kestrel.

### 3. Update Host appsettings.json

Now that your relay is live, set the real URL:
```json
{
  "RelayUrl": "wss://your-service-name.onrender.com/relay",
  "HostToken": "your-host-token",
  "LicenseKey": "sub-admin-license-key"
}
```

And in the Viewer, change the Relay URL field to `wss://your-service-name.onrender.com/relay`.

### 5. Install Host as a Windows Service (End User Machines)

```powershell
# Run as Administrator on the end-user machine
sc.exe create RemoteAdminHost binPath="C:\RemoteAdminHost\Host.exe --service"
sc.exe config RemoteAdminHost start=auto
sc.exe start RemoteAdminHost

# Check status / logs
sc.exe query RemoteAdminHost
Get-Content C:\RemoteAdminHost\logs\host-*.log -Tail 50

# Remove
sc.exe stop RemoteAdminHost
sc.exe delete RemoteAdminHost
```

---

## Protocol

All communication uses **WebSockets** with length-prefixed binary frames:

```
[4 bytes: payload length][1 byte: MessageType][payload bytes]
```

| Category | Message Types |
|----------|--------------|
| **Relay auth** | HostRegister, HostRegisterAck, AdminAuth, AdminAuthAck |
| **Node management** | HostList, RequestHostList, HostOnline, HostOffline, ConnectToHost, SessionStarted, DisconnectFromHost |
| **Screen** | FrameData (JPEG bytes) |
| **Input** | MouseMove, MouseButton, MouseWheel, KeyEvent |
| **Features** | ClipboardSync, ChatMessage, MonitorList, MonitorSelect |
| **File Transfer** | FileTransferRequest, FileTransferAccept, FileTransferData, FileTransferComplete, FileTransferCancel |
| **Tenant management** | TenantList, TenantInfo, CreateTenant, CreateTenantAck, SuspendTenant, ResumeTenant |
| **Control** | Heartbeat, Disconnect |

See [`Shared/Protocol.cs`](Shared/Protocol.cs) for all message definitions.

---

## Screen Capture

The Host agent automatically selects the best capture method:

| Method | Speed | Availability |
|--------|-------|-------------|
| **DXGI Desktop Duplication** | ⚡ Fast (GPU) | Windows 8+, not under RDP |
| **GDI CopyFromScreen** | 🐢 Slower (CPU) | Always works (fallback) |

DXGI is tried first. If it fails (RDP session, locked screen, older drivers), GDI is used automatically. The adaptive quality system reduces JPEG quality down to 20 on slow connections and up to 80 on fast ones.

---

## Security Notes

- **TLS is on by default** in production. Use `TLS: false` only locally.
- **Generate strong random tokens** for production — never use the defaults.
- **Consent dialog** is shown by default on the Host. Set `UnattendedMode: true` only for machines where unattended access is required.
- **Each sub-admin's LicenseKey is unique** — if a sub-admin's key is compromised, you can suspend that tenant from the Viewer without affecting others.
- The relay is a **pure pass-through** — screen frames and input data are not stored or inspected.
- **Config files with real tokens** should be in `.gitignore` and never committed to source control.

---

## Logging

Both RelayServer and Host use **Serilog** structured logging:

- **Console:** Coloured, human-readable
- **Log files:** Daily rolling files in `logs/` (14-day retention)

```
logs/
├── relay-20260910.log
├── host-20260910.log
└── ...
```

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Relay Server | ASP.NET Core 8, Kestrel, WebSockets, EF Core 8, Npgsql (PostgreSQL) |
| Host Agent | .NET 8, WinForms (tray), SharpDX (DXGI), Windows Service |
| Admin Viewer | .NET 8, WPF |
| Shared | .NET 8 Class Library |
| Database | PostgreSQL (via Entity Framework Core + Npgsql provider) |
| Hosting | Render (Web Service + Managed PostgreSQL) |
| Logging | Serilog |
| Screen Capture | DXGI Desktop Duplication / GDI+ |
| Input Injection | Win32 SendInput API |
