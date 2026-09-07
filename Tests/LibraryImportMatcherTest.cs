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
    public void ScoreTitle_PenalizesMissingDistinguishingWord()
    {
        // "Mighty Morphin Power Rangers-Recharged" is a distinct reboot/spin-off, not just a
        // formatting variant of the plain ongoing "Mighty Morphin Power Rangers" -- the base
        // series must not casually out-score (or tie with) a real match on this folder.
        double baseScore = API.LibraryImportMatcher.ScoreTitle(
            "Mighty.Morphin.Power.Rangers-Recharged.", "Mighty Morphin Power Rangers");
        double exactScore = API.LibraryImportMatcher.ScoreTitle(
            "Mighty.Morphin.Power.Rangers-Recharged.", "Mighty Morphin Power Rangers: Recharged");

        Assert.True(exactScore > baseScore, $"expected exact ({exactScore}) > base ({baseScore})");
        Assert.True(baseScore < 90, $"expected the base (wrong) series to score below the auto-import threshold, got {baseScore}");
    }

    [Fact]
    public void ScoreTitle_FormatDescriptorDoesNotIncurMissingWordPenalty_UnlikeARealDistinguishingWord()
    {
        // "TPBs" is a format descriptor (trade paperback collections of the SAME series) and must
        // not be penalized the way a genuine distinguishing word is -- "Batman Beyond" is a real,
        // different spin-off series, so it should score lower against plain "Batman" than a same-
        // length-ish format-descriptor folder does.
        double tpbScore = API.LibraryImportMatcher.ScoreTitle("Batman TPBs", "Batman");
        double beyondScore = API.LibraryImportMatcher.ScoreTitle("Batman Beyond", "Batman");
        Assert.True(tpbScore > beyondScore, $"expected TPBs ({tpbScore}) > Beyond ({beyondScore})");
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

    [Theory]
    [InlineData(API.Schema.MangaContext.MangaReleaseStatus.Continuing, true)]
    [InlineData(API.Schema.MangaContext.MangaReleaseStatus.OnHiatus, true)]
    [InlineData(API.Schema.MangaContext.MangaReleaseStatus.Unreleased, true)]
    [InlineData(API.Schema.MangaContext.MangaReleaseStatus.Completed, false)]
    [InlineData(API.Schema.MangaContext.MangaReleaseStatus.Cancelled, false)]
    public void CheckForNewChapters_OnlyOngoing(API.Schema.MangaContext.MangaReleaseStatus status, bool expected)
    {
        Assert.Equal(expected, API.Workers.PeriodicWorkers.CheckForNewChaptersWorker.IsOngoing(status));
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
