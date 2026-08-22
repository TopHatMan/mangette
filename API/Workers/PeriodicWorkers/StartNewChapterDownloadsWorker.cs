using System.Diagnostics.CodeAnalysis;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using API.Workers.MangaDownloadWorkers;
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
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.Debug("Checking for missing chapters...");
        
        List<MangaConnectorId<Chapter>> missingChapters = await GetMissingChapters(MangaContext, CancellationToken);
        
        Log.DebugFormat("Found {0} missing chapters.", missingChapters.Count);
        List<DownloadChapterFromMangaconnectorWorker> runningDownloads = Mangette.GetRunningWorkers()
            .OfType<DownloadChapterFromMangaconnectorWorker>()
            .ToList();
        HashSet<string> inFlightConnectorIds = runningDownloads.Select(w => w.ChapterIdId).ToHashSet();
        HashSet<string> inFlightChapterKeys = runningDownloads.Select(w => w.ChapterKey).ToHashSet();

        int downloadWorkers = runningDownloads.Count;
        int amountNewWorkers = Math.Max(0, Mangette.Settings.MaxConcurrentDownloads - downloadWorkers);
        
        Log.DebugFormat("{0} running download Workers. {1} available new download Workers.", downloadWorkers, amountNewWorkers);

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

        Log.DebugFormat("{0} chapters queued after failover/cooldown filter.", newDownloadChapters.Count);

        List<BaseWorker> newWorkers = newDownloadChapters.Select(mcId => new DownloadChapterFromMangaconnectorWorker(mcId)).ToList<BaseWorker>();
        
        return newWorkers.ToArray();
    }
    
    internal static async Task<List<MangaConnectorId<Chapter>>> GetMissingChapters(MangaContext ctx, CancellationToken cancellationToken)
    {
        List<MangaConnectorId<Chapter>> missing = await ctx.MangaConnectorToChapter
            .Include(id => id.Obj)
            .Where(id => !id.Obj.Downloaded && id.UseForDownload)
            .ToListAsync(cancellationToken);
        return missing
            .Where(id => Mangette.TryGetMangaConnector(id.MangaConnectorName, out MangaConnectors.MangaConnector? c) && c.Enabled)
            .ToList();
    }
}
