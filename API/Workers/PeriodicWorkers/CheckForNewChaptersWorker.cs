using System.Diagnostics.CodeAnalysis;
using API.MangaConnectors;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using API.Workers.MangaDownloadWorkers;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// Refresh chapter lists for monitored <em>ongoing</em> series on their Daily/Weekly cadence.
/// New chapters raise the series total so missing downloads can be queued.
/// Completed/cancelled titles are skipped unless a user forces a refresh.
/// </summary>
public class CheckForNewChaptersWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    public TimeSpan Interval { get; set; } = interval??Constants.CheckForNewChaptersInterval;

    /// <summary>Continuing, hiatus, and unknown still get searched. Finished/cancelled do not.</summary>
    internal static bool IsOngoing(MangaReleaseStatus status) =>
        status is MangaReleaseStatus.Continuing
            or MangaReleaseStatus.OnHiatus
            or MangaReleaseStatus.Unreleased;

    internal static TimeSpan IntervalDuration(NewChapterCheckInterval interval) =>
        interval == NewChapterCheckInterval.Weekly ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);

    /// <summary>
    /// A monitored ongoing series is due when it has never been scanned, or when Daily/Weekly has elapsed.
    /// </summary>
    internal static bool IsDue(Manga manga, DateTime utcNow)
    {
        if (!manga.Monitored || !IsOngoing(manga.ReleaseStatus))
            return false;
        if (manga.LastNewChapterCheck is null)
            return true;
        return utcNow - manga.LastNewChapterCheck.Value >= IntervalDuration(manga.NewChapterCheck);
    }
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.Debug("Checking ongoing series for new chapters...");
        DateTime now = DateTime.UtcNow;
        List<Manga> monitored = await MangaContext.Mangas
            .Include(m => m.MangaConnectorIds)
            .Where(m => m.Monitored)
            .ToListAsync(CancellationToken);

        List<Manga> due = monitored.Where(m => IsDue(m, now)).ToList();
        int skippedCompleted = monitored.Count(m => !IsOngoing(m.ReleaseStatus));
        int waiting = monitored.Count - due.Count - skippedCompleted;
        Log.InfoFormat(
            "New-chapter search: {0} due of {1} monitored ({2} completed/cancelled skipped, {3} waiting on Daily/Weekly).",
            due.Count, monitored.Count, skippedCompleted, waiting);

        if (due.Count == 0)
            return [];

        foreach (Manga manga in due)
            manga.LastNewChapterCheck = now;

        if (await MangaContext.Sync(CancellationToken, GetType(), "Mark new-chapter checks") is { success: false } sync)
            Log.ErrorFormat("Failed to save last chapter-check times: {0}", sync.exceptionMessage);

        List<BaseWorker> newWorkers = CreateRefreshJobs(due.SelectMany(ConnectorsToRefresh)).ToList();
        Log.DebugFormat("Creating {0} update jobs...", newWorkers.Count);
        return newWorkers.ToArray();
    }

    internal static IEnumerable<MangaConnectorId<Manga>> ConnectorsToRefresh(Manga manga)
    {
        IEnumerable<MangaConnectorId<Manga>> enabled = manga.MangaConnectorIds.Where(id => id.UseForDownload);
        return enabled.Any() ? enabled : manga.MangaConnectorIds;
    }

    internal static List<BaseWorker> CreateRefreshJobs(IEnumerable<MangaConnectorId<Manga>> connectorIds)
    {
        return connectorIds
            .Where(id => Mangette.TryGetMangaConnector(id.MangaConnectorName, out MangaConnector? c) && c.Enabled)
            .Where(id => !DownloadFailureTracker.IsConnectorCoolingDown(id.MangaConnectorName))
            .Select(id => (BaseWorker)new RetrieveMangaChaptersFromMangaconnectorWorker(id, Mangette.Settings.DownloadLanguage))
            .ToList();
    }
}
