using System.Text.RegularExpressions;
using System.Web;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

namespace API.MangaConnectors;

public class NeloManga : MangaConnector
{
    private const string Root = "https://www.nelomanga.net";

    public NeloManga() : base("NeloManga", ["en"], ["nelomanga.net", "www.nelomanga.net"],
        "https://www.nelomanga.net/favicon.ico")
    {
        downloadClient = new HttpDownloadClient();
    }

    public override (Manga, MangaConnectorId<Manga>)[] SearchManga(string mangaSearchName)
    {
        string slug = Regex.Replace(mangaSearchName.Trim().ToLowerInvariant(), @"\s+", "_");
        string[] urls =
        [
            $"{Root}/search/story/{HttpUtility.UrlEncode(slug)}",
            $"{Root}/home_json_search?searchword={HttpUtility.UrlEncode(mangaSearchName)}"
        ];

        List<(Manga, MangaConnectorId<Manga>)> results = [];
        HashSet<string> seen = [];
        foreach (string url in urls)
        {
            string? body = HtmlFetch.GetHtml(downloadClient, url, Root);
            if (body is null)
                continue;

            if (body.TrimStart().StartsWith('[') || body.TrimStart().StartsWith('{'))
            {
                try
                {
                    JToken token = JToken.Parse(body);
                    IEnumerable<JToken> items = token is JArray arr
                        ? arr
                        : token["data"] as JArray ?? Enumerable.Empty<JToken>();
                    foreach (JToken item in items)
                    {
                        string name = item.Value<string>("name") ?? item.Value<string>("title") ?? "";
                        string storyUrl = item.Value<string>("url_story") ?? item.Value<string>("url") ?? item.Value<string>("slug") ?? "";
                        string image = item.Value<string>("image") ?? item.Value<string>("thumbnail") ?? "";
                        if (string.IsNullOrWhiteSpace(name))
                            continue;
                        if (!storyUrl.Contains("/manga/", StringComparison.OrdinalIgnoreCase))
                            storyUrl = $"{Root}/manga/{storyUrl.Trim('/')}";
                        storyUrl = HtmlFetch.Absolute(Root, storyUrl);
                        Match idMatch = Regex.Match(storyUrl, @"/manga/([^/]+)");
                        if (!idMatch.Success || !seen.Add(idMatch.Groups[1].Value))
                            continue;
                        results.Add(BuildManga(name, HtmlFetch.Absolute(Root, image), idMatch.Groups[1].Value, storyUrl, "", MangaReleaseStatus.Continuing, [], []));
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"NeloManga JSON search parse failed: {ex.Message}");
                }
            }

            HtmlDocument doc = new();
            doc.LoadHtml(body);
            foreach (HtmlNode node in HtmlFetch.Select(doc,
                         "//div[contains(@class,'story_item')]//h3/a",
                         "//div[contains(@class,'item-title')]/a",
                         "//a[contains(@href,'/manga/') and not(contains(@href,'/chapter'))]"))
            {
                string href = HtmlFetch.Attr(node, "href") ?? "";
                Match idMatch = Regex.Match(href, @"/manga/([^/]+)");
                if (!idMatch.Success || href.Contains("/chapter", StringComparison.OrdinalIgnoreCase) || !seen.Add(idMatch.Groups[1].Value))
                    continue;
                string title = HtmlFetch.Attr(node, "title") ?? HtmlFetch.Text(node) ?? idMatch.Groups[1].Value;
                HtmlNode? img = node.SelectSingleNode(".//img") ?? node.ParentNode?.ParentNode?.SelectSingleNode(".//img");
                string cover = HtmlFetch.Absolute(Root, HtmlFetch.Attr(img, "src", "data-src") ?? "");
                results.Add(BuildManga(title, cover, idMatch.Groups[1].Value, HtmlFetch.Absolute(Root, href), "", MangaReleaseStatus.Continuing, [], []));
            }

            if (results.Count > 0)
                break;
        }

        Log.InfoFormat("NeloManga search '{0}' yielded {1} results.", mangaSearchName, results.Count);
        return results.ToArray();
    }

    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromUrl(string url)
    {
        Match match = Regex.Match(url, @"nelomanga\.net/manga/([^/]+)", RegexOptions.IgnoreCase);
        return match.Success ? GetMangaFromId(match.Groups[1].Value) : null;
    }

    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromId(string mangaIdOnSite)
    {
        string url = $"{Root}/manga/{mangaIdOnSite}";
        HtmlDocument? doc = HtmlFetch.GetDocument(downloadClient, url, Root);
        if (doc is null)
            return null;

        string title = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//div[contains(@class,'story-info-right')]//h1", "//h1", "//ul[contains(@class,'manga-info-text')]//h1")) ?? mangaIdOnSite;
        string cover = HtmlFetch.Absolute(url, HtmlFetch.Attr(HtmlFetch.SelectOne(doc, "//div[contains(@class,'story-info-left')]//img", "//div[contains(@class,'manga-info-pic')]//img"), "src", "data-src") ?? "");
        string description = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//div[contains(@id,'panel-story-info-description')]", "//div[contains(@class,'panel-story-info-description')]")) ?? "";
        List<Author> authors = HtmlFetch.Select(doc, "//td[contains(.,'Author')]/following-sibling::td//a", "//li[contains(.,'Author')]//a")
            .Select(a => HtmlFetch.Text(a))
            .Where(n => !string.IsNullOrWhiteSpace(n) && !n.Equals("Updating", StringComparison.OrdinalIgnoreCase))
            .Select(n => new Author(n!))
            .ToList();
        List<MangaTag> tags = HtmlFetch.Select(doc, "//td[contains(.,'Genres')]/following-sibling::td//a", "//li[contains(.,'Genres')]//a")
            .Select(a => HtmlFetch.Text(a))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => new MangaTag(t!))
            .ToList();
        string statusText = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//td[contains(.,'Status')]/following-sibling::td", "//li[contains(.,'Status')]")) ?? "";
        MangaReleaseStatus status = statusText.Contains("Complete", StringComparison.OrdinalIgnoreCase)
            ? MangaReleaseStatus.Completed
            : MangaReleaseStatus.Continuing;
        return BuildManga(title, cover, mangaIdOnSite, url, description, status, authors, tags);
    }

    public override (Chapter, MangaConnectorId<Chapter>)[] GetChapters(MangaConnectorId<Manga> mangaId, string? language = null)
    {
        string url = mangaId.WebsiteUrl ?? $"{Root}/manga/{mangaId.IdOnConnectorSite}";
        HtmlDocument? doc = HtmlFetch.GetDocument(downloadClient, url, Root);
        if (doc is null)
            return [];

        List<(Chapter, MangaConnectorId<Chapter>)> chapters = [];
        HashSet<string> seen = [];
        foreach (HtmlNode node in HtmlFetch.Select(doc,
                     "//ul[contains(@class,'row-content-chapter')]//a",
                     "//div[contains(@class,'chapter-list')]//a",
                     "//a[contains(@href,'/chapter-')]"))
        {
            string href = HtmlFetch.Attr(node, "href") ?? "";
            string full = HtmlFetch.Absolute(Root, href);
            Match path = Regex.Match(full, @"/chapter-(\d+(?:-\d+)*)", RegexOptions.IgnoreCase);
            string? chapterNumber = path.Success
                ? path.Groups[1].Value.Replace('-', '.')
                : HtmlFetch.ChapterNumberFromText(HtmlFetch.Text(node) ?? full);
            if (chapterNumber is null || !seen.Add(chapterNumber))
                continue;
            string title = HtmlFetch.Text(node) ?? "";
            Chapter? chapter = HtmlFetch.TryChapter(mangaId.Obj, chapterNumber, null, title);
            if (chapter is null)
                continue;
            MangaConnectorId<Chapter> id = new(chapter, this, $"{mangaId.IdOnConnectorSite}/chapter-{chapterNumber.Replace('.', '-')}", full);
            chapter.MangaConnectorIds.Add(id);
            chapters.Add((chapter, id));
        }

        Log.InfoFormat("NeloManga found {0} chapters for {1}", chapters.Count, mangaId.Obj.Name);
        return chapters.OrderBy(c => c.Item1, new Chapter.ChapterComparer()).ToArray();
    }

    internal override string[] GetChapterImageUrls(MangaConnectorId<Chapter> chapterId)
    {
        if (chapterId.WebsiteUrl is null)
            return [];
        HtmlDocument? doc = HtmlFetch.GetDocument(downloadClient, chapterId.WebsiteUrl, Root);
        if (doc is null)
            return [];

        List<string> images = [];
        foreach (HtmlNode img in HtmlFetch.Select(doc,
                     "//div[contains(@class,'container-chapter-reader')]//img",
                     "//div[contains(@class,'vung-doc')]//img",
                     "//div[contains(@class,'reading-content')]//img"))
        {
            string? src = HtmlFetch.Attr(img, "data-src", "data-url", "src");
            if (string.IsNullOrWhiteSpace(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;
            images.Add(HtmlFetch.Absolute(chapterId.WebsiteUrl, src));
        }

        Log.InfoFormat("NeloManga found {0} images for {1}", images.Count, chapterId.Obj);
        return images.Distinct().ToArray();
    }

    private (Manga, MangaConnectorId<Manga>) BuildManga(string title, string cover, string id, string url, string description,
        MangaReleaseStatus status, List<Author> authors, List<MangaTag> tags)
    {
        Manga manga = new(title, description, cover, status, authors, tags, [], []);
        MangaConnectorId<Manga> mcId = new(manga, this, id, url);
        manga.MangaConnectorIds.Add(mcId);
        return (manga, mcId);
    }
}
