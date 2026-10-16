<#
.SYNOPSIS
    Collects what shows why the app host's dashboard does not answer. Read-only: it changes nothing.

.DESCRIPTION
    Compares what answers on the dashboard port from this machine with what answers inside the app host
    container, where DCP proxies the dashboard. Send the whole output when asking for help.
#>
[CmdletBinding()]
param(
    [string]$ContainerName = "app-host",
    [int]$Port = 15100
)

# Keep going past failures: every section is worth seeing.
$ErrorActionPreference = "Continue"

function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host "==> $Title" -ForegroundColor Cyan
}

function Test-Url([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
        "  $Url -> HTTP $($response.StatusCode)"
    }
    catch {
        "  $Url -> $($_.Exception.Message)"
    }
}

Write-Section "Docker"
docker version --format "client {{.Client.Version}}, server {{.Server.Version}} ({{.Server.Os}}/{{.Server.Arch}})"
docker info --format "{{.OperatingSystem}}, kernel {{.KernelVersion}}, {{.NCPU}} CPUs, {{.MemTotal}} bytes"

Write-Section "Containers"
docker ps -a --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

Write-Section "App host container"
docker inspect $ContainerName --format 'restarts {{.RestartCount}}, OOM-killed {{.State.OOMKilled}}, memory limit {{.HostConfig.Memory}}'
docker inspect $ContainerName --format '{{range $name, $net := .NetworkSettings.Networks}}network {{$name}}: {{$net.IPAddress}}{{println}}{{end}}'
docker stats --no-stream --format "memory {{.MemUsage}}, CPU {{.CPUPerc}}" $ContainerName

Write-Section "Listening on port $Port on this machine"
Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { "  $($_.LocalAddress):$($_.LocalPort) -> $((Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName) (pid $($_.OwningProcess))" }

Write-Section "Dashboard from this machine"
Test-Url "http://127.0.0.1:$Port/"
Test-Url "http://localhost:$Port/"
Test-Url "http://[::1]:$Port/"

# Passed base64-encoded: Windows PowerShell 5.1 mangles double quotes inside arguments to native programs.
$inside = @'
echo 'listeners for the dashboard and OTLP ports (address:port -> process):'
for f in /proc/net/tcp /proc/net/tcp6; do awk 'NR>1 && $4=="0A" {print $2, $10}' $f; done | while read addr inode; do
  port=$((0x${addr##*:}))
  case $port in 15100|21100|21101) ;; *) continue ;; esac
  owner=$(for p in /proc/[0-9]*; do ls -l $p/fd 2>/dev/null | grep -q "socket:\[$inode\]" && { cat $p/comm; break; }; done)
  echo "  ${addr%%:*}:$port -> $owner"
done
echo 'dashboard process:'
for p in /proc/[0-9]*; do grep -q Aspire.Dashboard $p/cmdline 2>/dev/null && tr '\0' '\n' < $p/environ | grep -E '^ASPNETCORE_URLS=' | sed 's/^/  /'; done | sort -u
echo 'HTTP inside the container:'
for target in 127.0.0.1 $(hostname -i); do
  result=$(timeout 5 bash -c "exec 3<>/dev/tcp/$target/15100 && printf 'GET / HTTP/1.0\r\nHost: localhost\r\n\r\n' >&3 && head -1 <&3" 2>&1)
  echo "  $target:15100 -> ${result:-no answer within 5 s}"
done
'@
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($inside.Replace("`r", "")))

Write-Section "Inside the app host container"
docker exec $ContainerName sh -c "echo $encoded | base64 -d | bash"
