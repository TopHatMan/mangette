using API.Schema.MangaContext;
using API.Workers.PeriodicWorkers;

namespace Tests;

public class CheckForNewChaptersWorkerTest
{
    [Theory]
    [InlineData(MangaReleaseStatus.Continuing, true)]
    [InlineData(MangaReleaseStatus.OnHiatus, true)]
    [InlineData(MangaReleaseStatus.Unreleased, true)]
    [InlineData(MangaReleaseStatus.Completed, false)]
    [InlineData(MangaReleaseStatus.Cancelled, false)]
    public void IsOngoing_MatchesReleaseStatus(MangaReleaseStatus status, bool expected)
    {
        Assert.Equal(expected, CheckForNewChaptersWorker.IsOngoing(status));
    }

    [Fact]
    public void IsDue_UnmonitoredIsNeverDue()
    {
        Manga manga = Series(monitored: false, MangaReleaseStatus.Continuing, NewChapterCheckInterval.Daily, null);
        Assert.False(CheckForNewChaptersWorker.IsDue(manga, DateTime.UtcNow));
    }

    [Fact]
    public void IsDue_CompletedIsNeverDue()
    {
        Manga manga = Series(monitored: true, MangaReleaseStatus.Completed, NewChapterCheckInterval.Daily, null);
        Assert.False(CheckForNewChaptersWorker.IsDue(manga, DateTime.UtcNow));
    }

    [Fact]
    public void IsDue_NeverCheckedOngoingIsDue()
    {
        Manga manga = Series(monitored: true, MangaReleaseStatus.Continuing, NewChapterCheckInterval.Daily, null);
        Assert.True(CheckForNewChaptersWorker.IsDue(manga, DateTime.UtcNow));
    }

    [Fact]
    public void IsDue_DailyWaitsADay()
    {
        DateTime now = DateTime.UtcNow;
        Manga manga = Series(monitored: true, MangaReleaseStatus.Continuing, NewChapterCheckInterval.Daily, now.AddHours(-12));
        Assert.False(CheckForNewChaptersWorker.IsDue(manga, now));
        manga.LastNewChapterCheck = now.AddHours(-25);
        Assert.True(CheckForNewChaptersWorker.IsDue(manga, now));
    }

    [Fact]
    public void IsDue_WeeklyWaitsAWeek()
    {
        DateTime now = DateTime.UtcNow;
        Manga manga = Series(monitored: true, MangaReleaseStatus.Continuing, NewChapterCheckInterval.Weekly, now.AddDays(-3));
        Assert.False(CheckForNewChaptersWorker.IsDue(manga, now));
        manga.LastNewChapterCheck = now.AddDays(-8);
        Assert.True(CheckForNewChaptersWorker.IsDue(manga, now));
    }

    [Fact]
    public void IntervalDuration_DailyAndWeekly()
    {
        Assert.Equal(TimeSpan.FromDays(1), CheckForNewChaptersWorker.IntervalDuration(NewChapterCheckInterval.Daily));
        Assert.Equal(TimeSpan.FromDays(7), CheckForNewChaptersWorker.IntervalDuration(NewChapterCheckInterval.Weekly));
    }

    [Fact]
    public void SetMonitored_EnablesSourcesWhenNoneSelected()
    {
        Manga manga = Series(monitored: false, MangaReleaseStatus.Continuing, NewChapterCheckInterval.Daily, null);
        manga.MangaConnectorIds.Add(new MangaConnectorId<Manga>(
            manga, "WeebCentral", "op-1", "https://example.com/op", useForDownload: false));
        manga.SetMonitored(true);
        Assert.True(manga.Monitored);
        Assert.True(manga.MangaConnectorIds.Single().UseForDownload);
    }

    [Fact]
    public void SetMonitored_UnmonitorKeepsSources()
    {
        Manga manga = Series(monitored: false, MangaReleaseStatus.Continuing, NewChapterCheckInterval.Daily, null);
        manga.MangaConnectorIds.Add(new MangaConnectorId<Manga>(
            manga, "WeebCentral", "op-1", "https://example.com/op", useForDownload: true));
        manga.SetMonitored(true);
        manga.SetMonitored(false);
        Assert.False(manga.Monitored);
        Assert.True(manga.MangaConnectorIds.Single().UseForDownload);
    }

    private static Manga Series(
        bool monitored,
        MangaReleaseStatus status,
        NewChapterCheckInterval interval,
        DateTime? lastCheck)
    {
        Manga manga = new("One Piece", "d", "https://example.com/c.jpg", status, [], [], [], []);
        manga.SetMonitored(monitored);
        manga.NewChapterCheck = interval;
        manga.LastNewChapterCheck = lastCheck;
        return manga;
    }
}
