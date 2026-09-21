# MakeDevHostKey.ps1
# ============================================================
# Creates Host\host.key for local development/debugging.
# Run this ONCE after creating a new tenant in the Viewer.
#
# Usage:
#   .\MakeDevHostKey.ps1 -LicenseKey <paste-license-key-here>
#
# Where to get the LicenseKey:
#   Open Viewer.exe -> log in as super admin -> Tenants -> Details -> copy License Key
#
# What this does:
#   Computes SHA-256(LicenseKey) and writes it to Host\host.key
#   This is the same hash that build.ps1 writes when publishing for distribution.
#   The relay validates the hash, not the raw key.
# ============================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$LicenseKey
)

$root    = $PSScriptRoot
$keyFile = Join-Path $root "Host\host.key"

# Compute SHA-256 of the raw license key
$bytes     = [System.Text.Encoding]::UTF8.GetBytes($LicenseKey)
$sha256    = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash($bytes)
$hash      = [System.BitConverter]::ToString($hashBytes).Replace("-","").ToLowerInvariant()

# Write hash to Host\host.key
Set-Content -Path $keyFile -Encoding UTF8 -NoNewline -Value $hash

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  host.key -> $keyFile"
Write-Host "  Hash     -> $hash"
Write-Host ""
Write-Host "Now rebuild the Host project in Visual Studio (Ctrl+Shift+B)."
Write-Host "The host.key will be copied to the output folder automatically."
Write-Host ""
