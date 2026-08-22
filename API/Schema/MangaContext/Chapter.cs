using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
namespace API.Schema.MangaContext;

[PrimaryKey("Key")]
public class Chapter : Identifiable, IComparable<Chapter>
{
    [StringLength(64)] public string ParentMangaId { get; init; } = null!;
    public Manga ParentManga = null!;

    [NotMapped] public Dictionary<string, string> IdsOnMangaConnectors =>
        MangaConnectorIds.ToDictionary(id => id.MangaConnectorName, id => id.IdOnConnectorSite);
    public ICollection<MangaConnectorId<Chapter>> MangaConnectorIds = null!;

    public int? VolumeNumber { get; private set; }
    [StringLength(10)] public string ChapterNumber { get; private set; }

    [StringLength(256)] public string? Title { get; private set; }

    [StringLength(256)] public string? FileName { get; internal set; }

    public bool Downloaded { get; internal set; }

    /// <exception cref="DirectoryNotFoundException">Library for Manga not loaded</exception>
    [NotMapped]
    public string? FullArchiveFilePath => GetFullFilepath();

    private static readonly Regex ChapterNumberRegex = new(@"(?:\d+\.)*\d+", RegexOptions.Compiled);
    public Chapter(Manga parentManga, string chapterNumber,
        int? volumeNumber, string? title = null)
        : base(TokenGen.CreateToken(typeof(Chapter), parentManga.Key, chapterNumber))
    {
        if(ChapterNumberRegex.Match(chapterNumber) is not { Success: true } match || !match.Value.Equals(chapterNumber))
            throw new ArgumentException($"Invalid chapter number: {chapterNumber}");
        chapterNumber = string.Join('.', chapterNumber.Split('.').Select(p => int.Parse(p).ToString()));
        this.ChapterNumber = chapterNumber;
        this.ParentManga = parentManga;
        this.MangaConnectorIds = [];
        this.VolumeNumber = volumeNumber;
        this.Title = title;
        this.Downloaded = false;
        this.MangaConnectorIds = [];
    }

    /// <summary>
    /// EF ONLY!!!
    /// </summary>
    internal Chapter(string key, int? volumeNumber, string chapterNumber, string? title, string? fileName, bool downloaded)
        : base(key)
    {
        this.VolumeNumber = volumeNumber;
        this.ChapterNumber = chapterNumber;
        this.Title = title;
        this.FileName = fileName;
        this.Downloaded = downloaded;
    }

    public int CompareTo(Chapter? other)
    {
        if (other is not { } otherChapter)
            throw new ArgumentException($"{other} can not be compared to {this}");
        return VolumeNumber?.CompareTo(otherChapter.VolumeNumber) switch
        {
            < 0 => -1,
            > 0 => 1,
            _ => CompareChapterNumbers(ChapterNumber, otherChapter.ChapterNumber)
        };
    }


    /// <summary>
    /// Checks the filesystem if an archive for this chapter exists (exact name, padded numbers, or Ch.N in the filename).
    /// </summary>
    public async Task<bool> CheckDownloaded(MangaContext context, CancellationToken? token = null, bool persist = true)
    {
        if (ParentManga?.Library is null)
        {
            if (await context.Chapters
                    .Include(c => c.ParentManga)
                    .ThenInclude(p => p.Library)
                    .Include(c => c.ParentManga)
                    .ThenInclude(p => p.AltTitles)
                    .FirstOrDefaultAsync(c => c.Key == Key, token ?? CancellationToken.None) is not { } loaded)
                throw new KeyNotFoundException("Unable to find chapter");
            ParentManga = loaded.ParentManga;
        }

        ApplyDownloadedMatch();
        if (persist)
            await context.Sync(token ?? CancellationToken.None, GetType(), $"CheckDownloaded {this} {Downloaded}");
        return Downloaded;
    }

    internal bool ApplyDownloadedMatch(List<string>? quarantinedFiles = null, bool inspectZip = false)
    {
        if (ParentManga?.Library is null || string.IsNullOrWhiteSpace(ParentManga.Library.BasePath))
        {
            Downloaded = false;
            FileName = null;
            return false;
        }

        ParentManga.TryAttachExistingSeriesFolder();
        string seriesDirectory;
        try
        {
            seriesDirectory = Path.GetFullPath(Path.Combine(ParentManga.Library.BasePath, ParentManga.DirectoryName));
        }
        catch
        {
            Downloaded = false;
            FileName = null;
            return false;
        }

        string? expected = FileName ?? GetArchiveFileName();
        string? found = DownloadedChapterMatcher.FindExistingChapterFile(
            seriesDirectory,
            ChapterNumber,
            expected,
            exactNameOnly: Constants.DownloadedChaptersCheckMatchExactName,
            volumeNumber: VolumeNumber,
            quarantinedFiles: quarantinedFiles,
            inspectZip: inspectZip);

        if (found is not null)
        {
            Downloaded = true;
            FileName = found;
            return true;
        }

        Downloaded = false;
        if (FileName is not null && !File.Exists(Path.Combine(seriesDirectory, FileName)))
            FileName = null;
        return false;
    } 
    
    /// Placeholders:
    /// %M Obj Name
    /// %V Volume
    /// %C Chapter
    /// %T Title
    /// %A Author (first in list)
    /// %I Chapter Internal ID
    /// %i Obj Internal ID
    /// %Y Year (Obj)
    private static readonly Regex NullableRex = new(@"\?([a-zA-Z])\(([^\)]*)\)|(.+?)");
    private static readonly Regex ReplaceRexx = new(@"%([a-zA-Z])|(.+?)");
    /// <summary>
    /// Returns the formatted Filename of the Archive for this chapter. Formatting is done according to <see cref="MangetteSettings.ChapterNamingScheme"/>
    /// </summary>
    /// <returns>A filename</returns>
    private string GetArchiveFileName()
    {
        string archiveNamingScheme = Mangette.Settings.ChapterNamingScheme;
        StringBuilder stringBuilder = new();
        foreach (Match nullable in NullableRex.Matches(archiveNamingScheme))
        {
            if (nullable.Groups[3].Success)
            {
                stringBuilder.Append(nullable.Groups[3].Value);
                continue;
            }

            char placeholder = nullable.Groups[1].Value[0];
            bool isNull = placeholder switch
            {
                'M' => ParentManga?.Name is null,
                'V' => VolumeNumber is null,
                'C' => ChapterNumber is null,
                'T' => Title is null,
                'A' => ParentManga?.Authors?.FirstOrDefault()?.AuthorName is null,
                'Y' => ParentManga?.Year is null,
                _ => true
            };
            if(!isNull)
                stringBuilder.Append(nullable.Groups[2].Value);
        }
        
        string checkedString = stringBuilder.ToString();
        stringBuilder = new();
        
        foreach (Match replace in ReplaceRexx.Matches(checkedString))
        {
            if (replace.Groups[2].Success)
            {
                stringBuilder.Append(replace.Groups[2].Value);
                continue;
            }
            
            char placeholder = replace.Groups[1].Value[0];
            string? value = placeholder switch
            {
                'M' => ParentManga?.Name,
                'V' => VolumeNumber?.ToString() ?? (Constants.ZeroVolumeInFilenameIfNull ? "0" : null),
                'C' => ChapterNumber,
                'T' => Title,
                'A' => ParentManga?.Authors?.FirstOrDefault()?.AuthorName,
                'Y' => ParentManga?.Year.ToString(),
                _ => null
            };
            stringBuilder.Append(value);
        }

        stringBuilder.Append(".cbz");

        return stringBuilder.ToString().CleanNameForWindows();
    }

    private string? GetFullFilepath()
    {
        try
        {
            return Path.Join(ParentManga.FullDirectoryPath, this.FileName is null ? GetArchiveFileName() : FileName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public class ChapterComparer : IComparer<Chapter>
    {
        public int Compare(Chapter? x, Chapter? y)
        {
            if (x is null && y is null)
                return 0;
            if(x is null)
                return -1;
            if (y is null)
                return 1;
            return CompareChapterNumbers(x.ChapterNumber, y.ChapterNumber);
        }
    }

    /// <summary>
    /// True when both rows represent the same volume/chapter on the same manga
    /// (including equivalent numbers such as "1" and "1.0").
    /// </summary>
    public bool IsSameLogicalChapter(Chapter other)
    {
        if (ReferenceEquals(this, other) || Key == other.Key)
            return true;

        string thisManga = ParentMangaId ?? ParentManga?.Key ?? string.Empty;
        string otherManga = other.ParentMangaId ?? other.ParentManga?.Key ?? string.Empty;
        if (thisManga.Length == 0 || thisManga != otherManga)
            return false;

        try
        {
            return new ChapterComparer().Compare(this, other) == 0;
        }
        catch (ArgumentException)
        {
            return ChapterNumber == other.ChapterNumber;
        }
    }

    private static int CompareChapterNumbers(string ch1, string ch2)
    {
        int[] ch1Arr = ch1.Split('.').Select(c => int.TryParse(c, out int result) ? result : -1).ToArray();
        int[] ch2Arr = ch2.Split('.').Select(c => int.TryParse(c, out int result) ? result : -1).ToArray();
        
        if (ch1Arr.Contains(-1) || ch2Arr.Contains(-1))
            throw new ArgumentException("Chapter number is not in correct format");
        
        int i = 0, j = 0;

        while (i < ch1Arr.Length && j < ch2Arr.Length)
        {
            if (ch1Arr[i] < ch2Arr[j])
                return -1;
            if (ch1Arr[i] > ch2Arr[j])
                return 1;
            i++;
            j++;
        }

        return 0;
    }

    internal string GetComicInfoXmlString()
    {
        // Komga/Kavita read this from the .cbz. Series is required for them to group issues.
        XElement comicInfo = new("ComicInfo",
            new XElement("Series", ParentManga.Name),
            new XElement("Number", ChapterNumber),
            new XElement("Manga", "Yes")
        );
        if (!string.IsNullOrWhiteSpace(Title))
            comicInfo.Add(new XElement("Title", Title));
        if (VolumeNumber is not null)
            comicInfo.Add(new XElement("Volume", VolumeNumber));
        if (ParentManga.Year is not null)
            comicInfo.Add(new XElement("Year", ParentManga.Year.Value));
        if (!string.IsNullOrWhiteSpace(ParentManga.Description))
            comicInfo.Add(new XElement("Summary", ParentManga.Description));
        if (ParentManga.Authors is { Count: > 0 })
            comicInfo.Add(new XElement("Writer", string.Join(',', ParentManga.Authors.Select(author => author.AuthorName))));
        if (ParentManga.MangaTags is { Count: > 0 })
        {
            string tags = string.Join(',', ParentManga.MangaTags.Select(tag => tag.Tag));
            comicInfo.Add(new XElement("Genre", tags));
            comicInfo.Add(new XElement("Tags", tags));
        }
        if (ParentManga.OriginalLanguage is not null)
            comicInfo.Add(new XElement("LanguageISO", ParentManga.OriginalLanguage));
        string? web = ParentManga.MangaConnectorIds?.FirstOrDefault(id => id.UseForDownload)?.WebsiteUrl
            ?? ParentManga.MangaConnectorIds?.FirstOrDefault()?.WebsiteUrl
            ?? ParentManga.Links?.FirstOrDefault()?.LinkUrl;
        if (!string.IsNullOrWhiteSpace(web))
            comicInfo.Add(new XElement("Web", web));
        return comicInfo.ToString();
    }

    public override string ToString() => $"{base.ToString()} Vol.{VolumeNumber} Ch.{ChapterNumber} - {Title}";
}