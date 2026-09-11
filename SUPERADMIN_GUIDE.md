# RemoteAdminTool — Super Admin Operations Guide

This guide tells you exactly what to do, step by step, with no coding required.
Keep it open whenever you are operating the software.

---

## What You Have

Three components, three audiences:

| Component | Who uses it | What it does |
|-----------|-------------|-------------|
| **RemoteAdmin.exe** (the Viewer) | You (super admin) AND every sub-admin | The control panel. You log in with your token to manage everything. Sub-admins log in with their token to see their machines. |
| **Host.exe** | End users (the PCs being remotely controlled) | Runs silently in the system tray. Lets the sub-admin connect. |
| **Relay Server** | Nobody touches this | Runs on Render 24/7. Connects everything together. |

---

## Part 1: Your Daily App — RemoteAdmin.exe

### Opening it

Double-click **RemoteAdmin.exe** on your Desktop. (You publish this yourself from Visual Studio.)

### Logging in as Super Admin

1. In the **Admin Token** field, type your super admin token (from the .env file — Auth__SuperAdminToken)
2. Leave **License Key** completely empty
3. Click **Connect to Relay**
4. You are now connected. The **Tenants** tab appears at the top. Sub-admins do NOT see this tab.

### What you can do once connected

- **Nodes tab** — See every host machine from every sub-admin currently online
- **Tenants tab** — Manage your sub-admins (create, view credentials, suspend, resume)
- **Click any node** — Start a remote session with that machine

---

## Part 2: Adding a New Sub-Admin

Do this whenever a new sub-admin joins your platform.

### Step 1 — Create the tenant in the Viewer

1. Open RemoteAdmin.exe and log in as super admin
2. Click the **Tenants** tab
3. Click **+ New**
4. Type the sub-admin's name and email → click **Create Tenant**
5. A credentials dialog appears — **do not close it yet**

### Step 2 — Copy the License Key

In the credentials dialog, click **Copy** next to **License Key** and paste it somewhere (Notepad).
Also copy the **Admin Token** — you will give this to the sub-admin.

### Step 3 — Build the Host EXE for this sub-admin

1. Go to your project folder: `C:\c#-projects\RemoteAdminTool\`
2. Double-click **BuildHostForSubAdmin.bat**
3. When prompted, paste the License Key you copied → press **Enter**
4. Wait about 1 minute while it builds
5. When done it shows: `Host EXE -> C:\c#-projects\RemoteAdminTool\dist\Host-xxx\Host.exe`

### Step 4 — What to send the sub-admin

Send the sub-admin **three things**:

| What | Where to find it | Purpose |
|------|-----------------|---------|
| **Host.exe** | `dist\Host-xxx\` folder | They distribute this to every PC they want to control |
| **Admin Token** | From the credentials dialog (Step 2) | They paste this into RemoteAdmin.exe to log in |
| **RemoteAdmin.exe** | Your own copy | Same app you use — works for sub-admins too |

---

## Part 3: What Sub-Admins Do

Tell your sub-admin exactly this:

### To log into RemoteAdmin.exe

1. Open RemoteAdmin.exe
2. Paste your **Admin Token** in the Admin Token field
3. Leave **License Key** empty
4. Click **Connect to Relay**
5. The Nodes tab shows all your machines

### To install Host.exe on a PC

1. Copy Host.exe to the PC (USB, email, shared drive — any way)
2. Double-click it — it starts immediately and sits in the system tray
3. Optional: to make it start automatically, place a shortcut in the Windows Startup folder

### To connect to a machine

1. Log into RemoteAdmin.exe
2. The machine appears in the Nodes list (Host.exe must be running on it)
3. Click the machine → **Connect**
4. A consent dialog appears on the user's screen — they click **Allow**
5. The remote session starts

---

## Part 4: Viewing Credentials for an Existing Sub-Admin

If you need to rebuild a Host EXE or share the Admin Token again:

1. Open RemoteAdmin.exe → log in as super admin
2. Click **Tenants** tab
3. Click **Details** next to the sub-admin
4. The credentials dialog opens with copy buttons for everything

---

## Part 5: Suspending / Resuming a Sub-Admin

- **Suspend** — blocks their Host machines from connecting
- **Resume** — re-activates them

1. Tenants tab → find the sub-admin
2. Click **Suspend** or **Resume**

---

## Part 6: Deploying Relay Server Updates

When code changes need to go live:

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
| Open your admin app | Double-click RemoteAdmin.exe |
| Log in as super admin | Admin Token = Auth__SuperAdminToken from .env, License Key = empty |
| Create a sub-admin | Tenants → + New |
| Get sub-admin credentials anytime | Tenants → Details |
| Build Host EXE for a sub-admin | Double-click BuildHostForSubAdmin.bat → paste License Key |
| Suspend a sub-admin | Tenants → Suspend |
| Resume a sub-admin | Tenants → Resume |
| Deploy relay update | git push origin main |