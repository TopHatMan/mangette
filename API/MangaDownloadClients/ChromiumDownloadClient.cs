using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using log4net;
using PuppeteerSharp;

namespace API.MangaDownloadClients;

internal class ChromiumDownloadClient : IDownloadClient, IAsyncDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ChromiumDownloadClient));
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static readonly Regex ImageUrlRex = new(@"https?:\/\/.*\.(?:p?jpe?g|gif|a?png|bmp|avif|webp)(\?.*)?");
    private static IBrowser? Browser;
    private static string? ExecutablePath;
    private static long ActivePages;
    private const int MaxPages = 2;

    public static ChromiumDownloadClient Shared { get; } = new();

    private readonly HttpDownloadClient _httpFallback = new(allowCloudflareBypass: false);

    public static async Task WarmupAsync()
    {
        try
        {
            await EnsureBrowserAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.WarnFormat("Chromium warmup failed: {0}", ex.Message);
        }
    }

    public static string? LastResolvedExecutable => ExecutablePath;

    public async Task<HttpResponseMessage> MakeRequest(string url, RequestType requestType, string? referrer = null, CancellationToken? cancellationToken = null)
    {
        Log.DebugFormat("Using {0} for {1}", nameof(ChromiumDownloadClient), url);
        CancellationToken ct = cancellationToken ?? CancellationToken.None;

        if (ImageUrlRex.IsMatch(url))
            return await _httpFallback.MakeRequest(url, requestType, referrer, ct);

        IBrowser? browser = await EnsureBrowserAsync(ct);
        if (browser is null)
        {
            Log.Warn("Chromium is not available; Cloudflare pages cannot be loaded without it.");
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        while (Interlocked.Read(ref ActivePages) >= MaxPages)
            await Task.Delay(50, ct);

        Interlocked.Increment(ref ActivePages);
        IPage? page = null;
        try
        {
            page = await browser.NewPageAsync();
            await page.SetUserAgentAsync(Mangette.Settings.UserAgent);
            if (!string.IsNullOrEmpty(referrer))
                await page.SetExtraHttpHeadersAsync(new Dictionary<string, string> { { "Referer", referrer } });

            bool success = false;
            Exception? lastEx = null;
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    await page.GoToAsync(url, new NavigationOptions
                    {
                        Timeout = 45000,
                        WaitUntil = [WaitUntilNavigation.DOMContentLoaded]
                    });
                    success = true;
                    break;
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                {
                    lastEx = ex;
                    await Task.Delay(1000 * (retry + 1), ct);
                    await page.CloseAsync();
                    page = await browser.NewPageAsync();
                }
            }

            if (!success)
            {
                Log.ErrorFormat("Chromium request failed for {0}: {1}", url, lastEx?.Message);
                return new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
            }

            await Task.Delay(1500, ct);
            string html = await page.GetContentAsync();
            if (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(8000, ct);
                html = await page.GetContentAsync();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        }
        finally
        {
            if (page is not null)
            {
                try { await page.CloseAsync(); }
                catch (Exception ex) { Log.WarnFormat("Error closing page: {0}", ex.Message); }
            }
            Interlocked.Decrement(ref ActivePages);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Shared browser lives for the process.
        await Task.CompletedTask;
    }

    private static async Task<IBrowser?> EnsureBrowserAsync(CancellationToken ct)
    {
        if (Browser is { IsClosed: false })
            return Browser;

        await InitLock.WaitAsync(ct);
        try
        {
            if (Browser is { IsClosed: false })
                return Browser;

            string? path = FindLocalChrome();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Log.Info("No system Chrome/Edge found. Downloading Chromium into data/chromium (first run can take a few minutes)...");
                path = await DownloadChromiumAsync(ct);
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Log.Error("Chromium/Chrome executable not found. Install Google Chrome or Microsoft Edge.");
                return null;
            }

            ExecutablePath = path;
            Log.InfoFormat("Cloudflare bypass using Chromium at {0}", path);
            Browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Timeout = 60000,
                ExecutablePath = path,
                Args =
                [
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--disable-blink-features=AutomationControlled",
                    "--headless=new"
                ]
            });
            return Browser;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to start Chromium: {ex.Message}");
            Browser = null;
            return null;
        }
        finally
        {
            InitLock.Release();
        }
    }

    internal static string? FindLocalChrome()
    {
        string? env = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH")
                      ?? Environment.GetEnvironmentVariable("CHROME_BIN");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            "/usr/bin/google-chrome",
            "/usr/bin/google-chrome-stable",
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "/snap/bin/chromium"
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string?> DownloadChromiumAsync(CancellationToken ct)
    {
        string cache = Path.Join(MangetteSettings.DataDirectory, "chromium");
        Directory.CreateDirectory(cache);
        BrowserFetcher fetcher = new(new BrowserFetcherOptions { Path = cache });
        var installed = await fetcher.DownloadAsync();
        ct.ThrowIfCancellationRequested();
        return installed.GetExecutablePath();
    }
}
