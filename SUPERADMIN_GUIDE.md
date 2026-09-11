# RemoteAdminTool — Super Admin Operations Guide

This guide tells you exactly what to do, step by step, with no coding or VS required.
Keep it open whenever you are operating the software.

---

## What You Have

Three components, three audiences:

| Component | Who uses it | What it does |
|-----------|-------------|-------------|
| **RemoteAdmin.exe** (the Viewer) | You (super admin) AND every sub-admin | The control panel — log in with your token to manage everything; sub-admins log in with their token to see their machines |
| **Host.exe** | End users (the PCs being remotely controlled) | Runs silently in the system tray; lets the sub-admin connect |
| **Relay Server** | Nobody touches this | Runs on Render 24/7; connects everything together |

---

## One-Time Setup (Already Done — For Reference)

- Relay server is live at: https://remoteadmintool.onrender.com
- Your super admin token is in the .env file (Auth__SuperAdminToken)
- Your RemoteAdmin.exe is already on your Desktop

---

## Part 1: Your Daily App — RemoteAdmin.exe

### Opening it

Double-click **RemoteAdmin.exe** on your Desktop.

### Logging in as Super Admin

1. In the **Admin Token** field, type your super admin token (from .env → Auth__SuperAdminToken)
2. Leave **License Key** completely empty
3. Click **Connect to Relay**
4. You are now connected. The **Tenants** tab appears at the top — sub-admins do NOT see this tab

### What you can do once connected

- **Nodes tab** — See every host machine from every sub-admin currently online
- **Tenants tab** — Manage your sub-admins (create, view credentials, suspend, resume)
- **Click any node** — Start a remote session with that machine

---

## Part 2: Adding a New Sub-Admin

Do this whenever a new sub-admin joins your platform.

### Step 1 — Create the tenant

1. Open RemoteAdmin.exe and log in as super admin
2. Click **Tenants** tab
3. Click **+ New**
4. Type the sub-admin's name and email → **Create Tenant**
5. A credentials dialog appears. **Do not close it yet.**

### Step 2 — Copy the License Key

In the credentials dialog, click **Copy** next to **License Key**.
Paste it somewhere temporary (Notepad).

The dialog also shows the **Admin Token** — copy that too. You will give it to the sub-admin.

### Step 3 — Build the Host EXE for this sub-admin

Open a PowerShell window in your project folder:

```
Right-click the Start button → "Terminal" or "Windows PowerShell"
Then type:
cd C:\c#-projects\RemoteAdminTool
.\build.ps1 -PublishHostForTenant PASTE_LICENSE_KEY_HERE
```

Example:
```
.\build.ps1 -PublishHostForTenant def456abc789...
```

This takes about 1 minute. When done it says:
```
Host EXE -> C:\c#-projects\RemoteAdminTool\dist\Host-def456\Host.exe
```

### Step 4 — What to send the sub-admin

Send the sub-admin TWO things:

1. **Host.exe** — from `C:\c#-projects\RemoteAdminTool\dist\Host-def456\`
   They distribute this to every PC they want to control

2. **Their Admin Token** — from the credentials dialog
   They type this into RemoteAdmin.exe to log in

3. **RemoteAdmin.exe** — your Viewer app (same one you use)
   They use it to see and control their machines

---

## Part 3: What Sub-Admins Do

Tell your sub-admin exactly this:

### To log into the Viewer (RemoteAdmin.exe)

1. Open RemoteAdmin.exe
2. Paste your Admin Token in the **Admin Token** field
3. Leave License Key empty
4. Click **Connect to Relay**
5. You will see your Nodes tab with all your machines

### To install the Host on a PC

1. Copy **Host.exe** to the PC (USB, email, shared drive — any way)
2. Double-click it — it starts immediately and sits in the system tray
3. To make it start automatically: right-click → "Open file location" → copy shortcut to the Startup folder

### To connect to a machine

1. Log into Viewer
2. See the machine appear in the Nodes list (it must have Host.exe running)
3. Click the machine → **Connect**
4. A consent dialog appears on the user's screen — they click Allow
5. The remote session starts

---

## Part 4: Rebuilding Your Own Viewer EXE

Do this when you rotate tokens or change the relay URL.

1. Edit `.env` file (the only file you ever touch)
2. Open PowerShell in project folder
3. Run:
```
.\build.ps1 -PublishViewer
```
4. New EXE appears at `dist\Viewer\Viewer.exe`
5. Copy it to your Desktop (replaces the old one)

---

## Part 5: Viewing Credentials for an Existing Sub-Admin

If you forgot a sub-admin's token or need to rebuild their Host EXE:

1. Open RemoteAdmin.exe → log in as super admin
2. Click **Tenants** tab
3. Find the sub-admin → click **Details**
4. The credentials dialog opens — copy License Key or Admin Token

---

## Part 6: Suspending / Resuming a Sub-Admin

- **Suspend** — blocks their Host machines from connecting. Use if they stop paying or cause problems.
- **Resume** — re-activates them.

1. Tenants tab → find the sub-admin
2. Click **Suspend** or **Resume**

---

## Part 7: Deploying Relay Server Updates

When you or your developer pushes code changes:

```
git add .
git commit -m "description of change"
git push origin main
```

Render automatically redeploys. Takes about 2 minutes. The relay stays live during the update.

---

## Quick Reference

| Task | Command / Action |
|------|-----------------|
| Open your admin app | Double-click RemoteAdmin.exe on Desktop |
| Log in as super admin | Admin Token = Auth__SuperAdminToken from .env, License Key = empty |
| Create sub-admin | Tenants → + New |
| Get sub-admin credentials | Tenants → Details |
| Build Viewer EXE | PowerShell: `.\build.ps1 -PublishViewer` |
| Build Host EXE for sub-admin | PowerShell: `.\build.ps1 -PublishHostForTenant <LicenseKey>` |
| Suspend sub-admin | Tenants → Suspend |
| Resume sub-admin | Tenants → Resume |
| Deploy relay update | `git push origin main` |

---

## Your Tokens (fill in and store securely)

| Token | Value |
|-------|-------|
| Super Admin Token | (see Auth__SuperAdminToken in .env) |
| Relay URL | wss://remoteadmintool.onrender.com/relay |
| Render Dashboard | https://dashboard.render.com |
