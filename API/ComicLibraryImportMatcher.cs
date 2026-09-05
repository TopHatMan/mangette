using System.Text.RegularExpressions;

namespace API;

/// <summary>Comic-specific cleanup on top of <see cref="LibraryImportMatcher"/> for folder-name junk this library's audit showed.</summary>
public static class ComicLibraryImportMatcher
{
    private static readonly Regex ComicJunkTokens = new(
        @"\b(getcomics(\.info)?|tgx|torrentgalaxy)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CleanSeriesName(string folderName)
    {
        string cleaned = LibraryImportMatcher.CleanFolderQuery(folderName);
        cleaned = ComicJunkTokens.Replace(cleaned, " ");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
}
