using System.IO.Compression;

namespace Tests;

public class DownloadedChapterMatcherTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mangette-scan-" + Guid.NewGuid().ToString("N"));

    public DownloadedChapterMatcherTest()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
            // ignore leftover temp files
        }
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("001", "1")]
    [InlineData("1.0", "1")]
    [InlineData("1.5", "1.5")]
    [InlineData("10.20", "10.20")]
    public void NormalizeChapterNumber_StripsPaddingAndTrailingZero(string input, string expected)
    {
        Assert.Equal(expected, API.DownloadedChapterMatcher.NormalizeChapterNumber(input));
    }

    [Fact]
    public void ChapterNumbersEqual_TreatsPaddingAsSameChapter()
    {
        Assert.True(API.DownloadedChapterMatcher.ChapterNumbersEqual("1", "001"));
        Assert.True(API.DownloadedChapterMatcher.ChapterNumbersEqual("1.0", "1"));
        Assert.False(API.DownloadedChapterMatcher.ChapterNumbersEqual("1", "1.5"));
        Assert.False(API.DownloadedChapterMatcher.ChapterNumbersEqual("1", "2"));
    }

    [Theory]
    [InlineData("One Piece - Ch.1.cbz", "1")]
    [InlineData("One Piece - Ch.001.cbz", "1")]
    [InlineData("One Piece - Vol.1 Ch.5 - Title.cbz", "5")]
    [InlineData("chapter-12.cbz", "12")]
    [InlineData("c001.cbz", "1")]
    [InlineData("Ch.1.5.cbz", "1.5")]
    [InlineData("0007.cbz", "7")]
    [InlineData("Bobobo-bo Bo-bobo - Vol.01.cbz", "1")]
    [InlineData("Vol.40.cbz", "40")]
    [InlineData("v12.cbz", "12")]
    [InlineData("Volume 3.cbz", "3")]
    public void TryParseChapterNumber_ReadsCommonArchiveNames(string fileName, string expected)
    {
        Assert.True(API.DownloadedChapterMatcher.TryParseChapterNumber(fileName, out string parsed));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void FindExistingChapterFile_MatchesTrangaStyleName()
    {
        string series = Path.Combine(_root, "One Piece");
        Directory.CreateDirectory(series);
        WriteDummyCbz(Path.Combine(series, "One Piece - Ch.001.cbz"));

        string? found = API.DownloadedChapterMatcher.FindExistingChapterFile(series, "1", "One Piece - Ch.1.cbz");
        Assert.Equal("One Piece - Ch.001.cbz", found);
    }

    [Fact]
    public void FindExistingChapterFile_MatchesExactGeneratedName()
    {
        string series = Path.Combine(_root, "Series");
        Directory.CreateDirectory(series);
        WriteDummyCbz(Path.Combine(series, "Series - Ch.2.cbz"));

        string? found = API.DownloadedChapterMatcher.FindExistingChapterFile(series, "2", "Series - Ch.2.cbz");
        Assert.Equal("Series - Ch.2.cbz", found);
    }

    [Fact]
    public void FindExistingChapterFile_FindsNestedVolumeFolder()
    {
        string series = Path.Combine(_root, "Nested");
        string vol = Path.Combine(series, "Vol.1");
        Directory.CreateDirectory(vol);
        WriteDummyCbz(Path.Combine(vol, "Ch.3.cbz"));

        string? found = API.DownloadedChapterMatcher.FindExistingChapterFile(series, "3", "Nested - Ch.3.cbz");
        Assert.Equal(Path.Combine("Vol.1", "Ch.3.cbz"), found);
    }

    [Fact]
    public void FindExistingChapterFile_ExactOnlyIgnoresDifferentName()
    {
        string series = Path.Combine(_root, "Exact");
        Directory.CreateDirectory(series);
        WriteDummyCbz(Path.Combine(series, "Exact - Ch.001.cbz"));

        Assert.Null(API.DownloadedChapterMatcher.FindExistingChapterFile(
            series, "1", "Exact - Ch.1.cbz", exactNameOnly: true));
    }

    [Fact]
    public void FindExistingChapterFile_MatchesVolumeArchiveToVolumeListing()
    {
        string series = Path.Combine(_root, "Bobobo");
        Directory.CreateDirectory(series);
        WriteDummyCbz(Path.Combine(series, "Bobobo-bo Bo-bobo - Vol.01.cbz"));

        string? found = API.DownloadedChapterMatcher.FindExistingChapterFile(
            series, "1", "Bobobo - Ch.1.cbz", volumeNumber: 1);
        Assert.Equal("Bobobo-bo Bo-bobo - Vol.01.cbz", found);
    }

    [Fact]
    public void VolumeArchive_CoversEveryChapterInThatVolume()
    {
        Assert.True(API.DownloadedChapterMatcher.ArchiveCoversChapter("Vol.01.cbz", "5", 1));
        Assert.True(API.DownloadedChapterMatcher.ArchiveCoversChapter("Vol.01.cbz", "1", 1));
        Assert.False(API.DownloadedChapterMatcher.ArchiveCoversChapter("Vol.01.cbz", "5", 2));
        Assert.False(API.DownloadedChapterMatcher.ArchiveCoversChapter("Vol.01.cbz", "5", null));
        Assert.True(API.DownloadedChapterMatcher.ArchiveCoversChapter("Vol.1 Ch.5.cbz", "5", 1));
        Assert.False(API.DownloadedChapterMatcher.ArchiveCoversChapter("Vol.1 Ch.5.cbz", "6", 1));
    }

    [Fact]
    public void FindSeriesFolder_MatchesQualifierSuffix()
    {
        Directory.CreateDirectory(Path.Combine(_root, "One Piece (EN)"));
        string? found = API.DownloadedChapterMatcher.FindSeriesFolder(_root, "One Piece", "One Piece", []);
        Assert.Equal("One Piece (EN)", found);
    }

    [Fact]
    public void FindExistingChapterFile_SkipsEmptyAndCorruptArchives()
    {
        string series = Path.Combine(_root, "Holes");
        Directory.CreateDirectory(series);
        File.WriteAllBytes(Path.Combine(series, "Holes - Ch.1.cbz"), []);
        File.WriteAllText(Path.Combine(series, "Holes - Ch.2.cbz"), "not a zip");
        WriteDummyCbz(Path.Combine(series, "Holes - Ch.3.cbz"));

        Assert.Null(API.DownloadedChapterMatcher.FindExistingChapterFile(series, "1", "Holes - Ch.1.cbz"));
        Assert.Null(API.DownloadedChapterMatcher.FindExistingChapterFile(series, "2", "Holes - Ch.2.cbz"));
        Assert.Equal("Holes - Ch.3.cbz", API.DownloadedChapterMatcher.FindExistingChapterFile(series, "3", "Holes - Ch.3.cbz"));
        Assert.True(File.Exists(Path.Combine(series, "Holes - Ch.1.cbz.corrupt")));
        Assert.True(File.Exists(Path.Combine(series, "Holes - Ch.2.cbz.corrupt")));
        Assert.False(API.DownloadedChapterMatcher.IsUsableArchive(Path.Combine(series, "Holes - Ch.1.cbz.corrupt")));
        Assert.Null(API.DownloadedChapterMatcher.FindExistingChapterFile(
            series, "2", "Holes - Ch.2.cbz.corrupt", inspectZip: true));
    }

    private static void WriteDummyCbz(string path)
    {
        using FileStream fs = File.Create(path);
        using ZipArchive zip = new(fs, ZipArchiveMode.Create);
        using Stream entry = zip.CreateEntry("001.jpg").Open();
        entry.Write([0xFF, 0xD8, 0xFF, 0xD9]);
    }
}
