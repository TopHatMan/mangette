using System.Diagnostics.CodeAnalysis;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using API.Workers.MangaDownloadWorkers;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// Create new Workers for Chapters on Manga marked for Download, that havent been downloaded yet.
/// One job per logical chapter, using the highest-preference source that is not cooling down.
/// </summary>
public class StartNewChapterDownloadsWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{

    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    public TimeSpan Interval { get; set; } = interval ?? TimeSpan.FromMinutes(1);
    private static readonly ILog QueueLog = LogManager.GetLogger(typeof(StartNewChapterDownloadsWorker));
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.Debug("Checking for missing chapters...");
        List<DownloadChapterFromMangaconnectorWorker> workers = await SelectNewDownloadWorkers(MangaContext, CancellationToken);
        return workers.ToArray<BaseWorker>();
    }

    /// <summary>Queue missing monitored chapters up to MaxConcurrentDownloads. Used after a disk scan.</summary>
    internal static async Task<int> EnqueueAvailableDownloads(MangaContext ctx, CancellationToken cancellationToken, string? mangaId = null)
    {
        List<DownloadChapterFromMangaconnectorWorker> workers = await SelectNewDownloadWorkers(ctx, cancellationToken, mangaId);
        if (workers.Count > 0)
            Mangette.AddWorkers(workers);
        return workers.Count;
    }

    internal static async Task<List<DownloadChapterFromMangaconnectorWorker>> SelectNewDownloadWorkers(
        MangaContext ctx,
        CancellationToken cancellationToken,
        string? mangaId = null)
    {
        List<MangaConnectorId<Chapter>> missingChapters = await GetMissingChapters(ctx, cancellationToken, mangaId);

        QueueLog.DebugFormat("Found {0} missing chapters.", missingChapters.Count);
        List<DownloadChapterFromMangaconnectorWorker> runningDownloads = Mangette.GetRunningWorkers()
            .OfType<DownloadChapterFromMangaconnectorWorker>()
            .ToList();
        HashSet<string> inFlightConnectorIds = runningDownloads.Select(w => w.ChapterIdId).ToHashSet();
        HashSet<string> inFlightChapterKeys = runningDownloads.Select(w => w.ChapterKey).ToHashSet();

        int downloadWorkers = runningDownloads.Count;
        int amountNewWorkers = Math.Max(0, Mangette.Settings.MaxConcurrentDownloads - downloadWorkers);

        QueueLog.DebugFormat("{0} running download Workers. {1} available new download Workers.", downloadWorkers, amountNewWorkers);

        Dictionary<string, string> chapterToSeries = missingChapters
            .GroupBy(id => id.ObjId)
            .ToDictionary(g => g.Key, g => DownloadFailureTracker.SeriesKey(g.First()));
        Dictionary<string, int> inFlightBySeries = [];
        foreach (DownloadChapterFromMangaconnectorWorker running in runningDownloads)
        {
            if (!chapterToSeries.TryGetValue(running.ChapterKey, out string? series))
                continue;
            inFlightBySeries[series] = inFlightBySeries.GetValueOrDefault(series) + 1;
        }

        List<MangaConnectorId<Chapter>> newDownloadChapters = DownloadFailureTracker.SelectDownloadSources(
            missingChapters,
            inFlightConnectorIds,
            inFlightChapterKeys,
            amountNewWorkers,
            inFlightBySeries);

        if (newDownloadChapters.Count > 0)
        {
            string preview = string.Join(", ", newDownloadChapters
                .Select(id => $"{DownloadFailureTracker.SeriesName(id)} ch.{id.Obj.ChapterNumber}"));
            QueueLog.InfoFormat("A–Z download turn ({0}): {1}", newDownloadChapters.Count, preview);
        }
        return newDownloadChapters.Select(mcId => new DownloadChapterFromMangaconnectorWorker(mcId)).ToList();
    }
    
    internal static async Task<List<MangaConnectorId<Chapter>>> GetMissingChapters(MangaContext ctx, CancellationToken cancellationToken, string? mangaId = null)
    {
        IQueryable<MangaConnectorId<Chapter>> query = ctx.MangaConnectorToChapter
            .Include(id => id.Obj)
            .ThenInclude(c => c.ParentManga)
            .Where(id => !id.Obj.Downloaded && id.UseForDownload);
        if (!string.IsNullOrWhiteSpace(mangaId))
            query = query.Where(id => id.Obj.ParentMangaId == mangaId);

        List<MangaConnectorId<Chapter>> missing = await query.ToListAsync(cancellationToken);
        return missing
            .Where(id => Mangette.TryGetMangaConnector(id.MangaConnectorName, out MangaConnectors.MangaConnector? c) && c.Enabled)
            .ToList();
    }
}
