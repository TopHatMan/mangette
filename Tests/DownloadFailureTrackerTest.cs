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
    [InlineData("MangaDex", 0)]
    [InlineData("AsuraComic", 1)]
    [InlineData("Mangaworld", 2)]
    [InlineData("WeebCentral", 3)]
    [InlineData("Global", 4)]
    [InlineData("UnknownSite", 4)]
    public void Rank_MatchesPreferenceOrder(string connector, int expectedRank)
    {
        Assert.Equal(expectedRank, DownloadFailureTracker.Rank(connector));
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
    public void ThreeEmptyImageFailures_CoolsWholeConnector()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        DownloadFailureTracker.RecordFailure("ch-1", "WeebCentral", "No imageUrls");
        DownloadFailureTracker.RecordFailure("ch-2", "WeebCentral", "No imageUrls");
        Assert.False(DownloadFailureTracker.IsConnectorCoolingDown("WeebCentral"));

        DownloadFailureTracker.RecordFailure("ch-3", "WeebCentral", "No imageUrls");
        Assert.True(DownloadFailureTracker.IsConnectorCoolingDown("WeebCentral"));
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
        Assert.Contains(selected, s => s.MangaConnectorName == "MangaDex" && s.Obj.ChapterNumber == "5");
        Assert.DoesNotContain(selected, s => s.MangaConnectorName == "WeebCentral");
        Assert.Contains(selected, s => s.MangaConnectorName == "AsuraComic" && s.Obj.ChapterNumber == "6");
    }

    [Fact]
    public void SelectDownloadSources_SkipsCoolingDownSourceAndUsesFailover()
    {
        DateTime now = DateTime.UtcNow;
        DownloadFailureTracker.UtcNow = () => now;

        MangaConnectorId<Chapter> weeb = Source("One Piece", "5", "WeebCentral", "wc-5");
        MangaConnectorId<Chapter> dex = Source("One Piece", "5", "MangaDex", "md-5");

        DownloadFailureTracker.RecordFailure(dex.Key, "MangaDex", "timeout");

        List<MangaConnectorId<Chapter>> selected = DownloadFailureTracker.SelectDownloadSources(
            [weeb, dex],
            [],
            [],
            take: 1);

        Assert.Single(selected);
        Assert.Equal("WeebCentral", selected[0].MangaConnectorName);
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
