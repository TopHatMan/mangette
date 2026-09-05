namespace API.IndexerConnectors;

/// <summary>
/// Searches for releases across an indexer aggregator. Unlike <see cref="API.MangaConnectors.MangaConnector"/>,
/// an indexer connector does not scrape pages or know how to fetch chapter images — it only finds a
/// downloadable release (torrent/nzb) for a search query.
/// </summary>
public interface IIndexerConnector
{
    Task<IndexerRelease[]> Search(string query, CancellationToken cancellationToken);
}
