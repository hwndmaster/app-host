# app-host

One Aspire app host for the self-hosted applications deployed on a server (such as PrepperBox,
PriceChecker, etc.). It creates and supervises their containers, and its dashboard shows their resources,
console logs, structured logs, traces and metrics in one place. Telemetry is stored on a volume and
survives restarts.

Each application still updates on its own. Its `update.ps1` pulls its images, removes its two containers
and restarts the app host, which recreates them. The other applications keep running throughout: their
containers are persistent and do not depend on the app host to run.

## How an application is described

The app host's `apps.json` only lists the applications and their folders:

```json
{ "apps": [ { "path": "my-app" } ] }
```

An entry may also carry a `name`, but only as a label for messages; the application's own name comes from
its `app.json`.

Each folder comes from the application's own repository (`Deployment/`) and holds its `app.json`:

```json
{
  "name": "my-app",
  "displayName": "MyApp",
  "type": "atom-webapp",
  "imageTag": "${MY_APP_IMAGE_TAG:-latest}",
  "api": {
    "containerPort": 8041,
    "hostPort": "${MY_APP_API_PORT:-5090}",
    "memory": "512m",
    "volumes": [ { "host": "${MY_APP_DATA_PATH}", "container": "/app/Data" } ]
  },
  "web": { "containerPort": 8042, "hostPort": 5091, "memory": "64m" }
}
```

`${NAME}` values come from the `.env` next to it. The `atom-webapp` type adds everything the applications
share: container and image names, telemetry settings, nginx upstreams, log rotation, labels, and the
grouping on the Resources page. `Deployment/app.schema.json` describes every field.

## Develop

```shell
dotnet test AppHost.slnx
```

### Running it locally

On the server the apps root is `C:\Docker`. Locally it is this repository's `build\` folder (git-ignored),
which `sync-apps.ps1` assembles with the same layout:

```
build\
├─ app-host\          this repository's Deployment\, plus .env and apps.json
├─ my-app\            my-app's Deployment\, plus .env
└─ ...                one folder per application
```

It takes as applications the repositories next to this one that have a `Deployment\app.json` (`-Apps`
narrows that down). It copies their deployment files in again on every run, and creates each `.env` and
`apps.json` only once. An application's `.env` is taken from its repository's `Deployment\.env` if one
exists (an earlier local deployment), otherwise from its example.

```powershell
.\sync-apps.ps1
docker compose build                      # the app host image, tagged latest
.\build\app-host\update.ps1 -SkipPull     # start the app host from it
.\build\my-app\update.ps1                 # pull and start one application
.\update-apps.ps1                         # or every application, one after another
```

Alternatively, run the app host from the IDE with the `local` launch profile. It reads `build\` too, but
the dashboard then runs on this machine instead of in a container.

## Publish and deploy

```powershell
.\publish-docker.ps1 -RegistryToken <token with write:packages>
```

Installing on the server, adding applications and updating are described in
[`Deployment/README.md`](Deployment/README.md) and in the operator runbook.
