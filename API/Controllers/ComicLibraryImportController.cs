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
/// Brings an existing on-disk Comic library into Mangette: scan a Comic-kind <see cref="FileLibrary"/>
/// for series folders, match each against ComicVine, and import (adopt the folder in place, no file
/// moves -- same as <see cref="LibraryImportController"/> does for manga). Real comic libraries are
/// 2-4 folders deep per series (Volumes/Annuals/Extras/Variants under one series folder), unlike the
/// flat one-folder-per-series manga layout, so Scan works at the top-level-folder granularity and
/// counts archives recursively underneath.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class ComicLibraryImportController(MangaContext context) : ControllerBase
{
    private readonly ILog Log = LogManager.GetLogger(typeof(ComicLibraryImportController));

    [HttpGet("Scan")]
    [ProducesResponseType<ComicScanResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<ComicScanResult>, BadRequest<string>>> Scan([FromQuery] string? fileLibraryId = null)
    {
        FileLibrary? library = await ResolveLibrary(fileLibraryId);
        if (library is null)
            return TypedResults.BadRequest("No Comic-kind library found. Create one first (FileLibrary with Kind=Comic).");

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
            return TypedResults.BadRequest($"Library folder does not exist: {root}.");

        List<string> mappedDirectoryNames = await context.Mangas
            .Where(m => m.LibraryId == library.Key && m.Monitored)
            .Select(m => m.DirectoryName)
            .ToListAsync(HttpContext.RequestAborted);
        HashSet<string> mapped = mappedDirectoryNames
            .Select(NormalizeFolderKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> topLevelDirs;
        try
        {
            topLevelDirs = Directory.EnumerateDirectories(root).ToList();
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Cannot read {root}: {ex.Message}");
        }

        List<ComicScanFolderRecord> unmapped = [];
        int mappedCount = 0;
        foreach (string dir in topLevelDirs)
        {
            string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (LibraryImportMatcher.IsSkippableFolder(name))
                continue;
            if (mapped.Contains(NormalizeFolderKey(name)))
            {
                mappedCount++;
                continue;
            }

            (int archives, int other) = CountFiles(dir);
            unmapped.Add(new ComicScanFolderRecord(name, archives, other, ComicLibraryImportMatcher.CleanSeriesName(name)));
        }

        unmapped = unmapped.OrderBy(f => f.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
        string? warning = LibraryImportMatcher.LibraryPathWarning(root);
        Log.InfoFormat("Comic scan {0}: {1} unmapped, {2} already in library.", root, unmapped.Count, mappedCount);
        return TypedResults.Ok(new ComicScanResult(library.Key, library.LibraryName, root, unmapped, mappedCount, warning));
    }

    [HttpPost("Match")]
    [ProducesResponseType<ComicMatchResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public Task<Results<Ok<ComicMatchResult>, BadRequest<string>>> Match([FromBody] ComicMatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FolderName))
            return Task.FromResult<Results<Ok<ComicMatchResult>, BadRequest<string>>>(TypedResults.BadRequest("FolderName is required."));

        string query = string.IsNullOrWhiteSpace(request.Query)
            ? ComicLibraryImportMatcher.CleanSeriesName(request.FolderName)
            : request.Query.Trim();
        if (query.Length == 0)
            return Task.FromResult<Results<Ok<ComicMatchResult>, BadRequest<string>>>(TypedResults.BadRequest("Could not build a search query from that folder name."));

        try
        {
            List<ComicMatchCandidate> candidates = Mangette.ComicVine.SearchMetadataEntry(query)
                .Select(r => new ComicMatchCandidate(r.Name, r.Identifier, r.Url, r.CoverUrl, LibraryImportMatcher.ScoreTitle(request.FolderName, r.Name)))
                .OrderByDescending(c => c.Score)
                .Take(8)
                .ToList();
            Log.InfoFormat("Comic match \"{0}\" query \"{1}\": {2} hits.", request.FolderName, query, candidates.Count);
            return Task.FromResult<Results<Ok<ComicMatchResult>, BadRequest<string>>>(TypedResults.Ok(new ComicMatchResult(request.FolderName, candidates)));
        }
        catch (Exception ex)
        {
            Log.Error($"Comic match failed for \"{request.FolderName}\" query \"{query}\": {ex.Message}", ex);
            return Task.FromResult<Results<Ok<ComicMatchResult>, BadRequest<string>>>(TypedResults.BadRequest($"Match failed: {ex.Message}"));
        }
    }

    [HttpPost("Import")]
    [ProducesResponseType<ComicImportResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok<ComicImportResult>, BadRequest<string>, InternalServerError<string>>> Import(
        [FromBody] ComicImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FolderName) || string.IsNullOrWhiteSpace(request.ComicVineVolumeId))
            return TypedResults.BadRequest("FolderName and ComicVineVolumeId are required.");

        FileLibrary? library = await ResolveLibrary(request.LibraryId);
        if (library is null)
            return TypedResults.BadRequest("No Comic-kind library found.");

        JObject? volume = await Mangette.ComicVine.GetVolume(request.ComicVineVolumeId, HttpContext.RequestAborted);
        if (volume is null)
            return TypedResults.BadRequest($"Could not load ComicVine volume {request.ComicVineVolumeId}.");

        string name = volume.Value<string>("name") ?? request.FolderName;
        string coverUrl = volume.Value<JObject>("image")?.Value<string>("medium_url") ?? "";
        string description = ComicVine.StripHtml(volume.Value<string>("description") ?? "");
        int issueCount = volume.Value<int?>("count_of_issues") ?? 0;
        uint? year = uint.TryParse(volume.Value<string>("start_year"), out uint y) ? y : null;

        Manga comic = new(name, description, coverUrl, MangaReleaseStatus.Continuing, [], [], [], [], library, year: year)
        {
            Kind = MediaKind.Comic,
            ComicIssueStart = 1,
            ComicIssueEnd = issueCount > 0 ? issueCount : null
        };
        comic.SetDirectoryName(request.FolderName);
        comic.SetMonitored(true);

        string? siteUrl = volume.Value<string>("site_detail_url");
        if (!string.IsNullOrWhiteSpace(siteUrl))
            comic.Links.Add(new Link("ComicVine", siteUrl));

        context.Mangas.Add(comic);
        if (await context.Sync(HttpContext.RequestAborted, GetType(), "Import comic") is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        Mangette.AddWorker(new MaterializeWantedComicIssuesWorker(mangaId: comic.Key));
        Mangette.AddWorker(Mangette.UpdateChaptersDownloadedWorker);

        string seriesDir = Path.Combine(library.BasePath, request.FolderName);
        (int archives, _) = Directory.Exists(seriesDir) ? CountFiles(seriesDir) : (0, 0);
        Log.InfoFormat("Imported \"{0}\" as {1} from ComicVine ({2} issues listed, {3} archives on disk).",
            request.FolderName, comic.Name, issueCount, archives);
        return TypedResults.Ok(new ComicImportResult(comic.Key, comic.Name, issueCount, archives));
    }

    [HttpGet("Unmatched")]
    [ProducesResponseType<List<string>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<List<string>>, NotFound<string>>> Unmatched([FromQuery] string mangaId)
    {
        Manga? comic = await context.Mangas
            .Include(m => m.Library)
            .Include(m => m.Chapters)
            .FirstOrDefaultAsync(m => m.Key == mangaId, HttpContext.RequestAborted);
        if (comic?.Library is null)
            return TypedResults.NotFound($"Unknown Manga {mangaId}, or it has no library.");

        string seriesDirectory;
        try
        {
            seriesDirectory = Path.GetFullPath(Path.Combine(comic.Library.BasePath, comic.DirectoryName));
        }
        catch (Exception ex)
        {
            return TypedResults.NotFound($"Series folder path is not valid: {ex.Message}");
        }
        if (!Directory.Exists(seriesDirectory))
            return TypedResults.Ok(new List<string>());

        HashSet<string> known = comic.Chapters
            .Where(c => c.FileName is not null)
            .Select(c => Path.GetFullPath(Path.Combine(seriesDirectory, c.FileName!)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> unmatched = Directory.EnumerateFiles(seriesDirectory, "*", SearchOption.AllDirectories)
            .Where(DownloadedChapterMatcher.IsArchive)
            .Where(f => !known.Contains(Path.GetFullPath(f)))
            .Select(f => Path.GetRelativePath(seriesDirectory, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return TypedResults.Ok(unmatched);
    }

    private async Task<FileLibrary?> ResolveLibrary(string? libraryId)
    {
        if (!string.IsNullOrWhiteSpace(libraryId))
            return await context.FileLibraries.FirstOrDefaultAsync(l => l.Key == libraryId && l.Kind == MediaKind.Comic, HttpContext.RequestAborted);
        return await context.FileLibraries.Where(l => l.Kind == MediaKind.Comic).OrderBy(l => l.LibraryName).FirstOrDefaultAsync(HttpContext.RequestAborted);
    }

    private static string NormalizeFolderKey(string name) =>
        name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    private static (int archives, int other) CountFiles(string directory)
    {
        try
        {
            int archives = 0, other = 0;
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (DownloadedChapterMatcher.IsArchive(path))
                    archives++;
                else
                    other++;
            }
            return (archives, other);
        }
        catch
        {
            return (0, 0);
        }
    }
}

public sealed record ComicScanFolderRecord(string FolderName, int ArchiveCount, int OtherFileCount, string SuggestedQuery);
public sealed record ComicScanResult(string LibraryId, string LibraryName, string BasePath, List<ComicScanFolderRecord> UnmappedFolders, int MappedFolderCount, string? Warning);
public sealed record ComicMatchRequest(string FolderName, string? Query);
public sealed record ComicMatchCandidate(string Name, string ComicVineVolumeId, string? Url, string? CoverUrl, double Score);
public sealed record ComicMatchResult(string FolderName, List<ComicMatchCandidate> Matches);
public sealed record ComicImportRequest(string LibraryId, string FolderName, string ComicVineVolumeId);
public sealed record ComicImportResult(string MangaId, string Name, int IssueCount, int ArchivesOnDisk);
