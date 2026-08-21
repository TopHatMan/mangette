using System.ComponentModel;
using API.Schema.MangaContext;

namespace API.Controllers.DTOs;

/// <summary>Library row for the dashboard (poster wall / table).</summary>
public sealed record LibrarySeries(
    string Key,
    string Name,
    string Description,
    MangaReleaseStatus ReleaseStatus,
    IEnumerable<MangaConnectorId<Manga>> MangaConnectorIds,
    uint? Year,
    bool Monitored,
    int ChapterCount,
    int DownloadedCount)
    : MinimalManga(Key, Name, Description, ReleaseStatus, MangaConnectorIds)
{
    public uint? Year { get; init; } = Year;

    [Description("At least one site is set to download")]
    public bool Monitored { get; init; } = Monitored;

    public int ChapterCount { get; init; } = ChapterCount;
    public int DownloadedCount { get; init; } = DownloadedCount;
}
