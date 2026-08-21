using System.ComponentModel;

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
}
