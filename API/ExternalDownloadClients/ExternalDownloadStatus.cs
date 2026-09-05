namespace API.ExternalDownloadClients;

/// <summary>Snapshot of an in-progress or finished download on an external client.</summary>
public sealed record ExternalDownloadStatus(bool Done, double Progress, string? OutputPath, bool Failed, string? ErrorMessage);
