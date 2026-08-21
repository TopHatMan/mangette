# Mangette

Standalone manga downloader: one process serves the API, workers, and UI.

Open [http://localhost:6531](http://localhost:6531) after starting it.

## Run

### Pre-built binary

Linux (Debian/x64):

```bash
chmod +x API
./API
```

Windows:

```powershell
.\API.exe
```

Then open http://localhost:6531.

Data lives next to the executable:

| Path | What |
| --- | --- |
| `./data/mangette.db` | SQLite library |
| `./data/settings.json` | Settings |
| `./data/imageCache/` | Cover cache |
| `./Manga/` | Downloaded `.cbz` files |

Override the app folder with `MANGETTE_HOME` and the download folder with `DOWNLOAD_LOCATION`. Port is `6531` (`PORT` to change).

No Docker. No Postgres.

### From source

```bash
dotnet run --project API/API.csproj
```

## Multi-source downloads

A series can have several websites attached (MangaDex, AsuraComic, Mangaworld, WeebCentral, …). Missing chapters are taken from the best source that is not cooling down:

**MangaDex → AsuraComic → Mangaworld → WeebCentral → other**

If a source returns 403, Cloudflare, or an empty image list, that source is backed off (30 minutes, doubling up to 6 hours) instead of being retried every minute. Another attached source is used for the same chapter when it has it.

## Chrome / FlareSolverr

Most sources use plain HTTP. Chrome is **optional** and only needed for sites that must be scraped in a browser (WeebCentral chapter pages).

Install Chrome/Chromium locally and point at it:

```bash
export CHROME_BIN=/usr/bin/chromium
# or
export PUPPETEER_EXECUTABLE_PATH=/usr/bin/google-chrome
```

Docker Chromium is not required.

[FlareSolverr](https://github.com/FlareSolverr/FlareSolverr) is optional. Set the URL in Settings if you run one.

## Publish a single-file binary

Linux x64 (primary):

```bash
bash scripts/publish-linux-x64.sh
# dist/linux-x64/API
```

Windows x64:

```powershell
.\scripts\publish-win-x64.ps1
# dist\win-x64\API.exe
```

Rebuild the UI as part of publish with `SKIP_FRONTEND=false` (needs Node.js).

## API

Swagger stays at http://localhost:6531/swagger. Routes are under `/v2`.

## Sources

- [MangaDex](https://mangadex.org/)
- [MangaWorld](https://www.mangaworld.cx)
- [AsuraComic](https://asurascanz.com)
- [WeebCentral](https://weebcentral.com/)

Library scan: [Komga](https://komga.org/), [Kavita](https://www.kavitareader.com/).  
Notifications: Gotify, Ntfy, Pushover, or a generic webhook.
