using System.Diagnostics.CodeAnalysis;
using API.MangaConnectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.MangaContext;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.MangaDownloadWorkers;

/// <summary>
/// Retrieves the metadata of available chapters on the Mangaconnector
/// </summary>
/// <param name="mcId"></param>
/// <param name="language"></param>
/// <param name="dependsOn"></param>
public class RetrieveMangaChaptersFromMangaconnectorWorker(MangaConnectorId<Manga> mcId, string language, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn)
{
    private readonly string _mangaConnectorIdId = mcId.Key;

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private ActionsContext ActionsContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
        ActionsContext = GetContext<ActionsContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.DebugFormat("Getting Chapters for MangaConnectorId {0}...", _mangaConnectorIdId);
        // Getting MangaConnector info
        if (await MangaContext.MangaConnectorToManga
                .Include(id => id.Obj)
                    .ThenInclude(m => m.Chapters)
                    .ThenInclude(ch => ch.MangaConnectorIds)
                .Include(id => id.Obj)
                    .ThenInclude(m => m.MangaConnectorIds)
                .Include(id => id.Obj)
                    .ThenInclude(m => m.Library)
                .Include(id => id.Obj)
                    .ThenInclude(m => m.AltTitles)
                .FirstOrDefaultAsync(c => c.Key == _mangaConnectorIdId, CancellationToken) is not { } mangaConnectorId)
        {
            Log.Error("Could not get MangaConnectorId.");
            return []; //TODO Exception?
        }
        if (!Mangette.TryGetMangaConnector(mangaConnectorId.MangaConnectorName, out MangaConnector? mangaConnector))
        {
            Log.Error("Could not get MangaConnector.");
            return []; //TODO Exception?
        }
        Log.DebugFormat("Getting Chapters for MangaConnectorId {0}...", mangaConnectorId);
        
        Manga manga = mangaConnectorId.Obj;
        
        (Chapter chapter, MangaConnectorId<Chapter> chapterId)[] allChapters;
        try
        {
            allChapters = mangaConnector.GetChapters(mangaConnectorId, language).DistinctBy(c => c.Item1.Key).ToArray();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to list chapters from {mangaConnector.Name} for {manga.Name}: {ex.Message}", ex);
            return [];
        }
        Log.DebugFormat("Got {0} chapters from connector.", allChapters.Length);

        List<MangaConnectorId<Chapter>> newIds = [];
        int reusedChapters = 0;

        foreach ((Chapter incomingChapter, MangaConnectorId<Chapter> incomingId) in allChapters)
        {
            Chapter? existing = manga.Chapters.FirstOrDefault(c => c.IsSameLogicalChapter(incomingChapter));
            Chapter target = existing ?? incomingChapter;
            if (existing is not null)
            {
                reusedChapters++;
                incomingId.Obj = existing;
                incomingId.ObjId = existing.Key;
            }
            else
            {
                manga.Chapters.Add(incomingChapter);
            }

            bool idExists = target.MangaConnectorIds.Any(existingId =>
                existingId.MangaConnectorName == incomingId.MangaConnectorName &&
                existingId.IdOnConnectorSite == incomingId.IdOnConnectorSite);
            if (idExists)
                continue;

            if (!ReferenceEquals(incomingId.Obj, target))
            {
                incomingId.Obj = target;
                incomingId.ObjId = target.Key;
            }

            target.MangaConnectorIds.Add(incomingId);
            newIds.Add(incomingId);
        }

        Log.DebugFormat("Reused {0} existing chapter rows. Got {1} new download-Ids.", reusedChapters, newIds.Count);

        bool monitored = mangaConnectorId.UseForDownload || manga.MangaConnectorIds.Any(id => id.UseForDownload);
        if (monitored)
        {
            foreach (MangaConnectorId<Chapter> chapterId in newIds)
                chapterId.UseForDownload = true;
        }

        if (newIds.Count > 0)
            MangaContext.MangaConnectorToChapter.AddRange(newIds);

        manga.TryAttachExistingSeriesFolder();
        int alreadyOnDisk = 0;
        foreach (Chapter chapter in manga.Chapters)
        {
            chapter.ParentManga = manga;
            if (chapter.ApplyDownloadedMatch())
                alreadyOnDisk++;
        }
        if (alreadyOnDisk > 0)
            Log.InfoFormat("Recognized {0} existing archives on disk for {1}.", alreadyOnDisk, manga.Name);

        if(await MangaContext.Sync(CancellationToken, GetType(), "Chapters retrieved") is { success: false } mangaContextException)
            Log.ErrorFormat("Failed to save database changes: {0}", mangaContextException.exceptionMessage);

        ActionsContext.Actions.Add(new ChaptersRetrievedActionRecord(manga));
        if(await ActionsContext.Sync(CancellationToken, GetType(), "Chapters retrieved") is { success: false } actionsContextException)
            Log.ErrorFormat("Failed to save database changes: {0}", actionsContextException.exceptionMessage);

        return [];
    }

    public override string ToString() => $"{base.ToString()} {_mangaConnectorIdId}";
}
