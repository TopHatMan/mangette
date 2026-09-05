using System.Net;
using System.Web;
using log4net;
using Newtonsoft.Json.Linq;

namespace API.IndexerConnectors;

/// <summary>
/// Searches every indexer configured in a Prowlarr instance through Prowlarr's aggregated search
/// endpoint. Mangette never talks Torznab/Newznab directly or stores per-indexer credentials;
/// Prowlarr fans the query out and returns a single unified result list.
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
