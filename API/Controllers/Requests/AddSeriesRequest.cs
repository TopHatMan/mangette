using System.ComponentModel;
using API.Schema.MangaContext;

namespace API.Controllers.Requests;

public sealed record AddSeriesRequest
{
    [Description("Site name, e.g. WeebCentral")]
    public required string ConnectorName { get; init; }

    [Description("Series id on that site")]
    public required string IdOnSite { get; init; }

    [Description("File library to store .cbz files. Default library if omitted.")]
    public string? LibraryId { get; init; }

    [Description("Monitor and download missing chapters. Default true.")]
    public bool Monitor { get; init; } = true;

    [Description("How often to look for newly published chapters on ongoing series. Default Daily.")]
    public NewChapterCheckInterval? NewChapterCheck { get; init; }
}
