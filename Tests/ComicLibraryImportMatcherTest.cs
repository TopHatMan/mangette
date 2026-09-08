namespace Tests;

public class ComicLibraryImportMatcherTest
{
    [Theory]
    [InlineData("01-Daredevil v1 (001-380+) (1964-1998)", "Daredevil v1")]
    [InlineData("02-Daredevil v2 (Ω-119,500-512+) (1998-2011) (digital)", "Daredevil v2")]
    [InlineData("Adventures in the DCU (001-019+Annual) (1997-1998) GetComics.INFO", "Adventures in the DCU")]
    [InlineData("Captain Universe (001-005) (2006) (digital)", "Captain Universe")]
    [InlineData("Batman TPBs", "Batman TPBs")]
    public void CleanSeriesName_StripsSortPrefixAndComicJunk(string folder, string expected)
    {
        Assert.Equal(expected, API.ComicLibraryImportMatcher.CleanSeriesName(folder));
    }

    [Theory]
    [InlineData("Covers")]
    [InlineData("covers")]
    [InlineData("Variant Covers")]
    [InlineData("variants")]
    // Real folder names pulled directly from a 1,376-folder library audit -- an exact-match list
    // would need updating for every new phrasing an uploader happens to use.
    [InlineData("c2c scans")]
    [InlineData("scans with extra covers")]
    [InlineData("scans w. xtra covers")]
    [InlineData("related variants")]
    public void IsAccessoryFolder_MatchesKnownAccessoryNames(string folder)
    {
        Assert.True(API.ComicLibraryImportMatcher.IsAccessoryFolder(folder));
    }

    [Fact]
    public void IsAccessoryFolder_DoesNotMatchRealSeriesNames()
    {
        Assert.False(API.ComicLibraryImportMatcher.IsAccessoryFolder("Captain Universe (001-005) (2006) (digital)"));
    }

    [Theory]
    // Real wrapper-folder names from the same audit -- pure wrapper terms (Trades, Mini-Series,
    // Spin-offs+, Motion Comics) fall back to the ancestor's name with no suffix appended, unlike
    // Annual/Extra/Special which append a singularized suffix (see BuildSuggestedQuery).
    [InlineData("Ant-Man/Trades", "Ant-Man")]
    [InlineData("Doctor Strange/Mini-Series", "Doctor Strange")]
    [InlineData("Fantastic Four/Spin-offs+", "Fantastic Four")]
    [InlineData("Daredevil/Motion Comics", "Daredevil")]
    [InlineData("Ant-Man/One-Shots+", "Ant-Man One-Shot")]
    public void BuildSuggestedQuery_HandlesRealAuditWrapperFolders(string relativePath, string expected)
    {
        Assert.Equal(expected, API.ComicLibraryImportMatcher.BuildSuggestedQuery(relativePath));
    }
}
