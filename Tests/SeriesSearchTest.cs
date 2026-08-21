namespace Tests;

public class SeriesSearchTest
{
    [Theory]
    [InlineData("One Piece", "One Piece", 100)]
    [InlineData("one piece", "One Piece", 100)]
    public void ScoreQuery_ExactTitleIs100(string query, string title, double expected)
    {
        Assert.Equal(expected, API.SeriesSearch.ScoreQuery(query, title));
    }

    [Fact]
    public void ScoreQuery_RanksExactAboveSpinoff()
    {
        double main = API.SeriesSearch.ScoreQuery("One Piece", "One Piece");
        double spinoff = API.SeriesSearch.ScoreQuery("One Piece", "One Piece: Wanted!");
        Assert.True(main > spinoff);
    }
}
