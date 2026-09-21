# RemoteAdminTool

A **multi-tenant Windows remote desktop platform** built in C#. A super admin (platform owner) manages multiple sub-admins, each with their own pool of end-user machines.

---

## How It Works

```
Relay Server (Render - always on)
        |
        +--- You (Super Admin) ---- Viewer.exe ---- manage all tenants and nodes
        |
        +--- Sub-Admin A ---------- Viewer.exe ---- sees only their own nodes
        |        +-- Host.exe (PC 1, PC 2, ...)
        |
        +--- Sub-Admin B ---------- Viewer.exe ---- sees only their own nodes
                 +-- Host.exe (PC 1, PC 2, ...)
```

- **Viewer.exe** is a universal app. Super admin and sub-admins use the same binary - the difference is the token they log in with.
- **Host.exe** is unique per sub-admin. Each build has a per-tenant `HostToken` compiled in; `host.key` carries the hashed license key.
- **The relay** is the only server - handles auth, routing, tenant isolation, and auto-update delivery.

---

## Features

| Feature | Details |
|---------|---------|
| Remote Desktop | Real-time screen viewing with full mouse and keyboard control |
| Hidden Cursor | Admin cursor hidden on remote screen, replaced with a tracking dot |
| Multi-Tenant | Super admin → sub-admins → end-user nodes (3-tier isolation) |
| Per-Tenant Host Token | Each sub-admin's Host.exe has a unique compiled-in token - key leaks don't cross tenants |
| Hashed host.key | License key stored as SHA-256 hash on disk - raw key never written to the client |
| Hardware MachineId | MachineId derived from CPU + motherboard + MAC - can't be spoofed by file copying |
| Rate Limiting | 15 connections/IP/minute; 5 auth failures → 10-min IP ban |
| Terminal | Full PowerShell / CMD shell with Up/Down history, red stderr, clear button, runtime shell switching |
| Code Editor | Multi-line script editor (PowerShell or Batch) with output pane |
| File Manager | Browse remote directories, upload/download files with progress, delete remote files |
| Device Info | Machine name, username, OS version, uptime, CPU, RAM, IP addresses - auto-loaded at session start |
| System Monitor | Live CPU, RAM, disk I/O, network stats and top processes |
| Silent Mode | Connect without showing consent dialog on the remote machine (opt-in per build via appsettings) |
| Auto-Reconnect | Viewer automatically retries connection every 5 seconds after a drop (up to 20 attempts) |
| Auto-Update | Host/Viewer silently self-update from GitHub Releases on startup |
| SHA-256 Update Verification | EXE hash verified before applying any auto-update - tampered binaries rejected |
| Authenticated Version API | `/api/version` requires `X-Host-Token` - anonymous probing returns 401 |
| Hub-and-Spoke Relay | Central relay on Render, no port forwarding needed |
| Tenant Management | Create, suspend, resume sub-admin tenants from the UI |
| DXGI GPU Capture | GPU-accelerated screen capture with GDI fallback |
| Adaptive Quality | JPEG quality auto-adjusts based on network conditions |
| Multi-Monitor | Select which monitor to capture on remote machines |
| Clipboard Sync | Bidirectional clipboard sharing |
| File Transfer (legacy) | Chunked file transfer with SHA-256 verification |
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
|-- build.ps1            Master build script - reads .env, patches constants, publishes EXEs
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
| Hardware Fingerprint | WMI (System.Management) - CPU + mobo serial + MAC |
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

4. Publish Viewer.exe so you can log in:

```powershell
.\build.ps1 -PublishViewer
```

Output: `Viewer\dist\Viewer.exe` - open this and log in as super admin to manage tenants.

5. Running Host from Visual Studio (dev only):

Host.exe requires a `host.key` file next to it to start. For local dev, after creating your first tenant in the Viewer, generate the key with:

```powershell
.\MakeDevHostKey.ps1 -LicenseKey <paste-license-key-from-viewer>
```

Then rebuild Host in VS (`Ctrl+Shift+B`) - the key copies to the output folder automatically.

> For distribution to real sub-admins, use `BuildHostForSubAdmin.bat` instead - it handles everything.

### Render Environment Variables

Set these in your Render Web Service dashboard under **Environment**:

#### Always required (set once)

| Variable | Value |
|----------|----- |
| `Auth__SuperAdminToken` | Your super admin token |
| `Auth__HostToken` | Global host registration token |
| `RelayServer__TLS` | `false` |
| `DATABASE_URL` | Auto-set when you link a Render Postgres DB |

#### Auto-update (update every time you publish a new EXE)

| Variable | When to update | If blank | If stale/wrong |
|----------|---------------|----------|----------------|
| `HOST_VERSION` | Every Host.exe release | Clients never see an update | Clients skip download (think they're current) |
| `HOST_DOWNLOAD_URL` | Every Host.exe release | Auto-update disabled - no error | Download fails silently |
| `HOST_EXE_SHA256` | Every Host.exe release | Hash check skipped | Clients reject the download |
| `VIEWER_VERSION` | Every Viewer.exe release | Same as HOST_VERSION | Same |
| `VIEWER_DOWNLOAD_URL` | Every Viewer.exe release | Same as HOST_DOWNLOAD_URL | Same |
| `VIEWER_EXE_SHA256` | Every Viewer.exe release | Same as HOST_EXE_SHA256 | Same |

**Rule:** Update all three matching vars together on every release (`VERSION` + `DOWNLOAD_URL` + `EXE_SHA256`). Missing one causes silent failures.

---

## Build Commands

```powershell
# Patch constants + build solution
.\build.ps1

# Publish Viewer EXE to Viewer\dist\
.\build.ps1 -PublishViewer

# Publish Host EXE for a specific sub-admin (use BuildHostForSubAdmin.bat instead)
.\build.ps1 -PublishHostForTenant <LicenseKey> -HostToken <HostToken>

# Publish both Viewer + build (no Host EXE)
.\build.ps1 -PublishAll
```

After every publish, `build.ps1` prints the **SHA-256 hash** of the output EXE:

```
SHA-256 (paste into Render env var HOST_EXE_SHA256):
  a3f8c2d91e0b...64 hex chars...
```

### The download URL - what it is and how to get it

The `HOST_DOWNLOAD_URL` / `VIEWER_DOWNLOAD_URL` is a **permanent direct link** to a file attached to a GitHub Release:

```
https://github.com/<username>/<repo>/releases/download/<tag>/<filename>
```

For this repo:
```
https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Host.exe
https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Viewer.exe
```

**How to get it:** GitHub repo → Releases → click the release → under Assets → right-click the file → **Copy link address**.

> Leave `HOST_DOWNLOAD_URL` / `VIEWER_DOWNLOAD_URL` **blank** to disable auto-update entirely with no errors. Fill in once you publish to GitHub Releases.

### Viewer auto-update - complete checklist

Run this every time you want to push a new Viewer to sub-admins:

```
[ ] 1. In .env: bump VIEWER_VERSION (e.g. 1.0.0 → 1.1.0)
[ ] 2. Run: .\build.ps1 -PublishViewer
         Output: Viewer\dist\Viewer.exe + SHA-256 hash printed on screen
[ ] 3. Copy the SHA-256 hash
[ ] 4. Go to GitHub repo → Releases → Draft a new release
         Tag: v1.1.0 | Drag Viewer\dist\Viewer.exe into assets | Publish
[ ] 5. Right-click Viewer.exe under Assets → Copy link address
         URL format: https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.1.0/Viewer.exe
[ ] 6. On Render → Environment tab, set all three together:
         VIEWER_VERSION      = 1.1.0
         VIEWER_DOWNLOAD_URL = <URL from step 5>
         VIEWER_EXE_SHA256   = <hash from step 3>
[ ] 7. Click Save Changes → Render redeploys (~2 min)
[ ] Done. Sub-admins get the update silently on next launch.
```

> **Host auto-update** follows the exact same pattern using `HOST_VERSION`, `HOST_DOWNLOAD_URL`, `HOST_EXE_SHA256`. See `SUPERADMIN_GUIDE.md` Part 6 for full details.

### SHA-256 update rule

> **Every time you publish a new EXE → copy the printed hash → paste into the matching Render env var. Same sitting, same step.**

| Situation | Action |
|-----------|--------|
| Just published a new `Host.exe` | Update `HOST_EXE_SHA256` in Render |
| Just published a new `Viewer.exe` | Update `VIEWER_EXE_SHA256` in Render |
| Only pushed relay server code (`git push`) | Do nothing - EXEs didn't change |
| Hash is blank | Verification is skipped - safe for initial setup |
| Forgot to update hash after publishing | Clients silently abort the update. Fix: paste the correct hash into Render now. |

---

## Security Design

| Secret | Where it lives | How it gets there |
|--------|---------------|------------------|
| Relay URL | `HostConstants.cs`, `ViewerConstants.cs` | `build.ps1` patches before compile |
| Global host token | `ViewerConstants.cs` | `build.ps1` patches before compile |
| Per-tenant host token | `HostConstants.cs` | `build.ps1 -HostToken <token>` patches before compile |
| License key (raw) | Relay DB only | Generated at tenant creation - never written to client disk |
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

Render auto-redeploys on push. Schema migrations run automatically on startup - new columns are added idempotently.