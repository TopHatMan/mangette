namespace Tests;

public class LibraryImportMatcherTest
{
    [Theory]
    [InlineData("One Piece", "One Piece")]
    [InlineData("One Piece (EN)", "One Piece")]
    [InlineData("One_Piece", "One Piece")]
    [InlineData("[Group] One Piece", "One Piece")]
    public void CleanFolderQuery_StripsJunk(string folder, string expected)
    {
        Assert.Equal(expected, API.LibraryImportMatcher.CleanFolderQuery(folder));
    }

    [Fact]
    public void ScoreTitle_ExactFolderIs100()
    {
        Assert.Equal(100, API.LibraryImportMatcher.ScoreTitle("One Piece (EN)", "One Piece"));
    }

    [Fact]
    public void IsSkippableFolder_HidesSystemDirs()
    {
        Assert.True(API.LibraryImportMatcher.IsSkippableFolder("@eaDir"));
        Assert.True(API.LibraryImportMatcher.IsSkippableFolder(".git"));
        Assert.False(API.LibraryImportMatcher.IsSkippableFolder("One Piece"));
    }
}
