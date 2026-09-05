using System.Web;
using log4net;
using Newtonsoft.Json.Linq;
using API.IndexerConnectors;

namespace API.ExternalDownloadClients;

/// <summary>Usenet download client backed by the SABnzbd REST API.</summary>
public class SabnzbdDownloadClient : IExternalDownloadClient
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(SabnzbdDownloadClient));
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<string?> Add(IndexerRelease release, CancellationToken cancellationToken)
    {
        if (!IsConfigured())
            return null;

        // The category (Settings -> SABnzbd) routes Comic NZBs to their own Folder/Category Dir in
        // SABnzbd's own Categories config, instead of the default completed-download directory.
        string category = string.IsNullOrWhiteSpace(Mangette.Settings.SabnzbdCategory)
            ? ""
            : $"&cat={HttpUtility.UrlEncode(Mangette.Settings.SabnzbdCategory)}";
        string requestUrl = $"{Mangette.Settings.SabnzbdUrl}/api" +
                             $"?mode=addurl" +
                             $"&name={HttpUtility.UrlEncode(release.DownloadUrl)}" +
                             $"&nzbname={HttpUtility.UrlEncode(release.Title)}" +
                             $"&apikey={Mangette.Settings.SabnzbdApiKey}" +
                             category +
                             $"&output=json";

        JObject? result = await Get(requestUrl, cancellationToken);
        if (result is null)
            return null;

        if (result.Value<bool?>("status") != true)
        {
            Log.Error($"SABnzbd rejected \"{release.Title}\": {result.Value<string>("error")}");
            return null;
        }

        return result.Value<JArray>("nzo_ids")?.FirstOrDefault()?.Value<string>();
    }

    public async Task<ExternalDownloadStatus> GetStatus(string externalId, CancellationToken cancellationToken)
    {
        if (!IsConfigured())
            return new ExternalDownloadStatus(false, 0, null, true, "SABnzbd is not configured.");

        // Still queued/downloading?
        JObject? queue = await Get(
            $"{Mangette.Settings.SabnzbdUrl}/api?mode=queue&apikey={Mangette.Settings.SabnzbdApiKey}&output=json",
            cancellationToken);
        JToken? slot = queue?.Value<JObject>("queue")?.Value<JArray>("slots")
            ?.FirstOrDefault(s => s.Value<string>("nzo_id") == externalId);
        if (slot is not null)
        {
            double percentage = slot.Value<double?>("percentage") ?? 0;
            return new ExternalDownloadStatus(false, percentage / 100.0, null, false, null);
        }

        // Not in the queue anymore -- check history for the finished (or failed) result.
        JObject? history = await Get(
            $"{Mangette.Settings.SabnzbdUrl}/api?mode=history&apikey={Mangette.Settings.SabnzbdApiKey}&output=json&nzo_ids={externalId}",
            cancellationToken);
        JToken? historySlot = history?.Value<JObject>("history")?.Value<JArray>("slots")?.FirstOrDefault();
        if (historySlot is null)
            return new ExternalDownloadStatus(false, 0, null, true, "SABnzbd has no record of this download.");

        string status = historySlot.Value<string>("status") ?? "";
        if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            return new ExternalDownloadStatus(false, 0, null, true, historySlot.Value<string>("fail_message"));

        bool done = status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        string? outputPath = done ? historySlot.Value<string>("storage") : null;
        return new ExternalDownloadStatus(done, done ? 1.0 : 0.5, outputPath, false, null);
    }

    public async Task Remove(string externalId, bool deleteFiles, CancellationToken cancellationToken)
    {
        if (!IsConfigured())
            return;
        string mode = deleteFiles ? "history" : "queue";
        await Get(
            $"{Mangette.Settings.SabnzbdUrl}/api?mode={mode}&name=delete&value={externalId}&del_files=1&apikey={Mangette.Settings.SabnzbdApiKey}&output=json",
            cancellationToken);
    }

    private static bool IsConfigured()
    {
        if (!string.IsNullOrWhiteSpace(Mangette.Settings.SabnzbdUrl) && !string.IsNullOrWhiteSpace(Mangette.Settings.SabnzbdApiKey))
            return true;
        Log.Debug("SABnzbd is not configured.");
        return false;
    }

    private static async Task<JObject?> Get(string url, CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response = await Client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Log.Error($"SABnzbd request failed: {(int)response.StatusCode} {response.StatusCode}");
                return null;
            }
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JObject.Parse(body);
        }
        catch (Exception ex)
        {
            Log.Error($"SABnzbd request threw: {ex.Message}", ex);
            return null;
        }
    }
}
