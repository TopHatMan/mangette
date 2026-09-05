using System.Text.RegularExpressions;

namespace API;

/// <summary>Comic-specific cleanup on top of <see cref="LibraryImportMatcher"/> for folder-name junk this library's audit showed.</summary>
public static class ComicLibraryImportMatcher
{
    // Runs after LibraryImportMatcher.CleanFolderQuery, which already turned '.' into a space --
    // so "GetComics.INFO" arrives here as "GetComics INFO", not with the literal dot.
    private static readonly Regex ComicJunkTokens = new(
        @"\b(getcomics(\s+info)?|tgx|torrentgalaxy)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Sort-order prefixes ("01-", "02 - ") people add so folders list in reading order
    /// (e.g. "01-Daredevil v1 (001-380+) (1964-1998)"). These carry no title information.
    /// </summary>
    private static readonly Regex SortPrefix = new(@"^\d{1,3}[\s._-]+", RegexOptions.Compiled);

    /// <summary>
    /// Folder names that are near-universally accessory content (cover scans, alternate covers)
    /// rather than a distinct series/run in their own right. Their archives still count toward
    /// their parent folder's recursive total in Scan; they're just not offered as their own
    /// importable candidate.
    /// </summary>
    private static readonly string[] AccessoryFolderNames = ["covers", "cover", "variant covers", "variants"];

    public static bool IsAccessoryFolder(string folderName) =>
        AccessoryFolderNames.Contains(folderName.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string CleanSeriesName(string folderName)
    {
        string cleaned = SortPrefix.Replace(folderName, "");
        cleaned = LibraryImportMatcher.CleanFolderQuery(cleaned);
        cleaned = ComicJunkTokens.Replace(cleaned, " ");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Walks the whole tree under <paramref name="root"/> and returns every directory that directly
    /// contains an archive as its own importable candidate (see <see cref="API.Controllers.ComicLibraryImportController"/>
    /// for why: a comic library is often a character/franchise hub folder holding many unrelated runs
    /// several levels deep, not one folder per series like manga).
    /// </summary>
    public static (List<ComicScanCandidate> Unmapped, int MappedCount) FindCandidates(string root, ISet<string> mappedRelativePaths)
    {
        List<ComicScanCandidate> unmapped = [];
        int mappedCount = 0;
        Walk(root, root, mappedRelativePaths, unmapped, ref mappedCount);
        return (unmapped, mappedCount);
    }

    private static void Walk(string root, string dir, ISet<string> mapped, List<ComicScanCandidate> unmapped, ref int mappedCount)
    {
        string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        bool isRoot = dir.Equals(root, StringComparison.OrdinalIgnoreCase);
        if (!isRoot && LibraryImportMatcher.IsSkippableFolder(name))
            return;

        if (!isRoot && HasDirectArchive(dir) && !IsAccessoryFolder(name))
        {
            string relative = NormalizeFolderKey(Path.GetRelativePath(root, dir));
            if (mapped.Contains(relative))
                mappedCount++;
            else
            {
                (int archives, int other) = CountFiles(dir);
                unmapped.Add(new ComicScanCandidate(relative, archives, other, CleanSeriesName(name)));
            }
        }

        string[] children;
        try
        {
            children = Directory.GetDirectories(dir);
        }
        catch
        {
            return;
        }
        foreach (string child in children)
            Walk(root, child, mapped, unmapped, ref mappedCount);
    }

    private static bool HasDirectArchive(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).Any(DownloadedChapterMatcher.IsArchive);
        }
        catch
        {
            return false;
        }
    }

    public static (int Archives, int Other) CountFiles(string directory)
    {
        try
        {
            int archives = 0, other = 0;
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (DownloadedChapterMatcher.IsArchive(path))
                    archives++;
                else
                    other++;
            }
            return (archives, other);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>Normalizes a relative path's separators so folder keys compare consistently across platforms.</summary>
    public static string NormalizeFolderKey(string name) =>
        name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}

/// <summary>One importable folder found by <see cref="ComicLibraryImportMatcher.FindCandidates"/>.</summary>
public sealed record ComicScanCandidate(string RelativePath, int ArchiveCount, int OtherFileCount, string SuggestedQuery);
