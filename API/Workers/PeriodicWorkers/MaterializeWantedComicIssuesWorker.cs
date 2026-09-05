using System.Diagnostics.CodeAnalysis;
using API.Schema.MangaContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// Comics have no scraping connector to discover issues from, so instead of a chapter list we watch
/// an expected issue-number range (<see cref="Manga.ComicIssueStart"/>/<see cref="Manga.ComicIssueEnd"/>)
/// and materialize a placeholder <see cref="Chapter"/> for every issue not yet on disk/queued. Those
/// placeholders are exactly what <see cref="SearchIndexerForMissingIssuesWorker"/> looks for.
/// </summary>
public class MaterializeWantedComicIssuesWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null, string? mangaId = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    /// <summary>For an open-ended series (no ComicIssueEnd), how many issues past the highest downloaded one to watch for.</summary>
    private const int OpenEndedLookahead = 3;

    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    public TimeSpan Interval { get; set; } = interval ?? TimeSpan.FromMinutes(10);
    private static readonly ILog QueueLog = LogManager.GetLogger(typeof(MaterializeWantedComicIssuesWorker));

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        IQueryable<Manga> query = MangaContext.Mangas
            .Include(m => m.Chapters)
            .Where(m => m.Kind == MediaKind.Comic && m.Monitored);
        if (!string.IsNullOrWhiteSpace(mangaId))
            query = query.Where(m => m.Key == mangaId);

        List<Manga> comics = await query.ToListAsync(CancellationToken);
        int created = 0;
        foreach (Manga comic in comics)
        {
            if (comic.ComicIssueStart is not { } start)
                continue;

            int highestDownloaded = comic.Chapters
                .Where(c => c.Downloaded)
                .Select(c => int.TryParse(c.ChapterNumber, out int n) ? n : start - 1)
                .DefaultIfEmpty(start - 1)
                .Max();
            int end = comic.ComicIssueEnd ?? Math.Max(start - 1, highestDownloaded) + OpenEndedLookahead;

            HashSet<string> existingIssues = comic.Chapters.Select(c => c.ChapterNumber).ToHashSet();
            for (int issue = start; issue <= end; issue++)
            {
                string issueNumber = issue.ToString();
                if (existingIssues.Contains(issueNumber))
                    continue;
                comic.Chapters.Add(new Chapter(comic, issueNumber, null));
                created++;
            }
        }

        if (created > 0)
        {
            if (await MangaContext.Sync(CancellationToken, GetType(), "Materialize wanted comic issues") is { success: false } result)
                QueueLog.Error($"Failed to save wanted comic issues: {result.exceptionMessage}");
            else
                QueueLog.InfoFormat("Materialized {0} wanted comic issue(s).", created);
        }

        return [];
    }
}
