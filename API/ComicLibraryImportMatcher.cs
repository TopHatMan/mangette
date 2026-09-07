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
    /// A folder's own name carries no series title at all for "Volume 01 (1940)", "Annuals", or
    /// "Extras" -- these are run/format descriptors that only make sense under a parent series
    /// folder (e.g. "Batman/Volume 01 (1940)/Annuals"). Confirmed live: leaf-only cleaning produced
    /// a bare "Volume 01" (identical, useless query) for two different Batman runs, and a bare
    /// "Annuals" for its annual issues -- neither contains the word "Batman" at all.
    /// </summary>
    private static readonly Regex GenericRunDescriptor = new(
        @"^(?:vol(?:ume)?s?\.?\s*\d*|v\d+|tpbs?|omnibus(?:es)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SpecialContentDescriptor = new(
        @"^(?:annuals?|specials?|extras?|giant-?sizes?|one-?shots?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Naive plural-to-singular for the small closed set <see cref="SpecialContentDescriptor"/> matches.</summary>
    private static string Singularize(string word) =>
        word.EndsWith('s') && !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ? word[..^1] : word;

    /// <summary>
    /// Builds the search query for one candidate folder from its whole path relative to the
    /// library root, instead of cleaning the leaf folder name in isolation. A leaf that already
    /// carries its own real title text (e.g. "Batman v1", "Batman Annuals 01-28") is used as-is --
    /// it's already more specific than anything an ancestor could add. Only when the leaf is
    /// PURELY a generic run/content descriptor with no title of its own (bare "Volume 01",
    /// "Annuals", "Extras") do we fall back to the closest ancestor folder that isn't itself
    /// another such descriptor, since otherwise the query loses all series-name context (confirmed
    /// live: two different runs both produced the bare, identical, useless query "Volume 01").
    /// A special-content leaf (Annual, Extra, Special, ...) gets its singular form appended to that
    /// ancestor, since that genuinely helps ComicVine find a distinct volume (e.g. "Batman Annual")
    /// instead of just the main run.
    /// </summary>
    public static string BuildSuggestedQuery(string relativePath)
    {
        string[] segments = NormalizeFolderKey(relativePath)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanSeriesName)
            .Where(s => s.Length > 0)
            .ToArray();
        if (segments.Length == 0)
            return "";

        string leaf = segments[^1];
        bool leafIsSpecial = SpecialContentDescriptor.IsMatch(leaf);
        if (!GenericRunDescriptor.IsMatch(leaf) && !leafIsSpecial)
            return leaf;
        if (segments.Length == 1)
            return leaf;

        string? baseName = null;
        for (int i = segments.Length - 2; i >= 0; i--)
        {
            if (!GenericRunDescriptor.IsMatch(segments[i]) && !SpecialContentDescriptor.IsMatch(segments[i]))
            {
                baseName = segments[i];
                break;
            }
        }
        baseName ??= segments[0];

        return leafIsSpecial ? $"{baseName} {Singularize(leaf)}" : baseName;
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
                unmapped.Add(new ComicScanCandidate(relative, archives, other, BuildSuggestedQuery(relative)));
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

    /// <summary>
    /// Counts files under <paramref name="directory"/>, but does NOT descend into a subdirectory
    /// that is itself a separate importable candidate (has its own direct archive and isn't an
    /// accessory folder) -- otherwise a hub folder like "Batman" holding "Volume 01 (1940)",
    /// "Volume 01 (1987)", etc. as siblings would report every one of their files as its OWN
    /// archive count too, wildly inflating it (confirmed live: a hub with 2 direct loose files and
    /// three separately-listed sub-runs reported 7, not 2). Each file is counted under exactly one
    /// candidate this way. Accessory folders (Covers/Variants) still roll their files up into the
    /// parent, since they're deliberately not offered as their own candidate.
    /// </summary>
    public static (int Archives, int Other) CountFiles(string directory)
    {
        try
        {
            int archives = 0, other = 0;
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                if (DownloadedChapterMatcher.IsArchive(path))
                    archives++;
                else
                    other++;
            }
            foreach (string sub in Directory.GetDirectories(directory))
            {
                string subName = Path.GetFileName(sub.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (LibraryImportMatcher.IsSkippableFolder(subName))
                    continue;
                if (HasDirectArchive(sub) && !IsAccessoryFolder(subName))
                    continue; // counted under its own separate candidate, not rolled up here
                (int subArchives, int subOther) = CountFiles(sub);
                archives += subArchives;
                other += subOther;
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
