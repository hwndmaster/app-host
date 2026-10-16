<#
.SYNOPSIS
    Installs or updates the app host.

.DESCRIPTION
    Pulls the app host's image and recreates its container, which makes it read apps.json and every
    registered application's app.json and .env again. Run it after publishing a new app host image, and
    with -SkipPull after changing anything in this folder.

    The applications keep running throughout. Their containers are persistent, and the app host attaches
    to them again when it starts. Only the dashboard is unavailable, for about 20 seconds.

    Every application's update.ps1 restarts this same container, so all of them, and this script, take one
    machine-wide lock first: two updates never stop the app host under each other.

.PARAMETER SkipPull
    Recreate the container from the image already on this machine.

.EXAMPLE
    .\update.ps1

.EXAMPLE
    .\update.ps1 -SkipPull
#>
[CmdletBinding()]
param(
    [switch]$SkipPull,
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$LockName = "Global\AppHostUpdate"
$ContainerName = "app-host"
$DashboardUrl = "http://localhost:15100/"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan

    & $Action

    $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        Write-Error "Step failed: $Name (exit code $exitCode)."
        exit $exitCode
    }

    Write-Host "OK: $Name" -ForegroundColor Green
}

# Returns why the dashboard cannot be reached, or nothing once it answers. The redirect to its login page is
# followed: refusing it fails differently in Windows PowerShell 5.1 and 7.
function Get-DashboardProblem {
    try {
        Invoke-WebRequest -Uri $DashboardUrl -UseBasicParsing -TimeoutSec 5 | Out-Null
        return $null
    }
    catch {
        return $_.Exception.Message
    }
}

Push-Location $PSScriptRoot
$lock = [System.Threading.Mutex]::new($false, $LockName)
$lockTaken = $false
try {
    $examples = [ordered]@{ ".env" = ".env.example"; "apps.json" = "apps.example.json" }
    foreach ($file in $examples.Keys) {
        if (-not (Test-Path $file)) {
            Write-Error "$file not found next to this script. Create it from its example and fill it in: Copy-Item $($examples[$file]) $file"
            exit 1
        }
    }

    Write-Host "Waiting for any other app host or application update to finish..." -ForegroundColor DarkGray
    try {
        $lockTaken = $lock.WaitOne([TimeSpan]::FromMinutes(10))
    }
    catch [System.Threading.AbandonedMutexException] {
        # A previous update was killed while holding the lock. The lock is ours now.
        $lockTaken = $true
    }

    if (-not $lockTaken) {
        Write-Error "Another update has held the lock for 10 minutes. Check for a stuck update.ps1 window."
        exit 1
    }

    if ($SkipPull) {
        Write-Host "Skipping the pull; using the image already on this machine." -ForegroundColor Yellow
    }
    else {
        Invoke-Step "Pull the app host image" {
            docker compose --env-file .env pull
        }
    }

    Invoke-Step "Recreate the app host" {
        # --force-recreate so a changed apps.json is read even when the image and .env are unchanged.
        docker compose --env-file .env up -d --force-recreate
    }

    Invoke-Step "Wait for the dashboard" {
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ($problem = Get-DashboardProblem) {
            $running = docker inspect --format "{{.State.Running}}" $ContainerName
            if ($running -ne "true" -or (Get-Date) -gt $deadline) {
                # Not redirected into the pipeline: Windows PowerShell 5.1 turns native stderr piped through
                # 2>&1 into a terminating error under $ErrorActionPreference = "Stop".
                docker logs --tail 50 $ContainerName
                Write-Error "The app host did not come up. Its log is above. Reaching $DashboardUrl last failed with: $problem"
                exit 1
            }

            Start-Sleep -Seconds 2
        }
    }

    Write-Host ""
    Write-Host "The app host is running. Dashboard: $DashboardUrl" -ForegroundColor Green
    Write-Host "The login link is in its log:  docker logs app-host"
}
finally {
    if ($lockTaken) {
        $lock.ReleaseMutex()
    }

    $lock.Dispose()
    Pop-Location
}
