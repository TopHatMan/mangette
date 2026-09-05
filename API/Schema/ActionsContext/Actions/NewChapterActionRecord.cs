using API.Schema.ActionsContext.Actions.Generic;
using API.Schema.MangaContext;

namespace API.Schema.ActionsContext.Actions;

/// <summary>A newly published chapter was added to the library (series grew, then we got the file).</summary>
public sealed class NewChapterActionRecord(Actions action, DateTime performedAt, string mangaId, string chapterId)
    : ActionRecord(action, performedAt), IActionWithChapterRecord, IActionWithMangaRecord
{
    public NewChapterActionRecord(Manga manga, Chapter chapter)
        : this(Actions.NewChapter, DateTime.UtcNow, manga.Key, chapter.Key) { }

    public string ChapterId { get; init; } = chapterId;
    public string MangaId { get; init; } = mangaId;
}
