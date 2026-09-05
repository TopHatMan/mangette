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
    MediaKind Kind,
    uint? Year,
    bool Monitored,
    NewChapterCheckInterval NewChapterCheck,
    int ChapterCount,
    int DownloadedCount)
    : MinimalManga(Key, Name, Description, ReleaseStatus, MangaConnectorIds, Kind)
{
    public uint? Year { get; init; } = Year;

    [Description("Series is monitored: missing chapters download and ongoing titles are scanned for new chapters.")]
    public bool Monitored { get; init; } = Monitored;

    [Description("How often to re-fetch the chapter list when the series is ongoing.")]
    public NewChapterCheckInterval NewChapterCheck { get; init; } = NewChapterCheck;

    public int ChapterCount { get; init; } = ChapterCount;
    public int DownloadedCount { get; init; } = DownloadedCount;
}
