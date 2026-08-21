using API.MangaConnectors;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using API.Workers.MangaDownloadWorkers;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;
using Manga = API.Schema.MangaContext.Manga;

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class LibraryImportController(MangaContext context) : ControllerBase
{
    [HttpGet("Scan")]
    [ProducesResponseType<LibraryScanResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<LibraryScanResult>, BadRequest<string>>> Scan([FromQuery] string? FileLibraryId = null)
    {
        FileLibrary? library = await ResolveLibrary(FileLibraryId);
        if (library is null)
            return TypedResults.BadRequest("No library folder is set. Save a library path in Settings first.");

        string root;
        try
        {
            root = Path.GetFullPath(library.BasePath);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Library path is not valid: {ex.Message}");
        }

        if (!Directory.Exists(root))
            return TypedResults.BadRequest($"Library folder does not exist: {root}");

        List<string> mappedNames = await context.Mangas
            .Where(m => m.LibraryId == library.Key)
            .Select(m => m.DirectoryName)
            .ToListAsync(HttpContext.RequestAborted);
        HashSet<string> mapped = mappedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<LibraryFolderRecord> unmapped = [];
        int mappedCount = 0;
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (LibraryImportMatcher.IsSkippableFolder(name))
                continue;
            if (mapped.Contains(name))
            {
                mappedCount++;
                continue;
            }
            int archives = CountArchives(dir);
            unmapped.Add(new LibraryFolderRecord(name, archives, LibraryImportMatcher.CleanFolderQuery(name)));
        }

        unmapped = unmapped.OrderBy(f => f.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
        return TypedResults.Ok(new LibraryScanResult(library.Key, library.LibraryName, root, unmapped, mappedCount));
    }

    [HttpPost("Match")]
    [ProducesResponseType<LibraryMatchResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public Results<Ok<LibraryMatchResult>, BadRequest<string>> Match([FromBody] LibraryMatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FolderName))
            return TypedResults.BadRequest("FolderName is required.");

        string query = string.IsNullOrWhiteSpace(request.Query)
            ? LibraryImportMatcher.CleanFolderQuery(request.FolderName)
            : request.Query.Trim();
        if (query.Length == 0)
            return TypedResults.BadRequest("Could not build a search query from that folder name.");

        List<LibraryMatchCandidate> candidates = SearchCandidates(request.FolderName, query);
        return TypedResults.Ok(new LibraryMatchResult(request.FolderName, candidates));
    }

    [HttpPost("Import")]
    [ProducesResponseType<LibraryImportResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok<LibraryImportResult>, BadRequest<string>, NotFound<string>, InternalServerError<string>>> Import(
        [FromBody] LibraryImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FolderName) ||
            string.IsNullOrWhiteSpace(request.ConnectorName) ||
            string.IsNullOrWhiteSpace(request.IdOnSite))
            return TypedResults.BadRequest("FolderName, ConnectorName, and IdOnSite are required.");

        FileLibrary? library = await ResolveLibrary(request.LibraryId);
        if (library is null)
            return TypedResults.BadRequest("No library folder is set.");

        if (!Mangette.TryGetMangaConnector(request.ConnectorName, out MangaConnector? connector))
            return TypedResults.NotFound(nameof(request.ConnectorName));

        (Manga manga, MangaConnectorId<Manga> mcId)? fetched = connector.GetMangaFromId(request.IdOnSite);
        if (fetched is null)
            return TypedResults.NotFound("Could not load that series from the site.");

        (Manga manga, MangaConnectorId<Manga> id)? added = await context.AddMangaToContext(fetched.Value, HttpContext.RequestAborted);
        if (added is null)
            return TypedResults.InternalServerError("Could not save the series.");

        Manga manga = await context.Mangas
            .Include(m => m.Library)
            .Include(m => m.MangaConnectorIds)
            .Include(m => m.Chapters)
            .FirstAsync(m => m.Key == added.Value.manga.Key, HttpContext.RequestAborted);

        manga.Library = library;
        manga.SetDirectoryName(request.FolderName);
        if (manga.MangaConnectorIds.FirstOrDefault(x => x.MangaConnectorName == request.ConnectorName) is { } link)
            link.UseForDownload = true;
        else
            added.Value.id.UseForDownload = true;

        if (await context.Sync(HttpContext.RequestAborted, GetType(), "Library import") is { success: false } sync)
            return TypedResults.InternalServerError(sync.exceptionMessage);

        MangaConnectorId<Manga> monitor = manga.MangaConnectorIds.First(x => x.MangaConnectorName == request.ConnectorName);
        RetrieveMangaChaptersFromMangaconnectorWorker retrieve = new(monitor, Mangette.Settings.DownloadLanguage);
        DownloadCoverFromMangaconnectorWorker cover = new(monitor);
        Mangette.AddWorkers([cover, retrieve]);

        string seriesDir = Path.Combine(library.BasePath, request.FolderName);
        int archives = Directory.Exists(seriesDir) ? CountArchives(seriesDir) : 0;
        return TypedResults.Ok(new LibraryImportResult(manga.Key, manga.Name, archives));
    }

    private async Task<FileLibrary?> ResolveLibrary(string? libraryId)
    {
        if (!string.IsNullOrWhiteSpace(libraryId))
            return await context.FileLibraries.FirstOrDefaultAsync(l => l.Key == libraryId, HttpContext.RequestAborted);
        return await context.FileLibraries.OrderBy(l => l.LibraryName).FirstOrDefaultAsync(HttpContext.RequestAborted);
    }

    private static List<LibraryMatchCandidate> SearchCandidates(string folderName, string query)
    {
        List<LibraryMatchCandidate> found = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int connectorsTried = 0;
        foreach (string connectorName in DownloadFailureTracker.GetPreferenceOrder())
        {
            if (!Mangette.TryGetMangaConnector(connectorName, out MangaConnector? connector) || !connector.Enabled)
                continue;
            if (connectorName.Equals("Global", StringComparison.OrdinalIgnoreCase))
                continue;
            if (connectorsTried >= 3 && found.Count > 0)
                break;
            connectorsTried++;
            (Manga manga, MangaConnectorId<Manga> id)[] hits;
            try
            {
                hits = connector.SearchManga(query);
            }
            catch
            {
                continue;
            }

            foreach ((Manga manga, MangaConnectorId<Manga> id) in hits)
            {
                string key = $"{id.MangaConnectorName}:{id.IdOnConnectorSite}";
                if (!seen.Add(key))
                    continue;
                double score = LibraryImportMatcher.ScoreTitle(folderName, manga.Name);
                found.Add(new LibraryMatchCandidate(
                    manga.Name,
                    id.MangaConnectorName,
                    id.IdOnConnectorSite,
                    id.WebsiteUrl,
                    manga.CoverUrl,
                    Math.Round(score, 1)));
            }
        }

        return found.OrderByDescending(c => c.Score).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(8).ToList();
    }

    private static int CountArchives(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Count(path =>
                {
                    string ext = Path.GetExtension(path);
                    return ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".cbr", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".cb7", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch
        {
            return 0;
        }
    }
}

public sealed record LibraryFolderRecord(string FolderName, int ArchiveCount, string SuggestedQuery);
public sealed record LibraryScanResult(string LibraryId, string LibraryName, string BasePath, List<LibraryFolderRecord> UnmappedFolders, int MappedFolderCount);
public sealed record LibraryMatchRequest(string FolderName, string? Query);
public sealed record LibraryMatchCandidate(string Name, string ConnectorName, string IdOnSite, string? WebsiteUrl, string CoverUrl, double Score);
public sealed record LibraryMatchResult(string FolderName, List<LibraryMatchCandidate> Matches);
public sealed record LibraryImportRequest(string LibraryId, string FolderName, string ConnectorName, string IdOnSite);
public sealed record LibraryImportResult(string MangaId, string Name, int ArchivesOnDisk);
