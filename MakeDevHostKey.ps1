# MakeDevHostKey.ps1
# ============================================================
# Creates Host\host.key AND Host\host.token for local dev/debugging.
# Run this ONCE after creating a new tenant in the Viewer.
#
# Usage:
#   .\MakeDevHostKey.ps1 -LicenseKey <paste-license-key> -HostToken <paste-host-token>
#
# Where to get the values:
#   Open Viewer.exe -> log in as super admin -> Tenants -> Details
#   Copy the License Key AND the Host Token
#
# What this does:
#   host.key   = SHA-256(LicenseKey)   - same as BuildHostForSubAdmin.bat produces
#   host.token = HostToken (plaintext) - same as BuildHostForSubAdmin.bat produces
# ============================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$LicenseKey,

    [Parameter(Mandatory=$true)]
    [string]$HostToken
)

$root      = $PSScriptRoot
$keyFile   = Join-Path $root "Host\host.key"
$tokenFile = Join-Path $root "Host\host.token"

# Compute SHA-256 of the raw license key
$bytes     = [System.Text.Encoding]::UTF8.GetBytes($LicenseKey)
$sha256    = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash($bytes)
$hash      = [System.BitConverter]::ToString($hashBytes).Replace("-","").ToLowerInvariant()

# Write both files
Set-Content -Path $keyFile   -Encoding UTF8 -NoNewline -Value $hash
Set-Content -Path $tokenFile -Encoding UTF8 -NoNewline -Value $HostToken

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  host.key   -> $keyFile"
Write-Host "  host.token -> $tokenFile"
Write-Host ""
Write-Host "Now rebuild the Host project in Visual Studio (Ctrl+Shift+B)."
Write-Host "Both files will be copied to the output folder automatically."
Write-Host ""
