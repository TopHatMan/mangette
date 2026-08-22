using System.IO.Compression;
using System.Text.RegularExpressions;
using Soenneker.Utils.String.NeedlemanWunsch;

namespace API;

/// <summary>
/// Matches existing archives on disk to logical chapters so recovered libraries
/// (old Tranga folders, Komga-style names, padded numbers) are not re-downloaded.
/// </summary>
public static class DownloadedChapterMatcher
{
    private static readonly string[] ArchiveExtensions = [".cbz", ".zip", ".cbr", ".cb7"];

    private static readonly Regex ChapterToken = new(
        @"(?:^|[^\p{L}\d])(?:ch(?:apter)?|c)[\s._-]*(\d+(?:\.\d+)*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VolumeToken = new(
        @"(?:^|[^\p{L}\d])(?:vol(?:ume)?s?|v)[\s._-]*(\d+(?:\.\d+)*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BareNumberFile = new(
        @"^0*(\d+(?:\.\d+)*)\.(?:cbz|zip|cbr|cb7)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string NormalizeChapterNumber(string chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(chapterNumber))
            return "";
        string[] parts = chapterNumber.Split('.', StringSplitOptions.RemoveEmptyEntries);
        List<int> numbers = [];
        foreach (string part in parts)
        {
            if (!int.TryParse(part, out int value))
                return chapterNumber.Trim();
            numbers.Add(value);
        }
        while (numbers.Count > 1 && numbers[^1] == 0)
            numbers.RemoveAt(numbers.Count - 1);
        return numbers.Count == 0 ? "0" : string.Join('.', numbers);
    }

    public static bool ChapterNumbersEqual(string left, string right) =>
        NormalizeChapterNumber(left).Equals(NormalizeChapterNumber(right), StringComparison.Ordinal);

    public static bool TryParseArchiveNumbers(string fileName, out string? chapterNumber, out int? volumeNumber)
    {
        chapterNumber = null;
        volumeNumber = null;
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string name = Path.GetFileName(fileName);
        Match volume = VolumeToken.Match(name);
        if (volume.Success && int.TryParse(volume.Groups[1].Value.Split('.')[0], out int vol))
            volumeNumber = vol;

        Match chapter = ChapterToken.Match(name);
        if (chapter.Success)
        {
            chapterNumber = NormalizeChapterNumber(chapter.Groups[1].Value);
            return chapterNumber.Length > 0 || volumeNumber is not null;
        }

        if (volumeNumber is not null)
            return true;

        Match bare = BareNumberFile.Match(name);
        if (bare.Success)
        {
            chapterNumber = NormalizeChapterNumber(bare.Groups[1].Value);
            return chapterNumber.Length > 0;
        }

        return false;
    }

    public static bool TryParseChapterNumber(string fileName, out string chapterNumber)
    {
        chapterNumber = "";
        if (!TryParseArchiveNumbers(fileName, out string? chapter, out int? volume))
            return false;
        if (!string.IsNullOrEmpty(chapter))
        {
            chapterNumber = chapter;
            return true;
        }
        if (volume is not null)
        {
            chapterNumber = volume.Value.ToString();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Volume-only archives (Vol.01.cbz) match volume-listed rows, and also every chapter
    /// that belongs to that volume so a volume dump is not re-downloaded as hundreds of chapters.
    /// </summary>
    public static bool ArchiveCoversChapter(string fileName, string chapterNumber, int? volumeNumber)
    {
        if (!TryParseArchiveNumbers(fileName, out string? fileChapter, out int? fileVolume))
            return false;

        string want = NormalizeChapterNumber(chapterNumber);
        if (!string.IsNullOrEmpty(fileChapter))
            return fileChapter.Equals(want, StringComparison.Ordinal);

        if (fileVolume is null)
            return false;

        if (volumeNumber is not null && volumeNumber == fileVolume)
            return true;
        return want.Equals(fileVolume.Value.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Empty or tiny files are never treated as downloaded. Opening every zip (inspectZip)
    /// is only for an explicit Wanted scan — a startup pass over a large library would
    /// block the UI, especially on a failing disk.
    /// </summary>
    public static bool IsUsableArchive(string path, bool inspectZip = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;
        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch
        {
            return false;
        }
        if (info.Length < 32)
            return false;

        if (!inspectZip)
            return true;
        string ext = info.Extension;
        if (ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return ZipLooksReadable(path);
        return true;
    }

    public static bool TryQuarantineCorrupt(string path)
    {
        try
        {
            string dest = path + ".corrupt";
            int n = 2;
            while (File.Exists(dest))
                dest = path + $".corrupt{n++}";
            File.Move(path, dest);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? FindExistingChapterFile(
        string seriesDirectory,
        string chapterNumber,
        string? expectedFileName,
        bool exactNameOnly = false,
        int? volumeNumber = null,
        List<string>? quarantinedFiles = null,
        bool inspectZip = false)
    {
        if (string.IsNullOrWhiteSpace(seriesDirectory) || !Directory.Exists(seriesDirectory))
            return null;

        string want = NormalizeChapterNumber(chapterNumber);
        if (want.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(expectedFileName))
        {
            string expectedPath = Path.Combine(seriesDirectory, expectedFileName);
            string? accepted = AcceptArchive(seriesDirectory, expectedPath, quarantinedFiles, inspectZip);
            if (accepted is not null)
                return accepted;
        }

        List<string> archives = [];
        try
        {
            foreach (string path in Directory.EnumerateFiles(seriesDirectory, "*", SearchOption.AllDirectories))
            {
                if (IsArchive(path))
                    archives.Add(path);
            }
        }
        catch (Exception)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(expectedFileName))
        {
            string expectedName = Path.GetFileName(expectedFileName);
            string? exact = archives.FirstOrDefault(path =>
                Path.GetFileName(path).Equals(expectedName, StringComparison.OrdinalIgnoreCase));
            string? acceptedExact = exact is null ? null : AcceptArchive(seriesDirectory, exact, quarantinedFiles, inspectZip);
            if (acceptedExact is not null)
                return acceptedExact;
        }

        if (exactNameOnly)
            return null;

        List<(string Path, int Score)> ranked = [];
        foreach (string path in archives)
        {
            string name = Path.GetFileName(path);
            if (!ArchiveCoversChapter(name, chapterNumber, volumeNumber))
                continue;

            int score = 0;
            TryParseArchiveNumbers(name, out string? fileChapter, out _);
            if (!string.IsNullOrEmpty(fileChapter))
                score += 50;
            if (!string.IsNullOrWhiteSpace(expectedFileName) &&
                name.Equals(Path.GetFileName(expectedFileName), StringComparison.OrdinalIgnoreCase))
                score += 100;
            score += Math.Max(0, 40 - Path.GetRelativePath(seriesDirectory, path).Count(c => c is '/' or '\\'));
            ranked.Add((path, score));
        }

        foreach ((string path, int _) in ranked.OrderByDescending(r => r.Score))
        {
            string? accepted = AcceptArchive(seriesDirectory, path, quarantinedFiles, inspectZip);
            if (accepted is not null)
                return accepted;
        }

        return null;
    }

    private static bool ZipLooksReadable(string path)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => !string.IsNullOrEmpty(e.Name) && e.Length > 0);
        }
        catch
        {
            return false;
        }
    }

    private static string? AcceptArchive(string seriesDirectory, string path, List<string>? quarantinedFiles, bool inspectZip)
    {
        if (!File.Exists(path))
            return null;
        if (IsUsableArchive(path, inspectZip))
            return ToRelative(seriesDirectory, path);
        if (TryQuarantineCorrupt(path))
            quarantinedFiles?.Add(Path.GetFileName(path));
        return null;
    }

    public static string? FindSeriesFolder(string libraryRoot, string directoryName, string mangaName, IEnumerable<string>? altTitles)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            return null;

        string expectedPath = Path.Combine(libraryRoot, directoryName);
        if (Directory.Exists(expectedPath))
            return directoryName;

        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase) { directoryName };
        AddClean(candidates, mangaName);
        if (altTitles is not null)
        {
            foreach (string title in altTitles)
                AddClean(candidates, title);
        }

        string[] folders;
        try
        {
            folders = Directory.GetDirectories(libraryRoot);
        }
        catch
        {
            return null;
        }

        foreach (string folder in folders)
        {
            string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (candidates.Contains(name))
                return name;
            if (candidates.Any(c => FoldersLookLikeSameSeries(c, name)))
                return name;
        }

        string expected = directoryName;
        string? similar = null;
        double best = 0;
        foreach (string folder in folders)
        {
            string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            double score = NeedlemanWunschStringUtil.CalculateSimilarityPercentage(expected, name);
            if (score > best)
            {
                best = score;
                similar = name;
            }
        }

        if (similar is not null && best >= 96 && Math.Abs(similar.Length - expected.Length) <= 8)
            return similar;
        return null;
    }

    private static bool FoldersLookLikeSameSeries(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return true;
        string na = StripQualifier(a);
        string nb = StripQualifier(b);
        if (na.Equals(nb, StringComparison.OrdinalIgnoreCase) && na.Length >= 3)
            return true;
        return false;
    }

    private static string StripQualifier(string name)
    {
        int paren = name.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0)
            name = name[..paren];
        return name.Trim();
    }

    private static void AddClean(HashSet<string> set, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        string cleaned = value.CleanNameForWindows();
        if (cleaned.Length > 0)
            set.Add(cleaned);
    }

    private static bool IsArchive(string path)
    {
        string ext = Path.GetExtension(path);
        return ArchiveExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToRelative(string seriesDirectory, string fullPath)
    {
        string relative = Path.GetRelativePath(seriesDirectory, fullPath);
        return relative.Replace('/', Path.DirectorySeparatorChar);
    }
}
