# Run from any directory. Reads existing Identity settings without printing credentials.
# This creates a git-ignored local file used by development and IIS publish output.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repositoryRoot 'src/Services/Identity/Identity.Api/appsettings.json'
$destination = Join-Path $repositoryRoot 'src/Services/ServiceRequest/ServiceRequest.Api/appsettings.Local.json'
if (Test-Path $destination) {
    Write-Host 'Existing Service Request local settings preserved. Edit that file explicitly if values need updating.'
    return
}
$identity = Get-Content -Raw $source | ConvertFrom-Json
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection.ConnectionString = $identity.ConnectionStrings.DefaultConnection
$connection['Database'] = 'zumaris_service_request'
if ([string]::IsNullOrWhiteSpace($identity.Jwt.Key)) { throw 'Identity JWT configuration is missing.' }
$local = [ordered]@{
    ConnectionStrings = @{ DefaultConnection = $connection.ConnectionString }
    Jwt = @{ Key = $identity.Jwt.Key; Issuer = $identity.Jwt.Issuer; Audience = $identity.Jwt.Audience }
}
$json = $local | ConvertTo-Json -Depth 6
# UTF-8 without BOM, compatible with both Windows PowerShell and PowerShell 7.
[System.IO.File]::WriteAllText($destination, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host 'Service Request local settings created. Only the database name was changed; credential values are not printed.'
