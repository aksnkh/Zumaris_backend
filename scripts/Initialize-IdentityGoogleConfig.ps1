# Run on the IIS server against the published Identity folder, not the Gateway folder.
[CmdletBinding()]
param(
    [string]$IdentityPublishPath,
    [string]$GoogleClientId
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($IdentityPublishPath)) {
    $IdentityPublishPath = Read-Host 'Enter the published Identity folder path (contains Identity.Api.dll)'
}
$IdentityPublishPath = $IdentityPublishPath.Trim().Trim('"')
$settingsPath = Join-Path $IdentityPublishPath 'appsettings.json'
if (!(Test-Path (Join-Path $IdentityPublishPath 'Identity.Api.dll')) -or !(Test-Path $settingsPath)) {
    throw 'Choose the published Identity folder containing Identity.Api.dll and appsettings.json.'
}
if ([string]::IsNullOrWhiteSpace($GoogleClientId)) {
    $GoogleClientId = Read-Host 'Paste AUTH_GOOGLE_ID from the frontend (not AUTH_GOOGLE_SECRET)'
}
$GoogleClientId = $GoogleClientId.Trim()
if ($GoogleClientId -notmatch '^[A-Za-z0-9._-]+\.apps\.googleusercontent\.com$') {
    throw 'The Google Client ID must end with .apps.googleusercontent.com.'
}
# Preserve all existing database, JWT and other settings. Never print credentials.
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if (!$settings.Google) { $settings | Add-Member -NotePropertyName Google -NotePropertyValue ([pscustomobject]@{}) -Force }
$settings.Google | Add-Member -NotePropertyName ClientIds -NotePropertyValue @($GoogleClientId) -Force
if (!$settings.Bff) { $settings | Add-Member -NotePropertyName Bff -NotePropertyValue ([pscustomobject]@{}) -Force }
$existingKey = [string]$settings.Bff.ClientKey
if ([System.Text.Encoding]::UTF8.GetByteCount($existingKey) -lt 32) {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $settings.Bff | Add-Member -NotePropertyName ClientKey -NotePropertyValue ([Convert]::ToBase64String($bytes)) -Force
}
$json = $settings | ConvertTo-Json -Depth 100
# A temporary file in the same folder preserves the original if writing fails.
$temporaryPath = Join-Path $IdentityPublishPath ([Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Replace($temporaryPath, $settingsPath, $null)
} finally {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath }
}
Write-Host 'Google settings saved. Existing database/JWT values and any valid BFF key were preserved.'
Write-Host 'Recycle the Identity application pool in IIS. No database changes were performed.'
Write-Host 'Keep this server configuration when publishing updates. Do not commit it or send its contents in chat.'
Write-Host 'Existing Google__ClientIds__0 or Bff__ClientKey environment overrides take priority over this file.'
