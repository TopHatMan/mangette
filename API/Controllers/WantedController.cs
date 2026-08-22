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
