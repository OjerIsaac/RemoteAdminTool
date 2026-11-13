# MakeDevHostKey.ps1
# ============================================================
# Saves Local Dev LicenseKeyHash and HostToken into .env
# (DEV_LICENSE_KEY_HASH and DEV_HOST_TOKEN) so DevBuildHost.bat
# compiles a 100% standalone single-file Host.exe with ZERO
# .key or .token files in dist\!
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

$root    = $PSScriptRoot
$envFile = Join-Path $root ".env"

if (-not (Test-Path $envFile)) {
    Write-Error ".env file not found at $envFile"
    exit 1
}

# 1. Compute SHA-256 of the raw license key
$bytes     = [System.Text.Encoding]::UTF8.GetBytes($LicenseKey.Trim())
$sha256    = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash($bytes)
$hash      = [System.BitConverter]::ToString($hashBytes).Replace("-","").ToLowerInvariant()
$token     = $HostToken.Trim()

# 2. Update DEV_LICENSE_KEY_HASH and DEV_HOST_TOKEN in .env
$content = Get-Content $envFile -Raw
if ($content -match "(?m)^DEV_LICENSE_KEY_HASH=.*$") {
    $content = $content -replace "(?m)^DEV_LICENSE_KEY_HASH=.*$", "DEV_LICENSE_KEY_HASH=$hash"
} else {
    $content += "`nDEV_LICENSE_KEY_HASH=$hash"
}
if ($content -match "(?m)^DEV_HOST_TOKEN=.*$") {
    $content = $content -replace "(?m)^DEV_HOST_TOKEN=.*$", "DEV_HOST_TOKEN=$token"
} else {
    $content += "`nDEV_HOST_TOKEN=$token"
}
Set-Content -Path $envFile -Encoding UTF8 -NoNewline -Value $content

Write-Host ""
Write-Host "Done! Local Dev Host credentials saved into .env (zero .key/.token files needed in dist!):" -ForegroundColor Green
Write-Host "  DEV_LICENSE_KEY_HASH = $hash" -ForegroundColor Cyan
Write-Host ""
Write-Host "To build and run your standalone Dev Host:"
Write-Host "  1. Run .\DevBuildHost.bat (produces ONLY dist\<date>\host_dev\Host.exe)"
Write-Host "  2. Run dist\<date>\host_dev\Host.exe"
Write-Host ""
