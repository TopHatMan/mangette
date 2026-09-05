using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using API.ExternalDownloadClients;
using API.IndexerConnectors;
using API.Schema.MangaContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// For every wanted Comic issue (a Chapter placeholder created by <see cref="MaterializeWantedComicIssuesWorker"/>
/// with no in-flight <see cref="ComicDownloadJob"/>), searches Prowlarr and hands the winning release to
/// qBittorrent or SABnzbd depending on <see cref="MangetteSettings.ComicProtocolPreference"/>.
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

    private static readonly IIndexerConnector Indexer = new ProwlarrIndexerConnector();
    private static readonly IExternalDownloadClient QBittorrent = new QBittorrentDownloadClient();
    private static readonly IExternalDownloadClient Sabnzbd = new SabnzbdDownloadClient();

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

            string query = $"{chapter.ParentManga.Name} {chapter.ChapterNumber}";
            IndexerRelease[] releases;
            try
            {
                releases = await Indexer.Search(query, CancellationToken);
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
            IExternalDownloadClient client = chosen.Protocol == ReleaseProtocol.Torrent ? QBittorrent : Sabnzbd;
            DownloadClientKind clientKind = chosen.Protocol == ReleaseProtocol.Torrent
                ? DownloadClientKind.QBittorrent
                : DownloadClientKind.Sabnzbd;

            string? externalId;
            try
            {
                externalId = await client.Add(chosen, CancellationToken);
            }
            catch (Exception ex)
            {
                QueueLog.Error($"Sending \"{chosen.Title}\" to {clientKind} failed: {ex.Message}", ex);
                continue;
            }
            if (externalId is null)
            {
                QueueLog.WarnFormat("{0} did not accept \"{1}\".", clientKind, chosen.Title);
                continue;
            }

            ComicDownloadJob job = new(chapter, chosen.IndexerName, chosen.Title, chosen.DownloadUrl, chosen.Protocol, clientKind);
            job.MarkDownloading(externalId);
            MangaContext.ComicDownloadJobs.Add(job);
            grabbed++;
            QueueLog.InfoFormat("Grabbed \"{0}\" from {1} ({2}) for {3} #{4}.",
                chosen.Title, chosen.IndexerName, clientKind, chapter.ParentManga.Name, chapter.ChapterNumber);
        }

        if (grabbed > 0 && await MangaContext.Sync(CancellationToken, GetType(), "Grab comic releases") is { success: false } result)
            QueueLog.Error($"Failed to save grabbed comic releases: {result.exceptionMessage}");

        return [];
    }
}
