using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace API.Schema.MangaContext;

[PrimaryKey("Key")]
public class FileLibrary(string basePath, string libraryName)
    : Identifiable(TokenGen.CreateToken(typeof(FileLibrary), basePath))
{
    [StringLength(256)] public string BasePath { get; internal set; } = basePath;

    [StringLength(512)] public string LibraryName { get; internal set; } = libraryName;

    /// <summary>Which kind of series may bind to this library. Set at creation.</summary>
    public MediaKind Kind { get; internal set; } = MediaKind.Manga;

    public override string ToString() => $"{base.ToString()} {LibraryName} - {BasePath}";
}