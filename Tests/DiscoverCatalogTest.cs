using API.Schema.MangaContext;
using API.Schema.MangaContext.MetadataFetchers;
using Newtonsoft.Json.Linq;

namespace Tests;

public class DiscoverCatalogTest
{
    [Fact]
    public void ParseBrowseItem_ReadsTitleCoverAndKind()
    {
        JObject media = JObject.Parse("""
            {
              "id": 30013,
              "title": { "romaji": "One Piece", "english": "One Piece" },
              "description": "Pirates rule.",
              "status": "RELEASING",
              "format": "MANGA",
              "countryOfOrigin": "JP",
              "startDate": { "year": 1997 },
              "coverImage": { "large": "https://example.com/op.jpg" },
              "averageScore": 92,
              "popularity": 500000,
              "genres": ["Adventure", "Comedy"],
              "chapters": null,
              "siteUrl": "https://anilist.co/manga/30013"
            }
            """);

        AniList.BrowseItem? item = AniList.ParseBrowseItem(media);
        Assert.NotNull(item);
        Assert.Equal(30013, item.Value.Id);
        Assert.Equal("One Piece", item.Value.Name);
        Assert.Equal("Pirates rule.", item.Value.Description);
        Assert.Equal(1997u, item.Value.Year);
        Assert.Equal(MangaReleaseStatus.Continuing, item.Value.Status);
        Assert.Equal("Manga", item.Value.Kind);
        Assert.Equal(92, item.Value.AverageScore);
        Assert.Contains("Adventure", item.Value.Genres);
    }

    [Theory]
    [InlineData("KR", "MANGA", "Manhwa")]
    [InlineData("CN", "MANGA", "Manhua")]
    [InlineData("JP", "ONE_SHOT", "One-shot")]
    [InlineData("JP", "MANGA", "Manga")]
    public void KindFrom_MapsCountryAndFormat(string country, string format, string expected)
    {
        Assert.Equal(expected, AniList.KindFrom(country, format));
    }

    [Fact]
    public void MatchLibrary_UsesAniListLink()
    {
        List<API.DiscoverCatalog.LibraryTitle> library =
        [
            new("m1", "One Piece", [], [30013])
        ];
        (bool inLibrary, string? id) = API.DiscoverCatalog.MatchLibrary(30013, "One Piece", library);
        Assert.True(inLibrary);
        Assert.Equal("m1", id);
    }

    [Fact]
    public void MatchLibrary_MatchesCloseTitle()
    {
        List<API.DiscoverCatalog.LibraryTitle> library =
        [
            new("m1", "One Piece", ["ワンピース"], [])
        ];
        (bool inLibrary, string? id) = API.DiscoverCatalog.MatchLibrary(0, "One Piece", library);
        Assert.True(inLibrary);
        Assert.Equal("m1", id);
    }

    [Fact]
    public void MatchLibrary_IgnoresUnrelatedTitle()
    {
        List<API.DiscoverCatalog.LibraryTitle> library =
        [
            new("m1", "Naruto", [], [])
        ];
        (bool inLibrary, string? id) = API.DiscoverCatalog.MatchLibrary(99, "One Piece", library);
        Assert.False(inLibrary);
        Assert.Null(id);
    }
}
