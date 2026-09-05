namespace Tests;

public class ComicLibraryScanTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mangette-comic-scan-" + Guid.NewGuid().ToString("N"));

    public ComicLibraryScanTest()
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
            /* ignore leftover temp files */
        }
    }

    private void Archive(params string[] pathParts)
    {
        string full = Path.Combine([_root, .. pathParts]);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[64]);
    }

    /// <summary>
    /// Mirrors the real-world case that motivated per-directory scanning: "Batman" is a generic hub
    /// with no archives of its own, holding several unrelated runs several levels deep.
    /// </summary>
    [Fact]
    public void FindCandidates_SurfacesEachRunInsideAGenericHubFolder()
    {
        Archive("Batman", "01-Batman v1 (001-713) (1940-2011)", "Batman 001 (1940).cbz");
        Archive("Batman", "Batman White Knight (2017-)", "Volume 01 (2017)", "Batman White Knight 01.cbz");
        Archive("Batman", "Batman White Knight (2017-)", "Volume 01 (2017)", "Batman Annuals 01-28 (1961-2011)", "Batman Annual 01.cbz");

        (List<API.ComicScanCandidate> unmapped, int mappedCount) = API.ComicLibraryImportMatcher.FindCandidates(_root, new HashSet<string>());

        Assert.Equal(0, mappedCount);
        Assert.Equal(3, unmapped.Count);
        Assert.Contains(unmapped, c => c.SuggestedQuery == "Batman v1" && c.RelativePath.Contains("01-Batman v1"));
        // "Volume 01 (2017)" is the leaf's own name -- it carries no title on its own (the real title,
        // "Batman White Knight", lives on its parent folder). This is the expected, by-design gap:
        // the suggested query is a starting guess, and the human retypes it before Match when a leaf
        // folder name alone isn't a real title (same as this project's manga import already works).
        Assert.Contains(unmapped, c => c.SuggestedQuery == "Volume 01" && c.RelativePath.EndsWith("Volume 01 (2017)"));
        Assert.Contains(unmapped, c => c.SuggestedQuery == "Batman Annuals 01-28");
        // The generic "Batman" hub itself never becomes a candidate: it has no archives directly in it.
        Assert.DoesNotContain(unmapped, c => c.RelativePath == "Batman");
    }

    [Fact]
    public void FindCandidates_SkipsAccessoryFoldersButKeepsThemInTheParentsCount()
    {
        Archive("X-Men Legacy", "X-Men Legacy 003.cbz");
        Archive("X-Men Legacy", "Variant Covers", "X-Men Legacy 003 (Variant).cbz");

        (List<API.ComicScanCandidate> unmapped, _) = API.ComicLibraryImportMatcher.FindCandidates(_root, new HashSet<string>());

        API.ComicScanCandidate series = Assert.Single(unmapped);
        Assert.Equal("X-Men Legacy", series.RelativePath);
        Assert.Equal(2, series.ArchiveCount); // recursive: includes the variant cover file
    }

    [Fact]
    public void FindCandidates_MarksAlreadyImportedFolderAsMapped()
    {
        Archive("Amazing Spider-Man", "Amazing Spider-Man 001.cbz");

        HashSet<string> mapped = new(StringComparer.OrdinalIgnoreCase) { "Amazing Spider-Man" };
        (List<API.ComicScanCandidate> unmapped, int mappedCount) = API.ComicLibraryImportMatcher.FindCandidates(_root, mapped);

        Assert.Empty(unmapped);
        Assert.Equal(1, mappedCount);
    }
}
