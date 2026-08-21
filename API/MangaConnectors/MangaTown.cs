using System.Text.RegularExpressions;
using System.Web;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using HtmlAgilityPack;

namespace API.MangaConnectors;

public class MangaTown : MangaConnector
{
    private const string Root = "https://www.mangatown.com";

    public MangaTown() : base("MangaTown", ["en"], ["mangatown.com", "www.mangatown.com"],
        "https://www.mangatown.com/favicon.ico")
    {
        downloadClient = new HttpDownloadClient();
    }

    public override (Manga, MangaConnectorId<Manga>)[] SearchManga(string mangaSearchName)
    {
        string url = $"{Root}/search?name={HttpUtility.UrlEncode(mangaSearchName)}";
        HtmlDocument? doc = HtmlFetch.GetDocument(downloadClient, url, Root);
        if (doc is null)
            return [];

        List<(Manga, MangaConnectorId<Manga>)> results = [];
        HashSet<string> seen = [];
        foreach (HtmlNode node in HtmlFetch.Select(doc, "//a[starts-with(@href,'/manga/') and string-length(@href) > 8]"))
        {
            string href = HtmlFetch.Attr(node, "href") ?? "";
            if (Regex.IsMatch(href, @"/c\d", RegexOptions.IgnoreCase))
                continue;
            string full = HtmlFetch.Absolute(Root, href.TrimEnd('/') + "/");
            Match slugMatch = Regex.Match(full, @"/manga/([^/]+)/?");
            if (!slugMatch.Success || !seen.Add(slugMatch.Groups[1].Value))
                continue;

            string title = HtmlFetch.Text(node) ?? HtmlFetch.Attr(node, "title") ?? slugMatch.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(title))
                continue;

            HtmlNode? img = node.SelectSingleNode(".//img") ?? node.ParentNode?.SelectSingleNode(".//img");
            string cover = HtmlFetch.Absolute(Root, HtmlFetch.Attr(img, "src", "data-src") ?? "");
            results.Add(BuildManga(title, cover, slugMatch.Groups[1].Value, full, "", MangaReleaseStatus.Continuing, [], []));
        }

        Log.InfoFormat("MangaTown search '{0}' yielded {1} results.", mangaSearchName, results.Count);
        return results.ToArray();
    }

    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromUrl(string url)
    {
        Match match = Regex.Match(url, @"mangatown\.com/manga/([^/]+)", RegexOptions.IgnoreCase);
        return match.Success ? GetMangaFromId(match.Groups[1].Value) : null;
    }

    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromId(string mangaIdOnSite)
    {
        string url = $"{Root}/manga/{mangaIdOnSite}/";
        HtmlDocument? doc = HtmlFetch.GetDocument(downloadClient, url, Root);
        if (doc is null)
            return null;

        string title = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//h1", "//div[@class='title-top']//h1")) ?? mangaIdOnSite;
        string cover = HtmlFetch.Absolute(url, HtmlFetch.Attr(HtmlFetch.SelectOne(doc, "//div[contains(@class,'detail_info')]//img", "//img[contains(@src,'cover') or contains(@src,'ocover')]"), "src") ?? "");
        string description = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//span[@id='show']", "//b[contains(text(),'Summary')]/following-sibling::span", "//*[contains(@class,'summary')]")) ?? "";
        List<Author> authors = HtmlFetch.Select(doc, "//b[contains(text(),'Author')]/following-sibling::a")
            .Select(a => HtmlFetch.Text(a))
            .Where(n => !string.IsNullOrWhiteSpace(n) && n != "N/A")
            .Select(n => new Author(n!))
            .ToList();
        List<MangaTag> tags = HtmlFetch.Select(doc, "//b[contains(text(),'Genre')]/following-sibling::a")
            .Select(a => HtmlFetch.Text(a))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => new MangaTag(t!))
            .ToList();
        string statusText = HtmlFetch.Text(HtmlFetch.SelectOne(doc, "//b[contains(text(),'Status')]/..")) ?? "";
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
        foreach (HtmlNode node in HtmlFetch.Select(doc, "//ul[contains(@class,'chapter_list')]//a", "//a[contains(@href,'/manga/') and contains(@href,'/c')]"))
        {
            string href = HtmlFetch.Attr(node, "href") ?? "";
            if (string.IsNullOrEmpty(href))
                continue;
            string full = HtmlFetch.Absolute(Root, href);
            Match path = Regex.Match(full, @"/manga/[^/]+/(?:v([^/]+)/)?c(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
            if (!path.Success)
                continue;
            string chapterNumber = path.Groups[2].Value;
            if (!seen.Add(chapterNumber))
                continue;
            int? volume = int.TryParse(path.Groups[1].Value, out int vol) ? vol : HtmlFetch.VolumeFromText(HtmlFetch.Text(node) ?? "");
            string chapterUrl = full.Contains(".html", StringComparison.OrdinalIgnoreCase) ? full : full.TrimEnd('/') + "/";
            Chapter? chapter = HtmlFetch.TryChapter(mangaId.Obj, chapterNumber, volume, HtmlFetch.Text(node));
            if (chapter is null)
                continue;
            MangaConnectorId<Chapter> id = new(chapter, this, $"{mangaId.IdOnConnectorSite}/c{chapterNumber}", chapterUrl);
            chapter.MangaConnectorIds.Add(id);
            chapters.Add((chapter, id));
        }

        Log.InfoFormat("MangaTown found {0} chapters for {1}", chapters.Count, mangaId.Obj.Name);
        return chapters.OrderBy(c => c.Item1, new Chapter.ChapterComparer()).ToArray();
    }

    internal override string[] GetChapterImageUrls(MangaConnectorId<Chapter> chapterId)
    {
        if (chapterId.WebsiteUrl is null)
            return [];
        string chapterUrl = chapterId.WebsiteUrl;
        if (!chapterUrl.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            chapterUrl = chapterUrl.TrimEnd('/') + "/1.html";

        string? firstHtml = HtmlFetch.GetHtml(downloadClient, chapterUrl, Root);
        if (firstHtml is null)
            return [];

        HtmlDocument first = new();
        first.LoadHtml(firstHtml);
        List<string> pageUrls = [];
        foreach (HtmlNode option in HtmlFetch.Select(first, "//div[contains(@class,'page_select')]//option", "//select[contains(@class,'page')]//option"))
        {
            string value = HtmlFetch.Attr(option, "value") ?? "";
            string label = HtmlFetch.Text(option) ?? "";
            if (string.IsNullOrEmpty(value) || !Regex.IsMatch(label, @"^\d+$"))
                continue;
            pageUrls.Add(HtmlFetch.Absolute(chapterUrl, value.Contains(".html") ? value : value.TrimEnd('/') + ".html"));
        }

        if (pageUrls.Count == 0)
            pageUrls.Add(chapterUrl);

        List<string> images = [];
        foreach (string pageUrl in pageUrls.Distinct().Take(250))
        {
            HtmlDocument? page = pageUrl == chapterUrl ? first : HtmlFetch.GetDocument(downloadClient, pageUrl, Root);
            if (page is null)
                continue;
            HtmlNode? img = HtmlFetch.SelectOne(page, "//div[@id='viewer']//img", "//img[@id='image']", "//div[contains(@class,'read_img')]//img");
            string? src = HtmlFetch.Attr(img, "src", "data-src");
            if (string.IsNullOrWhiteSpace(src) || src.Contains("static.mangatown", StringComparison.OrdinalIgnoreCase))
                continue;
            images.Add(HtmlFetch.Absolute(pageUrl, src));
        }

        Log.InfoFormat("MangaTown found {0} images for {1}", images.Count, chapterId.Obj);
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
