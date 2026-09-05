using System.ComponentModel;
using API.Schema.MangaContext;

namespace API.Controllers.DTOs;

/// <summary>Radarr-style Discover catalog: trending, popular, new, recently updated.</summary>
public sealed record DiscoverPage(
    IReadOnlyList<DiscoverHit> Trending,
    IReadOnlyList<DiscoverHit> Popular,
    IReadOnlyList<DiscoverHit> NewReleases,
    IReadOnlyList<DiscoverHit> RecentlyUpdated,
    IReadOnlyList<DiscoverHit> PopularOnSites,
    DateTime GeneratedAtUtc,
    string Source)
{
    [Description("Hot on AniList right now")]
    public IReadOnlyList<DiscoverHit> Trending { get; init; } = Trending;

    [Description("All-time popular on AniList")]
    public IReadOnlyList<DiscoverHit> Popular { get; init; } = Popular;

    [Description("Recently started series")]
    public IReadOnlyList<DiscoverHit> NewReleases { get; init; } = NewReleases;

    [Description("Ongoing series AniList updated lately — often new chapters")]
    public IReadOnlyList<DiscoverHit> RecentlyUpdated { get; init; } = RecentlyUpdated;

    [Description("Popular titles on an enabled download site (WeebCentral when it is up)")]
    public IReadOnlyList<DiscoverHit> PopularOnSites { get; init; } = PopularOnSites;

    public DateTime GeneratedAtUtc { get; init; } = GeneratedAtUtc;
    public string Source { get; init; } = Source;
}

public sealed record DiscoverHit(
    string Name,
    string Description,
    uint? Year,
    MangaReleaseStatus ReleaseStatus,
    string CoverUrl,
    int AniListId,
    string SiteUrl,
    int? AverageScore,
    int Popularity,
    string Kind,
    IReadOnlyList<string> Genres,
    int? Chapters,
    bool AlreadyInLibrary,
    string? ExistingMangaId,
    string? ConnectorName,
    string? IdOnSite)
{
    public string Name { get; init; } = Name;
    public string Description { get; init; } = Description;
    public uint? Year { get; init; } = Year;
    public MangaReleaseStatus ReleaseStatus { get; init; } = ReleaseStatus;
    public string CoverUrl { get; init; } = CoverUrl;
    [Description("AniList media id. 0 when the row came from a download site, not AniList.")]
    public int AniListId { get; init; } = AniListId;
    public string SiteUrl { get; init; } = SiteUrl;
    public int? AverageScore { get; init; } = AverageScore;
    public int Popularity { get; init; } = Popularity;
    [Description("Manga, Manhwa, Manhua, or One-shot")]
    public string Kind { get; init; } = Kind;
    public IReadOnlyList<string> Genres { get; init; } = Genres;
    public int? Chapters { get; init; } = Chapters;
    public bool AlreadyInLibrary { get; init; } = AlreadyInLibrary;
    public string? ExistingMangaId { get; init; } = ExistingMangaId;
    [Description("Download site when this row is from Popular on sites")]
    public string? ConnectorName { get; init; } = ConnectorName;
    public string? IdOnSite { get; init; } = IdOnSite;
}
