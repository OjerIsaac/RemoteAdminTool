# RemoteAdminTool — Super Admin Operations Guide

This guide tells you exactly what to do, step by step, with no coding required.
Keep it open whenever you are operating the software.

---

## What You Have

Four components, three audiences:

| Component | Who uses it | What it does |
|-----------|-------------|--------------|
| **Viewer.exe** (RemoteAdmin) | You (super admin) AND every sub-admin | The control panel. Log in with your token to manage everything. Sub-admins log in with their token to see their own machines. |
| **Host.exe + host.key** | End users (PCs being remotely controlled) | Host.exe runs silently in the system tray. `host.key` holds the tenant's license. Both files must stay together. |
| **Relay Server** | Nobody touches this | Runs on Render 24/7. Connects everything together. |
| **.env** | You only | Your local secrets file. Never share or commit this. |

---

## Part 1: Your Daily App — Viewer.exe

### Opening it

Double-click **Viewer.exe** (publish it from Visual Studio using the Standalone publish profile, or run `build.ps1 -PublishViewer`). The output goes to `Viewer\dist\Viewer.exe`.

### Logging in as Super Admin

1. In the **Admin Token** field, paste your super admin token (from `.env` → `Auth__SuperAdminToken`)
2. Leave **License Key** completely blank
3. Click **Connect to Relay**
4. You are now connected — the **Tenants** tab appears. Sub-admins do NOT see this tab.

### Logging out

Click the red **Logout** button in the top-left bar at any time to disconnect cleanly.

### What you can do once connected

- **Nodes tab** — Every host machine from every sub-admin currently online
- **Tenants tab** — Manage sub-admins (create, view credentials, suspend, resume)
- **Click any node** — Start a remote session instantly

### Remote session tabs

Once connected to a node, three tabs appear in the toolbar:

| Tab | What it shows |
|-----|---------------|
| **🖥 Screen** | Live remote desktop — full mouse and keyboard control |
| **⚡ Console** | Interactive PowerShell terminal on the remote machine |
| **📊 Monitor** | Live CPU, RAM, disk, network stats + top processes |

---

## Part 2: Adding a New Sub-Admin

Do this whenever a new sub-admin joins your platform.

### Step 1 — Create the tenant in the Viewer

1. Open Viewer.exe and log in as super admin
2. Click the **Tenants** tab
3. Click **+ New**
4. Type the sub-admin's name and email → click **Create Tenant**
5. A credentials dialog appears — **do not close it yet**

### Step 2 — Copy the credentials

In the credentials dialog, copy:
- **License Key** → paste into Notepad (you need this in Step 3)
- **Admin Token** → save this to give to the sub-admin

### Step 3 — Build Host.exe for this sub-admin

1. Go to your project folder: `C:\c#-projects\RemoteAdminTool\`
2. Double-click **BuildHostForSubAdmin.bat**
3. When prompted, paste the **License Key** → press **Enter**
4. Wait about 1 minute while it builds
5. When done, you will see:
   ```
   Host EXE -> Host\dist\Host.exe
   host.key -> Host\dist\host.key
   ```

### Step 4 — What to send the sub-admin

Send the sub-admin **four things**:

| What | Where to find it | Purpose |
|------|-----------------|---------|
| **Host.exe** | `Host\dist\Host.exe` | They distribute this to every PC they want to control |
| **host.key** | `Host\dist\host.key` | Their unique license key — must stay in the same folder as Host.exe |
| **Admin Token** | From the credentials dialog (Step 2) | They paste this into Viewer.exe to log in |
| **Viewer.exe** | `Viewer\dist\Viewer.exe` | Same app you use — works for sub-admins too |

> **Important:** `Host.exe` and `host.key` must always be in the same folder on every end-user machine. Never separate them.

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

1. Copy **both** `Host.exe` and `host.key` to the PC (USB, email, shared drive — any way)
2. Keep both files in the **same folder**
3. Double-click **Host.exe** — it starts immediately and sits silently in the system tray (no window)
4. Optional: to auto-start on boot, right-click `Host.exe` → Create Shortcut → paste the shortcut into:
   `C:\Users\<username>\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup`

### To connect to a machine

1. Log into Viewer.exe
2. The machine appears in the Nodes list (Host.exe must be running on it)
3. Click the machine → it connects automatically
4. A consent dialog appears on the user's screen — they click **Allow**
5. The remote session starts

---

## Part 4: Viewing Credentials for an Existing Sub-Admin

If you need to rebuild a Host EXE or share the Admin Token again:

1. Open Viewer.exe → log in as super admin
2. Click **Tenants** tab
3. Click **Details** next to the sub-admin
4. The credentials dialog opens with copy buttons for everything

---

## Part 5: Suspending / Resuming a Sub-Admin

- **Suspend** — blocks their Host machines from connecting (e.g. non-payment)
- **Resume** — re-activates them

1. Tenants tab → find the sub-admin
2. Click **Suspend** or **Resume**

---

## Part 6: Pushing Updates to Host.exe (Auto-Update)

When you release a new version of Host.exe, every existing installation updates itself silently on next startup — no resharing required.

### Step 1 — Bump the version

Open `.env` and increase `HOST_VERSION`:
```
HOST_VERSION=1.0.1    ← change this
```

### Step 2 — Build the new Host.exe

Run `BuildHostForSubAdmin.bat` with **any** existing license key (just to produce a fresh generic `Host.exe`). Or use build.ps1 directly:
```
.\build.ps1 -PublishHostForTenant <any-license-key>
```
This produces `Host\dist\Host.exe` — a generic binary, same for all tenants.

### Step 3 — Upload to GitHub Releases (get the download URL)

1. Go to your GitHub repo → click **Releases** (right side of the page)
2. Click **Draft a new release**
3. Click **Choose a tag** → type `v1.0.1` → click **Create new tag: v1.0.1**
4. Give it a title (e.g. `Host v1.0.1`)
5. Under **Attach binaries**, drag and drop `Host\dist\Host.exe`
6. Click **Publish release**
7. After publishing, right-click the `Host.exe` file listed under Assets → **Copy link address**

The URL will look exactly like this:
```
https://github.com/<your-username>/<your-repo>/releases/download/v1.0.1/Host.exe
```

For example:
```
https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Host.exe
```

That is your `HOST_DOWNLOAD_URL`.

### Step 4 — Set the URL in Render

1. Go to [render.com](https://render.com) → your relay web service → **Environment** tab
2. Find (or add) these two variables:
   ```
   HOST_VERSION      = 1.0.1
   HOST_DOWNLOAD_URL = https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Host.exe
   ```
3. Click **Save Changes** — Render redeploys automatically (~2 min)

### Step 5 — Done

On next startup, every running `Host.exe` will:
1. Check `/api/version` on your relay
2. See the newer version
3. Download the new `Host.exe` silently in the background
4. Replace itself and restart — user sees nothing

> **Note:** `host.key` is NEVER replaced during auto-update. Each machine keeps its original `host.key` forever.

---

## Part 7: Pushing Updates to Viewer.exe (Auto-Update)

Same process as Host, but for the sub-admin app.

### Step 1 — Bump the version

In `.env`:
```
VIEWER_VERSION=1.0.1
```

### Step 2 — Build the new Viewer.exe

```
.\build.ps1 -PublishViewer
```
Output: `Viewer\dist\Viewer.exe`

### Step 3 — Upload to GitHub Releases

Same as Part 6 Step 3, but upload `Viewer\dist\Viewer.exe`.

You can attach **both** `Host.exe` and `Viewer.exe` to the same release tag if both changed.

The URL will be:
```
https://github.com/<your-username>/<your-repo>/releases/download/v1.0.1/Viewer.exe
```

### Step 4 — Set the URL in Render

```
VIEWER_VERSION      = 1.0.1
VIEWER_DOWNLOAD_URL = https://github.com/OjerIsaac/RemoteAdminTool/releases/download/v1.0.1/Viewer.exe
```

Save → Render redeploys → sub-admins' Viewer.exe silently updates itself on next launch.

---

## Part 8: Deploying Relay Server Updates

When the relay server code changes need to go live:

```
git add .
git commit -m "description of change"
git push origin main
```

Render automatically redeploys. Takes about 2 minutes.

---

## Quick Reference

| Task | Action |
|------|--------|
| Open your admin app | Double-click `Viewer\dist\Viewer.exe` |
| Log in as super admin | Admin Token = `Auth__SuperAdminToken` from `.env`, License Key = blank |
| Log out | Click red **Logout** button |
| Create a sub-admin | Tenants → + New |
| Get sub-admin credentials anytime | Tenants → Details |
| Build Host for a sub-admin | Double-click `BuildHostForSubAdmin.bat` → paste License Key |
| Files to send sub-admin | `Host.exe` + `host.key` + `Viewer.exe` + Admin Token |
| Suspend a sub-admin | Tenants → Suspend |
| Resume a sub-admin | Tenants → Resume |
| Push Host update silently | Bump `HOST_VERSION` in `.env` → build → upload → update Render env vars |
| Push Viewer update silently | Bump `VIEWER_VERSION` in `.env` → `build.ps1 -PublishViewer` → upload → update Render env vars |
| Deploy relay server update | `git push origin main` |