# MakeDevHostKey.ps1
# ============================================================
# Creates host.key AND host.token for local dev/debugging.
# All output is saved exclusively under dist\YYYY-MM-DD\host_dev\.
#
# Usage:
#   .\MakeDevHostKey.ps1 -LicenseKey <paste-license-key> -HostToken <paste-host-token>
# ============================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$LicenseKey,

    [Parameter(Mandatory=$true)]
    [string]$HostToken
)

$root        = $PSScriptRoot
$dateStr     = (Get-Date).ToString("yyyy-MM-dd")
$distDevHost = Join-Path $root "dist\$dateStr\host_dev"
$binDebug    = Join-Path $root "Host\bin\Debug\net8.0-windows"

# 1. Compute SHA-256 of the raw license key
$bytes     = [System.Text.Encoding]::UTF8.GetBytes($LicenseKey)
$sha256    = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash($bytes)
$hash      = [System.BitConverter]::ToString($hashBytes).Replace("-","").ToLowerInvariant()

# 2. Write to dist\YYYY-MM-DD\host_dev\ (all dist files live in root dist\ by date)
New-Item -ItemType Directory -Path $distDevHost -Force | Out-Null
Set-Content -Path (Join-Path $distDevHost "host.key")   -Encoding UTF8 -NoNewline -Value $hash
Set-Content -Path (Join-Path $distDevHost "host.token") -Encoding UTF8 -NoNewline -Value $HostToken

# Copy appsettings.json into host_dev if present in Host\
$appSettings = Join-Path $root "Host\appsettings.json"
if (Test-Path $appSettings) {
    Copy-Item $appSettings (Join-Path $distDevHost "appsettings.json") -Force
}

# 3. Write to Host\bin\Debug\net8.0-windows\ if it exists (for Visual Studio F5 debugging)
if (Test-Path $binDebug) {
    Set-Content -Path (Join-Path $binDebug "host.key")   -Encoding UTF8 -NoNewline -Value $hash
    Set-Content -Path (Join-Path $binDebug "host.token") -Encoding UTF8 -NoNewline -Value $HostToken
}

# Record human-readable metadata for tracking
$metaContent = @"
Created:       $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Type:          Local Dev / Test Host
LicenseKey:    $LicenseKey
LicenseHash:   $hash
HostToken:     $HostToken
Folder:        dist\$dateStr\host_dev
"@
Set-Content -Path (Join-Path $distDevHost "license_info.txt") -Encoding UTF8 -Value $metaContent

Write-Host ""
Write-Host "Done! Keys generated in dist folder:" -ForegroundColor Green
Write-Host "  Dev Host folder: dist\$dateStr\host_dev\" -ForegroundColor Cyan
Write-Host "  host.key:        dist\$dateStr\host_dev\host.key" -ForegroundColor Cyan
Write-Host "  host.token:      dist\$dateStr\host_dev\host.token" -ForegroundColor Cyan
Write-Host ""
Write-Host "To build and run the Host:"
Write-Host "  1. Double-click DevBuildHost.bat (builds Host.exe directly into dist\$dateStr\host_dev\)"
Write-Host "  2. Run dist\$dateStr\host_dev\Host.exe"
Write-Host "  (Or press F5 in Visual Studio)"
Write-Host ""
