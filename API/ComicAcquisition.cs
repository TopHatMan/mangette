using API.ExternalDownloadClients;
using API.IndexerConnectors;
using API.Schema.MangaContext;

namespace API;

/// <summary>
/// Shared indexer-search-and-grab logic for Comics, used by both the automatic sweep
/// (<see cref="Workers.PeriodicWorkers.SearchIndexerForMissingIssuesWorker"/>) and the interactive
/// per-issue search (<see cref="Controllers.ComicController"/>) so there's one place that knows how
/// to build a query, pick a client, and hand a release off.
/// </summary>
public static class ComicAcquisition
{
    public static readonly IIndexerConnector Indexer = new ProwlarrIndexerConnector();
    private static readonly IExternalDownloadClient QBittorrent = new QBittorrentDownloadClient();
    private static readonly IExternalDownloadClient Sabnzbd = new SabnzbdDownloadClient();

    public static string BuildQuery(Manga comic, Chapter chapter) => $"{comic.Name} {chapter.ChapterNumber}";

    public static (IExternalDownloadClient Client, DownloadClientKind Kind) PickClient(ReleaseProtocol protocol) =>
        protocol == ReleaseProtocol.Torrent
            ? (QBittorrent, DownloadClientKind.QBittorrent)
            : (Sabnzbd, DownloadClientKind.Sabnzbd);

    /// <summary>Hands one chosen release to the right client and builds the job row that tracks it. Does not save.</summary>
    public static async Task<(ComicDownloadJob? Job, string? Error)> Grab(Chapter chapter, IndexerRelease release, CancellationToken cancellationToken)
    {
        (IExternalDownloadClient client, DownloadClientKind kind) = PickClient(release.Protocol);

        string? externalId;
        try
        {
            externalId = await client.Add(release, cancellationToken);
        }
        catch (Exception ex)
        {
            return (null, $"Sending \"{release.Title}\" to {kind} failed: {ex.Message}");
        }
        if (externalId is null)
            return (null, $"{kind} did not accept \"{release.Title}\".");

        ComicDownloadJob job = new(chapter, release.IndexerName, release.Title, release.DownloadUrl, release.Protocol, kind);
        job.MarkDownloading(externalId);
        return (job, null);
    }
}
