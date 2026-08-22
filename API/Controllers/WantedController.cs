using API.Schema.MangaContext;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class WantedController(MangaContext context) : ControllerBase
{
    /// <summary>
    /// Monitored chapters that are not on disk — Sonarr Wanted → Missing.
    /// </summary>
    [HttpGet("Missing")]
    [ProducesResponseType<WantedMissing>(Status200OK, "application/json")]
    public async Task<Ok<WantedMissing>> Missing()
    {
        List<Chapter> rows = await context.Chapters
            .AsNoTracking()
            .Include(c => c.ParentManga)
            .Include(c => c.MangaConnectorIds)
            .Where(c => !c.Downloaded && c.MangaConnectorIds.Any(id => id.UseForDownload))
            .ToListAsync(HttpContext.RequestAborted);

        rows = rows
            .Where(c => c.MangaConnectorIds.Any(id =>
                id.UseForDownload &&
                Mangette.TryGetMangaConnector(id.MangaConnectorName, out MangaConnectors.MangaConnector? connector) &&
                connector.Enabled))
            .ToList();

        Dictionary<string, int> totals = await context.Chapters
            .AsNoTracking()
            .GroupBy(c => c.ParentMangaId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, HttpContext.RequestAborted);

        List<WantedSeries> series = rows
            .GroupBy(c => c.ParentMangaId)
            .Select(g =>
            {
                Manga manga = g.First().ParentManga;
                int chapterCount = totals.GetValueOrDefault(manga.Key);
                List<WantedChapter> missing = g
                    .OrderBy(c => c, new Chapter.ChapterComparer())
                    .Take(40)
                    .Select(c => new WantedChapter(c.Key, c.ChapterNumber, c.VolumeNumber, c.Title))
                    .ToList();
                return new WantedSeries(
                    manga.Key,
                    manga.Name,
                    g.Count(),
                    Math.Max(0, chapterCount - g.Count()),
                    chapterCount,
                    missing);
            })
            .OrderByDescending(s => s.MissingCount)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return TypedResults.Ok(new WantedMissing(rows.Count, series));
    }

    /// <summary>Preview archives in a folder and guess series/chapter, like Sonarr Manual Import.</summary>
    [HttpGet("ManualImport")]
    [ProducesResponseType<ManualImportPreview>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<ManualImportPreview>, BadRequest<string>>> PreviewManualImport([FromQuery] string? folder = null)
    {
        string? root = folder;
        if (string.IsNullOrWhiteSpace(root))
        {
            FileLibrary? library = await context.FileLibraries.OrderBy(l => l.LibraryName).FirstOrDefaultAsync(HttpContext.RequestAborted);
            root = library?.BasePath;
        }
        if (string.IsNullOrWhiteSpace(root))
            return TypedResults.BadRequest("No folder. Pass folder= or set a library path in Settings.");

        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(root);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
        if (!Directory.Exists(fullRoot))
            return TypedResults.BadRequest($"Folder does not exist: {fullRoot}");

        List<Manga> mangas = await context.Mangas
            .AsNoTracking()
            .Include(m => m.Library)
            .Include(m => m.Chapters)
            .OrderBy(m => m.Name)
            .ToListAsync(HttpContext.RequestAborted);

        List<ManualImport.SeriesInfo> seriesInfos = mangas
            .Select(m => new ManualImport.SeriesInfo(m.Key, m.Name, m.DirectoryName, m.Library?.BasePath))
            .ToList();
        Dictionary<string, List<ManualImport.ChapterInfo>> chapters = mangas.ToDictionary(
            m => m.Key,
            m => m.Chapters.Select(c => new ManualImport.ChapterInfo(c.Key, c.ChapterNumber, c.VolumeNumber, c.Downloaded, c.FileName)).ToList());
        HashSet<string> claimed = ManualImport.ClaimedArchivePaths(seriesInfos, chapters);

        List<ManualImportFile> files = [];
        foreach (string path in ManualImport.EnumerateArchives(fullRoot))
        {
            ManualImport.FileGuess guess = ManualImport.Guess(path, seriesInfos, chapters, claimed);
            if (claimed.Contains(guess.Path))
                continue;
            files.Add(new ManualImportFile(
                guess.Path,
                guess.FileName,
                guess.Size,
                guess.MangaId,
                guess.MangaName,
                guess.ChapterId,
                guess.ChapterNumber,
                guess.Volume,
                guess.Score));
        }

        List<ManualImportSeriesOption> options = mangas
            .Select(m => new ManualImportSeriesOption(m.Key, m.Name))
            .ToList();

        return TypedResults.Ok(new ManualImportPreview(fullRoot, files, options, files.Count >= ManualImport.MaxFiles));
    }

    [HttpGet("MissingChapters/{MangaId}")]
    [ProducesResponseType<List<WantedChapter>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<List<WantedChapter>>, NotFound<string>>> MissingChapters(string MangaId)
    {
        if (!await context.Mangas.AnyAsync(m => m.Key == MangaId, HttpContext.RequestAborted))
            return TypedResults.NotFound(nameof(MangaId));

        List<WantedChapter> chapters = await context.Chapters
            .AsNoTracking()
            .Where(c => c.ParentMangaId == MangaId && !c.Downloaded)
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new WantedChapter(c.Key, c.ChapterNumber, c.VolumeNumber, c.Title))
            .ToListAsync(HttpContext.RequestAborted);
        return TypedResults.Ok(chapters);
    }

    [HttpPost("ManualImport")]
    [ProducesResponseType<ManualImportResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<ManualImportResult>, BadRequest<string>>> CommitManualImport([FromBody] ManualImportRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            return TypedResults.BadRequest("No files selected.");

        int imported = 0;
        List<string> errors = [];
        foreach (ManualImportItem item in request.Items)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.Path) ||
                    string.IsNullOrWhiteSpace(item.MangaId) ||
                    string.IsNullOrWhiteSpace(item.ChapterId))
                {
                    errors.Add("Skip a row with no series/chapter.");
                    continue;
                }

                string source = Path.GetFullPath(item.Path);
                if (!System.IO.File.Exists(source) || !DownloadedChapterMatcher.IsArchive(source))
                {
                    errors.Add($"Missing archive: {item.Path}");
                    continue;
                }

                Manga? manga = await context.Mangas
                    .Include(m => m.Library)
                    .Include(m => m.Chapters)
                    .FirstOrDefaultAsync(m => m.Key == item.MangaId, HttpContext.RequestAborted);
                if (manga is null)
                {
                    errors.Add($"Series not found for {Path.GetFileName(source)}.");
                    continue;
                }
                await context.AssignDefaultLibraryIfMissing(manga, HttpContext.RequestAborted);
                if (manga.Library is null || string.IsNullOrWhiteSpace(manga.Library.BasePath))
                {
                    errors.Add($"No library folder for {manga.Name}.");
                    continue;
                }

                Chapter? chapter = manga.Chapters.FirstOrDefault(c => c.Key == item.ChapterId);
                if (chapter is null)
                {
                    errors.Add($"Chapter not found for {Path.GetFileName(source)}.");
                    continue;
                }

                chapter.ParentManga = manga;
                string seriesDir = Path.GetFullPath(Path.Combine(manga.Library.BasePath, manga.DirectoryName));
                Directory.CreateDirectory(seriesDir);
                string destName = Path.GetFileName(source);
                string dest = Path.GetFullPath(Path.Combine(seriesDir, destName));
                if (!string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
                {
                    if (System.IO.File.Exists(dest))
                    {
                        destName = $"{Path.GetFileNameWithoutExtension(destName)}-import{Path.GetExtension(destName)}";
                        dest = Path.Combine(seriesDir, destName);
                    }
                    System.IO.File.Copy(source, dest, overwrite: false);
                    if (request.DeleteSource)
                    {
                        try
                        {
                            System.IO.File.Delete(source);
                        }
                        catch
                        {
                            /* keep source if delete fails */
                        }
                    }
                }

                chapter.FileName = destName;
                chapter.Downloaded = true;
                imported++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(item.Path)}: {ex.Message}");
            }
        }

        if (imported > 0 &&
            await context.Sync(HttpContext.RequestAborted, GetType(), "Manual import") is { success: false } sync)
            return TypedResults.BadRequest(sync.exceptionMessage ?? "Could not save.");

        return TypedResults.Ok(new ManualImportResult(imported, errors));
    }
}

public sealed record WantedMissing(int TotalMissing, IReadOnlyList<WantedSeries> Series);

public sealed record WantedSeries(
    string MangaId,
    string Name,
    int MissingCount,
    int DownloadedCount,
    int ChapterCount,
    IReadOnlyList<WantedChapter> Chapters);

public sealed record WantedChapter(string ChapterId, string ChapterNumber, int? Volume, string? Title);

public sealed record ManualImportPreview(
    string Folder,
    IReadOnlyList<ManualImportFile> Files,
    IReadOnlyList<ManualImportSeriesOption> Series,
    bool Truncated);

public sealed record ManualImportFile(
    string Path,
    string FileName,
    long Size,
    string? MangaId,
    string? MangaName,
    string? ChapterId,
    string? ChapterNumber,
    int? Volume,
    double Score);

public sealed record ManualImportSeriesOption(string MangaId, string Name);

public sealed record ManualImportRequest(List<ManualImportItem> Items, bool DeleteSource = false);

public sealed record ManualImportItem(string Path, string MangaId, string ChapterId);

public sealed record ManualImportResult(int Imported, IReadOnlyList<string> Errors);
