using System.Diagnostics.CodeAnalysis;
using API.MangaConnectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.MangaContext;
using API.Schema.NotificationsContext;
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
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private NotificationsContext NotificationsContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
        ActionsContext = GetContext<ActionsContext>(serviceScope);
        NotificationsContext = GetContext<NotificationsContext>(serviceScope);
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
        Log.DebugFormat("Got {0} chapters from connector (library had {1}).", allChapters.Length, manga.Chapters.Count);
        manga.LastNewChapterCheck = DateTime.UtcNow;

        List<MangaConnectorId<Chapter>> newIds = [];
        int reusedChapters = 0;
        List<Chapter> priorCatalog = manga.Chapters.ToList();
        List<Chapter> newlyPublished = [];

        foreach ((Chapter incomingChapter, MangaConnectorId<Chapter> incomingId) in allChapters)
        {
            Chapter? existing = manga.Chapters.FirstOrDefault(c => c.IsSameLogicalChapter(incomingChapter));
            Chapter target = existing ?? incomingChapter;
            if (existing is not null)
            {
                reusedChapters++;
                existing.ApplyCatalogDetails(incomingChapter.VolumeNumber, incomingChapter.Title);
                incomingId.Obj = existing;
                incomingId.ObjId = existing.Key;
            }
            else
            {
                if (Chapter.IsNewlyPublished(incomingChapter, priorCatalog))
                {
                    incomingChapter.NewRelease = true;
                    newlyPublished.Add(incomingChapter);
                }
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

        Log.InfoFormat("{0} chapter list from {1}: {2} total, {3} reused, {4} new source links.",
            manga.Name, mangaConnector.Name, manga.Chapters.Count, reusedChapters, newIds.Count);

        if (manga.Monitored && mangaConnectorId.UseForDownload)
        {
            foreach (MangaConnectorId<Chapter> chapterId in newIds)
                chapterId.UseForDownload = true;
            // Chapter rows reused from an earlier scan keep UseForDownload=false unless we flip them here.
            int enabledExisting = 0;
            foreach (Chapter chapter in manga.Chapters)
            {
                foreach (MangaConnectorId<Chapter> existingId in chapter.MangaConnectorIds)
                {
                    if (!existingId.MangaConnectorName.Equals(mangaConnectorId.MangaConnectorName, StringComparison.OrdinalIgnoreCase) ||
                        existingId.UseForDownload)
                        continue;
                    existingId.UseForDownload = true;
                    enabledExisting++;
                }
            }
            if (enabledExisting > 0)
                Log.InfoFormat("Turned on downloads for {0} existing {1} chapter links on {2}.", enabledExisting, mangaConnector.Name, manga.Name);
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

        if (newlyPublished.Count > 0)
        {
            Log.InfoFormat("{0}: {1} newly published chapter(s) {2}.",
                manga.Name,
                newlyPublished.Count,
                string.Join(", ", newlyPublished.Select(c => $"Ch.{c.ChapterNumber}")));
            List<Chapter> alreadyGot = newlyPublished.Where(c => c.Downloaded).ToList();
            if (alreadyGot.Count > 0)
                await AnnounceNewChapters(manga, alreadyGot);
        }

        return [];
    }

    private async Task AnnounceNewChapters(Manga manga, IReadOnlyList<Chapter> chapters)
    {
        foreach (Chapter chapter in chapters)
        {
            chapter.ParentManga = manga;
            await ActionsContext.Actions.AddAsync(new NewChapterActionRecord(manga, chapter), CancellationToken);
            await NotificationsContext.Notifications.AddAsync(
                new Notification("New chapter", Chapter.NotifyText(manga, chapter), NotificationUrgency.High),
                CancellationToken);
        }
        if (await ActionsContext.Sync(CancellationToken, GetType(), "New chapter") is { success: false } actionsEx)
            Log.ErrorFormat("Failed to save new-chapter activity: {0}", actionsEx.exceptionMessage);
        if (await NotificationsContext.Sync(CancellationToken, GetType(), "New chapter") is { success: false } notesEx)
            Log.ErrorFormat("Failed to save new-chapter notification: {0}", notesEx.exceptionMessage);
    }

    public override string ToString() => $"{base.ToString()} {_mangaConnectorIdId}";
}
