# app-host on the server

This folder goes to `C:\Docker\app-host` on the server. `C:\Docker` is the apps root: each application's
folder sits next to this one, with its deployment files from its repository and, by default, its data
and logs.

```
C:\Docker\
├─ app-host\          this folder, plus .env and apps.json created from their examples
├─ MyApp\             app.json, .env, docker-compose.yml, update.ps1 from my-app's Deployment\,
│                     plus Data\ and Logs\
└─ ...                other containers' folders, which the app host ignores
```

The app host mounts the whole apps root read-only and reads only the folders `apps.json` registers. It
can therefore see other folders under `C:\Docker` too. That is no new exposure: the Docker socket it
already holds gives it full control of this machine.

To try it on a development machine, `sync-apps.ps1` at the repository root assembles the same layout in
the repository's `build\` folder from the neighbouring application repositories.

| File | What it is |
|------|------------|
| `docker-compose.yml` | Runs the `app-host` container |
| `.env.example` | Copy to `.env`. The app host's own settings only: the apps root, the public host, dashboard access |
| `apps.example.json` | Copy to `apps.json`. Lists the applications to run and their folders |
| `apps.schema.json`, `app.schema.json` | Let an editor validate `apps.json` and each application's `app.json` |
| `update.ps1` | Installs or updates the app host |

## Install

1. The server needs Docker Desktop with Linux containers, logged in to the registry:
   `echo <token with read:packages> | docker login ghcr.io -u hwndmaster --password-stdin`
2. Copy this folder to `C:\Docker\app-host`, then `Copy-Item .env.example .env` and
   `Copy-Item apps.example.json apps.json`. Check `APPHOST_APPS_ROOT` and set `APPHOST_PUBLIC_HOST` in
   `.env`.
3. `.\update.ps1`
4. Open `http://<server>:15100`. The login link is in `docker logs app-host`.

## Add an application

1. Copy the application's `Deployment\` folder to its folder under `C:\Docker` (for MyApp
   `C:\Docker\MyApp`), create its `.env` from `.env.example`, and create the data folders its
   `.env` names.
2. Add it to `apps.json`: `name` as in its `app.json`, `path` the folder inside `C:\Docker`, e.g.
   `{ "name": "my-app", "path": "MyApp" }`.
3. Run the application's own `update.ps1`. It pulls the images and restarts the app host, which creates
   the containers.

## Update

- **An application:** its own `update.ps1`. Only that application is recreated.
- **The app host:** `.\update.ps1` here. With `-SkipPull` it applies a changed `.env` or `apps.json`. The
  applications keep running either way.

Run one update at a time. Every update script takes the same machine-wide lock, so a second one waits.

## When something is wrong

- **The app host does not start.** `docker logs app-host` names the problem in `apps.json`.
- **An application shows "Configuration error"** on the Resources page. Its row says what is wrong in its
  `app.json` or `.env`. Fix it and run that application's `update.ps1 -SkipPull`.
- **A container shows "Failed to start".** The reason is in DCP's log inside the app host:
  `docker exec app-host sh -c "tail -n 40 /tmp/aspire-dcp*/resource-container-*.log"`
