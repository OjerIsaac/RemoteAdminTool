# RemoteAdminTool

A **multi-tenant Windows remote desktop platform** built in C#. A super admin (platform owner) manages multiple sub-admins, each with their own pool of end-user machines.

---

## How It Works

```
Relay Server (Render — always on)
        |
        +--- You (Super Admin) ---- RemoteAdmin.exe ---- manage all tenants and nodes
        |
        +--- Sub-Admin A ---------- RemoteAdmin.exe ---- sees only their own nodes
        |        +-- Host.exe (PC 1, PC 2, ...)
        |
        +--- Sub-Admin B ---------- RemoteAdmin.exe ---- sees only their own nodes
                 +-- Host.exe (PC 1, PC 2, ...)
```

- **RemoteAdmin.exe** is a universal app. Super admin and sub-admins use the same binary. The difference is the token they log in with.
- **Host.exe** is unique per sub-admin. The sub-admin LicenseKey is compiled into the binary at build time.
- **The relay** is the only server. It handles auth, routing, and tenant isolation.

---

## Features

| Feature | Details |
|---------|---------|
| Remote Desktop | Real-time screen viewing with mouse and keyboard control |
| Multi-Tenant | Super admin to Sub-admins to End-user nodes (3-tier isolation) |
| Hub-and-Spoke Relay | Central relay on Render, no port forwarding needed |
| Tenant Management | Create, suspend, resume sub-admin tenants from the UI |
| Compiled Secrets | Relay URL, tokens, license keys compiled into EXEs at build time |
| DXGI GPU Capture | GPU-accelerated screen capture with GDI fallback |
| Adaptive Quality | JPEG quality auto-adjusts based on network conditions |
| Multi-Monitor | Select which monitor to capture on remote machines |
| Clipboard Sync | Bidirectional clipboard sharing |
| File Transfer | Chunked file transfer with SHA256 verification |
| Chat | Real-time text chat between admin and remote user |
| Consent Dialog | Remote user accepts or denies incoming connections |
| Windows Service | Host can run as a background service for unattended access |
| PostgreSQL | Tenant records stored in Postgres (Render managed DB) |
| Keep-Alive | Relay self-pings every 2 minutes to prevent Render free-tier sleep |

---

## Project Structure

```
RemoteAdminTool/
|-- RelayServer/         ASP.NET Core WebSocket hub (deployed to Render)
|-- Viewer/              WPF admin dashboard (super admin + sub-admin)
|-- Host/                WPF tray app installed on end-user PCs
|-- Shared/              Protocol messages shared across projects
|-- .env                 Single source of truth for all secrets (never committed)
|-- build.ps1            Master build script — reads .env, patches constants, publishes EXEs
+-- SUPERADMIN_GUIDE.md  Full step-by-step operations guide (no coding required)
```

---

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Relay Server | ASP.NET Core 8, WebSocket hub |
| Admin Viewer | WPF .NET 8, Windows only |
| Host Agent | WPF .NET 8, Windows Service support |
| Database | PostgreSQL (Render managed) |
| Hosting | Render |
| Screen Capture | Windows DXGI Desktop Duplication API |
| Logging | Serilog |

---

## Developer Setup

### Prerequisites

- .NET 8 SDK
- Visual Studio 2022
- Render account

### First-time setup

1. Clone the repo
2. Create a .env file at the root:

```ini
RELAY_URL=wss://your-app.onrender.com/relay
Auth__SuperAdminToken=generate-a-random-token
Auth__HostToken=generate-a-random-token
RelayServer__TLS=false
ConnectionStrings__Postgres=postgresql://user:pass@host/db
```

3. Build and patch:

```powershell
.\build.ps1
```

### Render Environment Variables

Set these in your Render Web Service dashboard under Environment:

| Variable | Value |
|----------|-------|
| Auth__SuperAdminToken | Your super admin token |
| Auth__HostToken | Host registration token |
| RelayServer__TLS | false |
| DATABASE_URL | Auto-set when you link a Render Postgres DB |

---

## Build Commands

```powershell
# Build everything
.\build.ps1

# Publish Viewer EXE (RemoteAdmin.exe) to dist\Viewer\
.\build.ps1 -PublishViewer

# Publish Host EXE for a sub-admin (LicenseKey baked in) to dist\Host-xxx\
.\build.ps1 -PublishHostForTenant <LicenseKey>
```

---

## Security Design

| Secret | Where it lives | How it gets there |
|--------|---------------|------------------|
| Relay URL | HostConstants.cs, ViewerConstants.cs | build.ps1 patches before compile |
| Host token | HostConstants.cs | build.ps1 patches before compile |
| License key | HostConstants.cs | build.ps1 -PublishHostForTenant |
| Super admin token | Render env only | Never in code |
| DB password | .env (gitignored) + Render env | Never in code |

---

## Operations Guide

For day-to-day usage, see **SUPERADMIN_GUIDE.md** for a complete step-by-step manual requiring no coding knowledge.

---

## Deployment

```powershell
git add .
git commit -m "description"
git push origin main
```

Render auto-redeploys on push. Schema migrations run automatically on startup.