using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using API.Controllers.DTOs;
using API.MangaConnectors;
using API.Schema.MangaContext;
using API.Schema.MangaContext.MetadataFetchers;
using Microsoft.EntityFrameworkCore;
using Manga = API.Schema.MangaContext.Manga;
using MangaConnectorId = API.Schema.MangaContext.MangaConnectorId<API.Schema.MangaContext.Manga>;

namespace API;

/// <summary>
/// Radarr Discover analog: AniList public GraphQL for trending/popular/new,
/// plus an optional WeebCentral popularity scrape.
/// </summary>
public static class DiscoverCatalog
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);
    internal const double LibraryMatchScore = 88;
    private static readonly Regex AniListIdInUrl = new(@"anilist\.co/manga/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AniList AniListClient = new();

    public sealed record LibraryTitle(string MangaId, string Name, IReadOnlyList<string> AltTitles, IReadOnlyList<int> AniListIds);

    private readonly record struct CacheEntry(DiscoverPage Page, DateTime CachedAtUtc);

    public static async Task<DiscoverPage> Load(MangaContext context, CancellationToken token, bool refresh = false)
    {
        List<LibraryTitle> library = await LoadLibrary(context, token);
        string cacheKey = "discover";
        if (!refresh &&
            Cache.TryGetValue(cacheKey, out CacheEntry entry) &&
            DateTime.UtcNow - entry.CachedAtUtc < CacheFor)
        {
            return Relabel(entry.Page, library);
        }

        Task<List<AniList.BrowseItem>> trendingTask = Task.Run(() => SafeBrowse("TRENDING_DESC"), token);
        Task<List<AniList.BrowseItem>> popularTask = Task.Run(() => SafeBrowse("POPULARITY_DESC"), token);
        Task<List<AniList.BrowseItem>> newestTask = Task.Run(() => SafeBrowse("START_DATE_DESC", ", status_in: [RELEASING, NOT_YET_RELEASED]"), token);
        Task<List<AniList.BrowseItem>> updatedTask = Task.Run(() => SafeBrowse("UPDATED_AT_DESC", ", status: RELEASING"), token);
        Task<List<DiscoverHit>> sitesTask = Task.Run(() => LoadPopularOnSites(library), token);
        await Task.WhenAll(trendingTask, popularTask, newestTask, updatedTask);

        List<AniList.BrowseItem> trending = trendingTask.Result;
        List<AniList.BrowseItem> popular = popularTask.Result;
        List<AniList.BrowseItem> newest = newestTask.Result;
        List<AniList.BrowseItem> updated = updatedTask.Result;
        List<DiscoverHit> onSites = [];
        try
        {
            onSites = await sitesTask.WaitAsync(TimeSpan.FromSeconds(8), token);
        }
        catch
        {
            /* WeebCentral Cloudflare/timeouts must not block Discover */
        }

        DiscoverPage page = new(
            trending.Select(i => ToHit(i, library)).ToList(),
            popular.Select(i => ToHit(i, library)).ToList(),
            newest.Select(i => ToHit(i, library)).ToList(),
            updated.Select(i => ToHit(i, library)).ToList(),
            onSites,
            DateTime.UtcNow,
            "AniList");
        Cache[cacheKey] = new CacheEntry(page, DateTime.UtcNow);
        return page;
    }

    public static async Task<List<LibraryTitle>> LoadLibrary(MangaContext context, CancellationToken token)
    {
        var rows = await context.Mangas
            .AsNoTracking()
            .Select(m => new
            {
                m.Key,
                m.Name,
                Alts = m.AltTitles.Select(a => a.Title).ToList(),
                Urls = m.Links.Select(l => l.LinkUrl).ToList()
            })
            .ToListAsync(token);
        return rows.Select(r => new LibraryTitle(r.Key, r.Name, r.Alts, AniListIdsOf(r.Urls))).ToList();
    }

    internal static DiscoverHit ToHit(AniList.BrowseItem item, IReadOnlyList<LibraryTitle> library)
    {
        (bool inLibrary, string? mangaId) = MatchLibrary(item.Id, item.Name, library);
        return new DiscoverHit(
            item.Name,
            item.Description,
            item.Year,
            item.Status,
            item.CoverUrl,
            item.Id,
            item.SiteUrl,
            item.AverageScore,
            item.Popularity,
            item.Kind,
            item.Genres,
            item.Chapters,
            inLibrary,
            mangaId,
            null,
            null);
    }

    internal static (bool InLibrary, string? MangaId) MatchLibrary(
        int anilistId,
        string title,
        IReadOnlyList<LibraryTitle> library)
    {
        if (anilistId > 0)
        {
            LibraryTitle? byId = library.FirstOrDefault(l => l.AniListIds.Contains(anilistId));
            if (byId is not null)
                return (true, byId.MangaId);
        }

        LibraryTitle? best = null;
        double bestScore = 0;
        foreach (LibraryTitle row in library)
        {
            double score = SeriesSearch.ScoreQuery(title, row.Name);
            foreach (string alt in row.AltTitles)
                score = Math.Max(score, SeriesSearch.ScoreQuery(title, alt));
            if (score > bestScore)
            {
                bestScore = score;
                best = row;
            }
        }

        if (best is not null && bestScore >= LibraryMatchScore)
            return (true, best.MangaId);
        return (false, null);
    }

    private static List<int> AniListIdsOf(IEnumerable<string> urls)
    {
        List<int> ids = [];
        foreach (string url in urls)
        {
            Match m = AniListIdInUrl.Match(url);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int id) && !ids.Contains(id))
                ids.Add(id);
        }
        return ids;
    }

    private static List<AniList.BrowseItem> SafeBrowse(string sort, string extraFilters = "")
    {
        try
        {
            return AniListClient.Browse(sort, 24, extraFilters);
        }
        catch
        {
            return [];
        }
    }

    private static List<DiscoverHit> LoadPopularOnSites(IReadOnlyList<LibraryTitle> library)
    {
        if (!Mangette.TryGetMangaConnector("WeebCentral", out API.MangaConnectors.MangaConnector? connector) ||
            connector is not WeebCentral weeb ||
            !weeb.Enabled)
            return [];

        try
        {
            (Manga manga, MangaConnectorId id)[] found = weeb.BrowsePopular(24);
            List<DiscoverHit> hits = [];
            foreach ((Manga manga, MangaConnectorId id) in found)
            {
                (bool inLibrary, string? mangaId) = MatchLibrary(0, manga.Name, library);
                hits.Add(new DiscoverHit(
                    manga.Name,
                    manga.Description,
                    manga.Year,
                    manga.ReleaseStatus,
                    manga.CoverUrl,
                    0,
                    id.WebsiteUrl ?? "",
                    null,
                    0,
                    "Manga",
                    [],
                    null,
                    inLibrary,
                    mangaId,
                    id.MangaConnectorName,
                    id.IdOnConnectorSite));
            }
            return hits;
        }
        catch
        {
            return [];
        }
    }

    private static DiscoverPage Relabel(DiscoverPage page, IReadOnlyList<LibraryTitle> library) =>
        new(
            page.Trending.Select(h => Relabel(h, library)).ToList(),
            page.Popular.Select(h => Relabel(h, library)).ToList(),
            page.NewReleases.Select(h => Relabel(h, library)).ToList(),
            page.RecentlyUpdated.Select(h => Relabel(h, library)).ToList(),
            page.PopularOnSites.Select(h => Relabel(h, library)).ToList(),
            page.GeneratedAtUtc,
            page.Source);

    private static DiscoverHit Relabel(DiscoverHit hit, IReadOnlyList<LibraryTitle> library)
    {
        (bool inLibrary, string? mangaId) = MatchLibrary(hit.AniListId, hit.Name, library);
        return hit with { AlreadyInLibrary = inLibrary, ExistingMangaId = mangaId };
    }
}
