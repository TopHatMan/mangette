using API.Controllers.DTOs;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using Microsoft.EntityFrameworkCore;
using Manga = API.Schema.MangaContext.Manga;
using MangaConnector = API.MangaConnectors.MangaConnector;
using MangaConnectorId = API.Schema.MangaContext.MangaConnectorId<API.Schema.MangaContext.Manga>;

namespace API;

/// <summary>
/// Site lookup that does not write to the library. Add is a separate step, like Sonarr/Radarr.
/// </summary>
public static class SeriesSearch
{
    public const int MaxPerConnector = 12;
    public const int MaxTotal = 48;

    public sealed record ExistingSeries(string ConnectorName, string IdOnSite, string MangaId, string Name);

    public static List<SearchHit> Lookup(string query, string? connectorName, IReadOnlyCollection<ExistingSeries> existing)
    {
        Dictionary<string, ExistingSeries> bySite = existing
            .GroupBy(e => SiteKey(e.ConnectorName, e.IdOnSite), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        List<SearchHit> hits = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (MangaConnector connector in ConnectorsToSearch(connectorName))
        {
            (Manga manga, MangaConnectorId id)[] found;
            try
            {
                found = connector.SearchManga(query);
            }
            catch
            {
                continue;
            }

            int addedFromConnector = 0;
            foreach ((Manga manga, MangaConnectorId id) in found)
            {
                string siteKey = SiteKey(id.MangaConnectorName, id.IdOnConnectorSite);
                if (!seen.Add(siteKey))
                    continue;
                bySite.TryGetValue(siteKey, out ExistingSeries? match);
                hits.Add(ToHit(manga, id, query, match));
                addedFromConnector++;
                if (addedFromConnector >= MaxPerConnector)
                    break;
            }

            if (hits.Count >= MaxTotal)
                break;
        }

        return hits
            .OrderByDescending(h => h.AlreadyInLibrary)
            .ThenByDescending(h => h.Score)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxTotal)
            .ToList();
    }

    public static SearchHit? FromUrl(string url, IReadOnlyCollection<ExistingSeries> existing)
    {
        if (Mangette.MangaConnectors.FirstOrDefault(c =>
                c.Name.Equals("Global", StringComparison.OrdinalIgnoreCase)) is not { } global)
            return null;
        if (global.GetMangaFromUrl(url) is not ({ } manga, { } id))
            return null;
        ExistingSeries? match = existing.FirstOrDefault(e =>
            e.ConnectorName.Equals(id.MangaConnectorName, StringComparison.OrdinalIgnoreCase) &&
            e.IdOnSite.Equals(id.IdOnConnectorSite, StringComparison.OrdinalIgnoreCase));
        return ToHit(manga, id, manga.Name, match);
    }

    public static async Task<List<ExistingSeries>> LoadExisting(MangaContext context, CancellationToken token)
    {
        var links = await context.MangaConnectorToManga
            .AsNoTracking()
            .Select(id => new { id.MangaConnectorName, id.IdOnConnectorSite, id.ObjId })
            .ToListAsync(token);
        Dictionary<string, string> names = await context.Mangas.AsNoTracking()
            .ToDictionaryAsync(m => m.Key, m => m.Name, token);
        return links
            .Select(l => new ExistingSeries(
                l.MangaConnectorName,
                l.IdOnConnectorSite,
                l.ObjId,
                names.GetValueOrDefault(l.ObjId, "")))
            .ToList();
    }

    public static double ScoreQuery(string query, string title) =>
        LibraryImportMatcher.ScoreTitle(query, title);

    private static SearchHit ToHit(Manga manga, MangaConnectorId id, string query, ExistingSeries? match)
    {
        double score = ScoreQuery(query, manga.Name);
        return new SearchHit(
            manga.Name,
            manga.Description,
            manga.Year,
            manga.ReleaseStatus,
            manga.CoverUrl,
            id.MangaConnectorName,
            id.IdOnConnectorSite,
            id.WebsiteUrl,
            Math.Round(score, 1),
            match is not null,
            match?.MangaId);
    }

    private static string SiteKey(string connector, string id) => $"{connector}:{id}";

    private static IEnumerable<MangaConnector> ConnectorsToSearch(string? connectorName)
    {
        if (!string.IsNullOrWhiteSpace(connectorName))
        {
            if (Mangette.TryGetMangaConnector(connectorName, out MangaConnector? one) &&
                one.Enabled &&
                !one.Name.Equals("Global", StringComparison.OrdinalIgnoreCase))
                yield return one;
            yield break;
        }

        foreach (string name in DownloadFailureTracker.GetPreferenceOrder())
        {
            if (!Mangette.TryGetMangaConnector(name, out MangaConnector? connector) || !connector.Enabled)
                continue;
            if (connector.Name.Equals("Global", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return connector;
        }
    }
}
