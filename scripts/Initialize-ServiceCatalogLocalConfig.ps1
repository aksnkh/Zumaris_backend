# Creates a per-machine git-ignored ServiceCatalog config; no SQL or network calls are made.
[CmdletBinding()]
param(
    [string]$IdentitySettingsPath,
    [string]$OutputDirectory,
    [string]$DatabaseServer
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($IdentitySettingsPath)) {
    $IdentitySettingsPath = Join-Path $repositoryRoot 'src/Services/Identity/Identity.Api/appsettings.json'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'src/Services/ServiceCatalog/ServiceCatalog.Api'
}
if (!(Test-Path -LiteralPath $OutputDirectory -PathType Container)) { throw 'OutputDirectory must already exist.' }
$destination = Join-Path $OutputDirectory 'appsettings.Local.json'
if (Test-Path -LiteralPath $destination) {
    Write-Host 'Existing ServiceCatalog local settings preserved. Edit that file explicitly if values need updating.'
    return
}
$identity = Get-Content -LiteralPath $IdentitySettingsPath -Raw | ConvertFrom-Json
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
# Explicit .NET accessors avoid PowerShell's IDictionary property adaptation.
$connection.set_ConnectionString([string]$identity.ConnectionStrings.DefaultConnection)
$connection.set_Item('Database', 'zumaris_service_catalog')
if (![string]::IsNullOrWhiteSpace($DatabaseServer)) { $connection.set_Item('Server', $DatabaseServer) }
if ([System.Text.Encoding]::UTF8.GetByteCount([string]$identity.Jwt.Key) -lt 32 -or
    [string]::IsNullOrWhiteSpace($identity.Jwt.Issuer) -or [string]::IsNullOrWhiteSpace($identity.Jwt.Audience)) {
    throw 'The Identity settings file must contain its JWT key, issuer and audience.'
}
$local = [ordered]@{
    ConnectionStrings = @{ DefaultConnection = $connection.get_ConnectionString() }
    Jwt = @{ Key = $identity.Jwt.Key; Issuer = $identity.Jwt.Issuer; Audience = $identity.Jwt.Audience }
}
[System.IO.File]::WriteAllText($destination, ($local | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))
Write-Host 'ServiceCatalog local settings created using existing Identity values. Database name: zumaris_service_catalog.'
Write-Host 'Credentials were not printed. No database changes were performed.'
