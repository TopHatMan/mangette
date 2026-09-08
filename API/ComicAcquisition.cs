using API.ExternalDownloadClients;
using API.IndexerConnectors;
using API.Schema.MangaContext;
using Microsoft.EntityFrameworkCore;

namespace API;

/// <summary>
/// Shared indexer-search-and-grab logic for Comics, used by both the automatic sweep
/// (<see cref="Workers.PeriodicWorkers.SearchIndexerForMissingIssuesWorker"/>) and the interactive
/// per-issue search (<see cref="Controllers.ComicController"/>) so there's one place that knows how
/// to build a query, match a release to an issue, pick a client, and hand a release off.
/// </summary>
public static class ComicAcquisition
{
    public static readonly IIndexerConnector Indexer = new ProwlarrIndexerConnector();
    private static readonly IExternalDownloadClient QBittorrent = new QBittorrentDownloadClient();
    private static readonly IExternalDownloadClient Sabnzbd = new SabnzbdDownloadClient();

    /// <summary>
    /// Search by series title alone, not "title + issue number". Comic indexers rarely have a
    /// standardized query grammar for issue numbers the way TV indexers do for SxxExx, so appending
    /// the issue number to the query text just narrows a full-text search and loses real hits (a
    /// release titled "Absolute Superman 001 (2024)" doesn't reliably match a query ending in "1").
    /// One series-wide search also covers every missing issue at once instead of one Prowlarr
    /// request per issue.
    /// </summary>
    public static string BuildQuery(Manga comic) => comic.Name;

    /// <summary>
    /// Comics very often share a bare ComicVine volume name with a completely different real-world
    /// run -- the classic 1940 "Batman" run and later relaunches are frequently just named "Batman"
    /// in ComicVine's own data, distinguished only by volume id/year, not by anything in the name
    /// text. Manga.Key derives from Name alone, so adding a second series under an identical name
    /// collides on the primary key -- confirmed live, this previously surfaced as a raw
    /// "SQLite Error 19: UNIQUE constraint failed" instead of anything actionable.
    /// Prefers "{name} ({year})" whenever a year is known at all (not just on collision) -- this is
    /// Kavita's own ComicVine-library naming convention (see the folder-org research two rounds
    /// ago), and disambiguating proactively means most future collisions never happen in the first
    /// place. Falls back to the bare name only when no year is known. If even the preferred name
    /// collides (a genuine re-add), returns a friendly error instead of a raw SQLite exception.
    /// </summary>
    public static async Task<(string Name, string? Error)> ResolveNonCollidingName(
        MangaContext context, string name, uint? year, CancellationToken cancellationToken)
    {
        async Task<bool> Exists(string candidate) =>
            await context.Mangas.AnyAsync(m => m.Key == TokenGen.CreateToken(typeof(Manga), candidate), cancellationToken);

        string preferred = year is { } y ? $"{name} ({y})" : name;
        if (!await Exists(preferred))
            return (preferred, null);

        return (name, $"A series named \"{preferred}\" is already in your library. Rename the folder/title to something more distinguishing and try again.");
    }

    /// <summary>
    /// Does this release's title look like it's the issue the chapter wants? Two separate ways a
    /// broad title-only Prowlarr search can surface a wrong-series release, both checked here:
    /// (1) indexer full-text search is loose keyword matching, not an exact-title filter, so a
    /// search for "Absolute Superman" can surface "Superman 2" (shares "Superman" but drops the
    /// distinguishing "Absolute" prefix) -- reject anything whose title doesn't actually start
    /// with the series name. (2) series get relaunched/renumbered often enough (New 52, Rebirth,
    /// etc.) that the same series name and issue number can legitimately belong to a completely
    /// different run -- if the tracked comic has a known start year (from ComicVine) and the
    /// release title has a parseable year earlier than that, reject it too: a run can't have
    /// published an issue before it started.
    /// </summary>
    public static bool MatchesIssue(IndexerRelease release, Chapter chapter)
    {
        if (!DownloadedChapterMatcher.TitleStartsWithSeries(release.Title, chapter.ParentManga.Name))
            return false;

        if (!DownloadedChapterMatcher.TryParseComicIssueNumber(release.Title, out string issueNumber) ||
            !DownloadedChapterMatcher.ChapterNumbersEqual(issueNumber, chapter.ChapterNumber))
            return false;

        if (chapter.ParentManga.Year is { } startYear &&
            DownloadedChapterMatcher.TryParseReleaseYear(release.Title, out int releaseYear) &&
            releaseYear < (int)startYear)
            return false;

        return true;
    }

    public static (IExternalDownloadClient Client, DownloadClientKind Kind) PickClient(ReleaseProtocol protocol) =>
        protocol == ReleaseProtocol.Torrent
            ? (QBittorrent, DownloadClientKind.QBittorrent)
            : (Sabnzbd, DownloadClientKind.Sabnzbd);

    /// <summary>Picks the release matching this chapter's issue number the caller's protocol preference likes best.</summary>
    public static IndexerRelease? PickBestForIssue(IEnumerable<IndexerRelease> releases, Chapter chapter, ReleaseProtocol preferred)
    {
        List<IndexerRelease> candidates = releases.Where(r => MatchesIssue(r, chapter)).ToList();
        if (candidates.Count == 0)
            return null;
        return candidates.FirstOrDefault(r => r.Protocol == preferred)
               ?? candidates.OrderByDescending(r => r.Seeders ?? 0).First();
    }

    /// <summary>Hands one chosen release to the right client and builds the job row that tracks it. Does not save.</summary>
    public static async Task<(ComicDownloadJob? Job, string? Error)> Grab(Chapter chapter, IndexerRelease release, CancellationToken cancellationToken)
    {
        (IExternalDownloadClient client, DownloadClientKind kind) = PickClient(release.Protocol);

        string? externalId;
        try
        {
            externalId = await client.Add(release, cancellationToken);
        }
        catch (Exception ex)
        {
            return (null, $"Sending \"{release.Title}\" to {kind} failed: {ex.Message}");
        }
        if (externalId is null)
            return (null, $"{kind} did not accept \"{release.Title}\".");

        ComicDownloadJob job = new(chapter, release.IndexerName, release.Title, release.DownloadUrl, release.Protocol, kind);
        job.MarkDownloading(externalId);
        return (job, null);
    }
}
