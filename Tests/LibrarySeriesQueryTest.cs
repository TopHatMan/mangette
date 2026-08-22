using API.Controllers;
using API.Controllers.DTOs;
using API.Schema.MangaContext;
using Microsoft.EntityFrameworkCore;
using Chapter = API.Schema.MangaContext.Chapter;
using FileLibrary = API.Schema.MangaContext.FileLibrary;
using Manga = API.Schema.MangaContext.Manga;
using MangaConnectorId = API.Schema.MangaContext.MangaConnectorId<API.Schema.MangaContext.Manga>;

namespace Tests;

public class LibrarySeriesQueryTest
{
    [Fact]
    public async Task LoadLibrarySeries_CountsChaptersWithoutLoadingRows_AndOmitsUnmonitoredEmpty()
    {
        await using MangaContext ctx = await Seed();

        List<LibrarySeries> library = await MangaController.LoadLibrarySeries(ctx, CancellationToken.None);

        Assert.Equal(2, library.Count);
        LibrarySeries monitored = library.Single(m => m.Name == "Monitored");
        Assert.True(monitored.Monitored);
        Assert.Equal(3, monitored.ChapterCount);
        Assert.Equal(1, monitored.DownloadedCount);
        Assert.Contains(monitored.MangaConnectorIds, id => id.MangaConnectorName == "WeebCentral" && id.UseForDownload);

        LibrarySeries imported = library.Single(m => m.Name == "Imported");
        Assert.False(imported.Monitored);
        Assert.Equal(2, imported.ChapterCount);
        Assert.Equal(2, imported.DownloadedCount);

        Assert.DoesNotContain(library, m => m.Name == "Leftover");
    }

    private static async Task<MangaContext> Seed()
    {
        string name = $"file:libquery_{Guid.NewGuid():N}?mode=memory&cache=shared";
        DbContextOptions<MangaContext> options = new DbContextOptionsBuilder<MangaContext>()
            .UseSqlite($"Data Source={name};Mode=Memory;Cache=Shared")
            .Options;
        MangaContext ctx = new(options);
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        FileLibrary library = new(@"C:\Manga", "Main");
        ctx.FileLibraries.Add(library);

        Manga monitored = NewManga("Monitored", library);
        Manga imported = NewManga("Imported", library);
        Manga leftover = NewManga("Leftover", library);
        ctx.Mangas.AddRange(monitored, imported, leftover);

        monitored.MangaConnectorIds.Add(new MangaConnectorId(
            monitored, "WeebCentral", "op-1", "https://example.com/op", useForDownload: true));
        leftover.MangaConnectorIds.Add(new MangaConnectorId(
            leftover, "WeebCentral", "left-1", "https://example.com/left", useForDownload: false));

        Chapter m1 = new(monitored, "1", 1);
        Chapter m2 = new(monitored, "2", 1);
        Chapter m3 = new(monitored, "3", 1) { Downloaded = true };
        Chapter i1 = new(imported, "1", 1) { Downloaded = true };
        Chapter i2 = new(imported, "2", 1) { Downloaded = true };
        Chapter l1 = new(leftover, "1", 1);
        ctx.Chapters.AddRange(m1, m2, m3, i1, i2, l1);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static Manga NewManga(string title, FileLibrary library) =>
        new(title, "d", "https://example.com/c.jpg", MangaReleaseStatus.Continuing, [], [], [], [], library);
}
