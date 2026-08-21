using System.Text.RegularExpressions;
using Soenneker.Utils.String.NeedlemanWunsch;

namespace API;

public static class LibraryImportMatcher
{
    private static readonly Regex BracketBlock = new(@"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex JunkTokens = new(
        @"\b(digital|omnibus|complete|scan|scans|rarbg|nyaa)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        return NeedlemanWunschStringUtil.CalculateSimilarityPercentage(folder, series);
    }

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
