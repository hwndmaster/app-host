<#
.SYNOPSIS
    Builds the app host image and pushes it to ghcr.io.

.DESCRIPTION
    Modelled on the atom-devops publish-docker.ps1 the applications use, minus the package token: the
    app host restores from nuget.org only. On the server, C:\Docker\app-host\update.ps1 then pulls it.
#>
[CmdletBinding()]
param(
    [Alias("GithubToken")]
    [string]$RegistryToken = $env:GITHUB_REGISTRY_TOKEN,
    [string]$DockerUser = "hwndmaster",
    [string]$Registry = "ghcr.io",
    [int]$DockerReadyTimeoutSeconds = 60,
    [switch]$NoAutoStartDockerDesktop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan

    try {
        & $Action
    }
    catch {
        Write-Error "Step failed: $Name. $($_.Exception.Message)"
        exit 1
    }

    $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        Write-Error "Step failed: $Name (exit code $exitCode)."
        exit $exitCode
    }

    Write-Host "OK: $Name" -ForegroundColor Green
}

function Test-DockerDaemonReady {
    & docker info *> $null
    return $LASTEXITCODE -eq 0
}

function Ensure-DockerReady {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Error "Docker CLI was not found in PATH. Install Docker Desktop and try again."
        exit 1
    }

    if (Test-DockerDaemonReady) {
        return
    }

    $dockerDesktopExe = $null
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $dockerDesktopExe = Join-Path $env:ProgramFiles "Docker\Docker\Docker Desktop.exe"
    }

    if (
        -not $NoAutoStartDockerDesktop -and
        -not [string]::IsNullOrWhiteSpace($dockerDesktopExe) -and
        (Test-Path $dockerDesktopExe)
    ) {
        Write-Host ""
        Write-Host "Docker daemon is not reachable. Starting Docker Desktop..." -ForegroundColor Yellow
        Start-Process -FilePath $dockerDesktopExe | Out-Null
    }

    $startTime = Get-Date
    while (((Get-Date) - $startTime).TotalSeconds -lt $DockerReadyTimeoutSeconds) {
        if (Test-DockerDaemonReady) {
            Write-Host "Docker daemon is ready." -ForegroundColor Green
            return
        }

        Start-Sleep -Seconds 2
    }

    Write-Error "Docker daemon is not reachable. Start Docker Desktop, ensure Linux containers are enabled, and retry."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($RegistryToken)) {
    Write-Error "Registry token is required. Provide -RegistryToken (or -GithubToken) or set GITHUB_REGISTRY_TOKEN."
    exit 1
}

Ensure-DockerReady

# The tag docker-compose.yml builds; it already names the published image.
$image = "$Registry/$DockerUser/app-host:latest"

Push-Location $PSScriptRoot
try {
    Invoke-Step "Docker login to $Registry" {
        $RegistryToken | docker login $Registry -u $DockerUser --password-stdin
    }

    Invoke-Step "Build the app host image" {
        docker compose --progress=plain build app-host
    }

    Invoke-Step "Push the app host image" {
        docker push $image
    }

    Write-Host ""
    Write-Host "Published $image. On the server: C:\Docker\app-host\update.ps1" -ForegroundColor Green
}
finally {
    Pop-Location
}
