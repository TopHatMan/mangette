using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using API.IndexerConnectors;
using API.Schema.MangaContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// For every wanted Comic issue (a Chapter placeholder created by <see cref="MaterializeWantedComicIssuesWorker"/>
/// with no in-flight <see cref="ComicDownloadJob"/>), searches Prowlarr and hands the winning release to
/// qBittorrent or SABnzbd depending on <see cref="MangetteSettings.ComicProtocolPreference"/>. This is the
/// automatic sweep; <see cref="Controllers.ComicController"/> exposes the same search-and-grab interactively
/// for one issue at a time via <see cref="ComicAcquisition"/>.
/// </summary>
public class SearchIndexerForMissingIssuesWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    public TimeSpan Interval { get; set; } = interval ?? TimeSpan.FromMinutes(5);
    private static readonly ILog QueueLog = LogManager.GetLogger(typeof(SearchIndexerForMissingIssuesWorker));

    /// <summary>Avoids hammering Prowlarr every tick for an issue no indexer has yet. Not persisted; resets on restart.</summary>
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

        int grabbed = 0;
        foreach (Chapter chapter in wanted)
        {
            if (inFlight.Contains(chapter.Key))
                continue;
            if (LastAttempt.TryGetValue(chapter.Key, out DateTime last) && DateTime.UtcNow - last < Cooldown)
                continue;
            LastAttempt[chapter.Key] = DateTime.UtcNow;

            string query = ComicAcquisition.BuildQuery(chapter.ParentManga, chapter);
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

            ReleaseProtocol preferred = Mangette.Settings.ComicProtocolPreference == ComicProtocolPreference.Usenet
                ? ReleaseProtocol.Usenet
                : ReleaseProtocol.Torrent;
            IndexerRelease chosen = releases.FirstOrDefault(r => r.Protocol == preferred) ?? releases[0];

            (ComicDownloadJob? job, string? error) = await ComicAcquisition.Grab(chapter, chosen, CancellationToken);
            if (job is null)
            {
                QueueLog.Warn(error);
                continue;
            }

            MangaContext.ComicDownloadJobs.Add(job);
            grabbed++;
            QueueLog.InfoFormat("Grabbed \"{0}\" from {1} ({2}) for {3} #{4}.",
                chosen.Title, chosen.IndexerName, job.ClientName, chapter.ParentManga.Name, chapter.ChapterNumber);
        }

        if (grabbed > 0 && await MangaContext.Sync(CancellationToken, GetType(), "Grab comic releases") is { success: false } result)
            QueueLog.Error($"Failed to save grabbed comic releases: {result.exceptionMessage}");

        return [];
    }
}
