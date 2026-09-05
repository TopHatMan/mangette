using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using API.Schema.MangaContext;

namespace API.Controllers.DTOs;

public sealed record FileLibrary(string Key, string BasePath, string LibraryName, MediaKind Kind) : Identifiable(Key)
{
    /// <summary>
    /// The directory Path of the library
    /// </summary>
    [Required]
    [Description("The directory Path of the library")]
    public string BasePath { get; internal set; } = BasePath;

    /// <summary>
    /// The Name of the library
    /// </summary>
    [Required]
    [Description("The Name of the library")]
    public string LibraryName { get; internal set; } = LibraryName;

    /// <summary>
    /// Which kind of series may bind to this library
    /// </summary>
    [Description("Which kind of series may bind to this library")]
    public MediaKind Kind { get; internal set; } = Kind;
}