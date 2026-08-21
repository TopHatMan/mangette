using System.Diagnostics.CodeAnalysis;
using API.Schema.MangaContext;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// Updates the database to reflect changes made on disk
/// </summary>
public class UpdateChaptersDownloadedWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    public TimeSpan Interval { get; set; } = interval??TimeSpan.FromDays(1);
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.Debug("Checking chapter files...");
        List<Manga> mangas = await MangaContext.Mangas
            .Include(m => m.Library)
            .Include(m => m.AltTitles)
            .Include(m => m.Chapters)
            .ToListAsync(CancellationToken);
        int matched = 0;
        foreach (Manga manga in mangas)
        {
            try
            {
                manga.TryAttachExistingSeriesFolder();
                foreach (Chapter chapter in manga.Chapters)
                {
                    chapter.ParentManga = manga;
                    if (chapter.ApplyDownloadedMatch())
                        matched++;
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception);
            }
        }
        Log.InfoFormat("Library scan: {0} chapters already on disk out of {1}.", matched,
            mangas.Sum(m => m.Chapters.Count));

        if(await MangaContext.Sync(CancellationToken, GetType(), System.Reflection.MethodBase.GetCurrentMethod()?.Name) is { success: false } e)
            Log.ErrorFormat("Failed to save database changes: {0}", e.exceptionMessage);
        
        return [];
    }
}