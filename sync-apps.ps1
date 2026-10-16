<#
.SYNOPSIS
    Assembles a local apps root in build\, laid out like C:\Docker on the server, so the app host can be
    run and tried on this machine.

.DESCRIPTION
    Copies the deployment folders in:

      build\app-host\         this repository's Deployment\
      build\<DisplayName>\    each application's Deployment\, from its repository next to this one

    The applications are the repositories next to this one that have a Deployment\app.json, unless -Apps
    names them. Their folders are named after each application's displayName (e.g. MyApp), as they are
    under C:\Docker on the server, so the apps.json written here works there unchanged.

    Every run copies the deployment files again, so build\ always carries the repositories' current
    versions. What is specific to a machine is created once and then left alone:

      - an application's .env comes from its repository's Deployment\.env when there is one (the settings
        of an earlier local deployment), otherwise from .env.example. Check its paths before starting.
      - build\app-host\.env comes from .env.example, with APPHOST_APPS_ROOT pointing at build\.
      - build\app-host\apps.json registers every application synced here; later runs only add the ones it
        does not list yet.

    Then, to run it:
      docker compose build                      # in this repository: the app host image, tagged latest
      .\build\app-host\update.ps1 -SkipPull     # starts the app host from that image
      .\build\MyApp\update.ps1                  # pulls and starts one application
      .\update-apps.ps1                         # or every application in turn

.PARAMETER SourceRoot
    The folder holding the application repositories. Defaults to the parent of this repository.

.PARAMETER Apps
    The repository folder names of the applications to copy in. Defaults to every repository in
    SourceRoot that has a Deployment\app.json.

.EXAMPLE
    .\sync-apps.ps1

.EXAMPLE
    .\sync-apps.ps1 -Apps my-app
#>
[CmdletBinding()]
param(
    [string]$SourceRoot,
    [string[]]$Apps
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Not a parameter default: Windows PowerShell 5.1 leaves $PSScriptRoot empty while binding parameters.
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Split-Path -Parent $PSScriptRoot
}

# Found rather than listed, so no application's name has to live in this repository. This repository has a
# Deployment folder too, but no app.json in it.
if (-not $Apps) {
    $Apps = @(Get-ChildItem -Path $SourceRoot -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName "Deployment\app.json") } |
        ForEach-Object { $_.Name })

    if ($Apps.Count -eq 0) {
        Write-Warning "No repository in $SourceRoot has a Deployment\app.json; only the app host is copied in."
    }
}

$Destination = Join-Path $PSScriptRoot "build"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Every file in a Deployment folder except the ones that belong to this machine.
function Copy-DeploymentFiles {
    param([string]$From, [string]$To, [string[]]$Keep)

    New-Item -ItemType Directory -Force -Path $To | Out-Null
    Get-ChildItem -Path $From -File -Force |
        Where-Object { $Keep -notcontains $_.Name } |
        ForEach-Object { Copy-Item -Path $_.FullName -Destination (Join-Path $To $_.Name) -Force }
}

# Written without a byte-order mark: docker compose reads a BOM as part of the first variable's name.
function Write-Text {
    param([string]$Path, [string]$Text)

    [System.IO.File]::WriteAllText($Path, $Text, $Utf8NoBom)
}

$appHostSource = Join-Path $PSScriptRoot "Deployment"
$appHostTarget = Join-Path $Destination "app-host"

Write-Host "==> app-host" -ForegroundColor Cyan
Copy-DeploymentFiles -From $appHostSource -To $appHostTarget -Keep @(".env", "apps.json")

$appHostEnv = Join-Path $appHostTarget ".env"
if (Test-Path $appHostEnv) {
    Write-Host "   .env kept"
}
else {
    $root = $Destination.Replace("\", "/")
    $text = [System.IO.File]::ReadAllText((Join-Path $appHostSource ".env.example"))
    $text = [regex]::Replace($text, "(?m)^APPHOST_APPS_ROOT=.*$", "APPHOST_APPS_ROOT=$root")
    Write-Text -Path $appHostEnv -Text $text
    Write-Host "   .env created, APPHOST_APPS_ROOT=$root" -ForegroundColor Yellow
}

$registrations = @()
foreach ($repository in $Apps) {
    $source = Join-Path (Join-Path $SourceRoot $repository) "Deployment"
    $appFile = Join-Path $source "app.json"
    if (-not (Test-Path $appFile)) {
        Write-Warning "$repository has no Deployment\app.json at $source - skipped."
        continue
    }

    $app = [System.IO.File]::ReadAllText($appFile) | ConvertFrom-Json
    $folder = if ($app.PSObject.Properties["displayName"]) { $app.displayName } else { $app.name }
    $target = Join-Path $Destination $folder

    Write-Host "==> $repository -> build\$folder" -ForegroundColor Cyan
    Copy-DeploymentFiles -From $source -To $target -Keep @(".env")

    $envFile = Join-Path $target ".env"
    if (Test-Path $envFile) {
        Write-Host "   .env kept"
    }
    elseif (Test-Path (Join-Path $source ".env")) {
        Copy-Item -Path (Join-Path $source ".env") -Destination $envFile
        Write-Host "   .env copied from $repository\Deployment\.env" -ForegroundColor Yellow
    }
    else {
        Copy-Item -Path (Join-Path $source ".env.example") -Destination $envFile
        Write-Host "   .env created from .env.example - check the paths in build\$folder\.env" -ForegroundColor Yellow
    }

    $registrations += [pscustomobject]@{ name = $app.name; path = $folder }
}

$appsFile = Join-Path $appHostTarget "apps.json"
# Not $apps: PowerShell variable names ignore case, so that would be the [string[]] $Apps parameter.
if (Test-Path $appsFile) {
    $appsDocument = [System.IO.File]::ReadAllText($appsFile) | ConvertFrom-Json
    $listed = @($appsDocument.apps | ForEach-Object { $_.name })
    $added = @($registrations | Where-Object { $listed -notcontains $_.name })
    if ($added.Count -gt 0) {
        $appsDocument.apps = @($appsDocument.apps) + $added
        Write-Text -Path $appsFile -Text ($appsDocument | ConvertTo-Json -Depth 5)
        Write-Host "   apps.json: registered $(($added | ForEach-Object { $_.name }) -join ', ')" -ForegroundColor Yellow
    }
}
else {
    $appsDocument = [pscustomobject]@{ '$schema' = "./apps.schema.json"; apps = @($registrations) }
    Write-Text -Path $appsFile -Text ($appsDocument | ConvertTo-Json -Depth 5)
    Write-Host "   apps.json created with $(($registrations | ForEach-Object { $_.name }) -join ', ')" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "build\ is ready. To run it:" -ForegroundColor Green
Write-Host "  docker compose build                      # the app host image, from this repository"
Write-Host "  .\build\app-host\update.ps1 -SkipPull     # start the app host"
Write-Host "  .\build\<Application>\update.ps1          # pull and start an application"
