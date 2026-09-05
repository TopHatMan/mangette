using API.Schema.MangaContext;

namespace Tests;

public class ChapterNewReleaseTest
{
    [Fact]
    public void IsNewlyPublished_FirstCatalogIsNotANewRelease()
    {
        Manga manga = Series();
        Chapter incoming = new(manga, "1", 1);
        Assert.False(Chapter.IsNewlyPublished(incoming, []));
    }

    [Fact]
    public void IsNewlyPublished_SameNumberIsNotNew()
    {
        Manga manga = Series();
        Chapter existing = new(manga, "10", 1) { };
        manga.Chapters.Add(existing);
        Chapter incoming = new(manga, "10", 1);
        Assert.False(Chapter.IsNewlyPublished(incoming, manga.Chapters));
    }

    [Fact]
    public void IsNewlyPublished_HoleIsNotNew()
    {
        Manga manga = Series();
        manga.Chapters.Add(new(manga, "1", 1));
        manga.Chapters.Add(new(manga, "3", 1));
        Chapter hole = new(manga, "2", 1);
        Assert.False(Chapter.IsNewlyPublished(hole, manga.Chapters));
    }

    [Fact]
    public void IsNewlyPublished_NewerThanLatestIsNew()
    {
        Manga manga = Series();
        manga.Chapters.Add(new(manga, "120", 12));
        Chapter next = new(manga, "121", 12);
        Assert.True(Chapter.IsNewlyPublished(next, manga.Chapters));
    }

    [Fact]
    public void IsNewlyPublished_DecimalAfterLatestIsNew()
    {
        Manga manga = Series();
        manga.Chapters.Add(new(manga, "10", 1));
        Chapter extra = new(manga, "10.5", 1);
        Assert.True(Chapter.IsNewlyPublished(extra, manga.Chapters));
    }

    [Fact]
    public void NotifyText_IncludesTitleWhenPresent()
    {
        Manga manga = Series();
        Chapter chapter = new(manga, "1", 1, "Romance Dawn");
        Assert.Equal("One Piece Ch. 1 – Romance Dawn", Chapter.NotifyText(manga, chapter));
    }

    private static Manga Series() =>
        new("One Piece", "d", "https://example.com/c.jpg", MangaReleaseStatus.Continuing, [], [], [], []);
}
