using System.ComponentModel;
using API.Schema.MangaContext;

namespace API.Controllers.Requests;

public sealed record PatchMonitorRequest
{
    [Description("Monitor this series. Missing chapters download; ongoing titles are scanned for new chapters.")]
    public required bool Monitored { get; init; }

    [Description("How often to re-fetch the chapter list. Omit to keep the current value.")]
    public NewChapterCheckInterval? NewChapterCheck { get; init; }
}
