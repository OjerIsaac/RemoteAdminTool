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
This produces `dist\<today-date>\viewer\Viewer.exe`. This is the app you will use every day.

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

Double-click **Viewer.exe** from `dist\<today-date>\viewer\Viewer.exe`.
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
| **Terminal** | Direct-typing interactive **PowerShell** or **CMD** console. Type directly at the inline `PS>` / `CMD>` prompt, use Up/Down arrows for command history, check the privilege badge (`Standard User` vs `Administrator (Elevated)`), and click **`⬆ Elevate (UAC)`** to request admin rights on the host when needed. |
| **Files** | Full File Explorer for the remote machine. Includes Quick Access buttons (`All Drives`, `C:\`, `Desktop`, `Downloads`, `Documents`, `Temp`), clickable breadcrumb path bar, and buttons to **Upload**, **Download**, and **Delete** files. |
| **Device** | Machine info - CPU, RAM, OS version, uptime, IP addresses, disk space. Loaded automatically when a session starts. |
| **Monitor** | Live CPU, RAM, disk, network stats and top running processes. |

*(Note: The **Screen** tab and 15 FPS capture are currently commented out for lightweight Server RMM mode and can be re-enabled anytime in `Viewer/MainWindow.xaml` and `Host/HostAgent.cs`).*

### Silent mode

The **Silent** checkbox in the session toolbar lets you take control of a machine without showing the consent dialog to the remote user. The host machine must have `AllowRemoteSilentMode: true` in its `appsettings.json` (this is the default when you build from `build.ps1`). Tick it before clicking a node to connect silently.

---

## Part 2: Local Testing & Development (Testing Host on Your PC First)

Before building and distributing `Host.exe` to real sub-admins, you can run and test everything locally on your development machine.

### How it works
- The relay routes all hosts by tenant license keys.
- **The Super Admin has global visibility**: you can see and remotely control all hosts across all tenants in your **Nodes** tab.
- To test locally without affecting real clients, you simply create a dedicated local test tenant (e.g., `Local Dev`). Once your local Host connects with that test tenant's keys, your PC immediately shows up in your Super Admin Viewer, ready for live testing.

---

### Step 1 - Log into Viewer.exe as Super Admin
1. Open `dist\<today-date>\viewer\Viewer.exe` (or press **F5** on the Viewer project in Visual Studio).
2. In **Admin Token**, paste your `Auth__SuperAdminToken` from `.env`.
3. Leave **License Key** blank.
4. Click **Connect to Relay**.
5. The status turns green (`Connected (Super Admin)`) and the **Tenants** tab appears.

---

### Step 2 - Create a Local Dev/Test Tenant
*(You only need to do this once per database reset)*:
1. In `Viewer.exe`, click the **Tenants** tab.
2. Click **+ New**.
3. Enter:
   - **Name:** `Local Dev` (or `Internal Test`)
   - **Email:** `dev@local`
4. Click **Create Tenant**.
5. A credentials popup appears. Copy the **License Key** and the **Host Token**.
   *(If you already closed the popup, click **Details** next to `Local Dev` in the Tenants tab to copy them anytime).*

---

### Step 3 - Set up your Local Dev Keys
Open PowerShell in `C:\c#-projects\RemoteAdminTool\` and run:

```powershell
.\MakeDevHostKey.ps1 -LicenseKey "<pasted-license-key>" -HostToken "<pasted-host-token>"
```

What this does:
- Writes `host.key`, `host.token`, and metadata to `dist\<today-date>\host_dev\`.
- Also updates `Host\bin\Debug\net8.0-windows\` (for Visual Studio debugging).

---

### Step 4 - Run the Host locally
Choose either method:

* **Method A: Standalone EXE (Quick test)**
  Run `DevBuildHost.bat` (builds `Host.exe` directly into `dist\<today-date>\host_dev\Host.exe`), then run it.
* **Method B: Visual Studio (Debugging & breakpoints)**
  In Visual Studio, right-click the **Host** project → **Set as Startup Project**, then press **F5** or **Ctrl+F5**.

> The Host agent runs in the background and places a small icon in your Windows System Tray (near your clock).

---

### Step 5 - View and Control Your PC in the Viewer
1. Switch back to your open `Viewer.exe`.
2. Click the **Nodes** tab.
3. Your local machine (e.g. `DESKTOP-...`) will appear with a green **Online** indicator and `Tenant: Local Dev`.
4. Click on the node card to launch the session:
   - **Screen Tab:** View your screen and test mouse/keyboard inputs.
   - **Terminal Tab:** Test interactive PowerShell and CMD shell commands.
   - **Files Tab:** Browse your local directories, test file upload, download, and delete.
   - **Device Tab:** Verify hardware specs, IP addresses, and uptime.
   - **Monitor Tab:** View live CPU, RAM, and process stats.
   - **Silent Mode:** Check the **Silent** box in the top bar before connecting to test bypassing the user consent dialog.

---

### Step 6 - Making Code Changes & Retesting (Fast Iteration Loop)
When you modify code in `Host`:
1. Double-click `KillHost.bat` to stop any currently running Host process.
2. Double-click `DevBuildHost.bat` (or rebuild in Visual Studio). It compiles your new changes while preserving your dev keys in `dist\<today-date>\host_dev\`.
3. Start `dist\<today-date>\host_dev\Host.exe` (or press F5 in Visual Studio).
4. Reconnect from Viewer.

When you modify code in `Viewer`:
1. Close the running Viewer.
2. Rebuild via Visual Studio or run `.\build.ps1 -PublishViewer`.
3. Reopen `dist\<today-date>\viewer\Viewer.exe`.

---

## Part 3: Adding a New Sub-Admin

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
3. Type the **Sub-Admin / Company Name** (e.g. `AcmeCorp`) → press **Enter**
4. Paste the **License Key** → press **Enter**
5. Paste the **Host Token** → press **Enter**
6. Wait about 1 minute while it builds
7. When done, the script prints a **SHA-256 hash** - copy it! (you need it in Step 4)

Output files (in `dist\<today-date>\subadmin_<Name>_<KeyPrefix>\`):
```
AcmeCorp_Host_Package.zip  <- ready-to-share ZIP file containing everything below!
Host.exe                   <- the agent binary
host.key                   <- hashed license key (unique to this sub-admin)
host.token                 <- auth token        (unique to this sub-admin)
appsettings.json           <- configuration
README_FOR_SUBADMIN.txt    <- instructions for the client
```

> `Host.exe` is a generic binary - it reads the tenant identity from `host.key` and `host.token` at runtime. This means ALL sub-admins share the same EXE and the same SHA-256 hash.

### Step 4 - Update Render with the SHA-256 hash

> **When to do this:**
> - **Adding your FIRST sub-admin** → YES, do this now
> - **Adding more sub-admins later** → SKIP - the hash doesn't change (same Host.exe)
> - **Publishing a new version of Host.exe** → YES, update the hash (Part 7)

1. Go to [render.com](https://render.com) -> your relay web service -> **Environment** tab
2. Add/update:
   ```
   HOST_EXE_SHA256 = <paste the hash the script just printed>
   ```
3. Click **Save Changes** - Render redeploys (~2 min)

> **Not using auto-update yet?** If `HOST_DOWNLOAD_URL` is blank in Render, skip this entirely - clients won't check for updates. Come back to it when you set up GitHub Releases (Part 7).

### Step 5 - What to send the sub-admin

Send the sub-admin the ZIP file (or folder) from `dist\<today-date>\subadmin_<Name>_<KeyPrefix>\`:

| What | Where to find it | Purpose |
|------|-----------------|---------|
| **Host Package (ZIP)** | `dist\<date>\subadmin_...\<Name>_Host_Package.zip` | Complete ready-to-send package (or send the individual files) |
| **Admin Token** | From the credentials dialog (Step 2) | They paste this into Viewer.exe to log in |
| **Viewer.exe** | `dist\<date>\viewer\Viewer.exe` | Same app you use - works for sub-admins too |

> **Critical:** `Host.exe`, `host.key`, and `host.token` must always be in the same folder on every end-user machine. If either file is missing, `Host.exe` will exit immediately with an error.

> **Good news on auto-update:** Because `Host.exe` is now a generic binary (no token compiled in), all sub-admins share the same EXE. One `HOST_EXE_SHA256` in Render covers everyone. Only `host.key` and `host.token` are tenant-specific and they are NEVER replaced by auto-update.

---

## Part 4: What Sub-Admins Do

Tell your sub-admin exactly this:

### To log into Viewer.exe

1. Open Viewer.exe
2. Paste your **Admin Token** in the Admin Token field
3. Leave **License Key** blank
4. Click **Connect to Relay**
5. The Nodes tab shows all your machines
6. To disconnect, click the **Logout** button

### To install Host.exe on a PC

1. Extract **all files** from the package (`Host.exe`, `host.key`, `host.token`, `appsettings.json`) to a folder on the target PC
2. Keep all files in the **same folder**
3. Double-click **Host.exe** - it starts 100% silently (zero UAC prompt) and sits in the system tray
4. **Auto-Start on Reboot is Automatic:** On first launch, `Host.exe` automatically registers itself in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (`RemoteAdminHost`). Whenever the PC shuts down and turns back on, `Host.exe` starts and reconnects to the relay automatically.

### To connect to a machine

1. Log into Viewer.exe
2. The machine appears in the Nodes list (Host.exe must be running on it)
3. Click the machine → it connects automatically
4. A consent dialog appears on the user's screen - they click **Allow**
5. The remote session starts

---

## Part 5: Viewing Credentials for an Existing Sub-Admin

If you need to rebuild a Host EXE or share the Admin Token again:

1. Open Viewer.exe → log in as super admin
2. Click **Tenants** tab
3. Click **Details** next to the sub-admin
4. The credentials dialog opens - copy buttons for all four values (License Key, Host Token, Admin Token, Tenant ID)

---

## Part 6: Suspending / Resuming a Sub-Admin

- **Suspend** - blocks their Host machines from connecting (e.g. non-payment)
- **Resume** - re-activates them

1. Tenants tab → find the sub-admin
2. Click **Suspend** or **Resume**

---

## Part 7: Pushing Updates to Host.exe (Auto-Update)

When you release a new version of `Host.exe`, **every existing Host across every Sub-Admin updates itself automatically and silently** - you never have to reshare `Host.exe` or `.zip` packages with anyone.

### How auto-update works (Startup + Live Background Check)

`Host.exe` checks your relay at `/api/version` in **three ways**:
1. **On every startup / PC boot**
2. **Immediately when reconnecting after a Render redeploy** (so as soon as you save the new version variables in Render and Render finishes redeploying, every online `Host.exe` reconnects, detects the update, downloads it, and restarts itself within seconds!)
3. **Every 30 minutes in the background** while idle (not in an active remote session)

The relay returns three fields from `/api/version` (authenticated using either the global token or any Sub-Admin's per-tenant `host.token`):

| Field | Render env var | What it means |
|-------|---------------|---------------|
| `host` | `HOST_VERSION` | The latest version number (e.g. `1.0.1`) |
| `hostUrl` | `HOST_DOWNLOAD_URL` | The direct download link to the new `Host.exe` |
| `hostSha256` | `HOST_EXE_SHA256` | SHA-256 fingerprint of that EXE |

If `HOST_VERSION` on Render is higher than the running `Host.exe`'s compiled version (`1.0.0` -> `1.0.1`), `Host.exe` downloads the new binary from `HOST_DOWNLOAD_URL`, verifies its SHA-256 hash matches `HOST_EXE_SHA256`, swaps `Host.exe` on disk using a retry loop, and relaunches silently.

> **Important:** `host.key` and `host.token` on each client machine are **NEVER** touched or overwritten during auto-update. Every machine across every Sub-Admin downloads the exact same generic `Host.exe` and keeps its own tenant credentials.

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
This produces `dist\<today-date>\subadmin_<KeyPrefix>\Host.exe`.

> For auto-update purposes, the new `Host.exe` works for **all tenants** once downloaded - the new binary re-reads `host.key` from disk. You only need to build once.

At the end, the script prints:
```
Host.exe -> dist\<date>\subadmin_<KeyPrefix>\Host.exe

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
5. Under **Attach binaries**, drag and drop `Host.exe` from `dist\<today-date>\subadmin_...`
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


## Part 8: Pushing Updates to Viewer.exe (Auto-Update)

When you ship a new Viewer (e.g. after adding new features like the Terminal, File Manager, Device tab etc.), every sub-admin's existing `Viewer.exe` downloads and installs it silently the next time they open it. They never have to do anything manually.

### How the Viewer auto-update works

Every time `Viewer.exe` starts, it hits `/api/version` on your relay and reads:

| Field | Render env var | What it means |
|-------|---------------|---------------|
| `viewer` | `VIEWER_VERSION` | The latest version number (e.g. `1.1.0`) |
| `viewerUrl` | `VIEWER_DOWNLOAD_URL` | The direct download link to the new `Viewer.exe` |
| `viewerSha256` | `VIEWER_EXE_SHA256` | SHA-256 fingerprint of that EXE |

If the relay version is higher than the compiled-in version, `Viewer.exe` downloads the file, checks its SHA-256, replaces itself on disk, and relaunches - all in the background.

---

### Step 1 - Bump the version in `.env`

Open `.env` and increase `VIEWER_VERSION`:
```ini
VIEWER_VERSION=1.1.0
```

Pick a number higher than what sub-admins currently have. If they have `1.0.0`, use `1.1.0`.

---

### Step 2 - Build the new Viewer.exe

Open PowerShell in `C:\c#-projects\RemoteAdminTool\` and run:
```powershell
.\build.ps1 -PublishViewer
```

This produces `dist\<today-date>\viewer\Viewer.exe` and prints at the end:
```
  SHA-256 (paste into Render env var VIEWER_EXE_SHA256):
  a3f8c2d91e0b4c7f...  (64 hex characters)
```

**Copy that hash immediately - you need it in Step 4.**

---

### Step 3 - Upload Viewer.exe to a GitHub Release

This is where sub-admins' machines download the update from. GitHub Releases is free and the links are permanent.

1. Go to your GitHub repo → click **Releases** on the right side of the page
2. Click **Draft a new release**
3. Click **Choose a tag** → type `v1.1.0` → click **Create new tag: v1.1.0**
4. Title: `v1.1.0 - Viewer update` (or whatever describes the changes)
5. Under the description box, drag and drop **`dist\<today-date>\viewer\Viewer.exe`** into the assets area
6. If you are also releasing a new Host.exe at the same time, drag **`Host.exe`** from `dist\<today-date>\subadmin_...` here too
7. Click **Publish release**
8. After publishing, scroll down to **Assets** → right-click **Viewer.exe** → **Copy link address**

The URL looks like this (permanent - never changes for this release):
```
https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.1.0/Viewer.exe
```

> **Important:** You must use the `/releases/download/` link. Do NOT use the GitHub repo homepage URL or a commit URL - those don't work for direct downloads.

---

### Step 4 - Update three Render env vars

Go to [render.com](https://render.com) → your relay web service → **Environment** tab.

Set all three together - they must all match each other:

```
VIEWER_VERSION      = 1.1.0
VIEWER_DOWNLOAD_URL = https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.1.0/Viewer.exe
VIEWER_EXE_SHA256   = <the 64-character hash from Step 2>
```

Click **Save Changes** - Render redeploys automatically (about 2 minutes).

> **Why all three together?**
> - If `VIEWER_VERSION` is old - clients think they are already up to date, skip the download
> - If `VIEWER_DOWNLOAD_URL` points to the wrong file - download fails silently
> - If `VIEWER_EXE_SHA256` is stale - clients download but reject the file, stay on old version

---

### Step 5 - Done

Next time any sub-admin launches their `Viewer.exe`:
1. It calls `/api/version` on your relay
2. Sees `VIEWER_VERSION` (`1.1.0`) is higher than its compiled version (`1.0.0`)
3. Downloads the file at `VIEWER_DOWNLOAD_URL`
4. Verifies the SHA-256 matches `VIEWER_EXE_SHA256`
5. If it matches - writes `Viewer.exe.new` to disk, launches a background PowerShell to swap it, exits
6. New Viewer launches automatically - sub-admin is now on the latest version
7. If hash does not match - silently aborts, keeps running old version, tries again next launch

**The sub-admin does nothing. They just open their app as normal.**

> **Note:** You only need to send them a new `Viewer.exe` manually the very first time. All updates after that are delivered silently through this mechanism.

---

### Viewer update decision table

| Situation | What to do with `VIEWER_DOWNLOAD_URL` |
|-----------|--------------------------------------|
| Sub-admins don't have Viewer.exe yet | Send them `dist\<date>\viewer\Viewer.exe` manually - first distribution only |
| You haven't set up GitHub Releases yet | Leave `VIEWER_DOWNLOAD_URL` blank - auto-update disabled, no error |
| First time setting up auto-update | Follow Steps 1-4 above exactly |
| New feature release | Bump version → build → upload to new release tag → update all 3 Render vars |
| `VIEWER_VERSION` not bumped | Sub-admins won't download - they think they're current |
| `VIEWER_EXE_SHA256` is wrong | Sub-admins download but reject the file - stays on old version |

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
| New Host.exe feature release | `HOST_VERSION` + `HOST_DOWNLOAD_URL` + `HOST_EXE_SHA256` |
| New Viewer.exe feature release | `VIEWER_VERSION` + `VIEWER_DOWNLOAD_URL` + `VIEWER_EXE_SHA256` |
| Both Host.exe and Viewer.exe released together | Update all 6 variables (`HOST_*` and `VIEWER_*`) |
| Relay server code only | Push to GitHub (`git push`) - Render auto-deploys |
| Created a new sub-admin (same code version) | Nothing on Render needs to change unless you also bumped `HOST_VERSION` |

---


## Part 9: The SHA-256 Hash - When to Update It

The SHA-256 hash is a fingerprint of your EXE file. Clients check it before installing any update - if the file doesn't match the hash you set in Render, they refuse to install it. This stops anyone tampering with the download.

### Simple rule

> **Every time you publish a new EXE → copy the hash → update Render. Same sitting, same step. Never skip it.**

### When does the hash change?

The hash changes **only when the binary changes** - i.e. every time you run `build.ps1 -PublishHostForTenant` or `build.ps1 -PublishViewer`. Two builds of the same source code produce different hashes because the compiler embeds timestamps.

### Decision table - should I update the hash right now?

| Situation | Do I update `HOST_EXE_SHA256`? |
|-----------|-------------------------------|
| I bumped `HOST_VERSION`, built a new `Host.exe`, and uploaded it to GitHub Releases | **YES** - copy the hash printed by `build.ps1` for that exact uploaded `Host.exe` and paste it into Render |
| I bumped `VIEWER_VERSION`, ran `build.ps1 -PublishViewer`, and uploaded `Viewer.exe` to GitHub Releases | **YES** - update `VIEWER_EXE_SHA256` in Render |
| I pushed relay server code only (`git push`) | **NO** - the EXE on GitHub Releases didn't change, hash stays the same |
| I created a new sub-admin (`BuildHostForSubAdmin.bat`) without releasing a new version on GitHub | **NO** - `HOST_EXE_SHA256` must always match the file currently hosted at `HOST_DOWNLOAD_URL` on GitHub Releases |
| I haven't set up auto-update yet (URL is blank) | **SKIP** - leave hash blank, clients won't try to update |
| I forgot to update the hash after uploading a new release | **Fix it now** - go to Render, paste the hash of the uploaded EXE, save. All running Hosts will immediately reconnect and update once Render finishes redeploying. |

### Step-by-step reminder (already covered in Parts 7 & 8, summarised here)

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

## Part 10: Deploying Relay Server Updates

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
| Open your admin app | Double-click `dist\<today-date>\viewer\Viewer.exe` |
| Log in as super admin | Admin Token = `Auth__SuperAdminToken` from `.env`, License Key = blank |
| Log out | Click red **Logout** button |
| Set up local dev keys for Host | `.\MakeDevHostKey.ps1 -LicenseKey <Key> -HostToken <Token>` |
| Rebuild Host for local dev | Double-click `DevBuildHost.bat` (keys preserved in `dist\<date>\host_dev\`) |
| Stop running Host process | Double-click `KillHost.bat` |
| Create a sub-admin | Tenants → + New |
| Get sub-admin credentials anytime | Tenants → Details |
| Build Host for a sub-admin | Double-click `BuildHostForSubAdmin.bat` → paste License Key → paste Host Token |
| Files to send sub-admin | Package in `dist\<date>\subadmin_<Key>\` + Admin Token |
| Suspend a sub-admin | Tenants → Suspend |
| Resume a sub-admin | Tenants → Resume |
| Push Host update silently | Bump `HOST_VERSION` → build → upload → update Render: `HOST_DOWNLOAD_URL` + `HOST_EXE_SHA256` + `HOST_VERSION` |
| Push Viewer update silently | 1. Bump `VIEWER_VERSION` in `.env` → 2. `build.ps1 -PublishViewer` → 3. Upload `dist\<date>\viewer\Viewer.exe` to new GitHub Release → 4. Update Render: `VIEWER_VERSION` + `VIEWER_DOWNLOAD_URL` + `VIEWER_EXE_SHA256` → Save |
| Deploy relay server update | `git push origin main` |
| Update SHA-256 after a publish | Copy hash printed by `build.ps1` → Render Environment → `HOST_EXE_SHA256` or `VIEWER_EXE_SHA256` → Save |