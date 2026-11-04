# MakeDevHostKey.ps1
# ============================================================
# Creates host.key AND host.token for local dev/debugging.
# All keys are stored inside gitignored dist/ folders (never in git).
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

$root      = $PSScriptRoot
$distHost  = Join-Path $root "Host\dist"
$binDebug  = Join-Path $root "Host\bin\Debug\net8.0-windows"

# 1. Compute SHA-256 of the raw license key
$bytes     = [System.Text.Encoding]::UTF8.GetBytes($LicenseKey)
$sha256    = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash($bytes)
$hash      = [System.BitConverter]::ToString($hashBytes).Replace("-","").ToLowerInvariant()

# 2. Write to Host\dist\ (gitignored - used by Host\dist\Host.exe and DevBuildHost.bat)
if (-not (Test-Path $distHost)) {
    New-Item -ItemType Directory -Path $distHost -Force | Out-Null
}
Set-Content -Path (Join-Path $distHost "host.key")   -Encoding UTF8 -NoNewline -Value $hash
Set-Content -Path (Join-Path $distHost "host.token") -Encoding UTF8 -NoNewline -Value $HostToken

# 3. Write to Host\bin\Debug\net8.0-windows\ if it exists (for Visual Studio F5 debugging)
if (Test-Path $binDebug) {
    Set-Content -Path (Join-Path $binDebug "host.key")   -Encoding UTF8 -NoNewline -Value $hash
    Set-Content -Path (Join-Path $binDebug "host.token") -Encoding UTF8 -NoNewline -Value $HostToken
}

# 4. Save a dated, tracked package under dist\YYYY-MM-DD\
$dateStr     = (Get-Date).ToString("yyyy-MM-dd")
$timeStr     = (Get-Date).ToString("HHmmss")
$datedFolder = Join-Path $root "dist\$dateStr\dev_$timeStr"
New-Item -ItemType Directory -Path $datedFolder -Force | Out-Null

Set-Content -Path (Join-Path $datedFolder "host.key")   -Encoding UTF8 -NoNewline -Value $hash
Set-Content -Path (Join-Path $datedFolder "host.token") -Encoding UTF8 -NoNewline -Value $HostToken

# Copy Host.exe and appsettings.json if they exist in distHost
$sourceExe = Join-Path $distHost "Host.exe"
if (Test-Path $sourceExe) {
    Copy-Item $sourceExe (Join-Path $datedFolder "Host.exe") -Force
}
$sourceSettings = Join-Path $distHost "appsettings.json"
if (Test-Path $sourceSettings) {
    Copy-Item $sourceSettings (Join-Path $datedFolder "appsettings.json") -Force
}

# Record human-readable metadata for tracking
$metaContent = @"
Created:       $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Type:          Local Dev / Test Host
LicenseKey:    $LicenseKey
LicenseHash:   $hash
HostToken:     $HostToken
Folder:        dist\$dateStr\dev_$timeStr
Note:          All files in dist/ are gitignored and safe from commits.
"@
Set-Content -Path (Join-Path $datedFolder "license_info.txt") -Encoding UTF8 -Value $metaContent

Write-Host ""
Write-Host "Done! Keys generated in gitignored dist/ folders:" -ForegroundColor Green
Write-Host "  Active Dev:    $distHost\host.key" -ForegroundColor Cyan
Write-Host "  Active Dev:    $distHost\host.token" -ForegroundColor Cyan
Write-Host "  Date Archive:  $datedFolder" -ForegroundColor Yellow
Write-Host ""
Write-Host "Source directory Host\ remains completely clean (no secret files to commit)." -ForegroundColor Green
Write-Host "To run Host locally:"
Write-Host "  - Standalone:  Host\dist\Host.exe (or .\DevBuildHost.bat)"
Write-Host "  - VS Debug:    Press F5 in Visual Studio"
Write-Host ""
