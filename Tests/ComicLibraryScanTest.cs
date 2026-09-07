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
        // "Volume 01 (2017)" is the leaf's own name and carries no title on its own -- the real
        // title, "Batman White Knight", lives on its parent folder, and the suggested query now
        // pulls it from there instead of asking the human to retype it every time.
        Assert.Contains(unmapped, c => c.SuggestedQuery == "Batman White Knight" && c.RelativePath.EndsWith("Volume 01 (2017)"));
        // "Batman Annuals 01-28" already carries its own real title text (not a bare "Annuals"), so
        // it's used as-is rather than combined with an ancestor.
        Assert.Contains(unmapped, c => c.SuggestedQuery == "Batman Annuals 01-28");
        // The generic "Batman" hub itself never becomes a candidate: it has no archives directly in it.
        Assert.DoesNotContain(unmapped, c => c.RelativePath == "Batman");
    }

    /// <summary>
    /// A hub folder ("Batman") holding loose issues directly in it, alongside per-run subfolders
    /// that are each their own separate candidate -- confirmed live against a real library. Two
    /// things must hold: the hub's own archive count must not double-count its subfolders' files,
    /// and a bare "Annuals"/"Extras" leaf must inherit the series name from its parent run folder.
    /// </summary>
    [Fact]
    public void FindCandidates_HubWithLooseFilesAndAnnualsExtrasSubfolders()
    {
        Archive("Batman", "Batman - Ch.17.cbz");
        Archive("Batman", "Batman - Ch.30.cbz");
        Archive("Batman", "Volume 01 (1940)", "Batman 001.cbz");
        Archive("Batman", "Volume 01 (1940)", "Batman 002.cbz");
        Archive("Batman", "Volume 01 (1940)", "Annuals", "Batman Annual 001.cbz");
        Archive("Batman", "Volume 01 (1940)", "Extras", "Batman Giant-Size 001.cbz");
        Archive("Batman", "Volume 01 (1987)", "Batman v2 001.cbz");

        (List<API.ComicScanCandidate> unmapped, _) = API.ComicLibraryImportMatcher.FindCandidates(_root, new HashSet<string>());

        Assert.Equal(5, unmapped.Count);

        API.ComicScanCandidate hub = Assert.Single(unmapped, c => c.RelativePath == "Batman");
        Assert.Equal(2, hub.ArchiveCount); // only its own 2 loose files, not the 5 more belonging to subfolders

        API.ComicScanCandidate v1940 = Assert.Single(unmapped, c => c.RelativePath.EndsWith("Volume 01 (1940)"));
        Assert.Equal(2, v1940.ArchiveCount); // its own 2 issues, not the annual/extra nested under it
        Assert.Equal("Batman", v1940.SuggestedQuery);

        API.ComicScanCandidate v1987 = Assert.Single(unmapped, c => c.RelativePath.EndsWith("Volume 01 (1987)"));
        Assert.Equal("Batman", v1987.SuggestedQuery); // same base series as 1940 -- year disambiguation happens via Match, not the query text

        API.ComicScanCandidate annuals = Assert.Single(unmapped, c => c.RelativePath.EndsWith("Annuals"));
        Assert.Equal("Batman Annual", annuals.SuggestedQuery);

        API.ComicScanCandidate extras = Assert.Single(unmapped, c => c.RelativePath.EndsWith("Extras"));
        Assert.Equal("Batman Extra", extras.SuggestedQuery);
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
