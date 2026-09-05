namespace API.IndexerConnectors;

public enum ReleaseProtocol
{
    Torrent,
    Usenet
}

/// <summary>One release returned by an <see cref="IIndexerConnector"/> search.</summary>
public sealed record IndexerRelease(
    string Title,
    string DownloadUrl,
    string? InfoUrl,
    ReleaseProtocol Protocol,
    string IndexerName,
    long Size,
    DateTime PublishDate,
    int? Seeders);
