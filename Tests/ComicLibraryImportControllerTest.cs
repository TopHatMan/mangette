using API.Controllers;

namespace Tests;

public class ComicLibraryImportControllerTest
{
    [Fact]
    public void IssueCountBonus_ExactMatch_GivesMaxBonus()
    {
        Assert.Equal(5, ComicLibraryImportController.IssueCountBonus(79, 79));
    }

    [Fact]
    public void IssueCountBonus_WildlyDifferentCounts_GivesTinyBonus()
    {
        // A 79-archive folder against a 713-issue volume (a different, much longer-running
        // "Batman" volume) shouldn't get a meaningful nudge toward that wrong candidate.
        double bonus = ComicLibraryImportController.IssueCountBonus(79, 713);
        Assert.True(bonus < 1, $"expected a near-zero bonus, got {bonus}");
    }

    [Theory]
    [InlineData(null, 79)]
    [InlineData(0, 79)]
    [InlineData(79, 0)]
    public void IssueCountBonus_MissingData_GivesNoBonus(int? archiveCount, int issueCount)
    {
        Assert.Equal(0, ComicLibraryImportController.IssueCountBonus(archiveCount, issueCount));
    }

    [Fact]
    public void IssueCountBonus_CloseButNotExact_BreaksTiesTowardCloserVolume()
    {
        // Two same-named volumes, one close to the real count and one far off -- the closer one
        // should score higher, which is the whole point of the "v1 vs v2" disambiguation.
        double closeBonus = ComicLibraryImportController.IssueCountBonus(79, 85);
        double farBonus = ComicLibraryImportController.IssueCountBonus(79, 12);
        Assert.True(closeBonus > farBonus);
    }
}
