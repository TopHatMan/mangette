using API;
using API.IndexerConnectors;
using API.Schema.MangaContext;

namespace Tests;

public class ComicAcquisitionTest
{
    private static Manga NewComic(string name) =>
        new(name, "", "", MangaReleaseStatus.Continuing, [], [], [], []) { Kind = MediaKind.Comic };

    private static IndexerRelease Release(string title, ReleaseProtocol protocol = ReleaseProtocol.Usenet, int? seeders = null) =>
        new(title, "https://example.com/download", null, protocol, "TestIndexer", 1024, DateTime.UtcNow, seeders);

    [Fact]
    public void BuildQuery_IsSeriesNameAlone_NotNameAndIssue()
    {
        Manga comic = NewComic("Absolute Superman");
        Assert.Equal("Absolute Superman", ComicAcquisition.BuildQuery(comic));
    }

    [Theory]
    [InlineData("Absolute Superman 001 (2025) (Digital).cbz", "1", true)]
    [InlineData("Absolute Superman 002 (2025) (Digital).cbz", "1", false)]
    [InlineData("Absolute Superman #3.cbz", "3", true)]
    public void MatchesIssue_ParsesIssueNumberFromReleaseTitle(string title, string chapterNumber, bool expected)
    {
        Manga comic = NewComic("Absolute Superman");
        Chapter chapter = new(comic, chapterNumber, null);
        Assert.Equal(expected, ComicAcquisition.MatchesIssue(Release(title), chapter));
    }

    [Fact]
    public void PickBestForIssue_PrefersConfiguredProtocolAmongMatchingIssue()
    {
        Manga comic = NewComic("Absolute Superman");
        Chapter chapter = new(comic, "1", null);
        IndexerRelease[] releases =
        [
            Release("Absolute Superman 002 (2025).cbz", ReleaseProtocol.Torrent), // wrong issue
            Release("Absolute Superman 001 (2025) (Digital).cbz", ReleaseProtocol.Torrent),
            Release("Absolute Superman 001 (2025) (Usenet-Release)", ReleaseProtocol.Usenet),
        ];

        IndexerRelease? chosen = ComicAcquisition.PickBestForIssue(releases, chapter, ReleaseProtocol.Usenet);

        Assert.NotNull(chosen);
        Assert.Equal(ReleaseProtocol.Usenet, chosen!.Protocol);
    }

    [Fact]
    public void PickBestForIssue_FallsBackToOtherProtocolWhenPreferredHasNoMatch()
    {
        Manga comic = NewComic("Absolute Superman");
        Chapter chapter = new(comic, "1", null);
        IndexerRelease[] releases = [Release("Absolute Superman 001 (2025) (Digital).cbz", ReleaseProtocol.Torrent)];

        IndexerRelease? chosen = ComicAcquisition.PickBestForIssue(releases, chapter, ReleaseProtocol.Usenet);

        Assert.NotNull(chosen);
        Assert.Equal(ReleaseProtocol.Torrent, chosen!.Protocol);
    }

    [Fact]
    public void PickBestForIssue_ReturnsNullWhenNothingMatchesTheIssue()
    {
        Manga comic = NewComic("Absolute Superman");
        Chapter chapter = new(comic, "1", null);
        IndexerRelease[] releases = [Release("Absolute Superman 002 (2025).cbz")];

        Assert.Null(ComicAcquisition.PickBestForIssue(releases, chapter, ReleaseProtocol.Usenet));
    }
}
