# ============================================================
# build.ps1 - RemoteAdminTool Master Build Script
# ============================================================
# Usage:
#   .\build.ps1                                           - patch constants + build all
#   .\build.ps1 -PublishViewer                            - build + publish Viewer EXE
#   .\build.ps1 -PublishHostForTenant <key> -HostToken <token>  - build + publish Host EXE + host.key
#   .\build.ps1 -PublishAll                               - publish Viewer + build
#
# Full workflow when creating a new sub-admin:
#   1. In the Viewer, create the tenant and copy its LicenseKey AND HostToken
#   2. Run BuildHostForSubAdmin.bat (it calls this script with both values)
#   3. Grab the complete package in dist\<date>\subadmin_<key>\ - send to the sub-admin
#   4. Sub-admin shares both files with their users (both must stay in same folder)
#
# Auto-update workflow (when you push a new Host/Viewer build):
#   1. Bump HOST_VERSION or VIEWER_VERSION in .env
#   2. Run .\build.ps1 -PublishAll  (or -PublishHostForTenant for Host only)
#   3. Upload the new EXE to your download URL (GitHub Releases, etc.)
#   4. Copy the SHA-256 hash printed below into Render env vars
#   5. Set HOST_DOWNLOAD_URL / VIEWER_DOWNLOAD_URL in Render env vars
#   6. Redeploy relay - existing installs self-update silently on next startup
# ============================================================

param(
    [string]$PublishHostForTenant = "",   # LicenseKey for a specific sub-admin
    [string]$HostToken            = "",   # Per-tenant HostToken (from Viewer Details dialog)
    [string]$TenantName           = "",   # Optional display name (e.g. AcmeCorp) for folder naming
    [switch]$PublishViewer,
    [switch]$PublishHostDev,              # Rebuild Host.exe only - keeps existing host.key/host.token
    [switch]$PublishAll
)

$root    = $PSScriptRoot
$envFile = Join-Path $root ".env"

# ---- Parse .env ----
if (-not (Test-Path $envFile)) {
    Write-Error ".env file not found at $envFile"
    exit 1
}

$envVars = @{}
Get-Content $envFile | Where-Object { $_ -match "^\s*([^#=\s][^=]*)=(.*)" } | ForEach-Object {
    $key, $value = $_ -split "=", 2
    $envVars[$key.Trim()] = $value.Trim()
}

$relayUrl      = $envVars["RELAY_URL"]
$globalToken   = $envVars["Auth__HostToken"]
$hostVersion   = if ($envVars["HOST_VERSION"])   { $envVars["HOST_VERSION"] }   else { "1.0.0" }
$viewerVersion = if ($envVars["VIEWER_VERSION"]) { $envVars["VIEWER_VERSION"] } else { "1.0.0" }

# For -PublishHostForTenant: use the per-tenant HostToken if provided, otherwise fall back to global
# (global is used for plain builds / Viewer; per-tenant overrides for Host EXE builds)
$effectiveHostToken = if ($HostToken) { $HostToken } else { $globalToken }

# Derive HTTP base URL from relay WebSocket URL  (wss://host/relay -> https://host)
$relayHttpUrl = $relayUrl -replace "^wss://", "https://" -replace "^ws://", "http://"
$relayHttpUrl = $relayHttpUrl -replace "/relay$", ""

if (-not $relayUrl)     { Write-Error "RELAY_URL is not set in .env";       exit 1 }
if (-not $globalToken)  { Write-Error "Auth__HostToken is not set in .env"; exit 1 }

Write-Host ""
Write-Host "=== RemoteAdminTool Build ===" -ForegroundColor Cyan
Write-Host "  RELAY_URL      = $relayUrl"
Write-Host "  GLOBAL_TOKEN   = $($globalToken.Substring(0,[Math]::Min(6,$globalToken.Length)))..."
Write-Host "  HOST_VERSION   = $hostVersion"
Write-Host "  VIEWER_VERSION = $viewerVersion"
if ($PublishHostForTenant) {
    Write-Host "  LICENSE_KEY    = $($PublishHostForTenant.Substring(0,[Math]::Min(6,$PublishHostForTenant.Length)))..."
    Write-Host "  HOST_TOKEN     = $($effectiveHostToken.Substring(0,[Math]::Min(6,$effectiveHostToken.Length)))..."
}
Write-Host ""

# ---- Obfuscation Helpers (prevents plaintext URLs/tokens in PE string table) ----
function New-RandomKeyBytes() {
    $k   = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($k)
    $rng.Dispose()
    return $k
}

function Format-ByteArray([byte[]]$bytes) {
    if (-not $bytes -or $bytes.Length -eq 0) { return "" }
    return ($bytes | ForEach-Object { "0x{0:X2}" -f $_ }) -join ", "
}

function Encode-ObfuscatedString([string]$plain, [byte[]]$key) {
    if ([string]::IsNullOrEmpty($plain)) { return "" }
    $utf8 = [System.Text.Encoding]::UTF8.GetBytes($plain)
    $out  = New-Object byte[] $utf8.Length
    for ($i = 0; $i -lt $utf8.Length; $i++) {
        $salt    = (($i * 31 + 17) -band 0xFF)
        $out[$i] = [byte]($utf8[$i] -bxor $key[$i % $key.Length] -bxor $salt)
    }
    return (Format-ByteArray $out)
}

# ---- [1/3] Patch Host/HostConstants.cs ----
$hostConstantsFile = Join-Path $root "Host\HostConstants.cs"

function Set-HostConstants([string]$licHash = "", [string]$tok = "") {
    $k        = New-RandomKeyBytes
    $kLit     = Format-ByteArray $k
    $urlLit   = Encode-ObfuscatedString $relayUrl $k
    $httpLit  = Encode-ObfuscatedString $relayHttpUrl $k
    $verLit   = Encode-ObfuscatedString $hostVersion $k
    $licLit   = Encode-ObfuscatedString $licHash $k
    $tokLit   = Encode-ObfuscatedString $tok $k

    Set-Content -Path $hostConstantsFile -Encoding UTF8 -Value @"
namespace RemoteAdminTool.Host;

/// <summary>
/// Compiled-in obfuscated constants for the Host agent.
/// AUTO-GENERATED by build.ps1 - do not edit manually.
/// </summary>
internal static class HostConstants
{
    private static readonly byte[] _k    = [ $kLit ];
    private static readonly byte[] _url  = [ $urlLit ];
    private static readonly byte[] _http = [ $httpLit ];
    private static readonly byte[] _ver  = [ $verLit ];
    private static readonly byte[] _lic  = [ $licLit ];
    private static readonly byte[] _tok  = [ $tokLit ];

    public static string RelayUrl              => Decode(_url);
    public static string RelayHttpUrl          => Decode(_http);
    public static string AppVersion            => Decode(_ver);
    public static string DefaultLicenseKeyHash => Decode(_lic);
    public static string DefaultHostToken      => Decode(_tok);

    private static string Decode(byte[] data)
    {
        if (data.Length == 0) return string.Empty;
        var buf = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            buf[i] = (byte)(data[i] ^ _k[i % _k.Length] ^ ((i * 31 + 17) & 0xFF));
        return System.Text.Encoding.UTF8.GetString(buf);
    }
}
"@
}

Write-Host "[1/3] Patching Host/HostConstants.cs (obfuscated)..." -ForegroundColor Yellow
Set-HostConstants "" ""
Write-Host "  Done." -ForegroundColor Green

# ---- [2/3] Patch Viewer/ViewerConstants.cs ----
$viewerConstantsFile = Join-Path $root "Viewer\ViewerConstants.cs"
Write-Host "[2/3] Patching Viewer/ViewerConstants.cs (obfuscated)..." -ForegroundColor Yellow

$vk       = New-RandomKeyBytes
$vkLit    = Format-ByteArray $vk
$vUrlLit  = Encode-ObfuscatedString $relayUrl $vk
$vHttpLit = Encode-ObfuscatedString $relayHttpUrl $vk
$vTokLit  = Encode-ObfuscatedString $globalToken $vk
$vVerLit  = Encode-ObfuscatedString $viewerVersion $vk

Set-Content -Path $viewerConstantsFile -Encoding UTF8 -Value @"
namespace RemoteAdminTool.Viewer;

/// <summary>
/// Compiled-in obfuscated constants for the Viewer.
/// AUTO-GENERATED by build.ps1 - do not edit manually.
/// </summary>
internal static class ViewerConstants
{
    private static readonly byte[] _k    = [ $vkLit ];
    private static readonly byte[] _url  = [ $vUrlLit ];
    private static readonly byte[] _http = [ $vHttpLit ];
    private static readonly byte[] _tok  = [ $vTokLit ];
    private static readonly byte[] _ver  = [ $vVerLit ];

    public static string RelayUrl     => Decode(_url);
    public static string RelayHttpUrl => Decode(_http);
    public static string HostToken    => Decode(_tok);
    public static string AppVersion   => Decode(_ver);

    private static string Decode(byte[] data)
    {
        if (data.Length == 0) return string.Empty;
        var buf = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            buf[i] = (byte)(data[i] ^ _k[i % _k.Length] ^ ((i * 31 + 17) & 0xFF));
        return System.Text.Encoding.UTF8.GetString(buf);
    }
}
"@
Write-Host "  Done." -ForegroundColor Green

# ---- [3/3] Build solution ----
Write-Host "[3/3] Building solution..." -ForegroundColor Yellow
$buildOutput = & dotnet build "$root\RemoteAdminTool.sln" -c Release -m:1 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host $buildOutput
    Write-Error "Build failed."
    exit 1
}
Write-Host "  Build succeeded." -ForegroundColor Green

# Helper: compute SHA-256 of a file and return hex string
function Get-FileSha256([string]$path) {
    $hash = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hashBytes = $hash.ComputeHash($bytes)
    return [System.BitConverter]::ToString($hashBytes).Replace("-","").ToLowerInvariant()
}

# ---- Publish Viewer (Super Admin app) ----
if ($PublishViewer -or $PublishAll) {
    Write-Host ""
    Write-Host "Publishing Viewer (Super Admin EXE)..." -ForegroundColor Yellow
    $dateStr    = (Get-Date).ToString("yyyy-MM-dd")
    $distViewer = Join-Path $root "dist\$dateStr\viewer"
    New-Item -ItemType Directory -Path $distViewer -Force | Out-Null

    & dotnet publish "$root\Viewer\Viewer.csproj" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $distViewer 2>&1 | Where-Object { $_ -match "error|warning|published" }
    if ($LASTEXITCODE -eq 0) {
        Get-ChildItem $distViewer -Exclude "Viewer.exe" | Remove-Item -Force
        $viewerExe = Join-Path $distViewer "Viewer.exe"
        $viewerHash = Get-FileSha256 $viewerExe

        Write-Host "  Viewer EXE -> $distViewer\Viewer.exe" -ForegroundColor Green
        Write-Host ""
        Write-Host "  SHA-256 (paste into Render env var VIEWER_EXE_SHA256):" -ForegroundColor Yellow
        Write-Host "  $viewerHash" -ForegroundColor Cyan
    }
}

# ---- Publish Host for a specific sub-admin (100% Standalone Single-File EXE) ----
if ($PublishHostForTenant) {
    Write-Host ""
    Write-Host "Publishing standalone Host.exe for sub-admin (LicenseKey=$($PublishHostForTenant.Substring(0, [Math]::Min(6, $PublishHostForTenant.Length)))...)..." -ForegroundColor Yellow
    $dateStr        = (Get-Date).ToString("yyyy-MM-dd")
    $tenantPrefix   = $PublishHostForTenant.Substring(0, [Math]::Min(8, $PublishHostForTenant.Length))
    $cleanName      = if ($TenantName) { ($TenantName -replace '[^a-zA-Z0-9_\-]', '') + "_" } else { "" }
    $folderName     = "subadmin_${cleanName}${tenantPrefix}"
    $distTenantHost = Join-Path $root "dist\$dateStr\$folderName"
    New-Item -ItemType Directory -Path $distTenantHost -Force | Out-Null

    # Compute SHA-256(LicenseKey) and bake both LicenseKeyHash and HostToken directly into Host.exe
    $licenseBytes  = [System.Text.Encoding]::UTF8.GetBytes($PublishHostForTenant)
    $sha256        = [System.Security.Cryptography.SHA256]::Create()
    $hashBytes     = $sha256.ComputeHash($licenseBytes)
    $licenseHash   = [System.BitConverter]::ToString($hashBytes).Replace("-","").ToLowerInvariant()

    try {
        Set-HostConstants $licenseHash $effectiveHostToken
        & dotnet publish "$root\Host\Host.csproj" -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $distTenantHost 2>&1 | Where-Object { $_ -match "error|warning|published" }
        $pubExit = $LASTEXITCODE
    }
    finally {
        # Always clear tenant secrets from source tree after compile
        Set-HostConstants "" ""
    }

    if ($pubExit -eq 0) {
        # Keep ONLY Host.exe - no .key, .token, or .json sidecar files needed!
        Get-ChildItem $distTenantHost -Exclude "Host.exe" | Remove-Item -Force -Recurse

        # Also copy the latest Viewer.exe into the sub-admin folder if available so the ZIP has both EXEs ready
        $latestViewer = Get-ChildItem (Join-Path $root "dist") -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "viewer\Viewer.exe" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($latestViewer) {
            Copy-Item $latestViewer (Join-Path $distTenantHost "Viewer.exe") -Force
        }

        # Compute SHA-256 of the Host.exe itself (for auto-update verification)
        $hostExe  = Join-Path $distTenantHost "Host.exe"
        $hostHash = Get-FileSha256 $hostExe

        # Create a ready-to-share .zip containing ONLY the standalone EXE(s) (Host.exe + Viewer.exe)
        $prevProgress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        $zipBase = if ($TenantName) { ($TenantName -replace '[^a-zA-Z0-9_\-]', '') + "_Host_Package.zip" } else { "subadmin_${tenantPrefix}_Host_Package.zip" }
        $zipPath = Join-Path $distTenantHost $zipBase
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        $exeFilesToZip = Get-ChildItem $distTenantHost -Filter "*.exe" | Select-Object -ExpandProperty FullName
        Compress-Archive -Path $exeFilesToZip -DestinationPath $zipPath -Force
        $ProgressPreference = $prevProgress

        $displayNameLine = if ($TenantName) { "Tenant / Client:    $TenantName`n" } else { "" }
        $subAdminReadme = @"
RemoteAdminTool - Standalone Sub-Admin Package
Generated:          $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
$displayNameLine
Tenant License Key: $PublishHostForTenant
Host Auth Token:    $effectiveHostToken
Host EXE SHA-256:   $hostHash

HOW IT WORKS (SINGLE-FILE EXE):
- Host.exe is 100% standalone (your tenant credentials are compiled inside Host.exe).
- No host.key, host.token, or appsettings.json files are needed!
- You can deploy Host.exe (or rename it) on as many host PCs as your plan allows.
- Double-clicking Host.exe runs it silently in the tray and registers it to auto-start on PC reboot.
"@
        Set-Content -Path (Join-Path $distTenantHost "README_FOR_SUBADMIN.txt") -Encoding UTF8 -Value $subAdminReadme

        Write-Host "  Host Package -> $distTenantHost" -ForegroundColor Green
        Write-Host "  Host.exe     -> $distTenantHost\Host.exe (100% standalone single-file EXE!)" -ForegroundColor Green
        if ($latestViewer) {
            Write-Host "  Viewer.exe   -> $distTenantHost\Viewer.exe (copied into package)" -ForegroundColor Green
        }
        Write-Host "  ZIP Package  -> $zipPath (contains ONLY the standalone EXE files)" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  SHA-256 (paste into Render env var HOST_EXE_SHA256 if releasing an update):" -ForegroundColor Yellow
        Write-Host "  $hostHash" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  Ready to send to sub-admin:" -ForegroundColor Yellow
        Write-Host "    $zipPath" -ForegroundColor White
    }
}

# ---- Dev Host publish (bakes dev keys from .env directly into Host.exe - zero .key/.token files!) ----
if ($PublishHostDev) {
    Write-Host ""
    Write-Host "Publishing standalone Host.exe for local dev testing..." -ForegroundColor Yellow
    $dateStr     = (Get-Date).ToString("yyyy-MM-dd")
    $distDevHost = Join-Path $root "dist\$dateStr\host_dev"

    $devKeyHash = $envVars["DEV_LICENSE_KEY_HASH"]
    $devToken   = $envVars["DEV_HOST_TOKEN"]

    if (-not $devKeyHash -or -not $devToken) {
        Write-Error "DEV_LICENSE_KEY_HASH or DEV_HOST_TOKEN not found in .env - run MakeDevHostKey.ps1 first."
    } else {
        New-Item -ItemType Directory -Path $distDevHost -Force | Out-Null
        try {
            Set-HostConstants $devKeyHash $devToken
            & dotnet publish "$root\Host\Host.csproj" -c Release -r win-x64 --self-contained true `
                -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
                -o $distDevHost 2>&1 | Where-Object { $_ -match "error|warning|published" }
            $devPubExit = $LASTEXITCODE
        }
        finally {
            Set-HostConstants "" ""
        }

        if ($devPubExit -eq 0) {
            # Keep ONLY Host.exe (and logs folder if present) - zero .key or .token files!
            Get-ChildItem $distDevHost -Exclude "Host.exe","logs" | Remove-Item -Force -Recurse

            Write-Host "  Host.exe rebuilt -> $distDevHost\Host.exe (100% standalone single-file EXE!)" -ForegroundColor Green
        }
    }
}

if ($PublishAll) {
    Write-Host ""
    Write-Host "Note: -PublishAll published the Viewer only." -ForegroundColor Yellow
    Write-Host "To publish a Host EXE for a sub-admin, run BuildHostForSubAdmin.bat" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Quick reference:" -ForegroundColor White
Write-Host "  Super admin logs in with:  Admin Token from .env (Auth__SuperAdminToken)"
Write-Host "  Sub-admin logs in with:    Their AdminToken (click [Details] in Viewer)"
Write-Host "  Host EXE for sub-admin:    Double-click BuildHostForSubAdmin.bat"
Write-Host ""
Write-Host "Auto-update workflow:" -ForegroundColor White
Write-Host "  1. Bump HOST_VERSION or VIEWER_VERSION in .env"
Write-Host "  2. Run BuildHostForSubAdmin.bat (or .\build.ps1 -PublishViewer)"
Write-Host "  3. Upload new EXE to GitHub Releases - copy SHA-256 hash printed above"
Write-Host "  4. Set HOST_EXE_SHA256 / VIEWER_EXE_SHA256 in Render env vars"
Write-Host "  5. Set HOST_DOWNLOAD_URL / VIEWER_DOWNLOAD_URL in Render env vars + redeploy"
Write-Host "  6. All running Hosts/Viewers auto-update silently on next startup"
Write-Host ""
