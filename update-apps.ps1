<#
.SYNOPSIS
    Runs the update.ps1 of every application in build\, one after another.

.DESCRIPTION
    A local convenience over the folders sync-apps.ps1 assembles. Each application's update.ps1 restarts the
    app host and takes the machine-wide update lock, so they run in turn, never side by side. Stops at the
    first one that fails.

.PARAMETER SkipPull
    Passed on to every update.ps1: recreate the containers from the images already on this machine.

.EXAMPLE
    .\update-apps.ps1

.EXAMPLE
    .\update-apps.ps1 -SkipPull
#>
[CmdletBinding()]
param(
    [switch]$SkipPull
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$build = Join-Path $PSScriptRoot "build"
$scripts = @()
if (Test-Path $build) {
    $scripts = @(Get-ChildItem -Path $build -Directory |
        Where-Object { $_.Name -ne "app-host" } |
        ForEach-Object { Join-Path $_.FullName "update.ps1" } |
        Where-Object { Test-Path $_ })
}

if ($scripts.Count -eq 0) {
    Write-Error "No application in build\. Run .\sync-apps.ps1 first."
    exit 1
}

foreach ($script in $scripts) {
    Write-Host ""
    Write-Host "######## $(Split-Path -Leaf (Split-Path -Parent $script))" -ForegroundColor Magenta
    & $script -SkipPull:$SkipPull
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
