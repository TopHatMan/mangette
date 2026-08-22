namespace API;

/// <summary>Sonarr-style Wanted → Manual Import: map leftover archives to missing chapters.</summary>
public static class ManualImport
{
    public const int MaxFiles = 400;

    public sealed record SeriesInfo(string Id, string Name, string DirectoryName, string? LibraryPath);

    public sealed record ChapterInfo(string Id, string Number, int? Volume, bool Downloaded, string? FileName);

    public sealed record FileGuess(
        string Path,
        string FileName,
        long Size,
        string? MangaId,
        string? MangaName,
        string? ChapterId,
        string? ChapterNumber,
        int? Volume,
        double Score);

    public static IEnumerable<string> EnumerateArchives(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        int count = 0;
        foreach (string path in files)
        {
            if (count >= MaxFiles)
                yield break;
            if (path.Contains(".corrupt", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!DownloadedChapterMatcher.IsArchive(path))
                continue;
            count++;
            yield return path;
        }
    }

    public static FileGuess Guess(
        string path,
        IReadOnlyList<SeriesInfo> series,
        IReadOnlyDictionary<string, List<ChapterInfo>> chaptersByManga,
        HashSet<string> claimedPaths)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            full = path;
        }

        long size = 0;
        try
        {
            size = new FileInfo(full).Length;
        }
        catch
        {
            /* ignore */
        }

        string fileName = Path.GetFileName(full);
        if (claimedPaths.Contains(full))
            return new FileGuess(full, fileName, size, null, null, null, null, null, 0);

        string folder = Path.GetFileName(Path.GetDirectoryName(full) ?? "") ?? "";
        SeriesInfo? bestSeries = null;
        double bestScore = 0;
        foreach (SeriesInfo s in series)
        {
            double folderScore = LibraryImportMatcher.ScoreTitle(folder, s.Name);
            double dirScore = LibraryImportMatcher.ScoreTitle(folder, s.DirectoryName);
            double score = Math.Max(folderScore, dirScore);
            if (score > bestScore)
            {
                bestScore = score;
                bestSeries = s;
            }
        }

        DownloadedChapterMatcher.TryParseArchiveNumbers(fileName, out string? chapterNumber, out int? volume);
        ChapterInfo? chapter = null;
        if (bestSeries is not null && bestScore >= 60 && chaptersByManga.TryGetValue(bestSeries.Id, out List<ChapterInfo>? chapters))
        {
            IEnumerable<ChapterInfo> missing = chapters.Where(c => !c.Downloaded);
            if (!string.IsNullOrEmpty(chapterNumber))
            {
                chapter = missing.FirstOrDefault(c =>
                    DownloadedChapterMatcher.ChapterNumbersEqual(c.Number, chapterNumber));
            }
            if (chapter is null && volume is not null)
            {
                chapter = missing.FirstOrDefault(c => c.Volume == volume &&
                    (string.IsNullOrEmpty(chapterNumber) ||
                     DownloadedChapterMatcher.ChapterNumbersEqual(c.Number, volume.Value.ToString())));
            }
        }

        return new FileGuess(
            full,
            fileName,
            size,
            bestScore >= 60 ? bestSeries?.Id : null,
            bestScore >= 60 ? bestSeries?.Name : null,
            chapter?.Id,
            chapter?.Number ?? chapterNumber,
            chapter?.Volume ?? volume,
            Math.Round(bestScore, 1));
    }

    public static HashSet<string> ClaimedArchivePaths(IEnumerable<SeriesInfo> series, IReadOnlyDictionary<string, List<ChapterInfo>> chaptersByManga)
    {
        HashSet<string> claimed = new(StringComparer.OrdinalIgnoreCase);
        foreach (SeriesInfo s in series)
        {
            if (string.IsNullOrWhiteSpace(s.LibraryPath) || !chaptersByManga.TryGetValue(s.Id, out List<ChapterInfo>? chapters))
                continue;
            string seriesDir;
            try
            {
                seriesDir = Path.GetFullPath(Path.Combine(s.LibraryPath, s.DirectoryName));
            }
            catch
            {
                continue;
            }
            foreach (ChapterInfo ch in chapters)
            {
                if (string.IsNullOrWhiteSpace(ch.FileName))
                    continue;
                try
                {
                    claimed.Add(Path.GetFullPath(Path.Combine(seriesDir, ch.FileName)));
                }
                catch
                {
                    /* ignore */
                }
            }
        }
        return claimed;
    }
}
