using API.Controllers.Requests;
using API.IndexerConnectors;
using API.Schema.MangaContext;
using API.Schema.MangaContext.MetadataFetchers;
using API.Workers.PeriodicWorkers;
using Asp.Versioning;
using log4net;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using static Microsoft.AspNetCore.Http.StatusCodes;
using Manga = API.Schema.MangaContext.Manga;

namespace API.Controllers;

/// <summary>
/// Comics have no scraping connector, so watching a series works like the manga "Add New" search
/// page but against ComicVine instead of a site: search a title, pick the exact volume/run (e.g.
/// "Batman v1 (1940)" vs "Batman (2011) New 52" are different ComicVine volumes), then add it with
/// an issue-number range to watch. <see cref="MaterializeWantedComicIssuesWorker"/> turns that range
/// into placeholder Chapters and the indexer/download pipeline takes it from there.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class ComicController(MangaContext context) : ControllerBase
{
    private readonly ILog Log = LogManager.GetLogger(typeof(ComicController));

    /// <summary>Searches ComicVine volumes -- pick the exact result you want, then AddComic with its ComicVineVolumeId.</summary>
    /// <response code="200"></response>
    /// <response code="400">Query is empty, or ComicVine is not configured</response>
    [HttpGet("Search")]
    [ProducesResponseType<List<ComicVineVolumeSummary>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<List<ComicVineVolumeSummary>>, BadRequest<string>>> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return TypedResults.BadRequest("Query is required.");
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ComicVineApiKey))
            return TypedResults.BadRequest("ComicVine is not configured. Set an API key in Settings first.");

        ComicVineVolumeSummary[] results = await Mangette.ComicVine.SearchVolumes(query.Trim(), HttpContext.RequestAborted);
        return TypedResults.Ok(results.ToList());
    }

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
        if (requestData.IssueEnd is { } issueEnd && issueEnd < requestData.IssueStart)
            return TypedResults.BadRequest("IssueEnd must be greater than or equal to IssueStart.");

        FileLibrary? library = await context.FileLibraries
            .FirstOrDefaultAsync(l => l.Key == requestData.FileLibraryId, HttpContext.RequestAborted);
        if (library is null)
            return TypedResults.BadRequest($"Unknown FileLibrary {requestData.FileLibraryId}.");
        if (library.Kind != MediaKind.Comic)
            return TypedResults.BadRequest($"Library \"{library.LibraryName}\" is not a Comic library.");

        string name = requestData.Name ?? "";
        string coverUrl = requestData.CoverUrl ?? "";
        string description = "";
        uint? year = requestData.Year;
        int? issueEndFromComicVine = requestData.IssueEnd;
        string? comicVineSiteUrl = null;

        if (!string.IsNullOrWhiteSpace(requestData.ComicVineVolumeId))
        {
            JObject? volume = await Mangette.ComicVine.GetVolume(requestData.ComicVineVolumeId, HttpContext.RequestAborted);
            if (volume is null)
                return TypedResults.BadRequest($"Could not load ComicVine volume {requestData.ComicVineVolumeId}.");

            name = volume.Value<string>("name") ?? name;
            coverUrl = volume.Value<JObject>("image")?.Value<string>("medium_url") ?? coverUrl;
            description = ComicVine.StripHtml(volume.Value<string>("description") ?? "");
            year = uint.TryParse(volume.Value<string>("start_year"), out uint y) ? y : year;
            comicVineSiteUrl = volume.Value<string>("site_detail_url");
            if (issueEndFromComicVine is null && volume.Value<int?>("count_of_issues") is { } issueCount and > 0)
                issueEndFromComicVine = issueCount;
        }

        if (string.IsNullOrWhiteSpace(name))
            return TypedResults.BadRequest("Name is required when ComicVineVolumeId is not set.");

        (string resolvedName, string? nameError) = await ComicAcquisition.ResolveNonCollidingName(context, name, year, HttpContext.RequestAborted);
        if (nameError is not null)
            return TypedResults.BadRequest(nameError);
        name = resolvedName;

        Manga comic = new(name, description, coverUrl, MangaReleaseStatus.Continuing, [], [], [], [], library, year: year)
        {
            Kind = MediaKind.Comic,
            ComicIssueStart = requestData.IssueStart,
            ComicIssueEnd = issueEndFromComicVine
        };
        comic.SetMonitored(true);
        if (!string.IsNullOrWhiteSpace(comicVineSiteUrl))
            comic.Links.Add(new Link("ComicVine", comicVineSiteUrl));

        context.Mangas.Add(comic);
        if (await context.Sync(HttpContext.RequestAborted, GetType(), "Add comic") is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        Mangette.AddWorker(new MaterializeWantedComicIssuesWorker(mangaId: comic.Key));
        Log.InfoFormat("Added Comic \"{0}\" (issues {1}-{2}) to library {3}.",
            comic.Name, requestData.IssueStart, comic.ComicIssueEnd?.ToString() ?? "∞", library.LibraryName);

        return TypedResults.Created(string.Empty, comic.Key);
    }

    /// <summary>
    /// Interactive search for one Comic issue: live Prowlarr search (comics have no scraping
    /// connector/cataloged release list to look up like manga's chapter Releases does).
    /// </summary>
    /// <response code="200"></response>
    /// <response code="400">Not a comic issue, or the indexer search failed</response>
    /// <response code="404">Unknown ChapterId</response>
    [HttpGet("Chapters/{ChapterId}/Releases")]
    [ProducesResponseType<List<IndexerRelease>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<List<IndexerRelease>>, BadRequest<string>, NotFound<string>>> ChapterReleases(
        string ChapterId, [FromQuery] string? query = null)
    {
        if (await context.Chapters.Include(c => c.ParentManga).FirstOrDefaultAsync(c => c.Key == ChapterId, HttpContext.RequestAborted)
            is not { } chapter)
            return TypedResults.NotFound(nameof(ChapterId));
        if (chapter.ParentManga.Kind != MediaKind.Comic)
            return TypedResults.BadRequest("This chapter belongs to a Manga series, not a Comic. Use Chapters/{ChapterId}/Releases instead.");

        string q = string.IsNullOrWhiteSpace(query) ? ComicAcquisition.BuildQuery(chapter.ParentManga) : query.Trim();
        try
        {
            IndexerRelease[] releases = await ComicAcquisition.Indexer.Search(q, HttpContext.RequestAborted);
            // Releases whose title parses to this exact issue number surface first; everything else
            // (omnibuses, whole-run packs, wrong issue) still shows below for the user to eyeball.
            List<IndexerRelease> sorted = releases
                .OrderByDescending(r => ComicAcquisition.MatchesIssue(r, chapter))
                .ToList();
            return TypedResults.Ok(sorted);
        }
        catch (Exception ex)
        {
            Log.Error($"Interactive indexer search failed for \"{q}\": {ex.Message}", ex);
            return TypedResults.BadRequest($"Indexer search failed: {ex.Message}");
        }
    }

    /// <summary>Sends one release the user picked from ChapterReleases to qBittorrent or SABnzbd.</summary>
    /// <response code="200"></response>
    /// <response code="400">Not a comic issue, already downloaded, already has an in-progress download, or the client rejected the release</response>
    /// <response code="404">Unknown ChapterId</response>
    /// <response code="500">Error during database operation</response>
    [HttpPost("Chapters/{ChapterId}/Grab")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, BadRequest<string>, NotFound<string>, InternalServerError<string>>> GrabChapterRelease(
        string ChapterId, [FromBody] GrabComicReleaseRequest requestData)
    {
        if (await context.Chapters.Include(c => c.ParentManga).FirstOrDefaultAsync(c => c.Key == ChapterId, HttpContext.RequestAborted)
            is not { } chapter)
            return TypedResults.NotFound(nameof(ChapterId));
        if (chapter.ParentManga.Kind != MediaKind.Comic)
            return TypedResults.BadRequest("This chapter belongs to a Manga series, not a Comic.");
        if (chapter.Downloaded)
            return TypedResults.BadRequest("This issue is already downloaded.");
        if (await context.ComicDownloadJobs.AnyAsync(
                j => j.ChapterId == chapter.Key && j.Status != ComicDownloadJobStatus.Failed, HttpContext.RequestAborted))
            return TypedResults.BadRequest("This issue already has an in-progress download.");

        IndexerRelease? release = requestData.Release;
        if (release is null)
        {
            // No release specified = auto-search-and-grab, same idea as manga's plain "Grab" button
            // picking the best attached site instead of asking the user to choose one.
            string query = ComicAcquisition.BuildQuery(chapter.ParentManga);
            IndexerRelease[] releases;
            try
            {
                releases = await ComicAcquisition.Indexer.Search(query, HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                Log.Error($"Auto-search failed for \"{query}\": {ex.Message}", ex);
                return TypedResults.BadRequest($"Indexer search failed: {ex.Message}");
            }
            ReleaseProtocol preferred = Mangette.Settings.ComicProtocolPreference == ComicProtocolPreference.Usenet
                ? ReleaseProtocol.Usenet
                : ReleaseProtocol.Torrent;
            release = ComicAcquisition.PickBestForIssue(releases, chapter, preferred);
            if (release is null)
                return TypedResults.BadRequest($"No Prowlarr release matches issue {chapter.ChapterNumber} of \"{chapter.ParentManga.Name}\".");
        }

        (ComicDownloadJob? job, string? error) = await ComicAcquisition.Grab(chapter, release, HttpContext.RequestAborted);
        if (job is null)
        {
            Log.Error(error);
            return TypedResults.BadRequest(error ?? "Could not start the download.");
        }

        context.ComicDownloadJobs.Add(job);
        if (await context.Sync(HttpContext.RequestAborted, GetType(), "Interactive comic grab") is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        Log.InfoFormat("Interactively grabbed \"{0}\" from {1} ({2}) for {3} #{4}.",
            release.Title, release.IndexerName, job.ClientName, chapter.ParentManga.Name, chapter.ChapterNumber);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Every active ComicDownloadJob (queued/downloading/importing), plus the most recent finished
    /// ones, Sonarr Activity/Queue-style. Optionally scoped to one series so the same endpoint backs
    /// both the library-wide Queue tab and a series' own "Active downloads" panel.
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("Queue")]
    [ProducesResponseType<List<ComicQueueEntry>>(Status200OK, "application/json")]
    public async Task<Ok<List<ComicQueueEntry>>> Queue([FromQuery] string? mangaId = null)
    {
        const int recentFinishedLimit = 20;

        IQueryable<ComicDownloadJob> jobs = context.ComicDownloadJobs
            .Include(j => j.Chapter)
            .ThenInclude(c => c.ParentManga);
        if (!string.IsNullOrWhiteSpace(mangaId))
            jobs = jobs.Where(j => j.Chapter.ParentMangaId == mangaId);

        List<ComicDownloadJob> active = await jobs
            .Where(j => j.Status != ComicDownloadJobStatus.Imported)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(HttpContext.RequestAborted);
        List<ComicDownloadJob> recentFinished = await jobs
            .Where(j => j.Status == ComicDownloadJobStatus.Imported)
            .OrderByDescending(j => j.LastCheckedAt)
            .Take(recentFinishedLimit)
            .ToListAsync(HttpContext.RequestAborted);

        List<ComicQueueEntry> entries = active.Concat(recentFinished)
            .Select(j => new ComicQueueEntry(
                j.Key, j.ChapterId, j.Chapter.ParentMangaId, j.Chapter.ParentManga.Name, j.Chapter.ChapterNumber,
                j.ReleaseTitle, j.IndexerName, j.Protocol, j.ClientName, j.Status, j.Progress, j.ErrorMessage, j.CreatedAt))
            .ToList();
        return TypedResults.Ok(entries);
    }

    /// <summary>
    /// Removes a job from the queue. This does not reach into qBittorrent/SABnzbd to cancel or
    /// delete the download itself -- it just un-blocks the issue so the next automatic sweep or a
    /// manual Interactive Search can try again.
    /// </summary>
    /// <response code="200"></response>
    /// <response code="404">Unknown jobId</response>
    [HttpDelete("Queue/{jobId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok, NotFound<string>>> RemoveFromQueue(string jobId)
    {
        if (await context.ComicDownloadJobs.Where(j => j.Key == jobId).ExecuteDeleteAsync(HttpContext.RequestAborted) < 1)
            return TypedResults.NotFound(nameof(jobId));
        return TypedResults.Ok();
    }

    /// <summary>Release is optional -- omit it (or send null) for "auto-search and grab the best match".</summary>
    public sealed record GrabComicReleaseRequest(IndexerRelease? Release);
}

public sealed record ComicQueueEntry(
    string JobId, string ChapterId, string MangaId, string MangaName, string ChapterNumber,
    string ReleaseTitle, string IndexerName, ReleaseProtocol Protocol, DownloadClientKind ClientName,
    ComicDownloadJobStatus Status, double Progress, string? ErrorMessage, DateTime CreatedAt);
