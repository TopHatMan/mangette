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
    public void IsAccessoryFolder_MatchesKnownAccessoryNames(string folder)
    {
        Assert.True(API.ComicLibraryImportMatcher.IsAccessoryFolder(folder));
    }

    [Fact]
    public void IsAccessoryFolder_DoesNotMatchRealSeriesNames()
    {
        Assert.False(API.ComicLibraryImportMatcher.IsAccessoryFolder("Captain Universe (001-005) (2006) (digital)"));
    }
}
