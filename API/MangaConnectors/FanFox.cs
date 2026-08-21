using System.Text.RegularExpressions;
using System.Web;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using HtmlAgilityPack;

namespace API.MangaConnectors;

public class FanFox : MangaConnector
{
    private const string Root = "https://fanfox.net";

    public FanFox() : base("FanFox", ["en"], ["fanfox.net", "www.fanfox.net", "m.fanfox.net", "mangafox.me"],
        "https://fanfox.net/favicon.ico")
    {
        downloadClient = new HttpDownloadClient();
    }

    public override (Manga, MangaConnectorId<Manga>)[] SearchManga(string mangaSearchName)
    {
        string url = $"{Root}/search?title={HttpUtility.UrlEncode(mangaSearchName)}";
        HtmlDocument? doc = HtmlFetch.GetDocument(downloadClient, url, Root);
        if (doc is null)
            return [];

        List<(Manga, MangaConnectorId<Manga>)> results = [];
        HashSet<string> seen = [];
        foreach (HtmlNode node in HtmlFetch.Select(doc, "//a[starts-with(@href,'/manga/') and string-length(@href) > 8]"))
        {
            string href = HtmlFetch.Attr(node, "href") ?? "";
            if (href.Contains("/c", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(href, @"/c\d"))
                continue;
            Match slugMatch = Regex.Match(href, @"/manga/([^/]+)/?");
            if (!slugMatch.Success || !seen.Add(slugMatch.Groups[1].Value))
                continue;

            string title = HtmlFetch.Attr(node, "title") ?? HtmlFetch.Text(node) ?? slugMatch.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(title) || title.StartsWith("Vol.", StringComparison.OrdinalIgnoreCase))
                continue;
            HtmlNode? img = node.SelectSingleNode(".//img") ?? node.ParentNode?.SelectSingleNode(".//img");
            string cover = HtmlFetch.Absolute(Root, HtmlFetch.Attr(img, "src", "data-src") ?? "");
            results.Add(BuildManga(title.Trim(), cover, slugMatch.Groups[1].Value, $"{Root}/manga/{slugMatch.Groups[1].Value}/", "", MangaReleaseStatus.Continuing, [], []));
        }

        Log.InfoFormat("FanFox search '{0}' yielded {1} results.", mangaSearchName, results.Count);
        return results.ToArray();
    }

    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromUrl(string url)
    {
        Match match = Regex.Match(url, @"(?:fanfox\.net|mangafox\.me)/manga/([^/]+)", RegexOptions.IgnoreCase);
        return match.Success ? GetMangaFromId(match.Groups[1].Value) : null;
    }

    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromId(string mangaIdOnSite)
    {
        string url = $"{Root}/manga/{mangaIdOnSite}/";
        HtmlDocument? doc = HtmlFetch.GetDocument(downloadClient, url, Root);
        if (doc is null)
            return null;

        string title = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//span[@class='detail-info-right-title-font']", "//div[contains(@class,'detail-info')]//span[contains(@class,'title')]", "//h1")) ?? mangaIdOnSite;
        string cover = HtmlFetch.Absolute(url, HtmlFetch.Attr(HtmlFetch.SelectOne(doc, "//img[contains(@class,'detail-info-cover-img')]", "//div[contains(@class,'detail-info')]//img"), "src") ?? "");
        string description = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//p[contains(@class,'fullcontent')]", "//p[@class='fullcontent']")) ?? "";
        List<Author> authors = HtmlFetch.Select(doc, "//p[contains(@class,'detail-info-right-say')]//a", "//a[contains(@href,'/search/author/')]")
            .Select(a => HtmlFetch.Text(a))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => new Author(n!))
            .DistinctBy(a => a.AuthorName)
            .ToList();
        List<MangaTag> tags = HtmlFetch.Select(doc, "//p[contains(@class,'detail-info-right-tag-list')]//a", "//a[contains(@href,'/directory/')]")
            .Select(a => HtmlFetch.Text(a))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => new MangaTag(t!))
            .ToList();
        string statusText = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//span[contains(@class,'detail-info-right-title-tip')]")) ?? "";
        MangaReleaseStatus status = statusText.Contains("Complete", StringComparison.OrdinalIgnoreCase)
            ? MangaReleaseStatus.Completed
            : MangaReleaseStatus.Continuing;
        return BuildManga(title, cover, mangaIdOnSite, url, description, status, authors, tags);
    }

    public override (Chapter, MangaConnectorId<Chapter>)[] GetChapters(MangaConnectorId<Manga> mangaId, string? language = null)
    {
        string url = mangaId.WebsiteUrl ?? $"{Root}/manga/{mangaId.IdOnConnectorSite}/";
        HtmlDocument? doc = HtmlFetch.GetDocument(downloadClient, url, Root);
        if (doc is null)
            return [];

        List<(Chapter, MangaConnectorId<Chapter>)> chapters = [];
        HashSet<string> seen = [];
        foreach (HtmlNode node in HtmlFetch.Select(doc, "//div[@id='chapterlist']//a", "//a[contains(@href,'/manga/') and contains(@href,'/c')]"))
        {
            string href = HtmlFetch.Attr(node, "href") ?? "";
            Match path = Regex.Match(href, @"/manga/[^/]+/(?:v([^/]+)/)?c(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
            if (!path.Success)
                continue;
            string chapterNumber = path.Groups[2].Value;
            if (!seen.Add(chapterNumber))
                continue;
            int? volume = int.TryParse(path.Groups[1].Value, out int vol) ? vol : null;
            string full = HtmlFetch.Absolute(Root, href);
            string title = HtmlFetch.Text(node) ?? "";
            Chapter? chapter = HtmlFetch.TryChapter(mangaId.Obj, chapterNumber, volume, title);
            if (chapter is null)
                continue;
            MangaConnectorId<Chapter> id = new(chapter, this, $"{mangaId.IdOnConnectorSite}/c{chapterNumber}", full);
            chapter.MangaConnectorIds.Add(id);
            chapters.Add((chapter, id));
        }

        Log.InfoFormat("FanFox found {0} chapters for {1}", chapters.Count, mangaId.Obj.Name);
        return chapters.OrderBy(c => c.Item1, new Chapter.ChapterComparer()).ToArray();
    }

    internal override string[] GetChapterImageUrls(MangaConnectorId<Chapter> chapterId)
    {
        if (chapterId.WebsiteUrl is null)
            return [];

        string chapterUrl = chapterId.WebsiteUrl;
        if (!chapterUrl.Contains(".html", StringComparison.OrdinalIgnoreCase))
            chapterUrl = chapterUrl.TrimEnd('/') + "/1.html";

        string? html = HtmlFetch.GetHtml(downloadClient, chapterUrl, Root);
        if (html is null)
            return [];

        HtmlDocument first = new();
        first.LoadHtml(html);
        int pageCount = 0;
        Match countMatch = Regex.Match(html, @"var\s+imagecount\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        if (countMatch.Success)
            int.TryParse(countMatch.Groups[1].Value, out pageCount);
        if (pageCount < 1)
        {
            pageCount = HtmlFetch.Select(first, "//select[contains(@class,'page') or @id='top_bar']//option", "//div[contains(@class,'pager-list')]//option")
                .Select(o => HtmlFetch.Text(o))
                .Count(t => t is not null && Regex.IsMatch(t, @"^\d+$"));
        }
        if (pageCount < 1)
            pageCount = 1;

        string baseUrl = Regex.Replace(chapterUrl, @"/\d+\.html.*$", "/", RegexOptions.IgnoreCase);
        List<string> images = [];
        for (int page = 1; page <= Math.Min(pageCount, 250); page++)
        {
            string pageUrl = page == 1 ? chapterUrl : $"{baseUrl}{page}.html";
            HtmlDocument? doc = page == 1 ? first : HtmlFetch.GetDocument(downloadClient, pageUrl, Root);
            if (doc is null)
                continue;
            foreach (HtmlNode img in HtmlFetch.Select(doc, "//div[@id='viewer']//img", "//img[@id='image']", "//div[contains(@class,'reader-main')]//img"))
            {
                string? src = HtmlFetch.Attr(img, "src", "data-src");
                if (string.IsNullOrWhiteSpace(src) || src.Contains("static.fanfox", StringComparison.OrdinalIgnoreCase))
                    continue;
                images.Add(HtmlFetch.Absolute(pageUrl, src));
            }
        }

        Log.InfoFormat("FanFox found {0} images for {1}", images.Count, chapterId.Obj);
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
