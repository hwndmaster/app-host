[CmdletBinding()]
param(
    [string]$Owner = "hwndmaster",
    [string[]]$Packages = @(
        "app-host"
    ),
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-GhApi {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Route,

        [ValidateSet("GET", "DELETE", "POST", "PATCH", "PUT")]
        [string]$Method = "GET"
    )

    $arguments = @("api", "-H", "Accept: application/vnd.github+json")

    if ($Method -ne "GET") {
        $arguments += @("--method", $Method)
    }

    $arguments += $Route

    $output = & gh @arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $details = ($output | Out-String).Trim()
        throw "gh api failed for [$Method $Route]. $details"
    }

    return $output
}

function Get-TokenScopes {
    $statusOutput = & gh auth status 2>&1

    if ($LASTEXITCODE -ne 0) {
        $details = ($statusOutput | Out-String).Trim()
        throw "gh auth status failed. $details"
    }

    $statusText = ($statusOutput | Out-String)
    $scopeLine = $statusText -split "`r?`n" | Where-Object { $_ -match "Token scopes:" } | Select-Object -First 1

    if (-not $scopeLine) {
        return @()
    }

    $rawScopes = ($scopeLine -replace ".*Token scopes:\s*", "")
    return @(
        $rawScopes -split "," |
            ForEach-Object {
                $scope = $_.Trim()
                $scope = $scope.Trim("'")
                $scope = $scope.Trim([char]34)
                $scope
            } |
            Where-Object { $_ }
    )
}

function Get-PackageVersions {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Owner,

        [Parameter(Mandatory = $true)]
        [string]$Package
    )

    $versions = @()
    $page = 1

    while ($true) {
        $route = "/users/$Owner/packages/container/$Package/versions?per_page=100&page=$page"
        $rawJson = Invoke-GhApi -Route $route
        $pageData = $rawJson | ConvertFrom-Json

        if ($null -eq $pageData) {
            break
        }

        $pageItems = @()

        if ($pageData -is [System.Array]) {
            $pageItems = $pageData
        }
        elseif ($pageData -is [PSCustomObject]) {
            $pageItems = @($pageData)
        }
        else {
            throw "Unexpected response for package versions. Expected an array or object."
        }

        if ($pageItems.Count -eq 0) {
            break
        }

        $versions += $pageItems

        if ($pageItems.Count -lt 100) {
            break
        }

        $page += 1
    }

    return $versions
}

function Remove-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Owner,

        [Parameter(Mandatory = $true)]
        [string]$Package,

        [Parameter(Mandatory = $true)]
        [string]$VersionId
    )

    $route = "/users/$Owner/packages/container/$Package/versions/$VersionId"
    Invoke-GhApi -Route $route -Method "DELETE" | Out-Null
}

$requiredScopes = @("read:packages", "delete:packages")
$currentScopes = Get-TokenScopes
$missingScopes = @($requiredScopes | Where-Object { $currentScopes -notcontains $_ })

if ($missingScopes.Count -gt 0) {
    Write-Host ("ERROR: Missing required gh token scopes: {0}" -f ($missingScopes -join ", "))
    Write-Host "Run: gh auth refresh -h github.com -s read:packages,delete:packages"
    exit 1
}

foreach ($Package in $Packages) {
    Write-Host "==============================="
    Write-Host "Processing package: $Package"
    Write-Host "==============================="

    $versions = $null

    try {
        # @() keeps a single version from collapsing to a scalar: under strict mode Windows PowerShell 5.1 has no .Count on scalars.
        $versions = @(Get-PackageVersions -Owner $Owner -Package $Package)
    }
    catch {
        Write-Host ("ERROR: Failed to fetch versions for {0}: {1}" -f $Package, $_.Exception.Message)
        continue
    }

    if (-not $versions -or $versions.Count -eq 0) {
        Write-Host "No versions found for $Package"
        continue
    }

    $latestVersions = @(
        $versions | Where-Object {
            $_.metadata -and
            $_.metadata.container -and
            $_.metadata.container.tags -and
            ($_.metadata.container.tags -contains "latest")
        }
    )

    if ($latestVersions.Count -eq 0) {
        Write-Host "WARNING: No 'latest' tag found for $Package - skipping deletion for safety"
        continue
    }

    $latestIds = @($latestVersions | Select-Object -ExpandProperty id -Unique)
    Write-Host ("Keeping latest version ID(s): {0}" -f ($latestIds -join ", "))

    foreach ($version in $versions) {
        if ($latestIds -contains $version.id) {
            Write-Host ("Skipping latest ({0})" -f $version.id)
            continue
        }

        $tags = @($version.metadata.container.tags)
        $tagText = if ($tags.Count -gt 0) { $tags -join ", " } else { "<untagged>" }

        if ($DryRun) {
            Write-Host ("[DRY RUN] Would delete version {0} (tags: {1})" -f $version.id, $tagText)
            continue
        }

        try {
            Write-Host ("Deleting version {0} (tags: {1})..." -f $version.id, $tagText)
            Remove-PackageVersion -Owner $Owner -Package $Package -VersionId $version.id
        }
        catch {
            Write-Host ("ERROR: Failed to delete version {0} for {1}: {2}" -f $version.id, $Package, $_.Exception.Message)
        }
    }

    Write-Host "Finished cleaning $Package"
    Write-Host ""
}
