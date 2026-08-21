using System.ComponentModel;
using API.Schema.MangaContext;

namespace API.Controllers.DTOs;

/// <summary>Preview of a series from a site. Not in the library until Add is called.</summary>
public sealed record SearchHit(
    string Name,
    string Description,
    uint? Year,
    MangaReleaseStatus ReleaseStatus,
    string CoverUrl,
    string ConnectorName,
    string IdOnSite,
    string? WebsiteUrl,
    double Score,
    bool AlreadyInLibrary,
    string? ExistingMangaId)
{
    [Description("Title on the site")]
    public string Name { get; init; } = Name;

    public string Description { get; init; } = Description;
    public uint? Year { get; init; } = Year;
    public MangaReleaseStatus ReleaseStatus { get; init; } = ReleaseStatus;
    public string CoverUrl { get; init; } = CoverUrl;
    public string ConnectorName { get; init; } = ConnectorName;
    public string IdOnSite { get; init; } = IdOnSite;
    public string? WebsiteUrl { get; init; } = WebsiteUrl;
    public double Score { get; init; } = Score;
    public bool AlreadyInLibrary { get; init; } = AlreadyInLibrary;
    public string? ExistingMangaId { get; init; } = ExistingMangaId;
}
