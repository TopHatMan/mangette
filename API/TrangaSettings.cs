using API.MangaDownloadClients;
using API.Workers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace API;

public class TrangaSettings
{
    public const int DefaultListenPort = 8585;
    [JsonIgnore] public static bool Debug => bool.Parse(Environment.GetEnvironmentVariable("DEBUG") ?? "false");
    /// <summary>Folder that contains the executable (or <c>MANGETTE_HOME</c> override).</summary>
    [JsonIgnore] public static string AppDirectory =>
        Environment.GetEnvironmentVariable("MANGETTE_HOME") is { Length: > 0 } home
            ? home
            : AppContext.BaseDirectory;
    [JsonIgnore] public static string DataDirectory => Path.Join(AppDirectory, "data");
    [JsonIgnore] public static string WorkingDirectory => DataDirectory;
    [JsonIgnore] public static string SettingsFilePath => Path.Join(DataDirectory, "settings.json");
    [JsonIgnore] public static string DatabasePath => Path.Join(DataDirectory, "mangette.db");
    [JsonIgnore] public static string CoverImageCache => Path.Join(DataDirectory, "imageCache");
    [JsonIgnore] public static string CoverImageCacheOriginal => Path.Join(CoverImageCache, "original");
    [JsonIgnore] public static string CoverImageCacheLarge => Path.Join(CoverImageCache, "large");
    [JsonIgnore] public static string CoverImageCacheMedium => Path.Join(CoverImageCache, "medium");
    [JsonIgnore] public static string CoverImageCacheSmall => Path.Join(CoverImageCache, "small");
    public static string DefaultDownloadLocation =>
        Environment.GetEnvironmentVariable("DOWNLOAD_LOCATION") ?? Path.Join(AppDirectory, "Manga");
    [JsonIgnore] public static string DefaultTempDownloadPath => Path.Join(DataDirectory, "incomplete");
    [JsonIgnore] internal static readonly string DefaultUserAgent = $"Mangette/2.0 ({Enum.GetName(Environment.OSVersion.Platform)}; {(Environment.Is64BitOperatingSystem ? "x64" : "")})";

    /// <summary>HTTP listen port. <c>PORT</c> env overrides. Restart required after changing.</summary>
    public int ListenPort { get; set; } = DefaultListenPort;
    public string UserAgent { get; set; } = DefaultUserAgent;
    public int ImageCompression{ get; set; } = 40;
    public bool BlackWhiteImages { get; set; } = false;
    public const string DefaultFlareSolverrUrl = "http://127.0.0.1:8191";
    public string FlareSolverrUrl { get; set; } =
        Environment.GetEnvironmentVariable("FLARESOLVERR_URL") ?? DefaultFlareSolverrUrl;
    /// <summary>Connector names in download order. First match that is not cooling down wins the chapter.</summary>
    public List<string> ConnectorPriority { get; set; } = new(DownloadFailureTracker.DefaultPreferenceOrder);
    /// <summary>
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
    /// </summary>
    public string ChapterNamingScheme { get; set; } = "%M - ?V(Vol.%V )Ch.%C?T( - %T)";
    public int WorkCycleTimeoutMs { get; set; } = 20000;

    public string DownloadLanguage { get; set; } = "en";

    /// <summary>Folder for in-progress chapter images. Packed into the library as .cbz when the chapter finishes.</summary>
    public string TempDownloadPath { get; set; } = DefaultTempDownloadPath;

    public int MaxConcurrentDownloads { get; set; } = (int)Math.Max(Environment.ProcessorCount * 0.75, 1); // Minimum of 1 Tasks, maximum of 0.75 per Core

    public int MaxConcurrentWorkers { get; set; } = Math.Max(Environment.ProcessorCount, 4); // Minimum of 4 Tasks, maximum of 1 per Core

    public LibraryRefreshSetting LibraryRefreshSetting { get; set; } = LibraryRefreshSetting.AfterMangaFinished;

    public int RefreshLibraryWhileDownloadingEveryMinutes { get; set; } = 10;

    /// <summary>Resolved library default shown in Settings. Not stored; file libraries own the actual path.</summary>
    [JsonProperty] public string DefaultLibraryPath => DefaultDownloadLocation;
    [JsonProperty] public string DataFolder => DataDirectory;

    public TrangaSettings()
    {
        Directory.CreateDirectory(WorkingDirectory);
    }

    public static TrangaSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
            new TrangaSettings().Save();
        TrangaSettings settings = JsonConvert.DeserializeObject<TrangaSettings>(File.ReadAllText(SettingsFilePath), new StringEnumConverter())
                                  ?? new TrangaSettings();
        string? envUrl = Environment.GetEnvironmentVariable("FLARESOLVERR_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
            settings.FlareSolverrUrl = envUrl;
        else if (string.IsNullOrWhiteSpace(settings.FlareSolverrUrl))
            settings.FlareSolverrUrl = DefaultFlareSolverrUrl;
        settings.ListenPort = ResolveListenPort(settings.ListenPort);
        settings.TempDownloadPath = NormalizeDirectory(settings.TempDownloadPath, DefaultTempDownloadPath);
        settings.ConnectorPriority = NormalizeConnectorPriority(settings.ConnectorPriority);
        DownloadFailureTracker.SetPreferenceOrder(settings.ConnectorPriority);
        Directory.CreateDirectory(settings.TempDownloadPath);
        return settings;
    }

    public void Save()
    {
        File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(this, Formatting.Indented, new StringEnumConverter()));
    }

    public static int NormalizeListenPort(int port) =>
        port is > 0 and < 65536 ? port : DefaultListenPort;

    public static int ResolveListenPort(int configured)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out int envPort))
            return NormalizeListenPort(envPort);
        return NormalizeListenPort(configured);
    }

    public static string NormalizeDirectory(string? path, string fallback)
    {
        string value = string.IsNullOrWhiteSpace(path) ? fallback : path.Trim();
        try
        {
            return Path.GetFullPath(value);
        }
        catch
        {
            return Path.GetFullPath(fallback);
        }
    }

    public void SetUserAgent(string value)
    {
        this.UserAgent = value;
        Save();
    }

    public void UpdateImageCompression(int value)
    {
        this.ImageCompression = value;
        Save();
    }

    public void SetBlackWhiteImageEnabled(bool enabled)
    {
        this.BlackWhiteImages = enabled;
        Save();
    }

    public void SetChapterNamingScheme(string scheme)
    {
        this.ChapterNamingScheme = scheme;
        Save();
    }

    public void SetFlareSolverrUrl(string url)
    {
        this.FlareSolverrUrl = url;
        Save();
    }

    public void SetListenPort(int port)
    {
        ListenPort = NormalizeListenPort(port);
        Save();
    }

    public void SetTempDownloadPath(string path)
    {
        TempDownloadPath = NormalizeDirectory(path, DefaultTempDownloadPath);
        Directory.CreateDirectory(TempDownloadPath);
        Save();
    }

    public void SetConnectorPriority(IEnumerable<string> names)
    {
        List<string> normalized = NormalizeConnectorPriority(names);
        ConnectorPriority.Clear();
        ConnectorPriority.AddRange(normalized);
        DownloadFailureTracker.SetPreferenceOrder(ConnectorPriority);
        Save();
    }

    public static List<string> NormalizeConnectorPriority(IEnumerable<string>? names)
    {
        HashSet<string> known = Tranga.MangaConnectors
            .Select(c => c.Name)
            .Where(n => !n.Equals("Global", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> ordered = [];
        foreach (string name in names ?? [])
        {
            string? match = known.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is null || ordered.Contains(match, StringComparer.OrdinalIgnoreCase))
                continue;
            ordered.Add(match);
        }
        foreach (string name in DownloadFailureTracker.DefaultPreferenceOrder.Concat(known))
        {
            if (!ordered.Contains(name, StringComparer.OrdinalIgnoreCase) && known.Contains(name))
                ordered.Add(known.First(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }
        return ordered;
    }

    public void SetDownloadLanguage(string language)
    {
        this.DownloadLanguage = language;
        Save();
    }

    public void SetMaxConcurrentDownloads(int value)
    {
        this.MaxConcurrentDownloads = Math.Clamp(value, 1, 64);
        Save();
    }

    public void SetMaxConcurrentWorkers(int value)
    {
        this.MaxConcurrentWorkers = Math.Clamp(value, 1, 256);
        Save();
    }

    public void SetLibraryRefreshSetting(LibraryRefreshSetting setting)
    {
        this.LibraryRefreshSetting = setting;
        Save();
    }

    public void SetRefreshLibraryWhileDownloadingEveryMinutes(int value)
    {
        this.RefreshLibraryWhileDownloadingEveryMinutes = value;
        Save();
    }
}
