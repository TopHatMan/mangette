using API.Controllers.DTOs;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.MangaContext;
using API.Workers;
using API.Workers.MangaDownloadWorkers;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Soenneker.Utils.String.NeedlemanWunsch;
using static Microsoft.AspNetCore.Http.StatusCodes;
using AltTitle = API.Controllers.DTOs.AltTitle;
using Author = API.Controllers.DTOs.Author;
using Chapter = API.Schema.MangaContext.Chapter;
using Link = API.Controllers.DTOs.Link;
using Manga = API.Controllers.DTOs.Manga;

// ReSharper disable InconsistentNaming

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class MangaController(MangaContext context, ActionsContext actionsContext) : ControllerBase
{
    
    /// <summary>
    /// Returns series in the library. Search results are not included until they are added.
    /// Unmonitored series with no downloaded chapters (leftover from the old search-adds-everything behavior) are omitted.
    /// </summary>
    /// <response code="200"><see cref="LibrarySeries"/> rows for the dashboard</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet]
    [ProducesResponseType<List<LibrarySeries>>(Status200OK, "application/json")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<LibrarySeries>>, InternalServerError>> GetAllManga ()
    {
        try
        {
            return TypedResults.Ok(await LoadLibrarySeries(context, HttpContext.RequestAborted));
        }
        catch
        {
            return TypedResults.InternalServerError();
        }
    }

    /// <summary>
    /// Library dashboard rows. Chapter totals are SQL counts — the old Include(Chapters) loaded every page of every series.
    /// </summary>
    internal static async Task<List<LibrarySeries>> LoadLibrarySeries(MangaContext context, CancellationToken ct)
    {
        var rows = await context.Mangas
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(m => m.MangaConnectorIds.Any(id => id.UseForDownload) || m.Chapters.Any(c => c.Downloaded))
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                m.Key,
                m.Name,
                m.Description,
                m.ReleaseStatus,
                m.Year,
                Ids = m.MangaConnectorIds.Select(id => new
                {
                    id.Key,
                    id.MangaConnectorName,
                    id.ObjId,
                    id.WebsiteUrl,
                    id.UseForDownload
                }).ToList(),
                ChapterCount = m.Chapters.Count(),
                DownloadedCount = m.Chapters.Count(c => c.Downloaded)
            })
            .ToListAsync(ct);

        return rows.Select(m => new LibrarySeries(
            m.Key,
            m.Name,
            m.Description,
            m.ReleaseStatus,
            m.Ids.Select(id => new DTOs.MangaConnectorId<Manga>(
                id.Key, id.MangaConnectorName, id.ObjId, id.WebsiteUrl, id.UseForDownload)),
            m.Year,
            m.Ids.Any(id => id.UseForDownload),
            m.ChapterCount,
            m.DownloadedCount)).ToList();
    }
    
    /// <summary>
    /// Returns all <see cref="Schema.MangaContext.Manga"/> that are being downloaded from at least one <see cref="API.MangaConnectors.MangaConnector"/>
    /// </summary>
    /// <response code="200"><see cref="MinimalManga"/> exert of <see cref="Schema.MangaContext.Manga"/>. Use <see cref="GetManga"/> for more information</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet("Downloading")]
    [ProducesResponseType<MinimalManga[]>(Status200OK, "application/json")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<MinimalManga>>, InternalServerError>> GetMangaDownloading()
    {
        if (await context.Mangas
                .Include(m => m.MangaConnectorIds)
                .Where(m => m.MangaConnectorIds.Any(id => id.UseForDownload))
                .OrderBy(m => m.Name)
                .ToArrayAsync(HttpContext.RequestAborted) is not { } result)
            return TypedResults.InternalServerError();

        return TypedResults.Ok(result.Select(m =>
        {
            IEnumerable<DTOs.MangaConnectorId<Manga>> ids = m.MangaConnectorIds.Select(id => new DTOs.MangaConnectorId<Manga>(id.Key, id.MangaConnectorName, id.ObjId, id.WebsiteUrl, id.UseForDownload));
            return new MinimalManga(m.Key, m.Name, m.Description, m.ReleaseStatus, ids);
        }).ToList());
    }

    /// <summary>
    /// Return <see cref="Schema.MangaContext.Manga"/> with <paramref name="MangaId"/>
    /// </summary>
    /// <param name="MangaId"><see cref="Schema.MangaContext.Manga"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Manga"/> with <paramref name="MangaId"/> not found</response>
    [HttpGet("{MangaId}")]
    [ProducesResponseType<Manga>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<Manga>, NotFound<string>>> GetManga (string MangaId)
    {
        if (await context.MangaWithMetadata().Include(m => m.MangaConnectorIds).FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));
        
        IEnumerable<DTOs.MangaConnectorId<Manga>> ids = manga.MangaConnectorIds.Select(id => new DTOs.MangaConnectorId<Manga>(id.Key, id.MangaConnectorName, id.ObjId, id.WebsiteUrl, id.UseForDownload));
        IEnumerable<Author> authors = manga.Authors.Select(a => new Author(a.Key, a.AuthorName));
        IEnumerable<string> tags = manga.MangaTags.Select(t => t.Tag);
        IEnumerable<Link> links = manga.Links.Select(l => new Link(l.Key, l.LinkProvider, l.LinkUrl));
        IEnumerable<AltTitle> altTitles = manga.AltTitles.Select(a => new AltTitle(a.Language, a.Title));
        Manga result = new (manga.Key, manga.Name, manga.Description, manga.ReleaseStatus, ids, manga.IgnoreChaptersBefore, manga.Year, manga.OriginalLanguage, authors, tags, links, altTitles, manga.LibraryId);
        
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Delete <see cref="Manga"/> with <paramref name="MangaId"/>
    /// </summary>
    /// <param name="MangaId"><see cref="Manga"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Manga"/> with <paramref name="MangaId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpDelete("{MangaId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, NotFound<string>, InternalServerError<string>>> DeleteManga (string MangaId)
    {
        if(await context.Mangas.FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));
        context.Remove(manga);
        
        if(await context.Sync(HttpContext.RequestAborted, GetType(), System.Reflection.MethodBase.GetCurrentMethod()?.Name) is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);
        return TypedResults.Ok();
    }


    /// <summary>
    /// Merge two <see cref="Manga"/> into one. THIS IS NOT REVERSIBLE!
    /// </summary>
    /// <param name="MangaIdFrom"><see cref="Manga"/>.Key of <see cref="Manga"/> merging data from (getting deleted)</param>
    /// <param name="MangaIdInto"><see cref="Manga"/>.Key of <see cref="Manga"/> merging data into</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Manga"/> with <paramref name="MangaIdFrom"/> or <paramref name="MangaIdInto"/> not found</response>
    [HttpPost("{MangaIdFrom}/MergeInto/{MangaIdInto}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok, NotFound<string>>> MergeIntoManga (string MangaIdFrom, string MangaIdInto)
    {
        if (await context.MangaIncludeAll().FirstOrDefaultAsync(m => m.Key == MangaIdFrom, HttpContext.RequestAborted) is not { } from)
            return TypedResults.NotFound(nameof(MangaIdFrom));
        if (await context.MangaIncludeAll().FirstOrDefaultAsync(m => m.Key == MangaIdInto, HttpContext.RequestAborted) is not { } into)
            return TypedResults.NotFound(nameof(MangaIdInto));
        
        BaseWorker[] newJobs = into.MergeFrom(from, context);
        Mangette.AddWorkers(newJobs);
        
        return TypedResults.Ok();
    }

    /// <summary>
    /// Returns Cover of <see cref="Manga"/> with <paramref name="MangaId"/>
    /// </summary>
    /// <param name="MangaId"><see cref="Manga"/>.Key</param>
    /// <param name="CoverSize">Size of the cover returned
    /// <br /> - <see cref="CoverSize.Small"/> <see cref="Constants.ImageSmSize"/>
    /// <br /> - <see cref="CoverSize.Medium"/> <see cref="Constants.ImageMdSize"/>
    /// <br /> - <see cref="CoverSize.Large"/> <see cref="Constants.ImageLgSize"/>
    /// </param>
    /// <response code="200">JPEG Image</response>
    /// <response code="204">Cover not loaded</response>
    /// <response code="404"><see cref="Manga"/> with <paramref name="MangaId"/> not found</response>
    /// <response code="503">Retry later, downloading cover</response>
    [HttpGet("{MangaId}/Cover/{CoverSize?}")]
    [ProducesResponseType<FileContentResult>(Status200OK,"image/jpeg")]
    [ProducesResponseType(Status204NoContent)]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status503ServiceUnavailable)]
    public async Task<Results<FileContentHttpResult, NoContent, BadRequest, NotFound<string>, StatusCodeHttpResult>> GetCover (string MangaId, CoverSize? CoverSize = null)
    {
        if (await context.Mangas.FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));

        string cache = CoverSize switch
        {
            MangaController.CoverSize.Small => MangetteSettings.CoverImageCacheSmall,
            MangaController.CoverSize.Medium => MangetteSettings.CoverImageCacheMedium,
            MangaController.CoverSize.Large => MangetteSettings.CoverImageCacheLarge,
            _ => MangetteSettings.CoverImageCacheOriginal
        };

        if (await manga.GetCoverImage(cache, HttpContext.RequestAborted) is not { } data)
        {
            return TypedResults.NotFound("Image not in cache");
        }
        
        DateTime lastModified = data.fileInfo.LastWriteTime;
        EntityTagHeaderValue entityTagHeaderValue = EntityTagHeaderValue.Parse($"\"{lastModified.Ticks}\"");
        if(HttpContext.Request.Headers.ETag.Equals(entityTagHeaderValue.Tag.Value))
            return TypedResults.StatusCode(Status304NotModified);
        HttpContext.Response.Headers.CacheControl = "public";
        return TypedResults.Bytes(data.stream.ToArray(), "image/jpeg", lastModified: new DateTimeOffset(lastModified), entityTag: entityTagHeaderValue);
    }
    public enum CoverSize { Original, Large, Medium, Small }

    /// <summary>
    /// Move <see cref="Manga"/> to different <see cref="DTOs.FileLibrary"/>
    /// </summary>
    /// <param name="MangaId"><see cref="Manga"/>.Key</param>
    /// <param name="LibraryId"><see cref="DTOs.FileLibrary"/>.Key</param>
    /// <response code="202">Folder is going to be moved</response>
    /// <response code="404"><paramref name="MangaId"/> or <paramref name="LibraryId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPost("{MangaId}/ChangeLibrary/{LibraryId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError,  "text/plain")]
    public async Task<Results<Ok, NotFound<string>, InternalServerError<string>>> ChangeLibrary(string MangaId, string LibraryId)
    {
        if (await context.Mangas
                .Include(m => m.Library)
                .Include(m => m.AltTitles)
                .Include(m => m.Chapters)
                .FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));
        if (await context.FileLibraries.FirstOrDefaultAsync(l => l.Key == LibraryId, HttpContext.RequestAborted) is not { } library)
            return TypedResults.NotFound(nameof(LibraryId));

        bool moved = manga.LibraryId != library.Key;
        if (moved)
        {
            Dictionary<Chapter, string?> oldPaths = manga.Chapters.Where(ch => ch.Downloaded).ToDictionary(ch => ch, ch => ch.FullArchiveFilePath);
            await context.BindMangaLibrary(manga, library, HttpContext.RequestAborted);
            Dictionary<Chapter, string?> newPaths = oldPaths.ToDictionary(kv => kv.Key, kv => kv.Key.FullArchiveFilePath);
            IEnumerable<MoveFileOrFolderWorker> workers = oldPaths.Select(kv => new MoveFileOrFolderWorker(newPaths[kv.Key]!, kv.Value!));
            Mangette.AddWorkers(workers);
        }

        manga.TryAttachExistingSeriesFolder();
        foreach (Chapter chapter in manga.Chapters)
        {
            chapter.ParentManga = manga;
            chapter.ApplyDownloadedMatch();
        }
        
        if(await context.Sync(HttpContext.RequestAborted, GetType(), "Move Manga") is { success: false } mangaContextResult)
            return TypedResults.InternalServerError(mangaContextResult.exceptionMessage);

        if (moved)
        {
            actionsContext.Actions.Add(new LibraryMovedActionRecord(manga, library));
            if(await actionsContext.Sync(HttpContext.RequestAborted, GetType(), "Move Manga") is { success: false } actionsContextResult)
                return TypedResults.InternalServerError(actionsContextResult.exceptionMessage);
        }
        
        return TypedResults.Ok();
    }

    /// <summary>
    /// (Un-)Marks <see cref="Manga"/> as requested for Download from <see cref="API.MangaConnectors.MangaConnector"/>
    /// </summary>
    /// <param name="MangaId"><see cref="Manga"/> with <paramref name="MangaId"/></param>
    /// <param name="MangaConnectorName"><see cref="API.MangaConnectors.MangaConnector"/> with <paramref name="MangaConnectorName"/></param>
    /// <param name="IsRequested">true to mark as requested, false to mark as not-requested</param>
    /// <response code="200"></response>
    /// <response code="404"><paramref name="MangaId"/> or <paramref name="MangaConnectorName"/> not found</response>
    /// <response code="412"><see cref="Manga"/> was not linked to <see cref="API.MangaConnectors.MangaConnector"/>, so nothing changed</response>
    /// <response code="428"><see cref="Manga"/> is not linked to <see cref="API.MangaConnectors.MangaConnector"/> yet. Search for <see cref="Manga"/> on <see cref="API.MangaConnectors.MangaConnector"/> first (to create a <see cref="DTOs.MangaConnectorId{T}"/>).</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPatch("{MangaId}/DownloadFrom/{MangaConnectorName}/{IsRequested}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound,  "text/plain")]
    [ProducesResponseType<string>(Status412PreconditionFailed,  "text/plain")]
    [ProducesResponseType<string>(Status428PreconditionRequired,  "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError,  "text/plain")]
    public async Task<Results<Ok, NotFound<string>, StatusCodeHttpResult, InternalServerError<string>>> MarkAsRequested(string MangaId, string MangaConnectorName, bool IsRequested)
    {
        if (await context.Mangas
                .Include(m => m.Chapters)
                .ThenInclude(c => c.MangaConnectorIds.Where(chID => chID.MangaConnectorName == MangaConnectorName))
                .Include(m => m.MangaConnectorIds.Where(mId => mId.MangaConnectorName == MangaConnectorName))
                .FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));
        if(!Mangette.TryGetMangaConnector(MangaConnectorName, out API.MangaConnectors.MangaConnector? _))
            return TypedResults.NotFound(nameof(MangaConnectorName));

        await context.AssignDefaultLibraryIfMissing(manga, HttpContext.RequestAborted);

        if (manga.MangaConnectorIds.FirstOrDefault(mId => mId.MangaConnectorName == MangaConnectorName) is not { } mcId)
        {
            if(IsRequested)
                return TypedResults.StatusCode(Status428PreconditionRequired);
            else
                return TypedResults.StatusCode(Status412PreconditionFailed);
        }
        else
        {
            mcId.UseForDownload = IsRequested;
        }

        if (manga.Chapters.SelectMany(ch =>
                ch.MangaConnectorIds.Where(chID => chID.MangaConnectorName == MangaConnectorName)) is { } chIds)
        {
            foreach (Schema.MangaContext.MangaConnectorId<Chapter> chId in chIds)
            {
                chId.UseForDownload = IsRequested;
            }
        }

        if(await context.Sync(HttpContext.RequestAborted, GetType(), "Update download from MangaConnector.") is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        DownloadCoverFromMangaconnectorWorker downloadCover = new(mcId);
        RetrieveMangaChaptersFromMangaconnectorWorker retrieveChapters = new(mcId, Mangette.Settings.DownloadLanguage);
        Mangette.AddWorkers([downloadCover, retrieveChapters]);
        
        return TypedResults.Ok();
    }

    public sealed record AttachSourceRequest(string IdOnSite);

    /// <summary>
    /// Attach another site to this series and use it for downloads. Only sites you turn on here are searched.
    /// </summary>
    [HttpPost("{MangaId}/Sources/{MangaConnectorName}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, BadRequest<string>, NotFound<string>, InternalServerError<string>>> AttachSource(
        string MangaId,
        string MangaConnectorName,
        [FromBody] AttachSourceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdOnSite))
            return TypedResults.BadRequest("IdOnSite is required.");
        if (!Mangette.TryGetMangaConnector(MangaConnectorName, out API.MangaConnectors.MangaConnector? connector) ||
            !connector.Enabled ||
            connector.Name.Equals("Global", StringComparison.OrdinalIgnoreCase))
            return TypedResults.NotFound($"Site {MangaConnectorName} is not available.");

        if (await context.Mangas
                .Include(m => m.MangaConnectorIds)
                .Include(m => m.Library)
                .Include(m => m.Chapters)
                .ThenInclude(c => c.MangaConnectorIds.Where(id => id.MangaConnectorName == MangaConnectorName))
                .FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));

        await context.AssignDefaultLibraryIfMissing(manga, HttpContext.RequestAborted);

        Schema.MangaContext.MangaConnectorId<Schema.MangaContext.Manga>? mcId =
            manga.MangaConnectorIds.FirstOrDefault(id =>
                id.MangaConnectorName.Equals(connector.Name, StringComparison.OrdinalIgnoreCase));
        if (mcId is null)
        {
            (Schema.MangaContext.Manga _, Schema.MangaContext.MangaConnectorId<Schema.MangaContext.Manga> fetchedId)? fetched;
            try
            {
                fetched = connector.GetMangaFromId(request.IdOnSite);
            }
            catch (Exception ex)
            {
                return TypedResults.InternalServerError($"Could not load that series from {connector.Name}: {ex.Message}");
            }
            if (fetched is null)
                return TypedResults.NotFound($"Could not load that series from {connector.Name}.");

            mcId = new Schema.MangaContext.MangaConnectorId<Schema.MangaContext.Manga>(
                manga, connector, request.IdOnSite, fetched.Value.Item2.WebsiteUrl, useForDownload: true);
            manga.MangaConnectorIds.Add(mcId);
        }
        else
            mcId.UseForDownload = true;

        foreach (Schema.MangaContext.MangaConnectorId<Chapter> chId in manga.Chapters.SelectMany(ch =>
                     ch.MangaConnectorIds.Where(id => id.MangaConnectorName.Equals(connector.Name, StringComparison.OrdinalIgnoreCase))))
            chId.UseForDownload = true;

        if (await context.Sync(HttpContext.RequestAborted, GetType(), "Attach source") is { success: false } sync)
            return TypedResults.InternalServerError(sync.exceptionMessage);

        Mangette.AddWorkers(
        [
            new DownloadCoverFromMangaconnectorWorker(mcId),
            new RetrieveMangaChaptersFromMangaconnectorWorker(mcId, Mangette.Settings.DownloadLanguage)
        ]);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Initiate a search for <see cref="API.Schema.MangaContext.Manga"/> on a different <see cref="API.MangaConnectors.MangaConnector"/>
    /// </summary>
    /// <param name="MangaId"><see cref="API.Schema.MangaContext.Manga"/> with <paramref name="MangaId"/></param>
    /// <param name="MangaConnectorName"><see cref="API.MangaConnectors.MangaConnector"/>.Name</param>
    /// <param name="query">Title to search. Defaults to the series name already stored in the library.</param>
    /// <response code="200"><see cref="MinimalManga"/> exert of <see cref="Schema.MangaContext.Manga"/></response>
    /// <response code="404"><see cref="API.MangaConnectors.MangaConnector"/> with Name not found</response>
    /// <response code="412"><see cref="API.MangaConnectors.MangaConnector"/> with Name is disabled</response>
    [HttpGet("{MangaId}/OnMangaConnector/{MangaConnectorName}")]
    [ProducesResponseType<List<SearchHit>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status406NotAcceptable)]
    public async Task<Results<Ok<List<SearchHit>>, NotFound<string>, StatusCodeHttpResult>> SearchOnDifferentConnector (
        string MangaId,
        string MangaConnectorName,
        [FromQuery] string? query = null)
    {
        if (await context.Mangas.FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));

        if (!Mangette.TryGetMangaConnector(MangaConnectorName, out API.MangaConnectors.MangaConnector? connector) ||
            connector.Name.Equals("Global", StringComparison.OrdinalIgnoreCase))
            return TypedResults.NotFound(nameof(MangaConnectorName));
        if (!connector.Enabled)
            return TypedResults.StatusCode(Status412PreconditionFailed);

        string title = string.IsNullOrWhiteSpace(query) ? manga.Name : query.Trim();
        List<SeriesSearch.ExistingSeries> existing = await SeriesSearch.LoadExisting(context, HttpContext.RequestAborted);
        return TypedResults.Ok(SeriesSearch.Lookup(title, connector.Name, existing));
    }
    
    /// <summary>
    /// Returns all <see cref="Manga"/> which where Authored by <see cref="Author"/> with <paramref name="AuthorId"/>
    /// </summary>
    /// <param name="AuthorId"><see cref="Author"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Author"/> with <paramref name="AuthorId"/></response>
    /// /// <response code="500">Error during Database Operation</response>
    [HttpGet("WithAuthorId/{AuthorId}")]
    [ProducesResponseType<List<Manga>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<List<Manga>>, NotFound<string>, InternalServerError>> GetMangaWithAuthorIds (string AuthorId)
    {
        if (await context.Authors.FirstOrDefaultAsync(a => a.Key == AuthorId, HttpContext.RequestAborted) is not { } _)
            return TypedResults.NotFound(nameof(AuthorId));

        if (await context.MangaWithMetadata().Include(m => m.MangaConnectorIds)
                .Where(m => m.Authors.Any(a => a.Key == AuthorId))
                .OrderBy(m => m.Name)
                .ToListAsync(HttpContext.RequestAborted) is not { } result)
            return TypedResults.InternalServerError();

        return TypedResults.Ok(result.Select(m =>
        {
            IEnumerable<DTOs.MangaConnectorId<Manga>> ids = m.MangaConnectorIds.Select(id => new DTOs.MangaConnectorId<Manga>(id.Key, id.MangaConnectorName, id.ObjId, id.WebsiteUrl, id.UseForDownload));
            IEnumerable<Author> authors = m.Authors.Select(a => new Author(a.Key, a.AuthorName));
            IEnumerable<string> tags = m.MangaTags.Select(t => t.Tag);
            IEnumerable<Link> links = m.Links.Select(l => new Link(l.Key, l.LinkProvider, l.LinkUrl));
            IEnumerable<AltTitle> altTitles = m.AltTitles.Select(a => new AltTitle(a.Language, a.Title));
            return new Manga(m.Key, m.Name, m.Description, m.ReleaseStatus, ids, m.IgnoreChaptersBefore, m.Year, m.OriginalLanguage, authors, tags, links, altTitles, m.LibraryId);
        }).ToList());
    }
    
    /// <summary>
    /// Returns all <see cref="Manga"/> with <see cref="Tag"/>
    /// </summary>
    /// <param name="Tag"><see cref="Tag"/>.Tag</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Tag"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet("WithTag/{Tag}")]
    [ProducesResponseType<Manga[]>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<MinimalManga>>, NotFound<string>, InternalServerError>> GetMangasWithTag (string Tag)
    {
        if (await context.Mangas
                .Include(m => m.MangaConnectorIds)
                .Include(m => m.MangaTags)
                .Where(m => m.MangaTags.Any(t => t.Tag == Tag))
                .OrderBy(m => m.Name)
                .ToListAsync(HttpContext.RequestAborted) is not { } result)
            return TypedResults.InternalServerError();
        
        return TypedResults.Ok(result.Select(m =>
        {
            IEnumerable<DTOs.MangaConnectorId<Manga>> ids = m.MangaConnectorIds.Select(id => new DTOs.MangaConnectorId<Manga>(id.Key, id.MangaConnectorName, id.ObjId, id.WebsiteUrl, id.UseForDownload));
            return new MinimalManga(m.Key, m.Name, m.Description, m.ReleaseStatus, ids);
        }).ToList());
    }

    /// <summary>
    /// Returns <see cref="Schema.MangaContext.Manga"/> with names similar to <see cref="Schema.MangaContext.Manga"/> (identified by <paramref name="MangaId"/>)
    /// </summary>
    /// <param name="MangaId">Key of <see cref="Schema.MangaContext.Manga"/></param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Schema.MangaContext.Manga"/> with <paramref name="MangaId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet("WithSimilarName/{MangaId}")]
    [ProducesResponseType<List<string>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<string>>, NotFound<string>, InternalServerError>> GetSimilarManga (string MangaId)
    {
        if (await context.Mangas.FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));
        
        string name = manga.Name;

        if (await context.Mangas.Where(m => m.Key != MangaId)
                .ToDictionaryAsync(m => m.Key, m => m.Name, HttpContext.RequestAborted) is not { } mangaNames)
            return TypedResults.InternalServerError();

        List<string> similarIds = mangaNames
            .Where(kv => NeedlemanWunschStringUtil.CalculateSimilarityPercentage(name, kv.Value) > 0.8)
            .Select(kv => kv.Key)
            .ToList();
        
        return TypedResults.Ok(similarIds);
    }

    /// <summary>
    /// Returns the <see cref="DTOs.MangaConnectorId{T}"/> with <see cref="DTOs.MangaConnectorId{T}"/>.Key
    /// </summary>
    /// <param name="MangaConnectorIdId">Key of <see cref="DTOs.MangaConnectorId{T}"/></param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="DTOs.MangaConnectorId{T}"/> with <paramref name="MangaConnectorIdId"/> not found</response>
    [HttpGet("ConnectorId/{MangaConnectorIdId}")]
    [ProducesResponseType<DTOs.MangaConnectorId<Manga>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<DTOs.MangaConnectorId<Manga>>, NotFound<string>>> GetMangaMangaConnectorId (string MangaConnectorIdId)
    {
        if (await context.MangaConnectorToManga.FirstOrDefaultAsync(c => c.Key == MangaConnectorIdId, HttpContext.RequestAborted) is not { } mcIdManga)
            return TypedResults.NotFound(nameof(MangaConnectorIdId));

        DTOs.MangaConnectorId<Manga> result = new (mcIdManga.Key, mcIdManga.MangaConnectorName, mcIdManga.ObjId, mcIdManga.WebsiteUrl, mcIdManga.UseForDownload);
        
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Force re-check failed/undownloaded <see cref="Chapter"/> for <see cref="Manga"/>
    /// </summary>
    /// <param name="mangaId">(optional)<see cref="Manga"/>.Key</param>
    /// <response code="200">Affected Records</response>
    [HttpPost("ForceRecheck")]
    [HttpPost("ForceRecheck/{mangaId?}")]
    [ProducesResponseType<int>(Status200OK, "text/plain")]
    public async Task<Ok<int>> ForceRecheckMangaChapters(string? mangaId = null)
    {
        IQueryable<Schema.MangaContext.MangaConnectorId<Chapter>> queryable = context.MangaConnectorToChapter.Where(chId  => chId.Obj!.Downloaded);
        if(mangaId is not null)
            queryable = queryable.Where(chId => chId.Obj!.ParentMangaId == mangaId);
        
        int rowsAffected = await queryable.ExecuteDeleteAsync(HttpContext.RequestAborted);

        return TypedResults.Ok(rowsAffected);
    }

    /// <summary>
    /// Force re-check a specific <see cref="Chapter"/> by deleting its record.
    /// </summary>
    /// <param name="chapterId"><see cref="Chapter"/>.Key</param>
    /// <response code="200">Affected records</response>
    [HttpPost("ForceRecheck/Chapter/{chapterId}")]
    [ProducesResponseType<int>(Status200OK, "text/plain")]
    public async Task<Ok<int>> ForceRecheckChapter(string chapterId)
    {
        IQueryable<Schema.MangaContext.MangaConnectorId<Chapter>> queryable = context.MangaConnectorToChapter.Where(chId  => chId.ObjId == chapterId);
        
        int rowsAffected = await queryable.ExecuteDeleteAsync(HttpContext.RequestAborted);

        return TypedResults.Ok(rowsAffected);
    }
}