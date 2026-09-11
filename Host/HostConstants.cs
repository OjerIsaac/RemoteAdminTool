namespace RemoteAdminTool.Host;

/// <summary>
/// Compiled-in constants for the Host agent.
/// These are baked into the binary at build time — not readable from a plain
/// text editor like appsettings.json is. Changing them requires a rebuild.
///
/// Per-installation values (LicenseKey, MachineId, MachineName, UnattendedMode)
/// remain in appsettings.json so they can be set during distribution.
/// </summary>
internal static class HostConstants
{
    /// <summary>WebSocket URL of the central relay server.</summary>
    public const string RelayUrl = "wss://remoteadmintool.onrender.com/relay";

    /// <summary>
    /// Shared host registration token. Must match Auth:HostToken on the relay.
    /// All hosts across all tenants use this same token.
    /// </summary>
    public const string HostToken = "d6a6ac2563a8443ea686346c";
}
