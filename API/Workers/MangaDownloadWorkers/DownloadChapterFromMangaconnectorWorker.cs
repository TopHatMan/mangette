using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using API.MangaConnectors;
using API.MangaDownloadClients;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.MangaContext;
using API.Schema.NotificationsContext;
using API.Workers.PeriodicWorkers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Binarization;
using static System.IO.UnixFileMode;

namespace API.Workers.MangaDownloadWorkers;

/// <summary>
/// Downloads single chapter for Manga from Mangaconnector
/// </summary>
/// <param name="chId"></param>
/// <param name="dependsOn"></param>
public class DownloadChapterFromMangaconnectorWorker(MangaConnectorId<Chapter> chId, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn)
{
    public readonly string ChapterIdId = chId.Key;
    public readonly string ChapterKey = chId.ObjId;

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
        Log.Debug($"Downloading chapter for MangaConnectorId {ChapterIdId}...");
        // Getting MangaConnector info
        if (await MangaContext.MangaConnectorToChapter
                .Include(id => id.Obj)
                .ThenInclude(c => c.ParentManga)
                .ThenInclude(m => m.Library)
                .FirstOrDefaultAsync(c => c.Key == ChapterIdId, CancellationToken) is not { } mangaConnectorId)
        {
            Log.Error("Could not get MangaConnectorId.");
            Fail();
            return [];
        }
        
        // Check if Chapter already exists...
        if (await mangaConnectorId.Obj.CheckDownloaded(MangaContext, CancellationToken))
        {
            Log.Warn("Chapter already exists!");
            DownloadFailureTracker.RecordSuccess(mangaConnectorId.Key, mangaConnectorId.MangaConnectorName);
            return [];
        }
        
        if (!Mangette.TryGetMangaConnector(mangaConnectorId.MangaConnectorName, out MangaConnector? mangaConnector))
        {
            return FailDownload(mangaConnectorId.MangaConnectorName, "Could not get MangaConnector.");
        }
        
        Log.Debug($"Downloading chapter for MangaConnectorId {mangaConnectorId}...");
        
        Chapter chapter = mangaConnectorId.Obj;
        if (chapter.ParentManga.LibraryId is null)
        {
            Log.Info($"Library is not set for {chapter.ParentManga} {chapter}");
            Fail();
            return [];
        }
        
        Log.Info($"Getting imageUrls for chapter {chapter}");
        string[] imageUrls;
        try
        {
            imageUrls = mangaConnector.GetChapterImageUrls(mangaConnectorId);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return FailDownload(mangaConnectorId.MangaConnectorName, ex.Message);
        }
        if (imageUrls.Length < 1)
        {
            return FailDownload(mangaConnectorId.MangaConnectorName, $"No imageUrls for chapter {chapter}");
        }

        if (chapter.FullArchiveFilePath is not { } saveArchiveFilePath)
        {
            return FailDownload(mangaConnectorId.MangaConnectorName, "Failed getting saveArchiveFilePath");
        }
        Log.Debug($"Chapter path: {saveArchiveFilePath}");
        
        //Check if Publication Directory already exists
        string? directoryPath = Path.GetDirectoryName(saveArchiveFilePath);
        if (directoryPath is null)
        {
            return FailDownload(mangaConnectorId.MangaConnectorName, $"Directory path could not be found: {saveArchiveFilePath}");
        }
        if (!Directory.Exists(directoryPath))
        {
            Log.Info($"Creating publication Directory: {directoryPath}");
            Directory.CreateDirectory(directoryPath);
        }

        Log.Info($"Downloading images: {chapter}");
        string tempDir = Path.Join(Mangette.Settings.TempDownloadPath, chapter.Key.CleanNameForWindows());
        Directory.CreateDirectory(tempDir);
        List<string> imageFiles = [];
        try
        {
            for (int i = 0; i < imageUrls.Length; i++)
            {
                string imageUrl = imageUrls[i];
                try
                {
                    if (await mangaConnector.DownloadImage(imageUrl, CancellationToken) is not { } stream)
                        return FailDownload(mangaConnectorId.MangaConnectorName, $"Failed to download image: {imageUrl}");
                    await using Stream processed = await ProcessImage(stream, CancellationToken);
                    string dest = Path.Join(tempDir, $"{i:D3}.jpg");
                    await using FileStream file = File.Create(dest);
                    processed.Position = 0;
                    await processed.CopyToAsync(file, CancellationToken);
                    imageFiles.Add(dest);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    return FailDownload(mangaConnectorId.MangaConnectorName, ex.Message);
                }
            }

            await CopyCoverFromCacheToDownloadLocation(chapter.ParentManga);

            Log.Debug($"Loading collections {chapter}");
            foreach (CollectionEntry collectionEntry in MangaContext.Entry(chapter.ParentManga).Collections)
                await collectionEntry.LoadAsync(CancellationToken);

            if (File.Exists(saveArchiveFilePath))
            {
                Log.Info($"Archive {saveArchiveFilePath} already existed, overwriting.");
                File.Delete(saveArchiveFilePath);
            }

            Log.Debug($"Creating archive: {saveArchiveFilePath}");
            using ZipArchive archive = ZipFile.Open(saveArchiveFilePath, ZipArchiveMode.Create);

            if (Constants.CreateComicInfoXml)
            {
                Log.Debug("Writing ComicInfo.xml");
                Stream comicStream = archive.CreateEntry("ComicInfo.xml").Open();
                string comicInfo = chapter.GetComicInfoXmlString();
                await comicStream.WriteAsync(Encoding.UTF8.GetBytes(comicInfo), CancellationToken);
                await comicStream.DisposeAsync();
            }
            else
                Log.Debug("Skipping ComicInfo.xml. CREATE_COMICINFO_XML is set to false");

            for (int i = 0; i < imageFiles.Count; i++)
            {
                Log.Debug($"Packaging images to archive {chapter} , image {i}");
                await using FileStream imageStream = File.OpenRead(imageFiles[i]);
                Stream zipStream = archive.CreateEntry($"{i}.jpg").Open();
                await imageStream.CopyToAsync(zipStream, CancellationToken);
                await zipStream.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return FailDownload(mangaConnectorId.MangaConnectorName, ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                Log.Debug($"Could not clean temp download folder {tempDir}: {ex.Message}");
            }
        }

        DownloadFailureTracker.RecordSuccess(mangaConnectorId.Key, mangaConnectorId.MangaConnectorName);
        chapter.Downloaded = true;
        chapter.FileName = new FileInfo(saveArchiveFilePath).Name;
        if(await MangaContext.Sync(CancellationToken, GetType(), "Downloading complete") is { success: false } chapterContextException)
            Log.Error($"Failed to save database changes: {chapterContextException.exceptionMessage}");
        
        Log.Debug($"Downloaded chapter {chapter}.");

        await ActionsContext.Actions.AddAsync(new ChapterDownloadedActionRecord(chapter.ParentManga, chapter));
        if(await ActionsContext.Sync(CancellationToken, GetType(), "Download complete") is { success: false } actionsContextException)
            Log.Error($"Failed to save database changes: {actionsContextException.exceptionMessage}");

        await NotificationsContext.Notifications.AddAsync(new Notification(
            "Chapter downloaded",
            $"{chapter.ParentManga.Name} Ch. {chapter.ChapterNumber} - {chapter.FileName}"
            ), CancellationToken);
        if(await NotificationsContext.Sync(CancellationToken, GetType(), "Download complete") is { success: false } notificationsContextException)
            Log.Error($"Failed to save database changes: {notificationsContextException.exceptionMessage}");

        bool refreshLibrary = await CheckLibraryRefresh();
        if(refreshLibrary)
            Log.Info($"Condition {Mangette.Settings.LibraryRefreshSetting} met.");

        return refreshLibrary? [new RefreshLibrariesWorker()] : [];
    }

    private BaseWorker[] FailDownload(string connectorName, string reason)
    {
        Log.Error(reason);
        DownloadFailureTracker.RecordFailure(ChapterIdId, connectorName, reason);
        Fail();
        return [];
    }

    private async Task<bool> CheckLibraryRefresh() => Mangette.Settings.LibraryRefreshSetting switch
    {
        LibraryRefreshSetting.AfterAllFinished => await AllDownloadsFinished(),
        LibraryRefreshSetting.AfterMangaFinished => await MangaContext.MangaConnectorToChapter.Include(chId => chId.Obj).Where(chId => chId.UseForDownload).AllAsync(chId => chId.Obj.Downloaded, CancellationToken),
        LibraryRefreshSetting.AfterEveryChapter => true,
        LibraryRefreshSetting.WhileDownloading => await AllDownloadsFinished() ||  DateTime.UtcNow.Subtract(RefreshLibrariesWorker.LastRefresh).TotalMinutes > Mangette.Settings.RefreshLibraryWhileDownloadingEveryMinutes,
        _ => true
    };
    private async Task<bool> AllDownloadsFinished() => (await StartNewChapterDownloadsWorker.GetMissingChapters(MangaContext, CancellationToken)).Count == 0;
    
    private async Task<Stream> ProcessImage(Stream imageStream, CancellationToken? cancellationToken = null)
    {
        Log.Debug("Processing image");
        imageStream.Position = 0;
        if (!Mangette.Settings.BlackWhiteImages && Mangette.Settings.ImageCompression == 100)
        {
            Log.Debug("No processing requested for image");
            return imageStream;
        }

        MemoryStream processedImage = new ();
        try
        {
            using Image image = await Image.LoadAsync(imageStream, cancellationToken ?? CancellationToken.None);
            Log.Debug("Image loaded");
            if (Mangette.Settings.BlackWhiteImages)
                image.Mutate(i => i.ApplyProcessor(new AdaptiveThresholdProcessor()));
            await image.SaveAsJpegAsync(processedImage, new JpegEncoder()
            {
                Quality = Mangette.Settings.ImageCompression
            });
            Log.Debug("Image processed");
            processedImage.Position = 0;
            return processedImage;
        }
        catch (Exception e)
        {
            if (e is UnknownImageFormatException or NotSupportedException)
            {
                //If the Image-Format is not processable by ImageSharp, we can't modify it.
                Log.Debug("Unable to process image: Not supported image format");
            }else if (e is InvalidImageContentException)
            {
                Log.Debug("Unable to process image: Invalid Content");
            }
            else
            {
                Log.Error(e);
            }
            await imageStream.CopyToAsync(processedImage);
            processedImage.Position = 0;
            return processedImage;
        }
    }
    
    private async Task CopyCoverFromCacheToDownloadLocation(Manga manga)
    {
        Log.Debug($"Copying cover for {manga}");

        manga = await MangaContext.MangaWithMetadata().Include(m => m.MangaConnectorIds).FirstAsync(m => m.Key == manga.Key, CancellationToken);
        string publicationFolder;
        try
        {
            Log.Debug("Checking Manga directory exists...");
            //Check if Publication already has a Folder and cover
            publicationFolder = manga.FullDirectoryPath;

            Log.Debug("Checking cover already exists...");
            DirectoryInfo dirInfo = new(publicationFolder);
            if (dirInfo.EnumerateFiles()
                .Any(info => info.Name.Contains("cover", StringComparison.InvariantCultureIgnoreCase)))
            {
                Log.Debug($"Cover already exists at {publicationFolder}");
                return;
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
            return;
        }

        if (manga.CoverFileNameInCache is not { } coverFileNameInCache)
        {
            MangaConnectorId<Manga> mangaConnectorId = manga.MangaConnectorIds.First();
            if (!Mangette.TryGetMangaConnector(mangaConnectorId.MangaConnectorName, out MangaConnector? mangaConnector))
            {
                Log.Error($"MangaConnector with name {mangaConnectorId.MangaConnectorName} could not be found");
                return;
            }
            
            coverFileNameInCache = mangaConnector.SaveCoverImageToCache(mangaConnectorId);
            manga.CoverFileNameInCache = coverFileNameInCache;
            if (await MangaContext.Sync(CancellationToken, reason: "Update cover filename") is { success: false } result)
                Log.Error($"Couldn't update cover filename {result.exceptionMessage}");
        }
        if (coverFileNameInCache is null)
        {
            Log.Error($"File {coverFileNameInCache} does not exist and failed to download cover");
            return;
        }
        
        string fullCoverPath = Path.Join(MangetteSettings.CoverImageCacheOriginal, coverFileNameInCache);
        string newFilePath = Path.Join(publicationFolder, $"cover.{Path.GetFileName(coverFileNameInCache).Split('.')[^1]}" );
        File.Copy(fullCoverPath, newFilePath, true);
        Log.Debug($"Copied cover from {fullCoverPath} to {newFilePath}");
    }

    public override string ToString() => $"{base.ToString()} {ChapterIdId}";
}