using System.Net;
using System.Web;
using log4net;
using Newtonsoft.Json.Linq;

namespace API.IndexerConnectors;

/// <summary>One indexer configured in Prowlarr, for the "which indexers should Comics search" picker.</summary>
public sealed record ProwlarrIndexerInfo(int Id, string Name, ReleaseProtocol Protocol, bool EnabledInProwlarr, bool SelectedForComics);

/// <summary>
/// Searches indexers configured in a Prowlarr instance through Prowlarr's aggregated search
/// endpoint. Mangette never talks Torznab/Newznab directly or stores per-indexer credentials;
/// Prowlarr fans the query out and returns a single unified result list. Which of Prowlarr's
/// indexers actually participate is controlled by <see cref="MangetteSettings.ComicEnabledIndexerIds"/>
/// (Settings -> Indexers) -- like Readarr/Sonarr/Radarr picking indexers per app, not every indexer
/// Prowlarr knows about necessarily makes sense for comics.
/// </summary>
public class ProwlarrIndexerConnector : IIndexerConnector
{
    /// <summary>
    /// A reasonable starting point if a user wants to narrow searches by category in Settings --
    /// covers the whole Newznab/Torznab Books tree (7030 "Books/Comics" plus its usual siblings),
    /// since indexers are wildly inconsistent about which of these they actually tag comics with.
    /// Not applied automatically: <see cref="Search"/> sends no category filter unless
    /// <see cref="MangetteSettings.ComicSearchCategories"/> is explicitly set, because even this
    /// whole tree was observed silently filtering out real results a plain, unfiltered manual
    /// Prowlarr search found -- some indexers don't declare Books/Comics support in their Torznab
    /// capabilities even though they return results that get tagged as such.
    /// </summary>
    public static readonly int[] DefaultCategories = [7000, 7010, 7020, 7030, 7040, 7060];

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private static readonly ILog Log = LogManager.GetLogger(typeof(ProwlarrIndexerConnector));

    /// <summary>
    /// All indexers configured in Prowlarr, flagged with whether Comics is set to use them.
    /// Throws on a connection/auth failure instead of returning empty, so the caller can tell
    /// "Prowlarr has none configured" apart from "couldn't reach Prowlarr".
    /// </summary>
    /// <exception cref="InvalidOperationException">Prowlarr is not configured, unreachable, or rejected the request.</exception>
    public async Task<ProwlarrIndexerInfo[]> GetIndexers(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ProwlarrUrl) || string.IsNullOrWhiteSpace(Mangette.Settings.ProwlarrApiKey))
            throw new InvalidOperationException("Prowlarr URL and API key are required.");

        HttpRequestMessage request = new(HttpMethod.Get, $"{Mangette.Settings.ProwlarrUrl}/api/v1/indexer");
        request.Headers.TryAddWithoutValidation("X-Api-Key", Mangette.Settings.ProwlarrApiKey);

        JArray results;
        try
        {
            HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Log.Error($"Prowlarr indexer list returned {(int)response.StatusCode} {response.StatusCode}.");
                throw new InvalidOperationException($"Prowlarr returned {(int)response.StatusCode} {response.StatusCode}.");
            }
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            results = JArray.Parse(body);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not load Prowlarr indexers: {ex.Message}", ex);
            throw new InvalidOperationException($"Cannot connect to {Mangette.Settings.ProwlarrUrl}: {ex.Message}", ex);
        }

        HashSet<int> selected = Mangette.Settings.ComicEnabledIndexerIds.ToHashSet();
        List<ProwlarrIndexerInfo> indexers = [];
        foreach (JToken item in results)
        {
            int? id = item.Value<int?>("id");
            string? name = item.Value<string>("name");
            if (id is null || string.IsNullOrWhiteSpace(name))
                continue;
            string protocolRaw = item.Value<string>("protocol") ?? "torrent";
            ReleaseProtocol protocol = protocolRaw.Equals("usenet", StringComparison.OrdinalIgnoreCase)
                ? ReleaseProtocol.Usenet
                : ReleaseProtocol.Torrent;
            bool enabledInProwlarr = item.Value<bool?>("enable") ?? true;
            // Nothing explicitly selected yet = search every enabled indexer (today's default behavior).
            bool selectedForComics = selected.Count == 0 ? enabledInProwlarr : selected.Contains(id.Value);
            indexers.Add(new ProwlarrIndexerInfo(id.Value, name, protocol, enabledInProwlarr, selectedForComics));
        }
        return indexers.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Builds the exact query string <see cref="Search"/> sends to Prowlarr (minus the API key,
    /// which travels as a header, never in the URL) -- exposed separately so a diagnostic endpoint
    /// can show a user the literal request without duplicating this logic or needing server logs.
    /// </summary>
    /// <param name="query">Free-text search query.</param>
    /// <param name="type">
    /// Prowlarr's search "type" -- a real indexer (confirmed live: NZBgeek, a Usenet indexer) can
    /// return releases through Prowlarr's own manual Search page yet return zero through this API
    /// with type "search", so this is overridable per-call purely for the Settings diagnostic tool
    /// to test alternate values (e.g. "book-search") without a code change + redeploy each time.
    /// Real (non-diagnostic) searches always use the default "search".
    /// </param>
    public static string BuildSearchUrl(string query, string type = "search")
    {
        // No category filter by default -- matches a plain Prowlarr manual search (blank Categories
        // field), which regularly finds results this used to filter out (see DefaultCategories doc).
        string requestUrl =
            $"{Mangette.Settings.ProwlarrUrl}/api/v1/search" +
            $"?query={HttpUtility.UrlEncode(query)}" +
            $"&type={HttpUtility.UrlEncode(type)}";
        if (Mangette.Settings.ComicSearchCategories is { Count: > 0 } categories)
            requestUrl += $"&categories={string.Join(',', categories)}";
        // Empty selection = search every indexer Prowlarr has (matches Prowlarr's own default).
        foreach (int id in Mangette.Settings.ComicEnabledIndexerIds)
            requestUrl += $"&indexerIds={id}";
        return requestUrl;
    }

    /// <summary>
    /// Full detail behind one <see cref="Search"/> call -- what <see cref="ProwlarrIndexerConnector.Search"/>
    /// itself only logs, exposed here so a diagnostic endpoint can hand it straight to a user without
    /// them needing server log access to tell "Prowlarr returned nothing" apart from "Prowlarr returned
    /// results Mangette failed to parse".
    /// </summary>
    public sealed record SearchDiagnostics(
        string RequestUrl, int? HttpStatus, string? Error, int RawResultCount, IndexerRelease[] Releases, string? FirstSkippedRaw = null);

    public async Task<IndexerRelease[]> Search(string query, CancellationToken cancellationToken) =>
        (await SearchWithDiagnostics(query, cancellationToken)).Releases;

    public async Task<SearchDiagnostics> SearchWithDiagnostics(string query, CancellationToken cancellationToken, string type = "search")
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ProwlarrUrl) ||
            string.IsNullOrWhiteSpace(Mangette.Settings.ProwlarrApiKey))
        {
            Log.Debug("Prowlarr is not configured.");
            return new SearchDiagnostics("", null, "Prowlarr is not configured.", 0, []);
        }

        string requestUrl = BuildSearchUrl(query, type);

        // Logged at Info (not Debug) on purpose: this is the single most useful line for diagnosing
        // "Prowlarr finds it manually but Mangette doesn't" -- it shows exactly what was sent, with
        // no need to reproduce with a debugger. The API key never appears in requestUrl (it's a header).
        Log.InfoFormat("Prowlarr search request: {0}", requestUrl);

        HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("X-Api-Key", Mangette.Settings.ProwlarrApiKey);

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error($"Prowlarr search failed for \"{query}\": {ex.Message}", ex);
            return new SearchDiagnostics(requestUrl, null, ex.Message, 0, []);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Log.Error($"Prowlarr rejected the API key ({(int)response.StatusCode}).");
            return new SearchDiagnostics(requestUrl, (int)response.StatusCode, "Prowlarr rejected the API key.", 0, []);
        }
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Log.Error($"Prowlarr search for \"{query}\" returned {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
            return new SearchDiagnostics(requestUrl, (int)response.StatusCode, errorBody, 0, []);
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        JArray results;
        try
        {
            results = JArray.Parse(body);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not parse Prowlarr response for \"{query}\": {ex.Message}. Body: {body}", ex);
            return new SearchDiagnostics(requestUrl, (int)response.StatusCode, $"Could not parse Prowlarr's response: {ex.Message}", 0, []);
        }

        List<IndexerRelease> releases = [];
        string? firstSkippedRaw = null;
        int skipped = 0;
        foreach (JToken item in results)
        {
            if (ParseRelease(item) is { } release)
            {
                releases.Add(release);
            }
            else
            {
                skipped++;
                firstSkippedRaw ??= item.ToString(Newtonsoft.Json.Formatting.None);
            }
        }
        if (skipped > 0)
        {
            Log.WarnFormat("Prowlarr search \"{0}\": {1} of {2} raw result(s) were missing a title/downloadUrl/protocol and were skipped.", query, skipped, results.Count);
            Log.WarnFormat("First skipped raw result (field names Mangette expected didn't match): {0}", firstSkippedRaw);
        }

        Log.InfoFormat("Prowlarr search \"{0}\" returned {1} release(s) (raw: {2}).", query, releases.Count, results.Count);
        return new SearchDiagnostics(requestUrl, (int)response.StatusCode, null, results.Count, releases.ToArray(), firstSkippedRaw);
    }

    /// <summary>
    /// Case-insensitive, multi-name-aliased lookup -- Prowlarr's actual field casing/naming has
    /// proven inconsistent enough across releases (e.g. a magnet-only torrent release has no
    /// "downloadUrl" but does have "guid" or "magnetUrl") that a single exact key lookup silently
    /// dropped every result for some indexers even though Prowlarr returned them correctly.
    /// </summary>
    internal static string? GetString(JObject obj, params string[] names)
    {
        foreach (string name in names)
        {
            if (obj.Property(name, StringComparison.OrdinalIgnoreCase)?.Value is { Type: not JTokenType.Null } value &&
                value.Value<string>() is { Length: > 0 } s)
                return s;
        }
        return null;
    }

    internal static IndexerRelease? ParseRelease(JToken item)
    {
        if (item is not JObject obj)
            return null;

        string? title = GetString(obj, "title");
        string? downloadUrl = GetString(obj, "downloadUrl", "magnetUrl", "link", "guid");
        string? protocolRaw = GetString(obj, "protocol");
        if (title is null || downloadUrl is null || protocolRaw is null)
            return null;

        ReleaseProtocol protocol = protocolRaw.Equals("usenet", StringComparison.OrdinalIgnoreCase)
            ? ReleaseProtocol.Usenet
            : ReleaseProtocol.Torrent;

        string? sizeRaw = GetString(obj, "size");
        string? publishDateRaw = GetString(obj, "publishDate");
        string? seedersRaw = GetString(obj, "seeders");

        return new IndexerRelease(
            title,
            downloadUrl,
            GetString(obj, "infoUrl"),
            protocol,
            GetString(obj, "indexer") ?? "Prowlarr",
            long.TryParse(sizeRaw, out long size) ? size : 0,
            DateTime.TryParse(publishDateRaw, out DateTime publishDate) ? publishDate : DateTime.UtcNow,
            int.TryParse(seedersRaw, out int seeders) ? seeders : null);
    }
}
