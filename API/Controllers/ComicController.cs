using API.Controllers.Requests;
using API.Schema.MangaContext;
using API.Workers.PeriodicWorkers;
using Asp.Versioning;
using log4net;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;
using Manga = API.Schema.MangaContext.Manga;

namespace API.Controllers;

/// <summary>
/// Comics have no scraping connector (see AGENTS/plan notes), so a series is added by hand instead of
/// searched: a title, an optional cover, a Comic-kind library, and the issue-number range to watch.
/// <see cref="MaterializeWantedComicIssuesWorker"/> then creates placeholder Chapters for those issues
/// and the indexer/download pipeline takes it from there.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class ComicController(MangaContext context) : ControllerBase
{
    private readonly ILog Log = LogManager.GetLogger(typeof(ComicController));

    /// <summary>
    /// Adds a Comic series to watch and queues materialization of its wanted issues.
    /// </summary>
    /// <response code="201">Key of the new Comic (Manga row)</response>
    /// <response code="400">Bad request data, or the library is not Comic-kind</response>
    /// <response code="500">Error during database operation</response>
    [HttpPut]
    [ProducesResponseType<string>(Status201Created, "text/plain")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Created<string>, BadRequest<string>, InternalServerError<string>>> AddComic(
        [FromBody] AddComicRecord requestData)
    {
        if (string.IsNullOrWhiteSpace(requestData.Name))
            return TypedResults.BadRequest("Name is required.");
        if (requestData.IssueEnd is { } issueEnd && issueEnd < requestData.IssueStart)
            return TypedResults.BadRequest("IssueEnd must be greater than or equal to IssueStart.");

        FileLibrary? library = await context.FileLibraries
            .FirstOrDefaultAsync(l => l.Key == requestData.FileLibraryId, HttpContext.RequestAborted);
        if (library is null)
            return TypedResults.BadRequest($"Unknown FileLibrary {requestData.FileLibraryId}.");
        if (library.Kind != MediaKind.Comic)
            return TypedResults.BadRequest($"Library \"{library.LibraryName}\" is not a Comic library.");

        Manga comic = new(requestData.Name, "", requestData.CoverUrl ?? "", MangaReleaseStatus.Continuing,
            [], [], [], [], library, year: requestData.Year)
        {
            Kind = MediaKind.Comic,
            ComicIssueStart = requestData.IssueStart,
            ComicIssueEnd = requestData.IssueEnd
        };
        comic.SetMonitored(true);

        context.Mangas.Add(comic);
        if (await context.Sync(HttpContext.RequestAborted, GetType(), "Add comic") is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        Mangette.AddWorker(new MaterializeWantedComicIssuesWorker(mangaId: comic.Key));
        Log.InfoFormat("Added Comic \"{0}\" (issues {1}-{2}) to library {3}.",
            comic.Name, requestData.IssueStart, requestData.IssueEnd?.ToString() ?? "∞", library.LibraryName);

        return TypedResults.Created(string.Empty, comic.Key);
    }
}
