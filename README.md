# Mangette

**Sonarr-style manga library.** One process, one database, no Docker required.

Search a title, add **one** series, and Mangette keeps it complete from multiple sites. Existing `.cbz` folders import without re-downloading.

Open [http://localhost:8585](http://localhost:8585) after it starts.

[![Unit Tests](https://github.com/TopHatMan/mangette/actions/workflows/run-tests.yml/badge.svg)](https://github.com/TopHatMan/mangette/actions/workflows/run-tests.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)

## Why

Mangette is a fork of [Tranga](https://github.com/C9Glax/tranga) rebuilt as a standalone app:

- **No Postgres.** SQLite next to the executable.
- **No Docker** to run the app. Chrome/Edge on the machine handles Cloudflare. FlareSolverr is optional.
- **Add New like Sonarr.** Search is preview-only. Only the series you add lands in the library.
- **Multi-source.** One series can use several sites. First source in your list that has the chapter wins. Failures back off instead of spinning forever.
- **Windows service** or a single `Mangette.exe`.

## Features

- Poster library + table view, source priority, activity log
- Library Import: scan series folders, match titles, mark existing `Ch.001` / `Ch.1` archives as downloaded
- Built-in Chromium Cloudflare bypass (Chrome, Edge, or a download into `data/chromium`)
- Komga / Kavita library connectors
- Gotify, Ntfy, Pushover, or a generic webhook

**Default source order** (change in Settings):

WeebCentral → MangaDex → NeloManga → MangaTown → FanFox → AsuraComic → Mangaworld

## Quick start

Needs the **.NET 10 SDK** (`dotnet --list-sdks` must show `10.x`). `NETSDK1045` means this PC still has an old SDK.

```powershell
winget install Microsoft.DotNet.SDK.10 --source winget
```

### Windows (CMD)

From the cloned repo, stop any old Mangette window first:

```cmd
git pull
run.cmd
```

Or:

```cmd
dotnet run --project API\API.csproj --no-launch-profile
```

Then open http://localhost:8585. You should see a **dark left sidebar** (Library, Add New, Import, Activity, Settings).

First-run: Settings → **Paths and downloads** → set the library folder to a path this process can read (`D:\Manga`, not a mapped `Z:\`).

### Windows service (start at boot)

PowerShell **as Administrator** from the repo:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\install-win-service.ps1
```

That publishes `C:\Mangette\Mangette.exe`, delayed auto-start, firewall port 8585.

```powershell
Get-Service Mangette
Get-Content C:\Mangette\data\logs\mangette.log -Tail 50
.\scripts\uninstall-win-service.ps1
```

### Linux

```bash
bash scripts/publish-linux-x64.sh
chmod +x dist/linux-x64/Mangette
./dist/linux-x64/Mangette
```

## Add series and import a library

**Add New** — type a title, pick one result, Add. Other hits are not saved.

**Import** — for an existing Tranga/Mangette folder of series directories. Scan folders → Match all → Import. Settings → **Scan library for existing chapters** only matches files after series already exist in Mangette.

## Data

Next to the executable (or `MANGETTE_HOME`):

| Path | What |
| --- | --- |
| `data/mangette.db` | SQLite library |
| `data/settings.json` | Settings (port, FlareSolverr URL, …) |
| `data/imageCache/` | Covers |
| `data/incomplete/` | In-progress chapter images |
| `Manga/` | Finished `.cbz` (default library) |

`PORT` (default `8585`), `DOWNLOAD_LOCATION` (first-run library only). After Settings is saved, the library path lives in the database.

## Cloudflare

Built-in Chromium is the default. Settings → Cloudflare bypass → **Test Chromium**.

**FlareSolverr is optional.** If you already run one (often Docker on a Linux box), set its URL in Settings, e.g. `http://192.168.1.210:8191`. Compose in this repo uses host networking on port **8191** for a Debian VM; VirtualBox needs a **bridged** adapter (or a NAT forward of 8191). From Windows, `curl` that URL before saving it in Mangette. If the VM does not answer, Mangette still works with Chromium.

## Publish

```bash
bash scripts/publish-linux-x64.sh    # dist/linux-x64/Mangette
```

```powershell
.\scripts\publish-win-x64.ps1        # dist\win-x64\Mangette.exe
```

`SKIP_FRONTEND=false` rebuilds the UI (needs Node.js).

API: http://localhost:8585/swagger (`/v2`).

## Sources

[WeebCentral](https://weebcentral.com/) · [MangaDex](https://mangadex.org/) · [NeloManga](https://nelomanga.net/) · [MangaTown](https://www.mangatown.com/) · [FanFox](https://fanfox.net/) · [AsuraComic](https://asurascanz.com) · [MangaWorld](https://www.mangaworld.cx)

Library: [Komga](https://komga.org/), [Kavita](https://www.kavitareader.com/).

## Credits

Mangette is a fork of [Tranga](https://github.com/C9Glax/tranga) by [C9Glax](https://github.com/C9Glax). The original UI lived in [tranga-website](https://github.com/C9Glax/tranga-website).

## License

[GNU GPLv3](LICENSE)
