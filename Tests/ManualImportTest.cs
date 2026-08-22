namespace Tests;

public class ManualImportTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mangette-mi-" + Guid.NewGuid().ToString("N"));

    public ManualImportTest()
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
            /* ignore */
        }
    }

    [Fact]
    public void Guess_MatchesFolderAndChapterNumberToMissingChapter()
    {
        string seriesDir = Path.Combine(_root, "One Piece");
        Directory.CreateDirectory(seriesDir);
        string file = Path.Combine(seriesDir, "One Piece - Ch.1090.cbz");
        File.WriteAllBytes(file, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32]);

        List<API.ManualImport.SeriesInfo> series =
        [
            new("op", "One Piece", "One Piece", _root),
            new("conan", "Detective Conan", "Detective Conan", _root),
        ];
        Dictionary<string, List<API.ManualImport.ChapterInfo>> chapters = new()
        {
            ["op"] =
            [
                new("ch1090", "1090", null, false, null),
                new("ch1", "1", null, true, "old.cbz"),
            ],
        };

        API.ManualImport.FileGuess guess = API.ManualImport.Guess(file, series, chapters, []);

        Assert.Equal("op", guess.MangaId);
        Assert.Equal("ch1090", guess.ChapterId);
        Assert.Equal("1090", guess.ChapterNumber);
        Assert.True(guess.Score >= 90);
    }

    [Fact]
    public void Guess_SkipsFilesAlreadyClaimedAsDownloaded()
    {
        string seriesDir = Path.Combine(_root, "One Piece");
        Directory.CreateDirectory(seriesDir);
        string file = Path.Combine(seriesDir, "have.cbz");
        File.WriteAllBytes(file, new byte[40]);

        List<API.ManualImport.SeriesInfo> series = [new("op", "One Piece", "One Piece", _root)];
        Dictionary<string, List<API.ManualImport.ChapterInfo>> chapters = new()
        {
            ["op"] = [new("ch1", "1", null, true, "have.cbz")],
        };
        HashSet<string> claimed = API.ManualImport.ClaimedArchivePaths(series, chapters);

        API.ManualImport.FileGuess guess = API.ManualImport.Guess(file, series, chapters, claimed);
        Assert.Contains(Path.GetFullPath(file), claimed);
        Assert.Null(guess.MangaId);
    }
}
