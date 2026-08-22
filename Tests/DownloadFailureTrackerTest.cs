using API.MangaDownloadClients;
using API.Schema.MangaContext;

namespace Tests;

public class DownloadFailureTrackerTest : IDisposable
{
    public DownloadFailureTrackerTest()
    {
        DownloadFailureTracker.Reset();
    }

    public void Dispose()
    {
        DownloadFailureTracker.Reset();
    }

    [Theory]
    [InlineData("WeebCentral", 0)]
    [InlineData("NeloManga", 1)]
    [InlineData("MangaTown", 2)]
    [InlineData("FanFox", 3)]
    [InlineData("AsuraComic", 4)]
    [InlineData("Global", 5)]
    [InlineData("UnknownSite", 5)]
    public void Rank_MatchesPreferenceOrder(string connector, int expectedRank)
    {
        Assert.Equal(expectedRank, DownloadFailureTracker.Rank(connector));
    }

    [Fact]
    public void SetPreferenceOrder_MakesFirstConnectorWin()
    {
        DownloadFailureTracker.SetPreferenceOrder(["WeebCentral", "FanFox"]);
        Assert.Equal(0, DownloadFailureTracker.Rank("WeebCentral"));
        Assert.Equal(1, DownloadFailureTracker.Rank("FanFox"));
        Assert.True(DownloadFailureTracker.Rank("NeloManga") > DownloadFailureTracker.Rank("FanFox"));
    }

    [Fact]
    public void RecordFailure_CoolsDownThatChapterSource()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral", "No imageUrls");

        Assert.True(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
        Assert.False(DownloadFailureTracker.IsCoolingDown("ch-2", "WeebCentral"));
        Assert.False(DownloadFailureTracker.IsConnectorCoolingDown("WeebCentral"));
    }

    [Fact]
    public void RecordFailure_ExponentialBackoff_ThenExpires()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral");
        Assert.True(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));

        now = now.AddMinutes(29);
        Assert.True(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));

        now = now.AddMinutes(2);
        Assert.False(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));

        DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral");
        now = now.AddMinutes(59);
        Assert.True(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
        now = now.AddMinutes(2);
        Assert.False(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
    }

    [Fact]
    public void RecordFailure_CapsCooldownAtSixHours()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        for (int i = 0; i < 10; i++)
            DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral");

        now = now.AddHours(5).AddMinutes(59);
        Assert.True(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
        now = now.AddMinutes(2);
        Assert.False(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
    }

    [Fact]
    public void RecordSuccess_ClearsChapterAndConnectorCooldown()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral", "403 Cloudflare");
        Assert.True(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
        Assert.True(DownloadFailureTracker.IsConnectorCoolingDown("WeebCentral"));

        DownloadFailureTracker.RecordSuccess("ch-1", "WeebCentral");
        Assert.False(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
        Assert.False(DownloadFailureTracker.IsConnectorCoolingDown("WeebCentral"));
    }

    [Fact]
    public void ConnectorWideFailure_CoolsWholeConnectorImmediately()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral", "Probably your IP is banned for this site");

        Assert.True(DownloadFailureTracker.IsConnectorCoolingDown("WeebCentral"));
        Assert.False(DownloadFailureTracker.IsConnectorCoolingDown("MangaDex"));
    }

    [Fact]
    public void ThreeEmptyImageFailures_DoNotCoolWholeConnector()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral", "No imageUrls");
        DownloadFailureTracker.RecordFailure("ch-2", "WeebCentral", "No imageUrls");
        DownloadFailureTracker.RecordFailure("ch-3", "WeebCentral", "No imageUrls");
        Assert.False(DownloadFailureTracker.IsConnectorCoolingDown("WeebCentral"));
        Assert.True(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
    }

    [Fact]
    public void CloudflareChallenge_CoolsWholeConnectorImmediately()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral", "WeebCentral Cloudflare challenge after HTTP and Chromium");

        Assert.True(DownloadFailureTracker.IsConnectorCoolingDown("WeebCentral"));
        Assert.True(DownloadFailureTracker.IsCoolingDown("ch-1", "WeebCentral"));
    }

    [Fact]
    public void SelectDownloadSources_PicksPreferredConnectorPerChapter()
    {
        MangaConnectorId<Chapter> weeb = Source("One Piece", "5", "WeebCentral", "wc-5");
        MangaConnectorId<Chapter> dex = Source("One Piece", "5", "MangaDex", "md-5");
        MangaConnectorId<Chapter> asura = Source("One Piece", "6", "AsuraComic", "as-6");

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(
            [weeb, dex, asura],
            [],
            [],
            take: 10);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, s => s.MangaConnectorName == "WeebCentral" && s.Obj.ChapterNumber == "5");
        Assert.DoesNotContain(selected, s => s.MangaConnectorName == "MangaDex");
        Assert.Contains(selected, s => s.MangaConnectorName == "AsuraComic" && s.Obj.ChapterNumber == "6");
    }

    [Fact]
    public void SelectDownloadSources_SkipsCoolingDownSourceAndUsesFailover()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        MangaConnectorId<Chapter> weeb = Source("One Piece", "5", "WeebCentral", "wc-5");
        MangaConnectorId<Chapter> dex = Source("One Piece", "5", "MangaDex", "md-5");

        DownloadFailureTracker.RecordFailure(weeb.Key, "WeebCentral", "timeout");

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(
            [weeb, dex],
            [],
            [],
            take: 1);

        Assert.Single(selected);
        Assert.Equal("MangaDex", selected[0].MangaConnectorName);
    }

    [Fact]
    public void SelectDownloadSources_SkipsInFlightChapterEntirely()
    {
        MangaConnectorId<Chapter> weeb = Source("One Piece", "5", "WeebCentral", "wc-5");
        MangaConnectorId<Chapter> dex = Source("One Piece", "5", "MangaDex", "md-5");

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(
            [weeb, dex],
            [],
            [dex.ObjId],
            take: 10);

        Assert.Empty(selected);
    }

    [Fact]
    public void SelectDownloadSources_RespectsTakeLimit()
    {
        List<MangaConnectorId<Chapter>> missing =
        [
            Source("A", "1", "MangaDex", "a1"),
            Source("B", "1", "MangaDex", "b1"),
            Source("C", "1", "MangaDex", "c1")
        ];

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(missing, [], [], take: 2);
        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void SelectDownloadSources_SharesSlotsAcrossSeriesInsteadOfLowestChapterGlobally()
    {
        List<MangaConnectorId<Chapter>> missing =
        [
            Source("Detective Conan", "1", "WeebCentral", "conan-1"),
            Source("Detective Conan", "2", "WeebCentral", "conan-2"),
            Source("Detective Conan", "3", "WeebCentral", "conan-3"),
            Source("One Piece", "1090", "WeebCentral", "op-1090"),
        ];

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(missing, [], [], take: 2);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, s => s.Obj.ParentManga.Name == "Detective Conan");
        Assert.Contains(selected, s => s.Obj.ParentManga.Name == "One Piece");
    }

    [Fact]
    public void SelectDownloadSources_OneChapterPerSeriesAzBeforeRepeating()
    {
        List<MangaConnectorId<Chapter>> missing =
        [
            Source("Conan", "1", "WeebCentral", "c1"),
            Source("Conan", "2", "WeebCentral", "c2"),
            Source("Baki", "1", "WeebCentral", "b1"),
            Source("Baki", "2", "WeebCentral", "b2"),
            Source("One Piece", "1", "WeebCentral", "o1"),
            Source("One Piece", "2", "WeebCentral", "o2"),
        ];

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(missing, [], [], take: 3);

        Assert.Equal(["Baki", "Conan", "One Piece"], selected.Select(s => s.Obj.ParentManga.Name).ToList());
        Assert.All(selected, s => Assert.Equal("1", s.Obj.ChapterNumber));
    }

    [Fact]
    public void SelectDownloadSources_RepeatsASeriesOnlyAfterEverySeriesHadATurn()
    {
        List<MangaConnectorId<Chapter>> missing =
        [
            Source("Baki", "1", "WeebCentral", "b1"),
            Source("Baki", "2", "WeebCentral", "b2"),
            Source("Conan", "1", "WeebCentral", "c1"),
        ];

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(missing, [], [], take: 3);

        Assert.Equal("Baki", selected[0].Obj.ParentManga.Name);
        Assert.Equal("Conan", selected[1].Obj.ParentManga.Name);
        Assert.Equal("Baki", selected[2].Obj.ParentManga.Name);
        Assert.Equal("2", selected[2].Obj.ChapterNumber);
    }

    [Fact]
    public void SelectDownloadSources_PrefersSeriesWithFewerInFlightDownloads()
    {
        MangaConnectorId<Chapter> conanNext = Source("Detective Conan", "4", "WeebCentral", "conan-4");
        MangaConnectorId<Chapter> imported = Source("One Piece", "1090", "WeebCentral", "op-1090");
        Dictionary<string, int> inFlight = new()
        {
            [DownloadFailureTracker.SeriesKey(conanNext)] = 3,
        };

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(
            [conanNext, imported],
            [],
            [],
            take: 1,
            inFlight);

        Assert.Single(selected);
        Assert.Equal("One Piece", selected[0].Obj.ParentManga.Name);
    }

    [Fact]
    public void IsSameLogicalChapter_TreatsEquivalentNumbersAsOneChapter()
    {
        Manga manga = NewManga("Same");
        Chapter a = new(manga, "1", 1);
        Chapter b = new(manga, "1.0", 1);
        Assert.True(a.IsSameLogicalChapter(b));
        Assert.True(b.IsSameLogicalChapter(a));
    }

    [Fact]
    public void IsSameLogicalChapter_DifferentNumbersAreNotEqual()
    {
        Manga manga = NewManga("Same");
        Chapter a = new(manga, "1", 1);
        Chapter b = new(manga, "2", 1);
        Assert.False(a.IsSameLogicalChapter(b));
    }

    private static Manga NewManga(string name) =>
        new(name, "d", "https://example.com/c.jpg", MangaReleaseStatus.Continuing, [], [], [], []);

    private static MangaConnectorId<Chapter> Source(string mangaName, string chapterNumber, string connector, string siteId)
    {
        Manga manga = NewManga(mangaName);
        Chapter chapter = new(manga, chapterNumber, null);
        manga.Chapters.Add(chapter);
        return new MangaConnectorId<Chapter>(chapter, connector, siteId, "https://example.com/ch");
    }
}
