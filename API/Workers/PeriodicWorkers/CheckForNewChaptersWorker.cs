using System.Diagnostics.CodeAnalysis;
using API.MangaConnectors;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using API.Workers.MangaDownloadWorkers;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// Refresh chapter lists for monitored <em>ongoing</em> series.
/// Completed/cancelled titles are skipped. Default interval is 3 hours.
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
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.Debug("Checking ongoing series for new chapters...");
        int monitoredAll = await MangaContext.MangaConnectorToManga
            .Where(id => id.UseForDownload)
            .Select(id => id.ObjId)
            .Distinct()
            .CountAsync(CancellationToken);

        List<string> ongoingMangaIds = await MangaContext.MangaConnectorToManga
            .Where(id => id.UseForDownload &&
                         (id.Obj.ReleaseStatus == MangaReleaseStatus.Continuing
                          || id.Obj.ReleaseStatus == MangaReleaseStatus.OnHiatus
                          || id.Obj.ReleaseStatus == MangaReleaseStatus.Unreleased))
            .Select(id => id.ObjId)
            .Distinct()
            .ToListAsync(CancellationToken);

        int skipped = monitoredAll - ongoingMangaIds.Count;
        Log.InfoFormat("New-chapter search: {0} ongoing series every {1}h ({2} completed/cancelled skipped).",
            ongoingMangaIds.Count, Interval.TotalHours, skipped);

        if (ongoingMangaIds.Count == 0)
            return [];

        List<MangaConnectorId<Manga>> connectorIdsManga = await MangaContext.MangaConnectorToManga
            .Include(id => id.Obj)
            .Where(id => ongoingMangaIds.Contains(id.ObjId) && id.UseForDownload)
            .ToListAsync(CancellationToken);

        connectorIdsManga = connectorIdsManga
            .Where(id => Mangette.TryGetMangaConnector(id.MangaConnectorName, out MangaConnector? c) && c.Enabled)
            .Where(id => !DownloadFailureTracker.IsConnectorCoolingDown(id.MangaConnectorName))
            .ToList();

        Log.DebugFormat("Creating {0} update jobs...", connectorIdsManga.Count);

        List<BaseWorker> newWorkers = connectorIdsManga.Select(id => new RetrieveMangaChaptersFromMangaconnectorWorker(id, Mangette.Settings.DownloadLanguage))
            .ToList<BaseWorker>();

        return newWorkers.ToArray();
    }
}
