# RemoteAdminTool — scaffold

A minimal two-sided remote desktop tool for accessing your own server. Not a general-purpose
product — built for a single owner, single machine, single session at a time.

## Structure

```
RemoteAdminTool.sln
Shared/           Wire protocol (message types, framing) used by both sides
Host/             Runs on the server. Captures screen, injects input, always shows a tray icon.
Viewer/           WPF app you run on your laptop/wherever. Connects out, shows the remote screen.
```

## How it talks

Raw TCP, length-prefixed frames: `[4-byte length][1-byte MessageType][JSON or binary payload]`.
See `Shared/Protocol.cs` and `Shared/MessageFraming.cs`. Screen frames are JPEG bytes; input
events are small JSON structs.

## Running it (once built in Visual Studio / `dotnet build`)

1. Run `Host` on the server. It listens on port 5900 and shows a tray icon at all times —
   text changes when a viewer connects.
2. Run `Viewer` anywhere else. Enter the host address, port, and the shared token, hit Connect.

**This assumes you're already on a private network path to the server** — Tailscale, WireGuard,
or an SSH/VPN tunnel. Nothing here does NAT traversal or port forwarding for you; point
`HostBox` at whatever address that private network gives the server.

## What's deliberately left rough (by design, for you to harden before relying on this)

- **Auth token is hardcoded** in `Host/Program.cs` (`CHANGE_ME_BEFORE_USE`). Move it to an
  environment variable or a config file that isn't checked into source control.
- **No TLS on the socket itself.** Fine if you're only ever running this over a VPN tunnel
  (Tailscale/WireGuard already encrypt the link). If you ever expose the raw port directly to
  the internet, wrap the stream in `SslStream` first — don't skip this step in that case.
- **Frame codec is naive** — full JPEG per frame at a fixed 15fps. Works, but wastes bandwidth.
  A real implementation would send only changed screen regions (delta encoding) or use
  hardware encoding via DXGI Desktop Duplication.
- **Single session only** — a second Viewer connecting will just wait for `AcceptTcpClientAsync`
  to loop back around after the first session ends. Fine for solo use.
- **`SetCursorPos`/`SendInput` require the Host process to be running in an interactive desktop
  session** (not as a Session-0 service) to actually move things on screen. Run it as a normal
  logged-in app, not a Windows Service, unless you add session-attach logic.

## Suggested next steps

1. Get it building and connecting locally (loopback, `127.0.0.1`) first before testing over Tailscale.
2. Swap the hardcoded token for an env var.
3. Set up Tailscale on both machines, point Viewer at the server's Tailscale IP/hostname.
4. Once basic version works, consider: delta-frame encoding, clipboard sync, multi-monitor
   selection, auto-reconnect, and moving the token to a proper `appsettings.json` + user secrets.
