# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

**app-host**: one Aspire app host that runs self-hosted applications deployed on the server
(such as PrepperBox, PriceChecker, etc.). Inside its container, DCP creates and supervises each
application's api and web containers on the server's Docker daemon, through a mounted Docker socket. The
dashboard gives start/stop/restart, console logs, structured logs, traces and metrics for all of them.
Repository: <https://github.com/hwndmaster/app-host>.

It is not built on Genius.Atom and restores from nuget.org only (`nuget.config`). Aspire is pinned to
13.6.1 in two places that must agree: the `Sdk` in `AppHost/AppHost.csproj` and `Directory.Packages.props`.

## Solution map

`AppHost.slnx`

| Path | Scope |
|------|-------|
| `AppHost/Program.cs` | Loads the catalog, adds one group per application, runs |
| `AppHost/HostSettings.cs` | `AppHost:*` settings (registry, bind address, public host, OTLP endpoints, pull policy) |
| `AppHost/Configuration/` | `apps.json` and `app.json` models, `.env` parsing, `${VAR}` interpolation, `AppCatalog` (load + validate) |
| `AppHost/Types/` | `IAppType` and `AtomWebAppType` (`atom-webapp`, the shape of all current apps) |
| `AppHost/Resources/AppGroupResource.cs` | The parent row per application on the Resources page |
| `AppHost/Dockerfile` | The image: build output, the Linux DCP and dashboard packages, the docker CLI |
| `AppHost.Tests/` | xUnit v3 tests: parsing, interpolation, catalog validation, the resource model |
| `Deployment/` | What the server runs: compose file, `.env.example`, `apps.example.json`, schemas, `update.ps1` |

## Configuration model

- **The apps root** is `C:\Docker` on the server, which also holds unrelated containers' folders.
  Locally it is this repository's git-ignored `build\`, assembled by `sync-apps.ps1` from the
  neighbouring app repositories. Both use the same layout: `app-host\` plus one folder per application,
  named after its `displayName` (`MyApp`, ...).
- **`apps.json`** (in the app-host folder, next to the compose file) lists the applications and their
  folders, relative to the apps root mounted at `/apps`.
- **Each application's folder** holds its `app.json`, `.env`, a pull-only `docker-compose.yml` and
  `update.ps1`, all from that application's own repo (`Deployment/`). `${VAR}` in `app.json` resolves from
  that application's `.env` only.
- **A broken `apps.json` stops the app host.** A broken application does not: it shows as
  "Configuration error" with the reason on the Resources page, its containers keep running unmanaged, and
  the other applications are orchestrated as usual. Keep it that way.
- **JSON reading is strict on purpose.** Unknown properties are errors (catches typos); numbers may be
  strings (ports from `.env`); errors name the file and JSON path.

## Rules that came from breakage

These are why `AtomWebAppType` looks the way it does. Each one failed silently when ignored.

- **No `--network` runtime argument.** DCP reconciles the networks of containers it owns; a foreign one
  fails the start with only "Failed to start" shown. The real error is in
  `/tmp/aspire-dcp*/resource-container-*.log` inside the app host.
- **Mounts are raw `-v` arguments, never `WithBindMount`.** `WithBindMount` normalises Windows host paths
  with this Linux container's rules and breaks them.
- **DCP publishes on 127.0.0.1 only.** The reachable publish is an explicit `-p <bind>:<host>:<container>`.
- **`OTEL_SERVICE_NAME` is set explicitly.** Aspire injects it for projects only, and it equals the
  resource name so the dashboard ties telemetry to the resource.
- **Images are pulled on the server, never by DCP.** Registry login is client-side, so a pull from inside
  this container is anonymous. `ImagePullPolicy` defaults to `Missing`.
- **Containers are `ContainerLifetime.Persistent`.** DCP adopts them on start without re-checking the
  image, so an update must remove them before the app host starts again; every `update.ps1` does.
- **Compose labels are stamped explicitly**, or containers inherit their image's build-time labels and
  land in the wrong group in Docker Desktop.

## Ports

| What | Port |
|------|------|
| Dashboard | 15100 |
| OTLP gRPC (APIs) | 21100 |
| OTLP HTTP (browsers, via each web container's nginx) | 21101 |

## Commands

```shell
dotnet test AppHost.slnx
docker build -f AppHost/Dockerfile -t ghcr.io/hwndmaster/app-host:dev .
```

`publish-docker.ps1` builds and pushes `ghcr.io/hwndmaster/app-host:latest`; `packages-cleanup.ps1` prunes
old versions of that package.

## Scripts

`Deployment/update.ps1` runs on the server under Windows PowerShell 5.1 as well as 7. Never pipe native
stderr through `2>&1`: under `$ErrorActionPreference = "Stop"`, 5.1 turns it into a terminating error.
It shares the machine-wide mutex `Global\AppHostUpdate` with every application's `update.ps1`; keep the
name in step.
