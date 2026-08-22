using API.MangaConnectors;
using API.Schema.MangaContext;
using log4net;

namespace API;

/// <summary>
/// AniList/MAL have series-level data only. MangaDex chapter feeds include titles/volumes;
/// use that as a catalog (never as a download source).
/// </summary>
public static class ChapterCatalog
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ChapterCatalog));
    public const double MinTitleMatch = 85;

    public static int FillTitlesFromMangaDex(Manga manga, string? language = null)
    {
        if (Mangette.MangaConnectors.FirstOrDefault(c => c.Name.Equals("MangaDex", StringComparison.OrdinalIgnoreCase))
            is not MangaDex dex)
            return 0;
        if (manga.Chapters is null || manga.Chapters.Count == 0)
            return 0;

        (Manga hit, MangaConnectorId<Manga> id)? best = PickSeries(dex, manga);
        if (best is null)
        {
            Log.InfoFormat("MangaDex catalog: no close title match for {0}.", manga.Name);
            return 0;
        }

        (Chapter chapter, MangaConnectorId<Chapter> id)[] listed;
        try
        {
            listed = dex.GetChapters(best.Value.id, language ?? Mangette.Settings.DownloadLanguage);
        }
        catch (Exception ex)
        {
            Log.WarnFormat("MangaDex catalog feed failed for {0}: {1}", manga.Name, ex.Message);
            return 0;
        }

        int filled = 0;
        foreach ((Chapter incoming, MangaConnectorId<Chapter> _) in listed)
        {
            if (string.IsNullOrWhiteSpace(incoming.ChapterNumber) || incoming.ChapterNumber == "0")
                continue;
            Chapter? existing = manga.Chapters.FirstOrDefault(c =>
                DownloadedChapterMatcher.ChapterNumbersEqual(c.ChapterNumber, incoming.ChapterNumber));
            if (existing is null)
                continue;
            filled += existing.ApplyCatalogDetails(incoming.VolumeNumber, incoming.Title);
        }

        Log.InfoFormat("MangaDex catalog filled {0} title/volume fields on {1} (matched “{2}”).",
            filled, manga.Name, best.Value.hit.Name);
        return filled;
    }

    private static (Manga manga, MangaConnectorId<Manga> id)? PickSeries(MangaDex dex, Manga manga)
    {
        List<string> queries = [manga.Name];
        if (manga.AltTitles is not null)
            queries.AddRange(manga.AltTitles.Select(t => t.Title).Where(t => !string.IsNullOrWhiteSpace(t)));

        (Manga manga, MangaConnectorId<Manga> id, double score)? best = null;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string query in queries.Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
        {
            (Manga hit, MangaConnectorId<Manga> id)[] found;
            try
            {
                found = dex.SearchManga(query);
            }
            catch
            {
                continue;
            }
            foreach ((Manga hit, MangaConnectorId<Manga> id) in found.Take(8))
            {
                if (!seen.Add(id.IdOnConnectorSite))
                    continue;
                double score = Math.Max(
                    LibraryImportMatcher.ScoreTitle(manga.Name, hit.Name),
                    hit.AltTitles?.Select(t => LibraryImportMatcher.ScoreTitle(manga.Name, t.Title)).DefaultIfEmpty(0).Max() ?? 0);
                if (best is null || score > best.Value.score)
                    best = (hit, id, score);
            }
            if (best?.score >= 100)
                break;
        }

        if (best is null || best.Value.score < MinTitleMatch)
            return null;
        return (best.Value.manga, best.Value.id);
    }
}
