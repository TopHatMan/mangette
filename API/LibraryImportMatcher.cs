using System.Text.RegularExpressions;
using Soenneker.Utils.String.NeedlemanWunsch;

namespace API;

public static class LibraryImportMatcher
{
    private static readonly Regex BracketBlock = new(@"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex JunkTokens = new(
        @"\b(digital|omnibus|complete|scan|scans|rarbg|nyaa)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WordSplit = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    // Words that describe a format/collection type rather than a distinguishing part of a title --
    // excluded only from the missing-word title-match check below, not from CleanFolderQuery's
    // output (which stays visible as-is in the UI's editable "suggested query" field).
    private static readonly HashSet<string> NonDistinguishingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "of", "and", "&", "tpb", "tpbs", "hc", "hardcover", "omnibus", "digital"
    };

    public static string CleanFolderQuery(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return "";
        string name = folderName.Replace('_', ' ').Replace('.', ' ');
        name = BracketBlock.Replace(name, " ");
        name = JunkTokens.Replace(name, " ");
        name = Regex.Replace(name, @"\s+", " ").Trim();
        return name;
    }

    public static double ScoreTitle(string folderName, string title)
    {
        string folder = CleanFolderQuery(folderName);
        string series = CleanFolderQuery(title);
        if (folder.Length == 0 || series.Length == 0)
            return 0;
        if (folder.Equals(series, StringComparison.OrdinalIgnoreCase))
            return 100;
        if (folder.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase))
            return 99;

        double score = NeedlemanWunschStringUtil.CalculateSimilarityPercentage(folder, series);

        // A folder like "Mighty Morphin Power Rangers-Recharged" scoring high against a candidate
        // titled plain "Mighty Morphin Power Rangers" would silently match/import the wrong (base,
        // unrelated) series -- character-level edit distance alone can't tell "a reboot/spin-off
        // dropped a distinguishing word" apart from "a formatting variant of the same title". Any
        // significant word the folder asks for that the candidate's title doesn't have at all is a
        // strong signal this candidate is a different work, so it costs real score -- enough that a
        // genuinely matching, more specific candidate (if ComicVine has it) wins instead.
        string[] folderWords = SignificantWords(folder);
        HashSet<string> seriesWords = new(SignificantWords(series), StringComparer.OrdinalIgnoreCase);
        int missing = folderWords.Count(w => !seriesWords.Contains(w));
        return Math.Max(0, score - missing * 25);
    }

    private static string[] SignificantWords(string text) =>
        WordSplit.Split(text).Where(w => w.Length > 0 && !NonDistinguishingWords.Contains(w)).ToArray();

    public static bool IsSkippableFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return true;
        if (folderName.StartsWith('.'))
            return true;
        return folderName is "@eaDir" or "lost+found" or "#recycle" or "$RECYCLE.BIN" or "System Volume Information";
    }

    /// <summary>
    /// Windows services run as LocalSystem and cannot see user-mapped drives (Z:\). UNC paths work.
    /// </summary>
    public static string? LibraryPathWarning(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || root[1] != ':')
            return null;
        try
        {
            DriveInfo drive = new(root[..1]);
            if (!drive.IsReady)
                return $"Drive {root[..2]} is not available to Mangette. Mapped network drives are invisible to the Windows service. Use a UNC path like \\\\server\\share\\Manga.";
            if (drive.DriveType == DriveType.Network)
                return $"Library is on a network drive ({root[..2]}). If Scan finds 0 folders, use a UNC path instead of a mapped letter.";
        }
        catch (Exception ex)
        {
            return $"Cannot inspect {root[..2]}: {ex.Message}";
        }
        return null;
    }
}
