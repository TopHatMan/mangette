using API.Schema.MangaContext;

namespace Tests;

public class ChapterCatalogTest
{
    [Fact]
    public void ApplyCatalogDetails_FillsBlankTitleAndVolume()
    {
        Manga manga = new("T", "d", "https://example.com/c.jpg", MangaReleaseStatus.Continuing, [], [], [], []);
        Chapter chapter = new(manga, "12", null);
        Assert.Equal(2, chapter.ApplyCatalogDetails(3, "The Raid"));
        Assert.Equal(3, chapter.VolumeNumber);
        Assert.Equal("The Raid", chapter.Title);
    }

    [Fact]
    public void ApplyCatalogDetails_DoesNotOverwriteRealTitle()
    {
        Manga manga = new("T", "d", "https://example.com/c.jpg", MangaReleaseStatus.Continuing, [], [], [], []);
        Chapter chapter = new(manga, "12", 1, "Already Named");
        Assert.Equal(0, chapter.ApplyCatalogDetails(9, "Other"));
        Assert.Equal(1, chapter.VolumeNumber);
        Assert.Equal("Already Named", chapter.Title);
    }

    [Fact]
    public void ApplyCatalogDetails_ReplacesTitleThatIsJustTheNumber()
    {
        Manga manga = new("T", "d", "https://example.com/c.jpg", MangaReleaseStatus.Continuing, [], [], [], []);
        Chapter chapter = new(manga, "12", null, "12");
        Assert.Equal(1, chapter.ApplyCatalogDetails(null, "New Title"));
        Assert.Equal("New Title", chapter.Title);
    }
}
