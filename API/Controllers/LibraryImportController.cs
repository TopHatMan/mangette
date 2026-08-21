using API.MangaConnectors;
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
            return TypedResults.BadRequest($"Library folder does not exist: {root}. Set Settings → Library folder to the directory that contains one folder per series.");

        List<Manga> inLibrary = await context.Mangas
            .Include(m => m.MangaConnectorIds)
            .Include(m => m.Chapters)
            .Where(m => m.LibraryId == library.Key)
            .ToListAsync(HttpContext.RequestAborted);
        HashSet<string> mapped = inLibrary
            .Where(m => m.MangaConnectorIds.Any(id => id.UseForDownload) || m.Chapters.Any(c => c.Downloaded))
            .Select(m => NormalizeFolderKey(m.DirectoryName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(root).ToList();
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Cannot read {root}: {ex.Message}. The Windows service cannot see mapped drives (Z:\\). Use D:\\Manga or \\\\server\\share\\Manga.");
        }

        List<LibraryFolderRecord> unmapped = [];
        int mappedCount = 0;
        int seen = 0;
        foreach (string dir in dirs)
        {
            foreach ((string relative, string full) in SeriesFolders(root, dir))
            {
                seen++;
                if (mapped.Contains(NormalizeFolderKey(relative)))
                {
                    mappedCount++;
                    continue;
                }
                int archives = CountArchives(full);
                unmapped.Add(new LibraryFolderRecord(relative, archives, LibraryImportMatcher.CleanFolderQuery(Path.GetFileName(relative))));
            }
        }

        unmapped = unmapped.OrderBy(f => f.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
        string? warning = LibraryImportMatcher.LibraryPathWarning(root);
        if (seen == 0 && warning is null)
            warning = $"No series folders under {root}. Mangette expects Library\\\\SeriesName\\\\*.cbz.";
        return TypedResults.Ok(new LibraryScanResult(library.Key, library.LibraryName, root, unmapped, mappedCount, warning, seen));
    }

    [HttpPost("Match")]
    [ProducesResponseType<LibraryMatchResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<LibraryMatchResult>, BadRequest<string>>> Match([FromBody] LibraryMatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FolderName))
            return TypedResults.BadRequest("FolderName is required.");

        string query = string.IsNullOrWhiteSpace(request.Query)
            ? LibraryImportMatcher.CleanFolderQuery(Path.GetFileName(request.FolderName.Replace('/', Path.DirectorySeparatorChar)))
            : request.Query.Trim();
        if (query.Length == 0)
            return TypedResults.BadRequest("Could not build a search query from that folder name.");

        List<SeriesSearch.ExistingSeries> existing = await SeriesSearch.LoadExisting(context, HttpContext.RequestAborted);
        List<LibraryMatchCandidate> candidates = SeriesSearch.Lookup(query, null, existing)
            .Select(h => new LibraryMatchCandidate(
                h.Name,
                h.ConnectorName,
                h.IdOnSite,
                h.WebsiteUrl,
                h.CoverUrl,
                h.Score))
            .Take(8)
            .ToList();
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

        string seriesDir = Path.Combine(library.BasePath, request.FolderName.Replace('/', Path.DirectorySeparatorChar));
        int archives = Directory.Exists(seriesDir) ? CountArchives(seriesDir) : 0;
        return TypedResults.Ok(new LibraryImportResult(manga.Key, manga.Name, archives));
    }

    private async Task<FileLibrary?> ResolveLibrary(string? libraryId)
    {
        if (!string.IsNullOrWhiteSpace(libraryId))
            return await context.FileLibraries.FirstOrDefaultAsync(l => l.Key == libraryId, HttpContext.RequestAborted);
        return await context.FileLibraries.OrderBy(l => l.LibraryName).FirstOrDefaultAsync(HttpContext.RequestAborted);
    }

    private static string NormalizeFolderKey(string name) =>
        name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    private static IEnumerable<(string Relative, string Full)> SeriesFolders(string root, string topLevel)
    {
        string name = Path.GetFileName(topLevel.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (LibraryImportMatcher.IsSkippableFolder(name))
            yield break;

        int archivesHere = CountArchives(topLevel);
        string[] children;
        try
        {
            children = Directory.GetDirectories(topLevel);
        }
        catch
        {
            children = [];
        }

        bool nestedSeries = archivesHere == 0 && children.Length > 0 &&
                            children.Any(c => CountArchives(c) > 0);
        if (!nestedSeries)
        {
            yield return (name, topLevel);
            yield break;
        }

        foreach (string child in children)
        {
            string childName = Path.GetFileName(child);
            if (LibraryImportMatcher.IsSkippableFolder(childName))
                continue;
            if (CountArchives(child) == 0 && Directory.GetDirectories(child).Length == 0)
                continue;
            yield return ($"{name}/{childName}", child);
        }
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
public sealed record LibraryScanResult(string LibraryId, string LibraryName, string BasePath, List<LibraryFolderRecord> UnmappedFolders, int MappedFolderCount, string? Warning, int TotalFoldersSeen);
public sealed record LibraryMatchRequest(string FolderName, string? Query);
public sealed record LibraryMatchCandidate(string Name, string ConnectorName, string IdOnSite, string? WebsiteUrl, string CoverUrl, double Score);
public sealed record LibraryMatchResult(string FolderName, List<LibraryMatchCandidate> Matches);
public sealed record LibraryImportRequest(string LibraryId, string FolderName, string ConnectorName, string IdOnSite);
public sealed record LibraryImportResult(string MangaId, string Name, int ArchivesOnDisk);
