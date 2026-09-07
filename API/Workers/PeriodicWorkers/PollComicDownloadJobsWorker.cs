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
/// finds every comic archive it produced -- one release can satisfy many issues at once (a
/// "complete run" download with hundreds of archives, not just the single issue that triggered the
/// search) -- matches each by its own parsed issue number to a wanted chapter of the series, moves
/// it into the library folder (named per <see cref="MangetteSettings.ChapterNamingScheme"/>,
/// preserving whatever archive format the release actually was), and marks that issue downloaded --
/// the same finish line <see cref="MangaDownloadWorkers.DownloadChapterFromMangaconnectorWorker"/>
/// reaches for scraped chapters.
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

    /// <summary>
    /// One grabbed release can contain far more than the single issue that triggered the search --
    /// a "complete run" NZB/torrent (e.g. all 713 Batman issues in one download) drops hundreds of
    /// archives in one output folder, not just <see cref="ComicDownloadJob.Chapter"/>'s one. Every
    /// archive found is matched by its own parsed issue number against every not-yet-downloaded
    /// chapter of the series, instead of assuming the output folder holds exactly one file for
    /// exactly the chapter that requested it.
    /// </summary>
    private async Task<bool> Import(ComicDownloadJob job, string outputPath)
    {
        Chapter primaryChapter = job.Chapter;
        if (primaryChapter.ParentManga.LibraryId is null)
        {
            job.MarkFailed("Series has no library.");
            return false;
        }

        List<string> sourceFiles = FindArchiveFiles(outputPath);
        if (sourceFiles.Count == 0)
        {
            job.MarkFailed($"No comic archive found in {outputPath}.");
            QueueLog.WarnFormat("\"{0}\" completed but no comic archive was found under {1}.", job.ReleaseTitle, outputPath);
            return false;
        }

        List<Chapter> wanted = await MangaContext.Chapters
            .Include(c => c.ParentManga)
            .ThenInclude(m => m.Library)
            .Where(c => c.ParentMangaId == primaryChapter.ParentMangaId && !c.Downloaded)
            .ToListAsync(CancellationToken);
        if (wanted.All(c => c.Key != primaryChapter.Key))
            wanted.Add(primaryChapter);

        (List<(string ChapterNumber, string SourceFile)> fileMatches, List<string> unmatched) =
            DownloadedChapterMatcher.MatchArchivesToIssues(
                sourceFiles.OrderByDescending(f => new FileInfo(f).Length), wanted.Select(c => c.ChapterNumber));

        int imported = 0;
        foreach ((string chapterNumber, string sourceFile) in fileMatches)
        {
            Chapter matched = wanted.First(c => c.ChapterNumber == chapterNumber);
            if (await ImportOneFile(matched, sourceFile))
                imported++;
            else
                unmatched.Add(sourceFile);
        }

        if (imported == 0)
        {
            job.MarkFailed($"None of the {sourceFiles.Count} archive(s) in {outputPath} matched a wanted issue for {primaryChapter.ParentManga.Name}.");
            return false;
        }

        job.MarkImported(outputPath);
        if (unmatched.Count > 0)
        {
            QueueLog.WarnFormat("\"{0}\": {1} of {2} archive(s) under {3} didn't match a wanted issue and were left in place: {4}",
                job.ReleaseTitle, unmatched.Count, sourceFiles.Count, outputPath, string.Join(", ", unmatched.Select(Path.GetFileName)));
        }
        QueueLog.InfoFormat("Imported {0} issue(s) from \"{1}\" for {2}.", imported, job.ReleaseTitle, primaryChapter.ParentManga.Name);
        return true;
    }

    /// <summary>Moves one matched archive into place and records it. Returns false (leaving the source file alone) on any I/O failure.</summary>
    private async Task<bool> ImportOneFile(Chapter chapter, string sourceFile)
    {
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
            QueueLog.Error($"Could not move \"{sourceFile}\" to \"{destinationFile}\": {ex.Message}", ex);
            return false;
        }

        chapter.Downloaded = true;
        chapter.FileName = new FileInfo(destinationFile).Name;

        // DataMoved records the raw source -> destination paths (same record type manga's own file
        // moves use); ChapterDownloaded is what the Activity page's default "Downloads" filter shows.
        // Together they give a full audit trail of exactly what got moved where -- useful for
        // catching a wrong-series import after the fact, not just trusting the match was right.
        await ActionsContext.Actions.AddAsync(new DataMovedActionRecord(sourceFile, destinationFile));
        await ActionsContext.Actions.AddAsync(new ChapterDownloadedActionRecord(chapter.ParentManga, chapter));
        if (await ActionsContext.Sync(CancellationToken, GetType(), "Comic issue imported") is { success: false } actionsResult)
            QueueLog.Error($"Failed to save action record: {actionsResult.exceptionMessage}");

        await NotificationsContext.Notifications.AddAsync(new Notification(
            "Issue downloaded",
            $"{chapter.ParentManga.Name} #{chapter.ChapterNumber} - {chapter.FileName}"), CancellationToken);
        if (await NotificationsContext.Sync(CancellationToken, GetType(), "Comic issue imported") is { success: false } notificationsResult)
            QueueLog.Error($"Failed to save notification: {notificationsResult.exceptionMessage}");

        return true;
    }

    private static List<string> FindArchiveFiles(string outputPath)
    {
        if (File.Exists(outputPath))
            return IsComicArchive(outputPath) ? [outputPath] : [];
        if (!Directory.Exists(outputPath))
            return [];

        try
        {
            return Directory.EnumerateFiles(outputPath, "*", SearchOption.AllDirectories)
                .Where(IsComicArchive)
                .ToList();
        }
        catch (Exception ex)
        {
            QueueLog.Error($"Could not scan {outputPath} for comic archives: {ex.Message}", ex);
            return [];
        }
    }

    private static bool IsComicArchive(string path) =>
        ComicArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
