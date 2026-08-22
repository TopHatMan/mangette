using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace API.MangaConnectors;

internal static class WeebCentralParse
{
    private static readonly Regex SeriesHref = new(
        @"/series/(?<id>[^/]+)(?:/(?<slug>[^/?#]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChapterHref = new(
        @"/chapters/(?<id>[^/?#]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChapterNumber = new(
        @"(?:chapter|ch\.?)\s*(\d+(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VolumeNumber = new(
        @"^(?:volume|vol\.?|season|s\.?)\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyNumber = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

    public readonly record struct SearchItem(string Id, string Title, string Url, string CoverUrl);
    public readonly record struct ChapterItem(string ChapterNumber, int? VolumeNumber, string ChapterId, string Url);

    public static List<SearchItem> SearchResults(string html)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        HtmlNodeCollection? nodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '/series/')]");
        if (nodes is null)
            return [];

        List<SearchItem> items = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (HtmlNode node in nodes)
        {
            string href = node.GetAttributeValue("href", "");
            Match match = SeriesHref.Match(href);
            if (!match.Success)
                continue;
            string id = match.Groups["id"].Value;
            if (!seen.Add(id))
                continue;

            string slug = match.Groups["slug"].Success ? match.Groups["slug"].Value : "";
            string url = AbsoluteUrl(href, $"https://weebcentral.com/series/{id}");
            string title = TitleFromNode(node, slug);
            string cover = CoverFromNode(node);
            items.Add(new SearchItem(id, title, url, cover));
        }

        return items;
    }

    public static List<ChapterItem> Chapters(string html)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        HtmlNodeCollection? nodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '/chapters/')]");
        if (nodes is null)
            return [];

        List<ChapterItem> items = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (HtmlNode node in nodes)
        {
            string href = node.GetAttributeValue("href", "");
            Match idMatch = ChapterHref.Match(href);
            if (!idMatch.Success)
                continue;
            string chapterId = idMatch.Groups["id"].Value;
            if (!seen.Add(chapterId))
                continue;

            string? label = ChapterLabel(node);
            if (label is null || !TryParseChapterLabel(label, out string number, out int? volume))
                continue;

            items.Add(new ChapterItem(number, volume, chapterId, $"https://weebcentral.com/chapters/{chapterId}"));
        }

        return items;
    }

    /// <summary>
    /// Chapter reader pages are an HTMX shell. Images come from this fragment.
    /// Without reading_style=long_strip the endpoint returns 400 and no pages.
    /// </summary>
    public static string ImageFragmentUrl(string chapterUrl)
    {
        if (!Uri.TryCreate(chapterUrl, UriKind.Absolute, out Uri? uri))
            return chapterUrl;

        string path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/images", StringComparison.OrdinalIgnoreCase))
            path += "/images";

        UriBuilder builder = new(uri)
        {
            Path = path,
            Query = "is_prev=False&current_page=1&reading_style=long_strip"
        };
        return builder.Uri.ToString();
    }

    public static string[] ImageUrls(string html)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        HtmlNodeCollection? nodes = doc.DocumentNode.SelectNodes("//img");
        if (nodes is null)
            return [];

        List<string> pageAlts = [];
        List<string> other = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (HtmlNode node in nodes)
        {
            string src = node.GetAttributeValue("src", "");
            if (string.IsNullOrEmpty(src))
                src = node.GetAttributeValue("data-src", "");
            if (!TryNormalizePageSrc(src, out string url) || !seen.Add(url))
                continue;

            string alt = node.GetAttributeValue("alt", "");
            if (IsUiImage(alt, url))
                continue;
            if (alt.StartsWith("Page", StringComparison.OrdinalIgnoreCase))
                pageAlts.Add(url);
            else
                other.Add(url);
        }

        return pageAlts.Count > 0 ? pageAlts.ToArray() : other.ToArray();
    }

    private static bool TryNormalizePageSrc(string src, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        if (src.StartsWith("//"))
            url = "https:" + src;
        else if (src.StartsWith('/'))
            url = "https://weebcentral.com" + src;
        else
            url = src;
        return true;
    }

    private static bool IsUiImage(string alt, string url)
    {
        return alt.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
               alt.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("/static/", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("broken_image", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("brand.png", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ChapterLabel(HtmlNode node)
    {
        HtmlNode? span =
            node.SelectSingleNode(".//span[@class='']") ??
            node.SelectSingleNode(".//span[contains(@class,'grow')]//span") ??
            node.SelectSingleNode(".//span[contains(@class,'grow')]");
        string text = HtmlEntity.DeEntitize(span?.InnerText ?? node.InnerText ?? "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    internal static bool TryParseChapterLabel(string text, out string chapterNumber, out int? volumeNumber)
    {
        chapterNumber = "";
        volumeNumber = null;
        Match vol = VolumeNumber.Match(text);
        if (vol.Success && int.TryParse(vol.Groups[1].Value, out int parsedVolume))
            volumeNumber = parsedVolume;

        Match ch = ChapterNumber.Match(text);
        if (ch.Success)
        {
            chapterNumber = ch.Groups[1].Value;
            return true;
        }

        MatchCollection numbers = AnyNumber.Matches(text);
        if (numbers.Count == 0)
            return false;
        chapterNumber = numbers[^1].Value;
        return true;
    }

    private static string TitleFromNode(HtmlNode node, string slug)
    {
        HtmlNode? img = node.SelectSingleNode(".//img");
        string fromAlt = HtmlEntity.DeEntitize(img?.GetAttributeValue("alt", "") ?? "").Trim();
        if (fromAlt.Length > 0 &&
            !fromAlt.Equals("cover", StringComparison.OrdinalIgnoreCase) &&
            !fromAlt.Contains("cover", StringComparison.OrdinalIgnoreCase))
            return fromAlt;

        string text = HtmlEntity.DeEntitize(node.InnerText ?? "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length is > 0 and < 160)
            return text;

        if (!string.IsNullOrWhiteSpace(slug))
            return slug.Replace('-', ' ').Replace('_', ' ').Trim();
        return "Unknown";
    }

    private static string CoverFromNode(HtmlNode node)
    {
        HtmlNode? img = node.SelectSingleNode(".//img");
        string src = img?.GetAttributeValue("src", "") ?? img?.GetAttributeValue("data-src", "") ?? "";
        if (string.IsNullOrWhiteSpace(src))
            return "";
        if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return src;
        if (src.StartsWith("//"))
            return "https:" + src;
        return "https://temp.compsci88.com" + (src.StartsWith('/') ? src : "/" + src);
    }

    private static string AbsoluteUrl(string href, string fallback)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out Uri? abs))
            return abs.ToString();
        if (Uri.TryCreate(new Uri("https://weebcentral.com"), href, out Uri? rel))
            return rel.ToString();
        return fallback;
    }
}
