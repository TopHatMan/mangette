using API.IndexerConnectors;
using Newtonsoft.Json.Linq;

namespace Tests;

public class ProwlarrIndexerConnectorTest
{
    [Fact]
    public void ParseRelease_StandardCamelCaseShape_Parses()
    {
        JObject item = JObject.Parse("""
            {
                "title": "Absolute Superman 023 (2026) (Digital)",
                "downloadUrl": "https://example.com/dl/1",
                "infoUrl": "https://example.com/info/1",
                "protocol": "usenet",
                "indexer": "NZBgeek",
                "size": 12345,
                "publishDate": "2026-08-20T00:00:00Z",
                "seeders": null
            }
            """);

        API.IndexerConnectors.IndexerRelease? release = ProwlarrIndexerConnector.ParseRelease(item);

        Assert.NotNull(release);
        Assert.Equal("Absolute Superman 023 (2026) (Digital)", release!.Title);
        Assert.Equal("https://example.com/dl/1", release.DownloadUrl);
        Assert.Equal(ReleaseProtocol.Usenet, release.Protocol);
    }

    [Fact]
    public void ParseRelease_PascalCaseFieldNames_StillParses()
    {
        // Field-name casing that differs from the plain-lowercase-camelCase shape this connector
        // was originally written against -- a real cause of every result silently getting dropped
        // even though Prowlarr returned them (raw count > 0, parsed count 0).
        JObject item = JObject.Parse("""
            {
                "Title": "Absolute Superman 023 (2026) (Digital)",
                "DownloadUrl": "https://example.com/dl/1",
                "Protocol": "usenet",
                "Indexer": "NZBgeek",
                "Size": 12345,
                "PublishDate": "2026-08-20T00:00:00Z"
            }
            """);

        Assert.NotNull(ProwlarrIndexerConnector.ParseRelease(item));
    }

    [Fact]
    public void ParseRelease_MagnetOnlyTorrentWithNoDownloadUrl_FallsBackToMagnetUrlOrGuid()
    {
        JObject item = JObject.Parse("""
            {
                "title": "Absolute Superman 023 (2026) (Digital)",
                "magnetUrl": "magnet:?xt=urn:btih:abc123",
                "protocol": "torrent",
                "indexer": "TheRARBG",
                "size": 12345,
                "publishDate": "2026-08-20T00:00:00Z",
                "seeders": 42
            }
            """);

        API.IndexerConnectors.IndexerRelease? release = ProwlarrIndexerConnector.ParseRelease(item);

        Assert.NotNull(release);
        Assert.Equal("magnet:?xt=urn:btih:abc123", release!.DownloadUrl);
        Assert.Equal(42, release.Seeders);
    }

    [Fact]
    public void ParseRelease_MissingTitle_ReturnsNull()
    {
        JObject item = JObject.Parse("""
            {
                "downloadUrl": "https://example.com/dl/1",
                "protocol": "usenet"
            }
            """);

        Assert.Null(ProwlarrIndexerConnector.ParseRelease(item));
    }

    [Fact]
    public void GetString_PrefersFirstMatchingNameInOrder()
    {
        JObject obj = JObject.Parse("""{"guid": "g1", "downloadUrl": "d1"}""");
        Assert.Equal("d1", ProwlarrIndexerConnector.GetString(obj, "downloadUrl", "guid"));
        Assert.Equal("g1", ProwlarrIndexerConnector.GetString(obj, "magnetUrl", "guid"));
    }
}
