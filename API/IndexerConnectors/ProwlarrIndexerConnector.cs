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

    public async Task<IndexerRelease[]> Search(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ProwlarrUrl) ||
            string.IsNullOrWhiteSpace(Mangette.Settings.ProwlarrApiKey))
        {
            Log.Debug("Prowlarr is not configured.");
            return [];
        }

        // No category filter by default -- matches a plain Prowlarr manual search (blank Categories
        // field), which regularly finds results this used to filter out (see DefaultCategories doc).
        string requestUrl =
            $"{Mangette.Settings.ProwlarrUrl}/api/v1/search" +
            $"?query={HttpUtility.UrlEncode(query)}" +
            $"&type=search";
        if (Mangette.Settings.ComicSearchCategories is { Count: > 0 } categories)
            requestUrl += $"&categories={string.Join(',', categories)}";
        // Empty selection = search every indexer Prowlarr has (matches Prowlarr's own default).
        foreach (int id in Mangette.Settings.ComicEnabledIndexerIds)
            requestUrl += $"&indexerIds={id}";

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
            return [];
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Log.Error($"Prowlarr rejected the API key ({(int)response.StatusCode}).");
            return [];
        }
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Log.Error($"Prowlarr search for \"{query}\" returned {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
            return [];
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
            return [];
        }

        List<IndexerRelease> releases = [];
        int skipped = 0;
        foreach (JToken item in results)
        {
            if (ParseRelease(item) is { } release)
                releases.Add(release);
            else
                skipped++;
        }
        if (skipped > 0)
            Log.WarnFormat("Prowlarr search \"{0}\": {1} of {2} raw result(s) were missing a title/downloadUrl/protocol and were skipped.", query, skipped, results.Count);

        Log.InfoFormat("Prowlarr search \"{0}\" returned {1} release(s) (raw: {2}).", query, releases.Count, results.Count);
        return releases.ToArray();
    }

    private static IndexerRelease? ParseRelease(JToken item)
    {
        string? title = item.Value<string>("title");
        string? downloadUrl = item.Value<string>("downloadUrl");
        string? protocolRaw = item.Value<string>("protocol");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(downloadUrl) ||
            string.IsNullOrWhiteSpace(protocolRaw))
            return null;

        ReleaseProtocol protocol = protocolRaw.Equals("usenet", StringComparison.OrdinalIgnoreCase)
            ? ReleaseProtocol.Usenet
            : ReleaseProtocol.Torrent;

        return new IndexerRelease(
            title,
            downloadUrl,
            item.Value<string?>("infoUrl"),
            protocol,
            item.Value<string?>("indexer") ?? "Prowlarr",
            item.Value<long?>("size") ?? 0,
            item.Value<DateTime?>("publishDate") ?? DateTime.UtcNow,
            item.Value<int?>("seeders"));
    }
}
