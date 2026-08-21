using System.Text.RegularExpressions;
using System.Web;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using HtmlAgilityPack;

namespace API.MangaConnectors;

internal static class HtmlFetch
{
    private static readonly Regex ChapterNumberInText = new(
        @"(?:ch(?:apter)?\.?\s*|c)(\d+(?:\.\d+)*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VolumeInText = new(
        @"(?:vol(?:ume)?\.?\s*|v)(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? GetHtml(IDownloadClient client, string url, string? referrer = null)
    {
        HttpResponseMessage response = client.MakeRequest(url, RequestType.Default, referrer).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            return null;
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    public static HtmlDocument? GetDocument(IDownloadClient client, string url, string? referrer = null)
    {
        string? html = GetHtml(client, url, referrer);
        if (html is null)
            return null;
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        return doc;
    }

    public static string Absolute(string pageUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return href;
        href = HttpUtility.HtmlDecode(href.Trim());
        if (href.StartsWith("//", StringComparison.Ordinal))
            return "https:" + href;
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return href;
        return new Uri(new Uri(pageUrl), href).ToString();
    }

    public static string? Text(HtmlNode? node)
    {
        if (node is null)
            return null;
        return HttpUtility.HtmlDecode(node.InnerText ?? string.Empty).Trim();
    }

    public static string? Attr(HtmlNode? node, params string[] names)
    {
        if (node is null)
            return null;
        foreach (string name in names)
        {
            string value = node.GetAttributeValue(name, string.Empty);
            if (!string.IsNullOrWhiteSpace(value))
                return HttpUtility.HtmlDecode(value.Trim());
        }
        return null;
    }

    public static string? ChapterNumberFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        Match match = ChapterNumberInText.Match(text);
        if (match.Success)
            return match.Groups[1].Value;
        MatchCollection numbers = Regex.Matches(text, @"\d+(?:\.\d+)*");
        return numbers.Count > 0 ? numbers[^1].Value : null;
    }

    public static int? VolumeFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        Match match = VolumeInText.Match(text);
        if (!match.Success)
            return null;
        return int.TryParse(match.Groups[1].Value, out int volume) ? volume : null;
    }

    public static IEnumerable<HtmlNode> Select(HtmlDocument doc, params string[] xpaths)
    {
        foreach (string xpath in xpaths)
        {
            HtmlNodeCollection? nodes = doc.DocumentNode.SelectNodes(xpath);
            if (nodes is { Count: > 0 })
                return nodes;
        }
        return [];
    }

    public static Chapter? TryChapter(Manga parent, string chapterNumber, int? volume, string? title)
    {
        try
        {
            return new Chapter(parent, chapterNumber, volume, title);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static HtmlNode? SelectOne(HtmlDocument doc, params string[] xpaths)
    {
        foreach (string xpath in xpaths)
        {
            HtmlNode? node = doc.DocumentNode.SelectSingleNode(xpath);
            if (node is not null)
                return node;
        }
        return null;
    }
}
