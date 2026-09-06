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
    /// <summary>Newznab/Torznab category for Books/Comics.</summary>
    private const int ComicsCategory = 7030;

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

        string requestUrl =
            $"{Mangette.Settings.ProwlarrUrl}/api/v1/search" +
            $"?query={HttpUtility.UrlEncode(query)}" +
            $"&categories={ComicsCategory}" +
            $"&type=search";
        // Empty selection = search every indexer Prowlarr has (matches Prowlarr's own default).
        foreach (int id in Mangette.Settings.ComicEnabledIndexerIds)
            requestUrl += $"&indexerIds={id}";

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
            Log.Error($"Prowlarr search for \"{query}\" returned {(int)response.StatusCode} {response.StatusCode}.");
            return [];
        }

        JArray results;
        try
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            results = JArray.Parse(body);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not parse Prowlarr response for \"{query}\": {ex.Message}", ex);
            return [];
        }

        List<IndexerRelease> releases = [];
        foreach (JToken item in results)
        {
            if (ParseRelease(item) is { } release)
                releases.Add(release);
        }

        Log.InfoFormat("Prowlarr search \"{0}\" returned {1} release(s).", query, releases.Count);
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
