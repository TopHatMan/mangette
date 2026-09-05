using API.Controllers.Requests;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;
// ReSharper disable InconsistentNaming

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class SettingsController(MangaContext context) : ControllerBase
{
    /// <summary>
    /// Get all <see cref="Mangette.Settings"/>
    /// </summary>
    /// <response code="200"></response>
    [HttpGet]
    [ProducesResponseType<MangetteSettings>(Status200OK, "application/json")]
    public Ok<MangetteSettings> GetSettings()
    {
        return TypedResults.Ok(Mangette.Settings.ForClient());
    }

    /// <summary>
    /// Update listen port, library folder, temp downloads, and related download settings in one request.
    /// Listen port changes take effect after restart.
    /// </summary>
    [HttpPatch]
    [ProducesResponseType<MangetteSettings>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok<MangetteSettings>, BadRequest<string>, InternalServerError<string>>> PatchSetup(
        [FromBody] PatchSetupSettingsRecord requestData)
    {
        if (requestData.ListenPort is { } port)
        {
            if (port is <= 0 or >= 65536)
                return TypedResults.BadRequest("ListenPort must be between 1 and 65535.");
            Mangette.Settings.SetListenPort(port);
        }

        if (requestData.TempDownloadPath is { } tempPath)
        {
            if (string.IsNullOrWhiteSpace(tempPath))
                return TypedResults.BadRequest("TempDownloadPath cannot be empty.");
            try
            {
                Mangette.Settings.SetTempDownloadPath(tempPath);
            }
            catch (Exception ex)
            {
                return TypedResults.BadRequest($"Could not use temp download path: {ex.Message}");
            }
        }

        if (requestData.LibraryPath is { } libraryPath)
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
                return TypedResults.BadRequest("LibraryPath cannot be empty.");
            string full = MangetteSettings.NormalizeDirectory(libraryPath, MangetteSettings.DefaultDownloadLocation);
            try
            {
                Directory.CreateDirectory(full);
            }
            catch (Exception ex)
            {
                return TypedResults.BadRequest($"Could not create library folder: {ex.Message}");
            }

            // Scoped to Manga-kind libraries only: with a Comic-kind library also present, "first
            // library alphabetically" is ambiguous and this quick-setup field must never touch it.
            FileLibrary? library = await context.FileLibraries
                .Where(l => l.Kind == MediaKind.Manga)
                .OrderBy(l => l.LibraryName)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);
            if (library is null)
            {
                library = new FileLibrary(full, string.IsNullOrWhiteSpace(requestData.LibraryName) ? "Library" : requestData.LibraryName);
                context.FileLibraries.Add(library);
            }
            else
            {
                library.BasePath = full;
                if (!string.IsNullOrWhiteSpace(requestData.LibraryName))
                    library.LibraryName = requestData.LibraryName;
            }

            if (await context.Sync(HttpContext.RequestAborted, GetType(), "Update library path") is { success: false } result)
                return TypedResults.InternalServerError(result.exceptionMessage);

            Mangette.Settings.SetLibraryPath(full);
        }

        if (requestData.MaxConcurrentDownloads is { } downloads)
            Mangette.Settings.SetMaxConcurrentDownloads(downloads);
        if (requestData.MaxConcurrentWorkers is { } workers)
            Mangette.Settings.SetMaxConcurrentWorkers(workers);
        if (requestData.DownloadLanguage is { } language && !string.IsNullOrWhiteSpace(language))
            Mangette.Settings.SetDownloadLanguage(language.Trim());
        if (requestData.ChapterNamingScheme is { } scheme && !string.IsNullOrWhiteSpace(scheme))
            Mangette.Settings.SetChapterNamingScheme(scheme);
        if (requestData.FlareSolverrUrl is { } flare)
            Mangette.Settings.SetFlareSolverrUrl(flare.Trim());

        if (requestData.DefaultNewChapterCheck is { } check)
            Mangette.Settings.SetDefaultNewChapterCheck(check);

        if (requestData.AuthenticationEnabled is not null ||
            requestData.AuthUsername is not null ||
            requestData.AuthPassword is not null)
        {
            try
            {
                Mangette.Settings.SetAuthentication(
                    requestData.AuthenticationEnabled ?? Mangette.Settings.AuthenticationEnabled,
                    requestData.AuthUsername,
                    requestData.AuthPassword);
            }
            catch (Exception ex)
            {
                return TypedResults.BadRequest(ex.Message);
            }
        }

        return TypedResults.Ok(Mangette.Settings.ForClient());
    }
    
    /// <summary>
    /// Get the current UserAgent used by Mangette
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("UserAgent")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    public Ok<string> GetUserAgent()
    {
        return TypedResults.Ok(Mangette.Settings.UserAgent);
    }
    
    /// <summary>
    /// Set a new UserAgent
    /// </summary>
    /// <response code="200"></response>
    [HttpPatch("UserAgent")]
    [ProducesResponseType(Status200OK)]
    public Ok SetUserAgent([FromBody]string userAgent)
    {
        //TODO Validate
        Mangette.Settings.SetUserAgent(userAgent);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Reset the UserAgent to default
    /// </summary>
    /// <response code="200"></response>
    [HttpDelete("UserAgent")]
    [ProducesResponseType(Status200OK)]
    public Ok ResetUserAgent()
    {
        Mangette.Settings.SetUserAgent(MangetteSettings.DefaultUserAgent);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Returns Level of Image-Compression for Images
    /// </summary>
    /// <response code="200">JPEG ImageCompression-level as Integer</response>
    [HttpGet("ImageCompressionLevel")]
    [ProducesResponseType<int>(Status200OK, "text/plain")]
    public Ok<int> GetImageCompression()
    {
        return TypedResults.Ok(Mangette.Settings.ImageCompression);
    }
    
    /// <summary>
    /// Set the Image-Compression-Level for Images
    /// </summary>
    /// <param name="level">100 to disable, 0-99 for JPEG ImageCompression-Level</param>
    /// <response code="200"></response>
    /// <response code="400">Level outside permitted range</response>
    [HttpPatch("ImageCompressionLevel/{level}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status400BadRequest)]
    public Results<Ok, BadRequest> SetImageCompression(int level)
    {
        if (level < 1 || level > 100)
            return TypedResults.BadRequest();
        Mangette.Settings.UpdateImageCompression(level);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Get state of Black/White-Image setting
    /// </summary>
    /// <response code="200">True if enabled</response>
    [HttpGet("BWImages")]
    [ProducesResponseType<bool>(Status200OK, "text/plain")]
    public Ok<bool> GetBwImagesToggle()
    {
        return TypedResults.Ok(Mangette.Settings.BlackWhiteImages);
    }
    
    /// <summary>
    /// Enable/Disable conversion of Images to Black and White
    /// </summary>
    /// <param name="enabled">true to enable</param>
    /// <response code="200"></response>
    [HttpPatch("BWImages/{enabled}")]
    [ProducesResponseType(Status200OK)]
    public Ok SetBwImagesToggle(bool enabled)
    {
        Mangette.Settings.SetBlackWhiteImageEnabled(enabled);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Gets the Chapter Naming Scheme
    /// </summary>
    /// <remarks>
    /// Placeholders:
    /// %M Obj Name
    /// %V Volume
    /// %C Chapter
    /// %T Title
    /// %A Author (first in list)
    /// %I Chapter Internal ID
    /// %i Obj Internal ID
    /// %Y Year (Obj)
    ///
    /// ?_(...) replace _ with a value from above:
    /// Everything inside the braces will only be added if the value of %_ is not null
    /// </remarks>
    /// <response code="200"></response>
    [HttpGet("ChapterNamingScheme")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    public Ok<string> GetCustomNamingScheme()
    {
        return TypedResults.Ok(Mangette.Settings.ChapterNamingScheme);
    }
    
    /// <summary>
    /// Sets the Chapter Naming Scheme
    /// </summary>
    /// <remarks>
    /// Placeholders:
    /// %M Obj Name
    /// %V Volume
    /// %C Chapter
    /// %T Title
    /// %A Author (first in list)
    /// %Y Year (Obj)
    ///
    /// ?_(...) replace _ with a value from above:
    /// Everything inside the braces will only be added if the value of %_ is not null
    /// </remarks>
    /// <response code="200"></response>
    [HttpPatch("ChapterNamingScheme")]
    [ProducesResponseType(Status200OK)]
    public Ok SetCustomNamingScheme([FromBody]string namingScheme)
    {
        //TODO Move old Chapters
        Mangette.Settings.SetChapterNamingScheme(namingScheme);
        
        return TypedResults.Ok();
    }

    /// <summary>
    /// Connector names in download-failover order. The first source that has the chapter and is not cooling down is used.
    /// </summary>
    [HttpGet("ConnectorPriority")]
    [ProducesResponseType<List<string>>(Status200OK, "application/json")]
    public Ok<List<string>> GetConnectorPriority()
    {
        return TypedResults.Ok(Mangette.Settings.ConnectorPriority);
    }

    /// <summary>
    /// Set connector download-failover order. Unknown names are ignored. Missing known connectors are appended.
    /// </summary>
    [HttpPatch("ConnectorPriority")]
    [ProducesResponseType<List<string>>(Status200OK, "application/json")]
    public Ok<List<string>> SetConnectorPriority([FromBody] string[] names)
    {
        Mangette.Settings.SetConnectorPriority(names);
        return TypedResults.Ok(Mangette.Settings.ConnectorPriority);
    }

    /// <summary>
    /// Sets the FlareSolverr-URL
    /// </summary>
    /// <param name="flareSolverrUrl">URL of FlareSolverr-Instance</param>
    /// <response code="200"></response>
    [HttpPatch("FlareSolverr/Url")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<string>, BadRequest<string>>> SetFlareSolverrUrl()
    {
        using StreamReader reader = new(Request.Body);
        string raw = (await reader.ReadToEndAsync()).Trim();
        string value = raw;
        if (raw.StartsWith('"'))
        {
            try { value = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(raw) ?? ""; }
            catch { value = raw.Trim('"'); }
        }
        else if (raw.StartsWith('{'))
        {
            try
            {
                Newtonsoft.Json.Linq.JObject obj = Newtonsoft.Json.Linq.JObject.Parse(raw);
                value = (string?)obj["flareSolverrUrl"] ?? (string?)obj["url"] ?? "";
            }
            catch (Exception ex)
            {
                return TypedResults.BadRequest($"Could not read FlareSolverr URL: {ex.Message}");
            }
        }

        try
        {
            Mangette.Settings.SetFlareSolverrUrl(value);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Could not save settings.json: {ex.Message}");
        }

        return TypedResults.Ok(Mangette.Settings.FlareSolverrUrl);
    }

    /// <summary>
    /// Resets the FlareSolverr-URL (HttpClient does not use FlareSolverr anymore)
    /// </summary>
    /// <response code="200"></response>
    [HttpDelete("FlareSolverr/Url")]
    [ProducesResponseType(Status200OK)]
    public Ok ClearFlareSolverrUrl()
    {
        Mangette.Settings.SetFlareSolverrUrl(string.Empty);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Ping FlareSolverr itself (sessions.list). Does not browse a random third-party site.
    /// </summary>
    [HttpPost("FlareSolverr/Test")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<string>, BadRequest<string>>> TestFlareSolverrReachable()
    {
        string baseUrl = MangetteSettings.NormalizeFlareSolverrUrl(Mangette.Settings.FlareSolverrUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return TypedResults.BadRequest("FlareSolverr URL is empty. Set http://192.168.1.210:8191 and Save first.");

        Uri v1 = MangetteSettings.FlareSolverrV1Uri(baseUrl);
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        try
        {
            HttpResponseMessage get = await client.GetAsync(baseUrl, HttpContext.RequestAborted);
            string getBody = await get.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!get.IsSuccessStatusCode)
                return TypedResults.BadRequest($"GET {baseUrl} returned {(int)get.StatusCode}. {TrimBody(getBody)}");

            HttpRequestMessage post = new(HttpMethod.Post, v1)
            {
                Content = new StringContent("""{"cmd":"sessions.list"}""", System.Text.Encoding.UTF8, "application/json")
            };
            HttpResponseMessage listed = await client.SendAsync(post, HttpContext.RequestAborted);
            string listedBody = await listed.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!listed.IsSuccessStatusCode)
                return TypedResults.BadRequest($"POST {v1} returned {(int)listed.StatusCode}. {TrimBody(listedBody)}");

            return TypedResults.Ok($"Reached {baseUrl} (GET {(int)get.StatusCode}) and {v1} sessions.list ok.");
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(
                $"Cannot connect to {baseUrl}: {ex.Message}. From Windows try: curl {baseUrl}  On the Debian VM use host networking on port 8191 (docker compose up -d). Bridged adapter, not NAT, unless you port-forward 8191.");
        }
    }

    private static string TrimBody(string body)
    {
        string flat = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 240 ? flat : flat[..240] + "…";
    }

    private const string MaskedSecret = "********";

    /// <summary>Sets the Prowlarr URL and API key used to search indexers for Comics.</summary>
    [HttpPatch("Prowlarr")]
    [ProducesResponseType(Status200OK)]
    public Ok SetProwlarr([FromBody] SetProwlarrRecord requestData)
    {
        string apiKey = requestData.ApiKey == MaskedSecret ? Mangette.Settings.ProwlarrApiKey : requestData.ApiKey;
        Mangette.Settings.SetProwlarr(requestData.Url, apiKey);
        return TypedResults.Ok();
    }

    /// <summary>Checks that Prowlarr is reachable with the configured API key.</summary>
    [HttpPost("Prowlarr/Test")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<string>, BadRequest<string>>> TestProwlarr()
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ProwlarrUrl) || string.IsNullOrWhiteSpace(Mangette.Settings.ProwlarrApiKey))
            return TypedResults.BadRequest("Prowlarr URL and API key are required.");

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        HttpRequestMessage request = new(HttpMethod.Get, $"{Mangette.Settings.ProwlarrUrl}/api/v1/indexer");
        request.Headers.TryAddWithoutValidation("X-Api-Key", Mangette.Settings.ProwlarrApiKey);
        try
        {
            HttpResponseMessage response = await client.SendAsync(request, HttpContext.RequestAborted);
            string body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return TypedResults.BadRequest($"Prowlarr returned {(int)response.StatusCode}. {TrimBody(body)}");
            return TypedResults.Ok($"Reached Prowlarr at {Mangette.Settings.ProwlarrUrl}.");
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Cannot connect to {Mangette.Settings.ProwlarrUrl}: {ex.Message}");
        }
    }

    /// <summary>Sets the qBittorrent WebUI connection used as the torrent download client for Comics.</summary>
    [HttpPatch("QBittorrent")]
    [ProducesResponseType(Status200OK)]
    public Ok SetQBittorrent([FromBody] SetQBittorrentRecord requestData)
    {
        string password = requestData.Password == MaskedSecret ? Mangette.Settings.QBittorrentPassword : requestData.Password;
        Mangette.Settings.SetQBittorrent(requestData.Url, requestData.Username, password, requestData.Category);
        return TypedResults.Ok();
    }

    /// <summary>Logs into qBittorrent with the configured credentials.</summary>
    [HttpPost("QBittorrent/Test")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<string>, BadRequest<string>>> TestQBittorrent()
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.QBittorrentUrl))
            return TypedResults.BadRequest("qBittorrent URL is required.");

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        Dictionary<string, string> form = new()
        {
            ["username"] = Mangette.Settings.QBittorrentUsername,
            ["password"] = Mangette.Settings.QBittorrentPassword
        };
        try
        {
            HttpResponseMessage response = await client.PostAsync(
                $"{Mangette.Settings.QBittorrentUrl}/api/v2/auth/login", new FormUrlEncodedContent(form), HttpContext.RequestAborted);
            string body = (await response.Content.ReadAsStringAsync(HttpContext.RequestAborted)).Trim();
            if (!response.IsSuccessStatusCode || !body.Equals("Ok.", StringComparison.OrdinalIgnoreCase))
                return TypedResults.BadRequest($"qBittorrent login failed ({(int)response.StatusCode}): {TrimBody(body)}");
            return TypedResults.Ok($"Logged into qBittorrent at {Mangette.Settings.QBittorrentUrl}.");
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Cannot connect to {Mangette.Settings.QBittorrentUrl}: {ex.Message}");
        }
    }

    /// <summary>Sets the SABnzbd connection used as the Usenet download client for Comics.</summary>
    [HttpPatch("Sabnzbd")]
    [ProducesResponseType(Status200OK)]
    public Ok SetSabnzbd([FromBody] SetSabnzbdRecord requestData)
    {
        string apiKey = requestData.ApiKey == MaskedSecret ? Mangette.Settings.SabnzbdApiKey : requestData.ApiKey;
        Mangette.Settings.SetSabnzbd(requestData.Url, apiKey, requestData.Category);
        return TypedResults.Ok();
    }

    /// <summary>Checks that SABnzbd is reachable with the configured API key.</summary>
    [HttpPost("Sabnzbd/Test")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<string>, BadRequest<string>>> TestSabnzbd()
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.SabnzbdUrl) || string.IsNullOrWhiteSpace(Mangette.Settings.SabnzbdApiKey))
            return TypedResults.BadRequest("SABnzbd URL and API key are required.");

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        try
        {
            HttpResponseMessage response = await client.GetAsync(
                $"{Mangette.Settings.SabnzbdUrl}/api?mode=version&apikey={Mangette.Settings.SabnzbdApiKey}&output=json",
                HttpContext.RequestAborted);
            string body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return TypedResults.BadRequest($"SABnzbd returned {(int)response.StatusCode}. {TrimBody(body)}");
            return TypedResults.Ok($"Reached SABnzbd at {Mangette.Settings.SabnzbdUrl}. {TrimBody(body)}");
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Cannot connect to {Mangette.Settings.SabnzbdUrl}: {ex.Message}");
        }
    }

    /// <summary>Sets which protocol wins when a Comic search finds both a torrent and a Usenet release.</summary>
    [HttpPatch("ComicProtocolPreference/{preference}")]
    [ProducesResponseType(Status200OK)]
    public Ok SetComicProtocolPreference(ComicProtocolPreference preference)
    {
        Mangette.Settings.SetComicProtocolPreference(preference);
        return TypedResults.Ok();
    }

    /// <summary>Sets the ComicVine API key used as the metadata source for Comics.</summary>
    [HttpPatch("ComicVine")]
    [ProducesResponseType(Status200OK)]
    public Ok SetComicVine([FromBody] SetComicVineRecord requestData)
    {
        Mangette.Settings.SetComicVineApiKey(requestData.ApiKey == MaskedSecret ? Mangette.Settings.ComicVineApiKey : requestData.ApiKey);
        return TypedResults.Ok();
    }

    /// <summary>Checks that the configured ComicVine API key works.</summary>
    [HttpPost("ComicVine/Test")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Ok<string>, BadRequest<string>>> TestComicVine()
    {
        if (string.IsNullOrWhiteSpace(Mangette.Settings.ComicVineApiKey))
            return TypedResults.BadRequest("ComicVine API key is required.");

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mangette/1.0");
        string url = $"https://comicvine.gamespot.com/api/search/?api_key={Mangette.Settings.ComicVineApiKey}" +
                     "&format=json&resources=volume&query=test&limit=1";
        try
        {
            HttpResponseMessage response = await client.GetAsync(url, HttpContext.RequestAborted);
            string body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return TypedResults.BadRequest($"ComicVine returned {(int)response.StatusCode}. {TrimBody(body)}");
            return TypedResults.Ok("ComicVine API key works.");
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Cannot reach ComicVine: {ex.Message}");
        }
    }

    /// <summary>Load a page with the built-in Chromium Cloudflare bypass (no Docker).</summary>
    [HttpPost("CloudflareBypass/Test")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok, InternalServerError>> TestChromiumBypass()
    {
        HttpResponseMessage result = await ChromiumDownloadClient.Shared.MakeRequest("https://example.com", RequestType.Default);
        return result.IsSuccessStatusCode ? TypedResults.Ok() : TypedResults.InternalServerError();
    }

    /// <summary>
    /// Returns the language in which Manga are downloaded
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("DownloadLanguage")]
    [ProducesResponseType<string>(Status200OK,  "text/plain")]
    public Ok<string> GetDownloadLanguage()
    {
        return TypedResults.Ok(Mangette.Settings.DownloadLanguage);
    }

    /// <summary>
    /// Sets the language in which Manga are downloaded
    /// </summary>
    /// <response code="200"></response>
    [HttpPatch("DownloadLanguage/{Language}")]
    [ProducesResponseType(Status200OK)]
    public Ok SetDownloadLanguage(string Language)
    {
        //TODO Validation
        Mangette.Settings.SetDownloadLanguage(Language);
        return TypedResults.Ok();
    }
    

    /// <summary>
    /// Sets the time when Libraries are refreshed
    /// </summary>
    /// <response code="200"></response>
    [HttpPatch("LibraryRefresh")]
    [ProducesResponseType(Status200OK)]
    public Ok SetLibraryRefresh([FromBody]PatchLibraryRefreshRecord requestData)
    {
        Mangette.Settings.SetLibraryRefreshSetting(requestData.Setting);
        if(requestData.RefreshLibraryWhileDownloadingEveryMinutes is { } value)
            Mangette.Settings.SetRefreshLibraryWhileDownloadingEveryMinutes(value);
        return TypedResults.Ok();
    }
}