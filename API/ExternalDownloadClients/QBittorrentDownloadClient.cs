using System.Net;
using log4net;
using Newtonsoft.Json.Linq;
using API.IndexerConnectors;

namespace API.ExternalDownloadClients;

/// <summary>Torrent download client backed by the qBittorrent WebUI API (v2).</summary>
public class QBittorrentDownloadClient : IExternalDownloadClient
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(QBittorrentDownloadClient));
    private static readonly HttpClientHandler Handler = new() { UseCookies = true, CookieContainer = new CookieContainer() };
    private static readonly HttpClient Client = new(Handler) { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim LoginLock = new(1, 1);
    private static bool _loggedIn;

    private static readonly HashSet<string> DownloadingStates =
    [
        "downloading", "stalledDL", "metaDL", "queuedDL", "checkingDL", "forcedDL", "allocating", "checkingResumeData"
    ];
    private static readonly HashSet<string> FailedStates = ["error", "missingFiles"];

    public async Task<string?> Add(IndexerRelease release, CancellationToken cancellationToken)
    {
        if (!await EnsureLoggedIn(cancellationToken))
            return null;

        string tag = $"mangette-{Guid.NewGuid():N}";

        using MultipartFormDataContent form = BuildAddForm(release.DownloadUrl, tag);
        HttpResponseMessage response = await Client.PostAsync(
            $"{Mangette.Settings.QBittorrentUrl}/api/v2/torrents/add", form, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden && await Reauthenticate(cancellationToken))
        {
            using MultipartFormDataContent retryForm = BuildAddForm(release.DownloadUrl, tag);
            response = await Client.PostAsync($"{Mangette.Settings.QBittorrentUrl}/api/v2/torrents/add", retryForm, cancellationToken);
        }
        if (!response.IsSuccessStatusCode)
        {
            Log.Error($"qBittorrent add failed for \"{release.Title}\": {(int)response.StatusCode} {response.StatusCode}");
            return null;
        }

        // torrents/add does not return the hash directly; look it up by the tag we just set.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(500, cancellationToken);
            JArray info = await GetTorrentsInfo($"tag={Uri.EscapeDataString(tag)}", cancellationToken);
            if (info.Count > 0 && info[0].Value<string>("hash") is { Length: > 0 } hash)
                return hash;
        }

        Log.Warn($"qBittorrent accepted \"{release.Title}\" but it never showed up under tag {tag}.");
        return null;
    }

    /// <summary>
    /// The category (Settings → qBittorrent) routes Comic torrents to their own Default Save Path in
    /// qBittorrent's own Categories config, instead of the general download directory.
    /// </summary>
    private static MultipartFormDataContent BuildAddForm(string downloadUrl, string tag)
    {
        MultipartFormDataContent form = new()
        {
            { new StringContent(downloadUrl), "urls" },
            { new StringContent(tag), "tags" }
        };
        if (!string.IsNullOrWhiteSpace(Mangette.Settings.QBittorrentCategory))
            form.Add(new StringContent(Mangette.Settings.QBittorrentCategory), "category");
        return form;
    }

    public async Task<ExternalDownloadStatus> GetStatus(string externalId, CancellationToken cancellationToken)
    {
        if (!await EnsureLoggedIn(cancellationToken))
            return new ExternalDownloadStatus(false, 0, null, true, "Could not log in to qBittorrent.");

        JArray info = await GetTorrentsInfo($"hashes={externalId}", cancellationToken);
        if (info.Count == 0)
            return new ExternalDownloadStatus(false, 0, null, true, "Torrent no longer exists in qBittorrent.");

        JToken torrent = info[0];
        string state = torrent.Value<string>("state") ?? "";
        double progress = torrent.Value<double?>("progress") ?? 0;

        if (FailedStates.Contains(state))
            return new ExternalDownloadStatus(false, progress, null, true, $"qBittorrent reported state '{state}'.");

        bool done = progress >= 1.0 && !DownloadingStates.Contains(state);
        string? outputPath = done ? torrent.Value<string>("content_path") : null;
        return new ExternalDownloadStatus(done, progress, outputPath, false, null);
    }

    public async Task Remove(string externalId, bool deleteFiles, CancellationToken cancellationToken)
    {
        if (!await EnsureLoggedIn(cancellationToken))
            return;
        Dictionary<string, string> form = new() { ["hashes"] = externalId, ["deleteFiles"] = deleteFiles ? "true" : "false" };
        await Client.PostAsync($"{Mangette.Settings.QBittorrentUrl}/api/v2/torrents/delete", new FormUrlEncodedContent(form), cancellationToken);
    }

    private static async Task<JArray> GetTorrentsInfo(string query, CancellationToken cancellationToken)
    {
        string url = $"{Mangette.Settings.QBittorrentUrl}/api/v2/torrents/info?{query}";
        HttpResponseMessage response = await Client.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden && await Reauthenticate(cancellationToken))
            response = await Client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JArray.Parse(body);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not parse qBittorrent torrents/info response: {ex.Message}", ex);
            return [];
        }
    }

    private static Task<bool> Reauthenticate(CancellationToken cancellationToken)
    {
        _loggedIn = false;
        return EnsureLoggedIn(cancellationToken);
    }

    private static async Task<bool> EnsureLoggedIn(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.QBittorrentUrl))
        {
            Log.Debug("qBittorrent is not configured.");
            return false;
        }
        if (_loggedIn)
            return true;

        await LoginLock.WaitAsync(cancellationToken);
        try
        {
            if (_loggedIn)
                return true;

            Dictionary<string, string> form = new()
            {
                ["username"] = Mangette.Settings.QBittorrentUsername,
                ["password"] = Mangette.Settings.QBittorrentPassword
            };
            HttpResponseMessage response = await Client.PostAsync(
                $"{Mangette.Settings.QBittorrentUrl}/api/v2/auth/login", new FormUrlEncodedContent(form), cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            _loggedIn = response.IsSuccessStatusCode && body.Trim().Equals("Ok.", StringComparison.OrdinalIgnoreCase);
            if (!_loggedIn)
                Log.Error($"qBittorrent login failed: {(int)response.StatusCode} {response.StatusCode} {body}");
            return _loggedIn;
        }
        finally
        {
            LoginLock.Release();
        }
    }
}
