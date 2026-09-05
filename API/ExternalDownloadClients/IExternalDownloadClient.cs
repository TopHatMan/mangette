using API.IndexerConnectors;

namespace API.ExternalDownloadClients;

/// <summary>
/// Hands a release off to an external download daemon (a torrent client or a Usenet downloader) and
/// polls it for completion. Unlike <see cref="API.MangaDownloadClients.IDownloadClient"/> (fetch this
/// URL over HTTP for a scraper), this drives a long-running job on a separate process.
/// </summary>
public interface IExternalDownloadClient
{
    /// <summary>Queues the release for download. Returns an id this client's <see cref="GetStatus"/> can look up later.</summary>
    Task<string?> Add(IndexerRelease release, CancellationToken cancellationToken);

    Task<ExternalDownloadStatus> GetStatus(string externalId, CancellationToken cancellationToken);

    Task Remove(string externalId, bool deleteFiles, CancellationToken cancellationToken);
}
