using System.Diagnostics.CodeAnalysis;
using API.ExternalDownloadClients;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.MangaContext;
using API.Schema.NotificationsContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// Polls every in-flight <see cref="ComicDownloadJob"/> for completion. When a client reports done,
/// finds the comic archive it produced, moves it into the series' library folder (named per
/// <see cref="MangetteSettings.ChapterNamingScheme"/>, preserving whatever archive format the release
/// actually was), and marks the issue downloaded -- the same finish line
/// <see cref="MangaDownloadWorkers.DownloadChapterFromMangaconnectorWorker"/> reaches for scraped chapters.
/// </summary>
public class PollComicDownloadJobsWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    public TimeSpan Interval { get; set; } = interval ?? TimeSpan.FromSeconds(30);
    private static readonly ILog QueueLog = LogManager.GetLogger(typeof(PollComicDownloadJobsWorker));

    private static readonly string[] ComicArchiveExtensions = [".cbz", ".cbr", ".cb7", ".pdf", ".zip"];

    private static readonly IExternalDownloadClient QBittorrent = new QBittorrentDownloadClient();
    private static readonly IExternalDownloadClient Sabnzbd = new SabnzbdDownloadClient();

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
        List<ComicDownloadJob> downloading = await MangaContext.ComicDownloadJobs
            .Include(j => j.Chapter)
            .ThenInclude(c => c.ParentManga)
            .ThenInclude(m => m.Library)
            .Where(j => j.Status == ComicDownloadJobStatus.Downloading)
            .ToListAsync(CancellationToken);

        bool refreshLibrary = false;
        foreach (ComicDownloadJob job in downloading)
        {
            IExternalDownloadClient client = job.ClientName == DownloadClientKind.QBittorrent ? QBittorrent : Sabnzbd;
            if (job.ExternalId is not { } externalId)
            {
                job.MarkFailed("Job has no external id.");
                continue;
            }

            ExternalDownloadStatus status;
            try
            {
                status = await client.GetStatus(externalId, CancellationToken);
            }
            catch (Exception ex)
            {
                QueueLog.Error($"Checking status of \"{job.ReleaseTitle}\" on {job.ClientName} failed: {ex.Message}", ex);
                continue;
            }

            if (status.Failed)
            {
                job.MarkFailed(status.ErrorMessage ?? "Download failed.");
                QueueLog.WarnFormat("\"{0}\" failed on {1}: {2}", job.ReleaseTitle, job.ClientName, job.ErrorMessage);
                continue;
            }
            if (!status.Done)
            {
                job.SetProgress(status.Progress);
                continue;
            }
            job.SetProgress(1);
            if (status.OutputPath is not { } outputPath)
            {
                job.MarkFailed($"{job.ClientName} reported completion but gave no output path.");
                continue;
            }

            if (await Import(job, outputPath))
                refreshLibrary = true;
        }

        if (await MangaContext.Sync(CancellationToken, GetType(), "Poll comic downloads") is { success: false } result)
            QueueLog.Error($"Failed to save comic download job updates: {result.exceptionMessage}");

        return refreshLibrary ? [new RefreshLibrariesWorker()] : [];
    }

    private async Task<bool> Import(ComicDownloadJob job, string outputPath)
    {
        Chapter chapter = job.Chapter;
        string? sourceFile = FindArchiveFile(outputPath);
        if (sourceFile is null)
        {
            job.MarkFailed($"No comic archive found in {outputPath}.");
            QueueLog.WarnFormat("\"{0}\" completed but no comic archive was found under {1}.", job.ReleaseTitle, outputPath);
            return false;
        }
        if (chapter.ParentManga.LibraryId is null)
        {
            job.MarkFailed("Series has no library.");
            return false;
        }

        string destinationFile = Path.Join(chapter.ParentManga.FullDirectoryPath, chapter.GetArchiveFileName(Path.GetExtension(sourceFile)));
        try
        {
            Directory.CreateDirectory(chapter.ParentManga.FullDirectoryPath);
            if (File.Exists(destinationFile))
                File.Delete(destinationFile);
            File.Move(sourceFile, destinationFile);
        }
        catch (Exception ex)
        {
            job.MarkFailed($"Could not move \"{sourceFile}\" to \"{destinationFile}\": {ex.Message}");
            QueueLog.Error($"Import of \"{job.ReleaseTitle}\" failed: {ex.Message}", ex);
            return false;
        }

        job.MarkImported(destinationFile);
        chapter.Downloaded = true;
        chapter.FileName = new FileInfo(destinationFile).Name;

        await ActionsContext.Actions.AddAsync(new ChapterDownloadedActionRecord(chapter.ParentManga, chapter));
        if (await ActionsContext.Sync(CancellationToken, GetType(), "Comic issue imported") is { success: false } actionsResult)
            QueueLog.Error($"Failed to save action record: {actionsResult.exceptionMessage}");

        await NotificationsContext.Notifications.AddAsync(new Notification(
            "Issue downloaded",
            $"{chapter.ParentManga.Name} #{chapter.ChapterNumber} - {chapter.FileName}"), CancellationToken);
        if (await NotificationsContext.Sync(CancellationToken, GetType(), "Comic issue imported") is { success: false } notificationsResult)
            QueueLog.Error($"Failed to save notification: {notificationsResult.exceptionMessage}");

        QueueLog.InfoFormat("Imported \"{0}\" as {1} #{2}.", job.ReleaseTitle, chapter.ParentManga.Name, chapter.ChapterNumber);
        return true;
    }

    private static string? FindArchiveFile(string outputPath)
    {
        if (File.Exists(outputPath))
            return IsComicArchive(outputPath) ? outputPath : null;
        if (!Directory.Exists(outputPath))
            return null;

        try
        {
            return Directory.EnumerateFiles(outputPath, "*", SearchOption.AllDirectories)
                .Where(IsComicArchive)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            QueueLog.Error($"Could not scan {outputPath} for a comic archive: {ex.Message}", ex);
            return null;
        }
    }

    private static bool IsComicArchive(string path) =>
        ComicArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
