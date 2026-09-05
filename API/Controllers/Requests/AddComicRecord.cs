using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Requests;

/// <summary>
/// Manually adds a Comic series to watch. There is no scraping connector for comics, so issues are
/// discovered by expected issue number instead of a site's chapter list.
/// </summary>
public sealed record AddComicRecord
{
    [Required] [Description("Series title")] public required string Name { get; init; }

    [Description("Cover image URL, shown as-is (not cached/resized like scraped covers)")]
    public string? CoverUrl { get; init; }

    [Required] [Description("A Comic-kind FileLibrary to bind this series to")]
    public required string FileLibraryId { get; init; }

    [Required] [Range(1, int.MaxValue)] [Description("First issue number to watch for")]
    public required int IssueStart { get; init; }

    [Range(1, int.MaxValue)] [Description("Last issue number to watch for. Omit for an open-ended/ongoing series")]
    public int? IssueEnd { get; init; }

    public uint? Year { get; init; }
}
