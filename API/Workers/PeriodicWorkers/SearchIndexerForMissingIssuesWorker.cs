using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using API.IndexerConnectors;
using API.Schema.MangaContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// For every Comic series with a wanted issue (a Chapter placeholder created by
/// <see cref="MaterializeWantedComicIssuesWorker"/> with no in-flight <see cref="ComicDownloadJob"/>),
/// searches Prowlarr once for the whole series and distributes matching releases across every wanted
/// issue that search turned up -- one Prowlarr request can and usually does satisfy many issues at
/// once, since indexers return every release for the series, not just one. This is the automatic
/// sweep; <see cref="Controllers.ComicController"/> exposes the same search-and-grab interactively
/// for one issue at a time via <see cref="ComicAcquisition"/>.
/// </summary>
public class SearchIndexerForMissingIssuesWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    public TimeSpan Interval { get; set; } = interval ?? TimeSpan.FromMinutes(5);
    private static readonly ILog QueueLog = LogManager.GetLogger(typeof(SearchIndexerForMissingIssuesWorker));

    /// <summary>Avoids hammering Prowlarr every tick for a series with no new releases yet. Not persisted; resets on restart.</summary>
    private static readonly ConcurrentDictionary<string, DateTime> LastAttempt = new();
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        List<string> inFlightChapterIds = await MangaContext.ComicDownloadJobs
            .Where(j => j.Status != ComicDownloadJobStatus.Failed)
            .Select(j => j.ChapterId)
            .ToListAsync(CancellationToken);
        HashSet<string> inFlight = inFlightChapterIds.ToHashSet();

        List<Chapter> wanted = await MangaContext.Chapters
            .Include(c => c.ParentManga)
            .Where(c => !c.Downloaded && c.ParentManga.Kind == MediaKind.Comic && c.ParentManga.Monitored)
            .ToListAsync(CancellationToken);

        ReleaseProtocol preferred = Mangette.Settings.ComicProtocolPreference == ComicProtocolPreference.Usenet
            ? ReleaseProtocol.Usenet
            : ReleaseProtocol.Torrent;

        int grabbed = 0;
        foreach (IGrouping<string, Chapter> series in wanted.GroupBy(c => c.ParentMangaId))
        {
            List<Chapter> pending = series.Where(c => !inFlight.Contains(c.Key)).ToList();
            if (pending.Count == 0)
                continue;

            Manga comic = pending[0].ParentManga;
            if (LastAttempt.TryGetValue(comic.Key, out DateTime last) && DateTime.UtcNow - last < Cooldown)
                continue;
            LastAttempt[comic.Key] = DateTime.UtcNow;

            string query = ComicAcquisition.BuildQuery(comic);
            IndexerRelease[] releases;
            try
            {
                releases = await ComicAcquisition.Indexer.Search(query, CancellationToken);
            }
            catch (Exception ex)
            {
                QueueLog.Error($"Indexer search failed for \"{query}\": {ex.Message}", ex);
                continue;
            }
            if (releases.Length == 0)
                continue;

            foreach (Chapter chapter in pending)
            {
                IndexerRelease? chosen = ComicAcquisition.PickBestForIssue(releases, chapter, preferred);
                if (chosen is null)
                    continue;

                (ComicDownloadJob? job, string? error) = await ComicAcquisition.Grab(chapter, chosen, CancellationToken);
                if (job is null)
                {
                    QueueLog.Warn(error);
                    continue;
                }

                MangaContext.ComicDownloadJobs.Add(job);
                grabbed++;
                QueueLog.InfoFormat("Grabbed \"{0}\" from {1} ({2}) for {3} #{4}.",
                    chosen.Title, chosen.IndexerName, job.ClientName, comic.Name, chapter.ChapterNumber);
            }
        }

        if (grabbed > 0 && await MangaContext.Sync(CancellationToken, GetType(), "Grab comic releases") is { success: false } result)
            QueueLog.Error($"Failed to save grabbed comic releases: {result.exceptionMessage}");

        return [];
    }
}
