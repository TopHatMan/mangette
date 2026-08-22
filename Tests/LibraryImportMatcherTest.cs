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

public class AuthCryptoTest
{
    [Fact]
    public void HashAndVerify_RoundTrip()
    {
        string hash = API.AuthCrypto.HashPassword("hunter2");
        Assert.True(API.AuthCrypto.VerifyPassword("hunter2", hash));
        Assert.False(API.AuthCrypto.VerifyPassword("wrong", hash));
        Assert.False(API.AuthCrypto.VerifyPassword("hunter2", null));
    }
}

public class FlareSolverrUrlTest
{
    [Theory]
    [InlineData("192.168.1.210:8191", "http://192.168.1.210:8191")]
    [InlineData("http://192.168.1.210:8191/", "http://192.168.1.210:8191")]
    [InlineData("http://192.168.1.210:8191/v1", "http://192.168.1.210:8191/v1")]
    [InlineData("", "")]
    public void NormalizeFlareSolverrUrl_AddsSchemeAndTrimsSlash(string input, string expected)
    {
        Assert.Equal(expected, API.MangetteSettings.NormalizeFlareSolverrUrl(input));
    }
}
