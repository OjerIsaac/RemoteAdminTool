# RemoteAdminTool - Super Admin Operations Guide

This guide tells you exactly what to do, step by step, with no coding required.
Keep it open whenever you are operating the software.

---

## Before You Start - One-Time Setup

Do this once before anything else.

### 1. Publish Viewer.exe (your admin app)

Open a PowerShell terminal in `C:\c#-projects\RemoteAdminTool\` and run:
```powershell
.\build.ps1 -PublishViewer
```
This produces `Viewer\dist\Viewer.exe`. This is the app you will use every day.

> Run this again any time you update your code and want the latest version.

### 2. Make sure the relay is running

Your relay lives on Render and is always on. Check it is live by visiting:
```
https://remoteadmintool.onrender.com
```
You should see a response (even just `RemoteAdminTool relay running`). If Render shows the service as "Suspended", resume it from the Render dashboard.

---

## What You Have

Four components, three audiences:

| Component | Who uses it | What it does |
|-----------|-------------|--------------|
| **Viewer.exe** (RemoteAdmin) | You (super admin) AND every sub-admin | The control panel. Log in with your token to manage everything. Sub-admins log in with their token to see their own machines. |
| **Host.exe + host.key** | End users (PCs being remotely controlled) | Host.exe runs silently in the system tray. `host.key` holds the tenant's hashed license. **Both files must stay together in the same folder.** |
| **Relay Server** | Nobody touches this | Runs on Render 24/7. Connects everything together. |
| **.env** | You only | Your local secrets file. Never share or commit this. |

---

## Part 1: Your Daily App - Viewer.exe

### Opening it

Double-click **Viewer.exe** from `Viewer\dist\Viewer.exe`.
To (re)publish it: `.\build.ps1 -PublishViewer`

### Logging in as Super Admin

1. In the **Admin Token** field, paste your super admin token (from `.env` → `Auth__SuperAdminToken`)
2. Leave **License Key** completely blank
3. Click **Connect to Relay**
4. You are now connected - the **Tenants** tab appears. Sub-admins do NOT see this tab.

### Logging out

Click the red **Logout** button in the top-left bar at any time to disconnect cleanly.

### What you can do once connected

- **Nodes tab** - Every host machine from every sub-admin currently online
- **Tenants tab** - Manage sub-admins (create, view credentials, suspend, resume)
- **Click any node** - Start a remote session instantly

### Remote session tabs

| Tab | What it shows |
|-----|---------------|
| **🖥 Screen** | Live remote desktop - full mouse and keyboard control |
| **⚡ Console** | Interactive PowerShell terminal on the remote machine |
| **📊 Monitor** | Live CPU, RAM, disk, network stats + top processes |

---

## Part 2: Adding a New Sub-Admin

Do this whenever a new sub-admin joins your platform.

### Step 1 - Create the tenant in the Viewer

1. Open Viewer.exe and log in as super admin
2. Click the **Tenants** tab
3. Click **+ New**
4. Type the sub-admin's name and email → click **Create Tenant**
5. A credentials dialog appears - **do not close it yet**

### Step 2 - Copy the credentials

The dialog shows four values. Copy them now:

| Field | Copy it to... |
|-------|--------------|
| **License Key** | Paste into Notepad - you'll need it in Step 3 |
| **Host Token** | Paste into Notepad - you'll also need it in Step 3 |
| **Admin Token** | Save this to give to the sub-admin |
| **Tenant ID** | Reference only - not needed for normal operations |

> You can get these again at any time: Tenants tab → **Details** next to the sub-admin.

### Step 3 - Build Host.exe for this sub-admin

1. Go to your project folder: `C:\c#-projects\RemoteAdminTool\`
2. Double-click **BuildHostForSubAdmin.bat**
3. When prompted, paste the **License Key** → press **Enter**
4. When prompted, paste the **Host Token** → press **Enter**
5. Wait about 1 minute while it builds
6. When done, the script prints a **SHA-256 hash** - copy it! (you need it in Step 4)

Output files:
```
Host\dist\Host.exe   ← the agent binary (unique per tenant)
Host\dist\host.key   ← hashed license key (safe to leave on disk)
```

### Step 4 - Update Render with the SHA-256 hash

The hash prevents tampered updates from being installed on sub-admin machines.

> **Do this every time you build a new Host.exe.** The script always prints the hash - just copy and paste it.

1. Go to [render.com](https://render.com) -> your relay web service -> **Environment** tab
2. Add/update:
   ```
   HOST_EXE_SHA256 = <paste the hash the script just printed>
   ```
3. Click **Save Changes** - Render redeploys (~2 min)

> **Not using auto-update yet?** If `HOST_DOWNLOAD_URL` is blank in Render, you can skip this step for now - clients won't check for updates. Come back to it when you set up GitHub Releases (Part 6).

### Step 5 - What to send the sub-admin

Send the sub-admin **five things**:

| What | Where to find it | Purpose |
|------|-----------------|---------|
| **Host.exe** | `Host\dist\Host.exe` | They distribute this to every PC they want to control |
| **host.key** | `Host\dist\host.key` | Hashed license - must stay in the same folder as Host.exe |
| **host.token** | `Host\dist\host.token` | Auth token - must stay in the same folder as Host.exe |
| **Admin Token** | From the credentials dialog (Step 2) | They paste this into Viewer.exe to log in |
| **Viewer.exe** | `Viewer\dist\Viewer.exe` | Same app you use - works for sub-admins too |

> **Critical:** `Host.exe`, `host.key`, and `host.token` must always be in the same folder on every end-user machine. If either file is missing, `Host.exe` will exit immediately with an error.

> **Good news on auto-update:** Because `Host.exe` is now a generic binary (no token compiled in), all sub-admins share the same EXE. One `HOST_EXE_SHA256` in Render covers everyone. Only `host.key` and `host.token` are tenant-specific and they are NEVER replaced by auto-update.

---

## Part 3: What Sub-Admins Do

Tell your sub-admin exactly this:

### To log into Viewer.exe

1. Open Viewer.exe
2. Paste your **Admin Token** in the Admin Token field
3. Leave **License Key** blank
4. Click **Connect to Relay**
5. The Nodes tab shows all your machines
6. To disconnect, click the **Logout** button

### To install Host.exe on a PC

1. Copy **all three files** - `Host.exe`, `host.key`, and `host.token` - to the PC (USB, email, shared drive - any way)
2. Keep all three files in the **same folder**
3. Double-click **Host.exe** - it starts immediately and sits silently in the system tray
4. Optional: to auto-start on boot, right-click `Host.exe` → Create Shortcut → paste the shortcut into:
   `C:\Users\<username>\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup`

### To connect to a machine

1. Log into Viewer.exe
2. The machine appears in the Nodes list (Host.exe must be running on it)
3. Click the machine → it connects automatically
4. A consent dialog appears on the user's screen - they click **Allow**
5. The remote session starts

---

## Part 4: Viewing Credentials for an Existing Sub-Admin

If you need to rebuild a Host EXE or share the Admin Token again:

1. Open Viewer.exe → log in as super admin
2. Click **Tenants** tab
3. Click **Details** next to the sub-admin
4. The credentials dialog opens - copy buttons for all four values (License Key, Host Token, Admin Token, Tenant ID)

---

## Part 5: Suspending / Resuming a Sub-Admin

- **Suspend** - blocks their Host machines from connecting (e.g. non-payment)
- **Resume** - re-activates them

1. Tenants tab → find the sub-admin
2. Click **Suspend** or **Resume**

---

## Part 6: Pushing Updates to Host.exe (Auto-Update)

When you release a new version, every existing installation updates itself silently on next startup - no resharing required.

### How auto-update works (so you understand the variables)

Every time `Host.exe` starts, it calls your relay at `/api/version`. The relay returns three things:

| Field | Render env var | What it means |
|-------|---------------|---------------|
| `host` | `HOST_VERSION` | The latest version number (e.g. `1.0.1`) |
| `hostUrl` | `HOST_DOWNLOAD_URL` | The direct download link to the new `Host.exe` |
| `hostSha256` | `HOST_EXE_SHA256` | SHA-256 fingerprint of that EXE |

The client compares `HOST_VERSION` to its own compiled-in version. If the relay version is higher, it downloads from `HOST_DOWNLOAD_URL`, verifies the hash, then replaces itself.

> **If `HOST_DOWNLOAD_URL` is blank**, the relay omits update info entirely - clients don't try to update and no error occurs. Use this when you haven't set up GitHub Releases yet.

---

### Step 1 - Bump the version

Open `.env` and increase `HOST_VERSION`:
```ini
HOST_VERSION=1.0.1
```

Also bump it in Render later (Step 4). Both must match your new version.

---

### Step 2 - Build the new Host.exe

Run `BuildHostForSubAdmin.bat` with **any** existing tenant's credentials. Or use build.ps1 directly:
```powershell
.\build.ps1 -PublishHostForTenant <LicenseKey> -HostToken <HostToken>
```
This produces `Host\dist\Host.exe`.

> For auto-update purposes, the new `Host.exe` works for **all tenants** once downloaded - the new binary re-reads `host.key` from disk. You only need to build once.

At the end, the script prints:
```
Host EXE -> Host\dist\Host.exe

SHA-256 (paste into Render env var HOST_EXE_SHA256):
  a3f8c2d91e0b4c7f...  (64 hex characters)
```
**Copy this hash - you need it in Step 4.**

---

### Step 3 - Upload to GitHub Releases and get the download URL

The `HOST_DOWNLOAD_URL` is the **direct download link** to your `Host.exe` file on GitHub Releases. Here's how to get it:

1. Go to your GitHub repo → click **Releases** (right side of the page)
2. Click **Draft a new release**
3. Click **Choose a tag** → type `v1.0.1` → click **Create new tag: v1.0.1**
4. Give it a title (e.g. `v1.0.1 - Host update`)
5. Under **Attach binaries**, drag and drop `Host\dist\Host.exe`
6. Click **Publish release**
7. After publishing, find `Host.exe` listed under **Assets** → right-click it → **Copy link address**

The URL follows this exact pattern:
```
https://github.com/<your-username>/<your-repo>/releases/download/<tag>/<filename>
```

For example:
```
https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Host.exe
```

That link is your `HOST_DOWNLOAD_URL`. It is permanent - it never changes for that release.

> **Important:** Do NOT use the GitHub repo homepage URL or a commit URL. It must be the direct `/releases/download/` link.

---

### Step 4 - Update ALL four Render env vars (do all four together)

Go to [render.com](https://render.com) → your relay web service → **Environment** tab.

Update these four variables at the same time - they must all match each other:

```
HOST_VERSION      = 1.0.1
HOST_DOWNLOAD_URL = https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Host.exe
HOST_EXE_SHA256   = a3f8c2d91e0b4c7f...  ← from Step 2
```

Click **Save Changes** - Render redeploys automatically (~2 min).

> **Why must all three match?**  
> - If `HOST_VERSION` is old, clients won't think there's an update.  
> - If `HOST_DOWNLOAD_URL` points to the wrong file, clients download the wrong EXE.  
> - If `HOST_EXE_SHA256` is stale (from the previous build), clients reject the new download.

---

### Step 5 - Done

On next startup, every running `Host.exe` will:
1. Call `/api/version` (authenticated with its baked-in token)
2. See `HOST_VERSION` is higher than its own version
3. Download the file at `HOST_DOWNLOAD_URL`
4. Compute the SHA-256 of the downloaded file and compare to `HOST_EXE_SHA256`
5. If they match - replace itself and restart silently
6. If they don't match - abort, keep running old version, try again next startup

> **Note:** `host.key` is NEVER replaced during auto-update. Each machine keeps its original `host.key` forever.

---

### Download URL decision table

| When | What to do with `HOST_DOWNLOAD_URL` |
|------|---------------------------------|
| Haven't set up GitHub Releases yet | Leave blank - auto-update is disabled, no error |
| First time publishing a release | Paste the GitHub Releases link → also set `HOST_VERSION` and `HOST_EXE_SHA256` |
| New version released | Update the URL to the new release tag (e.g. `.../v1.0.2/Host.exe`) |
| URL is set but `HOST_VERSION` wasn't bumped | Clients won't download - they think they're already up to date |
| URL is set but `HOST_EXE_SHA256` is wrong | Clients download but reject the file - stays on old version |

---


## Part 7: Pushing Updates to Viewer.exe (Auto-Update)

Same process as Host. The variables are the same pattern - just `VIEWER_` instead of `HOST_`.

### Step 1 - Bump the version

In `.env`:
```ini
VIEWER_VERSION=1.0.1
```

### Step 2 - Build the new Viewer.exe

```powershell
.\build.ps1 -PublishViewer
```
Output: `Viewer\dist\Viewer.exe`

The script prints the SHA-256 hash at the end - copy it.

### Step 3 - Upload to GitHub Releases and get the download URL

Same steps as Part 6 Step 3, but upload `Viewer\dist\Viewer.exe`.

You can attach **both** `Host.exe` and `Viewer.exe` to the same release tag if both changed:
- Drag `Host\dist\Host.exe` → assets
- Drag `Viewer\dist\Viewer.exe` → assets
- Click Publish - you get two separate permanent links

Viewer URL format:
```
https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Viewer.exe
```

Right-click `Viewer.exe` under Assets → **Copy link address** to get `VIEWER_DOWNLOAD_URL`.

### Step 4 - Update ALL three Render env vars

```
VIEWER_VERSION      = 1.0.1
VIEWER_DOWNLOAD_URL = https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Viewer.exe
VIEWER_EXE_SHA256   = <hash from Step 2>
```

Save → Render redeploys → sub-admins' Viewer.exe silently updates itself on next launch.

---

## All Render Env Vars - Complete Reference

This is every variable the relay reads. Keep this as your master checklist.

### Always required (set once, rarely change)

| Variable | Example value | What it does |
|----------|--------------|------------- |
| `Auth__SuperAdminToken` | `f7941a09575e...` | Your super admin login token - keep secret |
| `Auth__HostToken` | `d6a6ac2563a8...` | Global host auth token - also compiled into Viewer |
| `DATABASE_URL` | `postgresql://...` | Auto-set by Render when you link a Postgres DB |
| `RELAY_URL` | `wss://remoteadmintool.onrender.com/relay` | Used by build.ps1 to patch into client EXEs |

### Auto-update (update every time you publish a new EXE)

| Variable | When to update | What happens if wrong or blank |
|----------|---------------|-------------------------------|
| `HOST_VERSION` | Every Host.exe release | If stale - clients think they're up to date, skip download |
| `HOST_DOWNLOAD_URL` | Every Host.exe release | If blank - auto-update disabled (no error). If wrong URL - download fails silently |
| `HOST_EXE_SHA256` | Every Host.exe release | If stale - clients reject the download, stay on old version |
| `VIEWER_VERSION` | Every Viewer.exe release | Same as HOST_VERSION |
| `VIEWER_DOWNLOAD_URL` | Every Viewer.exe release | Same as HOST_DOWNLOAD_URL |
| `VIEWER_EXE_SHA256` | Every Viewer.exe release | Same as HOST_EXE_SHA256 |

### The download URL explained

The download URL is a **permanent direct link** to a specific file attached to a GitHub Release. It follows this pattern:

```
https://github.com/<username>/<repo>/releases/download/<tag>/<filename>
```

For your repo:
```
https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Host.exe
https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Viewer.exe
```

**How to get it:**
1. Go to GitHub → your repo → Releases
2. Click the release
3. Under **Assets** → right-click the file → **Copy link address**

> **The link is permanent.** Once a release is published, the URL never changes for that version. You only update the URL when you create a new release tag.

### What to update on Render for each type of change

| You did... | Update in Render |
|-----------|------------------|
| New Host.exe build | `HOST_VERSION` + `HOST_DOWNLOAD_URL` + `HOST_EXE_SHA256` |
| New Viewer.exe build | `VIEWER_VERSION` + `VIEWER_DOWNLOAD_URL` + `VIEWER_EXE_SHA256` |
| Relay server code only | Nothing - EXEs didn't change |
| New sub-admin (new Host.exe) | `HOST_EXE_SHA256` only (version/URL stay same unless it's a new release) |

---


## Part 8: The SHA-256 Hash - When to Update It

The SHA-256 hash is a fingerprint of your EXE file. Clients check it before installing any update - if the file doesn't match the hash you set in Render, they refuse to install it. This stops anyone tampering with the download.

### Simple rule

> **Every time you publish a new EXE → copy the hash → update Render. Same sitting, same step. Never skip it.**

### When does the hash change?

The hash changes **only when the binary changes** - i.e. every time you run `build.ps1 -PublishHostForTenant` or `build.ps1 -PublishViewer`. Two builds of the same source code produce different hashes because the compiler embeds timestamps.

### Decision table - should I update the hash right now?

| Situation | Do I update `HOST_EXE_SHA256`? |
|-----------|-------------------------------|
| I just ran `build.ps1` and published a new Host.exe | **YES** - copy the hash the script printed and paste it into Render |
| I just ran `build.ps1 -PublishViewer` | **YES** - update `VIEWER_EXE_SHA256` in Render |
| I pushed relay server code only (`git push`) | **NO** - the EXE didn't change, hash stays the same |
| I created a new sub-admin and built their Host.exe | **YES** - the EXE changed, update `HOST_EXE_SHA256` |
| I haven't set up auto-update yet (URL is blank) | **SKIP** - leave hash blank, clients won't try to update |
| I forgot to update the hash after publishing | **Fix it now** - go to Render, paste the correct hash, save. Clients will retry on next startup. |

### Step-by-step reminder (already covered in Parts 6 & 7, summarised here)

1. Run `build.ps1` → script prints the SHA-256 at the bottom
2. Copy it (it's 64 hex characters, e.g. `a3f8c2d9...`)
3. Go to Render → Environment tab → paste into `HOST_EXE_SHA256` or `VIEWER_EXE_SHA256`
4. Also update the matching `HOST_VERSION` / `VIEWER_VERSION` and `HOST_DOWNLOAD_URL` / `VIEWER_DOWNLOAD_URL`
5. Save → Render redeploys → clients pick up the update on next startup

> **What happens if I forget?**  
> Clients download the new EXE, check the hash, find it doesn't match the (old) hash in Render, and **silently abort** - they keep running the old version. Nothing breaks, but they don't update. Fix it by pasting the correct hash into Render.

> **What if I leave the hash blank?**  
> Hash verification is skipped entirely - the update is applied without checking. This is safe for initial setup before you have a hash, but you should fill it in once you start shipping real updates.

---

## Part 9: Deploying Relay Server Updates

When relay server code changes need to go live:

```powershell
git add .
git commit -m "description of change"
git push origin main
```

Render automatically redeploys. Takes about 2 minutes. Schema migrations run automatically on startup.

---

## Security Notes (Know What Protects You)

| Protection | What it does |
|-----------|--------------|
| **Per-tenant Host Token** | Each sub-admin's `Host.exe` carries a unique token. Even if they extract it, it cannot be used to impersonate another tenant. |
| **Hashed host.key** | `host.key` contains a SHA-256 hash of the license key - not the raw key. Reading the file doesn't reveal the actual secret. |
| **Hardware MachineId** | Each machine's ID is derived from CPU + motherboard + MAC. Copying `Host.exe` to another machine generates a different ID - bypassing seat limits is not possible by file copying. |
| **Rate limiting** | The relay allows max 15 connections/IP/minute. After 5 auth failures from one IP, that IP is banned for 10 minutes. |
| **SHA-256 update verification** | Before applying any auto-update, the client verifies the downloaded EXE matches the SHA-256 hash you set in Render. A tampered file is rejected. |
| **Authenticated /api/version** | The version endpoint requires an `X-Host-Token` header - anonymous probing returns 401. |

---

## Quick Reference

| Task | Action |
|------|--------|
| Open your admin app | Double-click `Viewer\dist\Viewer.exe` |
| Log in as super admin | Admin Token = `Auth__SuperAdminToken` from `.env`, License Key = blank |
| Log out | Click red **Logout** button |
| Create a sub-admin | Tenants → + New |
| Get sub-admin credentials anytime | Tenants → Details |
| Build Host for a sub-admin | Double-click `BuildHostForSubAdmin.bat` → paste License Key → paste Host Token |
| Files to send sub-admin | `Host.exe` + `host.key` + `Viewer.exe` + Admin Token |
| Suspend a sub-admin | Tenants → Suspend |
| Resume a sub-admin | Tenants → Resume |
| Push Host update silently | Bump `HOST_VERSION` → build → upload → update Render: `HOST_DOWNLOAD_URL` + `HOST_EXE_SHA256` + `HOST_VERSION` |
| Push Viewer update silently | Bump `VIEWER_VERSION` → `build.ps1 -PublishViewer` → upload → update Render: `VIEWER_DOWNLOAD_URL` + `VIEWER_EXE_SHA256` + `VIEWER_VERSION` |
| Deploy relay server update | `git push origin main` |
| Update SHA-256 after a publish | Copy hash printed by `build.ps1` → Render Environment → `HOST_EXE_SHA256` or `VIEWER_EXE_SHA256` → Save |