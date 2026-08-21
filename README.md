# Mangette

Standalone manga downloader: one process serves the API, workers, and UI.

Open [http://localhost:8585](http://localhost:8585) after starting it.

Mangette itself does not use Docker. **FlareSolverr does** — that is the Cloudflare bypass for sites that block plain HTTP. Run it as a sidecar, then start the Mangette binary.

## Run

### 1. Start FlareSolverr (Docker)

```bash
docker compose up -d
```

That publishes FlareSolverr at `http://127.0.0.1:8191`. Mangette uses that URL by default (`FLARESOLVERR_URL` to override).

If Docker is on another machine (for example a Debian VirtualBox VM at `192.168.1.210`), bind FlareSolverr on all interfaces **on the VM**:

```bash
# on the Debian VM, in the repo folder
echo 'FLARESOLVERR_BIND=0.0.0.0' > .env
docker compose up -d
sudo ufw allow 8191/tcp   # only if ufw is enabled
```

On Windows, set FlareSolverr to `http://192.168.1.210:8191` (Settings, or `FLARESOLVERR_URL`). The VM must use **bridged** networking so that IP is reachable.

### 2. Start Mangette

### Pre-built binary

Linux (Debian/x64):

```bash
chmod +x Mangette
./Mangette
```

Windows:

```powershell
.\Mangette.exe
```

Then open http://localhost:8585.

On first run, Settings → **Paths and downloads** is the setup screen: library folder, temp/incomplete downloads, listen port, and how many chapters to grab at once. New series are assigned to that library automatically.

Recovering an old Tranga library: point **Library folder** at the existing `Manga` directory, add the series, then enable download. Mangette scans that folder for `.cbz` files (including `Ch.001` vs `Ch.1`) and marks those chapters downloaded instead of grabbing them again. Settings → **Scan library for existing chapters** re-runs that scan.

Data lives next to the executable:

| Path | What |
| --- | --- |
| `./data/mangette.db` | SQLite library |
| `./data/settings.json` | Settings (including listen port) |
| `./data/imageCache/` | Cover cache |
| `./data/incomplete/` | In-progress chapter images (cleaned up after each chapter) |
| `./Manga/` | Finished `.cbz` files (the default library) |

Override the app folder with `MANGETTE_HOME` and the default library folder with `DOWNLOAD_LOCATION`. Listen port defaults to `8585` (`PORT` env or Settings). FlareSolverr stays on `8191`.

No Postgres. Docker is only for FlareSolverr.

### Windows service (start at boot)

Mangette needs the **.NET 10 SDK** to build. `NETSDK1045` means this PC still has an old SDK (5.0, 6, 8, …). Install 10, then open a **new** PowerShell:

```powershell
winget install Microsoft.DotNet.SDK.10 --source winget
dotnet --list-sdks
```

You must see a `10.x` line. `C:\Program Files\dotnet` must be on PATH ahead of any `...\sdk\5.0...` folder.

Run PowerShell **as Administrator** from the cloned repo. Example for Docker on a Debian VM and an existing Tranga library:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\install-win-service.ps1 `
  -FlareSolverrUrl http://192.168.1.210:8191 `
  -LibraryPath D:\Manga
```

That publishes `Mangette.exe` to `C:\Mangette`, creates a **delayed auto-start** service (so VirtualBox can boot the VM first), opens firewall port 8585, and starts it. Open http://localhost:8585 (or `http://SERVER_IP:8585` from another PC).

```powershell
Get-Service Mangette
Get-Content C:\Mangette\data\logs\mangette.log -Tail 50
.\scripts\uninstall-win-service.ps1
```

### From source (manual, not auto-start)

Needs the **.NET 10 SDK** (`dotnet --list-sdks` must show `10.x`):

```powershell
dotnet run --project API/API.csproj
```

## Multi-source downloads

A series can have several websites attached. For each missing chapter, Mangette uses the **first source in your priority list** that has that chapter and is not cooling down.

Default order (change it in Settings → Download source priority):

**WeebCentral → MangaDex → NeloManga → MangaTown → FanFox → AsuraComic → Mangaworld**

If a source returns 403, Cloudflare, or an empty image list, that source is backed off (30 minutes, doubling up to 6 hours) instead of being retried every minute. The next source in the list is used for the same chapter.

## FlareSolverr (required for Cloudflare)

Protected sources (WeebCentral, some others) go through [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr) when HTTP returns 403/429 or a Cloudflare server header.

```bash
docker compose up -d          # FlareSolverr on 127.0.0.1:8191
docker compose logs -f        # watch challenge / IP-ban messages
```

Mangette talks to it at `http://127.0.0.1:8191`. Change that in Settings or with `FLARESOLVERR_URL`. If FlareSolverr is down, those sites will fail until you start the container.

Chrome/Chromium on the host is a **fallback** only (WeebCentral chapter pages if FlareSolverr cannot load them):

```bash
export CHROME_BIN=/usr/bin/chromium
# or
export PUPPETEER_EXECUTABLE_PATH=/usr/bin/google-chrome
```

## Publish a single-file binary

Linux x64 (primary):

```bash
bash scripts/publish-linux-x64.sh
# dist/linux-x64/Mangette
```

Windows x64:

```powershell
.\scripts\publish-win-x64.ps1
# dist\win-x64\Mangette.exe
```

Rebuild the UI as part of publish with `SKIP_FRONTEND=false` (needs Node.js).

## API

Swagger stays at http://localhost:8585/swagger. Routes are under `/v2`.

## Sources

- [WeebCentral](https://weebcentral.com/)
- [MangaDex](https://mangadex.org/)
- [NeloManga](https://nelomanga.net/)
- [MangaTown](https://www.mangatown.com/)
- [FanFox](https://fanfox.net/)
- [AsuraComic](https://asurascanz.com)
- [MangaWorld](https://www.mangaworld.cx)

Library scan: [Komga](https://komga.org/), [Kavita](https://www.kavitareader.com/).  
Notifications: Gotify, Ntfy, Pushover, or a generic webhook.
