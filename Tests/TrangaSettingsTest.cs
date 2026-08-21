namespace Tests;

public class TrangaSettingsTest
{
    [Theory]
    [InlineData(8585, 8585)]
    [InlineData(8080, 8080)]
    [InlineData(1, 1)]
    [InlineData(65535, 65535)]
    [InlineData(0, 8585)]
    [InlineData(-1, 8585)]
    [InlineData(65536, 8585)]
    public void NormalizeListenPort_RejectsInvalid(int input, int expected)
    {
        Assert.Equal(expected, API.TrangaSettings.NormalizeListenPort(input));
    }

    [Fact]
    public void DefaultListenPort_Is8585()
    {
        Assert.Equal(8585, API.TrangaSettings.DefaultListenPort);
    }

    [Fact]
    public void NormalizeDirectory_FallsBackWhenEmpty()
    {
        string fallback = Path.GetFullPath(Path.Join(Path.GetTempPath(), "mangette-fallback"));
        Assert.Equal(fallback, API.TrangaSettings.NormalizeDirectory("  ", fallback));
        Assert.Equal(fallback, API.TrangaSettings.NormalizeDirectory(null, fallback));
    }

    [Fact]
    public void NormalizeDirectory_ExpandsRelative()
    {
        string result = API.TrangaSettings.NormalizeDirectory(".", Path.GetTempPath());
        Assert.Equal(Path.GetFullPath("."), result);
    }
}
