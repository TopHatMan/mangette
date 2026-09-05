using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Requests;

/// <summary>
/// Adds a Comic series to watch. Pass <see cref="ComicVineVolumeId"/> (from
/// <c>GET Comic/Search</c>) to fill Name/CoverUrl/Year/IssueEnd from real ComicVine data --
/// <see cref="Name"/>/<see cref="CoverUrl"/>/<see cref="Year"/> are only a fallback for the rare
/// case ComicVine has nothing for a series and it's being watched by title alone.
/// </summary>
public sealed record AddComicRecord
{
    [Description("ComicVine volume id from GET Comic/Search. When set, Name/CoverUrl/Year/IssueEnd are filled from ComicVine.")]
    public string? ComicVineVolumeId { get; init; }

    [Description("Series title. Required only when ComicVineVolumeId is not set.")]
    public string? Name { get; init; }

    [Description("Cover image URL, shown as-is (not cached/resized like scraped covers)")]
    public string? CoverUrl { get; init; }

    [Required] [Description("A Comic-kind FileLibrary to bind this series to")]
    public required string FileLibraryId { get; init; }

    [Required] [Range(1, int.MaxValue)] [Description("First issue number to watch for")]
    public required int IssueStart { get; init; }

    [Range(1, int.MaxValue)] [Description("Last issue number to watch for. Omit for an open-ended/ongoing series, or to use ComicVine's real issue count.")]
    public int? IssueEnd { get; init; }

    public uint? Year { get; init; }
}
