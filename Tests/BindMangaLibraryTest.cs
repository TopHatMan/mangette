using API.Schema.MangaContext;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class BindMangaLibraryTest
{
    private static MangaContext NewContext(string name)
    {
        DbContextOptions<MangaContext> options = new DbContextOptionsBuilder<MangaContext>()
            .UseSqlite($"Data Source={name};Mode=Memory;Cache=Shared")
            .Options;
        return new MangaContext(options);
    }

    [Fact]
    public async Task AssigningDetachedDuplicateLibrary_ThrowsOnDetectChanges()
    {
        await using MangaContext ctx = await Seed();
        Manga loaded = await ctx.Mangas.Include(m => m.Library).FirstAsync();
        FileLibrary duplicate = new(@"C:\Manga", "Main");
        Assert.Equal(loaded.Library!.Key, duplicate.Key);
        Assert.False(ReferenceEquals(loaded.Library, duplicate));

        loaded.Library = duplicate;
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ctx.ChangeTracker.Entries().ToList());
        Assert.Contains("FileLibrary", ex.Message);
        Assert.Contains("already being tracked", ex.Message);
    }

    [Fact]
    public async Task BindMangaLibrary_DoesNotThrowWhenSameLibraryIsDetached()
    {
        await using MangaContext ctx = await Seed();
        Manga loaded = await ctx.Mangas.Include(m => m.Library).FirstAsync();
        FileLibrary stale = new(@"C:\Manga", "Main");

        await ctx.BindMangaLibrary(loaded, stale, CancellationToken.None);
        (bool ok, string? err) = await ctx.Sync(CancellationToken.None, reason: "import");
        Assert.True(ok, err);
        Assert.Equal(stale.Key, loaded.LibraryId);
    }

    private static async Task<MangaContext> Seed()
    {
        string name = $"file:bindlib_{Guid.NewGuid():N}?mode=memory&cache=shared";
        MangaContext ctx = NewContext(name);
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        FileLibrary library = new(@"C:\Manga", "Main");
        Manga manga = new(
            "One Piece",
            "d",
            "https://example.com/c.jpg",
            MangaReleaseStatus.Continuing,
            [],
            [],
            [],
            [],
            library);
        ctx.FileLibraries.Add(library);
        ctx.Mangas.Add(manga);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }
}
