using API.Schema.MangaContext;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

/// <summary>
/// Dry-run only: reports what a rename/flatten pass would do to a Comic series' downloaded issues,
/// without moving or renaming a single file. Actually applying this is a separate, not-yet-built,
/// explicitly-confirmed step -- this controller never calls File.Move.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class ComicReorganizeController(MangaContext context) : ControllerBase
{
    [HttpGet("Preview")]
    [ProducesResponseType<List<ComicReorganizeEntry>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<List<ComicReorganizeEntry>>, NotFound<string>>> Preview([FromQuery] string mangaId)
    {
        Manga? comic = await context.Mangas
            .Include(m => m.Library)
            .Include(m => m.Chapters)
            .FirstOrDefaultAsync(m => m.Key == mangaId, HttpContext.RequestAborted);
        if (comic?.Library is null)
            return TypedResults.NotFound($"Unknown Manga {mangaId}, or it has no library.");

        List<ComicReorganizeEntry> entries = comic.Chapters
            .Where(c => c.Downloaded && c.FileName is not null)
            .Select(c => new ComicReorganizeEntry(
                c.Key,
                c.ChapterNumber,
                c.FileName!,
                c.GetArchiveFileName(Path.GetExtension(c.FileName!))))
            .Where(e => !e.CurrentRelativePath.Equals(e.ProposedRelativePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.CurrentRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return TypedResults.Ok(entries);
    }
}

public sealed record ComicReorganizeEntry(string ChapterId, string IssueNumber, string CurrentRelativePath, string ProposedRelativePath);
