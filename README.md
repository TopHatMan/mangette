# Mangette

Standalone manga downloader: one process serves the API, workers, and UI.

Open [http://localhost:8585](http://localhost:8585) after starting it.

No Docker and no Postgres. Cloudflare-protected sites use a **built-in Chromium** on the same machine (Chrome/Edge if installed, otherwise Chromium is downloaded into `data/chromium` on first use). FlareSolverr is optional.

## Run

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

On first run, Settings → **Paths and downloads** is the setup screen: library folder, temp/incomplete downloads, listen port, and how many chapters to grab at once.

**Add New** works like Sonarr/Radarr: search a title, pick **one** series, then Add. Search results are not saved to the library. New series use the default library automatically.

Recovering an old Tranga library: set **Library folder** to the existing `Manga` directory, then open **Library Import**. Mangette lists series folders, searches sites for matching titles, and imports them. Existing `.cbz` files (`Ch.001` vs `Ch.1`) are marked downloaded. Settings → **Scan library for existing chapters** re-runs the file match after series exist.

Data lives next to the executable:

| Path | What |
| --- | --- |
| `./data/mangette.db` | SQLite library |
| `./data/settings.json` | Settings (including listen port) |
| `./data/imageCache/` | Cover cache |
| `./data/incomplete/` | In-progress chapter images (cleaned up after each chapter) |
| `./Manga/` | Finished `.cbz` files (the default library) |

Override the app folder with `MANGETTE_HOME` and the default library folder with `DOWNLOAD_LOCATION`. Listen port defaults to `8585` (`PORT` env or Settings).

### Windows service (start at boot)

Mangette needs the **.NET 10 SDK** to build. `NETSDK1045` means this PC still has an old SDK (5.0, 6, 8, …). Install 10, then open a **new** PowerShell:

```powershell
winget install Microsoft.DotNet.SDK.10 --source winget
dotnet --list-sdks
```

You must see a `10.x` line. `C:\Program Files\dotnet` must be on PATH ahead of any `...\sdk\5.0...` folder.

Run PowerShell **as Administrator** from the cloned repo:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\install-win-service.ps1
```

That publishes `Mangette.exe` to `C:\Mangette`, creates a delayed auto-start service, opens firewall port 8585, and starts it. Open http://localhost:8585 (or `http://SERVER_IP:8585` from another PC). Chrome or Edge on Windows is enough for Cloudflare; no Docker VM is required.

You do **not** need `-LibraryPath` if Settings already has your library folder (it lives in `C:\Mangette\data\settings.json` and `mangette.db`). The script reads those files and keeps the existing service environment. Use `-LibraryPath D:\Manga` only on a brand-new install before you have saved Settings.

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

## Cloudflare bypass

When a site returns 403/429/Cloudflare, Mangette retries with **built-in Chromium**:

1. Google Chrome or Microsoft Edge on the machine, or
2. A Chromium build downloaded into `data/chromium` on first use

Settings → **Cloudflare bypass** → **Test Chromium**. `CHROME_BIN` / `PUPPETEER_EXECUTABLE_PATH` override the browser path.

### Optional FlareSolverr on a Debian VM (`192.168.1.210:8181`)

Mangette on Windows can use FlareSolverr in Docker on the VM. Compose binds **all interfaces** on host port **8181** (not loopback-only).

On the VM (bridged adapter so `192.168.1.210` is on the LAN):

```bash
# clone or copy this repo onto the VM, then:
docker compose up -d
# or: bash scripts/run-flaresolverr.sh
curl -sS -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8181/
```

If `ufw` is active: `ufw allow 8181/tcp`. NAT-only VMs need a VirtualBox TCP 8181 → 8181 forward.

On Windows, Settings → Cloudflare bypass → `http://192.168.1.210:8181` → Save → Test FlareSolverr. Or reinstall the service:

```powershell
.\scripts\install-win-service.ps1 -FlareSolverrUrl http://192.168.1.210:8181
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
