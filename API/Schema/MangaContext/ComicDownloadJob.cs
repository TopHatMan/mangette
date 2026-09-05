using System.ComponentModel.DataAnnotations;
using API.IndexerConnectors;
using Microsoft.EntityFrameworkCore;

namespace API.Schema.MangaContext;

public enum ComicDownloadJobStatus
{
    Queued,
    Downloading,
    Importing,
    Imported,
    Failed
}

public enum DownloadClientKind
{
    QBittorrent,
    Sabnzbd
}

/// <summary>
/// Tracks one release grabbed from an indexer for a Comic <see cref="Chapter"/> (issue), from the moment
/// it's handed to an external download client until it's imported into the library (or fails).
/// </summary>
[PrimaryKey("Key")]
public class ComicDownloadJob : Identifiable
{
    [StringLength(64)] public string ChapterId { get; internal set; }
    public Chapter Chapter { get; internal set; } = null!;
    [StringLength(128)] public string IndexerName { get; internal set; }
    [StringLength(512)] public string ReleaseTitle { get; internal set; }
    [StringLength(2048)] public string DownloadUrl { get; internal set; }
    public ReleaseProtocol Protocol { get; internal set; }
    public DownloadClientKind ClientName { get; internal set; }
    [StringLength(256)] public string? ExternalId { get; internal set; }
    public ComicDownloadJobStatus Status { get; internal set; }
    [StringLength(1024)] public string? OutputPath { get; internal set; }
    [StringLength(1024)] public string? ErrorMessage { get; internal set; }
    public DateTime CreatedAt { get; internal set; }
    public DateTime? LastCheckedAt { get; internal set; }

    public ComicDownloadJob(Chapter chapter, string indexerName, string releaseTitle, string downloadUrl,
        ReleaseProtocol protocol, DownloadClientKind clientName)
        : base(TokenGen.CreateToken(typeof(ComicDownloadJob), chapter.Key, releaseTitle, DateTime.UtcNow.Ticks.ToString()))
    {
        Chapter = chapter;
        ChapterId = chapter.Key;
        IndexerName = indexerName;
        ReleaseTitle = releaseTitle;
        DownloadUrl = downloadUrl;
        Protocol = protocol;
        ClientName = clientName;
        Status = ComicDownloadJobStatus.Queued;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// EF ONLY!!!
    /// </summary>
    public ComicDownloadJob(string key, string chapterId, string indexerName, string releaseTitle, string downloadUrl,
        ReleaseProtocol protocol, DownloadClientKind clientName, string? externalId, ComicDownloadJobStatus status,
        string? outputPath, string? errorMessage, DateTime createdAt, DateTime? lastCheckedAt)
        : base(key)
    {
        ChapterId = chapterId;
        IndexerName = indexerName;
        ReleaseTitle = releaseTitle;
        DownloadUrl = downloadUrl;
        Protocol = protocol;
        ClientName = clientName;
        ExternalId = externalId;
        Status = status;
        OutputPath = outputPath;
        ErrorMessage = errorMessage;
        CreatedAt = createdAt;
        LastCheckedAt = lastCheckedAt;
    }

    internal void MarkDownloading(string externalId)
    {
        ExternalId = externalId;
        Status = ComicDownloadJobStatus.Downloading;
        LastCheckedAt = DateTime.UtcNow;
    }

    internal void MarkFailed(string reason)
    {
        Status = ComicDownloadJobStatus.Failed;
        ErrorMessage = reason;
        LastCheckedAt = DateTime.UtcNow;
    }

    internal void MarkImported(string outputPath)
    {
        Status = ComicDownloadJobStatus.Imported;
        OutputPath = outputPath;
        LastCheckedAt = DateTime.UtcNow;
    }
}
