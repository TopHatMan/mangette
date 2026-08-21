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

    public static bool TryParseChapterNumber(string fileName, out string chapterNumber)
    {
        chapterNumber = "";
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string name = Path.GetFileName(fileName);
        Match chapter = ChapterToken.Match(name);
        if (chapter.Success)
        {
            chapterNumber = NormalizeChapterNumber(chapter.Groups[1].Value);
            return chapterNumber.Length > 0;
        }

        Match bare = BareNumberFile.Match(name);
        if (bare.Success)
        {
            chapterNumber = NormalizeChapterNumber(bare.Groups[1].Value);
            return chapterNumber.Length > 0;
        }

        return false;
    }

    public static string? FindExistingChapterFile(
        string seriesDirectory,
        string chapterNumber,
        string? expectedFileName,
        bool exactNameOnly = false)
    {
        if (string.IsNullOrWhiteSpace(seriesDirectory) || !Directory.Exists(seriesDirectory))
            return null;

        string want = NormalizeChapterNumber(chapterNumber);
        if (want.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(expectedFileName))
        {
            string expectedPath = Path.Combine(seriesDirectory, expectedFileName);
            if (File.Exists(expectedPath))
                return ToRelative(seriesDirectory, expectedPath);
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
            if (exact is not null)
                return ToRelative(seriesDirectory, exact);
        }

        if (exactNameOnly)
            return null;

        string? best = null;
        int bestScore = -1;
        foreach (string path in archives)
        {
            if (!TryParseChapterNumber(Path.GetFileName(path), out string parsed) ||
                !parsed.Equals(want, StringComparison.Ordinal))
                continue;

            int score = 0;
            if (!string.IsNullOrWhiteSpace(expectedFileName) &&
                Path.GetFileName(path).Equals(Path.GetFileName(expectedFileName), StringComparison.OrdinalIgnoreCase))
                score += 100;
            score += Math.Max(0, 40 - Path.GetRelativePath(seriesDirectory, path).Count(c => c is '/' or '\\'));
            if (score > bestScore)
            {
                bestScore = score;
                best = path;
            }
        }

        return best is null ? null : ToRelative(seriesDirectory, best);
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
