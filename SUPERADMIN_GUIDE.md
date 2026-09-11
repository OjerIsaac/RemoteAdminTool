# RemoteAdminTool — Super Admin Guide

## Overview: How This Software Works

RemoteAdminTool is a 3-tier remote access platform hosted on Render.

```
YOUR RELAY SERVER (Render)
        |
        +--- Super Admin (you) ---- Viewer.exe --- full control, manage tenants
        |
        +--- Sub-Admin A --- Viewer.exe --- sees only their own hosts
        |        +-- Host.exe (PC 1, PC 2, PC 3...)
        |
        +--- Sub-Admin B --- Viewer.exe --- sees only their own hosts
                 +-- Host.exe (PC 1, PC 2...)
```

| Role | App | What They Can Do |
|------|-----|-----------------|
| Super Admin (you) | Viewer.exe + your super-admin token | See all sub-admins, create/suspend tenants |
| Sub-Admin | Viewer.exe + their admin token | See only their own hosts, start remote sessions |
| End User | Host.exe (pre-configured, no setup) | Sits in tray, allows sub-admin to connect |

---

## Your App — The Viewer (Super Admin)

Build and place on your desktop once:

```powershell
.\build.ps1 -PublishViewer
# Output: dist\Viewer\Viewer.exe
```

To log in as super admin:
- Open Viewer.exe
- Enter your Auth__SuperAdminToken from .env in the Admin Token field
- Leave License Key empty
- Click Connect to Relay
- The Tenants tab appears (sub-admins do not see this tab)

---

## Creating a New Sub-Admin (Full Workflow)

### Step 1 - Create the tenant in Viewer

1. Open Viewer.exe, log in as super admin
2. Click Tenants tab -> + New
3. Enter display name and email -> Create Tenant
4. A copyable credentials dialog appears:

   Tenant ID:    abc123...
   License Key:  def456...   <- needed to build their Host EXE
   Admin Token:  ghi789...   <- give this to the sub-admin to log in

### Step 2 - Build the Host EXE for this sub-admin

```powershell
.\build.ps1 -PublishHostForTenant def456...
# Paste their LicenseKey from the dialog
# Output: dist\Host-def456\Host.exe
```

### Step 3 - Distribute

| What | Who gets it | Purpose |
|------|-------------|---------|
| dist\Host-def456\Host.exe | The sub-admin | They put this on each PC they want to control |
| ghi789... (Admin Token) | The sub-admin | They paste this into Viewer.exe to log in |
| Viewer.exe | The sub-admin | Same Viewer you use - universal app |

The sub-admin gives Host.exe to their end users.
End users just double-click it - zero config, zero setup.

---

## Viewing Existing Tenant Credentials

You can retrieve a sub-admin LicenseKey/AdminToken at any time:

1. Viewer -> Tenants tab
2. Click [Details] next to any tenant
3. Full copyable credentials dialog opens, including build command

---

## Managing Sub-Admins

| Action | How |
|--------|-----|
| See all connected hosts | Nodes tab (you see all tenants) |
| Suspend a sub-admin | Tenants -> Suspend (blocks new connections) |
| Re-activate a sub-admin | Tenants -> Resume |

---

## Your .env File (Single Source of Truth)

```ini
RELAY_URL=wss://remoteadmintool.onrender.com/relay
Auth__SuperAdminToken=...   <- your login token (keep secret)
Auth__HostToken=...         <- baked into every Host.exe
RelayServer__TLS=false
ConnectionStrings__Postgres=postgresql://...
```

Never commit .env to GitHub - it is gitignored.

---

## Build Commands Reference

```powershell
# Rebuild everything after any .env change
.\build.ps1

# Build + publish YOUR super admin Viewer EXE
.\build.ps1 -PublishViewer
# -> dist\Viewer\Viewer.exe

# Build + publish a Host EXE for one sub-admin
.\build.ps1 -PublishHostForTenant <LicenseKey>
# -> dist\Host-<first8chars>\Host.exe
```

---

## Render Dashboard - Environment Variables

Go to dashboard.render.com -> your Web Service -> Environment.
Set these (exact names, double underscore = nested key):

| Variable | Value |
|----------|-------|
| Auth__SuperAdminToken | Your super admin token |
| Auth__HostToken | Host registration token |
| RelayServer__TLS | false (Render handles TLS) |
| DATABASE_URL | Set automatically when you link Postgres |

Do NOT use the old RAT_ prefix names.

---

## Keep-Alive

The relay pings itself every 2 minutes automatically.
No external cron service needed. You will see in Render logs:
  [KeepAlive] Ping 200 <- https://remoteadmintool.onrender.com/

---

## Deploying Updates

```powershell
git add .
git commit -m "your message"
git push origin main
```

Render auto-redeploys. Schema migrations run automatically on startup.

---

## Security Summary

| Item | Where | Risk |
|------|-------|------|
| Super admin token | .env + Render dashboard | Low - never committed |
| Host token | Compiled into Host.exe | Medium - no admin access even if extracted |
| License key | Compiled into each Host.exe | Medium - only lets hosts register, not log in as admin |
| DB password | .env + DATABASE_URL on Render | Low - never in code |

Rogue host protection: seat limits (MaxHosts) prevent new machine registrations beyond the allowed count.
You can also ban individual machines by setting IsAllowed=false in the RegisteredHosts table.
