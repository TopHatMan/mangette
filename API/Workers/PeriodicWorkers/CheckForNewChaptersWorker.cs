using System.Diagnostics.CodeAnalysis;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using API.Workers.MangaDownloadWorkers;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// Creates Jobs to update available Chapters for all Manga that are marked for Download.
/// If any connector on a manga is monitored, every attached connector is refreshed.
/// </summary>
public class CheckForNewChaptersWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    public TimeSpan Interval { get; set; } = interval??Constants.CheckForNewChaptersInterval;
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.Debug("Checking for new chapters...");
        List<string> monitoredMangaIds = await MangaContext.MangaConnectorToManga
            .Where(id => id.UseForDownload)
            .Select(id => id.ObjId)
            .Distinct()
            .ToListAsync(CancellationToken);

        List<MangaConnectorId<Manga>> connectorIdsManga = await MangaContext.MangaConnectorToManga
            .Include(id => id.Obj)
            .Where(id => monitoredMangaIds.Contains(id.ObjId))
            .ToListAsync(CancellationToken);

        connectorIdsManga = connectorIdsManga
            .Where(id => !DownloadFailureTracker.IsConnectorCoolingDown(id.MangaConnectorName))
            .ToList();

        Log.DebugFormat("Creating {0} update jobs...", connectorIdsManga.Count);

        List<BaseWorker> newWorkers = connectorIdsManga.Select(id => new RetrieveMangaChaptersFromMangaconnectorWorker(id, Mangette.Settings.DownloadLanguage))
            .ToList<BaseWorker>();

        return newWorkers.ToArray();
    }
}
