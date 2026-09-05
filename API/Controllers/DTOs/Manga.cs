using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using API.Schema.MangaContext;

namespace API.Controllers.DTOs;

/// <summary>
/// <see cref="Schema.MangaContext.Manga"/> DTO
/// </summary>
public sealed record Manga(string Key, string Name, string Description, MangaReleaseStatus ReleaseStatus, IEnumerable<MangaConnectorId<Manga>> MangaConnectorIds, MediaKind Kind, float IgnoreChaptersBefore, uint? Year, string? OriginalLanguage, IEnumerable<Author> Authors, IEnumerable<string> Tags, IEnumerable<Link> Links, IEnumerable<AltTitle> AltTitles, string? FileLibraryId, bool Monitored, NewChapterCheckInterval NewChapterCheck, DateTime? LastNewChapterCheck)
    : MinimalManga(Key, Name, Description, ReleaseStatus, MangaConnectorIds, Kind)
{
    /// <summary>
    /// Chapter cutoff for Downloads (Chapters before this will not be downloaded)
    /// </summary>
    [Required]
    [Description("Chapter cutoff for Downloads (Chapters before this will not be downloaded)")]
    public float IgnoreChaptersBefore { get; init; } = IgnoreChaptersBefore;
    
    /// <summary>
    /// Release Year
    /// </summary>
    [Description("Release Year")]
    public uint? Year { get; init; } = Year;
    
    /// <summary>
    /// Release Language
    /// </summary>
    [Description("Release Language")]
    public string? OriginalLanguage { get; init; } = OriginalLanguage;
    
    /// <summary>
    /// Author-names
    /// </summary>
    [Required]
    [Description("Author-names")]
    public IEnumerable<Author> Authors { get; init; } = Authors;
    
    /// <summary>
    /// Manga Tags
    /// </summary>
    [Required]
    [Description("Manga Tags")]
    public IEnumerable<string> Tags { get; init; } = Tags;
    
    /// <summary>
    /// Links for more Metadata
    /// </summary>
    [Required]
    [Description("Links for more Metadata")]
    public IEnumerable<Link> Links { get; init; } = Links;
    
    /// <summary>
    /// Alt Titles of Manga
    /// </summary>
    [Required]
    [Description("Alt Titles of Manga")]
    public IEnumerable<AltTitle> AltTitles { get; init; } = AltTitles;
    
    /// <summary>
    /// Id of the Library the Manga gets downloaded to
    /// </summary>
    [Required]
    [Description("Id of the Library the Manga gets downloaded to")]
    public string? FileLibraryId { get; init; } = FileLibraryId;

    [Description("Series is monitored: missing chapters download and ongoing titles are scanned for new chapters.")]
    public bool Monitored { get; init; } = Monitored;

    [Description("How often to re-fetch the chapter list when the series is ongoing.")]
    public NewChapterCheckInterval NewChapterCheck { get; init; } = NewChapterCheck;

    [Description("UTC time of the last chapter-list refresh.")]
    public DateTime? LastNewChapterCheck { get; init; } = LastNewChapterCheck;
}