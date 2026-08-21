using System.ComponentModel;

namespace API.Controllers.Requests;

public sealed record PatchSetupSettingsRecord
{
    [Description("HTTP listen port. Restart required.")]
    public int? ListenPort { get; init; }

    [Description("Folder for in-progress chapter images before they are packed into the library.")]
    public string? TempDownloadPath { get; init; }

    [Description("Finished .cbz library folder. Creates or updates the default file library.")]
    public string? LibraryPath { get; init; }

    [Description("Display name for the default file library.")]
    public string? LibraryName { get; init; }

    public int? MaxConcurrentDownloads { get; init; }

    public int? MaxConcurrentWorkers { get; init; }

    public string? DownloadLanguage { get; init; }

    public string? ChapterNamingScheme { get; init; }

    public string? FlareSolverrUrl { get; init; }
}
