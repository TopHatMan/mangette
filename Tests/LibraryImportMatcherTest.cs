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
    public void ComicInfo_IncludesSeriesForKomga()
    {
        API.Schema.MangaContext.Manga manga = new(
            "One Piece",
            "Pirates",
            "https://example.com/c.jpg",
            API.Schema.MangaContext.MangaReleaseStatus.Continuing,
            [new API.Schema.MangaContext.Author("Eiichiro Oda")],
            [new API.Schema.MangaContext.MangaTag("Adventure")],
            [],
            [],
            null,
            0f,
            1997,
            "ja");
        API.Schema.MangaContext.Chapter chapter = new(manga, "1", 1, "Romance Dawn");
        string xml = chapter.GetComicInfoXmlString();
        Assert.Contains("<Series>One Piece</Series>", xml);
        Assert.Contains("<Number>1</Number>", xml);
        Assert.Contains("<Volume>1</Volume>", xml);
        Assert.Contains("<Title>Romance Dawn</Title>", xml);
        Assert.Contains("<Manga>Yes</Manga>", xml);
        Assert.Contains("<Year>1997</Year>", xml);
        Assert.Contains("<Writer>Eiichiro Oda</Writer>", xml);
        Assert.Contains("<Genre>Adventure</Genre>", xml);
        Assert.Contains("<LanguageISO>ja</LanguageISO>", xml);
        Assert.Contains("<Summary>Pirates</Summary>", xml);
    }

    [Fact]
    public void AniList_MapStatus_CoversReleasingAndFinished()
    {
        Assert.Equal(API.Schema.MangaContext.MangaReleaseStatus.Continuing, API.Schema.MangaContext.MetadataFetchers.AniList.MapStatus("RELEASING"));
        Assert.Equal(API.Schema.MangaContext.MangaReleaseStatus.Completed, API.Schema.MangaContext.MetadataFetchers.AniList.MapStatus("FINISHED"));
        Assert.Equal(API.Schema.MangaContext.MangaReleaseStatus.OnHiatus, API.Schema.MangaContext.MetadataFetchers.AniList.MapStatus("HIATUS"));
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
