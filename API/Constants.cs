using SixLabors.ImageSharp;

namespace API;

public struct Constants
{
    public static readonly Size ImageSmSize = new (225, 320);
    public static readonly Size ImageMdSize = new (450, 640);
    public static readonly Size ImageLgSize = new (900, 1280);
    
    public static readonly bool UpdateChaptersDownloadedBeforeStarting = bool.Parse(Environment.GetEnvironmentVariable("CHECK_CHAPTERS_BEFORE_START") ?? "true");
    /// <summary>
    /// When true, only the generated archive name counts as downloaded.
    /// Default false so recovered Tranga/Komga libraries with padded or slightly different names are recognized.
    /// </summary>
    public static readonly bool DownloadedChaptersCheckMatchExactName = bool.Parse(Environment.GetEnvironmentVariable("MATCH_EXACT_CHAPTER_NAME") ?? "false");
    
    public static readonly bool CreateComicInfoXml = bool.Parse(Environment.GetEnvironmentVariable("CREATE_COMICINFO_XML") ?? "true");
    public static readonly bool ZeroVolumeInFilenameIfNull = bool.Parse(Environment.GetEnvironmentVariable("ALWAYS_INCLUDE_VOLUME_IN_FILENAME") ?? "false");
    
    public static readonly int HttpRequestTimeout =  int.Parse(Environment.GetEnvironmentVariable("HTTP_REQUEST_TIMEOUT") ?? "60");
    public static readonly int RequestsPerMinute =  int.Parse(Environment.GetEnvironmentVariable("REQUESTS_PER_MINUTE") ?? "90");
    public static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(int.Parse(Environment.GetEnvironmentVariable("WORKER_TIMEOUT") ?? "600"));
    
    public static readonly TimeSpan NotificationSendInterval = TimeSpan.FromMinutes(int.Parse(Environment.GetEnvironmentVariable("MINUTES_BETWEEN_NOTIFICATIONS") ?? "1"));
    public static readonly TimeSpan CheckForNewChaptersInterval = TimeSpan.FromHours(int.Parse(Environment.GetEnvironmentVariable("HOURS_BETWEEN_NEW_CHAPTERS_CHECK") ?? "3"));
}