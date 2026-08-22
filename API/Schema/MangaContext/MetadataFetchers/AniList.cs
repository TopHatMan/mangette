using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Newtonsoft.Json.Linq;

namespace API.Schema.MangaContext.MetadataFetchers;

/// <summary>
/// AniList public GraphQL (no API key). Manga has no reliable "next chapter drops at" field;
/// we pull status, listed chapter count, and last AniList update as a schedule hint.
/// </summary>
public class AniList : MetadataFetcher
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex IdFromUrl = new(@"anilist\.co/manga/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    private const string Endpoint = "https://graphql.anilist.co";
    private const string MediaFields = """
        id
        title { romaji english native }
        description(asHtml: false)
        status
        chapters
        volumes
        startDate { year }
        updatedAt
        siteUrl
        coverImage { large }
        genres
        staff(perPage: 8) { edges { role node { name { full } } } }
        """;

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mangette/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public override MetadataSearchResult[] SearchMetadataEntry(Manga manga)
    {
        if (manga.Links.FirstOrDefault(l => l.LinkProvider.Equals("AniList", StringComparison.OrdinalIgnoreCase)) is { } linked)
        {
            Match m = IdFromUrl.Match(linked.LinkUrl);
            if (m.Success)
            {
                JToken? media = Query($$"""
                    query ($id: Int) { Media(id: $id, type: MANGA) { {{MediaFields}} } }
                    """, new JObject { ["id"] = int.Parse(m.Groups[1].Value) })?["data"]?["Media"];
                if (media is not null)
                    return [ToResult(media)];
            }
        }

        return SearchMetadataEntry(manga.Name);
    }

    public override MetadataSearchResult[] SearchMetadataEntry(string searchTerm)
    {
        Log.DebugFormat("Searching AniList '{0}'...", searchTerm);
        JToken? page = Query($$"""
            query ($search: String) {
              Page(page: 1, perPage: 8) {
                media(search: $search, type: MANGA, sort: SEARCH_MATCH) { {{MediaFields}} }
              }
            }
            """, new JObject { ["search"] = searchTerm })?["data"]?["Page"]?["media"];
        if (page is not JArray arr)
            return [];
        return arr.OfType<JObject>().Select(ToResult).ToArray();
    }

    public override async Task UpdateMetadata(MetadataEntry metadataEntry, MangaContext dbContext, CancellationToken token)
    {
        if (!int.TryParse(metadataEntry.Identifier, out int id))
        {
            Log.ErrorFormat("AniList id is not a number: {0}", metadataEntry.Identifier);
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

        JToken? media = Query($$"""
            query ($id: Int) { Media(id: $id, type: MANGA) { {{MediaFields}} } }
            """, new JObject { ["id"] = id })?["data"]?["Media"];
        if (media is null)
        {
            Log.ErrorFormat("AniList media {0} not found", id);
            return;
        }

        string? english = media["title"]?["english"]?.Value<string>();
        string? romaji = media["title"]?["romaji"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(english))
            dbManga.Name = english.Trim();
        else if (!string.IsNullOrWhiteSpace(romaji))
            dbManga.Name = romaji.Trim();

        string blurb = ScheduleBlurb(media);
        string synopsis = StripHtml(media["description"]?.Value<string>() ?? "");
        dbManga.Description = string.IsNullOrWhiteSpace(blurb)
            ? synopsis
            : string.IsNullOrWhiteSpace(synopsis) ? blurb : $"{blurb}\n\n{synopsis}";

        dbManga.ReleaseStatus = MapStatus(media["status"]?.Value<string>());

        List<MangaTag> genres = (media["genres"] as JArray)?
            .Select(g => g.Value<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => new MangaTag(g!))
            .ToList() ?? [];
        if (genres.Count > 0)
        {
            dbManga.MangaTags.Clear();
            dbManga.MangaTags = genres;
        }

        List<Author> authors = (media["staff"]?["edges"] as JArray)?
            .Where(e =>
            {
                string role = e?["role"]?.Value<string>() ?? "";
                return role.Contains("Story", StringComparison.OrdinalIgnoreCase) ||
                       role.Contains("Art", StringComparison.OrdinalIgnoreCase);
            })
            .Select(e => e?["node"]?["name"]?["full"]?.Value<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => new Author(n!))
            .ToList() ?? [];
        if (authors.Count > 0)
        {
            dbManga.Authors.Clear();
            dbManga.Authors = authors;
        }

        string? siteUrl = media["siteUrl"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(siteUrl) &&
            dbManga.Links.All(l => !l.LinkProvider.Equals("AniList", StringComparison.OrdinalIgnoreCase)))
        {
            dbManga.Links.Add(new Link("AniList", siteUrl));
        }

        if (await dbContext.Sync(token, GetType(), "Update AniList metadata") is { success: true })
            Log.InfoFormat("Updated AniList metadata: {0}", metadataEntry.MangaId);
    }

    internal static MangaReleaseStatus MapStatus(string? status) => status switch
    {
        "RELEASING" => MangaReleaseStatus.Continuing,
        "FINISHED" => MangaReleaseStatus.Completed,
        "HIATUS" => MangaReleaseStatus.OnHiatus,
        "CANCELLED" => MangaReleaseStatus.Cancelled,
        "NOT_YET_RELEASED" => MangaReleaseStatus.Unreleased,
        _ => MangaReleaseStatus.Continuing
    };

    internal static string ScheduleBlurb(JToken media)
    {
        string status = media["status"]?.Value<string>() ?? "UNKNOWN";
        int? chapters = media["chapters"]?.Value<int?>();
        long? updated = media["updatedAt"]?.Value<long?>();
        string chapterBit = chapters is > 0 ? $"{chapters} chapters listed" : "chapter count unknown";
        string updatedBit = updated is > 0
            ? $"AniList updated {DateTimeOffset.FromUnixTimeSeconds(updated.Value).UtcDateTime:yyyy-MM-dd}"
            : "";
        string line = $"AniList {status.Replace('_', ' ')} · {chapterBit}";
        if (updatedBit.Length > 0)
            line += $" · {updatedBit}";
        line += ". Exact drop times come from your download sites, not AniList.";
        return line;
    }

    private static MetadataSearchResult ToResult(JToken media)
    {
        int id = media["id"]?.Value<int>() ?? 0;
        string name = media["title"]?["english"]?.Value<string>()
                      ?? media["title"]?["romaji"]?.Value<string>()
                      ?? id.ToString();
        string url = media["siteUrl"]?.Value<string>() ?? $"https://anilist.co/manga/{id}";
        string cover = media["coverImage"]?["large"]?.Value<string>() ?? "";
        string desc = $"{ScheduleBlurb(media)}\n\n{StripHtml(media["description"]?.Value<string>() ?? "")}";
        return new MetadataSearchResult(id.ToString(), name, url, desc.Trim(), cover);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        string text = HtmlTag.Replace(html, " ");
        text = text.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#039;", "'");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private JObject? Query(string graphql, JObject variables)
    {
        try
        {
            JObject body = new() { ["query"] = graphql, ["variables"] = variables };
            using HttpRequestMessage req = new(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage res = Http.Send(req);
            string json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!res.IsSuccessStatusCode)
            {
                Log.ErrorFormat("AniList HTTP {0}: {1}", (int)res.StatusCode, json.Length > 300 ? json[..300] : json);
                return null;
            }
            return JObject.Parse(json);
        }
        catch (Exception ex)
        {
            Log.Error($"AniList query failed: {ex.Message}", ex);
            return null;
        }
    }
}
