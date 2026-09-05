using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Newtonsoft.Json.Linq;

namespace API.Schema.MangaContext.MetadataFetchers;

/// <summary>One issue in a ComicVine volume's issue list, used to build the wanted-issue range on import.</summary>
public sealed record ComicVineIssue(string IssueNumber, string? Name, DateTime? CoverDate);

/// <summary>
/// One ComicVine volume search result with the fields the "Add Comic" search UI needs displayed
/// separately (year, issue count) rather than folded into one description string.
/// </summary>
public sealed record ComicVineVolumeSummary(
    string ComicVineVolumeId, string Name, string? Url, string? CoverUrl, string? Description,
    int? Year, string? Publisher, int IssueCount);

/// <summary>
/// ComicVine REST API (https://comicvine.gamespot.com/api/) -- the metadata source for Comics,
/// the same role AniList/MyAnimeList play for Manga. Needs a free API key set in Settings.
/// </summary>
public class ComicVine : MetadataFetcher
{
    private const string BaseUrl = "https://comicvine.gamespot.com/api";
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex IdFromUrl = new(@"comicvine\.gamespot\.com/[^/]+/4050-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly ILog StaticLog = LogManager.GetLogger(typeof(ComicVine));

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mangette/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public override MetadataSearchResult[] SearchMetadataEntry(Manga manga)
    {
        if (manga.Links.FirstOrDefault(l => l.LinkProvider.Equals("ComicVine", StringComparison.OrdinalIgnoreCase)) is { } linked)
        {
            Match m = IdFromUrl.Match(linked.LinkUrl);
            if (m.Success && GetVolume(m.Groups[1].Value).Result is { } volume)
                return [ToResult(volume)];
        }
        return SearchMetadataEntry(manga.Name);
    }

    public override MetadataSearchResult[] SearchMetadataEntry(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ComicVineApiKey))
        {
            Log.Debug("ComicVine is not configured.");
            return [];
        }

        Log.DebugFormat("Searching ComicVine '{0}'...", searchTerm);
        string url = $"{BaseUrl}/search/?api_key={Mangette.Settings.ComicVineApiKey}&format=json&resources=volume" +
                     $"&query={Uri.EscapeDataString(searchTerm)}" +
                     "&field_list=id,name,start_year,image,description,count_of_issues,publisher,site_detail_url&limit=10";
        JObject? body = Get(url).Result;
        JArray? results = body?.Value<JArray>("results");
        return results?.OfType<JObject>().Select(ToResult).ToArray() ?? [];
    }

    public override async Task UpdateMetadata(MetadataEntry metadataEntry, MangaContext dbContext, CancellationToken token)
    {
        JObject? volume = await GetVolume(metadataEntry.Identifier, token);
        if (volume is null)
        {
            Log.ErrorFormat("ComicVine volume {0} not found", metadataEntry.Identifier);
            return;
        }

        Manga? dbManga = metadataEntry.Manga;
        if (dbManga is null)
        {
            dbManga = await dbContext.Mangas.FirstOrDefaultAsync(m => m.Key == metadataEntry.MangaId, token);
            if (dbManga is null)
                throw new DbUpdateException("Manga not found");
        }

        foreach (CollectionEntry collectionEntry in dbContext.Entry(dbManga).Collections)
        {
            if (!collectionEntry.IsLoaded)
                await collectionEntry.LoadAsync(token);
        }

        string? name = volume.Value<string>("name");
        if (!string.IsNullOrWhiteSpace(name))
            dbManga.Name = name.Trim();
        dbManga.Description = StripHtml(volume.Value<string>("description") ?? "");

        string? siteUrl = volume.Value<string>("site_detail_url");
        if (!string.IsNullOrWhiteSpace(siteUrl) &&
            dbManga.Links.All(l => !l.LinkProvider.Equals("ComicVine", StringComparison.OrdinalIgnoreCase)))
        {
            dbManga.Links.Add(new Link("ComicVine", siteUrl));
        }

        if (await dbContext.Sync(token, GetType(), "Update ComicVine metadata") is { success: true })
            Log.InfoFormat("Updated ComicVine metadata: {0}", metadataEntry.MangaId);
    }

    /// <summary>
    /// Search ComicVine volumes with year/issue-count/publisher exposed as their own fields, for the
    /// "Add Comic" search UI (picking "Batman v1 (1940) - 713 issues" apart from "Batman (2016) Rebirth").
    /// </summary>
    public async Task<ComicVineVolumeSummary[]> SearchVolumes(string searchTerm, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ComicVineApiKey))
            return [];

        string url = $"{BaseUrl}/search/?api_key={Mangette.Settings.ComicVineApiKey}&format=json&resources=volume" +
                     $"&query={Uri.EscapeDataString(searchTerm)}" +
                     "&field_list=id,name,start_year,image,description,count_of_issues,publisher,site_detail_url&limit=15";
        JObject? body = await Get(url, cancellationToken);
        JArray? results = body?.Value<JArray>("results");
        return results?.OfType<JObject>().Select(ToSummary).ToArray() ?? [];
    }

    private static ComicVineVolumeSummary ToSummary(JObject volume)
    {
        int id = volume.Value<int?>("id") ?? 0;
        int? year = int.TryParse(volume.Value<string>("start_year"), out int y) ? y : null;
        return new ComicVineVolumeSummary(
            id.ToString(),
            volume.Value<string>("name") ?? id.ToString(),
            volume.Value<string>("site_detail_url"),
            volume.Value<JObject>("image")?.Value<string>("medium_url"),
            StripHtml(volume.Value<string>("description") ?? ""),
            year,
            volume.Value<JObject>("publisher")?.Value<string>("name"),
            volume.Value<int?>("count_of_issues") ?? 0);
    }

    internal async Task<JObject?> GetVolume(string volumeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ComicVineApiKey))
            return null;
        string url = $"{BaseUrl}/volume/4050-{volumeId}/?api_key={Mangette.Settings.ComicVineApiKey}&format=json" +
                     "&field_list=id,name,start_year,image,description,count_of_issues,publisher,site_detail_url";
        JObject? body = await Get(url, cancellationToken);
        return body?.Value<JObject>("results");
    }

    /// <summary>Full issue list for a volume, used to build the real wanted-issue range on import (not a guess).</summary>
    public async Task<ComicVineIssue[]> GetVolumeIssues(string volumeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ComicVineApiKey))
            return [];

        List<ComicVineIssue> issues = [];
        int offset = 0;
        int total = int.MaxValue;
        while (offset < total)
        {
            string url = $"{BaseUrl}/issues/?api_key={Mangette.Settings.ComicVineApiKey}&format=json" +
                         $"&filter=volume:{volumeId}&field_list=issue_number,name,cover_date&limit=100&offset={offset}";
            JObject? body = await Get(url, cancellationToken);
            if (body is null)
                break;
            total = body.Value<int?>("number_of_total_results") ?? 0;
            if (body.Value<JArray>("results") is not { } results || results.Count == 0)
                break;

            foreach (JToken item in results)
            {
                string? issueNumber = item.Value<string>("issue_number");
                if (string.IsNullOrWhiteSpace(issueNumber))
                    continue;
                issues.Add(new ComicVineIssue(issueNumber, item.Value<string>("name"), item.Value<DateTime?>("cover_date")));
            }
            offset += 100;
        }
        return issues.ToArray();
    }

    private static async Task<JObject?> Get(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                StaticLog.Error($"ComicVine returned {(int)response.StatusCode} {response.StatusCode} for {url}");
                return null;
            }
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JObject.Parse(body);
        }
        catch (Exception ex)
        {
            StaticLog.Error($"ComicVine request failed: {ex.Message}", ex);
            return null;
        }
    }

    private static MetadataSearchResult ToResult(JObject volume)
    {
        int id = volume.Value<int?>("id") ?? 0;
        string name = volume.Value<string>("name") ?? id.ToString();
        string url = volume.Value<string>("site_detail_url") ?? $"https://comicvine.gamespot.com/volume/4050-{id}/";
        string cover = volume.Value<JObject>("image")?.Value<string>("medium_url") ?? "";
        string startYear = volume.Value<string>("start_year") ?? "?";
        int issueCount = volume.Value<int?>("count_of_issues") ?? 0;
        string publisher = volume.Value<JObject>("publisher")?.Value<string>("name") ?? "";
        string desc = StripHtml(volume.Value<string>("description") ?? "");
        string summary = $"{startYear} · {publisher} · {issueCount} issues\n\n{desc}".Trim();
        return new MetadataSearchResult(id.ToString(), name, url, summary, cover);
    }

    internal static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        string text = HtmlTag.Replace(html, " ");
        text = text.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#039;", "'");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
