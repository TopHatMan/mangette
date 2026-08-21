using API.Controllers.DTOs;
using API.Controllers.Requests;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using API.Workers;
using API.Workers.MangaDownloadWorkers;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;
using FileLibrary = API.Schema.MangaContext.FileLibrary;
using Manga = API.Schema.MangaContext.Manga;
using MangaConnector = API.MangaConnectors.MangaConnector;
using SchemaMangaId = API.Schema.MangaContext.MangaConnectorId<API.Schema.MangaContext.Manga>;

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class SearchController(MangaContext context) : ControllerBase
{
    /// <summary>
    /// Search sites without adding anything to the library. Same idea as Sonarr/Radarr Add New.
    /// </summary>
    [HttpGet("Lookup")]
    [ProducesResponseType<List<SearchHit>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<List<SearchHit>>, BadRequest<string>>> Lookup(
        [FromQuery] string query,
        [FromQuery] string? connectorName = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return TypedResults.BadRequest("Query is required.");

        string trimmed = query.Trim().Trim('"', '\'');
        List<SeriesSearch.ExistingSeries> existing = await SeriesSearch.LoadExisting(context, HttpContext.RequestAborted);

        if (IsUrl(trimmed))
        {
            SearchHit? fromUrl = SeriesSearch.FromUrl(trimmed, existing);
            return TypedResults.Ok(fromUrl is null ? new List<SearchHit>() : [fromUrl]);
        }

        return TypedResults.Ok(SeriesSearch.Lookup(trimmed, connectorName, existing));
    }

    /// <summary>
    /// Add the chosen search result to the library. Only this series is stored, not the rest of the lookup.
    /// </summary>
    [HttpPost("Add")]
    [ProducesResponseType<LibrarySeries>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok<LibrarySeries>, BadRequest<string>, NotFound<string>, InternalServerError<string>>> Add(
        [FromBody] AddSeriesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectorName) || string.IsNullOrWhiteSpace(request.IdOnSite))
            return TypedResults.BadRequest("ConnectorName and IdOnSite are required.");

        if (!Mangette.TryGetMangaConnector(request.ConnectorName, out MangaConnector? connector) || !connector.Enabled)
            return TypedResults.NotFound(nameof(request.ConnectorName));

        (Manga manga, SchemaMangaId mcId)? fetched = connector.GetMangaFromId(request.IdOnSite);
        if (fetched is null)
            return TypedResults.NotFound("Could not load that series from the site.");

        (Manga manga, SchemaMangaId id)? added = await context.AddMangaToContext(fetched.Value, HttpContext.RequestAborted);
        if (added is null)
            return TypedResults.InternalServerError("Could not save the series.");

        Manga manga = await context.Mangas
            .Include(m => m.Library)
            .Include(m => m.MangaConnectorIds)
            .Include(m => m.Chapters)
            .FirstAsync(m => m.Key == added.Value.manga.Key, HttpContext.RequestAborted);

        FileLibrary? library = null;
        if (!string.IsNullOrWhiteSpace(request.LibraryId))
            library = await context.FileLibraries.FirstOrDefaultAsync(l => l.Key == request.LibraryId, HttpContext.RequestAborted);
        library ??= await context.FileLibraries.OrderBy(l => l.LibraryName).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (library is not null)
            manga.Library = library;

        SchemaMangaId link = manga.MangaConnectorIds.FirstOrDefault(x =>
            x.MangaConnectorName.Equals(request.ConnectorName, StringComparison.OrdinalIgnoreCase))
            ?? added.Value.id;
        link.UseForDownload = request.Monitor;

        if (await context.Sync(HttpContext.RequestAborted, GetType(), "Add series") is { success: false } sync)
            return TypedResults.InternalServerError(sync.exceptionMessage);

        DownloadCoverFromMangaconnectorWorker cover = new(link);
        List<BaseWorker> jobs = [cover];
        if (request.Monitor)
            jobs.Add(new RetrieveMangaChaptersFromMangaconnectorWorker(link, Mangette.Settings.DownloadLanguage));
        Mangette.AddWorkers(jobs);

        return TypedResults.Ok(ToLibrarySeries(manga));
    }

    /// <summary>Proxy a remote cover so Add New posters can render without hotlink blocks.</summary>
    [HttpGet("Cover")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType(Status404NotFound)]
    public async Task<Results<FileContentHttpResult, BadRequest, NotFound>> Cover([FromQuery] string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return TypedResults.BadRequest();

        HttpDownloadClient client = new(false);
        HttpResponseMessage response;
        try
        {
            response = await client.MakeRequest(url, RequestType.MangaCover, $"{uri.Scheme}://{uri.Host}/", HttpContext.RequestAborted);
        }
        catch
        {
            return TypedResults.NotFound();
        }

        if (!response.IsSuccessStatusCode)
            return TypedResults.NotFound();

        byte[] bytes = await response.Content.ReadAsByteArrayAsync(HttpContext.RequestAborted);
        if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024)
            return TypedResults.NotFound();

        string media = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        if (!media.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            media = "image/jpeg";
        return TypedResults.File(bytes, media);
    }

    /// <summary>
    /// Search one site. Does not add results to the library.
    /// </summary>
    [HttpGet("{MangaConnectorName}/{Query}")]
    [ProducesResponseType<List<SearchHit>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status406NotAcceptable)]
    public async Task<Results<Ok<List<SearchHit>>, NotFound<string>, StatusCodeHttpResult>> SearchManga(string MangaConnectorName, string Query)
    {
        if (Mangette.MangaConnectors.FirstOrDefault(c =>
                c.Name.Equals(MangaConnectorName, StringComparison.InvariantCultureIgnoreCase)) is not { } connector)
            return TypedResults.NotFound(nameof(MangaConnectorName));
        if (!connector.Enabled)
            return TypedResults.StatusCode(Status412PreconditionFailed);

        List<SeriesSearch.ExistingSeries> existing = await SeriesSearch.LoadExisting(context, HttpContext.RequestAborted);
        return TypedResults.Ok(SeriesSearch.Lookup(Query, connector.Name, existing));
    }

    /// <summary>
    /// Resolve a series URL. Does not add it to the library.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<SearchHit>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<List<SearchHit>>, BadRequest<string>>> GetMangaFromUrl([FromQuery] string? url = null, [FromQuery] string? query = null)
    {
        string? input = url ?? query;
        if (string.IsNullOrWhiteSpace(input))
            return TypedResults.BadRequest("url or query is required.");
        return await Lookup(input, null);
    }

    private static bool IsUrl(string input)
    {
        return Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    internal static LibrarySeries ToLibrarySeries(Manga manga)
    {
        IEnumerable<DTOs.MangaConnectorId<DTOs.Manga>> ids = manga.MangaConnectorIds.Select(id =>
            new DTOs.MangaConnectorId<DTOs.Manga>(id.Key, id.MangaConnectorName, id.ObjId, id.WebsiteUrl, id.UseForDownload));
        int chapters = manga.Chapters?.Count ?? 0;
        int downloaded = manga.Chapters?.Count(c => c.Downloaded) ?? 0;
        bool monitored = manga.MangaConnectorIds.Any(id => id.UseForDownload);
        return new LibrarySeries(manga.Key, manga.Name, manga.Description, manga.ReleaseStatus, ids, manga.Year, monitored, chapters, downloaded);
    }
}
