# Requires Windows PowerShell 5.1+ or PowerShell 7, and .NET SDK 8+.
# Creates only missing files. Never replaces existing service code or runs SQL.
[CmdletBinding()]
param(
    [ValidateSet('Property','Document','Maintenance','ServiceCatalog','Provider','Billing','Notification','PropertyIntelligence','WebsiteBuilder','Integration')]
    [string[]]$Service,
    [switch]$Restore
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'Zumaris.sln'
if (!(Test-Path -LiteralPath $solutionPath)) { throw 'Zumaris.sln was not found next to the scripts folder.' }
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET SDK 8 or later first.' }
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0 -or [int]($sdk.Split('.')[0]) -lt 8) { throw '.NET SDK 8 or later is required.' }
$manifest = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'service-scaffolds.json') -Raw | ConvertFrom-Json)
$definitions = @($manifest | Where-Object { !$Service -or $_.name -in $Service })
$created = 0
function Write-NewFile([string]$Path, [string]$Content) {
    if (Test-Path -LiteralPath $Path) { return }
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    [System.IO.File]::WriteAllText($Path, $Content.Trim() + "`n", [System.Text.UTF8Encoding]::new($false))
    $script:created++
}
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('dotnet failed: ' + ($Arguments -join ' ')) }
}
foreach ($definition in $definitions) {
    $name = $definition.name
    $serviceRoot = Join-Path $repositoryRoot "src/Services/$name"
    $projectPaths = @()
    foreach ($layer in @('Domain','Application','Infrastructure','Api')) {
        $projectRoot = Join-Path $serviceRoot "$name.$layer"
        $projectPath = Join-Path $projectRoot "$name.$layer.csproj"
        $projectPaths += $projectPath
        $sdkName = if ($layer -eq 'Api') { 'Microsoft.NET.Sdk.Web' } else { 'Microsoft.NET.Sdk' }
        $references = switch ($layer) {
            'Application' { @('Domain') }
            'Infrastructure' { @('Application','Domain') }
            'Api' { @('Application','Infrastructure') }
            default { @() }
        }
        $folders = switch ($layer) {
            'Domain' { @('Entities') }
            'Application' { @('Abstractions','Features') }
            'Infrastructure' { @('Persistence','Integrations') }
            'Api' { @('Controllers') }
        }
        $lines = @("<Project Sdk=`"$sdkName`">", '  <PropertyGroup>', '    <TargetFramework>net8.0</TargetFramework>',
            '    <Nullable>enable</Nullable>', '    <ImplicitUsings>enable</ImplicitUsings>',
            "    <RootNamespace>Zumaris.$name.$layer</RootNamespace>", '  </PropertyGroup>')
        if ($references.Count -gt 0) {
            $lines += '  <ItemGroup>'
            foreach ($reference in $references) {
                $lines += "    <ProjectReference Include=`"../$name.$reference/$name.$reference.csproj`" />"
            }
            $lines += '  </ItemGroup>'
        }
        if ($layer -eq 'Api') {
            $lines += @('  <ItemGroup>', '    <PackageReference Include="Microsoft.OpenApi" Version="2.7.5" />',
                '    <PackageReference Include="Swashbuckle.AspNetCore" Version="10.2.3" />', '  </ItemGroup>')
        }
        $lines += '  <ItemGroup>'
        foreach ($folder in $folders) {
            $lines += "    <Folder Include=`"$folder/`" />"
            [System.IO.Directory]::CreateDirectory((Join-Path $projectRoot $folder)) | Out-Null
        }
        $lines += @('  </ItemGroup>', '</Project>')
        Write-NewFile $projectPath ($lines -join "`n")
    }
    $program = @"
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "$name API (Scaffold)", Version = "v1" });
    options.AddServer(new OpenApiServer { Url = "../.." });
});
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("v1/swagger.json", "$name API (Scaffold)"));
app.UseCors();
// Liveness only: there is no database, business workflow or authentication integration yet.
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
"@
    Write-NewFile (Join-Path $serviceRoot "$name.Api/Program.cs") $program
    $settings = [ordered]@{
        Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
        AllowedHosts = '*'
        Cors = @{ AllowedOrigins = @('https://app.zumaris.com') }
    }
    Write-NewFile (Join-Path $serviceRoot "$name.Api/appsettings.json") ($settings | ConvertTo-Json -Depth 8)
    $launch = @{ profiles = @{ http = @{
        commandName = 'Project'; dotnetRunMessages = $true; launchBrowser = $true; launchUrl = 'swagger'
        applicationUrl = "http://localhost:$($definition.developmentPort)"
        environmentVariables = @{ ASPNETCORE_ENVIRONMENT = 'Development' }
    } } }
    Write-NewFile (Join-Path $serviceRoot "$name.Api/Properties/launchSettings.json") ($launch | ConvertTo-Json -Depth 8)
    $readme = @"
# $name

Scaffold only: .NET 8 / Clean Architecture. No business endpoints, authentication wiring or persistence implementation yet.

- Domain: business entities and rules; no dependencies on outer layers.
- Application: use cases and repository/integration contracts; references Domain.
- Infrastructure: future MySQL stored procedures/direct SQL and integrations; references Application and Domain. No ORM.
- Api: HTTP composition root; references Application and Infrastructure.
- Development: http://localhost:$($definition.developmentPort)/swagger
- Liveness: GET /health (process check only, not business/database readiness).
- Planned Gateway prefix: /$($definition.route) (not registered until implementation).
- Planned MySQL schema: $($definition.schema) (not created).
- Suggested future IIS HTTP port: $($definition.suggestedIisPort) (not configured).

No database scripts run automatically. Add manual SQL migrations under database/$($definition.route).
Configure Identity JWT/session validation and resource permissions before exposing business endpoints.
See docs/service-architecture.md for ownership and cross-service boundaries.
"@
    Write-NewFile (Join-Path $serviceRoot 'README.md') $readme
    Write-NewFile (Join-Path $repositoryRoot "database/$($definition.route)/README.md") "# $name database`n`nReserved for manual MySQL migration scripts. Planned schema: $($definition.schema). No schema or tables have been created. Do not access another service's schema directly."
    $solutionText = [System.IO.File]::ReadAllText($solutionPath)
    $missing = @($projectPaths | Where-Object {
        $relative = "src/Services/$name/" + (Split-Path -Leaf (Split-Path -Parent $_)) + '/' + (Split-Path -Leaf $_)
        !$solutionText.Contains($relative) -and !$solutionText.Contains($relative.Replace('/', '\'))
    })
    if ($missing.Count -gt 0) {
        Invoke-DotNet (@('sln', $solutionPath, 'add', '--solution-folder', "src/Services/$name") + $missing)
    }
}
if ($Restore) { Invoke-DotNet @('restore', $solutionPath) }
Write-Host "Scaffolds ready: $($definitions.Count) services; $created new files. Existing files preserved. No SQL or deployment performed."
