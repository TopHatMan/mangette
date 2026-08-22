using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using log4net;
using System.Collections.Generic;
using System.Linq; // For OrderBy
using System.Text.Json;
using System.Text;
using System.Threading;

namespace API.MangaConnectors;

public class WeebCentral : MangaConnector
{
    public WeebCentral() : base("WeebCentral", new[] { "en" }, new[] { "weebcentral.com" }, "https://weebcentral.com/static/images/brand.png")
    {
        this.downloadClient = new HttpDownloadClient(); // Use Http for all
    }

    public override (Manga, MangaConnectorId<Manga>)[] SearchManga(string mangaSearchName)
    {
        Log.InfoFormat("Searching: {0}", mangaSearchName);
        string sanitizedTitle = string.Join(' ', Regex.Matches(mangaSearchName, @"[A-Za-z]+").Where(m => m.Value.Length > 0)).ToLowerInvariant();
        string requestUrl = $"https://weebcentral.com/search/data?limit=32&offset=0&text={HttpUtility.UrlEncode(sanitizedTitle)}&sort=Best+Match&order=Ascending&official=Any&display_mode=Minimal%20Display";
        HttpResponseMessage response = downloadClient.MakeRequest(requestUrl, RequestType.Default).GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Request failed or no HTML retrieved");
            return [];
        }

        string html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Log.DebugFormat("Search HTML length: {0}", html.Length);

        List<(Manga, MangaConnectorId<Manga>)> mangas = new();
        foreach (WeebCentralParse.SearchItem item in WeebCentralParse.SearchResults(html))
        {
            Manga manga = new(item.Title, "", item.CoverUrl, MangaReleaseStatus.Continuing, [], [], [], [], null, 0f, null, null);
            MangaConnectorId<Manga> mcId = new(manga, this, item.Id, item.Url);
            manga.MangaConnectorIds.Add(mcId);
            mangas.Add((manga, mcId));
            if (mangas.Count >= 12)
                break;
        }

        Log.InfoFormat("Search '{0}' yielded {1} preview results (not saved to library).", mangaSearchName, mangas.Count);
        return mangas.ToArray();
    }

   public override (Manga, MangaConnectorId<Manga>)? GetMangaFromUrl(string url)
    {
        Log.InfoFormat("Fetching manga from URL: {0}", url);
        // Robust regex: Capture full slug before optional UID
        Match urlMatch = Regex.Match(url, @"https?://(?:www\.)?weebcentral\.com/series/(?<uniqueId>[^/]+)/(?<coreSlug>[^/]+)");
        if (!urlMatch.Success)
            return null;

        string coreSlug = urlMatch.Groups["uniqueId"].Value;
        string storedUrl = $"https://weebcentral.com/series/{coreSlug}";  // Stable wildcard

        // Fetch once using full url (no double fetch)
        HttpResponseMessage response = downloadClient.MakeRequest(url, RequestType.MangaInfo).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Failed to retrieve manga page");
            return null;
        }

        string html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        HtmlDocument doc = new();
        doc.LoadHtml(html);

        return ParseMangaFromHtml(doc, coreSlug, storedUrl);
    }

    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromId(string mangaIdOnSite)
    {
        string url = $"https://weebcentral.com/series/{mangaIdOnSite}";
        HttpResponseMessage response = downloadClient.MakeRequest(url, RequestType.MangaInfo).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string msg = $"WeebCentral {url} returned HTTP {(int)response.StatusCode} {response.StatusCode}";
            Log.Error(msg);
            throw new InvalidOperationException(msg);
        }

        string html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        return ParseMangaFromHtml(doc, mangaIdOnSite, url);
    }

    private (Manga, MangaConnectorId<Manga>) ParseMangaFromHtml(HtmlDocument doc, string mangaIdOnSite, string url)
    {
        // Title with cleanup (kept for robustness, but simple decode to match original)
        HtmlNode? titleNode = doc.DocumentNode.SelectSingleNode("//title");
        string rawTitle = titleNode?.InnerText ?? mangaIdOnSite;

		Match m = Regex.Match(rawTitle,@"^(.*?)\s*\|\s*Weeb.*$",RegexOptions.IgnoreCase);

		string cleanTitle = m.Success ? m.Groups[1].Value.Trim() : rawTitle;
		cleanTitle = HtmlEntity.DeEntitize(cleanTitle); // Simple decode like original

        // Cover
        HtmlNode? coverNode = doc.DocumentNode.SelectSingleNode("//img[contains(@alt, 'cover')]");
        string coverUrl = coverNode?.GetAttributeValue("src", "") ?? "";
        if (!string.IsNullOrEmpty(coverUrl) && !coverUrl.StartsWith("http"))
            coverUrl = $"https://temp.compsci88.com{coverUrl}";

        // Description
        HtmlNode? descNode = doc.DocumentNode.SelectSingleNode("//strong[starts-with(text(),'Description')]/../p");
        string description = HtmlEntity.DeEntitize(descNode?.InnerText ?? "").Trim();

        // Tags
        HtmlNodeCollection? genreNodes = doc.DocumentNode.SelectNodes("//strong[starts-with(text(),'Tag')]/../span");
        List<MangaTag> tags = genreNodes?.Select(b => new MangaTag(HtmlEntity.DeEntitize(b.InnerText.Trim()))).ToList() ?? [];

        // Status
        HtmlNode? statusNode = doc.DocumentNode.SelectSingleNode("//strong[starts-with(text(),'Status')]/../a");
        string rawStatus = HtmlEntity.DeEntitize(statusNode?.InnerText ?? "").ToLowerInvariant().Trim();
        MangaReleaseStatus releaseStatus = rawStatus switch
        {
            "ongoing" => MangaReleaseStatus.Continuing,
            "hiatus" => MangaReleaseStatus.OnHiatus,
            "completed" => MangaReleaseStatus.Completed,
            "canceled" => MangaReleaseStatus.Cancelled,
            _ => MangaReleaseStatus.Unreleased
        };

        // Authors
        HtmlNodeCollection? authorNodes = doc.DocumentNode.SelectNodes("//strong[starts-with(text(),'Author')]/../span");
        List<Author> authors = authorNodes?.Select(a => new Author(HtmlEntity.DeEntitize(a.InnerText.Trim()))).ToList() ?? [];

        // Year
        HtmlNode? firstChapterNode = doc.DocumentNode.SelectSingleNode("//strong[starts-with(text(),'Released: ')]/../span");
        uint? year = null;
        if (firstChapterNode?.InnerText is { } firstText && firstText.Contains(" "))
        {
            string datePart = firstText.Split(' ').Last();
            uint.TryParse(datePart, out uint parsedYear);
            year = parsedYear > 0 ? parsedYear : null;
        }

        List<AltTitle> altTitles = new();
        List<Link> links = new();
        // Match original constructor (null language for consistent Key)
        Manga manga = new(cleanTitle, description, coverUrl, releaseStatus, authors, tags, links, altTitles, null, 0f, year, null);
        
        // Use mangaIdOnSite for ID (core slug, consistent)
        MangaConnectorId<Manga> mcId = new(manga, this, mangaIdOnSite, url);
        manga.MangaConnectorIds.Add(mcId);
        
        return (manga, mcId);
    }

    public override (Chapter, MangaConnectorId<Chapter>)[] GetChapters(MangaConnectorId<Manga> manga, string? language = null)
    {
        Log.InfoFormat("Fetching chapters for: {0}", manga.IdOnConnectorSite);

        string baseSlug = manga.IdOnConnectorSite;
        if (baseSlug.Contains("series/"))
            baseSlug = baseSlug.Substring(baseSlug.IndexOf("series/") + 7);

        string seriesUrl = $"https://weebcentral.com/series/{baseSlug}";
        string websiteUrl = $"{seriesUrl}/full-chapter-list";

        HttpResponseMessage response = downloadClient.MakeRequest(websiteUrl, RequestType.Default, seriesUrl).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Failed to load chapters page");
            return [];
        }

        string html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        List<(Chapter, MangaConnectorId<Chapter>)> chapters = new();
        foreach (WeebCentralParse.ChapterItem item in WeebCentralParse.Chapters(html))
        {
            try
            {
                Chapter ch = new(manga.Obj, item.ChapterNumber, item.VolumeNumber, null);
                MangaConnectorId<Chapter> mcId = new(ch, this, item.ChapterId, item.Url);
                ch.MangaConnectorIds.Add(mcId);
                chapters.Add((ch, mcId));
            }
            catch (ArgumentException ex)
            {
                Log.WarnFormat("Skipped chapter {0}: {1}", item.ChapterId, ex.Message);
            }
        }

        Log.InfoFormat("Found {0} chapters for {1}", chapters.Count, manga.Obj.Name);
        return chapters.OrderBy(c => c.Item1, new Chapter.ChapterComparer()).ToArray();
    }

    internal override string[] GetChapterImageUrls(MangaConnectorId<Chapter> chapterId)
    {
        Log.InfoFormat("Getting Chapter Image-Urls: {0}", chapterId.Obj);
        if (chapterId.WebsiteUrl is null)
        {
            Log.Error("Chapter URL is null");
            return [];
        }

        string? referrer = null;
        if (chapterId.Obj.ParentManga.MangaConnectorIds is not null && chapterId.Obj.ParentManga.MangaConnectorIds.Any())
        {
            referrer = chapterId.Obj.ParentManga.MangaConnectorIds
                .FirstOrDefault(id => id.MangaConnectorName == this.Name)?.WebsiteUrl;
        }

		return GetChapterImageUrlsAsync(chapterId, referrer).GetAwaiter().GetResult();
	}

	private async Task<string[]> GetChapterImageUrlsAsync(MangaConnectorId<Chapter> chapterId, string? referrer)
	{
		HttpResponseMessage response = await downloadClient.MakeRequest(chapterId.WebsiteUrl!, RequestType.Default, referrer);
		string html = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : "";

		if (!response.IsSuccessStatusCode || LooksLikeCloudflare(html))
		{
			Log.Warn("WeebCentral chapter page looks blocked or empty; trying Chromium");
			try
			{
				await using ChromiumDownloadClient chromium = new();
				response = await chromium.MakeRequest(chapterId.WebsiteUrl!, RequestType.Default, referrer);
				html = await response.Content.ReadAsStringAsync();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		if (LooksLikeCloudflare(html))
		{
			Log.Error("Failed to load WeebCentral chapter page (Cloudflare after HTTP and Chromium)");
			throw new InvalidOperationException("WeebCentral Cloudflare challenge after HTTP and Chromium");
		}

		if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
		{
			Log.Error("Failed to load WeebCentral chapter page (HTTP and Chromium)");
			return [];
		}
		
		HtmlDocument doc = new();
		doc.LoadHtml(html);

		HtmlNodeCollection? imageNodes = doc.DocumentNode.SelectNodes("//img[starts-with(@alt, 'Page')]");
		
		if (imageNodes is null || imageNodes.Count == 0)
		{
			Log.Warn("No chapter page images found");
			return [];
		}

		string[] imageUrls = imageNodes
			.Select(i => 
			{
				string src = i.GetAttributeValue("src", "");
				if (string.IsNullOrEmpty(src))
					src = i.GetAttributeValue("data-src", "");
				return src;
			})
			.Where(u => !string.IsNullOrEmpty(u))
			.ToArray();

		Log.InfoFormat("Found {0} images for chapter {1}", imageUrls.Length, chapterId.Obj);
		return imageUrls;
	}

    internal static bool LooksLikeCloudflare(string html)
    {
        if (string.IsNullOrEmpty(html) || html.Length < 120)
            return true;
        ReadOnlySpan<char> head = html.Length > 5000 ? html.AsSpan(0, 5000) : html.AsSpan();
        return head.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase) ||
               head.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase) ||
               head.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
               head.Contains("Attention Required", StringComparison.OrdinalIgnoreCase) ||
               head.Contains("cf-turnstile", StringComparison.OrdinalIgnoreCase);
    }
}
