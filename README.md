# RemoteAdminTool

A **multi-tenant Windows remote desktop platform** built in C#. A super admin (platform owner) manages multiple sub-admins, each with their own pool of end-user machines.

---

## How It Works

```
Relay Server (Render — always on)
        |
        +--- You (Super Admin) ---- Viewer.exe ---- manage all tenants and nodes
        |
        +--- Sub-Admin A ---------- Viewer.exe ---- sees only their own nodes
        |        +-- Host.exe (PC 1, PC 2, ...)
        |
        +--- Sub-Admin B ---------- Viewer.exe ---- sees only their own nodes
                 +-- Host.exe (PC 1, PC 2, ...)
```

- **Viewer.exe** is a universal app. Super admin and sub-admins use the same binary — the difference is the token they log in with.
- **Host.exe** is unique per sub-admin. Each build has a per-tenant `HostToken` compiled in; `host.key` carries the hashed license key.
- **The relay** is the only server — handles auth, routing, tenant isolation, and auto-update delivery.

---

## Features

| Feature | Details |
|---------|---------| 
| Remote Desktop | Real-time screen viewing with mouse and keyboard control |
| Multi-Tenant | Super admin → sub-admins → end-user nodes (3-tier isolation) |
| Per-Tenant Host Token | Each sub-admin's Host.exe has a unique compiled-in token — key leaks don't cross tenants |
| Hashed host.key | License key stored as SHA-256 hash on disk — raw key never written to the client |
| Hardware MachineId | MachineId derived from CPU + motherboard + MAC — can't be spoofed by file copying |
| Rate Limiting | 15 connections/IP/minute; 5 auth failures → 10-min IP ban |
| Auto-Update | Host/Viewer silently self-update from GitHub Releases on startup |
| SHA-256 Update Verification | EXE hash verified before applying any auto-update — tampered binaries rejected |
| Authenticated Version API | `/api/version` requires `X-Host-Token` — anonymous probing returns 401 |
| Hub-and-Spoke Relay | Central relay on Render, no port forwarding needed |
| Tenant Management | Create, suspend, resume sub-admin tenants from the UI |
| DXGI GPU Capture | GPU-accelerated screen capture with GDI fallback |
| Adaptive Quality | JPEG quality auto-adjusts based on network conditions |
| Multi-Monitor | Select which monitor to capture on remote machines |
| Clipboard Sync | Bidirectional clipboard sharing |
| File Transfer | Chunked file transfer with SHA-256 verification |
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
|-- Shared/              Protocol messages shared across all projects
|-- .env                 Single source of truth for all secrets (never committed)
|-- build.ps1            Master build script — reads .env, patches constants, publishes EXEs
|-- BuildHostForSubAdmin.bat   Double-click to build a Host EXE for a specific sub-admin
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
| Hardware Fingerprint | WMI (System.Management) — CPU + mobo serial + MAC |
| Logging | Serilog |

---

## Developer Setup

### Prerequisites

- .NET 8 SDK
- Visual Studio 2022
- Render account (web service + managed Postgres)

### First-time setup

1. Clone the repo
2. Create `.env` at the root:

```ini
RELAY_URL=wss://your-app.onrender.com/relay
Auth__SuperAdminToken=generate-a-random-token
Auth__HostToken=generate-a-random-token
RelayServer__TLS=false
ConnectionStrings__Postgres=postgresql://user:pass@host/db

# Auto-update (fill after first publish)
HOST_VERSION=1.0.0
VIEWER_VERSION=1.0.0
HOST_DOWNLOAD_URL=
VIEWER_DOWNLOAD_URL=
HOST_EXE_SHA256=
VIEWER_EXE_SHA256=
```

3. Patch constants and build:

```powershell
.\build.ps1
```

### Render Environment Variables

Set these in your Render Web Service dashboard under **Environment**:

| Variable | Value |
|----------|-------|
| `Auth__SuperAdminToken` | Your super admin token |
| `Auth__HostToken` | Global host registration token |
| `RelayServer__TLS` | `false` |
| `DATABASE_URL` | Auto-set when you link a Render Postgres DB |
| `HOST_VERSION` | Current host version (e.g. `1.0.0`) |
| `VIEWER_VERSION` | Current viewer version |
| `HOST_DOWNLOAD_URL` | GitHub Releases URL for Host.exe |
| `VIEWER_DOWNLOAD_URL` | GitHub Releases URL for Viewer.exe |
| `HOST_EXE_SHA256` | SHA-256 of published Host.exe (printed by build.ps1) |
| `VIEWER_EXE_SHA256` | SHA-256 of published Viewer.exe (printed by build.ps1) |

---

## Build Commands

```powershell
# Patch constants + build solution
.\build.ps1

# Publish Viewer EXE to Viewer\dist\
.\build.ps1 -PublishViewer

# Publish Host EXE for a specific sub-admin (use BuildHostForSubAdmin.bat instead)
.\build.ps1 -PublishHostForTenant <LicenseKey> -HostToken <HostToken>

# Publish both
.\build.ps1 -PublishAll
```

`build.ps1` prints the **SHA-256 hash** of each published EXE — paste this into the matching Render env var.

---

## Security Design

| Secret | Where it lives | How it gets there |
|--------|---------------|------------------|
| Relay URL | `HostConstants.cs`, `ViewerConstants.cs` | `build.ps1` patches before compile |
| Global host token | `ViewerConstants.cs` | `build.ps1` patches before compile |
| Per-tenant host token | `HostConstants.cs` | `build.ps1 -HostToken <token>` patches before compile |
| License key (raw) | Relay DB only | Generated at tenant creation — never written to client disk |
| License key hash | `host.key` on end-user disk | `build.ps1` computes SHA-256 and writes it |
| Super admin token | Render env only | Never in code |
| DB password | `.env` (gitignored) + Render env | Never in code |

---

## Operations Guide

For day-to-day usage (no coding required), see **[SUPERADMIN_GUIDE.md](SUPERADMIN_GUIDE.md)**.

---

## Deployment

```powershell
git add .
git commit -m "description"
git push origin main
```

Render auto-redeploys on push. Schema migrations run automatically on startup — new columns are added idempotently.