using API.MangaConnectors;

namespace Tests;

public class WeebCentralParseTest
{
    [Fact]
    public void SearchResults_ParsesListWithoutFetchingEachSeries()
    {
        const string html = """
            <a href="https://weebcentral.com/series/01J76XY7E9FNDZ1DBBM6PBJPFK/One-Piece">
              <img src="https://temp.compsci88.com/cover/fallback/01J76XY7E9FNDZ1DBBM6PBJPFK.jpg" alt="cover">
              One Piece
            </a>
            <a href="/series/01J76XYAQSGEJPXCSCVPQ3MHZM/One-Piece-Digital-Colored-Comics">
              <img src="/cover/color.jpg">
              One Piece (Color)
            </a>
            <a href="https://weebcentral.com/series/01J76XY7E9FNDZ1DBBM6PBJPFK/One-Piece">duplicate</a>
            """;

        List<WeebCentralParse.SearchItem> items = WeebCentralParse.SearchResults(html);
        Assert.Equal(2, items.Count);
        Assert.Equal("01J76XY7E9FNDZ1DBBM6PBJPFK", items[0].Id);
        Assert.Contains("One Piece", items[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("01J76XYAQSGEJPXCSCVPQ3MHZM", items[1].Id);
        Assert.StartsWith("https://temp.compsci88.com/cover/color.jpg", items[1].CoverUrl);
    }

    [Fact]
    public void Chapters_DoesNotThrowWhenEmptyClassSpanIsMissing()
    {
        const string html = """
            <a href="/chapters/ch-one"><span class="grow flex items-center gap-2"><span>Chapter 1</span></span></a>
            <a href="https://weebcentral.com/chapters/ch-two"><span class="">Chapter 2.5</span></a>
            <a href="/chapters/ch-bad"><span class="grow">Prologue</span></a>
            <a href="/not-a-chapter">Chapter 9</a>
            """;

        List<WeebCentralParse.ChapterItem> items = WeebCentralParse.Chapters(html);
        Assert.Equal(2, items.Count);
        Assert.Equal("1", items[0].ChapterNumber);
        Assert.Equal("ch-one", items[0].ChapterId);
        Assert.Equal("2.5", items[1].ChapterNumber);
        Assert.Equal("https://weebcentral.com/chapters/ch-two", items[1].Url);
    }

    [Theory]
    [InlineData("Chapter 1100", "1100", null)]
    [InlineData("Ch. 12.5", "12.5", null)]
    [InlineData("Volume 3 Chapter 10", "10", 3)]
    public void TryParseChapterLabel_ReadsNumberAndVolume(string label, string number, int? volume)
    {
        Assert.True(WeebCentralParse.TryParseChapterLabel(label, out string parsed, out int? vol));
        Assert.Equal(number, parsed);
        Assert.Equal(volume, vol);
    }

    [Fact]
    public void LooksLikeCloudflare_DetectsChallengePage()
    {
        Assert.True(WeebCentral.LooksLikeCloudflare("Just a moment... cf-browser-verification challenge-platform"));
        Assert.True(WeebCentral.LooksLikeCloudflare(""));
        Assert.True(WeebCentral.LooksLikeCloudflare(null!));
        Assert.False(WeebCentral.LooksLikeCloudflare(new string('x', 200) + "<img alt=\"Page 1\" src=\"https://cdn.example/1.jpg\">"));
    }
}
